using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GUI.Utils;
using ValveKeyValue;
using ValveResourceFormat.IO;

namespace GUI.Controls
{
    /// <summary>
    /// Lists bookmarks, recent files and the VPKs of installed Steam games, filtered by the box above.
    /// </summary>
    sealed class ExplorerControl : DockPanel
    {
        private sealed class FileItem(string text, string path, string? iconName, Bitmap? image)
        {
            public string Text { get; } = text;
            public string Path { get; } = path;
            public string? IconName { get; } = iconName;
            public Bitmap? Image { get; } = image;
        }

        private sealed class TreeData
        {
            public required int AppID { get; init; }
            public required string Text { get; init; }
            public required string? Path { get; init; }
            public required string? IconName { get; init; }
            public Bitmap? Image { get; init; }
            public required List<FileItem> Children { get; set; }
            public bool ExpandOnFirstSearch { get; set; } = true;
            public bool Expanded { get; set; }
        }

        private const int APPID_RECENT_FILES = -1000;
        private const int APPID_BOOKMARKS = -1001;
        private const int APPID_SCANNING = -1002;

        private readonly List<TreeData> treeData = [];
        private static readonly ConcurrentDictionary<string, string> WorkshopAddons = new();
        public static readonly List<GameFolderLocator.SteamLibraryGameInfo> SteamGames = [];

        private readonly TextBox filterTextBox;
        private readonly TreeView treeView;

        public ExplorerControl()
        {
            filterTextBox = new TextBox
            {
                PlaceholderText = "Filter\u2026",
                BorderThickness = new(0),
                CornerRadius = new(0),
            };
            filterTextBox.Classes.Add("pathBox");
            filterTextBox.TextChanged += (_, _) => Rebuild();

            treeView = new TreeView();
            treeView.Classes.Add("explorer");
            treeView.DoubleTapped += OnDoubleTapped;
            treeView.KeyDown += OnTreeKeyDown;
            treeView.ContextRequested += OnContextRequested;

            SetDock(filterTextBox, Dock.Top);
            Children.Add(filterTextBox);
            Children.Add(treeView);

            treeData.Add(new TreeData
            {
                AppID = APPID_BOOKMARKS,
                Text = "Bookmarks",
                Path = null,
                IconName = "Bookmarks",
                Children = GetFileItems(Settings.Config.BookmarkedFiles),
                Expanded = true,
            });

            treeData.Add(new TreeData
            {
                AppID = APPID_RECENT_FILES,
                Text = "Recent files",
                Path = null,
                IconName = "History",
                Children = GetFileItems(Settings.Config.RecentFiles),
                Expanded = true,
            });

            treeData.Add(new TreeData
            {
                AppID = APPID_SCANNING,
                Text = "Scanning game folders\u2026",
                Path = null,
                IconName = "History",
                Children = [],
            });

            Rebuild();

            _ = ScanAsync();
        }

        public void FocusFilter() => filterTextBox.Focus();

        protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            // Refresh recent files whenever the explorer is shown again
            SetChildren(APPID_RECENT_FILES, GetFileItems(Settings.Config.RecentFiles));
            Dispatcher.UIThread.Post(() => filterTextBox.Focus());
        }

        private async Task ScanAsync()
        {
            try
            {
                await Task.Run(ScanForSteamGames).ConfigureAwait(true);
                treeData.RemoveAll(static node => node.AppID == APPID_SCANNING);
            }
            catch (Exception e)
            {
                Log.Error(nameof(ExplorerControl), e.ToString());

                var index = treeData.FindIndex(static node => node.AppID == APPID_SCANNING);

                if (index >= 0)
                {
                    treeData[index] = new TreeData { AppID = APPID_SCANNING, Text = e.Message, Path = null, IconName = "History", Children = [] };
                }
            }

            // Workshop titles and game icons are known now
            SetChildren(APPID_BOOKMARKS, GetFileItems(Settings.Config.BookmarkedFiles), rebuild: false);
            SetChildren(APPID_RECENT_FILES, GetFileItems(Settings.Config.RecentFiles));
        }

