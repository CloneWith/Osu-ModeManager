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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using MahApps.Metro.IconPacks;
using Octokit;
using OsuModeManager.Extensions;
using OsuModeManager.Windows;

#endregion

namespace OsuModeManager
{
    public struct Gamemode : IEquatable<Gamemode>, ICloneable
    {
        public string GitHubUser;
        public string GitHubRepo;
        public string GitHubTagVersion;
        public string RulesetFilename;

        public UpdateStatus UpdateStatus;

        public Visibility DisplayAnyIcon =>
            UpdateStatus == UpdateStatus.Unchecked ? Visibility.Collapsed : Visibility.Visible;

        public PackIconMaterialKind DisplayIconType
        {
            get
            {
                switch (UpdateStatus)
                {
                    case UpdateStatus.Unchecked:
                        return PackIconMaterialKind.HelpRhombusOutline;
                    case UpdateStatus.UpToDate:
                        return PackIconMaterialKind.CheckboxMarkedCircleOutline;
                    case UpdateStatus.UpdateRequired:
                        return PackIconMaterialKind.Update;
                    case UpdateStatus.FileMissing:
                        return PackIconMaterialKind.Download;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        public string DisplayName => ToString();

        public static Gamemode CreateInstance(string GitHubUser, string GitHubRepo, string TagVersion = null,
            string RulesetFilename = null, UpdateStatus UpdateStatus = default)
        {
            return new Gamemode(GitHubUser, GitHubRepo, TagVersion, RulesetFilename, UpdateStatus);
        }

        public Gamemode(string GitHubUser, string GitHubRepo, string TagVersion, string RulesetFilename,
            UpdateStatus UpdateStatus = default)
        {
            this.GitHubUser = GitHubUser;
            this.GitHubRepo = GitHubRepo;
            GitHubTagVersion = TagVersion;
            this.RulesetFilename = RulesetFilename;
            this.UpdateStatus = UpdateStatus;
        }

        #region GitHub

        public async Task<(bool, Release)> TryGetLatestReleaseAsync()
        {
            IEnumerable<Release> Releases = await MainWindow.Client.Repository.Release.GetAll(GitHubUser, GitHubRepo);
            //Releases = Releases.Reverse();
            return Releases.TryGetFirst(out var LatestRelease) ? (true, LatestRelease) : (false, null);
        }

        public static bool TryGetRulesetAsync(Release Release, out ReleaseAsset FoundAsset)
        {
            foreach (var Asset in Release.Assets.Where(Asset => Asset.Name.ToLowerInvariant().EndsWith(".dll")))
            {
                FoundAsset = Asset;
                return true;
            }

            FoundAsset = null;
            return false;
        }

        public async Task<(bool, Release)> TryGetCurrentReleaseAsync()
        {
            // ReSharper disable once LoopCanBePartlyConvertedToQuery
            foreach (var Release in await MainWindow.Client.Repository.Release.GetAll(GitHubUser, GitHubRepo))
                if (Release.TagName.Equals(GitHubTagVersion, StringComparison.InvariantCultureIgnoreCase))
                    return (true, Release);
            return (false, null);
        }

        public async Task<(UpdateStatus, Release)> CheckForUpdate(DirectoryInfo LazerInstallationPath)
        {
            var (LatestSuccess, Latest) = await TryGetLatestReleaseAsync();
            var (CurrentSuccess, Current) = await TryGetCurrentReleaseAsync();

            if (LatestSuccess)
            {
                if (CurrentSuccess)
                {
                    if (Latest.TagName.Equals(Current.TagName, StringComparison.InvariantCultureIgnoreCase))
                    {
                        UpdateStatus = UpdateStatus.UpToDate;
                        if (LazerInstallationPath?.Exists == true)
                        {
                            if (LazerInstallationPath.TryGetRelativeFile(RulesetFilename, out var GamemodeFile) &&
                                GamemodeFile.Exists())
                                UpdateStatus = UpdateStatus.UpToDate;
                            else
                                UpdateStatus = UpdateStatus.FileMissing;
                            Debug.WriteLine("\tChecked if '" + GamemodeFile + "' exists... result? " + UpdateStatus);
                        }
                    }
                    else
                    {
                        UpdateStatus = UpdateStatus.UpdateRequired;
                    }
                }
                else
                {
                    UpdateStatus = UpdateStatus.UpdateRequired;
                }
            }
            else
            {
                UpdateStatus = UpdateStatus.Unchecked;
            }

            Debug.WriteLine("\t\tFinal Result: " + UpdateStatus);
            return (UpdateStatus, Latest);
        }

        #endregion

        #region Single Importing/Exporting

        public const char CharData = '';
        public const char CharLine = '';

        public string ExportGamemode()
        {
            return $"{GitHubUser}{CharData}{GitHubRepo}{CharData}{GitHubTagVersion}{CharData}{RulesetFilename}";
        }

        public static Gamemode? ImportGamemode(string ImportSection)
        {
            var Sections = ImportSection?.Split(CharData);
            if (Sections.TryGetAt(0, out var User) && Sections.TryGetAt(1, out var Repo) &&
                Sections.TryGetAt(2, out var Tag) &&
                Sections.TryGetAt(3, out var RulesetFile)) return CreateInstance(User, Repo, Tag, RulesetFile);
            return null;
        }

        #endregion

        #region File Importing/Exporting

        public static string ExportGamemodes(Gamemode[] Gamemodes)
        {
            var Result = "";
            for (var G = 0; G < Gamemodes.Length; G++)
                Result += (G > 0 ? CharLine.ToString() : "") + Gamemodes[G].ExportGamemode();
            return Result;
        }

        public static IEnumerable<Gamemode> ImportGamemodes(FileInfo GamemodeFile)
        {
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var Section in File.ReadAllText(GamemodeFile.FullName).Split(CharLine))
            {
                var SectionResult = ImportGamemode(Section);
                if (SectionResult.HasValue) yield return SectionResult.Value;
            }
        }

        #endregion

        #region Generic Overrides

        public override string ToString()
        {
            return $"{RulesetFilename} ({GitHubTagVersion})";
        }

        public override bool Equals(object Obj)
        {
            return Obj is Gamemode Gamemode && Equals(Gamemode);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                // ReSharper disable NonReadonlyMemberInGetHashCode
                var HashCode = GitHubUser != null ? GitHubUser.GetHashCode() : 0;
                HashCode = (HashCode * 397) ^ (GitHubRepo != null ? GitHubRepo.GetHashCode() : 0);
                HashCode = (HashCode * 397) ^ (GitHubTagVersion != null ? GitHubTagVersion.GetHashCode() : 0);
                HashCode = (HashCode * 397) ^ (RulesetFilename != null ? RulesetFilename.GetHashCode() : 0);
                // ReSharper restore NonReadonlyMemberInGetHashCode
                return HashCode;
            }
        }

        public bool Equals(Gamemode Other)
        {
            return GitHubUser == Other.GitHubUser &&
                   GitHubRepo == Other.GitHubRepo &&
                   GitHubTagVersion == Other.GitHubTagVersion &&
                   RulesetFilename == Other.RulesetFilename;
        }

        public object Clone()
        {
            return CreateInstance(GitHubUser, GitHubRepo, GitHubTagVersion, RulesetFilename, UpdateStatus);
        }

        public static bool operator ==(Gamemode Left, Gamemode Right)
        {
            return Left.Equals(Right);
        }

        public static bool operator !=(Gamemode Left, Gamemode Right)
        {
            return !(Left == Right);
        }

        #endregion
    }
}