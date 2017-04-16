// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

#define APIHACK
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using OpenLiveWriter.CoreServices;
using OpenLiveWriter.CoreServices.Diagnostics;
using OpenLiveWriter.Extensibility.BlogClient;
using OpenLiveWriter.BlogClient.Providers;
using OpenLiveWriter.Localization;
using Windows.Data.Xml.Dom;
using Windows.Web.Http;
using Windows.Web.Http.Headers;
using BlogWriter.OpenLiveWriter;
using HttpStatusCode = System.Net.HttpStatusCode;
using XmlDocument = Windows.Data.Xml.Dom.XmlDocument;
using XmlElement = Windows.Data.Xml.Dom.XmlElement;
using XmlNodeList = Windows.Data.Xml.Dom.XmlNodeList;

namespace OpenLiveWriter.BlogClient.Clients
{
    public abstract class AtomClient : BlogClientBase, IBlogClient
    {
        public const string ENTRY_CONTENT_TYPE = "application/atom+xml;type=entry";
        public const string SKYDRIVE_ENTRY_CONTENT_TYPE = ENTRY_CONTENT_TYPE;//"application/atom+xml";

        private const string XHTML_NS = "http://www.w3.org/1999/xhtml";
        private const string FEATURES_NS = "http://purl.org/atompub/features/1.0";
        private const string MEDIA_NS = "http://search.yahoo.com/mrss/";
        private const string LIVE_NS = "http://api.live.com/schemas";

        internal static readonly Namespace xhtmlNS = new Namespace(XHTML_NS, "xhtml");
        internal static readonly Namespace featuresNS = new Namespace(FEATURES_NS, "f");
        internal static readonly Namespace mediaNS = new Namespace(MEDIA_NS, "media");
        internal static readonly Namespace liveNS = new Namespace(LIVE_NS, "live");
        protected internal static XmlRestRequestHelper xmlRestRequestHelper = new XmlRestRequestHelper();

        protected internal AtomProtocolVersion _atomVer;
        internal readonly Namespace _atomNS;
        internal readonly Namespace _pubNS;
        protected internal XmlNamespaceManager _nsMgr;

        private readonly Uri _feedServiceUrl;
        private IBlogClientOptions _clientOptions;

        public AtomClient(AtomProtocolVersion atomVer, Uri postApiUrl, IBlogCredentialsAccessor credentials)
            : base(credentials)
        {
            _feedServiceUrl = postApiUrl;

            // configure client options
            BlogClientOptions clientOptions = new BlogClientOptions();
            ConfigureClientOptions(clientOptions);
            _clientOptions = clientOptions;

            _atomVer = atomVer;
            _atomNS = new Namespace(atomVer.NamespaceUri, "atom");
            _pubNS = new Namespace(atomVer.PubNamespaceUri, "app");
            _nsMgr = new XmlNamespaceManager(new NameTable());
            _nsMgr.AddNamespace(_atomNS.Prefix, _atomNS.Uri);
            _nsMgr.AddNamespace(_pubNS.Prefix, _pubNS.Uri);
            _nsMgr.AddNamespace(xhtmlNS.Prefix, xhtmlNS.Uri);
            _nsMgr.AddNamespace(featuresNS.Prefix, featuresNS.Uri);
            _nsMgr.AddNamespace(mediaNS.Prefix, mediaNS.Uri);
            _nsMgr.AddNamespace(liveNS.Prefix, liveNS.Uri);
        }

        protected virtual Uri FeedServiceUrl { get { return _feedServiceUrl; } }


        public IBlogClientOptions Options
        {
            get
            {
                return _clientOptions;
            }
        }

        /// <summary>
        /// Enable external users of the class to completely replace
        /// the client options
        /// </summary>
        /// <param name="newClientOptions"></param>
        public void OverrideOptions(IBlogClientOptions newClientOptions)
        {
            _clientOptions = newClientOptions;
        }

        /// <summary>
        /// Enable subclasses to change the default client options
        /// </summary>
        /// <param name="clientOptions"></param>
        protected virtual void ConfigureClientOptions(BlogClientOptions clientOptions)
        {
        }

