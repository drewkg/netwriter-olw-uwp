// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Diagnostics;
using System.Globalization;
using OpenLiveWriter.Localization;

namespace OpenLiveWriter.CoreServices
{
	public class DisplayableException : Exception
	{
	
		public DisplayableException(string titleFormat, string textFormat, params object[] textFormatArgs)
			: base(FormatMessage(titleFormat, textFormat, textFormatArgs))
		{
			_titleFormat = titleFormat ;
			_textFormat = textFormat ;
			_textFormatArgs = textFormatArgs ;
		}


		public string Title
		{
			get
			{
				return FormatString(_titleFormat, _textFormatArgs) ;
			}
		}

		public string Text
		{
			get
			{
				return FormatString(_textFormat, _textFormatArgs) ;
			}
		}
		
		
		private static string FormatMessage(string titleFormat, string textFormat, params object[] textFormatArgs)
		{
			string formattedTitle = FormatString(titleFormat, textFormatArgs) ;
			string formattedText = FormatString(textFormat, textFormatArgs) ;
			return String.Format(CultureInfo.CurrentCulture, string.Join(", ", textFormatArgs)) ;
		}

		private static string FormatString(string text, params object[] textFormatArgs)
		{
			try
			{
				return String.Format(CultureInfo.CurrentCulture, ReplaceNewlines(text), textFormatArgs) ;
			}
			catch(Exception ex)
			{
				Debug.Fail("Exception formatting error message for exception (check number of arguments): " + ex.ToString()) ;
				return text ;
			}
		}

		private static string ReplaceNewlines(string text)
		{
			return text.Replace("{NL}", "\r\n") ;
		}

		private string _titleFormat ;
		private string _textFormat ;
		private object[] _textFormatArgs ;
	}
}
