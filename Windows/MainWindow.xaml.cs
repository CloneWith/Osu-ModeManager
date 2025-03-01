﻿#region Copyright (C) 2017-2020  Starflash Studios

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License (Version 3.0)
// as published by the Free Software Foundation.
// 
// More information can be found here: https://www.gnu.org/licenses/gpl-3.0.en.html

#endregion

#region Using Directives

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Octokit;
using OsuModeManager.Extensions;
using OsuModeManager.Properties;

#endregion

namespace OsuModeManager.Windows
{
    public partial class MainWindow
    {
        public static DirectoryInfo LazerInstallationPath;
        public static GitHubClient Client;

        public List<Gamemode> LoadedGamemodes = new List<Gamemode>();

        public MainWindow()
        {
            UpdateLazerInstallationPath();
            InitializeComponent(); //Setup dependencies before initialisation
            Client = new GitHubClient(new ProductHeaderValue("Osu!ModeManager", GetCurrentApplicationVersionName()));

            /*
             * Need to fetch the token when:
             * - Specified in command line arguments
             * - A valid token doesn't exist
             * - The latest token is older than 8 hours
             */
            if (Environment.GetCommandLineArgs().Contains("--flush") || Settings.Default.LatestToken.IsNullOrEmpty() ||
                DateTime.UtcNow - Settings.Default.LatestTokenCreationTime >= TimeSpan.FromHours(8))
            {
                Debug.WriteLine("New OAuth Token Required");
                AuthoriseButton.IsEnabled = true;
            }
            else
            {
                Debug.WriteLine("Reusing OAuth Token");
                Client.Credentials = new Credentials(Settings.Default.LatestToken);
                Dispatcher.Invoke(async () => await SelfUpdateWindow.CreateUpdateChecker(this),
                    DispatcherPriority.Normal);
                AuthoriseButton.Content = "Already logged in!";
                AuthoriseButton.IsEnabled = false;
            }

            LazerVersionCombo.SelectedIndex = 0;

            LoadGamemodes();

            Debug.WriteLine("osu!lazer is installed at: " + LazerInstallationPath.FullName);
        }

        public ObservableCollection<Gamemode> Gamemodes { get; private set; } = new ObservableCollection<Gamemode>();

        public ObservableCollection<DirectoryInfo> LazerInstallations { get; private set; } =
            new ObservableCollection<DirectoryInfo>();

        public static Version GetCurrentApplicationVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version;
        }

        public static string GetCurrentApplicationVersionName()
        {
            return GetCurrentApplicationVersion().ToString();
        }

        public void UpdateLazerInstallationPath(bool CheckResources = true)
        {
            if (!CheckResources ||
                !Settings.Default.OsuLazerInstallationPath.TryParseDirectoryInfo(out var NewInstallationPath))
            {
                NewInstallationPath = FileExtensions.GetUserDirectory(
                    SelectedPath:
                    $@"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\\osulazer\\",
                    Description:
                    "Please locate your osu!lazer installation directory (generally C:\\Users\\[USER-NAME]\\AppData\\Local\\osulazer\\)");

                if (NewInstallationPath == null || !NewInstallationPath.Exists)
                {
                    Environment.Exit(0);
                    return;
                }
            }

            Settings.Default.OsuLazerInstallationPath = NewInstallationPath.FullName;
            Settings.Default.Save();

            LazerInstallationPath = NewInstallationPath;

            LazerInstallations = new ObservableCollection<DirectoryInfo>();
            foreach (var LazerInstallation in GetLazerVersions())
            {
                LazerInstallations.Add(LazerInstallation);
                Debug.WriteLine("Found Installation: " + LazerInstallation);
            }
        }

        //Call OAuth flow in OAuthWindow and get token back
        async void AuthoriseButton_Click(object Sender, RoutedEventArgs E)
        {
            AuthoriseButton.IsEnabled = false;
            var OAuthWindow = new OAuthWindow();
            OAuthWindow.Show();

            var Token = await OAuthWindow.GetOAuthToken(Client, Properties.Resources.ClientID,
                Properties.Resources.ClientSecret);

            OAuthWindow.Close();
            if (!Token.IsNullOrEmpty())
            {
                Debug.WriteLine("Got Token: " + Token);
                Client.Credentials = new Credentials(Token);
                Settings.Default.LatestToken = Token;
                Settings.Default.LatestTokenCreationTime = DateTime.UtcNow;
                Settings.Default.Save();

                await Dispatcher.Invoke(async () =>
                {
                    //Called on OAuth Success
                    AuthoriseButton.IsEnabled = false;
                    AuthoriseButton.Content = "Already logged in!";
                    await SelfUpdateWindow.CreateUpdateChecker(this);
                }, DispatcherPriority.Normal);
                return;
            }

            Dispatcher.Invoke(() => AuthoriseButton.IsEnabled = true,
                DispatcherPriority.Normal); //Called on OAuth Failure
        }

