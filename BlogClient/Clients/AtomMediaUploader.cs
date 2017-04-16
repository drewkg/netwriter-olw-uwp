// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

#define APIHACK
using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Windows.Web.Http;
using Windows.Web.Http.Headers;
using BlogWriter.OpenLiveWriter;
using OpenLiveWriter.CoreServices;
using OpenLiveWriter.Extensibility.BlogClient;
using OpenLiveWriter.HtmlParser.Parser;
using HttpStatusCode = System.Net.HttpStatusCode;
using XmlDocument = Windows.Data.Xml.Dom.XmlDocument;
using XmlElement = Windows.Data.Xml.Dom.XmlElement;

namespace OpenLiveWriter.BlogClient.Clients
{
    public class AtomMediaUploader
    {
        protected const string EDIT_MEDIA_LINK = "EditMediaLink";
        protected const string EDIT_MEDIA_ENTRY_LINK = "EditMediaLinkEntryLink";
        protected const string MEDIA_ETAG = "MediaEtag";

        protected XmlNamespaceManager _nsMgr
        {
            get;
            private set;
        }
        protected HttpAsyncRequestFilter _requestFilter
        {
            get;
            private set;
        }
        protected readonly string _collectionUri;
        protected IBlogClientOptions _options
        {
            get;
            private set;
        }
        protected XmlRestRequestHelper xmlRestRequestHelper
        {
            get;
            private set;
        }

        public AtomMediaUploader(XmlNamespaceManager nsMgr, HttpAsyncRequestFilter requestFilter, string collectionUri, IBlogClientOptions options)
            : this(nsMgr, requestFilter, collectionUri, options, new XmlRestRequestHelper())
        {
        }

        public AtomMediaUploader(XmlNamespaceManager nsMgr, HttpAsyncRequestFilter requestFilter, string collectionUri, IBlogClientOptions options, XmlRestRequestHelper xmlRestRequestHelper)
        {
            this._nsMgr = nsMgr;
            this._requestFilter = requestFilter;
            this._collectionUri = collectionUri;
            this._options = options;
            this.xmlRestRequestHelper = xmlRestRequestHelper;
        }

        public async Task<string> DoBeforePublishUploadWork(IFileUploadContext uploadContext)
        {
            string path = uploadContext.GetContentsLocalFilePath();

            PostNewImageResult result = new PostNewImageResult();

            result.editUri = uploadContext.Settings.GetString(EDIT_MEDIA_LINK, null);
            result.editEntryUri = uploadContext.Settings.GetString(EDIT_MEDIA_ENTRY_LINK, null);
            result.etag = uploadContext.Settings.GetString(MEDIA_ETAG, null);
            if (string.IsNullOrEmpty(result.editUri))
            {
                await PostNewImage(path, false, result);
            }
            else
            {
                try
                {
                    await UpdateImage(result.editUri, path, result.editEntryUri, result.etag, true, result);
                }
                catch (Exception e)
                {
                    //Debug.Fail(e.ToString());

                    bool success = false;
                    result.srcUrl = null; // compiler complains without this line
                    try
                    {
                        // couldn't update existing image? try posting a new one
                        await PostNewImage(path, false, result);
                        success = true;

                        if (e is WebException)
                        {
                            //Debug.WriteLine("Image PUT failed, but POST succeeded. PUT exception follows.");
                            HttpRequestHelper.LogException((WebException)e);
                        }
                    }
                    catch
                    {
                    }
                    if (!success)
                        throw;  // rethrow the exception from the update, not the post
                }
            }
            uploadContext.Settings.SetString(EDIT_MEDIA_LINK, result.editUri);
            uploadContext.Settings.SetString(EDIT_MEDIA_ENTRY_LINK, result.editEntryUri);
            uploadContext.Settings.SetString(MEDIA_ETAG, null);

            await UpdateETag(uploadContext, result.editUri);
            return result.srcUrl;
        }

        protected virtual async Task UpdateETag(IFileUploadContext uploadContext, string editUri)
        {
            try
            {
                string newEtag = await AtomClient.GetEtag(editUri, _requestFilter);
                uploadContext.Settings.SetString(MEDIA_ETAG, newEtag);
            }
            catch (Exception)
            {

            }
        }

        public class PostNewImageResult
        {
            public string editUri { get; set; }
            public string srcUrl { get; set; }
            public string editMediaUri { get; set; }
            public string editEntryUri { get; set; }
            public string selfPage { get; set; }
            public string etag { get; set; }
        }

