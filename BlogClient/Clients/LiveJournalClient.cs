// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Xml.Dom;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.Storage.Streams;
using Windows.Web.Http;
using BlogWriter.OpenLiveWriter;
using OpenLiveWriter.Extensibility.BlogClient;
using OpenLiveWriter.BlogClient.Providers;
using OpenLiveWriter.HtmlParser.Parser;
using OpenLiveWriter.CoreServices;

namespace OpenLiveWriter.BlogClient.Clients
{

    [BlogClient("LiveJournal", "LiveJournal")]
    public class LiveJournalClient : BloggerCompatibleClient
    {
        public LiveJournalClient(Uri postApiUrl, IBlogCredentialsAccessor credentials)
            : base(postApiUrl, credentials)
        {
        }



        protected override void ConfigureClientOptions(BlogClientOptions clientOptions)
        {
            clientOptions.SupportsFileUpload = true;
            clientOptions.SupportsCustomDate = false;
            clientOptions.SupportsExtendedEntries = true;
        }

        protected override string NodeToText(IXmlNode node)
        {
            XmlElement childNode = node.FirstChild as XmlElement;
            if (childNode != null && childNode.LocalName == "base64")
            {
                try
                {
                    return Encoding.UTF8.GetString(Convert.FromBase64String(childNode.InnerText));
                }
                catch (Exception e)
                {
                    //Debug.Fail(e.ToString());
                }
            }

            return node.InnerText;
        }

        public override Task<BlogPostCategory[]> GetCategories(string blogId)
        {
            // LiveJournal does not support client posting of categories
            return Task.FromResult(new BlogPostCategory[] { });
        }

        public override Task<BlogPostKeyword[]> GetKeywords(string blogId)
        {
            //Debug.Fail("LiveJournal does not support GetKeywords!");
            return Task.FromResult(new BlogPostKeyword[] { });
        }


        public override async Task<BlogPost> GetPost(string blogId, string postId)
        {
            // query for post 
            var postResult = await CallMethod("blogger.getPost",
                new XmlRpcString(APP_KEY),
                new XmlRpcString(postId),
                new XmlRpcString(Username),
                new XmlRpcString(Password, true));

            // parse results
            try
            {
                // get the post struct
                var postNode = postResult.SelectSingleNode("struct");

                // create a post to return
                BlogPost blogPost = new BlogPost();

                // extract content
                ExtractStandardPostFields(postNode, blogPost);

                // return the post
                return blogPost;
            }
            catch (Exception ex)
            {
                string response = postResult != null ? postResult.GetXml() : "(empty response)";
                //Debug.Fail("Exception occurred while parsing blogger.getPost response: " + response + "\r\n" + ex.ToString());
                throw new BlogClientInvalidServerResponseException("blogger.getPost", ex.Message, response);
            }
        }

        public override async Task<BlogPost[]> GetRecentPostsAsync(string blogId, int maxPosts, bool includeCategories, DateTime? now)
        {
            // posts to return
            ArrayList posts = new ArrayList();

            // call the method
            var result = await CallMethod("blogger.getRecentPosts",
                new XmlRpcString(APP_KEY),
                new XmlRpcString(blogId),
                new XmlRpcString(Username),
                new XmlRpcString(Password, true),
                new XmlRpcInt(maxPosts));

            // parse results
            try
            {
                var postNodes = result.SelectNodes("array/data/value/struct");
                foreach (var postNode in postNodes)
                {
                    // create blog post 
                    BlogPost blogPost = new BlogPost();

                    ExtractStandardPostFields(postNode, blogPost);

                    // add to our list of posts
                    if (!now.HasValue || blogPost.DatePublished.CompareTo(now.Value) < 0)
                        posts.Add(blogPost);
                }
            }
            catch (Exception ex)
            {
                string response = result != null ? result.GetXml() : "(empty response)";
                //Debug.Fail("Exception occurred while parsing GetRecentPosts response: " + response + "\r\n" + ex.ToString());
                throw new BlogClientInvalidServerResponseException("blogger.getRecentPosts", ex.Message, response);
            }


            // return list of posts
            return (BlogPost[])posts.ToArray(typeof(BlogPost));
        }




        public override async Task<string> NewPost(string blogId, BlogPost post, INewCategoryContext newCategoryContext, bool publish)
        {
            if (!publish && !Options.SupportsPostAsDraft)
            {
                //Debug.Fail("Post to draft not supported on this provider");
                throw new BlogClientPostAsDraftUnsupportedException();
            }

            // call the method
            var result = await CallMethod("blogger.newPost",
                new XmlRpcString(APP_KEY),
                new XmlRpcString(blogId),
                new XmlRpcString(Username),
                new XmlRpcString(Password, true),
                FormatBlogPost(post),
                new XmlRpcBoolean(publish));

            // return the blog-id
            return result.InnerText;
        }



