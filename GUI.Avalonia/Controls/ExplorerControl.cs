using System.Globalization;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using GUI.Utils;
using ValveKeyValue;
using ValveResourceFormat.IO;

namespace GUI.Controls
{
    /// <summary>
    /// Start page listing bookmarks, recent files and the VPKs of installed Steam games.
    /// </summary>
    sealed class ExplorerControl : DockPanel
    {
        private sealed record Section(string Title, List<(string Text, string Path)> Files);

        public static readonly List<GameFolderLocator.SteamLibraryGameInfo> SteamGames = [];

        private readonly TextBox filterTextBox;
        private readonly TreeView treeView;
        private readonly List<Section> sections = [];
        private readonly TextBlock scanStatus;

        public ExplorerControl()
        {
            filterTextBox = new TextBox
            {
                PlaceholderText = "Filter files (vpk names, game names)",
                Margin = new(6),
            };
            filterTextBox.TextChanged += (_, _) => Rebuild();

            scanStatus = new TextBlock { Text = "Scanning game folders...", Margin = new(8, 0, 8, 6), Opacity = 0.7 };

            treeView = new TreeView();
            treeView.DoubleTapped += OnDoubleTapped;
            treeView.KeyDown += OnTreeKeyDown;

            var openButton = new Button { Content = "Open file...", Margin = new(6, 6, 0, 6) };
            openButton.Click += async (_, _) => await Program.MainForm.OpenFilesFromDialogAsync().ConfigureAwait(true);

            var top = new DockPanel();
            SetDock(openButton, Dock.Left);
            top.Children.Add(openButton);
            top.Children.Add(filterTextBox);

            SetDock(top, Dock.Top);
            SetDock(scanStatus, Dock.Top);
            Children.Add(top);
            Children.Add(scanStatus);
            Children.Add(treeView);

            sections.Add(new Section("Bookmarks", [.. Settings.Config.BookmarkedFiles.Select(static f => (f, f))]));
            sections.Add(new Section("Recent files", [.. Enumerable.Reverse(Settings.Config.RecentFiles).Select(static f => (f, f))]));

            Rebuild();

            _ = ScanAsync();
        }

        private async Task ScanAsync()
        {
            try
            {
                var found = await Task.Run(ScanForSteamGames).ConfigureAwait(true);
                sections.AddRange(found);
                scanStatus.IsVisible = false;
                Rebuild();
            }
            catch (Exception e)
            {
                Log.Error(nameof(ExplorerControl), e.ToString());
                scanStatus.Text = e.Message;
            }
        }

        private void Rebuild()
        {
            var filter = filterTextBox.Text?.Trim() ?? string.Empty;

            treeView.Items.Clear();

            foreach (var section in sections)
            {
                var sectionMatches = filter.Length > 0 && section.Title.Contains(filter, StringComparison.OrdinalIgnoreCase);
                var files = filter.Length == 0 || sectionMatches
                    ? section.Files
                    : section.Files.Where(f => f.Text.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

                if (files.Count == 0 && filter.Length > 0)
                {
                    continue;
                }

                var sectionItem = new TreeViewItem
                {
                    Header = $"{section.Title} ({files.Count})",
                    IsExpanded = filter.Length > 0 || section.Files.Count <= 20,
                };

                foreach (var (text, path) in files)
                {
                    var item = new TreeViewItem { Header = text, Tag = path };
                    ToolTip.SetTip(item, path);
                    sectionItem.Items.Add(item);
                }

                treeView.Items.Add(sectionItem);
            }
        }

        private void OpenSelected()
        {
            if (treeView.SelectedItem is TreeViewItem { Tag: string path })
            {
                if (File.Exists(path))
                {
                    Program.MainForm.OpenFile(path);
                }
                else
                {
                    Log.Error(nameof(ExplorerControl), $"File '{path}' does not exist.");
                }
            }
        }

        private void OnDoubleTapped(object? sender, TappedEventArgs e) => OpenSelected();

        private void OnTreeKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OpenSelected();
                e.Handled = true;
            }
        }