        public virtual async Task PostNewImage(string path, bool allowWriteStreamBuffering, PostNewImageResult result)
        {
            string mediaCollectionUri = _collectionUri;
            if (mediaCollectionUri == null || mediaCollectionUri == "")
                throw new BlogClientFileUploadNotSupportedException();

            HttpResponseMessage response = null;
            try
            {
                response = await RedirectHelper.GetResponse(mediaCollectionUri,
                new RedirectHelper.RequestFactory(new ImageUploadHelper(this, path, "POST", null, allowWriteStreamBuffering).Create));

                string selfPage = string.Empty;
                XmlDocument xmlDoc = await GetCreatedEntity(response, result);
                result.selfPage = selfPage;
                ParseResponse(xmlDoc, result);
            }
            catch (WebException we)
            {
                // The error may have been due to the server requiring stream buffering (WinLive 114314, 252175)
                // Try again with stream buffering.                    
                if (we.Status == WebExceptionStatus.ProtocolError && !allowWriteStreamBuffering)
                {
                    await PostNewImage(path, true, result);

                }
                else
                {
                    throw;
                }
            }
            finally
            {
                //if (response != null)
                    //response.Close();
            }
        }

        private async Task<XmlDocument> GetCreatedEntity(HttpResponseMessage postResponse, PostNewImageResult result)
        {
            result.editUri = postResponse.Headers["Location"];
            string contentLocation = postResponse.Headers["Content-Location"];
            if (string.IsNullOrEmpty(result.editUri) || result.editUri != contentLocation)
            {
                XmlRestRequestHelper.XmlRequestResult xmlResult = new XmlRestRequestHelper.XmlRequestResult();

                xmlResult.uri = postResponse.RequestMessage.RequestUri;
                if (!string.IsNullOrEmpty(result.editUri))
                    xmlResult.uri = new Uri(result.editUri);
                XmlDocument doc = await xmlRestRequestHelper.Get(_requestFilter, xmlResult);
                result.etag = xmlResult.responseHeaders["ETag"];
                return doc;
            }
            else
            {
                result.etag = postResponse.Headers["ETag"];
                XmlDocument xmlDoc = new XmlDocument();

                var xml = await postResponse.Content.ReadAsStringAsync();
                xmlDoc.LoadXml(xml);
                    
                XmlHelper.ApplyBaseUri(xmlDoc, postResponse.RequestMessage.RequestUri);
                return xmlDoc;
            }
        }

        protected virtual async Task UpdateImage(string editMediaUri, string path, string editEntryUri, string etag, bool getEditInfo, PostNewImageResult result)
        { 
            await UpdateImage(false, editMediaUri, path, editEntryUri, etag, getEditInfo, result);
        }

        protected virtual async Task UpdateImage(bool allowWriteStreamBuffering, string editMediaUri, string path, string editEntryUri, string etag, bool getEditInfo, PostNewImageResult result)
        {
            HttpResponseMessage response = null;
            try
            {
                response = await RedirectHelper.GetResponse(editMediaUri,
                    new RedirectHelper.RequestFactory(new ImageUploadHelper(this, path, "PUT", etag, allowWriteStreamBuffering).Create));
            }
            catch (WebException we)
            {
                bool recovered = false;

                if (we.Status == WebExceptionStatus.ProtocolError && we.Response != null)
                {
                    HttpWebResponse errResponse = we.Response as HttpWebResponse;
                    if (errResponse != null && errResponse.StatusCode == HttpStatusCode.PreconditionFailed)
                    {
                        string newEtag = await AtomClient.GetEtag(editMediaUri, _requestFilter);
                        if (newEtag != null && newEtag.Length > 0 && newEtag != etag)
                        {
                            if (!AtomClient.ConfirmOverwrite())
                                throw new BlogClientOperationCancelledException();

                            try
                            {
                                response = await RedirectHelper.GetResponse(editMediaUri,
                                new RedirectHelper.RequestFactory(new ImageUploadHelper(this, path, "PUT", newEtag, allowWriteStreamBuffering).Create));
                            }
                            finally
                            {
                                if (response != null)
                                    response.Dispose();
                            }

                            recovered = true;
                        }
                    }
                    else if (!allowWriteStreamBuffering)
                    {
                        // The error may have been due to the server requiring stream buffering (WinLive 114314, 252175)
                        // Try again with stream buffering.                                            
                        await UpdateImage(true, editMediaUri, path, editEntryUri, etag, getEditInfo, result);
                        recovered = true;
                    }
                }
                if (!recovered)
                    throw;
            }

            // Check to see if we are going to get the src url and the etag, in most cases we will want to get this 
            // information, but in the case of a photo album, since we never edit the image or link directly to them
            // we don't need the information and it can saves an http request.
            if (getEditInfo)
            {
                string selfPage;

                XmlRestRequestHelper.XmlRequestResult xmlResult = new XmlRestRequestHelper.XmlRequestResult();
                xmlResult.uri = new Uri(editEntryUri);
                XmlDocument mediaLinkEntry = await xmlRestRequestHelper.Get(_requestFilter, xmlResult);
                ParseResponse(mediaLinkEntry, result);
            }
            else
            {
                //thumbnailSmall = null;
                //thumbnailLarge = null;
                //srcUrl = null;
            }
        }