        private void ScanForSteamGames()
        {
            if (GameFolderLocator.SteamPath == null)
            {
                return;
            }

            static int GetSortPriority(string? iconName) => iconName switch
            {
                "AssetTypes.vpk" => 10,
                "FolderShaders" => 9,
                "FolderMap" => 8,
                _ => 0,
            };

            var libraryCachePath = Path.Join(GameFolderLocator.SteamPath, "appcache", "librarycache");
            KVDocument? libraryAssetsKv = null;

            try
            {
                using var stream = File.OpenRead(Path.Join(libraryCachePath, "assetcache.vdf"));
                libraryAssetsKv = KVSerializer.Create(KVSerializationFormat.KeyValues1Binary).Deserialize(stream);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                //
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

            // Icons for recent and bookmarked files of these games are wanted as soon as possible
            foreach (var game in games)
            {
                if (Settings.Config.RecentFiles.Concat(Settings.Config.BookmarkedFiles).Any(path => path.StartsWith(game.GamePath, StringComparison.OrdinalIgnoreCase)))
                {
                    GetOrLoadAppImage(game.AppID, libraryAssetsKv, libraryCachePath);
                }
            }

            Parallel.ForEach(games, game =>
            {
                var (appID, appName, steamPath, gamePath) = game;
                var foundFiles = new List<FileItem>();

                var vpks = new FileSystemEnumerable<string>(gamePath, (ref entry) => entry.ToSpecifiedFullPath(), enumerationOptions)
                {
                    ShouldIncludePredicate = VpkPredicate,
                };

                foreach (var vpk in vpks)
                {
                    var vpkName = vpk[gamePath.Length..].Replace(Path.DirectorySeparatorChar, '/');
                    var fileName = Path.GetFileName(vpkName);

                    if (fileName.EndsWith("_bakeresourcecache.vpk", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var iconName = "AssetTypes.vpk";

                    if (fileName.StartsWith("shaders_", StringComparison.Ordinal))
                    {
                        iconName = "FolderShaders";
                    }
                    else if (vpkName.Contains("/maps/", StringComparison.Ordinal))
                    {
                        iconName = "FolderMap";
                    }

                    foundFiles.Add(new FileItem(vpkName, vpk, iconName, null));
                }

                if (foundFiles.Count == 0)
                {
                    return;
                }

                AddWorkshopAddons(appID, steamPath, foundFiles);

                foundFiles.Sort(static (a, b) =>
                {
                    var priority = GetSortPriority(b.IconName).CompareTo(GetSortPriority(a.IconName));
                    return priority != 0 ? priority : string.Compare(a.Text, b.Text, StringComparison.OrdinalIgnoreCase);
                });

                var image = GetOrLoadAppImage(appID, libraryAssetsKv, libraryCachePath);

                var node = new TreeData
                {
                    AppID = appID,
                    Text = $"[{appID}] {appName} - {gamePath.Replace(Path.DirectorySeparatorChar, '/')}",
                    Path = gamePath,
                    IconName = image == null ? "Folder" : null,
                    Image = image,
                    Children = foundFiles,
                };

                Dispatcher.UIThread.Post(() => InsertGame(node));
            });
        }

        private void InsertGame(TreeData node)
        {
            // Games are sorted by app id after bookmarks and recent files, which have negative ids
            var index = treeData.Count;

            for (var i = treeData.Count - 1; i >= 0; i--)
            {
                if (treeData[i].AppID > node.AppID)
                {
                    index = i;
                }
                else
                {
                    break;
                }
            }

            treeData.Insert(index, node);
            Rebuild();
        }

        private static void AddWorkshopAddons(int appID, string steamPath, List<FileItem> files)
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

                    using var stream = File.OpenRead(publishDataPath);
                    var addonTitle = kvDeserializer.Deserialize(stream)["title"];
                    var displayTitle = $"[Workshop {item.Key}] {addonTitle}";

                    files.Add(new FileItem(displayTitle, vpk, "FolderPlugin", null));
                    WorkshopAddons[vpk] = displayTitle;
                }
            }
            catch (Exception e)
            {
                Dispatcher.UIThread.Post(() => Log.Warn(nameof(ExplorerControl), $"Failed to read workshop addons for {appID}: {e.Message}"));
            }
        }

