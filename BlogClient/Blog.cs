// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Globalization;
using System.IO;
using System.Collections;
using System.Net;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Data.Xml.Dom;
using Windows.Web.Http;
using OpenLiveWriter.CoreServices;
using OpenLiveWriter.BlogClient.Clients;
using OpenLiveWriter.Extensibility.BlogClient;
using OpenLiveWriter.Api;

namespace OpenLiveWriter.BlogClient
{

    public class PostResult
    {
        public string PostId;
        public DateTime DatePublished;
        public string ETag;
        public XmlDocument AtomRemotePost;
    }

    /// <summary>
    /// Facade class for programmatically interacting with a blog
    /// </summary>
    public class Blog : IEditorAccount
    {
        public Blog(string blogId)
        {
            //_settings = BlogSettings.ForBlogId(blogId);
        }

        public Blog(IBlogSettingsAccessor accessor)
        {
            _settings = accessor;
        }

        public void Dispose()
        {
            if (_settings != null)
                _settings.Dispose();

            GC.SuppressFinalize(this);
        }

        ~Blog()
        {
            //Debug.Fail("Failed to dispose Blog object");
        }


        public string Id
        {
            get
            {
                return _settings.Id;
            }
        }

        public string ProviderId
        {
            get
            {
                return _settings.ProviderId;
            }
        }

        public string Name
        {
            get
            {
                return _settings.BlogName;
            }
        }

        public string HostBlogId
        {
            get
            {
                return _settings.HostBlogId;
            }
        }


        public string HomepageUrl
        {
            get
            {
                return _settings.HomepageUrl;
            }
        }

        public string HomepageBaseUrl
        {
            get
            {
                string baseUrl = HomepageUrl;
                Uri uri = new Uri(HomepageUrl);
                string path = uri.PathAndQuery;
                int queryIndex = path.IndexOf("?", StringComparison.OrdinalIgnoreCase);
                if (queryIndex != -1)
                    path = path.Substring(0, queryIndex);
                if (!path.EndsWith("/", StringComparison.OrdinalIgnoreCase))
                {
                    int lastPathIndex = path.LastIndexOf("/", StringComparison.OrdinalIgnoreCase);
                    string lastPathPart = path.Substring(lastPathIndex + 1);
                    if (lastPathPart.IndexOf(".", StringComparison.OrdinalIgnoreCase) != -1)
                    {
                        path = path.Substring(0, lastPathIndex);
                        string hostUrl = uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.Unescaped);
                        baseUrl = UrlHelper.UrlCombine(hostUrl, path);
                    }
                }
                return baseUrl;
            }
        }

        public string PostApiUrl
        {
            get
            {
                return _settings.PostApiUrl;
            }
        }

        public string AdminUrl
        {
            get
            {
                return FormatUrl(ClientOptions.AdminUrl);
            }
        }

        public string GetPostEditingUrl(string postId)
        {
            string pattern = ClientOptions.PostEditingUrlPostIdPattern;
            if (!string.IsNullOrEmpty(pattern))
            {
                Match m = Regex.Match(postId, pattern, RegexOptions.IgnoreCase);
                if (m.Success && m.Groups[1].Success)
                {
                    postId = m.Groups[1].Value;
                }
                else
                {
                    //Debug.Fail("Parsing failed: " + postId);
                }
            }
            return FormatUrl(ClientOptions.PostEditingUrl, postId);
        }

        private string FormatUrl(string url)
        {
            return FormatUrl(url, null);
        }

        private string FormatUrl(string url, string postId)
        {
            return BlogClientHelper.FormatUrl(url, _settings.HomepageUrl, _settings.PostApiUrl, _settings.HostBlogId, postId);
        }


        public string ServiceName
        {
            get
            {
                return _settings.ServiceName;
            }
        }

        public string ServiceDisplayName
        {
            get
            {
                // look for an option override
                if (BlogClient.Options.ServiceName != String.Empty)
                    return BlogClient.Options.ServiceName;
                else
                    return _settings.ServiceName;
            }
        }

        public IBlogClientOptions ClientOptions
        {
            get
            {
                return BlogClient.Options;
            }
        }

        IEditorOptions IEditorAccount.EditorOptions
        {
            get
            {
                return (IEditorOptions)BlogClient.Options;
            }
        }