        protected virtual void ParseResponse(XmlDocument xmlDoc, PostNewImageResult result)
        {
            XmlElement contentEl = xmlDoc.SelectSingleNodeNS("/atom:entry/atom:content", _nsMgr.ToNSMethodFormat()) as XmlElement;
            result.srcUrl = XmlHelper.GetUrl(contentEl, "@src", null);
            result.editUri = AtomEntry.GetLink(xmlDoc.SelectSingleNodeNS("/atom:entry", _nsMgr.ToNSMethodFormat()) as XmlElement, _nsMgr, "edit-media",
                              null, null, null);
            result.editEntryUri = AtomEntry.GetLink(xmlDoc.SelectSingleNodeNS("/atom:entry", _nsMgr.ToNSMethodFormat()) as XmlElement, _nsMgr, "edit",
                                   null, null, null);
            result.selfPage = AtomEntry.GetLink(xmlDoc.SelectSingleNodeNS("/atom:entry", _nsMgr.ToNSMethodFormat()) as XmlElement, _nsMgr, "alternate",
                       null, null, null);
        }

        protected class ImageUploadHelper
        {
            private readonly AtomMediaUploader _parent;
            private readonly string _filename;
            private readonly string _method;
            private readonly string _etag;
            private readonly bool _allowWriteStreamBuffering;

            public ImageUploadHelper(AtomMediaUploader parent, string filename, string method, string etag, bool allowWriteStreamBuffering)
            {
                _parent = parent;
                _filename = filename;
                _method = method;
                _etag = etag;
                _allowWriteStreamBuffering = allowWriteStreamBuffering;
            }

            public async Task<HttpRequestMessage> Create(string uri)
            {
                // TODO: ETag support required??
                // TODO: choose rational timeout values
                var request = HttpRequestHelper.CreateHttpWebRequest(uri, false);


                //request.AllowWriteStreamBuffering = _allowWriteStreamBuffering;

                if (_etag != null && _etag.Length != 0)
                    request.Headers["If-match"] = _etag;

                await _parent._requestFilter(request);

                using (Stream inS = new FileStream(_filename, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var streamContent = new HttpStreamContent(inS.AsInputStream());
                    streamContent.Headers.ContentType = HttpMediaTypeHeaderValue.Parse(MimeHelper.GetContentType(Path.GetExtension(_filename)));
                    if (_parent._options != null && _parent._options.SupportsSlug)
                        request.Headers["Slug"] = Path.GetFileNameWithoutExtension(_filename);

                    request.Method = new HttpMethod(_method);
                    
                }

                return request;
            }
        }

    }

    public class MultipartMimeRequestHelper
    {
        private string _boundary;
        private HttpRequestMessage _request;
        Stream _requestStream;
        protected MemoryStream _requestBodyTop = new MemoryStream();
        protected MemoryStream _requestBodyBottom = new MemoryStream();

        public virtual void Init(HttpRequestMessage request)
        {
            _boundary = String.Format(CultureInfo.InvariantCulture, "============{0}==", Guid.NewGuid().ToString().Replace("-", ""));
            _request = request;
            _request.Method = new HttpMethod("POST");
            //_request.Content = String.Format(CultureInfo.InvariantCulture,
            //                        @"multipart/related; boundary=""{0}""; type = ""application/atom+xml""",
            //                        _boundary);
        }

        public virtual void Open()
        {
            AddBoundary(true, _requestBodyTop);
        }

        public virtual void Close()
        {
            AddBoundary(false, _requestBodyBottom);
            Write("--" + Environment.NewLine, _requestBodyBottom);
        }

        public virtual void AddBoundary(bool newLine, MemoryStream stream)
        {
            Write("--" + _boundary + (newLine ? Environment.NewLine : ""), stream);
        }

        public virtual void AddXmlRequest(XmlDocument xmlDocument)
        {
            throw new NotImplementedException();
        }

        public virtual void AddFile(string filePath)
        {
            throw new NotImplementedException();
        }

        protected UTF8Encoding _utf8NoBOMEncoding = new UTF8Encoding(false);
        protected virtual void Write(String s, MemoryStream stream)
        {
            byte[] newText = _utf8NoBOMEncoding.GetBytes(s);
            stream.Write(newText, 0, newText.Length);
        }

        public virtual async Task<HttpRequestMessage> SendRequest(Stream stream)
        {
            var content = new HttpMultipartContent();
            content.Headers.ContentType = HttpMediaTypeHeaderValue.Parse(String.Format(CultureInfo.InvariantCulture,
                                    @"multipart/related; boundary=""{0}""; type = ""application/atom+xml""",
                                    _boundary));
            content.Add(new HttpStreamContent(stream.AsInputStream()));
            
            _request.Content = content;

            return _request;

            // _request.ContentLength = _requestBodyTop.Length + stream.Length + _requestBodyBottom.Length;
            //_request.AllowWriteStreamBuffering = false;
            //_requestStream = await _request.GetRequestStreamAsync();
            //_requestStream.Write(_requestBodyTop.ToArray(), 0, (int)_requestBodyTop.Length);
            //await StreamHelper.TransferAsync(stream, _requestStream, 8192, true);
            //_requestStream.Write(_requestBodyBottom.ToArray(), 0, (int)_requestBodyBottom.Length);
            //await _requestStream.FlushAsync();
            //return _request;
        }
    }
}