        public override async Task<bool> EditPost(string blogId, BlogPost post, INewCategoryContext newCategoryContext, bool publish)
        {
            if (!publish && !Options.SupportsPostAsDraft)
            {
                //Debug.Fail("Post to draft not supported on this provider");
                throw new BlogClientPostAsDraftUnsupportedException();
            }

            // call the method
            var result = await CallMethod("blogger.editPost",
                new XmlRpcString(APP_KEY),
                new XmlRpcString(post.Id),
                new XmlRpcString(Username),
                new XmlRpcString(Password, true),
                FormatBlogPost(post),
                new XmlRpcBoolean(publish));

            return result.InnerText.Trim() == "1";
        }

        public override async Task<string> DoBeforePublishUploadWork(IFileUploadContext uploadContext)
        {
            const int REQUEST_COUNT = 2;

            // get as many challenge tokens as we'll need (one for each authenticated request)
            FotobilderRequestManager frm = new FotobilderRequestManager(Username, Password);
            var doc = await frm.PerformGet("GetChallenges", null,
                "GetChallenges.Qty", REQUEST_COUNT.ToString(CultureInfo.InvariantCulture));
            var challengeNodes = doc.SelectNodes(@"/FBResponse/GetChallengesResponse/Challenge");
            //Debug.Assert(challengeNodes.Count == REQUEST_COUNT);
            Stack challenges = new Stack(challengeNodes.Count);
            foreach (var node in challengeNodes)
                challenges.Push(node.InnerText);

            // login
            long bytesAvailable = long.MaxValue;
            doc = await frm.PerformGet("Login", (string)challenges.Pop(),
                "Login.ClientVersion", ApplicationEnvironment.UserAgent);
            var remainingQuotaNode = doc.SelectSingleNode("/FBResponse/LoginResponse/Quota/Remaining");
            if (remainingQuotaNode != null)
                bytesAvailable = long.Parse(remainingQuotaNode.InnerText, CultureInfo.InvariantCulture);

            // upload picture
            using (Stream fileContents = uploadContext.GetContents())
            {
                doc = await frm.PerformPut("UploadPic", (string)challenges.Pop(), fileContents,
                                     "UploadPic.PicSec", "255",
                                     "UploadPic.Meta.Filename", uploadContext.FormatFileName(uploadContext.PreferredFileName),
                                     "UploadPic.Gallery._size", "1",
                                     "UploadPic.Gallery.0.GalName", ApplicationEnvironment.ProductName,
                                     "UploadPic.Gallery.0.GalSec", "255"); // GalSec 0 no longer supported, using 255
            }

            var picUrlNode = doc.SelectSingleNode("/FBResponse/UploadPicResponse/URL");
            if (picUrlNode != null)
            {
                return picUrlNode.InnerText;
            }
            else
            {
                throw new BlogClientInvalidServerResponseException("LiveJournal.UploadPic", "No URL returned from server", doc.GetXml());
            }
        }

        protected override BlogClientProviderException ExceptionForFault(string faultCode, string faultString)
        {
            if (
                (faultCode == "100") ||
                (faultCode == "101") ||
                (faultCode.ToUpperInvariant() == "SERVER" && faultString.StartsWith("invalid login", StringComparison.OrdinalIgnoreCase)))
            {
                return new BlogClientAuthenticationException(faultCode, faultString);
            }
            else
            {
                return null;
            }
        }


        private void ExtractStandardPostFields(IXmlNode postNode, BlogPost blogPost)
        {
            // post id
            blogPost.Id = NodeText(postNode.SelectSingleNode("member[name='postId']/value"));

            // contents and title
            ParsePostContent(postNode.SelectSingleNode("member[name='content']/value"), blogPost);

            // date published
            blogPost.DatePublished = ParseBlogDate(postNode.SelectSingleNode("member[name='dateCreated']/value"));
        }

        private void ParsePostContent(IXmlNode xmlNode, BlogPost blogPost)
        {
            // get raw content (decode base64 if necessary)
            string content;
            var base64Node = xmlNode.SelectSingleNode("base64");
            if (base64Node != null)
            {
                byte[] contentBytes = Convert.FromBase64String(base64Node.InnerText);
                content = _utf8EncodingNoBOM.GetString(contentBytes);
            }
            else // no base64 encoding, just read text
            {
                content = xmlNode.InnerText;
            }

            // parse out the title and contents of the post
            HtmlExtractor ex = new HtmlExtractor(content);
            if (ex.Seek("<title>").Success)
            {
                SetPostTitleFromXmlValue(blogPost, ex.CollectTextUntil("title"));
                content = content.Substring(ex.Parser.Position).TrimStart('\r', '\n');

            }

            if (content.Trim() != string.Empty)
            {
                HtmlExtractor ex2 = new HtmlExtractor(content);
                if (Options.SupportsExtendedEntries && ex2.Seek("<lj-cut>").Success)
                    blogPost.SetContents(content.Substring(0, ex2.Element.Offset), content.Substring(ex2.Element.Offset + ex2.Element.Length));
                else
                    blogPost.Contents = content;
            }


        }