        private static Bitmap? GetOrLoadAppImage(int appID, KVDocument? libraryAssetsKv, string libraryCachePath)
        {
            if (AppIcons.GameIcons.TryGetValue(appID, out var image))
            {
                return image;
            }

            if (libraryAssetsKv == null)
            {
                return null;
            }

            try
            {
                // The icon file name is in the assets cache, keyed like "0f" "1f" which appears to be an enum
                var appIDStr = appID.ToString(CultureInfo.InvariantCulture);
                var filename = libraryAssetsKv["0"]?[appIDStr]?["4f"];

                if (filename == null)
                {
                    return null;
                }

                var appIconPath = Path.Join(libraryCachePath, appIDStr, (string)filename);

                if (!File.Exists(appIconPath))
                {
                    return null;
                }

                using var stream = File.OpenRead(appIconPath);
                image = Bitmap.DecodeToWidth(stream, 32);

                return AppIcons.GameIcons.GetOrAdd(appID, image);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static List<FileItem> GetFileItems(List<string> paths)
        {
            var items = new List<FileItem>(paths.Count);

            for (var i = paths.Count - 1; i >= 0; i--)
            {
                var path = paths[i];
                var pathDisplay = path.Replace(Path.DirectorySeparatorChar, '/');
                var isVpk = false;
                string iconName;

                if (WorkshopAddons.TryGetValue(path, out var displayTitle))
                {
                    iconName = "FolderPlugin";
                    pathDisplay = $"{pathDisplay} {displayTitle}";
                }
                else
                {
                    isVpk = path.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase);

                    if (isVpk && Path.GetFileName(path).StartsWith("shaders_", StringComparison.Ordinal))
                    {
                        iconName = "FolderShaders";
                    }
                    else if (isVpk && pathDisplay.Contains("/maps/", StringComparison.Ordinal))
                    {
                        iconName = "FolderMap";
                    }
                    else
                    {
                        iconName = AppIcons.GetFileIconName(path);
                    }
                }

                Bitmap? gameImage = null;

                foreach (var game in SteamGames)
                {
                    if (path.StartsWith(game.GamePath, StringComparison.OrdinalIgnoreCase))
                    {
                        pathDisplay = $"[{game.AppName}] {pathDisplay.AsSpan(game.GamePath.Length)}";

                        if (isVpk)
                        {
                            AppIcons.GameIcons.TryGetValue(game.AppID, out gameImage);
                        }

                        break;
                    }
                }

                items.Add(new FileItem(pathDisplay, path, iconName, gameImage));
            }

            return items;
        }

        private void SetChildren(int appID, List<FileItem> children, bool rebuild = true)
        {
            var node = treeData.Find(node => node.AppID == appID);
            Debug.Assert(node != null);
            node.Children = children;
            node.Expanded = true;

            if (rebuild)
            {
                Rebuild();
            }
        }

        private void Rebuild()
        {
            var filter = (filterTextBox.Text ?? string.Empty).Replace(Path.DirectorySeparatorChar, '/');
            var showAll = filter.Length == 0;

            // Remember what the user expanded or collapsed since the last build
            foreach (var item in treeView.Items.OfType<TreeViewItem>())
            {
                if (item.Tag is TreeData data)
                {
                    data.Expanded = item.IsExpanded;
                }
            }

            treeView.Items.Clear();

            foreach (var node in treeData)
            {
                var children = showAll
                    ? node.Children
                    : node.Children.Where(child => child.Text.Contains(filter, StringComparison.OrdinalIgnoreCase) || child.Path.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

                if (!showAll && children.Count == 0)
                {
                    continue;
                }

                if (!showAll && node.ExpandOnFirstSearch)
                {
                    node.ExpandOnFirstSearch = false;
                    node.Expanded = true;
                }

                var parent = new TreeViewItem
                {
                    Header = CreateHeader(node.IconName, node.Image, node.Text),
                    Tag = node,
                };

                foreach (var child in children)
                {
                    var item = new TreeViewItem
                    {
                        Header = CreateHeader(child.IconName, child.Image, child.Text),
                        Tag = child,
                    };
                    ToolTip.SetTip(item, child.Path);
                    parent.Items.Add(item);
                }

                parent.IsExpanded = node.Expanded;
                treeView.Items.Add(parent);
            }
        }

        private static StackPanel CreateHeader(string? iconName, Bitmap? image, string text)
        {
            var icon = image != null ? AppIcons.Create(image) : AppIcons.Create(iconName ?? "File");
            icon.VerticalAlignment = VerticalAlignment.Center;
            return AppIcons.CreateHeader(icon, text);
        }

        private static string? GetPath(object? item) => item switch
        {
            TreeViewItem { Tag: FileItem file } => file.Path,
            TreeViewItem { Tag: TreeData data } => data.Path,
            _ => null,
        };

        private void OpenSelected()
        {
            if (treeView.SelectedItem is not TreeViewItem { Tag: FileItem file })
            {
                return;
            }

            if (File.Exists(file.Path))
            {
                Program.MainForm.OpenFile(file.Path);
            }
            else
            {
                Log.Error(nameof(ExplorerControl), $"File '{file.Path}' does not exist.");
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

        private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
        {
            var item = (e.Source as Avalonia.Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true);

            if (item == null)
            {
                return;
            }

            treeView.SelectedItem = item;
            e.Handled = true;

            var menu = new ContextMenu();

            if (item.Tag is TreeData { AppID: APPID_RECENT_FILES })
            {
                var clear = new MenuItem { Header = "_Clear recent files", Icon = AppIcons.Create("HistoryClear") };
                clear.Click += (_, _) =>
                {
                    Settings.ClearRecentFiles();
                    SetChildren(APPID_RECENT_FILES, []);
                };
                menu.Items.Add(clear);
            }
            else if (GetPath(item) is { } path)
            {
                var reveal = new MenuItem { Header = "R_eveal in file manager", Icon = AppIcons.Create("OpenInExplorer") };
                reveal.Click += (_, _) => RevealInFileManager(path);
                menu.Items.Add(reveal);

                if (item.Tag is FileItem)
                {
                    var isBookmarked = Settings.Config.BookmarkedFiles.Contains(path);

                    if (!isBookmarked)
                    {
                        var add = new MenuItem { Header = "Add to _Bookmarks", Icon = AppIcons.Create("BookmarksAdd") };
                        add.Click += (_, _) =>
                        {
                            if (!Settings.Config.BookmarkedFiles.Contains(path))
                            {
                                Settings.Config.BookmarkedFiles.Add(path);
                                SetChildren(APPID_BOOKMARKS, GetFileItems(Settings.Config.BookmarkedFiles));
                            }
                        };
                        menu.Items.Add(add);
                    }
                    else
                    {
                        var remove = new MenuItem { Header = "Remove from _Bookmarks", Icon = AppIcons.Create("BookmarksRemove") };
                        remove.Click += (_, _) =>
                        {
                            Settings.Config.BookmarkedFiles.Remove(path);
                            SetChildren(APPID_BOOKMARKS, GetFileItems(Settings.Config.BookmarkedFiles));
                        };
                        menu.Items.Add(remove);
                    }

                    if (Settings.Config.RecentFiles.Contains(path))
                    {
                        var removeRecent = new MenuItem { Header = "_Remove from Recent", Icon = AppIcons.Create("HistoryRemove") };
                        removeRecent.Click += (_, _) =>
                        {
                            Settings.Config.RecentFiles.Remove(path);
                            SetChildren(APPID_RECENT_FILES, GetFileItems(Settings.Config.RecentFiles));
                        };
                        menu.Items.Add(removeRecent);
                    }
                }
            }

            if (menu.Items.Count > 0)
            {
                menu.Open(item);
            }
        }

        internal static void RevealInFileManager(string path)
        {
            try
            {
                if (OperatingSystem.IsWindows() && File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select, \"{path}\""));
                    return;
                }

                // Other platforms have no portable way to select a file, open its folder instead
                var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);

                if (folder != null)
                {
                    Process.Start(new ProcessStartInfo(folder + Path.DirectorySeparatorChar) { UseShellExecute = true });
                }
            }
            catch (Exception e)
            {
                Log.Error(nameof(ExplorerControl), $"Failed to reveal '{path}': {e.Message}");
            }
        }
    }
}