        void GamemodeFolderOpen_Click(object Sender, RoutedEventArgs E)
        {
            var SelectedFolder = GetSelectedLazerInstall();
            if (SelectedFolder.Exists() &&
                SelectedFolder.TryGetRelativeFile("osu.Game.Rulesets.Osu.dll", out var SelectFile))
            {
                SelectFile.SelectInExplorer();
                Debug.WriteLine("Selecting '" + SelectFile + "' in explorer");
            }
            else
            {
                LazerInstallationPath.OpenInExplorer();
                Debug.WriteLine("Opening '" + LazerInstallationPath + "' in explorer");
            }
        }

        #region File Locations

        public DirectoryInfo GetCurrentLazerPath()
        {
            return (DirectoryInfo)LazerVersionCombo.SelectedItem;
        }

        public static List<DirectoryInfo> GetLazerVersions()
        {
            // Detect Velopack base versions (newer, higher priority)
            var AppVersions = LazerInstallationPath?.GetDirectories("current").ToList();

            // Detect Squirrel based versions
            if (AppVersions == null)
                AppVersions = LazerInstallationPath?.GetDirectories("app-*").ToList() ?? new List<DirectoryInfo>();

            AppVersions.Reverse();
            return AppVersions;
        }

        //public static List<FileInfo> GetGamemodes(DirectoryInfo LazerVersion) => LazerVersion.GetFiles("osu.Game.Rulesets.*.dll").ToList();

        public const string GamemodesFile = "osu.Game.Rulesets.List.txt";

        /// <summary>Gets the gamemodes file.</summary>
        /// <returns></returns>
        public static FileInfo GetGamemodesFile()
        {
            if (LazerInstallationPath.TryGetRelativeFile(GamemodesFile, out var SearchFile))
            {
                if (!SearchFile.Exists) SearchFile.Create();
                return SearchFile;
            }

            return null;
        }

        public DirectoryInfo GetSelectedLazerInstall()
        {
            return LazerInstallations[LazerVersionCombo.SelectedIndex];
        }

        #endregion

        #region Gamemode List Editing

        async void GamemodeListAdd_Click(object Sender, RoutedEventArgs E)
        {
            var NewGamemode = await GamemodeEditor.GetGamemodeEditor();
            Dispatcher.Invoke(() =>
            {
                Gamemodes.Add(NewGamemode);

                ToggleUpdateCheckButton();
            }, DispatcherPriority.Normal);
        }

        async void GamemodeList_MouseDoubleClick(object Sender, MouseButtonEventArgs E)
        {
            var SelectedIndex = GamemodeList.SelectedIndex;
            if (SelectedIndex >= 0)
            {
                var NewGamemode = await GamemodeEditor.GetGamemodeEditor(Gamemodes[SelectedIndex]);
                Gamemodes[SelectedIndex] = NewGamemode;
            }
        }

        void GamemodeListRemove_Click(object Sender, RoutedEventArgs E)
        {
            var SelectedIndex = GamemodeList.SelectedIndex;
            if (SelectedIndex >= 0)
            {
                Gamemodes.RemoveAt(SelectedIndex);
                if (Gamemodes.Count > 0) GamemodeList.SelectedIndex = (SelectedIndex - 1).Clamp(0);

                ToggleUpdateCheckButton();
            }
        }

        void GamemodeListImport_Click(object Sender, RoutedEventArgs E)
        {
            var ImportFile = FileExtensions.GetUserFile(Filter: $"{GamemodesFile}|{GamemodesFile}|Any File (*.*)|*.*");
            if (ImportFile != null && ImportFile.Exists)
            {
                GetGamemodesFile().WriteAllText(ImportFile.ReadAllText());
                Dispatcher.Invoke(LoadGamemodes, DispatcherPriority.Normal);
            }
        }

        void GamemodeListSave_Click(object Sender, RoutedEventArgs E)
        {
            SaveGamemodes();
        }

        #endregion

        #region Update Checking

