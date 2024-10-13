#region Copyright (C) 2017-2020  Starflash Studios

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License (Version 3.0)
// as published by the Free Software Foundation.
// 
// More information can be found here: https://www.gnu.org/licenses/gpl-3.0.en.html

#endregion

#region Using Directives

using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Octokit;
using OsuModeManager.Extensions;

#endregion

namespace OsuModeManager.Windows
{
    public partial class GamemodeEditor
    {
#pragma warning disable IDE1006 // Naming Styles
        const string GitHubUrlDecoder = @"^(.*?:\/\/)?(.*?\..*?\/)?(?<User>.*?)\/(?<Repo>.*?)(\/.*?)?$";
#pragma warning restore IDE1006 // Naming Styles

#pragma warning disable IDE1006 // Naming Styles
        //const string KnownNameRegex = @"^.*?NAME ?: ?(?<NAME>.+?)$";
        const string KnownUrlRegex = @"^.*?\**?URL\**? ?: ?\**?(?<URL>.+?)$";
        //const string KnownStatusRegex = @"^.*?STATUS ?: ?(?<STATUS>.+?)$";
#pragma warning restore IDE1006 // Naming Styles
        public TaskCompletionSource<Gamemode> Result;
        public Gamemode ResultantGamemode;

        public GamemodeEditor()
        {
            InitializeComponent();

            Task.Run(async () =>
            {
                var KnownUrls = await GetKnownModeUrls(MainWindow.Client);
                Dispatcher.Invoke(() =>
                {
                    foreach (var KnownUrl in KnownUrls)
                    {
                        Debug.WriteLine($"\tGot: {KnownUrl}");
                        KnownModes.Items.Add(KnownUrl);
                    }
                });
            });
        }

        public static async Task<Gamemode> GetGamemodeEditor(Gamemode CurrentGamemode = default)
        {
            Debug.WriteLine("Update Status: " + CurrentGamemode.UpdateStatus);
            var Window = new GamemodeEditor();
            Window.Show();
            return await Window.GetGamemode(CurrentGamemode);
        }

        public async Task<Gamemode> GetGamemode(Gamemode CurrentGamemode = default)
        {
            if (Result != null)
            {
                Debug.WriteLine("Please do not request multiple gamemodes at once.", "Warning");
                return default;
            }

            if (CurrentGamemode == default) return default;

            TextBoxGitHubURL.Text = $"https://github.com/{CurrentGamemode.GitHubUser}/{CurrentGamemode.GitHubRepo}/";
            //TextBoxGitHubUser.Text = CurrentGamemode.GitHubUser;
            //TextBoxGitHubRepo.Text = CurrentGamemode.GitHubRepo;
            TextBoxTagVersion.Text = CurrentGamemode.GitHubTagVersion;
            TextBoxRulsesetFilename.Text = CurrentGamemode.RulesetFilename;

            ResultantGamemode = CurrentGamemode;

            //await GetLatest();

            Result = new TaskCompletionSource<Gamemode>();
            var ResultGamemode = await Result.Task;

            Close();
            return ResultGamemode;
        }

