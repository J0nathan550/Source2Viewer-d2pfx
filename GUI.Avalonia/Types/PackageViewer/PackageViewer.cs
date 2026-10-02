using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using GUI.Controls;
using GUI.Forms;
using GUI.Types.Exporter;
using GUI.Types.Viewers;
using GUI.Utils;
using ValvePak;
using ValveResourceFormat.IO;

namespace GUI.Types.PackageViewer
{
    /// <summary>
    /// Browses the files of a VPK: a folder tree, the files of the selected folder or a search, and a preview.
    /// </summary>
    sealed class PackageViewer(VrfGuiContext vrfGuiContext) : IViewer, IDisposable, IPreviewHost
    {
        public sealed record FileRow(string Name, string Size, string Type, string Folder)
        {
            internal required PackageEntry Entry { get; init; }
        }

        private const int MaxListedFiles = 50_000;

        private VirtualPackageNode? VirtualRoot;
        private TreeView? treeView;
        private DataGrid? fileList;
        private TextBox? searchBox;
        private ComboBox? searchType;
        private TextBlock? listStatus;
        private TabControl? previewTabs;
        private DocumentTab? previewTab;

        public static bool IsAccepted(uint magic)
        {
            return magic == Package.MAGIC;
        }

        public async Task LoadAsync(Stream? stream)
        {
            var package = new Package();
            package.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);

            if (stream != null)
            {
                package.SetFileName(vrfGuiContext.FileName);
                package.Read(stream);
            }
            else
            {
                package.Read(vrfGuiContext.FileName);
            }

            vrfGuiContext.CurrentPackage = package;

            VirtualRoot = new VirtualPackageNode("root", 0, null);

            if (package.Entries != null)
            {
                foreach (var fileType in package.Entries)
                {
                    foreach (var file in fileType.Value)
                    {
                        AddFileNode(VirtualRoot, file);
                    }
                }
            }

            foreach (var node in VirtualRoot.Folders)
            {
                VirtualRoot.TotalSize += node.Value.TotalSize;
            }

            foreach (var node in VirtualRoot.Files)
            {
                VirtualRoot.TotalSize += node.TotalLength;
            }
        }

        public static VirtualPackageNode AddFolderNode(VirtualPackageNode currentNode, string directory, uint size)
        {
            var directorySpan = directory.AsSpan();

            foreach (var subPathRange in directorySpan.Split([Package.DirectorySeparatorChar]))
            {
                var subPath = directorySpan[subPathRange];

                if (!currentNode.Folders.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(subPath, out var subNode))
                {
                    var subPathString = subPath.ToString();
                    var toAdd = new VirtualPackageNode(subPathString, size, currentNode);
                    currentNode.Folders.Add(subPathString, toAdd);
                    currentNode = toAdd;
                }
                else
                {
                    currentNode = subNode;
                    currentNode.TotalSize += size;
                }
            }

            return currentNode;
        }

        public static VirtualPackageNode AddFileNode(VirtualPackageNode currentNode, PackageEntry file)
        {
            if (!string.IsNullOrWhiteSpace(file.DirectoryName))
            {
                currentNode = AddFolderNode(currentNode, file.DirectoryName, file.TotalLength);
            }

            currentNode.Files.Add(file);

            return currentNode;
        }

