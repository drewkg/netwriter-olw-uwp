// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

#define APIHACK
using System;
using System.Collections ;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Web.Http;
using BlogWriter.OpenLiveWriter;
using OpenLiveWriter.BlogClient;
using OpenLiveWriter.BlogClient.Clients;
using OpenLiveWriter.BlogClient.Detection;
using OpenLiveWriter.BlogClient.Providers;
using OpenLiveWriter.CoreServices;
using OpenLiveWriter.CoreServices.Diagnostics;
using OpenLiveWriter.CoreServices.Progress;
using OpenLiveWriter.Extensibility.BlogClient;
using OpenLiveWriter.HtmlParser.Parser;
using OpenLiveWriter.Localization;

namespace OpenLiveWriter.BlogClient.Detection
{
	public class BlogServiceDetector : BlogServiceDetectorBase
	{
		public BlogServiceDetector(/*IBlogClientUIContext uiContext, Control hiddenBrowserParentControl, */ string localBlogId, string homepageUrl, IBlogCredentialsAccessor credentials)
			: base(/*uiContext, hiddenBrowserParentControl,*/ localBlogId, homepageUrl, credentials)
		{
		}
	

		public override async Task<object> DetectBlogService(IProgressHost progressHost, IEnumerable<IBlogProvider> availableProviders)
		{
			//using ( BlogClientUIContextSilentMode uiContextScope = new BlogClientUIContextSilentMode() ) //supress prompting for credentials
			//{
				try
				{
					// get the weblog homepage and rsd service description if available
					var weblogDOM = await GetWeblogHomepageDOM(progressHost)  ;

					// while we have the DOM available, scan for a writer manifest url
					//if ( _manifestDownloadInfo == null )
					//{
					//	string manifestUrl = WriterEditingManifest.DiscoverUrl(_homepageUrl, weblogDOM) ; 
					//	if ( manifestUrl != String.Empty )
					//		_manifestDownloadInfo = new WriterEditingManifestDownloadInfo(manifestUrl);
					//}
					
					string html = weblogDOM?.Body ;

					bool detectionSucceeded = false;

					if (!detectionSucceeded)
						detectionSucceeded = AttemptGenericAtomLinkDetection(_homepageUrl, html, !ApplicationDiagnostics.PreferAtom);

					if (!detectionSucceeded)
						detectionSucceeded = await AttemptBloggerDetection(_homepageUrl, html);

					if (!detectionSucceeded)
					{
						RsdServiceDescription rsdServiceDescription = await GetRsdServiceDescription(progressHost, weblogDOM) ;
					
						// if there was no rsd service description or we fail to auto-configure from the
						// rsd description then move on to other auto-detection techniques
					    detectionSucceeded = await AttemptRsdBasedDetection(progressHost, rsdServiceDescription, availableProviders);

                        if ( !detectionSucceeded )
						{
							// try detection by analyzing the homepage url and contents
							UpdateProgress( progressHost, 75, "Analysing homepage..." ) ;
							if ( weblogDOM != null )
								detectionSucceeded = AttemptHomepageBasedDetection(_homepageUrl, html, availableProviders) ;
							else
								detectionSucceeded = AttemptUrlBasedDetection(_homepageUrl, availableProviders) ;

							// if we successfully detected then see if we can narrow down
							// to a specific weblog
							if ( detectionSucceeded )						
							{
								if ( !BlogProviderParameters.UrlContainsParameters(_postApiUrl) )
								{
									// we detected the provider, now see if we can detect the weblog id
									// (or at lease the list of the user's weblogs)
									UpdateProgress( progressHost, 80, "Analysing weblog list" ) ;
									await AttemptUserBlogDetectionAsync() ;
								}
							}
						}
					}

					if (!detectionSucceeded && html != null)
						AttemptGenericAtomLinkDetection(_homepageUrl, html, false);
				
					// finished
					UpdateProgress( progressHost, 100, String.Empty ) ;
				}
            //catch( OperationCancelledException )
            //{
            //	// WasCancelled == true
            //}
            //catch ( BlogClientOperationCancelledException )
            //{
            //	Cancel();
            //	// WasCancelled == true 
            //}
            catch (BlogAccountDetectorException ex)
            {
                //if (ApplicationDiagnostics.AutomationMode)
                //    Trace.WriteLine(ex.ToString());
                //else
                //    Trace.Fail(ex.ToString());
                //ErrorOccurred = true;
            }
            catch ( Exception ex )
				{
					// ErrorOccurred == true 
					//Trace.Fail(ex.Message, ex.ToString());
					ReportError(MessageId.WeblogDetectionUnexpectedError, ex.Message) ;
				}

				return this ;
			//}
		}