        public void ToggleUpdateCheckButton()
        {
            UpdateCheckButton.IsEnabled = GamemodeList.Items.Count > 0;
        }

        async void UpdateCheckButton_Click(object Sender, RoutedEventArgs E)
        {
            UpdateCheckButton.IsEnabled = false;
            Debug.WriteLine("Checking for updates...");
            var Updates = new Dictionary<Gamemode, Release>();
            for (var C = Gamemodes.Count - 1; C >= 0; C--) UpdateGamemodeStatus(C, UpdateStatus.Unchecked, C == 0);

            for (var G = 0; G < Gamemodes.Count; G++)
            {
                var Gamemode = Gamemodes[G];
                (var UpdateStatus, var LatestRelease) = await Gamemode.CheckForUpdate(GetSelectedLazerInstall());

                switch (UpdateStatus)
                {
                    case UpdateStatus.Unchecked:
                        Debug.WriteLine(
                            $"An error occurred when checking {Gamemode} ({Gamemode.GitHubRepo} by {Gamemode.GitHubUser})");
                        break;
                    case UpdateStatus.UpToDate:
                        Debug.WriteLine(Gamemode + " is up to date.");
                        break;
                    case UpdateStatus.UpdateRequired:
                    case UpdateStatus.FileMissing:
                        Debug.WriteLine(Gamemode.UpdateStatus + " for " + Gamemode + " | Newest release: " +
                                        LatestRelease.TagName);

                        if (!Updates.ContainsKey(Gamemodes[G])) Updates.Add(Gamemodes[G], LatestRelease);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                UpdateGamemodeStatus(G, UpdateStatus, true);
                UpdateCheckButton.IsEnabled = true;
            }

            var UpdateWindow = new UpdateWindow(this, Updates);
            UpdateWindow.Show();
#pragma warning disable IDE1006 // Naming Styles
            UpdateWindow.Closing += (_, __) =>
                Dispatcher.Invoke(() => MainGrid.IsEnabled = true, DispatcherPriority.Normal);
#pragma warning restore IDE1006 // Naming Styles
            MainGrid.IsEnabled = false;

            SaveGamemodes();
        }

        public void UpdateGamemode(int Index, Gamemode NewGamemode, bool UpdateDispatcher = false)
        {
            Gamemodes.RemoveAt(Index);
            Gamemodes.Insert(Index, NewGamemode);

            if (UpdateDispatcher)
                Dispatcher.Invoke(GamemodeList.GetBindingExpression(ItemsControl.ItemsSourceProperty).UpdateTarget,
                    DispatcherPriority.Normal);
        }

        public void UpdateGamemodeStatus(int Index, UpdateStatus UpdateStatus, bool UpdateDispatcher = false)
        {
            var GamemodeClone = (Gamemode)Gamemodes[Index].Clone();
            GamemodeClone.UpdateStatus = UpdateStatus;
            UpdateGamemode(Index, GamemodeClone, UpdateDispatcher);
        }

        #endregion

        #region Saving / Loading

        public void LoadGamemodes()
        {
            var LoadFile = GetGamemodesFile();
            if (LoadFile.Exists())
            {
                Gamemodes = new ObservableCollection<Gamemode>(Gamemode.ImportGamemodes(LoadFile));
                GamemodeList.GetBindingExpression(ItemsControl.ItemsSourceProperty).UpdateTarget();
                LoadedGamemodes = Gamemodes.ToList();
            }
        }

        public void SaveGamemodes()
        {
            var SaveFile = GetGamemodesFile();
            SaveFile.WriteAllText(Gamemode.ExportGamemodes(Gamemodes.ToArray()));
            LoadedGamemodes = Gamemodes.ToList();
            Debug.WriteLine("Saving...");
        }

        void MainWindowElement_Closing(object Sender, CancelEventArgs E)
        {
            if (!Gamemodes.SequenceEqual(LoadedGamemodes))
                // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
                switch (MessageBox.Show("Unsaved changes. Would you like to save now?", Title + "・Unsaved Changes",
                            MessageBoxButton.YesNoCancel, MessageBoxImage.Warning))
                {
                    case MessageBoxResult.Yes:
                    case MessageBoxResult.OK:
                        SaveGamemodes();
                        break;
                    //case MessageBoxResult.No:
                    //    break;
                    case MessageBoxResult.Cancel:
                        E.Cancel = true;
                        break;
                }
        }

        #endregion
    }
}