        public void DisplayException(Exception ex)
        {
            // display a custom display message for exceptions that have one
            // registered, otherwise display the generic error form
            if (ex is BlogClientProviderException)
            {
                //IBlogProvider provider = BlogProviderManager.FindProvider(_settings.ProviderId);
                //if (provider != null)
                //{
                //    BlogClientProviderException pe = ex as BlogClientProviderException;
                //    MessageId messageId = provider.DisplayMessageForProviderError(pe.ErrorCode, pe.ErrorString);
                //    if (messageId != MessageId.None)
                //    {
                //        DisplayMessage.Show(messageId, owner);
                //        return;
                //    }
                //}
            }
            else if (ex is WebException)
            {
                //WebException we = (WebException)ex;
                //HttpWebResponse resp = we.Response as HttpWebResponse;
                //if (resp != null)
                //{
                //    string friendlyError = HttpRequestHelper.GetFriendlyErrorMessage(we);
                //    Debug.WriteLine("Server response body:\r\n" + friendlyError);
                //    ex = new BlogClientHttpErrorException(
                //        UrlHelper.SafeToAbsoluteUri(resp.ResponseUri),
                //        friendlyError,
                //        we);
                //}
                //else
                //{
                //    DisplayMessage msg = new DisplayMessage(MessageId.ErrorConnecting);
                //    ex = new BlogClientException(msg.Title, msg.Text);
                //}
                //HttpRequestHelper.LogException(we);
            }

            // no custom message, use default UI
            //DisplayableExceptionDisplayForm.Show(owner, ex);
        }


        public bool IsSpacesBlog
        {
            get { return _settings.IsSpacesBlog; }
        }

        public SupportsFeature SupportsImageUpload
        {
            get
            {
                if (_settings.FileUploadSupport == FileUploadSupport.FTP || ClientOptions.SupportsFileUpload)
                    return SupportsFeature.Yes;
                else
                    return SupportsFeature.No;
            }
        }

        public string DefaultView
        {
            get { return ClientOptions.DefaultView; }
        }

        public FileUploadSupport FileUploadSupport
        {
            get { return _settings.FileUploadSupport; }
        }

        public IBlogFileUploadSettings FileUploadSettings
        {
            get { return _settings.FileUploadSettings; }
        }

        public bool VerifyCredentials()
        {
            return BlogClient.VerifyCredentials();
        }


        public BlogPostCategory[] Categories
        {
            get
            {
                return _settings.Categories;
            }
        }

        public BlogPostKeyword[] Keywords
        {
            get
            {
                return _settings.Keywords;
            }
            set
            {
                _settings.Keywords = value;
            }
        }

        public async Task RefreshKeywords()
        {
            try
            {
                _settings.Keywords = await BlogClient.GetKeywords(_settings.HostBlogId);
            }
            catch (BlogClientOperationCancelledException)
            {

            }
        }

        public async Task RefreshCategories()
        {
            try
            {
                _settings.Categories = await BlogClient.GetCategories(_settings.HostBlogId);
            }
            catch (BlogClientOperationCancelledException)
            {

            }
        }




        public async Task<BlogPost[]> GetRecentPosts(int maxPosts, bool includeCategories)
        {
            BlogPost[] recentPosts = await BlogClient.GetRecentPostsAsync(_settings.HostBlogId, maxPosts, includeCategories, null);
            foreach (BlogPost blogPost in recentPosts)
            {
                // apply content filters
                blogPost.Contents = ContentFilterApplier.ApplyContentFilters(ClientOptions.ContentFilter, blogPost.Contents, ContentFilterMode.Open);

                // if there is no permalink then attempt to construct one
                EnsurePermalink(blogPost);
            }


            return recentPosts;
        }

        public AuthorInfo[] Authors
        {
            get
            {
                AuthorInfo[] authors = _settings.Authors;
                if (authors != null)
                    Array.Sort(authors, new Comparison<AuthorInfo>(delegate(AuthorInfo a, AuthorInfo b)
                                            {
                                                if (a == null ^ b == null)
                                                    return (a == null) ? -1 : 1;
                                                else if (a == null)
                                                    return 0;
                                                else
                                                    return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
                                            }));
                return authors;
            }
        }

        public async Task RefreshAuthors()
        {
            _settings.Authors = await BlogClient.GetAuthors(_settings.HostBlogId);
        }

        public PageInfo[] PageList
        {
            get
            {
                return _settings.Pages;
            }
        }

        public async Task RefreshPageList()
        {
            _settings.Pages = await BlogClient.GetPageList(_settings.HostBlogId);
        }

