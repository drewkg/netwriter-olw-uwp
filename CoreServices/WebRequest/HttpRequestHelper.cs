// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using OpenLiveWriter.CoreServices.Diagnostics;
using HttpClient = Windows.Web.Http.HttpClient;
using HttpRequestMessage = Windows.Web.Http.HttpRequestMessage;
using HttpResponseMessage = Windows.Web.Http.HttpResponseMessage;

namespace OpenLiveWriter.CoreServices
{
	/// <summary>
	/// Delegate for augmenting and HTTP request.
	/// </summary>
	public delegate Task HttpAsyncRequestFilter(HttpRequestMessage request);

	
	/// <summary>
	/// Utility class for doing HTTP requests -- uses the Feeds Proxy settings (if any) for requests
	/// </summary>
	public class HttpRequestHelper
	{
		static HttpRequestHelper()
		{
			//// This is necessary to avoid problems connecting to Blogger server from behind a proxy.
			//ServicePointManager.Expect100Continue = false;

   //         try
   //         {
   //             // Add WSSE support everywhere.
   //             AuthenticationManager.Register(new WsseAuthenticationModule());
   //         }
   //         catch (InvalidOperationException)
   //         {
   //             // See http://blogs.msdn.com/shawnfa/archive/2005/05/16/417975.aspx
   //             Debug.WriteLine("Warning: WSSE support disabled");
   //         }

		 //   if (ApplicationDiagnostics.AllowUnsafeCertificates)
			//{
   //             ServicePointManager.ServerCertificateValidationCallback = new RemoteCertificateValidationCallback(CheckValidationResult);
				
			//}
		}

	    //private static bool CheckValidationResult(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
	    //{
     //       if (sslPolicyErrors != SslPolicyErrors.None)
     //       {
     //           Debug.WriteLine("SSL Policy error " + sslPolicyErrors);
     //       }

            
     //       return true;
	    //}



		public static void TrackResponseClosing(ref HttpWebRequest req)
		{
			//CloseTrackingHttpWebRequest.Wrap(ref req);
		}

        public static Task<HttpResponseMessage> SendRequest(string requestUri)
		{
			return SendRequest( requestUri, null ) ;
		}

		public async static Task<HttpResponseMessage> SendRequest( string requestUri, HttpAsyncRequestFilter filter )
        {
            var httpClient = new HttpClient();
            

		    var request = CreateHttpWebRequest(requestUri, true, null, null);
            if (filter != null)
				await filter(request);

			// get the response
			try
			{
			    var response = await httpClient.SendRequestAsync(request);

				//hack: For some reason, disabling auto-redirects also disables throwing WebExceptions for 300 status codes,
				//so if we detect a non-2xx error code here, throw a web exception.
			    response.EnsureSuccessStatusCode();
				return response ;
			}
			catch(WebException e)
			{
				if(e.Status == WebExceptionStatus.Timeout)
				{
					//throw a typed exception that lets callers know that the response timed out after the request was sent
					throw new WebResponseTimeoutException(e);
				}
				else
					throw;
			}
		}

	    public static void ApplyLanguage(HttpRequestMessage request)
		{
			string acceptLang = CultureInfo.CurrentUICulture.Name.Split('/')[0];
			if (acceptLang.ToUpperInvariant() == "SR-SP-LATN")
				acceptLang = "sr-Latn-CS";
			if (acceptLang != "en-US")
				acceptLang += ", en-US";
			acceptLang += ", en, *";
			request.Headers["Accept-Language"] = acceptLang;
		}

		public async static Task<HttpResponseMessage> SafeSendRequest( string requestUri, HttpAsyncRequestFilter filter )
		{
			try
			{
				return await SendRequest( requestUri, filter ) ;
			}
			catch(WebException we)
			{
				//if (ApplicationDiagnostics.TestMode)
				//	LogException(we);
				return null;
			}
		}

        public static void ApplyProxyOverride(HttpRequestMessage request)
        {
           
        }

        /// <summary>
        /// Returns the default proxy for an HTTP request. 
        /// 
        /// Consider using ApplyProxyOverride instead.
        /// </summary>
        /// <returns></returns>
        public static IWebProxy GetProxyOverride()
        {
            IWebProxy proxy = null;
            //if (WebProxySettings.ProxyEnabled)
            //{
            //    string proxyServerUrl = WebProxySettings.Hostname;
            //    if (proxyServerUrl.IndexOf("://", StringComparison.OrdinalIgnoreCase) == -1)
            //        proxyServerUrl = "http://" + proxyServerUrl;
            //    if (WebProxySettings.Port > 0)
            //        proxyServerUrl += ":" + WebProxySettings.Port;

            //    ICredentials proxyCredentials = CreateHttpCredentials(WebProxySettings.Username, WebProxySettings.Password, proxyServerUrl);
            //    proxy = new System.Net.weWebProxy(proxyServerUrl, false, new string[0], proxyCredentials);
            //}
            return proxy;
        }

