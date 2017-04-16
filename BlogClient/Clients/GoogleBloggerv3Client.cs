// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using OpenLiveWriter.CoreServices;
using OpenLiveWriter.Extensibility.BlogClient;
using OpenLiveWriter.Localization;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Blogger.v3;
using Google.Apis.Util.Store;
using Google.Apis.Services;
using Google.Apis.Auth.OAuth2.Flows;
using OpenLiveWriter.BlogClient.Providers;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Util;
using System.Globalization;
using System.Diagnostics;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.Web.Http;
using Windows.Web.Http.Headers;
using BlogWriter.OpenLiveWriter;
using Google.Apis.Blogger.v3.Data;
//using OpenLiveWriter.Controls;
//using System.Windows.Forms;
using Newtonsoft.Json;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace OpenLiveWriter.BlogClient.Clients
{
    [BlogClient("GoogleBloggerv3", "GoogleBloggerv3")]
    public class GoogleBloggerv3Client : BlogClientBase, IBlogClient
    {
        // These URLs map to OAuth2 permission scopes for Google Blogger.
        public static string PicasaServiceScope = "https://picasaweb.google.com/data";
        public static string BloggerServiceScope = BloggerService.Scope.Blogger;
        public static char LabelDelimiter = ',';

        /// <summary>
        /// Maximum number of results the Google Blogger v3 API will return in one request.
        /// </summary>
        public static int MaxResultsPerRequest = 500;

        public static async Task<UserCredential> GetOAuth2AuthorizationAsync(string blogId, CancellationToken taskCancellationToken)
        {
            // This async task will either find cached credentials in the IDataStore provided, or it will pop open a 
            // browser window and prompt the user for permissions and then write those permissions to the IDataStore.
            return await GoogleWebAuthorizationBroker.AuthorizeAsync(
                //GoogleClientSecrets.Load(await GetClientSecretsStream()).Secrets,
                new Uri("ms-appx:///blogger_client_id.json"),
                new List<string>() {BloggerServiceScope, PicasaServiceScope},
                blogId,
                //"user",
                taskCancellationToken);
                //GetCredentialsDataStoreForBlog(blogId));
        }

        private static async Task<Stream> GetClientSecretsStream()
        {

            // The secrets file is automatically generated at build time by OpenLiveWriter.BlogClient.csproj. It 
            // contains just a client ID and client secret, which are pulled from the user's environment variables.
            var storageFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///blogger_client_id.json"));
            return await storageFile.OpenStreamForReadAsync();

        }

        //private static IDataStore GetCredentialsDataStoreForBlog(string blogId)
        //{
        //    // The Google APIs will automatically store the OAuth2 tokens in the given path.
        //    var folderPath = Path.Combine(ApplicationEnvironment.ApplicationDataDirectory, "GoogleBloggerv3");
        //    return new FileDataStore(folderPath, true);
        //}

        private static BlogPost ConvertToBlogPost(Page page)
        {
            return new BlogPost()
            {
                Title = page.Title,
                Id = page.Id,
                Permalink = page.Url,
                Contents = page.Content,
                DatePublished = page.Published.Value,
                //Keywords = string.Join(LabelDelimiter, page.Labels)
            };
        }

        private static BlogPost ConvertToBlogPost(Post post)
        {
            return new BlogPost()
            {
                Title = post.Title,
                Id = post.Id,
                Permalink = post.Url,
                Contents = post.Content,
                DatePublished = post.Published.Value,
                Categories = post.Labels?.Select(x => new BlogPostCategory(x)).ToArray() ?? new BlogPostCategory[0],
                IsPublished = post.Status != "DRAFT"
                
            };
        }

        private static Page ConvertToGoogleBloggerPage(BlogPost page)
        {
            return new Page()
            {
                Content = page.Contents,
                // TODO:OLW - DatePublishedOverride didn't work quite right. Either the date published override was off by several hours, 
                // needs to be normalized to UTC or the Blogger website thinks I'm in the wrong time zone.
                Published = page.HasDatePublishedOverride ? page?.DatePublishedOverride : null,
                Title = page.Title,
            };
        }

        private static Post ConvertToGoogleBloggerPost(BlogPost post)
        {
            var labels = post.Categories?.Select(x => x.Name).ToList();
            labels?.AddRange(post.NewCategories?.Select(x => x.Name) ?? new List<string>());
            return new Post()
            {
                Content = post.Contents,
                Labels = labels ?? new List<string>(),
                // TODO:OLW - DatePublishedOverride didn't work quite right. Either the date published override was off by several hours, 
                // needs to be normalized to UTC or the Blogger website thinks I'm in the wrong time zone.
                Published = post.HasDatePublishedOverride ? post?.DatePublishedOverride : null,
                Title = post.Title,
            };
        }

        private static PageInfo ConvertToPageInfo(Page page)
        {
            // Google Blogger doesn't support parent/child pages, so we pass string.Empty.
            return new PageInfo(page.Id, page.Title, page.Published.GetValueOrDefault(DateTime.Now), string.Empty);
        }

        private const int MaxRetries = 5;

        private const string ENTRY_CONTENT_TYPE = "application/atom+xml;type=entry";
        private const string XHTML_NS = "http://www.w3.org/1999/xhtml";
        private const string FEATURES_NS = "http://purl.org/atompub/features/1.0";
        private const string MEDIA_NS = "http://search.yahoo.com/mrss/";
        private const string LIVE_NS = "http://api.live.com/schemas";

        private static readonly Namespace atomNS = new Namespace(AtomProtocolVersion.V10DraftBlogger.NamespaceUri, "atom");
        private static readonly Namespace pubNS = new Namespace(AtomProtocolVersion.V10DraftBlogger.PubNamespaceUri, "app");

        private IBlogClientOptions _clientOptions;
        private XmlNamespaceManager _nsMgr;

        public GoogleBloggerv3Client(Uri postApiUrl, IBlogCredentialsAccessor credentials)
            : base(credentials)
        {
            // configure client options
            BlogClientOptions clientOptions = new BlogClientOptions();
            clientOptions.SupportsCategories = true;
            clientOptions.SupportsMultipleCategories = true;
            clientOptions.SupportsNewCategories = true;
            clientOptions.SupportsCustomDate = true;
            clientOptions.SupportsExcerpt = false;
            clientOptions.SupportsSlug = false;
            clientOptions.SupportsFileUpload = true;
            clientOptions.SupportsKeywords = false;
            clientOptions.SupportsGetKeywords = false;
            clientOptions.SupportsPages = true;
            clientOptions.SupportsExtendedEntries = true;
            _clientOptions = clientOptions;

            _nsMgr = new XmlNamespaceManager(new NameTable());
            _nsMgr.AddNamespace(atomNS.Prefix, atomNS.Uri);
            _nsMgr.AddNamespace(pubNS.Prefix, pubNS.Uri);
            _nsMgr.AddNamespace(AtomClient.xhtmlNS.Prefix, AtomClient.xhtmlNS.Uri);
            _nsMgr.AddNamespace(AtomClient.featuresNS.Prefix, AtomClient.featuresNS.Uri);
            _nsMgr.AddNamespace(AtomClient.mediaNS.Prefix, AtomClient.mediaNS.Uri);
            _nsMgr.AddNamespace(AtomClient.liveNS.Prefix, AtomClient.liveNS.Uri);
        }

        public IBlogClientOptions Options
        {
            get
            {
                return _clientOptions;
            }
        }

        public bool IsSecure
        {
            get { return true; }
        }

        private async Task<BloggerService> GetService()
        {
            TransientCredentials transientCredentials = await Login();
            return new BloggerService(new BaseClientService.Initializer()
            {
                
                HttpClientInitializer = (UserCredential)transientCredentials.Token,
                ApplicationName = string.Format(CultureInfo.InvariantCulture, "{0} {1}", ApplicationEnvironment.ProductName, ApplicationEnvironment.ProductVersion),
            });
        }

        private bool IsValidToken(TokenResponse token)
        {
            // If the token is expired but we have a non-null RefreshToken, we can assume the token will be 
            // automatically refreshed when we query Google Blogger and is therefore valid.
            return token != null && (!token.IsExpired(SystemClock.Default) || token.RefreshToken != null);
        }

        protected async override Task<TransientCredentials> Login()
        {
            var transientCredentials = Credentials.TransientCredentials as TransientCredentials ?? 
                new TransientCredentials(Credentials.Username, Credentials.Password, null);
            await VerifyAndRefreshCredentials(transientCredentials);
            Credentials.TransientCredentials = transientCredentials;
            return transientCredentials;
        }

        protected override Task VerifyCredentials(TransientCredentials tc)
        {
            return VerifyAndRefreshCredentials(tc);
        }

        private async Task VerifyAndRefreshCredentials(TransientCredentials tc)
        {
            var userCredential = tc.Token as UserCredential;
            var token = userCredential?.Token;

            if (IsValidToken(token))
            {
                // We already have a valid OAuth token.
                return;
            }

            if (userCredential == null)
            {
                // Attempt to load a cached OAuth token.
                var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecretsStream = await GetClientSecretsStream(),
                    //DataStore = GetCredentialsDataStoreForBlog(tc.Username),
                    Scopes = new List<string>() { BloggerServiceScope, PicasaServiceScope },
                });

                var loadTokenTaskResult = await flow.LoadTokenAsync(tc.Username, CancellationToken.None);
                // We were able re-create the user credentials from the cache.
                userCredential = new UserCredential(flow, tc.Username, loadTokenTaskResult);
                token = loadTokenTaskResult;
                
            }

            if (!IsValidToken(token))
            {
                // The token is invalid, so we need to login again. This likely includes popping out a new browser window.
                //if (BlogClientUIContext.SilentModeForCurrentThread)
                //{
                //    // If we're in silent mode where prompting isn't allowed, throw the verification exception
                //    throw new BlogClientAuthenticationException(String.Empty, String.Empty);
                //}

                // Start an OAuth flow to renew the credentials.
                var authorizationTaskResult = await GetOAuth2AuthorizationAsync(tc.Username, CancellationToken.None);
                
                userCredential = authorizationTaskResult;
                token = userCredential?.Token;
                
            }

            if (!IsValidToken(token))
            {
                // The token is still invalid after all of our attempts to refresh it. The user did not complete the 
                // authorization flow, so we interpret that as a cancellation.
                throw new BlogClientOperationCancelledException();
            }

            // Stash the valid user credentials.
            tc.Token = userCredential;
        }

        public class LocalCredentialStore : IDataStore
        {
            public Task StoreAsync<T>(string key, T value)
            {
                throw new NotImplementedException();
            }

            public Task DeleteAsync<T>(string key)
            {
                throw new NotImplementedException();
            }

            public Task<T> GetAsync<T>(string key)
            {
                throw new NotImplementedException();
            }

            public Task ClearAsync()
            {
                throw new NotImplementedException();
            }
        }

        private async Task RefreshAccessToken(TransientCredentials transientCredentials)
        {
            // Using the BloggerService automatically refreshes the access token, but we call the Picasa endpoint 
            // directly and therefore need to force refresh the access token on occasion.
            var userCredential = transientCredentials.Token as UserCredential;
            var refreshTokenAsync = userCredential?.RefreshTokenAsync(CancellationToken.None);
            if (refreshTokenAsync != null)
                await refreshTokenAsync;
        }

        private async Task<HttpAsyncRequestFilter> CreateAuthorizationFilter()
        {
            var transientCredentials = await Login();
            var userCredential = (UserCredential)transientCredentials.Token;
            var accessToken = userCredential.Token.AccessToken;

            return request =>
            {
                request.Headers["Authorization"] = string.Format(CultureInfo.InvariantCulture, "Bearer {0}", accessToken);
                return Task.FromResult(true);
            };

        }

        public void OverrideOptions(IBlogClientOptions newClientOptions)
        {
            _clientOptions = newClientOptions;
        }

        public async Task<BlogInfo[]> GetUsersBlogsAsync()
        {
            var blogList = await (await GetService()).Blogs.ListByUser("self").ExecuteAsync();
            return blogList.Items?.Select(b => new BlogInfo(b.Id, b.Name, b.Url)).ToArray() ?? new BlogInfo[0];
        }

        private const string CategoriesEndPoint = "/feeds/posts/summary?alt=json&max-results=0";
        public async Task<BlogPostCategory[]> GetCategories(string blogId)
        {
            var categories = new BlogPostCategory[0];
            var blog = await (await GetService()).Blogs.Get(blogId).ExecuteAsync();

            if (blog != null)
            {
                var categoriesUrl = string.Concat(blog.Url, CategoriesEndPoint);

                var response = await SendAuthenticatedHttpRequest(categoriesUrl, 30, await CreateAuthorizationFilter());
                if (response != null)
                {
                    using (var reader = new StreamReader((await response.Content.ReadAsInputStreamAsync()).AsStreamForRead()))
                    {
                        var json = reader.ReadToEnd();
                        var item = JsonConvert.DeserializeObject<CategoryResponse>(json);
                        var cats = item?.Feed?.CategoryArray?.Select(x => new BlogPostCategory(x.Term));
                        categories = cats?.ToArray() ?? new BlogPostCategory[0];
                    }
                }
            }

            return categories;
        }

        public Task<BlogPostKeyword[]> GetKeywords(string blogId)
        {
            // Google Blogger does not support get labels
            return Task.FromResult(new BlogPostKeyword[] { });
        }

        private async Task<PostList> ListRecentPosts(string blogId, int maxPosts, DateTime? now, PostsResource.ListRequest.StatusEnum status, PostList previousPage)
        {
            if (previousPage != null && string.IsNullOrWhiteSpace(previousPage.NextPageToken))
            {
                // The previous page was also the last page, so do nothing and return an empty list.
                return new PostList();
            }

            var recentPostsRequest = (await GetService()).Posts.List(blogId);
            if (now.HasValue)
            {
                recentPostsRequest.EndDate = now.Value;
            }
            recentPostsRequest.FetchImages = false;
            recentPostsRequest.MaxResults = maxPosts;
            recentPostsRequest.OrderBy = PostsResource.ListRequest.OrderByEnum.Published;
            recentPostsRequest.Status = status;
            recentPostsRequest.PageToken = previousPage?.NextPageToken;

            return await recentPostsRequest.ExecuteAsync();
        }

        public async Task<BlogPost[]> GetRecentPostsAsync(string blogId, int maxPosts, bool includeCategories, DateTime? now)
        {
            // Blogger requires separate API calls to get drafts vs. live vs. scheduled posts. We aggregate each 
            // type of post separately.
            IList<Post> draftRecentPosts = new List<Post>();
            IList<Post> liveRecentPosts = new List<Post>();
            IList<Post> scheduledRecentPosts = new List<Post>();
            IEnumerable<Post> allPosts = new List<Post>();

            // We keep around the PostList returned by each request to support pagination.
            PostList draftRecentPostsList = null;
            PostList liveRecentPostsList = null;
            PostList scheduledRecentPostsList = null;

            // Google has a per-request results limit on their API.
            var maxResultsPerRequest = Math.Min(maxPosts, MaxResultsPerRequest);

            // We break out of the following loop depending on which one of these two cases we hit: 
            //   (a) the number of all blog posts ever posted to this blog is greater than maxPosts, so eventually 
            //       allPosts.count() will exceed maxPosts and we can stop making requests.
            //   (b) the number of all blog posts ever posted to this blog is less than maxPosts, so eventually our 
            //       calls to ListRecentPosts() will return 0 results and we need to stop making requests.
            do
            {
                draftRecentPostsList = await ListRecentPosts(blogId, maxResultsPerRequest, now, PostsResource.ListRequest.StatusEnum.Draft, draftRecentPostsList);
                liveRecentPostsList = await ListRecentPosts(blogId, maxResultsPerRequest, now, PostsResource.ListRequest.StatusEnum.Live, liveRecentPostsList);
                scheduledRecentPostsList = await ListRecentPosts(blogId, maxResultsPerRequest, now, PostsResource.ListRequest.StatusEnum.Scheduled, scheduledRecentPostsList);

                draftRecentPosts = draftRecentPostsList?.Items ?? new List<Post>();
                liveRecentPosts = liveRecentPostsList?.Items ?? new List<Post>();
                scheduledRecentPosts = scheduledRecentPostsList?.Items ?? new List<Post>();
                allPosts = allPosts.Concat(draftRecentPosts).Concat(liveRecentPosts).Concat(scheduledRecentPosts);

            } while (allPosts.Count() < maxPosts && (draftRecentPosts.Count > 0 || liveRecentPosts.Count > 0 || scheduledRecentPosts.Count > 0));
            
            return allPosts
                .OrderByDescending(p => p.Published)
                .Take(maxPosts)
                .Select(ConvertToBlogPost)
                .ToArray() ?? new BlogPost[0];
        }

        public async Task<string> NewPost(string blogId, BlogPost post, INewCategoryContext newCategoryContext, bool publish, PostResult postResult)
        {
            // The remote post is only meant to be used for blogs that use the Atom protocol.
            postResult.AtomRemotePost = null;

            if (!publish && !Options.SupportsPostAsDraft)
            {
                Debug.Fail("Post to draft not supported on this provider");
                throw new BlogClientPostAsDraftUnsupportedException();
            }

            var bloggerPost = ConvertToGoogleBloggerPost(post);
            var newPostRequest = (await GetService()).Posts.Insert(bloggerPost, blogId);
            newPostRequest.IsDraft = !publish;

            var newPost = await newPostRequest.ExecuteAsync();
            postResult.ETag = newPost.ETag;
            return newPost.Id;
        }

        public async Task<bool> EditPost(string blogId, BlogPost post, INewCategoryContext newCategoryContext, bool publish, EditPostResult editPostResult)
        {
            // The remote post is only meant to be used for blogs that use the Atom protocol.
            editPostResult.remotePost = null;

            if (!publish && !Options.SupportsPostAsDraft)
            {
                Debug.Fail("Post to draft not supported on this provider");
                throw new BlogClientPostAsDraftUnsupportedException();
            }

            var bloggerPost = ConvertToGoogleBloggerPost(post);
            var updatePostRequest = (await GetService()).Posts.Update(bloggerPost, blogId, post.Id);
            updatePostRequest.Publish = publish;

            var updatedPost = await updatePostRequest.ExecuteAsync();
            editPostResult.etag = updatedPost.ETag;
            return true;
        }

        public async Task<BlogPost> GetPost(string blogId, string postId)
        {
            var getPostRequest = (await GetService()).Posts.Get(blogId, postId);
            getPostRequest.View = PostsResource.GetRequest.ViewEnum.AUTHOR;
            return ConvertToBlogPost(await getPostRequest.ExecuteAsync());
        }

        public async Task DeletePost(string blogId, string postId, bool publish)
        {
            var deletePostRequest = (await GetService()).Posts.Delete(blogId, postId);
            await deletePostRequest.ExecuteAsync();
        }

        public async Task<BlogPost> GetPage(string blogId, string pageId)
        {
            var getPageRequest = (await GetService()).Pages.Get(blogId, pageId);
            getPageRequest.View = PagesResource.GetRequest.ViewEnum.AUTHOR;
            return ConvertToBlogPost(await getPageRequest.ExecuteAsync());
        }

        private async Task<PageList> ListPages(string blogId, int? maxPages, PagesResource.ListRequest.StatusEnum status, PageList previousPage)
        {
            if (previousPage != null && string.IsNullOrWhiteSpace(previousPage.NextPageToken))
            {
                // The previous page was also the last page, so do nothing and return an empty list.
                return new PageList();
            }

            var getPagesRequest = (await GetService()).Pages.List(blogId);
            if (maxPages.HasValue)
            {
                // Google has a per-request results limit on their API.
                getPagesRequest.MaxResults = Math.Min(maxPages.Value, MaxResultsPerRequest);
            }
            getPagesRequest.Status = status;
            return await getPagesRequest.ExecuteAsync();
        }

        private async Task<IEnumerable<Page>> ListAllPages(string blogId, int? maxPages)
        {
            // Blogger requires separate API calls to get drafts vs. live vs. scheduled posts. We aggregate each 
            // type of post separately.
            IList<Page> draftPages = new List<Page>();
            IList<Page> livePages = new List<Page>();
            IEnumerable<Page> allPages = new List<Page>();

            // We keep around the PageList returned by each request to support pagination.
            PageList draftPagesList = null;
            PageList livePagesList = null;
            
            // We break out of the following loop depending on which one of these two cases we hit: 
            //   (a) the number of all blog pages ever posted to this blog is greater than maxPages, so eventually 
            //       allPages.count() will exceed maxPages and we can stop making requests.
            //   (b) the number of all blog pages ever posted to this blog is less than maxPages, so eventually our 
            //       calls to ListPages() will return 0 results and we need to stop making requests.
            do
            {
                draftPagesList = await ListPages(blogId, maxPages, PagesResource.ListRequest.StatusEnum.Draft, draftPagesList);
                livePagesList = await ListPages(blogId, maxPages, PagesResource.ListRequest.StatusEnum.Live, livePagesList);

                draftPages = draftPagesList?.Items ?? new List<Page>();
                livePages = livePagesList?.Items ?? new List<Page>();
                allPages = allPages.Concat(draftPages).Concat(livePages);

            } while (allPages.Count() < maxPages && (draftPages.Count > 0 || livePages.Count > 0));

            return allPages;
        }

        public async Task<PageInfo[]> GetPageList(string blogId)
        {
            return (await ListAllPages(blogId, null))
                .OrderByDescending(p => p.Published)
                .Select(ConvertToPageInfo)
                .ToArray() ?? new PageInfo[0];
        }

        public async Task<BlogPost[]> GetPages(string blogId, int maxPages)
        {
            return (await ListAllPages(blogId, maxPages))
                .OrderByDescending(p => p.Published)
                .Select(ConvertToBlogPost)
                .Take(maxPages)
                .ToArray() ?? new BlogPost[0];
        }

        public async Task<string> NewPage(string blogId, BlogPost page, bool publish, string etag, Windows.Data.Xml.Dom.XmlDocument remotePost)
        {
            // The remote post is only meant to be used for blogs that use the Atom protocol.
            remotePost = null;

            if (!publish && !Options.SupportsPostAsDraft)
            {
                Debug.Fail("Post to draft not supported on this provider");
                throw new BlogClientPostAsDraftUnsupportedException();
            }

            var bloggerPage = ConvertToGoogleBloggerPage(page);
            var newPageRequest = (await GetService()).Pages.Insert(bloggerPage, blogId);
            newPageRequest.IsDraft = !publish;

            var newPage = await newPageRequest.ExecuteAsync();
            etag = newPage.ETag;
            return newPage.Id;
        }

        public async Task<bool> EditPage(string blogId, BlogPost page, bool publish, string etag, Windows.Data.Xml.Dom.XmlDocument remotePost)
        {
            // The remote post is only meant to be used for blogs that use the Atom protocol.
            remotePost = null;

            if (!publish && !Options.SupportsPostAsDraft)
            {
                Debug.Fail("Post to draft not supported on this provider");
                throw new BlogClientPostAsDraftUnsupportedException();
            }

            var bloggerPage = ConvertToGoogleBloggerPage(page);
            var updatePostRequest = (await GetService()).Pages.Update(bloggerPage, blogId, page.Id);
            updatePostRequest.Publish = publish;

            var updatedPage = await updatePostRequest.ExecuteAsync();
            etag = updatedPage.ETag;
            return true;
        }

        public async Task DeletePage(string blogId, string pageId)
        {
            var deletePostRequest = (await GetService()).Pages.Delete(blogId, pageId);
            await deletePostRequest.ExecuteAsync();
        }

        public Task<AuthorInfo[]> GetAuthors(string blogId)
        {
            throw new NotImplementedException();
        }

        public bool? DoesFileNeedUpload(IFileUploadContext uploadContext)
        {
            return null;
        }

        public class PicasaPostImage
        {
           public string srcUrl { get; set; }
           public string editUri { get; set; }
        }

        public async Task<string> DoBeforePublishUploadWork(IFileUploadContext uploadContext)
        {
            string albumName = ApplicationEnvironment.ProductName;

            string fileName = uploadContext.PreferredFileName;
            var fileStream = uploadContext.GetContents();

            if (Options.FileUploadNameFormat != null && Options.FileUploadNameFormat.Length > 0)
            {
                string formattedFileName = uploadContext.FormatFileName(uploadContext.PreferredFileName);
                string[] chunks = StringHelper.Reverse(formattedFileName).Split(new char[] { '/' }, 2);
                if (chunks.Length == 2)
                    albumName = StringHelper.Reverse(chunks[1]);
            }

            string EDIT_MEDIA_LINK = "EditMediaLink";

            var postImage = new PicasaPostImage();
            //postImage.editUri = uploadContext.Settings.GetString(EDIT_MEDIA_LINK, null);

            if (postImage.editUri == null || postImage.editUri.Length == 0)
            {
                await PostNewImage(albumName, fileStream, fileName, postImage);
            }
            else
            {
                try
                {
                    await UpdateImage(postImage.editUri, fileStream, fileName, postImage);
                }
                catch (Exception e)
                {
                    Debug.Fail(e.ToString());
                    if (e is WebException)
                        HttpRequestHelper.LogException((WebException)e);

                    bool success = false;
                    postImage.srcUrl = null; // compiler complains without this line
                    try
                    {
                        // couldn't update existing image? try posting a new one
                        await PostNewImage(albumName, fileStream, fileName, postImage);
                        success = true;
                    }
                    catch
                    {
                    }
                    if (!success)
                        throw;  // rethrow the exception from the update, not the post
                }
            }
            //uploadContext.Settings.SetString(EDIT_MEDIA_LINK, postImage.editUri);

            postImage.srcUrl = await PicasaRefererBlockingWorkaround(uploadContext.BlogId, uploadContext.Role, postImage.srcUrl);

            return postImage.srcUrl;
        }

        /// <summary>
        /// "It looks like the problem with the inline image is due to referrer checking.
        /// The thumbnail image being used is protected for display only on certain domains.
        /// These domains include *.blogspot.com and *.google.com.  This user is using a
        /// feature in Blogger which allows him to display his blog directly on his own
        /// domain, which will not pass the referrer checking.
        ///
        /// "The maximum size of a thumbnail image that can be displayed on non-*.blogspot.com
        /// domains is 800px. (blogs don't actually appear at *.google.com).  However, if you
        /// request a 800px thumbnail, and the image is less than 800px for the maximum
        /// dimension, then the original image will be returned without the referrer
        /// restrictions.  That sounds like it will work for you, so feel free to give it a
        /// shot and let me know if you have any further questions or problems."
        ///   -- Anonymous Google Employee
        /// </summary>
        private async Task<string> PicasaRefererBlockingWorkaround(string blogId, FileUploadRole role, string srcUrl)
        {
            if (role == FileUploadRole.LinkedImage && Options.UsePicasaS1600h)
            {
                try
                {
                    int lastSlash = srcUrl.LastIndexOf('/');
                    string srcUrl2 = srcUrl.Substring(0, lastSlash)
                                     + "/s1600-h"
                                     + srcUrl.Substring(lastSlash);
                    var req = HttpRequestHelper.CreateHttpWebRequest(srcUrl2, true);
                    req.Method = HttpMethod.Head;
                    await req.Content.BufferAllAsync();
                    srcUrl = srcUrl2;
                    return srcUrl;
                }
                catch (WebException we)
                {
                    Debug.Fail("Picasa s1600-h hack failed: " + we.ToString());
                }
            }

            try
            {
                srcUrl += ((srcUrl.IndexOf('?') >= 0) ? "&" : "?") + "imgmax=800";
            }
            catch (Exception ex)
            {
                Debug.Fail("Unexpected error while doing Picasa upload: " + ex.ToString());
            }

            return srcUrl;
        }

        public Task DoAfterPublishUploadWork(IFileUploadContext uploadContext)
        {
            // Nothing to do.
            return Task.FromResult(true);
        }

        public Task<string> AddCategory(string blogId, BlogPostCategory category)
        {
            throw new BlogClientMethodUnsupportedException("AddCategory");
        }

        public Task<BlogPostCategory[]> SuggestCategories(string blogId, string partialCategoryName)
        {
            throw new BlogClientMethodUnsupportedException("SuggestCategories");
        }

        public async Task<HttpResponseMessage> SendAuthenticatedHttpRequest(string requestUri, int timeoutMs, HttpAsyncRequestFilter filter)
        {
            return await BlogClientHelper.SendAuthenticatedHttpRequest(requestUri, filter, await CreateAuthorizationFilter());
        }

        public BlogInfo[] GetImageEndpoints()
        {
            throw new NotImplementedException();
        }

        #region Picasa image uploading - stolen from BloggerAtomClient

        public async Task<string> GetBlogImagesAlbum(string albumName)
        {
            const string FEED_REL = "http://schemas.google.com/g/2005#feed";
            const string GPHOTO_NS_URI = "http://schemas.google.com/photos/2007";
            
            Uri picasaUri = new Uri("https://picasaweb.google.com/data/feed/api/user/default");

            try
            {
                Uri reqUri = picasaUri;
                var albumListDoc = await AtomClient.xmlRestRequestHelper.Get(await CreateAuthorizationFilter(), new XmlRestRequestHelper.XmlRequestResult() {uri = reqUri}, "kind", "album");
                foreach (var entryEl in albumListDoc.SelectNodesNS(@"/atom:feed/atom:entry", _nsMgr.ToNSMethodFormat()))
                {
                    var titleNode = entryEl.SelectSingleNodeNS(@"atom:title", _nsMgr.ToNSMethodFormat());
                    if (titleNode != null)
                    {
                        string titleText = AtomProtocolVersion.V10DraftBlogger.TextNodeToPlaintext(titleNode);
                        if (titleText == albumName)
                        {
                            XmlNamespaceManager nsMgr2 = new XmlNamespaceManager(new NameTable());
                            nsMgr2.AddNamespace("gphoto", "http://schemas.google.com/photos/2007");
                            var numPhotosRemainingNode = entryEl.SelectSingleNodeNS("gphoto:numphotosremaining/text()", nsMgr2.ToNSMethodFormat());
                            if (numPhotosRemainingNode != null)
                            {
                                int numPhotosRemaining;
                                if (int.TryParse((string) numPhotosRemainingNode.NodeValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out numPhotosRemaining))
                                {
                                    if (numPhotosRemaining < 1)
                                        continue;
                                }
                            }
                            string selfHref = AtomEntry.GetLink(entryEl, _nsMgr, FEED_REL, "application/atom+xml", null, reqUri);
                            if (selfHref.Length > 1)
                                return selfHref;
                        }
                    }
                }
            }
            catch (WebException we)
            {
                HttpWebResponse httpWebResponse = we.Response as HttpWebResponse;
                if (httpWebResponse != null)
                {
                    HttpRequestHelper.DumpResponse(httpWebResponse);
                    if (httpWebResponse.StatusCode == HttpStatusCode.NotFound)
                    {
                        throw new BlogClientOperationCancelledException();
                    }
                }
                throw;
            }

            throw new Exception("Could not find a Google Photos album called 'NetWriter'. Create an Album in Google Photos called 'NetWriter' and try again.");

            //var newDoc = new Windows.Data.Xml.Dom.XmlDocument();
            //var newEntryEl = newDoc.CreateElementNS(AtomProtocolVersion.V10DraftBlogger.NamespaceUri, "atom:entry");
            //newDoc.AppendChild(newEntryEl);

            //var newTitleEl = newDoc.CreateElementNS(AtomProtocolVersion.V10DraftBlogger.NamespaceUri, "atom:title");
            //newTitleEl.SetAttribute("type", "text");
            //newTitleEl.InnerText = albumName;
            //newEntryEl.AppendChild(newTitleEl);

            //var newSummaryEl = newDoc.CreateElementNS(AtomProtocolVersion.V10DraftBlogger.NamespaceUri, "atom:summary");
            //newSummaryEl.SetAttribute("type", "text");
            //newSummaryEl.InnerText = "This album is used to store pictures from blog posts published by Net Writer.";
            //newEntryEl.AppendChild(newSummaryEl);

            //var newAccessEl = newDoc.CreateElementNS(GPHOTO_NS_URI, "gphoto:access");
            //newAccessEl.InnerText = "private";
            //newEntryEl.AppendChild(newAccessEl);
            
            //var newCategoryEl = newDoc.CreateElementNS(AtomProtocolVersion.V10DraftBlogger.NamespaceUri, "atom:category");
            //newCategoryEl.SetAttribute("scheme", "http://schemas.google.com/g/2005#kind");
            //newCategoryEl.SetAttribute("term", "http://schemas.google.com/photos/2007#album");
            //newEntryEl.AppendChild(newCategoryEl);

            
            //XmlRestRequestHelper.XmlRequestResult result = new XmlRestRequestHelper.XmlRequestResult() {uri = picasaUri};
            //var newAlbumResult = await AtomClient.xmlRestRequestHelper.Post(await CreateAuthorizationFilter(), "application/atom+xml", newDoc, null, result);
            //var newAlbumResultEntryEl = newAlbumResult.SelectSingleNodeNS("/atom:entry", _nsMgr.ToNSMethodFormat());
            //Debug.Assert(newAlbumResultEntryEl != null);
            //return AtomEntry.GetLink(newAlbumResultEntryEl, _nsMgr, FEED_REL, "application/atom+xml", null, result.uri);
        }

        private void ShowPicasaSignupPrompt(object sender, EventArgs e)
        {
            //if (DisplayMessage.Show(MessageId.PicasawebSignup) == DialogResult.Yes)
            //{
            //    ShellHelper.LaunchUrl("http://picasaweb.google.com");
            //}
        }

        private async Task PostNewImage(string albumName, Stream fileStream, string filename, PicasaPostImage postImage)
        {
            for (int retry = 0; retry < MaxRetries; retry++)
            {
                var transientCredentials = await Login();
                try
                {
                    string albumUrl = await GetBlogImagesAlbum(albumName);
                    var response = await RedirectHelper.GetResponse(albumUrl, new UploadFileRequestFactory(this, fileStream,  filename, "POST").Create);

                    if (!response.IsSuccessStatusCode)
                    {

                        if (retry < MaxRetries - 1 &&
                           response.StatusCode == Windows.Web.Http.HttpStatusCode.Forbidden)
                        {
                            // HTTP 403 Forbidden means our OAuth access token is not valid.
                            await RefreshAccessToken(transientCredentials);
                            continue;
                        }
                    }

                    var mediaEntry = await response.Content.ReadAsStringAsync();
                    
                    await ParseMediaEntry(mediaEntry, postImage);
                    return;
                    
                }
                catch (WebException we)
                {
                    if (retry < MaxRetries - 1 &&
                        we.Response as HttpWebResponse != null &&
                        ((HttpWebResponse)we.Response).StatusCode == HttpStatusCode.Forbidden)
                    {
                        // HTTP 403 Forbidden means our OAuth access token is not valid.
                        await RefreshAccessToken(transientCredentials);
                    }
                    else
                    {
                        throw;
                    }
                }
            }

            Debug.Fail("Should never get here");
            throw new Exception("Should never get here");
        }

        private async Task UpdateImage(string editUri, Stream fileStream, string filename, PicasaPostImage postImage)
        {
            for (int retry = 0; retry < MaxRetries; retry++)
            {
                var transientCredentials = await Login();
                HttpResponseMessage response;
                bool conflict = false;
                try
                {
                    response = await RedirectHelper.GetResponse(editUri, new RedirectHelper.RequestFactory(new UploadFileRequestFactory(this, fileStream, filename, "PUT").Create));

                    if (!response.IsSuccessStatusCode)
                    {
                        if (retry < MaxRetries - 1)
                        {
                            if (response.StatusCode == Windows.Web.Http.HttpStatusCode.Conflict)
                            {
                                //response = we.Response;
                                conflict = true;
                            }
                            else if (response.StatusCode == Windows.Web.Http.HttpStatusCode.Forbidden)
                            {
                                // HTTP 403 Forbidden means our OAuth access token is not valid.
                                await RefreshAccessToken(transientCredentials);
                                continue;
                            }
                        }
                    }

                 
                }
                catch (WebException we)
                {
                    if (retry < MaxRetries - 1 && 
                        we.Response as HttpWebResponse != null)
                    {
                        if (((HttpWebResponse)we.Response).StatusCode == HttpStatusCode.Conflict)
                        {
                            //response = we.Response;
                            conflict = true;
                        }
                        else if (((HttpWebResponse)we.Response).StatusCode == HttpStatusCode.Forbidden)
                        {
                            // HTTP 403 Forbidden means our OAuth access token is not valid.
                            await RefreshAccessToken(transientCredentials);
                            continue;
                        }
                    }

                    throw;
                }

                var mediaEntry = await response.Content.ReadAsStringAsync();
                {
                    await ParseMediaEntry(mediaEntry, postImage);
                }

                if (!conflict)
                {
                    return; // success!
                }

            }

            Debug.Fail("Should never get here");
            throw new Exception("Should never get here");
        }

        private async Task ParseMediaEntry(string mediaEntryString, PicasaPostImage postImage)
        {
            postImage.srcUrl = null;

            // First try <content src>
            var xmlDoc = new Windows.Data.Xml.Dom.XmlDocument();

            xmlDoc.LoadXml(mediaEntryString);
            var contentEl = xmlDoc.SelectSingleNodeNS("/atom:entry/atom:content", _nsMgr.ToNSMethodFormat());
            if (contentEl != null)
                postImage.srcUrl = XmlHelper.GetUrl(contentEl, "@src", _nsMgr, null);

            // Then try media RSS
            if (postImage.srcUrl == null || postImage.srcUrl.Length == 0)
            {
                contentEl = xmlDoc.SelectSingleNodeNS("/atom:entry/media:group/media:content[@medium='image']", _nsMgr.ToNSMethodFormat());
                if (contentEl == null)
                    throw new ArgumentException("Picasa photo entry was missing content element");
                postImage.srcUrl = XmlHelper.GetUrl(contentEl, "@url", _nsMgr, null);
            }

            postImage.editUri = AtomEntry.GetLink(xmlDoc.SelectSingleNodeNS("/atom:entry", _nsMgr.ToNSMethodFormat()), _nsMgr, "edit-media", null, null, null);
        }

        private class UploadFileRequestFactory
        {
            private readonly GoogleBloggerv3Client _parent;
            private readonly string _filename;
            private readonly string _method;
            private readonly Stream _fileStream;

            public UploadFileRequestFactory(GoogleBloggerv3Client parent, Stream fileStream, string filename, string method)
            {
                _parent = parent;
                _filename = filename;
                _method = method;
                _fileStream = fileStream;
            }

            public async Task<HttpRequestMessage> Create(string uri)
            {
                // TODO: choose rational timeout values
                var request = HttpRequestHelper.CreateHttpWebRequest(uri, false);

                await (await _parent.CreateAuthorizationFilter()).Invoke(request);

                //request.Headers.

                //request.ContentType = MimeHelper.GetContentType(Path.GetExtension(_filename));
                try
                {
                    request.Headers.Add("Slug", Path.GetFileNameWithoutExtension(_filename));
                }
                catch (ArgumentException)
                {
                    request.Headers.Add("Slug", "Image");
                }

                request.Method = new HttpMethod(_method);

                _fileStream.Seek(0, SeekOrigin.Begin);
                var streamContent = new HttpStreamContent(_fileStream.AsInputStream());
                streamContent.Headers.ContentLength = (ulong?) _fileStream.Length;
                streamContent.Headers.ContentType = new HttpMediaTypeHeaderValue(MimeHelper.GetContentType(Path.GetExtension(_filename)));

                request.Content = streamContent;

                //using (Stream s = request.GetRequestStream())
                //{
                //    using (Stream inS = new FileStream(_filename, FileMode.Open, FileAccess.Read, FileShare.Read))
                //    {
                //        StreamHelper.Transfer(inS, s);
                //    }
                //}

                return request;
            }
        }

        #endregion

        public class Category
        {
            [JsonProperty("term")]
            public string Term { get; set; }
        }

        public class Feed
        {
            [JsonProperty("category")]
            public Category[] CategoryArray { get; set; }
        }

        public class CategoryResponse
        {
            [JsonProperty("feed")]
            public Feed Feed { get; set; }
        }
    }
}