        public Control Create()
        {
            if (VirtualRoot == null)
            {
                throw new InvalidOperationException("Package was not loaded");
            }

            var rootNode = new BetterTreeNode(VirtualRoot) { IsExpanded = true };
            rootNode.EnsureChildren();

            treeView = new TreeView { ItemsSource = new[] { rootNode } };
            treeView.SelectionChanged += OnFolderSelected;
            treeView.ContextMenu = CreateFolderContextMenu();

            fileList = new DataGrid
            {
                IsReadOnly = true,
                CanUserSortColumns = true,
                CanUserResizeColumns = true,
                SelectionMode = DataGridSelectionMode.Extended,
                GridLinesVisibility = DataGridGridLinesVisibility.None,
                Columns =
                {
                    TextColumn("Name", nameof(FileRow.Name), 2),
                    TextColumn("Size", nameof(FileRow.Size), 0.6),
                    TextColumn("Type", nameof(FileRow.Type), 0.6),
                    TextColumn("Folder", nameof(FileRow.Folder), 2),
                },
            };
            fileList.SelectionChanged += OnFileSelected;
            fileList.DoubleTapped += (_, _) => OpenSelectedFiles(withoutViewer: false);
            fileList.KeyDown += OnFileListKeyDown;
            fileList.ContextMenu = CreateFileContextMenu();

            searchBox = new TextBox { PlaceholderText = "Search files (press Enter)", MinWidth = 200 };
            searchBox.KeyDown += OnSearchKeyDown;

            searchType = new ComboBox
            {
                ItemsSource = new[] { "File name", "Exact file name", "Full path", "Regex" },
                SelectedIndex = 0,
                Margin = new(4, 0),
            };

            var searchButton = new Button { Content = "Search" };
            searchButton.Click += (_, _) => RunSearch();

            var clearButton = new Button { Content = "Clear", Margin = new(4, 0, 0, 0) };
            clearButton.Click += (_, _) =>
            {
                searchBox.Text = string.Empty;
                ShowFolder(SelectedFolder ?? VirtualRoot);
            };

            listStatus = new TextBlock { Margin = new(8, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75 };

            var toolbar = new DockPanel { Margin = new(4) };
            DockPanel.SetDock(searchType, Dock.Right);
            DockPanel.SetDock(clearButton, Dock.Right);
            DockPanel.SetDock(searchButton, Dock.Right);
            toolbar.Children.Add(clearButton);
            toolbar.Children.Add(searchButton);
            toolbar.Children.Add(searchType);
            toolbar.Children.Add(searchBox);

            previewTabs = ViewerContentPresenter.CreateTabControl();

            var listPanel = new DockPanel();
            DockPanel.SetDock(listStatus, Dock.Bottom);
            listPanel.Children.Add(listStatus);
            listPanel.Children.Add(fileList);

            // Folder tree | file list over preview
            var right = new Grid { RowDefinitions = new RowDefinitions("*,4,*") };
            var rowSplitter = new GridSplitter { ResizeDirection = GridResizeDirection.Rows };
            Grid.SetRow(rowSplitter, 1);
            Grid.SetRow(previewTabs, 2);
            right.Children.Add(listPanel);
            right.Children.Add(rowSplitter);
            right.Children.Add(previewTabs);

            var body = new Grid { ColumnDefinitions = new ColumnDefinitions("280,4,*") };
            var columnSplitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns };
            Grid.SetColumn(columnSplitter, 1);
            Grid.SetColumn(right, 2);
            body.Children.Add(treeView);
            body.Children.Add(columnSplitter);
            body.Children.Add(right);

            var root = new DockPanel();
            DockPanel.SetDock(toolbar, Dock.Top);
            root.Children.Add(toolbar);
            root.Children.Add(body);

            ShowFolder(VirtualRoot);

            return root;
        }

        private static DataGridTextColumn TextColumn(string header, string property, double weight) => new()
        {
            Header = header,
#pragma warning disable IL2026 // Rows are a simple record, reflection binding is fine
            Binding = new Avalonia.Data.Binding(property),
#pragma warning restore IL2026
            Width = new DataGridLength(weight, DataGridLengthUnitType.Star),
        };

        private VirtualPackageNode? SelectedFolder => (treeView?.SelectedItem as BetterTreeNode)?.PkgNode;

