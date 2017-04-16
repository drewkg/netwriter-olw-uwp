// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections;
using System.Globalization;
using OpenLiveWriter.Extensibility.BlogClient;

namespace OpenLiveWriter.BlogClient
{
	public interface IBlogSettingsAccessor : IDisposable
	{	
		string Id { get; }

		bool IsSpacesBlog { get; }
		bool IsSharePointBlog { get; }

		string HostBlogId { get; }
		string BlogName { get; }

		string HomepageUrl { get; }
		bool ForceManualConfig { get; }
		WriterEditingManifestDownloadInfo ManifestDownloadInfo { get; }

		string ProviderId { get; }
		string ServiceName { get; }
	
		string ClientType { get; }
		string PostApiUrl { get; }		

		IDictionary OptionOverrides { get; }
		IDictionary UserOptionOverrides { get; }
        IDictionary HomePageOverrides { get; }

		IBlogCredentialsAccessor Credentials { get; }
		
		IBlogProviderButtonDescription[] ButtonDescriptions { get; }
		
		bool LastPublishFailed { get; set; }

		byte[] FavIcon { get; }
		byte[] Image { get; }
		byte[] WatermarkImage { get; }

		PageInfo[] Pages { get; set; }
		AuthorInfo[] Authors { get; set; }
		
		BlogPostCategory[] Categories { get; set; }
        BlogPostKeyword[] Keywords { get; set; }	


		FileUploadSupport FileUploadSupport { get; }
		IBlogFileUploadSettings FileUploadSettings { get; }
		
		IBlogFileUploadSettings AtomPublishingProtocolSettings { get; }
	}


	public enum FileUploadSupport
	{
		Weblog = 1,  // Weblog=1 for backcompat reasons, we used to have None=0
		FTP,
	} ;
	
	public interface IBlogFileUploadSettings
	{
		string GetValue(string name) ;
		void SetValue(string name, string value);
        string[] Names { get; }
	}
	
	public class WriterEditingManifestDownloadInfo
	{
		public WriterEditingManifestDownloadInfo(string sourceUrl)
			: this(sourceUrl, DateTime.MinValue, DateTime.MinValue, String.Empty )
		{
		}

		public WriterEditingManifestDownloadInfo(string sourceUrl, DateTime expires, DateTime lastModified, string eTag)
		{
			_sourceUrl = sourceUrl ;
			_expires = expires ;
			_lastModified = lastModified ;
			_eTag = eTag ;
		}

		public string SourceUrl
		{
			get { return _sourceUrl; }
		}
		private readonly string _sourceUrl ;

		public DateTime Expires
		{
			get { return _expires; }
		}
		private readonly DateTime _expires ;

		public DateTime LastModified
		{
			get { return _lastModified; }
		}
		private readonly DateTime _lastModified ;

		public string ETag
		{
			get { return _eTag; }
		}
		private readonly string _eTag ;
	}
	
	
}
