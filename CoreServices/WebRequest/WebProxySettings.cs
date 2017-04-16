// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Globalization;
using System.Text.RegularExpressions;
using OpenLiveWriter.CoreServices.Diagnostics;

namespace OpenLiveWriter.CoreServices
{

	public sealed class WebProxySettings
	{
	    public static int HttpRequestTimeout { get; set; }

		public static bool ProxyEnabled
		{
            get; set;
        }

		public static string Hostname
		{
            get; set;
        }

		public static int Port
		{
            get; set;
        }

		public static string Username
		{
            get; set;
        }

		public static string Password
		{
            get; set;
        }

		#region Class Configuration (location of settings, etc)



		//private static SettingsPersisterHelper WriteSettingsKey
		//{
		//	get
		//	{
		//		return _settingsKey;
		//	}
		//}

		//private static SettingsPersisterHelper ReadSettingsKey
		//{
		//	get
		//	{
		//		return _readSettingsKey;
		//	}
		//}

		//private static SettingsPersisterHelper _settingsKey;
		//private static SettingsPersisterHelper _readSettingsKey;

		static WebProxySettings()
		{
			//_settingsKey = ApplicationEnvironment.PreferencesSettingsRoot.GetSubSettings("WebProxy");
			//if (ApplicationDiagnostics.ProxySettingsOverride != null)
			//{
			//	Match m = Regex.Match(ApplicationDiagnostics.ProxySettingsOverride,
			//				@"^ ( (?<username>[^@:]+) : (?<password>[^@:]+) @)? (?<host>[^@:]+) (:(?<port>\d*))? $",
			//				RegexOptions.IgnorePatternWhitespace | RegexOptions.ExplicitCapture);
			//	if (m.Success)
			//	{
			//		string username = m.Groups["username"].Value;
			//		string password = m.Groups["password"].Value;
			//		string host = m.Groups["host"].Value;
			//		string port = m.Groups["port"].Value;

			//		_readSettingsKey = new SettingsPersisterHelper(new MemorySettingsPersister());

			//		_readSettingsKey.SetBoolean("Enabled", true);

			//		if (!string.IsNullOrEmpty(username))
			//			_readSettingsKey.SetString("Username", username);
			//		if (!string.IsNullOrEmpty(password))
			//			_readSettingsKey.SetEncryptedString("Password", password);

			//		_readSettingsKey.SetString("Hostname", host);

			//		if (!string.IsNullOrEmpty(port))
			//			_readSettingsKey.SetInt32("Port", int.Parse(port, CultureInfo.InvariantCulture));
			//	}
			//}

			//if (_readSettingsKey == null)
			//	_readSettingsKey = _settingsKey;
		}


		#endregion
	}






}