        private static List<Section> ScanForSteamGames()
        {
            var result = new List<Section>();

            if (GameFolderLocator.SteamPath == null)
            {
                Dispatcher.UIThread.Post(() => Log.Info(nameof(ExplorerControl), "Steam installation not found, game folders were not scanned."));
                return result;
            }

            lock (SteamGames)
            {
                if (SteamGames.Count == 0)
                {
                    SteamGames.AddRange(GameFolderLocator.FindAllSteamGames()
                        // Ignore Apex Legends, Titanfall, Titanfall 2 because Respawn has customized VPK format and VRF can't open it
                        .Where(static gameInfo => gameInfo.AppID is not (1237970 or 1454890 or 1172470))
                        .OrderBy(static gameInfo => gameInfo.AppID));
                }
            }

            var enumerationOptions = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = 6,
                BufferSize = 65536,
                IgnoreInaccessible = true,
            };

            static bool VpkPredicate(ref FileSystemEntry entry)
            {
                if (entry.IsDirectory || !entry.FileName.EndsWith(".vpk", StringComparison.Ordinal))
                {
                    return false;
                }

                if (!Regexes.VpkNumberArchive().IsMatch(entry.FileName))
                {
                    return true;
                }

                // If we matched dota_683.vpk, make sure dota_dir.vpk exists before excluding it from results
                var fixedPackage = $"{entry.ToFullPath()[..^8]}_dir.vpk";
                return !File.Exists(fixedPackage);
            }

            var games = SteamGames.DistinctBy(static game => game.GamePath, StringComparer.OrdinalIgnoreCase).ToList();
            var perGame = new Section?[games.Count];

            Parallel.For(0, games.Count, i =>
            {
                var (appID, appName, steamPath, gamePath) = games[i];
                var files = new List<(string, string)>();

                var vpks = new FileSystemEnumerable<string>(gamePath, (ref entry) => entry.ToSpecifiedFullPath(), enumerationOptions)
                {
                    ShouldIncludePredicate = VpkPredicate,
                };

                foreach (var vpk in vpks)
                {
                    var vpkName = vpk[gamePath.Length..].Replace(Path.DirectorySeparatorChar, '/');

                    if (vpkName.EndsWith("_bakeresourcecache.vpk", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    files.Add((vpkName, vpk));
                }

                if (files.Count == 0)
                {
                    return;
                }

                files.Sort(static (a, b) => string.Compare(a.Item1, b.Item1, StringComparison.OrdinalIgnoreCase));

                AddWorkshopAddons(appID, steamPath, files);

                perGame[i] = new Section($"{appName} [{appID}]", files);
            });

            result.AddRange(perGame.OfType<Section>());
            return result;
        }

        private static void AddWorkshopAddons(int appID, string steamPath, List<(string, string)> files)
        {
            try
            {
                var workshopManifest = Path.Join(steamPath, "workshop", $"appworkshop_{appID}.acf");

                if (!File.Exists(workshopManifest))
                {
                    return;
                }

                var kvDeserializer = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);
                KVObject workshopInfo;

                using (var stream = File.OpenRead(workshopManifest))
                {
                    workshopInfo = kvDeserializer.Deserialize(stream);
                }

                foreach (var item in workshopInfo["WorkshopItemsInstalled"].Children)
                {
                    var addonPath = Path.Join(steamPath, "workshop", "content", appID.ToString(CultureInfo.InvariantCulture), item.Key);
                    var publishDataPath = Path.Join(addonPath, "publish_data.txt");
                    var vpk = Path.Join(addonPath, $"{item.Key}.vpk");

                    if (!File.Exists(vpk))
                    {
                        vpk = Path.Join(addonPath, $"{item.Key}_dir.vpk");

                        if (!File.Exists(vpk))
                        {
                            continue;
                        }
                    }

                    var title = item.Key;

                    if (File.Exists(publishDataPath))
                    {
                        using var stream = File.OpenRead(publishDataPath);
                        title = kvDeserializer.Deserialize(stream)["title"]?.ToString() ?? item.Key;
                    }

                    files.Add(($"[Workshop {item.Key}] {title}", vpk));
                }
            }
            catch (Exception e)
            {
                Dispatcher.UIThread.Post(() => Log.Warn(nameof(ExplorerControl), $"Failed to read workshop addons for {appID}: {e.Message}"));
            }
        }
    }
}