	    private bool AttemptGenericAtomLinkDetection(string url, string html, bool preferredOnly)
		{
//			const string GENERIC_ATOM_PROVIDER_ID = "D48F1B5A-06E6-4f0f-BD76-74F34F520792";

//			if (html == null)
//				return false;
			
//			HtmlExtractor ex = new HtmlExtractor(html);
//			if (ex
//				.SeekWithin("<head>", "<body>")
//				.SeekWithin("<link href rel='service' type='application/atomsvc+xml'>", "</head>")
//				.Success)
//			{
//				IBlogProvider atomProvider = BlogProviderManager.FindProvider(GENERIC_ATOM_PROVIDER_ID);
			
//				BeginTag bt = ex.Element as BeginTag;

//				if (preferredOnly)
//				{
//					string classes = bt.GetAttributeValue("class");
//					if (classes == null)
//						return false;
//					if (!Regex.IsMatch(classes, @"\bpreferred\b"))
//						return false;
//				}

//				string linkUrl = bt.GetAttributeValue("href");

//				Debug.WriteLine("Atom service link detected in the blog homepage");

//				_providerId = atomProvider.Id;
//				_serviceName = atomProvider.Name;
//				_clientType = atomProvider.ClientType;
//				_blogName = string.Empty;
//				_postApiUrl = linkUrl;

//				IBlogClient client = new BlogClientManager.CreateClient(atomProvider.ClientType, _postApiUrl, _credentials);
//				client.VerifyCredentials();
//				_usersBlogs = client.GetUsersBlogs();
//				if (_usersBlogs.Length == 1)
//				{
//					_hostBlogId = _usersBlogs[0].Id;
//					_blogName = _usersBlogs[0].Name;
///*
//					if (_usersBlogs[0].HomepageUrl != null && _usersBlogs[0].HomepageUrl.Length > 0)
//						_homepageUrl = _usersBlogs[0].HomepageUrl;
//*/
//				}

//				// attempt to read the blog name from the homepage title
//				if (_blogName == null || _blogName.Length == 0)
//				{
//					HtmlExtractor ex2 = new HtmlExtractor(html);
//					if (ex2.Seek("<title>").Success)
//					{
//						_blogName = ex2.CollectTextUntil("title");
//					}
//				}
				
//				return true;
//			}
			return false;
		}

		private class BloggerGeneratorCriterion : IElementPredicate
		{
			public bool IsMatch(Element e)
			{
				BeginTag tag = e as BeginTag;
				if (tag == null)
					return false;
				
				if (!tag.NameEquals("meta"))
					return false;
				
				if (tag.GetAttributeValue("name") != "generator")
					return false;

				string generator = tag.GetAttributeValue("content");
				if (generator == null || CaseInsensitiveComparer.DefaultInvariant.Compare("blogger", generator) != 0)
					return false;
				
				return true;
			}
		}