        public async Task<BlogPost[]> GetPages(int maxPages)
        {
            // get the pages
            BlogPost[] pages = await BlogClient.GetPages(_settings.HostBlogId, maxPages);

            // ensure they are marked with IsPage = true
            foreach (BlogPost page in pages)
            {
                page.IsPage = true;
            }

            // narrow the array to the "max" if necessary
            ArrayList pageList = new ArrayList();
            for (int i = 0; i < Math.Min(pages.Length, maxPages); i++)
                pageList.Add(pages[i]);

            // return pages
            return pageList.ToArray(typeof(BlogPost)) as BlogPost[];
        }


        public async Task<PostResult> NewPost(BlogPost post, INewCategoryContext newCategoryContext, bool publish)
        {
            // initialize result
            PostResult result = new PostResult();

            try
            {
                using (new ContentFilterApplier(post, ClientOptions, ContentFilterMode.Publish))
                {
                    // make the post
                    if (post.IsPage)
                        result.PostId = await BlogClient.NewPage(_settings.HostBlogId, post, publish, result.ETag,
                            result.AtomRemotePost);
                    else
                    {
                        result.PostId = await BlogClient.NewPost(_settings.HostBlogId, post, newCategoryContext, publish, result);
                    }
                       
                }

                // note success
                _settings.LastPublishFailed = false;
            }
            catch
            {
                _settings.LastPublishFailed = true;
                throw;
            }

            // determine the date-published based on whether there was an override
            if (post.HasDatePublishedOverride)
                result.DatePublished = post.DatePublishedOverride;
            else
                result.DatePublished = DateTime.UtcNow;

            // return result
            return result;
        }


        public async Task<PostResult> EditPost(BlogPost post, INewCategoryContext newCategoryContext, bool publish)
        {
            // initialize result (for edits the id never changes)
            PostResult result = new PostResult();
            var editpostResult = new EditPostResult();
           
            result.PostId = post.Id;
            try
            {
                //apply any publishing filters and make the post
                using (new ContentFilterApplier(post, ClientOptions, ContentFilterMode.Publish))
                {
                    // make the post
                    if (post.IsPage)
                        await BlogClient.EditPage(_settings.HostBlogId, post, publish, result.ETag, result.AtomRemotePost);
                    else
                        await BlogClient.EditPost(_settings.HostBlogId, post, newCategoryContext, publish, editpostResult);
                }
                // note success
                _settings.LastPublishFailed = false;
            }
            catch (BlogClientProviderException ex)
            {
                if (ErrorIsInvalidPostId(ex))
                    return await NewPost(post, newCategoryContext, publish);
                else
                    throw;
            }
            catch
            {
                _settings.LastPublishFailed = true;
                throw;
            }

            // determine the date-published based on whether there was an override
            if (post.HasDatePublishedOverride)
                result.DatePublished = post.DatePublishedOverride;
            else
                result.DatePublished = DateTime.UtcNow;

            // return result
            return result;
        }


        /// <summary>
        /// Get the version of the post currently residing on the server
        /// </summary>
        /// <param name="blogPost"></param>
        /// <returns></returns>
        public async Task<BlogPost> GetPost(string postId, bool isPage)
        {
            BlogPost blogPost = null;

            if (isPage)
            {
                // get the page
                blogPost = await BlogClient.GetPage(_settings.HostBlogId, postId);

                // ensure it is marked as a page
                blogPost.IsPage = true;
            }
            else
            {
                blogPost = await BlogClient.GetPost(_settings.HostBlogId, postId);

                // if there is no permalink then attempt to construct one
                EnsurePermalink(blogPost);
            }

            // apply content filters
            blogPost.Contents = ContentFilterApplier.ApplyContentFilters(ClientOptions.ContentFilter, blogPost.Contents, ContentFilterMode.Open);

            // return the blog post
            return blogPost;
        }



        public void DeletePost(string postId, bool isPage, bool publish)
        {
            if (isPage)
                BlogClient.DeletePage(_settings.HostBlogId, postId);
            else
                BlogClient.DeletePost(_settings.HostBlogId, postId, publish);
        }

        /// <summary>
        /// Force a refresh of ClientOptions by forcing the re-creation of the _blogClient
        /// </summary>
        public void InvalidateClient()
        {
            _blogClient = null;
        }

        public IBlogProviderButtonDescription[] ButtonDescriptions
        {
            get
            {
                return _settings.ButtonDescriptions;
            }
        }

        public Task<HttpResponseMessage> SendAuthenticatedHttpRequest(string requestUri, int timeoutMs)
        {
            return BlogClient.SendAuthenticatedHttpRequest(requestUri, timeoutMs, null);
        }


        public override string ToString()
        {
            return _settings.BlogName;
        }

        private IBlogSettingsAccessor _settings;