        private void OnFolderSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (SelectedFolder is { } folder)
            {
                ShowFolder(folder);
            }
        }

        private void ShowFolder(VirtualPackageNode folder)
        {
            var files = new List<PackageEntry>(folder.Files);
            files.Sort(static (a, b) => string.Compare(a.GetFileName(), b.GetFileName(), StringComparison.OrdinalIgnoreCase));

            var path = folder.GetFullPath();
            SetFiles(files, $"{(path.Length == 0 ? "(root)" : path)}: {folder.Files.Count} files, {folder.Folders.Count} folders, {HumanReadableByteSizeFormatter.Format(folder.TotalSize)}");
        }

        private void SetFiles(List<PackageEntry> files, string status)
        {
            if (fileList == null || listStatus == null)
            {
                return;
            }

            var truncated = files.Count > MaxListedFiles;

            fileList.ItemsSource = files
                .Take(MaxListedFiles)
                .Select(static entry => new FileRow(
                    entry.GetFileName(),
                    HumanReadableByteSizeFormatter.Format(entry.TotalLength),
                    entry.TypeName,
                    entry.DirectoryName)
                {
                    Entry = entry,
                })
                .ToList();

            listStatus.Text = truncated ? $"{status} (showing first {MaxListedFiles})" : status;
        }

        private void OnSearchKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                RunSearch();
                e.Handled = true;
            }
        }

        private void RunSearch()
        {
            if (VirtualRoot == null || searchBox == null || searchType == null)
            {
                return;
            }

            var value = searchBox.Text?.Trim() ?? string.Empty;

            if (value.Length == 0)
            {
                ShowFolder(SelectedFolder ?? VirtualRoot);
                return;
            }

            var type = searchType.SelectedIndex switch
            {
                1 => SearchType.FileNameExactMatch,
                2 => SearchType.FullPath,
                3 => SearchType.Regex,
                _ => SearchType.FileNamePartialMatch,
            };

            Func<PackageEntry, bool> match;

            try
            {
                match = CreateMatcher(value, ref type);
            }
            catch (ArgumentException ex)
            {
                listStatus!.Text = $"Invalid search: {ex.Message}";
                return;
            }

            var results = new List<PackageEntry>();
            Search(SelectedFolder ?? VirtualRoot, results, match);
            results.Sort(static (a, b) => string.Compare(a.GetFullPath(), b.GetFullPath(), StringComparison.OrdinalIgnoreCase));

            SetFiles(results, $"Found {results.Count} files matching \"{value}\"");
        }

        private static Func<PackageEntry, bool> CreateMatcher(string value, ref SearchType searchType)
        {
            if (searchType is SearchType.FileNamePartialMatch or SearchType.FullPath)
            {
                value = value.Replace('\\', Package.DirectorySeparatorChar);
            }

            // If only file name search is selected, but entered text contains a slash, search full path
            if (searchType == SearchType.FileNamePartialMatch && value.Contains(Package.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                searchType = SearchType.FullPath;
            }

            switch (searchType)
            {
                case SearchType.FileNameExactMatch:
                    return entry => entry.GetFileName().Equals(value, StringComparison.OrdinalIgnoreCase);
                case SearchType.FullPath:
                    return entry => entry.GetFullPath().Contains(value, StringComparison.OrdinalIgnoreCase);
                case SearchType.Regex:
                {
                    var regex = new Regex(value, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                    return entry => regex.IsMatch(entry.GetFileName());
                }
                default:
                    return entry => entry.GetFileName().Contains(value, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static void Search(VirtualPackageNode node, List<PackageEntry> results, Func<PackageEntry, bool> matchFunction)
        {
            foreach (var entry in node.Files)
            {
                if (matchFunction(entry))
                {
                    results.Add(entry);
                }
            }

            foreach (var folder in node.Folders.Values)
            {
                Search(folder, results, matchFunction);
            }
        }

        private List<PackageEntry> SelectedEntries()
            => fileList?.SelectedItems.OfType<FileRow>().Select(static r => r.Entry).ToList() ?? [];

        private void OnFileSelected(object? sender, SelectionChangedEventArgs e)
        {
            var selected = SelectedEntries();

            if (selected.Count != 1)
            {
                return;
            }

            // Only preview when the selection settles, so arrowing through a list does not load every file
            var entry = selected[0];
            DispatcherTimer.RunOnce(() =>
            {
                if (SelectedEntries() is [var current] && current == entry)
                {
                    Preview(entry);
                }
            }, TimeSpan.FromMilliseconds(250));
        }

        private void Preview(PackageEntry entry)
        {
#pragma warning disable CA2000 // The opened tab owns the context and disposes it
            var context = new VrfGuiContext(entry.GetFullPath(), vrfGuiContext);
#pragma warning restore CA2000
            Program.MainForm.OpenFile(context, entry, this);
        }

        public void ShowPreview(DocumentTab tab)
        {
            if (previewTabs == null)
            {
                tab.Dispose();
                return;
            }

            var previous = previewTab;
            previewTab = tab;

            previewTabs.Items.Clear();
            previewTabs.Items.Add(tab);
            previewTabs.SelectedItem = tab;

            previous?.Dispose();
        }

        private void OnFileListKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OpenSelectedFiles(withoutViewer: false);
                e.Handled = true;
            }
        }

        private void OpenSelectedFiles(bool withoutViewer)
        {
            foreach (var entry in SelectedEntries())
            {
#pragma warning disable CA2000 // The opened tab owns the context and disposes it
                var context = new VrfGuiContext(entry.GetFullPath(), vrfGuiContext);
#pragma warning restore CA2000
                Program.MainForm.OpenFile(context, entry, withoutViewer: withoutViewer);
            }
        }

        private ContextMenu CreateFileContextMenu()
        {
            var open = new MenuItem { Header = "Open" };
            open.Click += (_, _) => OpenSelectedFiles(withoutViewer: false);

            var openBlocks = new MenuItem { Header = "Open resource blocks only" };
            openBlocks.Click += (_, _) => OpenSelectedFiles(withoutViewer: true);

            var copyPath = new MenuItem { Header = "Copy file path" };
            copyPath.Click += (_, _) => AppClipboard.SetText(string.Join(Environment.NewLine, SelectedEntries().Select(static e => e.GetFullPath())));

            var exportRaw = new MenuItem { Header = "Export as is..." };
            exportRaw.Click += OnExportRawClick;

            var decompile = new MenuItem { Header = "Decompile and export..." };
            decompile.Click += OnDecompileClick;

            return new ContextMenu { Items = { open, openBlocks, new Separator(), copyPath, new Separator(), exportRaw, decompile } };
        }

        private sealed record PackageFileItem(PackageEntry PackageEntry) : IBetterBaseItem
        {
            public VirtualPackageNode? PkgNode => null;
        }

        private void OnExportRawClick(object? sender, RoutedEventArgs e) => ExportSelection(SelectedEntries().Select(static e => new PackageFileItem(e)), decompile: false, keepPackageStructure: false);

        private void OnDecompileClick(object? sender, RoutedEventArgs e) => ExportSelection(SelectedEntries().Select(static e => new PackageFileItem(e)), decompile: true, keepPackageStructure: false);

        private void ExportSelection(IEnumerable<IBetterBaseItem> items, bool decompile, bool keepPackageStructure)
        {
            var exporter = new PackageExporter(new ExportData { VrfGuiContext = vrfGuiContext }, null, decompile);

            foreach (var item in items)
            {
                exporter.QueueSelection(item, includeSubfolders: true, keepPackageStructure);
            }

            exporter.ExecuteMultipleFileExtract();
        }

        private ContextMenu CreateFolderContextMenu()
        {
            IEnumerable<IBetterBaseItem> SelectedFolder() => treeView?.SelectedItem is BetterTreeNode node ? [node] : [];

            var exportRaw = new MenuItem { Header = "Export folder as is..." };
            exportRaw.Click += (_, _) => ExportSelection(SelectedFolder(), decompile: false, keepPackageStructure: false);

            var decompile = new MenuItem { Header = "Decompile and export folder..." };
            decompile.Click += (_, _) => ExportSelection(SelectedFolder(), decompile: true, keepPackageStructure: false);

            var copyPath = new MenuItem { Header = "Copy folder path" };
            copyPath.Click += (_, _) =>
            {
                if (treeView?.SelectedItem is BetterTreeNode node)
                {
                    AppClipboard.SetText(node.PkgNode.GetFullPath());
                }
            };

            return new ContextMenu { Items = { copyPath, new Separator(), exportRaw, decompile } };
        }

        public void Dispose()
        {
            previewTab?.Dispose();
            previewTab = null;
        }
    }
}