		/// <summary>
		/// Do special Blogger-specific detection logic.  We want to
		/// use the Blogger Atom endpoints specified in the HTML, not
		/// the Blogger endpoint in the RSD.
		/// </summary>
		private async Task<bool> AttemptBloggerDetection( string homepageUrl, string html )
		{
		    html = html ?? "";
            BloggerDetectionHelper bloggerDetectionHelper = new BloggerDetectionHelper(homepageUrl, html);
            if (!bloggerDetectionHelper.IsBlogger())
                return false;

			const string BLOGGER_ATOM_PROVIDER_ID = "B6F817C3-9D39-45c1-A634-EAC792B8A635";
			IBlogProvider bloggerProvider = BlogProviderManager.FindProvider(BLOGGER_ATOM_PROVIDER_ID);
			if (bloggerProvider == null)
			{
				//Trace.Fail("Couldn't retrieve Blogger provider");
				return false;
			}

            _providerId = bloggerProvider.Id;
            _serviceName = bloggerProvider.Name;
            _clientType = bloggerProvider.ClientType;
            _postApiUrl = "http://www.blogger.com/feeds/default/blogs";

            BlogAccountDetector blogAccountDetector = new BlogAccountDetector(bloggerProvider.ClientType, "http://www.blogger.com", _credentials);
            if (await blogAccountDetector.ValidateServiceAsync())
            {
                _usersBlogs = blogAccountDetector.UsersBlogs;
                foreach (BlogInfo blog in _usersBlogs)
                {
                    string blogHomepageUrl = blog.HomepageUrl;
                    if (NormalizeBloggerHomepageUrl(blogHomepageUrl) == NormalizeBloggerHomepageUrl(homepageUrl))
                    {
                        _hostBlogId = blog.Id;
                        _postApiUrl = blog.Id;
                        _blogName = blog.Name;
                        return true;
                    }
                }

                // We didn't find the specific blog, but we'll prompt the user with the list of blogs
                return true;
            }
            else
            {
                AuthenticationErrorOccurred = blogAccountDetector.Exception is BlogClientAuthenticationException;
                ReportErrorAndFail(blogAccountDetector.ErrorMessageType, blogAccountDetector.ErrorMessageParams);
                return false;
            }
        }

		private string NormalizeBloggerHomepageUrl(string url)
		{
			// trim and uppercase
			url = url.Trim().ToUpperInvariant();
			
			// if the url has any ONE of the common suffixes, it is dropped and
			// the string is returned.
			foreach (string commonSuffix in new string[] {"/index.html", "/", "/index.htm", "/index.php", "/default.htm", "/default.html"})
				if (url.EndsWith(commonSuffix, StringComparison.OrdinalIgnoreCase))
					return url.Substring(0, url.Length - commonSuffix.Length);
			return url;
		}