        public virtual async Task<BlogPostCategory[]> GetCategories(string blogId)
        {
            ArrayList categoryList = new ArrayList();

            XmlDocument xmlDoc = await GetCategoryXml(blogId);
            foreach (XmlElement categoriesNode in xmlDoc.DocumentElement.SelectNodesNS("app:categories", _pubNS.Uri))
            {
                string categoriesScheme = categoriesNode.GetAttribute("scheme");
                foreach (XmlElement categoryNode in categoriesNode.SelectNodesNS("atom:category", _atomNS.Uri))
                {
                    string categoryScheme = categoryNode.GetAttribute("scheme");
                    if (categoryScheme == "")
                        categoryScheme = categoriesScheme;
                    if (CategoryScheme == categoryScheme)
                    {
                        string categoryName = categoryNode.GetAttribute("term");
                        string categoryLabel = categoryNode.GetAttribute("label");
                        if (categoryLabel == "")
                            categoryLabel = categoryName;

                        categoryList.Add(new BlogPostCategory(categoryName, categoryLabel));
                    }
                }
            }

            return (BlogPostCategory[])categoryList.ToArray(typeof(BlogPostCategory));
        }

        public virtual Task<BlogPostKeyword[]> GetKeywords(string blogId)
        {
            //Debug.Fail("AtomClient does not support GetKeywords!");
            return Task.FromResult(new BlogPostKeyword[] { });
        }

        protected virtual void FixupBlogId(ref string blogId)
        {
        }

        protected virtual async Task<XmlDocument> GetCategoryXml(string blogId)
        {
            // Get the service document
            Login();

            FixupBlogId(ref blogId);

            XmlRestRequestHelper.XmlRequestResult result = new XmlRestRequestHelper.XmlRequestResult();
            result.uri = FeedServiceUrl;
            var xmlDoc = await xmlRestRequestHelper.Get(RequestFilter, result);

            foreach (XmlElement entryEl in xmlDoc.SelectNodesNS("app:service/app:workspace/app:collection", _nsMgr.ToNSMethodFormat()))
            {
                string href = XmlHelper.GetUrl(entryEl, "@href", result.uri);
                if (blogId == href)
                {
                    XmlDocument results = new XmlDocument();
                    XmlElement rootElement = results.CreateElement("categoryInfo");
                    results.AppendChild(rootElement);
                    foreach (XmlElement categoriesNode in entryEl.SelectNodesNS("app:categories", _nsMgr.ToNSMethodFormat()))
                    {
                        await AddCategoriesXml(categoriesNode, rootElement, result);
                    }
                    return results;
                }
            }
            //Debug.Fail("Couldn't find collection in service document:\r\n" + xmlDoc.OuterXml);
            return new XmlDocument();
        }

        private async Task AddCategoriesXml(XmlElement categoriesNode, XmlElement containerNode, XmlRestRequestHelper.XmlRequestResult result)
        {
            if (categoriesNode.Attributes.Any(a => a.NodeName == "href"))
            {
                string href = XmlHelper.GetUrl(categoriesNode, "@href", result.uri);
                if (href != null && href.Length > 0)
                {
                    Uri uri = new Uri(href);
                    if (result.uri == null || !uri.Equals(result.uri)) // detect simple cycles
                    {
                        XmlDocument doc = await xmlRestRequestHelper.Get(RequestFilter, result);
                        XmlElement categories = (XmlElement)doc.SelectSingleNodeNS(@"app:categories", _nsMgr.ToNSMethodFormat());
                        if (categories != null)
                            await AddCategoriesXml(categories, containerNode, result);
                    }
                }
            }
            else
            {
                containerNode.AppendChild(containerNode.OwnerDocument.ImportNode(categoriesNode, true));
            }
        }


        protected virtual HttpAsyncRequestFilter RequestFilter
        {
            get
            {
                return new HttpAsyncRequestFilter(AuthorizationFilter);
            }
        }

        private Task AuthorizationFilter(HttpRequestMessage request)
        {
            // TODO: Fix this

            //			request.KeepAlive = true;
            //			request.ProtocolVersion = HttpVersion.Version11;
            //request.Credentials = new NetworkCredential(Credentials.Username, Credentials.Password);
            
            return Task.FromResult(true);
        }

        public async Task DeletePost(string blogId, string postId, bool publish)
        {
            await Login();

            FixupBlogId(ref blogId);
            Uri editUri = PostIdToPostUri(postId);

            try
            {
                RedirectHelper.SimpleRequest sr = new RedirectHelper.SimpleRequest("DELETE", new HttpAsyncRequestFilter(DeleteRequestFilter));
                var response = await RedirectHelper.GetResponse(UrlHelper.SafeToAbsoluteUri(editUri), new RedirectHelper.RequestFactory(sr.Create));
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode != Windows.Web.Http.HttpStatusCode.NotFound && response.StatusCode !=
                        Windows.Web.Http.HttpStatusCode.Gone)
                    {
                        throw new Exception();
                    }
                    {
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                if (!AttemptDeletePostRecover(e, blogId, UrlHelper.SafeToAbsoluteUri(editUri), publish))
                    throw;
            }
        }

