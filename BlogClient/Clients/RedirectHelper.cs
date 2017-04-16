// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Net;
using System.Threading.Tasks;
using Windows.Web.Http;
using OpenLiveWriter.CoreServices;
using OpenLiveWriter.Extensibility.BlogClient;

namespace OpenLiveWriter.BlogClient.Clients
{
	public class RedirectHelper
	{
		public delegate Task<HttpRequestMessage> RequestFactory(string uri);

        public static async Task<HttpResponseMessage> GetResponse(string initialUri, RequestFactory requestFactory)
		{
			string uri = initialUri;
			for (int i = 0; i < 50; i++)
			{
                var request = await requestFactory(uri);
				//request.AllowAutoRedirect = false;
			    var response = await new HttpClient().SendRequestAsync(request);
                if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
				{
					string redirectedLocation = response.Headers["Location"];
					if (redirectedLocation == null || redirectedLocation == string.Empty)
						throw new BlogClientInvalidServerResponseException(initialUri, "An invalid redirect was returned (Location header was expected but not found)", string.Empty);
					uri = MergeUris(uri, redirectedLocation);
					//response.Close();
					continue;
				}
				return response;
			}
			throw new BlogClientInvalidServerResponseException(initialUri, "Allowed number of redirects (50) was exceeded", string.Empty);
		}

		private static string MergeUris(string uri, string newUri)
		{
			int i1 = uri.IndexOf('?');
			int i2 = newUri.IndexOf('?');
			if (i1 < 0 || i2 >= 0)
				return newUri;
			else
				return newUri + uri.Substring(i1);
		}

		public class SimpleRequest
		{
			private readonly string _method;
			private readonly HttpAsyncRequestFilter _filter;

			public SimpleRequest(string method, HttpAsyncRequestFilter filter)
			{
				_method = method;
				_filter = filter;
			}
			
			public async Task<HttpRequestMessage> Create(string uri)
			{
				var request = HttpRequestHelper.CreateHttpWebRequest(uri, false);
				request.Method = new HttpMethod(_method);
				if (_filter != null)
					await _filter(request);
				return request;
			}
		}
	}
}