		private async Task<bool> AttemptRsdBasedDetection( IProgressHost progressHost, RsdServiceDescription rsdServiceDescription, IEnumerable<IBlogProvider> availableProviders )
		{	
			// always return alse for null description
			if ( rsdServiceDescription == null )
				return false ;

			string providerId = String.Empty ;
			BlogAccount blogAccount = null ;
			
			// check for a match on rsd engine link
			foreach ( IBlogProvider provider in availableProviders)
			{
				blogAccount = provider.DetectAccountFromRsdHomepageLink(rsdServiceDescription) ;
				if ( blogAccount != null )
				{
					providerId = provider.Id ;
					break;
				}
			}

			// if none found on engine link, match on engine name
			if ( blogAccount == null )
			{
				foreach ( IBlogProvider provider in availableProviders)
				{
					blogAccount = provider.DetectAccountFromRsdEngineName(rsdServiceDescription) ;
					if ( blogAccount != null )
					{
						providerId = provider.Id ;
						break;
					}
				}
			}

			// No provider associated with the RSD file, try to gin one up (will only 
			// work if the RSD file contains an API for one of our supported client types)
			if ( blogAccount == null )
			{
				// try to create one from RSD
				blogAccount = BlogAccountFromRsdServiceDescription.Create(rsdServiceDescription);
			}

			// if we have an rsd-detected weblog 
			if ( blogAccount != null )
			{
				// confirm that the credentials are OK
				UpdateProgress( progressHost, 65, "Verifying interface" ) ;
				BlogAccountDetector blogAccountDetector = new BlogAccountDetector(
					blogAccount.ClientType, blogAccount.PostApiUrl, _credentials ) ;

                _serviceName = blogAccount.ServiceName;
                _clientType = blogAccount.ClientType;
                _hostBlogId = blogAccount.BlogId;
                _postApiUrl = blogAccount.PostApiUrl;
			    _providerId = providerId;

                //if ( await blogAccountDetector.ValidateServiceAsync() )
                //{
                //	// copy basic account info
                //	_providerId = providerId ;
                //	_serviceName = blogAccount.ServiceName;
                //	_clientType = blogAccount.ClientType ;
                //	_hostBlogId = blogAccount.BlogId;
                //	_postApiUrl = blogAccount.PostApiUrl;

                //	// see if we can improve on the blog name guess we already 
                //	// have from the <title> element of the homepage
                //	BlogInfo blogInfo = blogAccountDetector.DetectAccount(_homepageUrl, _hostBlogId) ;
                //	if ( blogInfo != null )
                //		_blogName = blogInfo.Name ;
                //}
                //else 
                //{
                //	// report user-authorization error
                //	ReportErrorAndFail(blogAccountDetector.ErrorMessageType, blogAccountDetector.ErrorMessageParams ) ;	
                //}

                // success!
                return true ;
			}
			else
			{
				// couldn't do it
				return false ;
			}
		}

		private bool AttemptUrlBasedDetection(string url, IEnumerable<IBlogProvider> availableBlogProviders)
		{
			// matched provider
			IBlogProvider blogAccountProvider = null ;

			// do url-based matching
			foreach ( IBlogProvider provider in availableBlogProviders)
			{
				if ( provider.IsProviderForHomepageUrl(url))
				{
					blogAccountProvider = provider ;
					break;
				}
			}

			if ( blogAccountProvider != null )
			{
				CopySettingsFromProvider(blogAccountProvider) ;
				return true ;
			}
			else
			{
				return false ;
			}
		}

		private bool AttemptContentBasedDetection(string homepageContent, IEnumerable<IBlogProvider> availableProviders)
		{
			// matched provider
			IBlogProvider blogAccountProvider = null ;

			// do url-based matching
			foreach ( IBlogProvider provider in availableProviders)
			{
				if ( provider.IsProviderForHomepageContent(homepageContent) )
				{
					blogAccountProvider = provider ;
					break;
				}
			}

			if ( blogAccountProvider != null )
			{
				CopySettingsFromProvider(blogAccountProvider) ;
				return true ;
			}
			else
			{
				return false ;
			}
		}
		
		private bool AttemptHomepageBasedDetection(string homepageUrl, string homepageContent, IEnumerable<IBlogProvider> availableProviders)
		{
			if ( AttemptUrlBasedDetection(homepageUrl, availableProviders) )
			{
				return true ;
			}
			else
			{
				return AttemptContentBasedDetection(homepageContent, availableProviders) ;
			}
		}
		
		private async Task<RsdServiceDescription> GetRsdServiceDescription(IProgressHost progressHost, HomepageDom weblogDOM)
		{
			if ( weblogDOM != null )
			{
				// try to download an RSD description
				UpdateProgress( progressHost, 50, "Analysing interface" ) ;
				return await RsdServiceDetector.DetectFromWeblogAsync( _homepageUrl, weblogDOM ) ;
			}
			else
			{
				return null ;
			}
		}
		
		
		private class BlogAccountFromRsdServiceDescription : BlogAccount
		{
			public static BlogAccount Create( RsdServiceDescription rsdServiceDescription )
			{
				try
				{
					return new BlogAccountFromRsdServiceDescription(rsdServiceDescription);
				}
				catch(NoSupportedRsdClientTypeException)
				{
					return null  ;
				}
			}

