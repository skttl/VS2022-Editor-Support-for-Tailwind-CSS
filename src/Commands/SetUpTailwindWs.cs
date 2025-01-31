﻿using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Node;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense
{
    [Command(PackageGuids.guidVSPackageCmdSetString, PackageIds.SetUpTailwindWsCmdId)]
    internal sealed class SetUpTailwindWs : BaseCommand<SetUpTailwindWs>
    {
        protected override async Task InitializeCompletedAsync()
        {
            SolutionExplorerSelection = await VS.GetMefServiceAsync<SolutionExplorerSelectionService>();
            TailwindSetUpProcess = await VS.GetMefServiceAsync<TailwindSetUpProcess>();
            SettingsProvider = await VS.GetMefServiceAsync<SettingsProvider>();
            FileFinder = await VS.GetMefServiceAsync<FileFinder>();
        }

        internal SolutionExplorerSelectionService SolutionExplorerSelection { get; set; }
        internal TailwindSetUpProcess TailwindSetUpProcess { get; set; }
        internal SettingsProvider SettingsProvider { get; set; }
        internal FileFinder FileFinder { get; set; }

        protected override void BeforeQueryStatus(EventArgs e)
        {
            var settings = ThreadHelper.JoinableTaskFactory.Run(SettingsProvider.GetSettingsAsync);

            var selected = SolutionExplorerSelection.CurrentSelectedItemFullPath;

            if (Path.GetExtension(selected) == "")
            {
                var path = ThreadHelper.JoinableTaskFactory.Run(FileFinder.GetCurrentMiscellaneousProjectPathAsync);

                Command.Visible = settings.EnableTailwindCss && string.IsNullOrEmpty(path) == false &&
                    path.TrimEnd(Path.DirectorySeparatorChar).Equals(selected.TrimEnd(Path.DirectorySeparatorChar), StringComparison.InvariantCultureIgnoreCase) &&
                        (settings.ConfigurationFiles.Count == 0 || settings.ConfigurationFiles.All(c =>
                        string.IsNullOrEmpty(c.Path) || File.Exists(c.Path) == false));
            }
            else
            {
                Command.Visible = false;
            }
        }

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            if (!TailwindSetUpProcess.IsSettingUp)
            {
                var directory = Path.GetDirectoryName(SolutionExplorerSelection.CurrentSelectedItemFullPath);

                // Check again to see if there were any changes since the last settings cache
                // User may have manually run the setup command, for example
                SettingsProvider.RefreshSettings();
                var settings = await SettingsProvider.GetSettingsAsync();

                if (settings.ConfigurationFiles.Count > 0 && settings.ConfigurationFiles.Any(c =>
                    !string.IsNullOrWhiteSpace(c.Path) && File.Exists(c.Path)))
                {
                    return;
                }

                await ThreadHelper.JoinableTaskFactory.RunAsync(() => TailwindSetUpProcess.RunAsync(directory, true));

                var configFile = Path.Combine(directory, "tailwind.config.js");

                settings.ConfigurationFiles.Add(new() { Path = configFile, IsDefault = true, ApplicableLocations = [] });
                await SettingsProvider.OverrideSettingsAsync(settings);

                var fileNames = new string[]
                {
                    Path.Combine(directory, "package.json"),
                    Path.Combine(directory, "package-lock.json"),
                    configFile
                };

                try
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    var projects = await VS.Solutions.GetAllProjectHierarchiesAsync();

                    foreach (IVsHierarchy hierarchy in projects)
                    {
                        // Get itemId of the hierarchy so we can use it to get the SolutionItem
                        hierarchy.ParseCanonicalName(directory, out var itemId);

                        var folder = await SolutionItem.FromHierarchyAsync(hierarchy, itemId);

                        // Include the created file if the current iterated folder/project is the same as the one that is selected
                        if (Path.GetDirectoryName(folder.FullPath) == directory)
                        {
                            var project = (Project)folder;
                            await project.AddExistingFilesAsync(fileNames);
                        }
                    }
                }
                catch (Exception ex)
                {
                    await ex.LogAsync();
                    await VS.StatusBar.ShowMessageAsync("One or more Tailwind CSS items could not be shown in the Solution Explorer.");
                }
            }            
        }
    }
}