        private XmlRpcValue FormatBlogPost(BlogPost post)
        {
            string content = post.MainContents;
            if (post.ExtendedContents != null && post.ExtendedContents.Length > 0)
                content += "<lj-cut>" + post.ExtendedContents;
            string blogPostBody = String.Format(CultureInfo.InvariantCulture, "<title>{0}</title>{1}", GetPostTitleForXmlValue(post), content);
            return new XmlRpcBase64(_utf8EncodingNoBOM.GetBytes(blogPostBody));
        }
        private Encoding _utf8EncodingNoBOM = new UTF8Encoding(false);


        private class FotobilderRequestManager
        {
            private const string ENDPOINT = "http://pics.livejournal.com/interface/simple";
            private readonly string username;
            private readonly string password;

            public FotobilderRequestManager(string username, string password)
            {
                this.username = username;
                this.password = password;
            }

            public Task<XmlDocument> PerformGet(string mode, string challenge, params string[] addlParams)
            {
                var request = CreateRequest(mode, challenge, addlParams);
                request.Method = new HttpMethod("GET");
                return GetResponse(request, mode);
            }

            public async Task<XmlDocument> PerformPut(string mode, string challenge, Stream requestBody, params string[] addlParams)
            {
                var request = CreateRequest(mode, challenge, addlParams);
                request.Method = new HttpMethod("PUT");
                request.Content = new HttpStreamContent(requestBody.AsInputStream());
                //using (Stream requestStream = await request.GetRequestStreamAsync())
                //    await StreamHelper.TransferAsync(requestBody, requestStream);
                return await GetResponse(request, mode);
            }

            private HttpRequestMessage CreateRequest(string mode, string challenge, params string[] addlParams)
            {
                var request = HttpRequestHelper.CreateHttpWebRequest(ENDPOINT, true, Timeout.Infinite, Timeout.Infinite);
                request.Headers["X-FB-User"] = username;
                if (challenge != null)
                    request.Headers["X-FB-Auth"] = CreateAuthString(challenge);
                request.Headers["X-FB-Mode"] = mode;

                if (addlParams != null)
                {
                    for (int i = 0; i < addlParams.Length; i += 2)
                    {
                        string name = addlParams[i];
                        string value = addlParams[i + 1];
                        if (name != null)
                            request.Headers["X-FB-" + name] = value;
                    }
                }

                return request;
            }

            private static async Task<XmlDocument> GetResponse(HttpRequestMessage request, string mode)
            {
                var response = await new HttpClient().SendRequestAsync(request);
                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(await response.Content.ReadAsStringAsync());
                CheckForErrors(xmlDoc, mode);
                return xmlDoc;
            }

            private string CreateAuthString(string challenge)
            {
                return string.Format(CultureInfo.InvariantCulture, "crp:{0}:{1}", challenge, MD5Hash(challenge + MD5Hash(password)));
            }

            public static string MD5Hash(string str)
            {
                var alg = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Md5);
                IBuffer buff = CryptographicBuffer.ConvertStringToBinary(str, BinaryStringEncoding.Utf8);
                var hashed = alg.HashData(buff);
                var res = CryptographicBuffer.EncodeToHexString(hashed);
                return res;
            }

            private static void CheckForErrors(XmlDocument doc, string mode)
            {
                IXmlNode errorNode;
                if ((errorNode = doc.SelectSingleNode("//Error")) != null)
                {
                    /*
                    Possible errors:
                    1xx: User Errors
                    100	User error
                    101	No user specified
                    102	Invalid user
                    103	Unknown user
					
                    2xx: Client Errors
                    200	Client error
                    201	Invalid request
                    202	Invalid mode
                    203	GetChallenge(s) is exclusive as primary mode
                    210	Unknown argument
                    211	Invalid argument
                    212	Missing required argument
                    213	Invalid image for upload
					
                    3xx: Access Errors
                    300	Access error
                    301	No auth specified
                    302	Invalid auth
                    303	Account status does not allow upload
					
                    4xx: Limit Errors
                    400	Limit error
                    401	No disk space remaining
                    402	Insufficient disk space remaining
                    403	File upload limit exceeded
					
                    5xx: Server Errors
                    500	Internal Server Error
                    501	Cannot connect to database
                    502	Database Error
                    503	Application Error
                    510	Error creating gpic
                    511	Error creating upic
                    512	Error creating gallery
                    513	Error adding to gallery
                    */
                    string errorCode = (string)errorNode.Attributes.Single(a => a.NodeName == "code").NodeValue;
                    string errorString = errorNode.InnerText;
                    switch (errorCode)
                    {
                        case "301":
                        case "302":
                            throw new BlogClientAuthenticationException(errorCode, errorString);
                        case "303":
                            throw new BlogClientFileUploadNotSupportedException(errorCode, errorString);
                        default:
                            throw new BlogClientProviderException(errorCode, errorString);
                    }
                }
            }

        }
    }
}
