// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Windows.Web.Http;
using Windows.Web.Http.Headers;
using OpenLiveWriter.CoreServices;
using OpenLiveWriter.CoreServices.Diagnostics;
using OpenLiveWriter.HtmlParser.Parser.FormAgent;
using BlogWriter.OpenLiveWriter;
using HttpStatusCode = System.Net.HttpStatusCode;
using XmlDocument = Windows.Data.Xml.Dom.XmlDocument;

namespace OpenLiveWriter.BlogClient.Clients
{
    /// <summary>
    /// Helper class for making REST-ful XML HTTP requests.
    /// </summary>
    public class XmlRestRequestHelper
    {
        public XmlRestRequestHelper()
        {
        }

        /// <summary>
        /// Retrieve the specified URI, using the given filter, with the supplied parameters (if any).
        /// The parameters parameter should be an even number of strings, where each odd element is
        /// a param name and each following even element is the corresponding param value.  For example,
        /// to retrieve http://www.vox.com/atom?svc=post&id=100, you would say:
        /// 
        /// Get("http://www.vox.com/atom", "svc", "post", "id", "100");
        /// 
        /// If a param value is null or empty string, that param will not be included in the final URL
        /// (i.e. the corresponding param name will also be dropped).
        /// </summary>
        public virtual Task<XmlDocument> Get(HttpAsyncRequestFilter filter, XmlRequestResult result, params string[] parameters)
        {
            return SimpleRequest("GET", filter, result, parameters);
        }

        /// <summary>
        /// Performs an HTTP DELETE on the URL and contains no body, returns the body as an XmlDocument if there is one
        /// </summary>
        public virtual Task<XmlDocument> Delete(HttpAsyncRequestFilter filter, XmlRequestResult result)
        {
            return SimpleRequest("DELETE", filter, result, new string[] { });
        }

        public class XmlRequestResult
        {
            public Uri uri { get; set; }
            public HttpResponseHeaderCollection responseHeaders { get; set; }

            public XmlRequestResult()
            {
                uri = null;
                responseHeaders = null;
            }
        }

        private static async Task<XmlDocument> SimpleRequest(string method, HttpAsyncRequestFilter filter, XmlRequestResult result, params string[] parameters)
        {
            string absUri = UrlHelper.SafeToAbsoluteUri(result.uri);

            if (parameters.Length > 0)
            {
                FormData formData = new FormData(true, parameters);

                if (absUri.IndexOf('?') == -1)
                    absUri += "?" + formData.ToString();
                else
                    absUri += "&" + formData.ToString();
            }

            RedirectHelper.SimpleRequest simpleRequest = new RedirectHelper.SimpleRequest(method, filter);
            var response = await RedirectHelper.GetResponse(absUri, new RedirectHelper.RequestFactory(simpleRequest.Create));
            try
            {
                result.uri = response.RequestMessage.RequestUri;
                result.responseHeaders = response.Headers;
                return await ParseXmlResponse(response);
            }
            finally
            {
                if (response != null)
                    response.Dispose();
            }
        }

        /// <summary>
        /// Performs an HTTP PUT with the specified XML document as the request body.
        /// </summary>
        public Task<XmlDocument> Put(string etag, HttpAsyncRequestFilter filter, string contentType, XmlDocument doc, string encoding, bool ignoreResponse, XmlRequestResult result)
        {
            return Send("PUT", etag, filter, contentType, doc, encoding, null, ignoreResponse, result);
        }

        /// <summary>
        /// Performs an HTTP POST with the specified XML document as the request body.
        /// </summary>
        public Task<XmlDocument> Post(HttpAsyncRequestFilter filter, string contentType, XmlDocument doc, string encoding, XmlRequestResult result)
        {
            return Send("POST", null, filter, contentType, doc, encoding, null, false, result);
        }


        /// <summary>
        /// Performs a multipart MIME HTTP POST with the specified XML document as the request body and filename as the payload.
        /// </summary>
        public Task<XmlDocument> Post(HttpAsyncRequestFilter filter, string contentType, XmlDocument doc, string encoding, string filename, XmlRequestResult result)
        {
            return Send("POST", null, filter, contentType, doc, encoding, filename, false, result);
        }

        protected virtual XmlDocument MultipartSend(string method, string etag, HttpAsyncRequestFilter filter, string contentType, XmlDocument doc, string encoding, string filename, bool ignoreResponse, XmlRequestResult result)
        {
            throw new NotImplementedException();
        }

        protected virtual async Task<XmlDocument> Send(string method, string etag, HttpAsyncRequestFilter filter, string contentType, XmlDocument doc, string encoding, string filename, bool ignoreResponse, XmlRequestResult result)
        {
            if (!String.IsNullOrEmpty(filename))
            {
                return MultipartSend(method, etag, filter, contentType, doc, encoding, filename, ignoreResponse,
                                     result);
            }

            string absUri = UrlHelper.SafeToAbsoluteUri(result.uri);
            Debug.WriteLine("XML Request to " + absUri + ":\r\n" + doc.GetXml());

            SendFactory sf = new SendFactory(etag, method, filter, contentType, doc, encoding);
            var response = await RedirectHelper.GetResponse(absUri, new RedirectHelper.RequestFactory(sf.Create));
            try
            {
                result.responseHeaders = response.Headers;
                result.uri = response.RequestMessage.RequestUri;
                if (ignoreResponse || response.StatusCode == Windows.Web.Http.HttpStatusCode.NoContent)
                {
                    return null;
                }
                else
                {
                    XmlDocument xmlDocResponse = await ParseXmlResponse(response);
                    return xmlDocResponse;
                }
            }
            finally
            {
                if (response != null)
                    response.Dispose();
            }
        }