			private BlogAccountFromRsdServiceDescription( RsdServiceDescription rsdServiceDescription )
			{
				// look for supported apis from highest fidelity to lowest
				RsdApi rsdApi = rsdServiceDescription.ScanForApi("WordPress") ;
				if ( rsdApi == null )
					rsdApi = rsdServiceDescription.ScanForApi("MovableType") ;
				if ( rsdApi == null )
					rsdApi = rsdServiceDescription.ScanForApi("MetaWeblog") ;

				if ( rsdApi != null )
				{
					Init( rsdServiceDescription.EngineName, rsdApi.Name, rsdApi.ApiLink, rsdApi.BlogId );
					return ;
				}
				else
				{
					// couldn't find a supported api type so we fall through to here 
					throw new NoSupportedRsdClientTypeException();		
				}
			}

			
		}

		private class NoSupportedRsdClientTypeException : Exception
		{
			
		}
	}

    public abstract class BlogServiceDetectorBase /*: ITemporaryBlogSettingsDetectionContext*/
	{
		public BlogServiceDetectorBase(/*IBlogClientUIContext uiContext, Control hiddenBrowserParentControl, */ string localBlogId, string homepageUrl, IBlogCredentialsAccessor credentials)
		{			
			// save references
			//_uiContext = uiContext ;
			_localBlogId = localBlogId ;
			_homepageUrl = homepageUrl ;
			_credentials = credentials ;
			
			// add blog service detection
			//AddProgressOperation( 
			//	new ProgressOperation(DetectBlogService), 
			//	35 );

			// add settings downloading (note: this operation will be a no-op
			// in the case where we don't succesfully detect a weblog)
			//AddProgressOperation(
			//	new ProgressOperation(DetectWeblogSettings), 
			//	new ProgressOperationCompleted(DetectWeblogSettingsCompleted),
			//	30 ) ;

			// add template downloading (note: this operation will be a no-op in the 
			// case where we don't successfully detect a weblog)
			//_blogEditingTemplateDetector = new BlogEditingTemplateDetector(uiContext, hiddenBrowserParentControl) ;
			//AddProgressOperation( 
			//	new ProgressOperation(_blogEditingTemplateDetector.DetectTemplate),
			//	35 ) ;
		}
		
		public BlogInfo[] UsersBlogs
		{
			get { return _usersBlogs; }
		}

		public string ProviderId
		{
			get { return _providerId; }
		}

		public string ServiceName 
		{
			get { return _serviceName; }
		}

	
		public string ClientType 
		{
			get { return _clientType; }
		}

		public string PostApiUrl 
		{
			get { return _postApiUrl; }
		}
	
		public string HostBlogId
		{
			get { return _hostBlogId; }
		}

		public string BlogName
		{
			get { return _blogName; }
		}

		public IDictionary OptionOverrides
		{
			get { return _optionOverrides; }
		}

        public IDictionary HomePageOverrides
        {
            get { return _homePageOverrides; }
        }

		public IDictionary UserOptionOverrides
		{
			get { return null; }
		}

		public IBlogProviderButtonDescription[] ButtonDescriptions
		{
			get { return _buttonDescriptions; }
		}
	
		public BlogPostCategory[] Categories
		{
			get { return _categories; }
		}

        public BlogPostKeyword[] Keywords
        {
            get { return _keywords; }
        }
	
		public byte[] FavIcon
		{
			get { return _favIcon; }
		}

		public byte[] Image
		{
			get { return _image; }
		}

		public byte[] WatermarkImage
		{
			get { return _watermarkImage; }
		}

		//public BlogEditingTemplateFile[] BlogTemplateFiles
		//{
		//	get { return _blogEditingTemplateDetector.BlogTemplateFiles; }
		//}

	 //   public Color? PostBodyBackgroundColor
	 //   {
  //          get { return _blogEditingTemplateDetector.PostBodyBackgroundColor; }
	 //   }

		//public bool WasCancelled
		//{
		//	get { return CancelRequested; }
		//}

