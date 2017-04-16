// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Net;
using System.IO;
using System.Threading.Tasks;
using Windows.Data.Xml.Dom;
using Windows.Web.Http;
using BlogWriter.OpenLiveWriter;
using OpenLiveWriter.Api;
using OpenLiveWriter.BlogClient;
using OpenLiveWriter.BlogClient.Clients;
using OpenLiveWriter.CoreServices;

namespace OpenLiveWriter.Extensibility.BlogClient
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class BlogClientAttribute : Attribute
    {
        public BlogClientAttribute(string typeName, string protocolName)
        {
            _typeName = typeName;
            _protocolName = protocolName;
        }

        public string TypeName
        {
            get { return _typeName; }
        }

        /// <summary>
        /// The name of the standard protocol that this blog client
        /// adheres to. This is used to let wlwmanifest.xml provide
        /// API-specific options (i.e. Spaces has different maxRecentPosts
        /// values depending on MetaWeblog or Atom API).
        /// </summary>
        public string ProtocolName
        {
            get { return _protocolName; }
        }

        private readonly string _typeName;
        private readonly string _protocolName;
    }


    public class EditPostResult
    {
        public string etag;
        public XmlDocument remotePost;
    }

    public interface IBlogClient
    {
        string ProtocolName { get; }

        IBlogClientOptions Options { get; }
        void OverrideOptions(IBlogClientOptions newClientOptions);

        bool VerifyCredentials();

        Task<BlogInfo[]> GetUsersBlogsAsync();

        Task<BlogPostCategory[]> GetCategories(string blogId);
        Task<BlogPostKeyword[]> GetKeywords(string blogId);

        /// <summary>
        /// Returns recent posts
        /// </summary>
        /// <param name="blogId"></param>
        /// <param name="maxPosts"></param>
        /// <param name="includeCategories"></param>
        /// <param name="now">If null, then includes future posts.  If non-null, then only includes posts before the *UTC* 'now' time.</param>
        /// <returns></returns>
        Task<BlogPost[]> GetRecentPostsAsync(string blogId, int maxPosts, bool includeCategories, DateTime? now);

        Task<string> NewPost(string blogId, BlogPost post, INewCategoryContext newCategoryContext, bool publish, PostResult postResult);

        Task<bool> EditPost(string blogId, BlogPost post, INewCategoryContext newCategoryContext, bool publish, EditPostResult result);

        /// <summary>
        /// Attempt to get a post with the specified id (note: may return null
        /// if the post could not be found on the remote server)
        /// </summary>
        Task<BlogPost> GetPost(string blogId, string postId);

        Task DeletePost(string blogId, string postId, bool publish);

        Task<BlogPost> GetPage(string blogId, string pageId);

        Task<PageInfo[]> GetPageList(string blogId);

        Task<BlogPost[]> GetPages(string blogId, int maxPages);

        Task<string> NewPage(string blogId, BlogPost page, bool publish, string etag, XmlDocument remotePost);

        Task<bool> EditPage(string blogId, BlogPost page, bool publish, string etag, XmlDocument remotePost);

        Task DeletePage(string blogId, string pageId);

        Task<AuthorInfo[]> GetAuthors(string blogId);

        bool? DoesFileNeedUpload(IFileUploadContext uploadContext);

        Task<string> DoBeforePublishUploadWork(IFileUploadContext uploadContext);

        Task DoAfterPublishUploadWork(IFileUploadContext uploadContext);

        Task<string> AddCategory(string blogId, BlogPostCategory category);

        Task<BlogPostCategory[]> SuggestCategories(string blogId, string partialCategoryName);

        Task<HttpResponseMessage> SendAuthenticatedHttpRequest(string requestUri, int timeoutMs, HttpAsyncRequestFilter filter);

        BlogInfo[] GetImageEndpoints();

        /// <summary>
        /// Returns false if credentials are sent in the clear
        /// </summary>
        bool IsSecure { get; }
    }

    public interface INewCategoryContext
    {
        void NewCategoryAdded(BlogPostCategory category);
    }

    public interface IFileUploadContext
    {
        string BlogId { get; }
        string PostId { get; }
        string PreferredFileName { get; }
        FileUploadRole Role { get; }
        Stream GetContents();
        string GetContentsLocalFilePath();
        IProperties Settings { get; }
        bool ForceDirectImageLink { get; }

        string FormatFileName(string filename);
    }

    public enum FileUploadRole { LinkedImage, InlineImage, File }

    public class PageInfo : ICloneable
    {
        public PageInfo(string id, string title, DateTime datePublished, string parentId)
        {
            _id = id;
            _title = title;
            _datePublished = datePublished;
            _parentId = parentId;
        }

        public string Id { get { return _id; } }

        public string Title { get { return _title; } }

        public DateTime DatePublished { get { return _datePublished; } }

        public string ParentId { get { return _parentId; } }

        public object Clone()
        {
            return new PageInfo(Id, Title, DatePublished, ParentId);
        }

        private string _id;
        private string _title;
        private DateTime _datePublished;
        private string _parentId;
    }


    public class AuthorInfo : ICloneable
    {
        public AuthorInfo(string id, string name)
        {
            _id = id;
            if (name != String.Empty)
                _name = name;
            else
                _name = _id;
        }

        public string Id { get { return _id; } }

        public string Name { get { return _name; } }


        public object Clone()
        {
            return new AuthorInfo(Id, Name);
        }

        private string _id;
        private string _name;




    }
}
