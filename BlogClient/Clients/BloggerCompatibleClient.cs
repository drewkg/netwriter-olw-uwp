// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections;
using System.Diagnostics;
using System.Threading;
using Windows.Data.Xml.Dom;
using System.Net;
using System.Threading.Tasks;
using OpenLiveWriter.Api;
using OpenLiveWriter.CoreServices;
using OpenLiveWriter.Extensibility.BlogClient;
using OpenLiveWriter.Localization;

namespace OpenLiveWriter.BlogClient.Clients
{
	/// <summary>
	/// Summary description for BloggerCompatibleClient.
	/// </summary>
	public abstract class BloggerCompatibleClient : XmlRpcBlogClient
	{
		public BloggerCompatibleClient(Uri postApiUrl, IBlogCredentialsAccessor credentials)
			: base(postApiUrl, credentials)
		{	
		}
	

		protected override async Task VerifyCredentials(TransientCredentials tc)
		{
			try
			{
				await GetUsersBlogs(tc.Username, tc.Password);
				return;
			}
			catch(BlogClientAuthenticationException)
			{
				throw;
			}
			catch(Exception e)
			{
				//if(!BlogClientUIContext.SilentModeForCurrentThread)
				//	ShowError(e.Message);
				throw;
			}
		}
		
		//private void ShowError(string error)
		//{
		//	ShowErrorHelper helper =
		//		new ShowErrorHelper(BlogClientUIContext.ContextForCurrentThread, MessageId.UnexpectedErrorLogin,
		//		                    new object[] {error});
		//	if (BlogClientUIContext.ContextForCurrentThread != null)
		//		BlogClientUIContext.ContextForCurrentThread.Invoke(new ThreadStart(helper.Show), null);
		//	else
		//		helper.Show();
		//}
		
		//private class ShowErrorHelper
		//{
		//	private readonly IWin32Window _owner;
		//	private readonly MessageId _messageId;
		//	private readonly object[] _args;

		//	public ShowErrorHelper(IWin32Window owner, MessageId messageId, object[] args)
		//	{
		//		_owner = owner;
		//		_messageId = messageId;
		//		_args = args;
		//	}
			
		//	public void Show()
		//	{
		//		DisplayMessage.Show(_messageId, _owner, _args);
		//	}
		//}

		public override async Task<BlogInfo[]> GetUsersBlogsAsync()
		{
			TransientCredentials tc = await Login();
			return await GetUsersBlogs(tc.Username, tc.Password);
		}
		
		private async Task<BlogInfo[]> GetUsersBlogs(string username, string password)
		{
			// call method
			var result = await CallMethod( "blogger.getUsersBlogs", 
				new XmlRpcString( APP_KEY ),
				new XmlRpcString( username ),
				new XmlRpcString( password, true ) ) ;

			try
			{
				// parse results
				ArrayList blogs = new ArrayList() ;
				XmlNodeList blogNodes = result.SelectNodes( "array/data/value/struct" ) ;
				foreach ( var blogNode in blogNodes )
				{
                    // get node values
                    var idNode = blogNode.SelectSingleNode("member[name='blogid']/value") ;
                    var nameNode = blogNode.SelectSingleNode("member[name='blogName']/value") ;
                    var urlNode = blogNode.SelectSingleNode("member[name='url']/value") ;

					// add to our list of blogs
                    blogs.Add(new BlogInfo(idNode.InnerText, Windows.Data.Html.HtmlUtilities.ConvertToText(NodeToText(nameNode)), urlNode.InnerText));			
				}

				// return list of blogs
				return (BlogInfo[])blogs.ToArray(typeof(BlogInfo)) ;
			}
			catch( Exception ex )
			{
				string response = result != null ? result.GetXml() : "(empty response)" ;
				//Debug.Fail( "Exception occurred while parsing GetUsersBlogsAsync response: " + response + "\r\n" + ex.ToString() ) ;
				throw new BlogClientInvalidServerResponseException( "blogger.getUsersBlogs", ex.Message, response ) ;                
			}
		}

	    protected virtual string NodeToText(IXmlNode node)
	    {
	        return node.InnerText;
	    }


	    public override async Task DeletePost( string blogId, string postId, bool publish ) 
		{
			TransientCredentials tc = await Login();
			var result = CallMethod( "blogger.deletePost", 
				new XmlRpcString( APP_KEY ),
				new XmlRpcString( postId ),
				new XmlRpcString( tc.Username ),
				new XmlRpcString( tc.Password, true ),
				new XmlRpcBoolean( publish ) ) ;		
		}



		protected const string APP_KEY = "0123456789ABCDEF" ;
	}
}