		public bool ErrorOccurred
		{
			get { return _errorMessageType != MessageId.None; }
		}
		
		public bool AuthenticationErrorOccurred
		{
			get { return _authenticationErrorOccured; }
			set { _authenticationErrorOccured = value; }
		}
		private bool _authenticationErrorOccured = false;

		public bool TemplateDownloadFailed
		{
			get { return /*_blogEditingTemplateDetector.ExceptionOccurred;*/ true; }
		}

		//IBlogCredentialsAccessor IBlogSettingsDetectionContext.Credentials
		//{
		//	get { return _credentials; }
		//}

		//string IBlogSettingsDetectionContext.HomepageUrl
		//{
		//	get { return _homepageUrl; }
		//}

		public WriterEditingManifestDownloadInfo ManifestDownloadInfo
		{
			get { return _manifestDownloadInfo; }
			set { _manifestDownloadInfo = value; }
		}

		//string IBlogSettingsDetectionContext.ClientType 
		//{
		//	get { return _clientType; }
		//	set { _clientType = value; }
		//}

		//byte[] IBlogSettingsDetectionContext.FavIcon
		//{
		//	get { return _favIcon; }
		//	set { _favIcon = value; }
		//}

		//byte[] IBlogSettingsDetectionContext.Image
		//{
		//	get { return _image; }
		//	set { _image = value; }
		//}

		//byte[] IBlogSettingsDetectionContext.WatermarkImage
		//{
		//	get { return _watermarkImage; }
		//	set { _watermarkImage = value; }
		//}

		//BlogPostCategory[] IBlogSettingsDetectionContext.Categories
		//{
		//	get { return _categories; }
		//	set { _categories = value; }
		//}

  //      BlogPostKeyword[] IBlogSettingsDetectionContext.Keywords
  //      {
  //          get { return _keywords; }
  //          set { _keywords = value; }
  //      }

		//IDictionary IBlogSettingsDetectionContext.OptionOverrides
		//{
		//	get { return _optionOverrides; }
		//	set { _optionOverrides = value; }
		//}

  //      IDictionary IBlogSettingsDetectionContext.HomePageOverrides
  //      {
  //          get { return _homePageOverrides; }
  //          set { _homePageOverrides = value; }
  //      }

		//IBlogProviderButtonDescription[] IBlogSettingsDetectionContext.ButtonDescriptions
		//{
		//	get { return _buttonDescriptions; }
		//	set { _buttonDescriptions = value; }
		//}

		public BlogInfo[] AvailableImageEndpoints
		{
			get { return availableImageEndpoints; }
			set { availableImageEndpoints = value; }
		}


		public void ShowLastError(/*IWin32Window owner*/)
		{
			//if ( ErrorOccurred )
			//{
			//	DisplayMessage.Show( _errorMessageType, owner, _errorMessageParams ) ;
			//}
			//else
			//{
			//	Trace.Fail("Called ShowLastError when no error occurred");
			//}
		}

		public static byte[] SafeDownloadFavIcon(/*string homepageUrl*/)
		{
		    return null;
		    //try
		    //{
		    //	string favIconUrl = UrlHelper.UrlCombine(homepageUrl, "favicon.ico") ;	
		    //	using ( Stream favIconStream = HttpRequestHelper.SafeDownloadFile(favIconUrl) )
		    //	{
		    //		using ( MemoryStream memoryStream = new MemoryStream() )
		    //		{
		    //			StreamHelper.Transfer(favIconStream, memoryStream) ;
		    //			memoryStream.Seek(0, SeekOrigin.Begin) ;
		    //			return memoryStream.ToArray() ;
		    //		}
		    //	}
		    //}
		    //catch
		    //{
		    //	return null ;
		    //}
		}

        public abstract Task<object> DetectBlogService(IProgressHost progressHost, IEnumerable<IBlogProvider> availableProviders);
		