        void SaveButton_Click(object Sender, RoutedEventArgs E)
        {
            var User = TextBoxGitHubUser.Text;
            var Repo = TextBoxGitHubRepo.Text;
            var Version = TextBoxTagVersion.Text;
            var RulesetFile = TextBoxRulsesetFilename.Text;

            if (User.IsNullOrEmpty() || Repo.IsNullOrEmpty() || Version.IsNullOrEmpty() || RulesetFile.IsNullOrEmpty())
            {
                MessageBox.Show("Invalid settings. Please make sure all fields are correctly filled.", Title + "・Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            ResultantGamemode = new Gamemode(User, Repo, Version, RulesetFile, ResultantGamemode.UpdateStatus);

            Result?.TrySetResult(ResultantGamemode);
        }

        void TextBoxGitHubURL_TextChanged(object Sender, TextChangedEventArgs E)
        {
            var Url = TextBoxGitHubURL.Text;
            DecodeGitHubUrl(Url, out var User, out var UserS, out var Repo, out var RepoS);
            if (UserS) TextBoxGitHubUser.Text = User;

            if (RepoS) TextBoxGitHubRepo.Text = Repo;

            GetLatestButton.IsEnabled = UserS && RepoS;
        }

        public static void DecodeGitHubUrl(string Url, out string User, out bool UserS, out string Repo, out bool RepoS)
        {
            User = Repo = string.Empty;
            UserS = RepoS = false;

            if (!Url.IsNullOrEmpty())
            {
                var DecodedUrl = Regex.Match(Url, GitHubUrlDecoder);

                var UserGroup = DecodedUrl.Groups["User"];
                if (UserGroup.Success)
                {
                    User = UserGroup.Value;
                    UserS = true;
                }

                var RepoGroup = DecodedUrl.Groups["Repo"];
                if (RepoGroup.Success)
                {
                    Repo = RepoGroup.Value;
                    RepoS = true;
                }
            }
        }

        async void GetLatestButton_Click(object Sender, RoutedEventArgs E)
        {
            GetLatestButton.IsEnabled = false;
            await GetLatest();
            GetLatestButton.IsEnabled = true;
        }

        async Task GetLatest()
        {
            var User = TextBoxGitHubUser.Text;
            var Repo = TextBoxGitHubRepo.Text;
            if ((await MainWindow.Client.Repository.Release.GetAll(User, Repo)).TryGetFirst(out var Release))
            {
                Dispatcher.Invoke(() => TextBoxTagVersion.Text = Release.TagName, DispatcherPriority.Normal);
                foreach (var Asset in Release.Assets.Where(Asset => Asset.Name.ToLowerInvariant().EndsWith(".dll")))
                {
                    Dispatcher.Invoke(() => TextBoxRulsesetFilename.Text = Asset.Name, DispatcherPriority.Normal);
                    ResultantGamemode.UpdateStatus = UpdateStatus.UpToDate;
                    break;
                }
            }
        }

        void MetroWindow_Closing(object Sender, CancelEventArgs E)
        {
            Result?.TrySetResult(ResultantGamemode);
        }

        void TextBox_InvalidateUpdateCheck(object Sender, TextChangedEventArgs E)
        {
            ResultantGamemode.UpdateStatus = UpdateStatus.Unchecked;
        }

        static async Task<List<string>> GetKnownModeUrls(GitHubClient Client)
        {
            var Urls = new List<string>();
            var Comments = await Client.Issue.Comment.GetAllForIssue("ppy", "osu", 5852);
            //Regex NameR = new Regex(KnownNameRegex);
            var UrlR = new Regex(KnownUrlRegex);
            //Regex StatusR = new Regex(KnownStatusRegex);
            foreach (var C in Comments)
            {
                //if (L.Default) { continue; }
                var D = C.Body.GetLines();

                //string Name = "Unknown";
                var Url = string.Empty;
                //string Status = "Unknown";

                // ReSharper disable once LoopCanBePartlyConvertedToQuery
                Debug.WriteLine($"[{C.Id}]-----------");
                Debug.WriteLine($"\tDescription: '{C.Body}'");
                foreach (var DL in D)
                {
                    //Match NM = NameR.Match(DL);
                    //Group N = NM.Groups["NAME"];
                    //if (NM.Success && N.Success) { Name = N.Value; }

                    Debug.WriteLine($"\t\t'{DL}'");
                    var UM = UrlR.Match(DL);
                    var UG = UM.Groups["URL"];
                    if (UM.Success && UG.Success)
                    {
                        DecodeGitHubUrl(UG.Value, out var U, out var US, out var R, out var UR);
                        if (!US) Debug.WriteLine($"\tPost {C} ({C.Id}) has no valid github user.");
                        if (!UR) Debug.WriteLine($"\tPost {C} ({C.Id}) has no valid github repo.");

                        Urls.Add($"{U}/{R}");
                        break;
                    }

                    //Match SM = StatusR.Match(DL);
                    //Group S = NM.Groups["STATUS"];
                    //if (SM.Success && S.Success) { Status = S.Value; }
                }

                //if (Name.IsNullOrEmpty()) { Debug.WriteLine($"Post {L} ({L.Id}) has no gamemode name."); }
                if (Url.IsNullOrEmpty())
                    Debug.WriteLine($"Post {C} ({C.Id}) has no gamemode URL.");
                else
                    Urls.Add(Url);
                //if (Status.IsNullOrEmpty()) { Debug.WriteLine($"Post {L} ({L.Id}) has no gamemode status."); }
            }

            return Urls;
        }

        void KnownModes_SelectionChanged(object Sender, SelectionChangedEventArgs E)
        {
            var KnownUrl = (Sender as ListView)?.SelectedItem as string;
            if (string.IsNullOrEmpty(KnownUrl)) return;
            TextBoxGitHubURL.Text = KnownUrl;
            GetLatestButton_Click(GetLatestButton, null);
            KnownModes.SelectedIndex = -1;
        }
    }
}