        private bool ErrorIsInvalidPostId(BlogClientProviderException ex)
        {
            string faultCodePattern = BlogClient.Options.InvalidPostIdFaultCodePattern;
            string faultStringPattern = BlogClient.Options.InvalidPostIdFaultStringPattern;

            if (faultCodePattern != String.Empty && faultStringPattern != String.Empty)
            {
                return FaultCodeMatchesInvalidPostId(ex.ErrorCode, faultCodePattern) &&
                       FaultStringMatchesInvalidPostId(ex.ErrorString, faultStringPattern);
            }
            else if (faultCodePattern != String.Empty)
            {
                return FaultCodeMatchesInvalidPostId(ex.ErrorCode, faultCodePattern);
            }
            else if (faultStringPattern != String.Empty)
            {
                return FaultStringMatchesInvalidPostId(ex.ErrorString, faultStringPattern);
            }
            else
            {
                return false;
            }
        }


        private bool FaultCodeMatchesInvalidPostId(string faultCode, string pattern)
        {
            try // defend against invalid regex in provider or manifest file
            {
                Regex regex = new Regex(pattern, RegexOptions.IgnoreCase);
                return regex.IsMatch(faultCode);
            }
            catch (ArgumentException e)
            {
                //Debug.Fail("Error processing regular expression: " + e.ToString());
                return false;
            }
        }

        private bool FaultStringMatchesInvalidPostId(string faultString, string pattern)
        {
            try // defend against invalid regex in provider or manifest file
            {
                Regex regex = new Regex(pattern, RegexOptions.IgnoreCase);
                return regex.IsMatch(faultString);
            }
            catch (ArgumentException e)
            {
                //Debug.Fail("Error processing regular expression: " + e.ToString());
                return false;
            }
        }

        private void EnsurePermalink(BlogPost blogPost)
        {
            if (blogPost.Permalink == String.Empty)
            {
                if (ClientOptions.PermalinkFormat != String.Empty)
                {
                    // construct the permalink from a pre-provided pattern
                    blogPost.Permalink = FormatUrl(ClientOptions.PermalinkFormat, blogPost.Id);
                }
            }
            else if (!UrlHelper.IsUrl(blogPost.Permalink))
            {
                // if it is not a URL, then we may need to combine it with the homepage url
                try
                {
                    string permalink = UrlHelper.UrlCombine(_settings.HomepageUrl, blogPost.Permalink);
                    if (UrlHelper.IsUrl(permalink))
                    {
                        blogPost.Permalink = permalink;
                    }
                }
                catch
                {
                    // url combine can throw exceptions, ignore these
                }
            }
        }


        /// <summary>
        /// Weblog client
        /// </summary>
        public IBlogClient BlogClient
        {
            get
            {
                //if (_blogClient == null)
                //    _blogClient = BlogClientManager.CreateClient(_settings);

                return _blogClient;
            }
            set { _blogClient = value; }
        }
        private IBlogClient _blogClient;


        private enum ContentFilterMode { Open, Publish };
        private class ContentFilterApplier : IDisposable
        {
            private BlogPost _blogPost;
            private string _originalContents;
            public ContentFilterApplier(BlogPost blogPost, IBlogClientOptions clientOptions, ContentFilterMode filterMode)
            {
                _blogPost = blogPost;
                _originalContents = _blogPost.Contents;
                if (_originalContents != null)
                    _blogPost.Contents = ApplyContentFilters(clientOptions.ContentFilter, _originalContents, filterMode);
            }

            internal static string ApplyContentFilters(string filters, string content, ContentFilterMode filterMode)
            {
                string[] contentFilters = filters.Split(',');
                foreach (string filterString in contentFilters)
                {
                    string contentFilter = filterString.Trim();
                    if (contentFilter != String.Empty)
                    {
                        IBlogPostContentFilter bpContentFilter = null;

                        switch (contentFilter)
                        {
                            case "WordPress":
                                bpContentFilter = new WordPressInputFormatter();
                                break;
                            case "LineBreak2BR":
                                bpContentFilter = new LineBreak2BRInputFormatter();
                                break;
                            case "LineBreak2PBR":
                                bpContentFilter = new LineBreak2PBRInputFormatter();
                                break;
                        }

                        if (bpContentFilter != null)
                        {
                            if (filterMode == ContentFilterMode.Open)
                                content = bpContentFilter.OpenFilter(content);
                            else
                                content = bpContentFilter.PublishFilter(content);
                        }
                    }
                }
                return content;
            }

            public void Dispose()
            {
                _blogPost.Contents = _originalContents;
            }
        }
    }
}