		protected async Task AttemptUserBlogDetectionAsync()
		{
			BlogAccountDetector blogAccountDetector = new BlogAccountDetector(
				_clientType, _postApiUrl, _credentials) ;

			if ( await blogAccountDetector.ValidateServiceAsync() )
			{
				BlogInfo blogInfo = blogAccountDetector.DetectAccount(_homepageUrl, _hostBlogId);
				if ( blogInfo != null )
				{
					// save the detected info
					// TODO: Commenting out next line for Spaces demo tomorrow.
					// need to decide whether to keep it commented out going forward.
					// _homepageUrl = blogInfo.HomepageUrl;
					_hostBlogId = blogInfo.Id ;
					_blogName = blogInfo.Name ;
				}

				// always save the list of user's blogs
				_usersBlogs = blogAccountDetector.UsersBlogs ;
			}
			else
			{
				AuthenticationErrorOccurred = blogAccountDetector.Exception is BlogClientAuthenticationException;
				ReportErrorAndFail( blogAccountDetector.ErrorMessageType, blogAccountDetector.ErrorMessageParams ) ;
			}
		}

        public class HomepageDom
        {
            public string Title { get; set; }
            public string Url { get; set; }
            public string Body { get; set; }

            public static async Task<HomepageDom> GetDomFromUrl(string url)
            {
                try
                {
                    using (var httpClient = new HttpClient())
                    {
                        var contentsMessage = await httpClient.GetAsync(new Uri(url));
                        if (!contentsMessage.IsSuccessStatusCode)
                        {
                            return null;
                        }

                        HtmlAgilityPack.HtmlDocument htmlDoc = new HtmlAgilityPack.HtmlDocument();
                        var bodyString = await contentsMessage.Content.ReadAsStringAsync();
                        htmlDoc.LoadHtml(bodyString);

                        var result = new HomepageDom();
                        result.Body = bodyString;
                        result.Url = url;
                        result.Title = htmlDoc.DocumentNode.Descendants("title").SingleOrDefault()?.InnerText;

                        return result;
                    }
                }
                catch
                {
                    return null;
                }
            }
        }


		protected async Task<HomepageDom> GetWeblogHomepageDOM(IProgressHost progressHost)
		{
			// try download the weblog home page
			UpdateProgress( progressHost, 25, "Analysing Homepage" ) ;
		    //string responseUri;
			var weblogDOM = await HomepageDom.GetDomFromUrl(_homepageUrl);
      //      if (responseUri != null && responseUri != _homepageUrl)
      //      {
      //          _homepageUrl = responseUri;
      //      }
		    //if ( weblogDOM != null )
      //      {
      //          // default the blog name to the title of the document
      //          if (weblogDOM.title != null)
      //          {
      //              _blogName = weblogDOM.title;

      //              // drop anything to the right of a "|", as it usually is a site name
      //              int index = _blogName.IndexOf("|", StringComparison.OrdinalIgnoreCase);
      //              if (index > 0)
      //              {
      //                  string newname = _blogName.Substring(0, index).Trim();
      //                  if (newname != String.Empty)
      //                      _blogName = newname;
      //              }
      //          }
      //      }

			return weblogDOM ;
		}

		protected void CopySettingsFromProvider(IBlogProvider blogAccountProvider)
		{
			_providerId = blogAccountProvider.Id ;
			_serviceName = blogAccountProvider.Name;
			_clientType = blogAccountProvider.ClientType;
			_postApiUrl = ProcessPostUrlMacros(blogAccountProvider.PostApiUrl);
		}


		private string ProcessPostUrlMacros(string postApiUrl)
		{
			return postApiUrl.Replace("<username>", _credentials.Username) ;
		}		

		
		//private object DetectWeblogSettings(IProgressHost progressHost)
		//{
		//	using ( BlogClientUIContextSilentMode uiContextScope = new BlogClientUIContextSilentMode() ) //supress prompting for credentials
		//	{
		//		// no-op if we don't have a blog-id to work with
		//		if ( HostBlogId == String.Empty )
		//			return this ;
			