        private Task DeleteRequestFilter(HttpRequestMessage request)
        {
            request.Headers["If-match"] = "*";
            RequestFilter(request);
            return Task.FromResult(true);
        }

        protected virtual bool AttemptDeletePostRecover(Exception e, string blogId, string postId, bool publish)
        {
            return false;
        }        

        public virtual Task<BlogPost[]> GetRecentPostsAsync(string blogId, int maxPosts, bool includeCategories, DateTime? now)
        {
            return GetRecentPostsInternal(blogId, maxPosts, includeCategories, now);
        }

        protected async Task<BlogPost[]> GetRecentPostsInternal(string blogId, int maxPosts, bool includeCategories, DateTime? now)
        {
            Login();

            FixupBlogId(ref blogId);

            HashSet<string> seenIds = new HashSet<string>();

            ArrayList blogPosts = new ArrayList();
            try
            {
                while (true)
                {
                    XmlDocument doc;
                    XmlRestRequestHelper.XmlRequestResult result = new XmlRestRequestHelper.XmlRequestResult();
                    result.uri = new Uri(blogId);

                    // This while-loop nonsense is necessary because New Blogger has a bug
                    // where the official URL for getting recent posts doesn't work when
                    // the orderby=published flag is set, but there's an un-official URL
                    // that will work correctly. Therefore, subclasses need the ability
                    // to inspect exceptions that occur, along with the URI that was used
                    // to make the request, and determine whether an alternate URI should
                    // be used.
                    while (true)
                    {
                        try
                        {
                            doc = await xmlRestRequestHelper.Get(RequestFilter, result);
                            break;
                        }
                        catch (Exception e)
                        {
                            //Debug.WriteLine(e.ToString());
                            if (AttemptAlternateGetRecentPostUrl(e, ref blogId))
                                continue;
                            else
                                throw;
                        }
                    }

                    XmlNodeList nodeList = doc.SelectNodesNS("/atom:feed/atom:entry", _nsMgr.ToNSMethodFormat());
                    if (nodeList.Count == 0)
                        break;
                    foreach (XmlElement node in nodeList)
                    {
                        BlogPost blogPost = this.Parse(node, includeCategories, result.uri);
                        if (blogPost != null)
                        {
                            if (seenIds.Contains(blogPost.Id))
                                throw new DuplicateEntryIdException();
                            seenIds.Add(blogPost.Id);

                            if (!now.HasValue || blogPost.DatePublished.CompareTo(now.Value) < 0)
                                blogPosts.Add(blogPost);
                        }
                        if (blogPosts.Count >= maxPosts)
                            break;
                    }
                    if (blogPosts.Count >= maxPosts)
                        break;

                    XmlElement nextNode = doc.SelectSingleNodeNS("/atom:feed/atom:link[@rel='next']", _nsMgr.ToNSMethodFormat()) as XmlElement;
                    if (nextNode == null)
                        break;
                    blogId = XmlHelper.GetUrl(nextNode, "@href", result.uri);
                    if (blogId.Length == 0)
                        break;
                }
            }
            catch (DuplicateEntryIdException)
            {

                //if (ApplicationDiagnostics.AutomationMode)
                //    Debug.WriteLine("Duplicate IDs detected in feed");
                //else
                //    Debug.Fail("Duplicate IDs detected in feed");

            }
            return (BlogPost[])blogPosts.ToArray(typeof(BlogPost));
        }

        /// <summary>
        /// Subclasses should override this if there are particular exception conditions
        /// that can be repaired by modifying the URI. Return true if the request should
        /// be retried using the (possibly modified) URI, or false if the exception should
        /// be thrown by the caller.
        /// </summary>
        protected virtual bool AttemptAlternateGetRecentPostUrl(Exception e, ref string uri)
        {
            return false;
        }

        private class DuplicateEntryIdException : Exception
        {
        }