        public static ICredentials CreateHttpCredentials(string username, string password, string url)
		{
			return CreateHttpCredentials(username, password, url, false) ;
		}

		/// <summary>
		/// Creates a set of credentials for the specified user/pass, or returns the default credentials if user/pass is null.
		/// </summary>
		/// <param name="username"></param>
		/// <param name="password"></param>
		/// <param name="url"></param>
		/// <returns></returns>
		public static ICredentials CreateHttpCredentials(string username, string password, string url, bool digestOnly)
		{
			ICredentials credentials = CredentialCache.DefaultCredentials;
			if(username != null || password != null)
			{
				CredentialCache credentialCache = new CredentialCache();
				string userDomain = String.Empty;

				if(username != null)
				{
					//try to parse the username string into a domain\userId
					int domainIndex = username.IndexOf(@"\", StringComparison.OrdinalIgnoreCase);
					if(domainIndex != -1)
					{
						userDomain = username.Substring(0, domainIndex);
						username = username.Substring(domainIndex+1);
					}
				}

				credentialCache.Add(new Uri(url), "Digest", new NetworkCredential(username, password, userDomain));

				if ( !digestOnly )
				{
					credentialCache.Add(new Uri(url), "Basic", new NetworkCredential(username, password, userDomain));
					credentialCache.Add(new Uri(url), "NTLM", new NetworkCredential(username, password, userDomain));
					credentialCache.Add(new Uri(url), "Negotiate", new NetworkCredential(username, password, userDomain));
					credentialCache.Add(new Uri(url), "Kerberos", new NetworkCredential(username, password, userDomain));
				}
				credentials = credentialCache;
			}
			return credentials;
		}

		public static HttpRequestMessage CreateHttpWebRequest(string requestUri, bool allowAutoRedirect)
		{
		    return CreateHttpWebRequest(requestUri, allowAutoRedirect, null, null);
		}

		public static HttpRequestMessage CreateHttpWebRequest(string requestUri, bool allowAutoRedirect, int? connectTimeoutMs, int? readWriteTimeoutMs)
		{
		    var requestMessage = new HttpRequestMessage();
            requestMessage.RequestUri = new Uri(requestUri);
            requestMessage.Headers.Accept.ParseAdd("*/*");
			ApplyLanguage(requestMessage);

            //   request.Timeout = timeout;
            //   request.ReadWriteTimeout = timeout*5;

            //if(connectTimeoutMs != null)
            //	request.Timeout = connectTimeoutMs.Value ;
            //if(readWriteTimeoutMs != null)
            //	request.ReadWriteTimeout = readWriteTimeoutMs.Value ;

            //request.AllowAutoRedirect = allowAutoRedirect;
            requestMessage.Headers["User-Agent"] = "Mozilla/4.0 (compatible; MSIE 9.11; Windows NT 6.2; NetWriter 1.0)";

            ApplyProxyOverride(requestMessage);

            //For robustness, we turn off keep alive and piplining by default.
            //If the caller wants to override, the filter parameter can be used to adjust these settings.
            //Warning: NTLM authentication requires keep-alive, so without adjusting this, NTLM-secured requests will always fail.
   //         request.KeepAlive = false;
			//request.Pipelined = false;
   //         request.CachePolicy = new RequestCachePolicy(RequestCacheLevel.Reload);
			return requestMessage;
		}
		
		public static string DumpResponse(HttpWebResponse resp)
		{
			StringBuilder sb = new StringBuilder();
			using (StringWriter sw = new StringWriter(sb, CultureInfo.InvariantCulture))
			{
				sw.WriteLine(String.Format(CultureInfo.InvariantCulture, "{0}/{1} {2} {3}", "HTTP", "", (int)resp.StatusCode, resp.StatusDescription));
				foreach(string key in resp.Headers.AllKeys)
				{
					sw.WriteLine(String.Format(CultureInfo.InvariantCulture, "{0}: {1}", key, resp.Headers[key]));
				}
				sw.WriteLine("");
				sw.WriteLine(DecodeBody(resp));
			}
			return sb.ToString();
		}
		
		public static string DumpRequestHeader(HttpWebRequest req)
		{
			StringBuilder sb = new StringBuilder();
			using (StringWriter sw = new StringWriter(sb, CultureInfo.InvariantCulture))
			{
				sw.WriteLine(String.Format(CultureInfo.InvariantCulture, "{0} {1}", req.Method, UrlHelper.SafeToAbsoluteUri(req.RequestUri)));
				foreach(string key in req.Headers.AllKeys)
				{
					sw.WriteLine(String.Format(CultureInfo.InvariantCulture, "{0}: {1}", key, req.Headers[key]));
				}
			}
			return sb.ToString();
		}


		public static DateTime GetExpiresHeader(HttpWebResponse response)
		{
			string expires = response.Headers["Expires"];
			if ( expires != null && expires != String.Empty && expires.Trim() != "-1")
			{
				try
				{
					DateTime expiresDate = DateTime.Parse(expires, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal) ;
					return expiresDate ;
				}
				catch(Exception ex)
				{
					// look for ANSI c's asctime() format as a last gasp
					try
					{ 
						string asctimeFormat =  "ddd' 'MMM' 'd' 'HH':'mm':'ss' 'yyyy" ;
						DateTime expiresDate = DateTime.ParseExact(expires, asctimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces) ;
						return expiresDate ;
					}
					catch
					{
					}

					//Debug.Fail("Exception parsing HTTP date - " + expires + ": " + ex.ToString());	
					return DateTime.MinValue ;
				}
			}
			else
			{
				return DateTime.MinValue ;
			}
		}

		public static string GetETagHeader(HttpWebResponse response)
		{
			return GetStringHeader(response, "ETag");
		}

		public static string GetStringHeader(HttpWebResponse response, string headerName)
		{
			string headerValue = response.Headers[headerName] ;
			if ( headerValue != null )
				return headerValue ;
			else
				return String.Empty ;
		}

		public static void LogException(WebException ex)
		{
			//Debug.WriteLine("== BEGIN WebException =====================");
			//Debug.WriteLine("Status: " + ex.Status);
			//Debug.WriteLine(ex.ToString());
			HttpWebResponse response = ex.Response as HttpWebResponse;
			//if (response != null)
			//	Debug.WriteLine(DumpResponse(response));
			//Debug.WriteLine("== END WebException =======================");
		}

		public static string GetFriendlyErrorMessage(WebException we)
		{
			if (we.Response != null && we.Response is HttpWebResponse)
			{
				HttpWebResponse response = (HttpWebResponse) we.Response;
				string bodyText = GetBodyText(response);
				int statusCode = (int) response.StatusCode;
				string statusDesc = response.StatusDescription;

				return String.Format(CultureInfo.CurrentCulture,
					"{0} {1}\r\n\r\n{2}",
					statusCode, statusDesc,
					bodyText);
			}
			else
			{
				return we.Message;
			}
		}

		private static string GetBodyText(HttpWebResponse resp)
		{
			if (resp.ContentType != null && resp.ContentType.Length > 0)
			{
				IDictionary contentTypeData = MimeHelper.ParseContentType(resp.ContentType, true);
				string mainType = (string) contentTypeData[""];
				switch (mainType)
				{
					case "text/plain":
					{
						return DecodeBody(resp);
					}
					case "text/html":
					{
						return DecodeBody(resp);
                    }
				}
			}
			return "";
		}

		private static string DecodeBody(HttpWebResponse response)
		{
			Stream s = response.GetResponseStream();
			StreamReader sr = new StreamReader(s);
			return sr.ReadToEnd();
		}
	}