		//		try
		//		{
		//			// detect settings
		//			BlogSettingsDetector blogSettingsDetector = new BlogSettingsDetector(this);
		//			blogSettingsDetector.DetectSettings(progressHost);
		//		}
		//		catch(OperationCancelledException)
		//		{
		//			// WasCancelled == true
		//		}
		//		catch (BlogClientOperationCancelledException)
		//		{
		//			Cancel() ;
		//			// WasCancelled == true
		//		}
		//		catch(Exception ex)
		//		{
		//			Trace.Fail("Unexpected error occurred while detecting weblog settings: " + ex.ToString());
		//		}

		//		return this ;
		//	}
		//}

		private void DetectWeblogSettingsCompleted(object result)
		{
		    return;
			// no-op if we don't have a blog detected
			//if ( HostBlogId == String.Empty )
			//	return ;
			
			//// get the editing template directory
			//string blogTemplateDir = BlogEditingTemplate.GetBlogTemplateDir(_localBlogId);

			//// set context for template detector
			//BlogAccount blogAccount = new BlogAccount(ServiceName, ClientType, PostApiUrl, HostBlogId);
   //         _blogEditingTemplateDetector.SetContext(blogAccount, _credentials, _homepageUrl, blogTemplateDir, _manifestDownloadInfo, false, _providerId, _optionOverrides, null, _homePageOverrides);	
		
		}
	

		protected void UpdateProgress( IProgressHost progressHost, int percent, string message )
		{
			//if ( CancelRequested )
			//	throw new OperationCancelledException() ;

			progressHost.UpdateProgress(percent, 100, message ) ;
		}		

		protected void ReportError( MessageId errorMessageType, params object[] errorMessageParams)
		{
			_errorMessageType = errorMessageType ;
			_errorMessageParams = errorMessageParams ;
		}

		protected void ReportErrorAndFail( MessageId errorMessageType, params object[] errorMessageParams )
		{
			ReportError( errorMessageType, errorMessageParams ) ;
			throw new BlogAccountDetectorException() ;
		}

		protected class BlogAccountDetectorException : Exception
		{
		    //public BlogAccountDetectorException() : base("Blog account detector did not succeed")
		    //{
		    //}
		}
	
		/// <summary>
		/// Blog account we are scanning
		/// </summary>
		private string _localBlogId ;
		protected string _homepageUrl ;
		protected WriterEditingManifestDownloadInfo _manifestDownloadInfo = null ;
		protected IBlogCredentialsAccessor _credentials ;
		
		// BlogTemplateDetector
		//private BlogEditingTemplateDetector _blogEditingTemplateDetector ;
		
		/// <summary>
		/// Results of scanning
		/// </summary>
		protected string _providerId = String.Empty ;
		protected string _serviceName = String.Empty;
		protected string _clientType = String.Empty;
		protected string _postApiUrl = String.Empty ;
		protected string _hostBlogId = String.Empty ;
		protected string _blogName = String.Empty;
		
		protected BlogInfo[] _usersBlogs = new BlogInfo[] {};

		// if we are unable to detect these values then leave them null
		// as an indicator that their values are "unknown" vs. "empty"
		// callers can then choose to not overwrite any existing settings
		// in this case
	    protected IDictionary _homePageOverrides = null;
		protected IDictionary _optionOverrides = null ;
		private BlogPostCategory[] _categories = null;
        private BlogPostKeyword[] _keywords = null;
		private byte[] _favIcon = null ;
		private byte[] _image = null ;
		private byte[] _watermarkImage = null ;
		private IBlogProviderButtonDescription[] _buttonDescriptions = null ;

		// error info
		private MessageId _errorMessageType ;
		private object[] _errorMessageParams ;
		//protected IBlogClientUIContext _uiContext ;
		private BlogInfo[] availableImageEndpoints;
	}
}