        private struct SendFactory
        {
            private readonly string _etag;
            private readonly string _method;
            private readonly HttpAsyncRequestFilter _filter;
            private readonly string _contentType;
            private readonly XmlDocument _doc;
            private readonly Encoding _encodingToUse;

            public SendFactory(string etag, string method, HttpAsyncRequestFilter filter, string contentType, XmlDocument doc, string encoding)
            {
                _etag = etag;
                _method = method;
                _filter = filter;
                _contentType = contentType;
                _doc = doc;

                //select the encoding
                _encodingToUse = new UTF8Encoding(false, false);
                try
                {
                    _encodingToUse = Encoding.GetEncoding(encoding);
                }
                catch (Exception ex)
                {
                    //Debug.Fail("Error while getting transport encoding: " + ex.ToString());
                }
            }

            public async Task<HttpRequestMessage> Create(string uri)
            {
                var request = HttpRequestHelper.CreateHttpWebRequest(uri, true);
                request.Method = new HttpMethod(_method);
                //			    request.KeepAlive = true;
                //			    request.Pipelined = true;
                if (_etag != null && _etag != "")
                    request.Headers["If-match"] = _etag;

                request.Content = new HttpStringContent(_doc.DocumentElement.GetXml());

                if (_contentType != null)
                    if (request.Content.Headers.ContentType != null)
                        request.Content.Headers.ContentType.MediaType = _contentType;
                if (_filter != null)
                    await _filter(request);

                    Debug.WriteLine(
                        string.Format(CultureInfo.InvariantCulture, "XML REST request:\r\n{0} {1}\r\n{2}\r\n{3}",
                            _method, uri, (_etag != null && _etag != "") ? "If-match: " + _etag : "(no etag)", _doc.GetXml()));

                

                //using (Stream s = await request.GetRequestStreamAsync())
                //{
                //    var writer = System.Xml.XmlWriter.Create(s, new XmlWriterSettings() {Encoding = _encodingToUse, Indent = true, IndentChars = " "});
                //    await writer.WriteRawAsync(_doc.DocumentElement.GetXml());
                //    await writer.FlushAsync();
                //}

                return request;
            }
        }

        public class MultipartMimeSendFactory
        {
            private readonly string _filename;
            private readonly XmlDocument _xmlDoc;
            private readonly Encoding _encoding;
            private readonly HttpAsyncRequestFilter _filter;
            private readonly MultipartMimeRequestHelper _multipartMimeRequestHelper;
            public MultipartMimeSendFactory(HttpAsyncRequestFilter filter, XmlDocument xmlRequest, string filename, string encoding, MultipartMimeRequestHelper multipartMimeRequestHelper)
            {
                if (xmlRequest == null)
                    throw new ArgumentNullException();

                // Add boundary to params                
                _filename = filename;
                _xmlDoc = xmlRequest;
                _filter = filter;
                _multipartMimeRequestHelper = multipartMimeRequestHelper;

                //select the encoding
                _encoding = new UTF8Encoding(false, false);
                try
                {
                    _encoding = Encoding.GetEncoding(encoding);
                }
                catch (Exception ex)
                {
                    //Debug.Fail("Error while getting transport encoding: " + ex.ToString());
                }
            }

            public async Task<HttpRequestMessage> Create(string uri)
            {                                
                var req = HttpRequestHelper.CreateHttpWebRequest(uri, true);
                _multipartMimeRequestHelper.Init(req);

                if (_filter != null)
                    await _filter(req);

                _multipartMimeRequestHelper.Open();
                _multipartMimeRequestHelper.AddXmlRequest(_xmlDoc);
                _multipartMimeRequestHelper.AddFile(_filename);
                _multipartMimeRequestHelper.Close();
                
                using (var stream = new FileStream(_filename, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    return await _multipartMimeRequestHelper.SendRequest(stream);
                }
            }
        }

        protected static async Task<XmlDocument> ParseXmlResponse(HttpResponseMessage response)
        {
            try
            {
                if (ApplicationDiagnostics.VerboseLogging)
                    Debug.WriteLine("XML REST response:\r\n" + UrlHelper.SafeToAbsoluteUri(response.RequestMessage.RequestUri) + "\r\n");
            }
            catch (Exception e)
            {
                Debug.Fail("Failed to log REST response: " + e.ToString());
            }

            try
            {
                XmlDocument xmlDoc = new XmlDocument();
                var xml = await response.Content.ReadAsStringAsync();
                if (String.IsNullOrWhiteSpace(xml)) return null;
                xmlDoc.LoadXml(xml);
                XmlHelper.ApplyBaseUri(xmlDoc, response.Headers.Location);

                return xmlDoc;
            }
            catch (Exception e)
            {
                Debug.Fail("Malformed XML document: " + e.ToString());
                return null;
            }
        }
    }
}