	public class HttpRequestCredentialsFilter
	{
		public static HttpAsyncRequestFilter Create(string username, string password, string url, bool digestOnly)
		{
			return new HttpAsyncRequestFilter(new HttpRequestCredentialsFilter(username, password, url, digestOnly).Filter);
		}

		private HttpRequestCredentialsFilter(string username, string password, string url, bool digestOnly)
		{
			_username = username ;
			_password = password ;
			_url = url ;
		    _digestOnly = digestOnly;
		}

		private Task Filter(HttpRequestMessage request)
		{
            //TODO: Reinstate
		    //request.Properties = HttpRequestHelper.CreateHttpCredentials(_username, _password, _url, _digestOnly);
		    return Task.FromResult(true);
		}

		private string _username ;
		private string _password ;
		private string _url ;
		private bool _digestOnly ;
	}


	/// <summary>
	/// Allow chaining together of http request filters
	/// </summary>
	public class CompoundHttpRequestFilter
	{
		public static HttpAsyncRequestFilter Create(HttpAsyncRequestFilter[] filters)
		{
			return new HttpAsyncRequestFilter(new CompoundHttpRequestFilter(filters).Filter);
		}

		private CompoundHttpRequestFilter(HttpAsyncRequestFilter[] filters)
		{
			_filters = filters ;
		}

		private async Task Filter(HttpRequestMessage request)
		{
			foreach (HttpAsyncRequestFilter filter in _filters)
				await filter(request) ;
		}

		
		private HttpAsyncRequestFilter[] _filters ;
	}


	/// <summary>
	/// Typed-exception that occurs when an HTTP request times out after the request has been sent, but
	/// before the response is received.
	/// </summary>
	public class WebResponseTimeoutException : WebException
	{
		public WebResponseTimeoutException(WebException innerException) : base(innerException.Message, innerException, innerException.Status, innerException.Response)
		{
			
		}
	}
}
