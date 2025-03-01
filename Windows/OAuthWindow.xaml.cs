﻿#region Copyright (C) 2017-2020  Starflash Studios

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License (Version 3.0)
// as published by the Free Software Foundation.
// 
// More information can be found here: https://www.gnu.org/licenses/gpl-3.0.en.html

#endregion

#region Using Directives

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CefSharp;
using Octokit;
using OsuModeManager.Extensions;

#endregion

namespace OsuModeManager.Windows
{
    public partial class OAuthWindow
    {
        //https://github.com/starflash-studios/Osu-ModeManager?code=67d7e5fa77a80370145f
        public const string OAuthCodeDecoder = @"(.+?)\?code=(?<OAuthCode>.+?)(?:\?.*?|$)";
        public static TaskCompletionSource<string> OAuthCodeSource;

        public static readonly Uri Redirect = new Uri("about:blank");

        public OAuthWindow()
        {
            InitializeComponent();
        }

        public async Task<string> GetOAuthToken(GitHubClient Client, string ClientID, string ClientSecret)
        {
            if (OAuthCodeSource != null)
            {
                Debug.WriteLine("Please do not request multiple tokens at once!", "Warning");
                return string.Empty;
            }

            var Request = new OauthLoginRequest(ClientID)
            {
                Scopes = { "user", "notifications" }
            };

            var OAuthLoginUrl = Client.Oauth.GetGitHubLoginUrl(Request);
            Debug.WriteLine($"Logging into: {OAuthLoginUrl.AbsoluteUri}");
            Browser.Address = OAuthLoginUrl.AbsoluteUri;
            //Browser.Navigate(OAuthLoginUrl);

            OAuthCodeSource = new TaskCompletionSource<string>();
            var Code = await OAuthCodeSource.Task;
            Debug.WriteLine("Get code: " + Code);

            if (Code.IsNullOrEmpty())
            {
                OAuthCodeSource = null;
                return string.Empty;
            }

            var OAuthToken = await GenerateTokenFromOAuth(Client, ClientID, ClientSecret, Code);

            OAuthCodeSource = null;
            return OAuthToken;
        }

        public static async Task<string> GenerateTokenFromOAuth(GitHubClient Client, string ClientID,
            string ClientSecret, string Code)
        {
            var Request = new OauthTokenRequest(ClientID, ClientSecret, Code);
            var Token = await Client.Oauth.CreateAccessToken(Request);

            return Token.AccessToken;
        }

        void Browser_LoadingStateChanged(object Sender, LoadingStateChangedEventArgs E)
        {
            if (OAuthCodeSource == null) return;
            var CurrentAddress = E.Browser.FocusedFrame.Url;

            var CodeMatch = Regex.Match(CurrentAddress, OAuthCodeDecoder);
            if (CodeMatch.Success)
            {
                var CodeGroup = CodeMatch.Groups["OAuthCode"];
                if (CodeGroup.Success) OAuthCodeSource.TrySetResult(CodeGroup.Value);
            }

            Dispatcher.Invoke(() =>
            {
                var BrowserTitle = Browser.Title;
                if (!BrowserTitle.IsNullOrEmpty()) Title = BrowserTitle;
            }, DispatcherPriority.Normal);
        }

        void Browser_Loaded(object Sender, RoutedEventArgs E)
        {
            Dispatcher.Invoke(() => Browser.Focus(), DispatcherPriority.Input);
        }

        void MetroWindow_Closing(object Sender, CancelEventArgs E)
        {
            OAuthCodeSource?.TrySetResult(null);
        }
    }
}