        public async Task<BlogPost> GetPost(string blogId, string postId)
        {
            Login();

            FixupBlogId(ref blogId);

            XmlRestRequestHelper.XmlRequestResult result = new XmlRestRequestHelper.XmlRequestResult();

            result.uri = PostIdToPostUri(postId);
            result.responseHeaders = new HttpResponseMessage().Headers;
            var doc = await xmlRestRequestHelper.Get(RequestFilter, result);
            XmlDocument remotePost = (XmlDocument) doc.CloneNode(true);
            XmlElement entryNode = doc.SelectSingleNodeNS("/atom:entry", _nsMgr.ToNSMethodFormat()) as XmlElement;
            if (entryNode == null)
                throw new BlogClientInvalidServerResponseException("GetPost", "No post entry returned from server", doc.GetXml());

            BlogPost post = Parse(entryNode, true, result.uri);
            post.Id = postId;
            post.ETag = FilterWeakEtag(result.responseHeaders["ETag"]);
            post.AtomRemotePost = remotePost;
            return post;
        }

        private static string FilterWeakEtag(string etag)
        {
            if (etag != null && etag.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
                return null;
            return etag;
        }

        protected virtual Uri PostIdToPostUri(string postId)
        {
            return new Uri(postId);
        }

        protected virtual string PostUriToPostId(string postUri)
        {
            return postUri;
        }

        public virtual async Task<bool> EditPost(string blogId, BlogPost post, INewCategoryContext newCategoryContext, bool publish, EditPostResult result)
        {
            if (!publish && !Options.SupportsPostAsDraft)
            {
                //Debug.Fail("Post to draft not supported on this provider");
                throw new BlogClientPostAsDraftUnsupportedException();
            }

            Login();

            FixupBlogId(ref blogId);

            XmlDocument doc = post.AtomRemotePost;
            XmlElement entryNode = doc.SelectSingleNodeNS("/atom:entry", _nsMgr.ToNSMethodFormat()) as XmlElement;

            // No documentUri is needed because we ensure xml:base is set on the root
            // when we retrieve from XmlRestRequestHelper
            Populate(post, null, entryNode, publish);
            string etagToMatch = FilterWeakEtag(post.ETag);

            try
            {
            retry:
                try
                {
                    XmlRestRequestHelper.XmlRequestResult xmlResult2 = new XmlRestRequestHelper.XmlRequestResult();
                    xmlResult2.uri = PostIdToPostUri(post.Id);
                    await xmlRestRequestHelper.Put(etagToMatch, RequestFilter, ENTRY_CONTENT_TYPE, doc, _clientOptions.CharacterSet, true, xmlResult2);
                }
                catch (WebException we)
                {
                    if (we.Status == WebExceptionStatus.ProtocolError)
                    {
                        if (((HttpWebResponse)we.Response).StatusCode == HttpStatusCode.PreconditionFailed)
                        {
                            if (etagToMatch != null && etagToMatch.Length > 0)
                            {
                                HttpRequestHelper.LogException(we);

                                string currentEtag = await GetEtag(UrlHelper.SafeToAbsoluteUri(PostIdToPostUri(post.Id)));

                                if (currentEtag != null && currentEtag.Length > 0
                                    && currentEtag != etagToMatch)
                                {
                                    if (ConfirmOverwrite())
                                    {
                                        etagToMatch = currentEtag;
                                        goto retry;
                                    }
                                    else
                                    {
                                        throw new BlogClientOperationCancelledException();
                                    }
                                }
                            }
                        }
                    }
                    throw;
                }
            }
            catch (Exception e)
            {
                if (!AttemptEditPostRecover(e, blogId, post, newCategoryContext, publish, result))
                {
                    // convert to a provider exception if this is a 404 (allow us to 
                    // catch this case explicitly and attempt a new post to recover)
                    if (e is WebException)
                    {
                        WebException webEx = e as WebException;
                        HttpWebResponse response = webEx.Response as HttpWebResponse;
                        if (response != null && response.StatusCode == HttpStatusCode.NotFound)
                            throw new BlogClientProviderException("404", e.Message);
                    }

                    // no special handling, just re-throw
                    throw;
                }
            }

            XmlRestRequestHelper.XmlRequestResult xmlResult = new XmlRestRequestHelper.XmlRequestResult();
            xmlResult.uri = PostIdToPostUri(post.Id);
            xmlResult.responseHeaders = new HttpResponseMessage().Headers;
            result.remotePost = await xmlRestRequestHelper.Get(RequestFilter, xmlResult);
            result.etag = FilterWeakEtag(xmlResult.responseHeaders["ETag"]);
            //Debug.Assert(remotePost != null, "After successful PUT, remote post could not be retrieved");

            if (Options.SupportsNewCategories)
                foreach (BlogPostCategory category in post.NewCategories)
                    newCategoryContext.NewCategoryAdded(category);

            return true;
        }

        public async Task<string> GetEtag(string uri)
        {
            return await GetEtag(uri, RequestFilter);
        }

        public static async Task<string> GetEtag(string uri, HttpAsyncRequestFilter requestFilter)
        {
            return await GetEtagImpl(uri, requestFilter, "HEAD", "GET");
        }

        /// <param name="uri"></param>
        /// <param name="methods">An array of HTTP methods that should be tried until one of them does not return 405.</param>
        private static async Task<string> GetEtagImpl(string uri, HttpAsyncRequestFilter requestFilter, params string[] methods)
        {
            try
            {
                var response = await RedirectHelper.GetResponse(uri,
                    new RedirectHelper.RequestFactory(new RedirectHelper.SimpleRequest(methods[0], requestFilter).Create));
                try
                {
                    return FilterWeakEtag(response.Headers["ETag"]);
                }
                finally
                {
                    response.Dispose();
                }
            }
            catch (WebException we)
            {
                if (methods.Length > 1 && we.Status == WebExceptionStatus.ProtocolError)
                {
                    HttpWebResponse resp = we.Response as HttpWebResponse;
                    if (resp != null && (resp.StatusCode == HttpStatusCode.MethodNotAllowed || resp.StatusCode == HttpStatusCode.NotImplemented))
                    {
                        string[] newMethods = new string[methods.Length - 1];
                        Array.Copy(methods, 1, newMethods, 0, newMethods.Length);
                        return await GetEtagImpl(uri, requestFilter, newMethods);
                    }
                }
                throw;
            }
        }

        public static bool ConfirmOverwrite()
        {
            //return DialogResult.Yes == BlogClientUIContext.ShowDisplayMessageOnUIThread(MessageId.ConfirmOverwrite);
            return true;
        }

        protected virtual bool AttemptEditPostRecover(Exception e, string blogId, BlogPost post, INewCategoryContext newCategoryContext, bool publish, EditPostResult result)
        {
            result.etag = null;
            result.remotePost = null;
            return false;
        }

        public virtual Task<BlogInfo[]> GetUsersBlogsAsync()
        {
            Login();
            return GetUsersBlogsInternal();
        }

        protected async Task<BlogInfo[]> GetUsersBlogsInternal()
        {
            
            XmlRestRequestHelper.XmlRequestResult xmlResult = new XmlRestRequestHelper.XmlRequestResult();
            xmlResult.uri = FeedServiceUrl;
            XmlDocument xmlDoc = await xmlRestRequestHelper.Get(RequestFilter, xmlResult);

            // Either the FeedServiceUrl points to a service document OR a feed.

            if (xmlDoc.SelectSingleNodeNS("/app:service", _nsMgr.ToNSMethodFormat()) != null)
            {
                ArrayList blogInfos = new ArrayList();
                foreach (XmlElement coll in xmlDoc.SelectNodesNS("/app:service/app:workspace/app:collection", _nsMgr.ToNSMethodFormat()))
                {
                    bool promote = ShouldPromote(coll);

                    // does this collection accept entries?
                    XmlNodeList acceptNodes = coll.SelectNodesNS("app:accept", _nsMgr.ToNSMethodFormat());
                    bool acceptsEntries = false;
                    if (acceptNodes.Count == 0)
                    {
                        acceptsEntries = true;
                    }
                    else
                    {
                        foreach (XmlElement acceptNode in acceptNodes)
                        {
                            if (AcceptsEntry(acceptNode.InnerText))
                            {
                                acceptsEntries = true;
                                break;
                            }
                        }
                    }

                    if (acceptsEntries)
                    {
                        string feedUrl = XmlHelper.GetUrl(coll, "@href", xmlResult.uri);
                        if (feedUrl == null || feedUrl.Length == 0)
                            continue;

                        // form title
                        StringBuilder titleBuilder = new StringBuilder();
                        foreach (XmlElement titleContainerNode in new XmlElement[] { coll.ParentNode as XmlElement, coll })
                        {
                            Debug.Assert(titleContainerNode != null);
                            if (titleContainerNode != null)
                            {
                                XmlElement titleNode = titleContainerNode.SelectSingleNodeNS("atom:title", _nsMgr.ToNSMethodFormat()) as XmlElement;
                                if (titleNode != null)
                                {
                                    string titlePart = _atomVer.TextNodeToPlaintext(titleNode);
                                    if (titlePart.Length != 0)
                                    {
                                        //Res.LOCME("loc the separator between parts of the blog name");
                                        if (titleBuilder.Length != 0)
                                            titleBuilder.Append(" - ");
                                        titleBuilder.Append(titlePart);
                                    }
                                }
                            }
                        }

                        // get homepage URL
                        string homepageUrl = "";
                        string dummy = "";
                        

                        XmlRestRequestHelper.XmlRequestResult xmlResult2 = new XmlRestRequestHelper.XmlRequestResult();
                        xmlResult2.uri = new Uri(feedUrl);
                        XmlDocument feedDoc = await xmlRestRequestHelper.Get(RequestFilter, xmlResult2);
                        ParseFeedDoc(feedDoc, xmlResult2.uri, false, ref homepageUrl, ref dummy);

                        // TODO: Sniff out the homepage URL
                        BlogInfo blogInfo = new BlogInfo(feedUrl, titleBuilder.ToString().Trim(), homepageUrl);
                        if (promote)
                            blogInfos.Insert(0, blogInfo);
                        else
                            blogInfos.Add(blogInfo);
                    }
                }

                return (BlogInfo[])blogInfos.ToArray(typeof(BlogInfo));
            }
            else
            {
                string title = string.Empty;
                string homepageUrl = string.Empty;

                ParseFeedDoc(xmlDoc, xmlResult.uri, true, ref homepageUrl, ref title);

                return new BlogInfo[] { new BlogInfo(UrlHelper.SafeToAbsoluteUri(FeedServiceUrl), title, homepageUrl) };
            }
        }

        protected virtual bool ShouldPromote(XmlElement collection)
        {
            return false;
        }

        private static bool AcceptsEntry(string contentType)
        {
            IDictionary values = MimeHelper.ParseContentType(contentType, true);
            string mainType = values[""] as string;

            switch (mainType)
            {
                case "entry":
                case "*/*":
                case "application/*":
                    return true;
                case "application/atom+xml":
                    string subType = values["type"] as string;
                    if (subType != null)
                        subType = subType.Trim().ToUpperInvariant();

                    if (subType == "ENTRY")
                        return true;
                    else
                        return false;

                default:
                    return false;
            }
        }

        private void ParseFeedDoc(XmlDocument xmlDoc, Uri baseUri, bool includeTitle, ref string homepageUrl, ref string title)
        {
            if (includeTitle)
            {
                XmlElement titleEl = xmlDoc.SelectSingleNodeNS(@"atom:feed/atom:title", _nsMgr.ToNSMethodFormat()) as XmlElement;
                if (titleEl != null)
                    title = _atomVer.TextNodeToPlaintext(titleEl);
            }

            foreach (XmlElement linkEl in xmlDoc.SelectNodesNS(@"atom:feed/atom:link[@rel='alternate']", _nsMgr.ToNSMethodFormat()))
            {
                IDictionary contentTypeInfo = MimeHelper.ParseContentType(linkEl.GetAttribute("type"), true);
                switch (contentTypeInfo[""] as string)
                {
                    case "text/html":
                    case "application/xhtml+xml":
                        homepageUrl = XmlHelper.GetUrl(linkEl, "@href", baseUri);
                        return;
                }
            }
        }

        public virtual BlogInfo[] GetImageEndpoints()
        {
            throw new NotImplementedException();
        }

        public virtual bool IsSecure
        {
            get
            {
                try
                {
                    return UrlHelper.SafeToAbsoluteUri(FeedServiceUrl).StartsWith("https://", StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            }
        }

        public virtual Task<string> DoBeforePublishUploadWork(IFileUploadContext uploadContext)
        {
            throw new BlogClientMethodUnsupportedException("UploadFileBeforePublish");
        }

        public virtual Task DoAfterPublishUploadWork(IFileUploadContext uploadContext)
        {
            throw new BlogClientMethodUnsupportedException("UploadFileAfterPublish");
        }

        public virtual Task<BlogPostCategory[]> SuggestCategories(string blogId, string partialCategoryName)
        {
            throw new BlogClientMethodUnsupportedException("SuggestCategories");
        }

        public virtual async Task<HttpResponseMessage> SendAuthenticatedHttpRequest(string requestUri, int timeoutMs, HttpAsyncRequestFilter filter)
        {
            return await BlogClientHelper.SendAuthenticatedHttpRequest(requestUri, filter, await CreateCredentialsFilter(requestUri));
        }

        protected virtual async Task<HttpAsyncRequestFilter> CreateCredentialsFilter(string requestUri)
        {
            TransientCredentials tc = await Login();
            return HttpRequestCredentialsFilter.Create(tc.Username, tc.Password, requestUri, true);
        }

        public virtual Task<string> AddCategory(string blogId, BlogPostCategory categohowry)
        {
            throw new BlogClientMethodUnsupportedException("AddCategory");
        }

        public async Task<string> NewPost(string blogId, BlogPost post, INewCategoryContext newCategoryContext, bool publish, PostResult postResult)
        {
            if (!publish && !Options.SupportsPostAsDraft)
            {
                //Debug.Fail("Post to draft not supported on this provider");
                throw new BlogClientPostAsDraftUnsupportedException();
            }

            Login();

            FixupBlogId(ref blogId);

            XmlDocument doc = new XmlDocument();
            XmlElement entryNode = doc.CreateElementNS(_atomNS.Uri, _atomNS.Prefix + ":entry");
            doc.AppendChild(entryNode);
            Populate(post, null, entryNode, publish);

            string slug = null;
            if (Options.SupportsSlug)
                slug = post.Slug;

            XmlRestRequestHelper.XmlRequestResult xmlResult2 = new XmlRestRequestHelper.XmlRequestResult();
            xmlResult2.uri = new Uri(blogId);
            XmlDocument result = await xmlRestRequestHelper.Post(
                new HttpAsyncRequestFilter(new NewPostRequest(this, slug).RequestFilter),
                ENTRY_CONTENT_TYPE,
                doc,
                _clientOptions.CharacterSet,
                xmlResult2);

            postResult.ETag = FilterWeakEtag(xmlResult2.responseHeaders["ETag"]);
            string location = xmlResult2.responseHeaders["Location"];
            if (string.IsNullOrEmpty(location))
            {
                throw new BlogClientInvalidServerResponseException("POST", "The HTTP response was missing the required Location header.", "");
            }
            if (location != xmlResult2.responseHeaders["Content-Location"] || result == null)
            {
                XmlRestRequestHelper.XmlRequestResult xmlResult = new XmlRestRequestHelper.XmlRequestResult();
                xmlResult.uri = new Uri(location);
                result = await xmlRestRequestHelper.Get(RequestFilter, xmlResult);
                postResult.ETag = FilterWeakEtag(xmlResult.responseHeaders["ETag"]);
            }

            postResult.AtomRemotePost = (XmlDocument) result.CloneNode(true);
            Parse(result.DocumentElement, true, xmlResult2.uri);

            if (Options.SupportsNewCategories)
                foreach (BlogPostCategory category in post.NewCategories)
                    newCategoryContext.NewCategoryAdded(category);

            return PostUriToPostId(location);
        }

        private class NewPostRequest
        {
            private readonly AtomClient _parent;
            private readonly string _slug;

            public NewPostRequest(AtomClient parent, string slug)
            {
                _parent = parent;
                _slug = slug;
            }

            public Task RequestFilter(HttpRequestMessage request)
            {
                _parent.RequestFilter(request);
                if (_parent.Options.SupportsSlug && _slug != null && _slug.Length > 0)
                    request.Headers["Slug"] = SlugHeaderValue;
                return Task.FromResult(true);
            }

            private string SlugHeaderValue
            {
                get
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(_slug);
                    StringBuilder sb = new StringBuilder(bytes.Length * 2);
                    foreach (byte b in bytes)
                    {
                        if (b > 0x7F || b == '%')
                        {
                            sb.AppendFormat("%{0:X2}", b);
                        }
                        else if (b == '\r' || b == '\n')
                        {
                            // no \r or \n allowed in slugs
                        }
                        else if (b == 0)
                        {
                            Debug.Fail("null byte in slug string, this should never happen");
                        }
                        else
                        {
                            sb.Append((char)b);
                        }
                    }
                    return sb.ToString();
                }
            }
        }

        public virtual Task<BlogPost> GetPage(string blogId, string pageId)
        {
            throw new BlogClientMethodUnsupportedException("GetPage");
        }

        public virtual Task<PageInfo[]> GetPageList(string blogId)
        {
            throw new BlogClientMethodUnsupportedException("GetPageList");
        }

        public virtual Task<BlogPost[]> GetPages(string blogId, int maxPages)
        {
            throw new BlogClientMethodUnsupportedException("GetPages");
        }

        public virtual Task<string> NewPage(string blogId, BlogPost page, bool publish, string etag, XmlDocument remotePost)
        {
            throw new BlogClientMethodUnsupportedException("NewPage");
        }

        public virtual Task<bool> EditPage(string blogId, BlogPost page, bool publish, string etag, XmlDocument remotePost)
        {
            throw new BlogClientMethodUnsupportedException("EditPage");
        }

        public virtual Task DeletePage(string blogId, string pageId)
        {
            throw new BlogClientMethodUnsupportedException("DeletePage");
        }

        public virtual Task<AuthorInfo[]> GetAuthors(string blogId)
        {
            throw new BlogClientMethodUnsupportedException("GetAuthors");
        }

        protected virtual string CategoryScheme
        {
            get { return ""; }
        }

        public virtual BlogPost Parse(XmlElement entryNode, bool includeCategories, Uri documentUri)
        {
            BlogPost post = new BlogPost();
            AtomEntry atomEntry = new AtomEntry(_atomVer, _atomNS, CategoryScheme, _nsMgr, documentUri, entryNode);

            post.Title = atomEntry.Title;
            post.Excerpt = atomEntry.Excerpt;
            post.Id = PostUriToPostId(atomEntry.EditUri);
            post.Permalink = atomEntry.Permalink;
            post.Contents = atomEntry.ContentHtml;
            post.DatePublished = atomEntry.PublishDate;
            if (Options.SupportsCategories && includeCategories)
                post.Categories = atomEntry.Categories;

            return post;
        }

        /// <summary>
        /// Take the blog post data and put it into the XML node.
        /// </summary>
        protected virtual void Populate(BlogPost post, Uri documentUri, XmlElement node, bool publish)
        {
            AtomEntry atomEntry = new AtomEntry(_atomVer, _atomNS, CategoryScheme, _nsMgr, documentUri, node);

            if (post.IsNew)
                atomEntry.GenerateId();
            atomEntry.Title = post.Title;
            if (Options.SupportsExcerpt && post.Excerpt != null && post.Excerpt.Length > 0)
                atomEntry.Excerpt = post.Excerpt;
            // extra space is to work around AOL Journals XML parsing bug
            atomEntry.ContentHtml = post.Contents + " ";
            if (Options.SupportsCustomDate && post.HasDatePublishedOverride)
            {
                atomEntry.PublishDate = post.DatePublishedOverride;
            }

            if (Options.SupportsCategories)
            {
                atomEntry.ClearCategories();

                foreach (BlogPostCategory cat in post.Categories)
                    if (!BlogPostCategoryNone.IsCategoryNone(cat))
                        atomEntry.AddCategory(cat);

                if (Options.SupportsNewCategories)
                    foreach (BlogPostCategory cat in post.NewCategories)
                        if (!BlogPostCategoryNone.IsCategoryNone(cat))
                            atomEntry.AddCategory(cat);
            }

            if (Options.SupportsPostAsDraft)
            {
                // remove existing draft nodes
                while (true)
                {
                    var draftNode = node.SelectSingleNodeNS(@"app:control/app:draft", _nsMgr.ToNSMethodFormat());
                    if (draftNode == null)
                        break;
                    draftNode.ParentNode.RemoveChild(draftNode);
                }

                if (!publish)
                {
                    // ensure control node exists
                    var controlNode = node.SelectSingleNodeNS(@"app:control", _nsMgr.ToNSMethodFormat());
                    if (controlNode == null)
                    {
                        controlNode = node.OwnerDocument.CreateElementNS(_pubNS.Uri, _pubNS.Prefix + ":control");
                        node.AppendChild(controlNode);
                    }
                    // create new draft node
                    XmlElement newDraftNode = node.OwnerDocument.CreateElementNS(_pubNS.Uri, _pubNS.Prefix + ":draft");
                    newDraftNode.InnerText = "yes";
                    controlNode.AppendChild(newDraftNode);
                }
            }

            //post.Categories;
            //post.CommentPolicy;
            //post.CopyFrom;
            //post.Excerpt;
            //post.HasDatePublishedOverride;
            //post.Id;
            //post.IsNew;
            //post.IsTemporary;
            //post.Keywords;
            //post.Link;
            //post.Permalink;
            //post.PingUrls;
            //post.ResetToNewPost;
            //post.TrackbackPolicy;
        }

        public bool? DoesFileNeedUpload(IFileUploadContext uploadContext)
        {
            return null;
        }
    }

    internal struct Namespace
    {
        public Namespace(string uri, string prefix)
        {
            Uri = uri;
            Prefix = prefix;
        }

        public readonly string Uri;
        public readonly string Prefix;
    }
}
