// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections;
using System.IO;
using System.Reflection;
using Windows.Data.Xml.Dom;
// using OpenLiveWriter.CoreServices.ResourceDownloading;
using OpenLiveWriter.Extensibility.BlogClient;

namespace OpenLiveWriter.BlogClient.Providers
{
	public class BlogProviderManager
	{

		public static IList Providers
		{
			get
			{
				lock(_classLock)
				{
					if ( _providerTypes == null )
					{	
						_providerTypes = new ArrayList() ;
					
						// add xml-based provider definitions
						_providerTypes.AddRange( GetXmlBlogProviders() ) ;

						// add static providers (compiled in)

						// add provider plugins (XML and C#)


					}
					return _providerTypes ;
				}
			}
		}
		private static ArrayList _providerTypes ;
		private static object _classLock = new object();


		public static IBlogProvider FindProvider(string providerId)
		{
			lock(_classLock)
			{
				if (providerId == null || providerId == String.Empty)
					return null ;

				// try to find the provider
				foreach (IBlogProvider provider in Providers)
					if ( provider.Id == providerId )
						return provider ;

				// not found
				return null ;
			}
		}

		public static IBlogProvider FindProviderByName(string providerName)
		{
			lock(_classLock)
			{
				// try to find the provider
				foreach (IBlogProvider provider in Providers)
					if ( provider.Name == providerName )
						return provider ;

				// not found
				return null ;
			}
		}

		
		/// Reads the blog providers
		/// </summary>
		/// <returns>The blog providers</returns>
		private static IBlogProvider[] GetXmlBlogProviders()
		{

            return new IBlogProvider[0];

		    //return
		    //    CabbedXmlResourceFileDownloader.Instance.ProcessLocalResource(Assembly.GetExecutingAssembly(),
		    //                                                                            "Providers.BlogProvidersB5.xml",
		    //                                                                            ReadXmlBlogProviders) as
		    //    IBlogProvider[];
		}

		/// <summary>
		/// Generic processing method used for reading blog providers
		/// </summary>
		/// <param name="searchProviders">stream containing blog provider document</param>
		/// <returns>array of providers (BlogProvider[]) </returns>
        public static BlogProvider[] ReadXmlBlogProviders(XmlDocument providersDocument)
		{
			//pre-verify the XML actually contains the list of providers (fixes bug 309968)
			var providersNode = providersDocument.SelectSingleNode( "//providers" ) ;			
			if(providersNode == null)
				throw new Exception("Invalid blogProviders.xml file detected");
			
			// get the list of providers from the xml
			ArrayList providers = new ArrayList() ;

			var providerNodes = providersDocument.SelectNodes( "//providers/provider" ) ;
			foreach ( var providerNode in providerNodes )
			{
				providers.Add( new BlogProviderFromXml(providerNode) ) ;
			}
		

			// return list of providers
			return (BlogProvider[])providers.ToArray(typeof(BlogProvider)) ;
		}

	}
	
}
