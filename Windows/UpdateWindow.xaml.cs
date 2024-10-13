#region Copyright (C) 2017-2020  Starflash Studios

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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Octokit;
using OsuModeManager.Extensions;

#endregion

namespace OsuModeManager.Windows
{
    public partial class UpdateWindow
    {
        public const string OriginalTitle = "%%COUNT%% Update%%S%% Available";

        public bool FilesRecycled;
        public Dictionary<Gamemode, Release> GamemodeReleases;
        public MainWindow MainWindow;

        public UpdateWindow(MainWindow MainWindow, Dictionary<Gamemode, Release> Updates)
        {
            InitializeComponent();
            this.MainWindow = MainWindow;
            if (Updates != null && Updates.Count > 0)
            {
                GamemodeReleases = Updates;

                UpdateSingleButton.IsEnabled = false;
                Title = OriginalTitle.Replace("%%COUNT%%", GamemodeReleases.Count.ToString("N0"))
                    .Replace("%%S%%", GamemodeReleases.Count != 1 ? "s" : string.Empty);
                ConfirmCount.Content = GamemodeReleases.Count;
                ConfirmGrammar.Content = Title.Substring(Title.IndexOf(' ') + 1);
                ConfirmButton.Visibility = Visibility.Visible;
            }
            else
            {
                Title = OriginalTitle.Replace("%%COUNT%%", "0").Replace("%%S%%", "s");
                CloseButton.Visibility = Visibility.Visible;
            }
        }

        public ObservableCollection<Gamemode> DisplayGamemodes { get; } = new ObservableCollection<Gamemode>();

        void UpdateList_SelectionChanged(object Sender, SelectionChangedEventArgs E)
        {
            UpdateSingleButton.IsEnabled = UpdateList.SelectedIndex >= 0;
        }

        async void UpdateSingleButton_Click(object Sender, RoutedEventArgs E)
        {
            MainGrid.IsEnabled = false;
            var SelectedIndex = UpdateList.SelectedIndex;
            if (SelectedIndex >= 0)
            {
                await Update(SelectedIndex);
                if (FilesRecycled)
                {
                    FilesRecycled = false;
                    Process.Start(FileExtensions.Explorer.FullName, "shell:RecycleBinFolder");
                }
            }

            Dispatcher.Invoke(() =>
            {
                if (GamemodeReleases.Count <= 0) CloseButton.Visibility = Visibility.Visible;
                MainGrid.IsEnabled = true;
            }, DispatcherPriority.Normal);
        }

        async void UpdateAllButton_Click(object Sender, RoutedEventArgs E)
        {
            MainGrid.IsEnabled = false;
            for (var G = GamemodeReleases.Count - 1; G >= 0; G--)
            {
                await Update(G);
                Dispatcher.Invoke(UpdateList.GetBindingExpression(ItemsControl.ItemsSourceProperty).UpdateTarget,
                    DispatcherPriority.Normal);
            }

            if (FilesRecycled)
            {
                FilesRecycled = false;
                Process.Start(FileExtensions.Explorer.FullName, "shell:RecycleBinFolder");
            }

            Dispatcher.Invoke(() =>
            {
                if (GamemodeReleases.Count <= 0) CloseButton.Visibility = Visibility.Visible;
                MainGrid.IsEnabled = true;
            }, DispatcherPriority.Normal);
        }

        public async Task Update(int SelectedIndex)
        {
            var Gamemode = DisplayGamemodes[SelectedIndex];
            var CallerIndex = MainWindow?.Gamemodes.IndexOf(Gamemode) ?? -1;
            var Release = GamemodeReleases[Gamemode];

            ReleaseAsset FoundAsset = null;
            foreach (var Asset in Release.Assets.Where(Asset => Asset.Name.EndsWith(".dll"))) FoundAsset = Asset;

            if (FoundAsset == null) return;

            if (!await Update(MainWindow.GetCurrentLazerPath(), Gamemode, FoundAsset)) return;

            GamemodeReleases.Remove(Gamemode);
            DisplayGamemodes.RemoveAt(SelectedIndex);
            Title = OriginalTitle.Replace("%%COUNT%%", GamemodeReleases.Count.ToString("N0"))
                .Replace("%%S%%", GamemodeReleases.Count != 1 ? "s" : string.Empty);

            if (CallerIndex >= 0)
            {
                var GamemodeClone = (Gamemode)Gamemode.Clone();
                GamemodeClone.UpdateStatus = UpdateStatus.UpToDate;
                GamemodeClone.GitHubTagVersion = Release.TagName ?? GamemodeClone.GitHubTagVersion;
                MainWindow.UpdateGamemode(CallerIndex, GamemodeClone, true);
            }
        }

        //Returns bool specifying whether or not to continue with the process
        public async Task<bool> Update(DirectoryInfo Destination, Gamemode Gamemode, ReleaseAsset Asset)
        {
            if (Destination == null || !Destination.Exists)
            {
                Dispatcher.Invoke(
                    () =>
                    {
                        MessageBox.Show("Selected osu!lazer installation path is invalid.", Title, MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }, DispatcherPriority.Normal);
                MainWindow.UpdateLazerInstallationPath(false);
                return false;
            }

            Debug.WriteLine("Will Update " + Gamemode.RulesetFilename + " from " + Asset.BrowserDownloadUrl);

            var DestinationFile = Destination.TryGetRelativeFile(Gamemode.RulesetFilename, out var File) ? File : null;
            Debug.WriteLine("\tDestination: " + DestinationFile?.FullName);

            if (DestinationFile.Exists()) RecycleFile(DestinationFile);

            await DownloadFileAsync(new Uri(Asset.BrowserDownloadUrl), DestinationFile);
            return true;
        }

        public static async Task DownloadFileAsync(Uri DownloadUri, FileInfo Destination)
        {
            try
            {
                using (var WebClient = new WebClient())
                {
                    WebClient.Credentials = CredentialCache.DefaultNetworkCredentials;
                    await WebClient.DownloadFileTaskAsync(DownloadUri, Destination.FullName);
                }
#pragma warning disable CA1031 // Do not catch general exception types
            }
            catch (Exception)
            {
#pragma warning restore CA1031 // Do not catch general exception types
                Debug.WriteLine("Failed to download file: " + DownloadUri, "Error");
            }
        }

        public static async Task DownloadMultipleFilesAsync(List<(Uri DownloadUri, FileInfo Destination)> Downloads)
        {
            await Task.WhenAll(Downloads.Select(Download =>
                DownloadFileAsync(Download.DownloadUri, Download.Destination)));
        }

        public void RecycleFile(FileInfo File)
        {
            File.Recycle();
            FilesRecycled = true; //Flag
        }

        void ConfirmButton_Click(object Sender, RoutedEventArgs E)
        {
            ConfirmButton.IsEnabled = false;
            ConfirmButton.Visibility = Visibility.Collapsed;
            foreach (var Gamemode in GamemodeReleases.Keys) DisplayGamemodes.Add(Gamemode);
        }

        void CloseButton_Click(object Sender, RoutedEventArgs E)
        {
            Close();
        }

        void UpdateList_MouseDoubleClick(object Sender, MouseButtonEventArgs E)
        {
            var SelectedIndex = UpdateList.SelectedIndex;
            if (SelectedIndex >= 0)
            {
                var Release = GamemodeReleases[DisplayGamemodes[SelectedIndex]];
                _ = ReleaseWindow.ShowRelease(Release);
            }
        }
    }
}