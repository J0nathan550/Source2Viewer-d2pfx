using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GUI.Controls;
using GUI.Forms;
using GUI.Types.Exporter;
using GUI.Types.Exporter.CharacterAssets;
using GUI.Types.PackageViewer.ThumbnailRenderers;
using GUI.Types.Viewers;
using GUI.Utils;
using ValvePak;
using ValveResourceFormat.IO;

namespace GUI.Types.PackageViewer
{
    /// <summary>
    /// Browses the files of a VPK like the WinForms TreeViewWithSearchResults: a path box on top, the folder and
    /// file tree on the left, and on the right either the selected folder as a list or grid, or a file preview.
    /// </summary>
    sealed class PackageViewer(VrfGuiContext vrfGuiContext) : IViewer, IDisposable, IPreviewHost
    {
        /// <summary>A row of the file list or grid: the parent folder link, a folder, or a file.</summary>
        public sealed class ListRow : IBetterBaseItem
        {
            public required string Name { get; init; }
            public required long Size { get; init; }
            public required string Type { get; init; }
            public required string IconName { get; init; }
            public string SizeText => HumanReadableByteSizeFormatter.Format(Size);
            public bool IsParent { get; init; }
            public PackageEntry? PackageEntry { get; init; }
            public VirtualPackageNode? PkgNode { get; init; }
        }

        // Grid mode lays out every item, so very large folders are cut off there (the list virtualizes)
        private const int MaxGridItems = 5000;

        private static double SplitterWidth = 400;

        private VirtualPackageNode? VirtualRoot;
        private TreeView? treeView;
        private BetterTreeNode? rootNode;
        private TextBox? pathBox;
        private Button? backButton;
        private Button? forwardButton;
        private RadioButton? listRadioButton;
        private RadioButton? gridRadioButton;
        private bool isGridMode;
        private Avalonia.Controls.Slider? gridSizeSlider;
        private Border? toolbar;
        private Panel? contentHost;
        private PackageListView? fileList;
        private ListBox? gridList;
        private TextBlock? gridOverflowText;
        private TabControl? previewTabs;
        private DocumentTab? previewTab;

        private List<ListRow> currentRows = [];
        private readonly NavigationHistory navigationHistory = new();
        private bool suppressHistoryRecording;
        private bool suppressTreeSelection;
        private CancellationTokenSource? previewTokenSource;
        private SearchWindow? searchWindow;

        private Dictionary<string, Dictionary<string, string>>? assetSearchData;
        private Dictionary<string, SortedSet<string>>? searchDataKeys;
        private bool searchDataBuilt;

        // A new package being put together with Create VPK from folder
        private bool isEditingPackage;

        public bool DeletedFilesRecovered { get; private set; }

        public VrfGuiContext VrfGuiContext => vrfGuiContext;

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

            // Recovering deleted files only works on split packages
            DeletedFilesRecovered = !package.IsDirVPK;
        }

        /// <summary>
        /// Starts an empty package that folders and files can be added to and then saved, for Create VPK from folder.
        /// </summary>
        public Control CreateEmpty()
        {
            isEditingPackage = true;

            var package = new Package();
            package.AddFile("README.txt", []); // TODO: Otherwise package.Entries is null

            vrfGuiContext.CurrentPackage = package;

            VirtualRoot = new VirtualPackageNode("root", 0, null);
            DeletedFilesRecovered = true;

            return Create();
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

            // Path box
            pathBox = new TextBox
            {
                PlaceholderText = "Search\u2026",
                BorderThickness = new(0),
                CornerRadius = new(0),
            };
            pathBox.Classes.Add("pathBox");
            pathBox.KeyDown += OnPathBoxKeyDown;

            // Tree
            var fullFilePath = vrfGuiContext.FileName;
            var rootName = fullFilePath.Length > 0
                ? $"{Path.GetFileName(Path.GetDirectoryName(fullFilePath))}/{Path.GetFileName(fullFilePath)}"
                : Path.GetFileName(fullFilePath);

            rootNode = new BetterTreeNode(rootName, VirtualRoot, "AssetTypes.vpk");
            rootNode.EnsureChildren();
            rootNode.IsExpanded = true;

            var tree = new TreeView { SelectionMode = SelectionMode.Multiple };
            tree.Classes.Add("explorer");
            tree.Classes.Add("packageTree");
            tree.Items.Add(rootNode);
            tree.SelectionChanged += OnTreeSelectionChanged;
            tree.DoubleTapped += OnTreeDoubleTapped;
            tree.ContextRequested += (_, e) => OnContextRequested(tree, e);
            treeView = tree;

            // Toolbar, laid out like the WinForms one: two navigation buttons, the view mode and the grid size
            backButton = new Button { Content = AppIcons.Create("NavigateBack", 24), IsEnabled = false };
            backButton.Classes.Add("navigation");
            ToolTip.SetTip(backButton, "Back");
            backButton.Click += (_, _) => NavigateBack();

            forwardButton = new Button { Content = AppIcons.Create("NavigateForward", 24), IsEnabled = false };
            forwardButton.Classes.Add("navigation");
            ToolTip.SetTip(forwardButton, "Forward");
            forwardButton.Click += (_, _) => NavigateForward();

            isGridMode = Settings.Config.PackageGridView != 0;

            // No group name: a named group spans the whole window, so every open package would share one view mode.
            // Unnamed radio buttons are grouped by their parent panel instead.
            listRadioButton = new RadioButton { Content = "List", IsChecked = !isGridMode, Margin = new(12, 0, 0, 0) };
            gridRadioButton = new RadioButton { Content = "Grid", IsChecked = isGridMode };
            listRadioButton.Classes.Add("small");
            gridRadioButton.Classes.Add("small");

            // The newly checked button raises its event before the group unchecks the other one,
            // so the mode comes from whichever button just became checked rather than from reading both
            listRadioButton.IsCheckedChanged += (_, _) =>
            {
                if (listRadioButton.IsChecked == true)
                {
                    SetViewMode(grid: false);
                }
            };
            gridRadioButton.IsCheckedChanged += (_, _) =>
            {
                if (gridRadioButton.IsChecked == true)
                {
                    SetViewMode(grid: true);
                }
            };

            gridSizeSlider = new Avalonia.Controls.Slider
            {
                Minimum = 0,
                Maximum = Enum.GetValues<ThumbnailSizes>().Length - 1,
                Value = Settings.Config.PackageGridSize,
                IsSnapToTickEnabled = true,
                TickFrequency = 1,
                TickPlacement = TickPlacement.BottomRight,
                Width = 107,
                IsEnabled = isGridMode,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            gridSizeSlider.ValueChanged += (_, _) =>
            {
                Settings.Config.PackageGridSize = (int)gridSizeSlider.Value;
                RefreshItems();
            };

            var toolbarGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("36,36,60,60,170") };
            Grid.SetColumn(forwardButton, 1);
            Grid.SetColumn(listRadioButton, 2);
            Grid.SetColumn(gridRadioButton, 3);
            Grid.SetColumn(gridSizeSlider, 4);
            toolbarGrid.Children.Add(backButton);
            toolbarGrid.Children.Add(forwardButton);
            toolbarGrid.Children.Add(listRadioButton);
            toolbarGrid.Children.Add(gridRadioButton);
            toolbarGrid.Children.Add(gridSizeSlider);

            toolbar = new Border
            {
                Height = 40,
                Padding = new(8, 0, 0, 0),
                Child = toolbarGrid,
            };
            toolbar.Classes.Add("packageToolbar");

            // List and grid
            var list = new PackageListView();
            list.ItemList.DoubleTapped += (_, e) => OnItemsDoubleTapped(e);
            list.ItemList.KeyDown += OnItemsKeyDown;
            list.ItemList.ContextRequested += (_, e) => OnContextRequested(list.ItemList, e);
            fileList = list;

            var grid = new ListBox
            {
                SelectionMode = SelectionMode.Multiple,
                ItemsPanel = new FuncTemplate<Panel?>(static () => new WrapPanel()),
                ItemTemplate = new FuncDataTemplate<ListRow>(CreateGridItem, supportsRecycling: false),
            };
            grid.Classes.Add("packageGrid");
            ScrollViewer.SetHorizontalScrollBarVisibility(grid, ScrollBarVisibility.Disabled);
            grid.DoubleTapped += (_, e) => OnItemsDoubleTapped(e);
            grid.KeyDown += OnItemsKeyDown;
            grid.ContextRequested += (_, e) => OnContextRequested(grid, e);
            gridList = grid;

            gridOverflowText = new TextBlock { Margin = new(8, 4), Opacity = 0.7, IsVisible = false };

            var gridPanel = new DockPanel();
            DockPanel.SetDock(gridOverflowText, Dock.Bottom);
            gridPanel.Children.Add(gridOverflowText);
            gridPanel.Children.Add(grid);

            previewTabs = ViewerContentPresenter.CreateTabControl();
            previewTabs.Classes.Add("preview");
            previewTabs.IsVisible = false;

            contentHost = new Panel { Children = { list, gridPanel, previewTabs } };

            var right = new DockPanel();
            DockPanel.SetDock(toolbar, Dock.Top);
            right.Children.Add(toolbar);
            right.Children.Add(contentHost);

            var body = new Grid { ColumnDefinitions = new ColumnDefinitions($"{SplitterWidth.ToString(CultureInfo.InvariantCulture)},4,*") };
            var columnSplitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns };
            columnSplitter.Classes.Add("packageSplitter");
            columnSplitter.DragCompleted += (_, _) => SplitterWidth = body.ColumnDefinitions[0].ActualWidth;
            Grid.SetColumn(columnSplitter, 1);
            Grid.SetColumn(right, 2);
            body.Children.Add(tree);
            body.Children.Add(columnSplitter);
            body.Children.Add(right);

            var root = new DockPanel();
            DockPanel.SetDock(pathBox, Dock.Top);
            root.Children.Add(pathBox);
            root.Children.Add(body);

            // Mouse back and forward buttons navigate the history anywhere over the package
            root.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
            {
                var kind = e.GetCurrentPoint(root).Properties.PointerUpdateKind;

                if (kind == PointerUpdateKind.XButton1Pressed)
                {
                    NavigateBack();
                    e.Handled = true;
                }
                else if (kind == PointerUpdateKind.XButton2Pressed)
                {
                    NavigateForward();
                    e.Handled = true;
                }
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

            suppressTreeSelection = true;
            tree.SelectedItem = rootNode;
            suppressTreeSelection = false;

            DisplayNodes(VirtualRoot);

            return root;
        }

        private StackPanel CreateGridItem(ListRow? row, INameScope scope)
        {
            var size = (double)Enum.GetValues<ThumbnailSizes>()[Math.Clamp(Settings.Config.PackageGridSize, 0, 4)];
            var width = Math.Max(size, 64) + 16;

            var icon = AppIcons.Create(row?.IconName ?? "File", size);
            icon.HorizontalAlignment = HorizontalAlignment.Center;

            var text = new TextBlock
            {
                Text = row?.Name,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            var item = new StackPanel { Width = width, Spacing = 4, Margin = new(4), Children = { icon, text } };

            if (row?.PackageEntry != null)
            {
                ToolTip.SetTip(item, $"{row.Name}\n{row.SizeText}");
            }

            return item;
        }

        private bool IsGridMode => isGridMode;

        private void SetViewMode(bool grid)
        {
            if (isGridMode == grid)
            {
                return;
            }

            isGridMode = grid;
            Settings.Config.PackageGridView = grid ? 1 : 0;
            Settings.Save();

            if (gridSizeSlider != null)
            {
                gridSizeSlider.IsEnabled = grid;
            }

            RefreshItems();
        }

        /// <summary>Shows <see cref="currentRows"/> in the current view mode.</summary>
        private void RefreshItems()
        {
            if (fileList == null || gridList == null || gridOverflowText == null)
            {
                return;
            }

            var showList = !IsShowingPreview;
            var grid = IsGridMode;

            fileList.IsVisible = showList && !grid;
            ((Control)gridList.Parent!).IsVisible = showList && grid;

            if (grid)
            {
                fileList.SetRows([]);
                gridList.ItemsSource = null;
                gridList.ItemsSource = currentRows.Count > MaxGridItems ? currentRows.Take(MaxGridItems).ToList() : currentRows;
                gridOverflowText.IsVisible = currentRows.Count > MaxGridItems;
                gridOverflowText.Text = $"Showing the first {MaxGridItems} of {currentRows.Count} items, switch to List to see all of them.";
            }
            else
            {
                gridList.ItemsSource = null;
                fileList.SetRows(currentRows);
                gridOverflowText.IsVisible = false;
            }
        }

        private bool IsShowingPreview => previewTabs?.IsVisible == true;

        private void DisplayNodes(VirtualPackageNode pkgNode, bool updatePath = true)
        {
            var rows = new List<ListRow>(pkgNode.Folders.Count + pkgNode.Files.Count + 1);

            if (pkgNode.Parent != null)
            {
                rows.Add(new ListRow
                {
                    Name = pkgNode.Parent.Parent == null ? ".." : $".. {pkgNode.Parent.Name}",
                    Size = pkgNode.Parent.TotalSize,
                    Type = string.Empty,
                    IconName = "FolderUp",
                    IsParent = true,
                    PkgNode = pkgNode.Parent,
                });
            }

            foreach (var folder in pkgNode.Folders.Values.OrderBy(static f => f.Name, StringComparer.OrdinalIgnoreCase))
            {
                rows.Add(new ListRow
                {
                    Name = folder.Name,
                    Size = folder.TotalSize,
                    Type = string.Empty,
                    IconName = "Folder",
                    PkgNode = folder,
                });
            }

            foreach (var file in pkgNode.Files.OrderBy(static f => f.GetFileName(), StringComparer.OrdinalIgnoreCase))
            {
                rows.Add(new ListRow
                {
                    Name = file.GetFileName(),
                    Size = file.TotalLength,
                    Type = file.TypeName,
                    IconName = AppIcons.GetExtensionIconName(file.TypeName),
                    PackageEntry = file,
                });
            }

            currentRows = rows;
            ShowList();

            if (updatePath && pathBox != null)
            {
                pathBox.Text = GetFolderPath(pkgNode);
            }

            RecordNavigation(new FolderNavigationEntry(pkgNode));
        }

        private static string GetFolderPath(VirtualPackageNode node)
        {
            var path = node.GetFullPath();
            return path.Length == 0 ? string.Empty : $"{path}{Package.DirectorySeparatorChar}";
        }

        private void ShowList()
        {
            previewTokenSource?.Cancel();

            if (previewTabs != null)
            {
                previewTabs.IsVisible = false;
                previewTabs.Items.Clear();
            }

            previewTab?.Dispose();
            previewTab = null;

            if (toolbar != null)
            {
                toolbar.IsVisible = true;
            }

            RefreshItems();

            Program.MainForm.ShowSelectedTabStatus();
        }

        public void ShowPreview(DocumentTab tab)
        {
            if (previewTabs == null || fileList == null || gridList == null || toolbar == null)
            {
                tab.Dispose();
                return;
            }

            var previous = previewTab;
            previewTab = tab;

            fileList.IsVisible = false;
            ((Control)gridList.Parent!).IsVisible = false;
            toolbar.IsVisible = false;

            previewTabs.Items.Clear();
            previewTabs.Items.Add(tab);
            previewTabs.SelectedItem = tab;
            previewTabs.IsVisible = true;

            previous?.Dispose();

            Program.MainForm.ShowPreviewStatus(tab);
        }

        #region Navigation

        private void RecordNavigation(NavigationEntry entry)
        {
            if (suppressHistoryRecording)
            {
                return;
            }

            navigationHistory.Record(entry);
            UpdateNavigationButtons();
        }

        private void NavigateBack()
        {
            if (navigationHistory.Back() is { } entry)
            {
                NavigateTo(entry);
            }
        }

        private void NavigateForward()
        {
            if (navigationHistory.Forward() is { } entry)
            {
                NavigateTo(entry);
            }
        }

        private void NavigateTo(NavigationEntry entry)
        {
            suppressHistoryRecording = true;

            try
            {
                if (entry is SearchNavigationEntry search)
                {
                    PerformSearch(search.Search);
                }
                else if (entry is FolderNavigationEntry { Node: var node })
                {
                    SelectInTree(node, null);
                    DisplayNodes(node);
                }
            }
            finally
            {
                suppressHistoryRecording = false;
                UpdateNavigationButtons();
            }
        }

        private void UpdateNavigationButtons()
        {
            if (backButton != null && forwardButton != null)
            {
                backButton.IsEnabled = navigationHistory.CanGoBack;
                forwardButton.IsEnabled = navigationHistory.CanGoForward;
            }
        }

        /// <summary>Expands the tree down to <paramref name="folder"/> and selects it, or the file in it.</summary>
        private void SelectInTree(VirtualPackageNode folder, PackageEntry? file)
        {
            if (treeView == null || rootNode == null)
            {
                return;
            }

            var chain = new Stack<VirtualPackageNode>();

            for (var node = folder; node.Parent != null; node = node.Parent)
            {
                chain.Push(node);
            }

            // The deleted files root is its own tree root
            var treeNode = treeView.Items.OfType<BetterTreeNode>().FirstOrDefault(n => n.PkgNode == GetRoot(folder)) ?? rootNode;

            while (chain.TryPop(out var next))
            {
                treeNode.EnsureChildren();
                treeNode.IsExpanded = true;

                var child = treeNode.ChildNodes.FirstOrDefault(n => n.PkgNode == next);

                if (child == null)
                {
                    break;
                }

                treeNode = child;
            }

            if (file != null)
            {
                treeNode.EnsureChildren();
                treeNode.IsExpanded = true;
                treeNode = treeNode.ChildNodes.FirstOrDefault(n => n.PackageEntry == file) ?? treeNode;
            }

            suppressTreeSelection = true;

            try
            {
                treeView.SelectedItems.Clear();
                treeView.SelectedItem = treeNode;
                treeNode.BringIntoView();
            }
            finally
            {
                suppressTreeSelection = false;
            }
        }

        private static VirtualPackageNode GetRoot(VirtualPackageNode node)
        {
            while (node.Parent != null)
            {
                node = node.Parent;
            }

            return node;
        }

        private void OnPathBoxKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || VirtualRoot == null || pathBox == null)
            {
                return;
            }

            e.Handled = true;

            var node = VirtualRoot;
            PackageEntry? fileToSelect = null;

            var inputPath = (pathBox.Text ?? string.Empty)
                .Replace('\\', Package.DirectorySeparatorChar)
                .AsSpan()
                .Trim(Package.DirectorySeparatorChar);

            foreach (var segmentRange in inputPath.Split([Package.DirectorySeparatorChar]))
            {
                var name = inputPath[segmentRange].ToString();

                if (node.Folders.TryGetValue(name, out var nextNode))
                {
                    node = nextNode;
                    continue;
                }

                fileToSelect = node.Files.Find(file => file.GetFileName() == name);
                break;
            }

            SelectInTree(node, fileToSelect);
            DisplayNodes(node, updatePath: false);
        }

        #endregion

        #region Tree and list interaction

        private List<BetterTreeNode> SelectedTreeNodes() => treeView?.SelectedItems?.OfType<BetterTreeNode>().ToList() ?? [];

        private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (suppressTreeSelection || treeView?.SelectedItem is not BetterTreeNode node || SelectedTreeNodes().Count != 1)
            {
                return;
            }

            previewTokenSource?.Cancel();
            previewTokenSource?.Dispose();
            previewTokenSource = new CancellationTokenSource();
            var token = previewTokenSource.Token;

            if (node.IsFolder)
            {
                if (node.PkgNode != null)
                {
                    DisplayNodes(node.PkgNode);
                }

                return;
            }

            if (node.PackageEntry is not { } entry || !CanQuickPreviewFile(entry))
            {
                return;
            }

            // Waiting a little lets a double click open the file instead, and arrowing through files not load each one
            DispatcherTimer.RunOnce(() =>
            {
                if (!token.IsCancellationRequested)
                {
                    Preview(entry);
                }
            }, TimeSpan.FromMilliseconds(200));
        }

        private void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
        {
            var node = (e.Source as Avalonia.Visual)?.FindAncestorOfType<BetterTreeNode>(includeSelf: true);

            if (node?.PackageEntry == null || node != treeView?.SelectedItem)
            {
                return;
            }

            previewTokenSource?.Cancel();
            OpenEntries([node.PackageEntry], withoutViewer: false);
            ShowList();
            e.Handled = true;
        }

        /// <summary>
        /// Whether a quick file preview should be shown: quick preview must be enabled, and the file must not
        /// be one that is deliberately not previewed inline (vpk to avoid nesting, vmap_c).
        /// </summary>
        public static bool CanQuickPreviewFile(PackageEntry entry)
        {
            if (((Settings.QuickPreviewFlags)Settings.Config.QuickFilePreview & Settings.QuickPreviewFlags.Enabled) == 0)
            {
                return false;
            }

            return entry.TypeName is not ("vpk" or "vmap_c");
        }

        private void Preview(PackageEntry entry)
        {
#pragma warning disable CA2000 // The opened tab owns the context and disposes it
            var context = new VrfGuiContext(entry.GetFullPath(), vrfGuiContext);
#pragma warning restore CA2000
            Program.MainForm.OpenFile(context, entry, this);
        }

        private List<ListRow> SelectedRows()
        {
            var selected = IsGridMode ? gridList?.SelectedItems : fileList?.ItemList.SelectedItems;
            return selected?.OfType<ListRow>().ToList() ?? [];
        }

        private void OnItemsDoubleTapped(TappedEventArgs e)
        {
            if (SelectedRows() is not [var row])
            {
                return;
            }

            e.Handled = true;
            OpenRow(row);
        }

        private void OnItemsKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && SelectedRows() is [var row])
            {
                OpenRow(row);
                e.Handled = true;
            }
            else if (e.Key == Key.Back && currentRows is [{ IsParent: true } parent, ..])
            {
                OpenRow(parent);
                e.Handled = true;
            }
        }

        private void OpenRow(ListRow row)
        {
            if (row.PkgNode != null)
            {
                SelectInTree(row.PkgNode, null);
                DisplayNodes(row.PkgNode);
            }
            else if (row.PackageEntry != null)
            {
                OpenEntries([row.PackageEntry], withoutViewer: false);
            }
        }

        private void OpenEntries(IEnumerable<PackageEntry> entries, bool withoutViewer)
        {
            foreach (var entry in entries)
            {
#pragma warning disable CA2000 // The opened tab owns the context and disposes it
                var context = new VrfGuiContext(entry.GetFullPath(), vrfGuiContext);
#pragma warning restore CA2000
                Program.MainForm.OpenFile(context, entry, withoutViewer: withoutViewer);
            }
        }

        #endregion

        #region Context menu

        private void OnContextRequested(Control source, ContextRequestedEventArgs e)
        {
            List<IBetterBaseItem> items;

            if (source is TreeView tree)
            {
                var node = (e.Source as Avalonia.Visual)?.FindAncestorOfType<BetterTreeNode>(includeSelf: true);

                if (node == null)
                {
                    return;
                }

                // Right clicking inside a multi selection keeps it, so the menu acts on all of it
                if (!tree.SelectedItems.Contains(node))
                {
                    suppressTreeSelection = true;
                    tree.SelectedItems.Clear();
                    tree.SelectedItem = node;
                    suppressTreeSelection = false;
                }

                items = [.. SelectedTreeNodes()];
            }
            else
            {
                items = [.. SelectedRows().Where(static row => !row.IsParent)];
            }

            if (items.Count == 0)
            {
                return;
            }

            e.Handled = true;

            var isRoot = items is [BetterTreeNode { PkgNode.Parent: null }];
            var isFolder = items.All(static item => item.IsFolder);
            var menu = isEditingPackage
                ? CreateEditingContextMenu(items[0], isRoot)
                : CreateContextMenu(items, isRoot, isFolder);
            menu.Open(source);
        }

        private ContextMenu CreateContextMenu(List<IBetterBaseItem> items, bool isRoot, bool isFolder)
        {
            var menu = new ContextMenu();

            MenuItem Item(string header, string icon, Action onClick)
            {
                var item = new MenuItem { Header = header, Icon = AppIcons.Create(icon) };
                item.Click += (_, _) => onClick();
                menu.Items.Add(item);
                return item;
            }

            Item("Export as is", "Export", () => _ = ExportFile.ExtractSelectedItems(items, vrfGuiContext, decompile: false));
            Item("Decompile & export", "Decompile", () => _ = ExportFile.ExtractSelectedItems(items, vrfGuiContext, decompile: true));

            // Type specific exporters are only offered when the selection holds files they can export
            if (ContextMenuSelection.ContainsFileType(items, "vmdl_c"))
            {
                Item("Decompile & export (custom VMDL extractor)", "Decompile", () => RunAsync(() => CustomVmdlExporter.ExtractAsync(vrfGuiContext, items)));
            }

            if (ContextMenuSelection.ContainsFileType(items, CustomVmatExporter.MaterialTypeName))
            {
                Item("Decompile & export (custom VMAT extractor)", "Decompile", () => RunAsync(() => CustomVmatExporter.ExtractAsync(vrfGuiContext, items)));
            }

            if (isRoot && CharacterAssetsExporter.CanExport(vrfGuiContext))
            {
                // A single underscore marks the access key in a menu header
                Item("Export character assets (items__game.txt)...", "Decompile", () => RunAsync(() => CharacterAssetsExporter.ExportAsync(vrfGuiContext)));
            }

            menu.Items.Add(new Separator());

            if (!isRoot)
            {
                Item("Copy name", "CopyName", () => CopyFileName(items, wantsFullPath: false));
            }

            Item("Copy URL", "CopyURL", () => CopyFileName(items, wantsFullPath: true));

            if (isRoot || !isFolder)
            {
                menu.Items.Add(new Separator());
            }

            if (!isRoot && !isFolder)
            {
                var files = items.Select(static item => item.PackageEntry).OfType<PackageEntry>().ToList();

                Item("Open without viewer", "OpenWithoutViewer", () => OpenEntries(files, withoutViewer: true));
                Item("Open with default app", "OpenWithDefaultApp", () => _ = OpenWithDefaultAppAsync(files));

                if (files is [var single])
                {
                    Item("View asset info", "Info", () => Program.MainForm.AddTab(SingleAssetInfo.Create(vrfGuiContext, single)));
                }
            }

            if (isRoot)
            {
                Item("Verify package contents", "VPKVerifyContent", VerifyPackageContents);

                if (!DeletedFilesRecovered)
                {
                    Item("Recover deleted files", "Recover", RecoverDeletedFiles);
                }
            }

            return menu;
        }

        /// <summary>Runs a menu action, reporting what it throws like an unhandled error.</summary>
        private static async void RunAsync(Func<Task> action)
        {
            try
            {
                await action().ConfigureAwait(true);
            }
            catch (Exception e)
            {
                Program.ShowError(e);
            }
        }

        private void CopyFileName(List<IBetterBaseItem> items, bool wantsFullPath)
        {
            var sb = new StringBuilder();

            foreach (var item in items)
            {
                if (sb.Length > 0)
                {
                    sb.AppendLine();
                }

                if (wantsFullPath)
                {
                    sb.Append("vpk:");

                    var packageChain = new Stack<string>();

                    for (var chainContext = vrfGuiContext; chainContext != null; chainContext = chainContext.ParentGuiContext)
                    {
                        packageChain.Push(chainContext.FileName);
                    }

                    sb.AppendJoin(':', packageChain.Select(static segment => segment.Replace('\\', '/')));
                }

                if (item.PackageEntry != null)
                {
                    if (wantsFullPath)
                    {
                        sb.Append(':');
                    }

                    sb.Append(item.PackageEntry.GetFullPath());
                }
                else if (item.PkgNode is { Parent: not null } node)
                {
                    if (wantsFullPath)
                    {
                        sb.Append(':');
                    }

                    sb.Append(GetFolderPath(node));
                }
            }

            if (sb.Length > 0)
            {
                AppClipboard.SetText(sb.ToString());
            }
        }

        private async Task OpenWithDefaultAppAsync(List<PackageEntry> files)
        {
            if (vrfGuiContext.CurrentPackage is not { } package)
            {
                return;
            }

            if (files.Count > 5 && !await AppMessageDialogs.ConfirmAsync(
                $"You are trying to open {files.Count} files in the default app for each of these files, are you sure you want to continue?",
                "Trying to open many files in the default app",
                buttons: ConfirmButtons.YesNo).ConfigureAwait(true))
            {
                return;
            }

            foreach (var file in files)
            {
                var tempPath = Path.Join(Path.GetTempPath(), $"Source 2 Viewer - {Path.GetFileName(package.FileName)} - {file.GetFileName()}");

                try
                {
                    using (var output = File.Create(tempPath))
                    using (var input = GameFileLoader.GetPackageEntryStream(package, file))
                    {
                        await input.CopyToAsync(output).ConfigureAwait(true);
                    }

                    Process.Start(new ProcessStartInfo(tempPath) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Log.Error(nameof(PackageViewer), $"Failed to open '{file.GetFullPath()}' with the default app: {ex.Message}");
                }
            }
        }

        private void VerifyPackageContents()
        {
            var package = vrfGuiContext.CurrentPackage;

            if (package?.Entries == null)
            {
                return;
            }

            using var progressDialog = new GenericProgressForm
            {
                Text = "Verifying package\u2026",
            };
            progressDialog.OnProcess = cancellationToken =>
            {
                try
                {
                    if (!package.IsSignatureValid())
                    {
                        throw new InvalidDataException("The signature in this package is not valid.");
                    }

                    progressDialog.SetProgress("Verifying hashes\u2026");

                    package.VerifyHashes();

                    // This does not need to be perfect, ValvePak reports a string per file, and success strings
                    var maximum = package.AccessPackFileHashes.Count + 2;

                    if (package.AccessPackFileHashes.Count == 0)
                    {
                        maximum += package.Entries.Sum(static x => x.Value.Count);
                    }

                    progressDialog.SetBarMax(maximum);

                    var processed = 0;
                    var progressReporter = new Progress<string>(text =>
                    {
                        progressDialog.SetBarValue(Interlocked.Increment(ref processed));
                        progressDialog.SetProgress(text);
                    });

                    if (!cancellationToken.IsCancellationRequested)
                    {
                        package.VerifyChunkHashes(progressReporter);
                    }

                    if (!cancellationToken.IsCancellationRequested && package.AccessPackFileHashes.Count == 0)
                    {
                        package.VerifyFileChecksums(progressReporter);
                    }

                    if (!cancellationToken.IsCancellationRequested)
                    {
                        progressDialog.SetBarValue(maximum);
                        Dispatcher.UIThread.Post(() => _ = AppMessageDialogs.ShowMessageAsync("Successfully verified package contents.", "Verified package contents"));
                    }
                }
                catch (Exception e)
                {
                    Log.Error(nameof(Package), $"Failed to verify package contents: {e.Message}");

                    if (!cancellationToken.IsCancellationRequested)
                    {
                        Dispatcher.UIThread.Post(() => _ = AppMessageDialogs.ShowMessageAsync(e.Message, "Failed to verify package contents", MessageIcon.Warning));
                    }
                }

                return Task.CompletedTask;
            };
            progressDialog.ShowDialog();
        }

        private void RecoverDeletedFiles()
        {
            DeletedFilesRecovered = true;

            var package = vrfGuiContext.CurrentPackage;

            if (package == null || treeView == null)
            {
                return;
            }

            List<PackageEntry> foundFiles = [];

            using (var progressDialog = new GenericProgressForm { Text = "Scanning for deleted files\u2026" })
            {
                progressDialog.OnProcess = _ =>
                {
                    progressDialog.SetProgress("Scanning for deleted files, this may take a while\u2026");
                    foundFiles = PackageRecovery.RecoverDeletedFiles(package, progressDialog);
                    return Task.CompletedTask;
                };
                progressDialog.ShowDialog();
            }

            if (foundFiles.Count == 0)
            {
                var none = new VirtualPackageNode("No deleted files found", 0, null);
                treeView.Items.Add(new BetterTreeNode(none.Name, none, "Recover"));
                return;
            }

            var rootVirtual = new VirtualPackageNode($"Deleted files ({foundFiles.Count} files found, names are guessed)", 0, null);

            foreach (var file in foundFiles)
            {
                AddFileNode(rootVirtual, file);
            }

            var root = new BetterTreeNode(rootVirtual.Name, rootVirtual, "Recover");
            treeView.Items.Add(root);
            root.IsExpanded = true;

            suppressTreeSelection = true;
            treeView.SelectedItem = root;
            suppressTreeSelection = false;

            DisplayNodes(rootVirtual);
        }

        #endregion

        #region Editing

        private ContextMenu CreateEditingContextMenu(IBetterBaseItem item, bool isRoot)
        {
            var menu = new ContextMenu();

            void Item(string header, string icon, Action onClick)
            {
                var menuItem = new MenuItem { Header = header, Icon = AppIcons.Create(icon) };
                menuItem.Click += (_, _) => onClick();
                menu.Items.Add(menuItem);
            }

            if (item.PkgNode is { } folder)
            {
                Item("Create folder", "FolderCreate", () => _ = CreateFolderAsync(folder));
                Item("_Add existing folder", "FolderAdd", () => _ = AddExistingFolderAsync(folder));
                Item("Add existing _files", "FileAdd", () => _ = AddExistingFilesAsync(folder));

                if (!isRoot)
                {
                    Item("_Remove this folder", "FolderRemove", () => RemoveFolder(folder));
                }
            }
            else if (item.PackageEntry is { } entry)
            {
                Item("_Remove this file", "CloseTab", () => RemoveFile(entry));
            }

            if (isRoot)
            {
                Item("_Save VPK to disk", "VPKSave", () => _ = SaveToDiskAsync());
            }

            return menu;
        }

        /// <summary>Path of <paramref name="name"/> inside <paramref name="folder"/>, with the package's separators.</summary>
        private static string GetPackagePath(VirtualPackageNode folder, string name)
        {
            name = name.Replace('\\', Package.DirectorySeparatorChar).Trim(Package.DirectorySeparatorChar);

            var prefix = folder.GetFullPath();
            return prefix.Length > 0 ? $"{prefix}{Package.DirectorySeparatorChar}{name}" : name;
        }

        private async Task CreateFolderAsync(VirtualPackageNode parent)
        {
            var name = await AppMessageDialogs.PromptAsync("New folder name").ConfigureAwait(true);

            if (string.IsNullOrWhiteSpace(name) || VirtualRoot == null)
            {
                return;
            }

            if (name.IndexOfAny(Path.GetInvalidPathChars()) != -1)
            {
                await AppMessageDialogs.ShowMessageAsync("Entered folder name contains invalid characters.", "Invalid characters", MessageIcon.Warning).ConfigureAwait(true);
                return;
            }

            var folder = AddFolderNode(VirtualRoot, GetPackagePath(parent, name.Trim()), 0);
            RefreshAfterEdit(folder);
        }

        private async Task AddExistingFolderAsync(VirtualPackageNode folder)
        {
            var inputDirectory = await AppFileDialogs.PickFolderAsync("Choose which folder to pack into a VPK", AppFileDialogs.RememberIn.OpenDirectory).ConfigureAwait(true);

            if (inputDirectory == null)
            {
                return;
            }

            AddFiles(folder, Directory.EnumerateFiles(inputDirectory, "*", SearchOption.AllDirectories), inputDirectory);
        }

        private async Task AddExistingFilesAsync(VirtualPackageNode folder)
        {
            var files = await AppFileDialogs.OpenFilesAsync("Choose which files to add to the VPK", null).ConfigureAwait(true);

            if (files == null)
            {
                return;
            }

            AddFiles(folder, files, null);
        }

        /// <summary>
        /// Adds files to <paramref name="folder"/>, keeping their paths below <paramref name="inputDirectory"/> when one is given.
        /// </summary>
        private void AddFiles(VirtualPackageNode folder, IEnumerable<string> files, string? inputDirectory)
        {
            if (vrfGuiContext.CurrentPackage is not { } package || VirtualRoot == null)
            {
                return;
            }

            foreach (var file in files)
            {
                if (!File.Exists(file))
                {
                    continue;
                }

                var name = inputDirectory == null ? Path.GetFileName(file) : Path.GetRelativePath(inputDirectory, file);

                try
                {
                    var entry = package.AddFile(GetPackagePath(folder, name), File.ReadAllBytes(file));
                    AddFileNode(VirtualRoot, entry);
                }
                catch (Exception e)
                {
                    Log.Error(nameof(PackageViewer), $"Failed to add '{file}': {e.Message}");
                }
            }

            RefreshAfterEdit(folder);
        }

        private void RemoveFolder(VirtualPackageNode folder)
        {
            RemovePackageFiles(folder);
            folder.Parent?.Folders.Remove(folder.Name);

            navigationHistory.RemoveSubtree(folder);
            UpdateNavigationButtons();

            RefreshAfterEdit(folder.Parent);
        }

        private void RemovePackageFiles(VirtualPackageNode folder)
        {
            foreach (var child in folder.Folders.Values)
            {
                RemovePackageFiles(child);
            }

            foreach (var file in folder.Files)
            {
                vrfGuiContext.CurrentPackage?.RemoveFile(file);
            }
        }

        private void RemoveFile(PackageEntry entry)
        {
            var folder = FindFolder(entry.DirectoryName);

            folder?.Files.Remove(entry);
            vrfGuiContext.CurrentPackage?.RemoveFile(entry);

            RefreshAfterEdit(folder);
        }

        private VirtualPackageNode? FindFolder(string directoryName)
        {
            var node = VirtualRoot;

            if (node == null || string.IsNullOrWhiteSpace(directoryName))
            {
                return node;
            }

            foreach (var name in directoryName.Split(Package.DirectorySeparatorChar))
            {
                if (!node.Folders.TryGetValue(name, out var next))
                {
                    return null;
                }

                node = next;
            }

            return node;
        }

        /// <summary>Rebuilds the tree from the edited package and shows <paramref name="folder"/>.</summary>
        private void RefreshAfterEdit(VirtualPackageNode? folder)
        {
            if (rootNode == null || VirtualRoot == null)
            {
                return;
            }

            rootNode.Invalidate();
            rootNode.IsExpanded = true;

            folder ??= VirtualRoot;
            SelectInTree(folder, null);
            DisplayNodes(folder);
        }

        private async Task SaveToDiskAsync()
        {
            if (vrfGuiContext.CurrentPackage is not { } package)
            {
                return;
            }

            var (fileName, _) = await AppFileDialogs.SaveFileAsync("Save VPK package", null, "vpk", "Valve Pak|*.vpk").ConfigureAwait(true);

            if (fileName == null)
            {
                return;
            }

            Log.Info(nameof(PackageViewer), $"Packing to '{fileName}'...");

            try
            {
                package.Write(fileName);
            }
            catch (Exception e)
            {
                Log.Error(nameof(PackageViewer), $"Failed to save '{fileName}': {e}");
                await AppMessageDialogs.ShowMessageAsync(e.Message, "Failed to save VPK", MessageIcon.Error).ConfigureAwait(true);
                return;
            }

            var fileCount = 0;
            var fileSize = 0L;

            if (package.Entries != null)
            {
                foreach (var fileType in package.Entries)
                {
                    foreach (var file in fileType.Value)
                    {
                        fileCount++;
                        fileSize += file.TotalLength;
                    }
                }
            }

            var result = $"Created {Path.GetFileName(fileName)} with {fileCount} files of size {HumanReadableByteSizeFormatter.Format(fileSize)}.";

            Log.Info(nameof(PackageViewer), result);

            await AppMessageDialogs.ShowMessageAsync(result, "VPK created").ConfigureAwait(true);
        }

        #endregion

        #region Search

        /// <summary>Shows the Find dialog and lists the matching files.</summary>
        public async Task ShowSearchAsync()
        {
            if (Program.MainForm is not { } owner)
            {
                return;
            }

            searchWindow ??= new SearchWindow();
            searchWindow.SetSearchableUserDataKeys(GetSearchDataKeysAsync());

            var result = await searchWindow.ShowDialog<bool>(owner).ConfigureAwait(true);

            // A closed window can not be shown again, keep the entered values in a fresh one next time
            var closedWindow = searchWindow;
            searchWindow = null;

            if (!result)
            {
                return;
            }

            var searchText = closedWindow.SearchText;
            var filterKey = closedWindow.SelectedFilterKey;

            if (!string.IsNullOrEmpty(searchText) || filterKey != null)
            {
                SearchAndFillResults(searchText, closedWindow.SelectedSearchType, filterKey, closedWindow.SelectedFilterValue);
            }
        }

        internal void SearchAndFillResults(string searchText, SearchType searchType, string? filterKey = null, string? filterValue = null)
        {
            var request = new SearchRequest(searchText, searchType, filterKey, filterValue);

            suppressHistoryRecording = true;

            try
            {
                PerformSearch(request);
            }
            finally
            {
                suppressHistoryRecording = false;
            }

            RecordNavigation(new SearchNavigationEntry(request));
        }

        private void PerformSearch(SearchRequest request)
        {
            List<PackageEntry> results;

            try
            {
                results = Search(request.Text, request.Type);
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or FormatException)
            {
                _ = AppMessageDialogs.ShowMessageAsync(e.Message, "Invalid search", MessageIcon.Warning);
                return;
            }

            if (request.FilterKey != null && assetSearchData != null)
            {
                results.RemoveAll(entry => !MatchesAssetFilter(entry, request.FilterKey, request.FilterValue));
            }

            var node = new VirtualPackageNode(string.Empty, 0, null);
            node.Files.AddRange(results);

            DisplayNodes(node, updatePath: false);
        }

        private List<PackageEntry> Search(string value, SearchType searchType)
        {
            var results = new List<PackageEntry>();

            if (VirtualRoot == null)
            {
                return results;
            }

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
                    SearchTree(VirtualRoot, results, entry => entry.GetFileName().Equals(value, StringComparison.OrdinalIgnoreCase));
                    break;

                case SearchType.FileNamePartialMatch:
                    SearchTree(VirtualRoot, results, entry => entry.GetFileName().Contains(value, StringComparison.OrdinalIgnoreCase));
                    break;

                case SearchType.FullPath:
                    SearchTree(VirtualRoot, results, entry => entry.GetFullPath().Contains(value, StringComparison.OrdinalIgnoreCase));
                    break;

                case SearchType.Regex:
                {
                    var regex = new Regex(value, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                    SearchTree(VirtualRoot, results, entry => regex.IsMatch(entry.GetFileName()));
                    break;
                }

                case SearchType.FileContents:
                    SearchFileContents(results, Encoding.UTF8.GetBytes(value));
                    break;

                case SearchType.FileContentsHex:
                    SearchFileContents(results, Convert.FromHexString(value.Replace(" ", "", StringComparison.Ordinal)));
                    break;
            }

            return results;
        }

        private static void SearchTree(VirtualPackageNode node, List<PackageEntry> results, Func<PackageEntry, bool> matchFunction)
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
                SearchTree(folder, results, matchFunction);
            }
        }

        private void SearchFileContents(List<PackageEntry> results, byte[] pattern)
        {
            if (vrfGuiContext.ParentGuiContext != null)
            {
                throw new InvalidOperationException("Inner paks are not supported.");
            }

            var package = vrfGuiContext.CurrentPackage;

            if (package?.Entries == null)
            {
                return;
            }

            using var progressDialog = new GenericProgressForm
            {
                Text = "Searching file contents\u2026",
            };
            progressDialog.OnProcess = cancellationToken =>
            {
                Log.Info(nameof(PackageViewer), "Pattern search");

                var matches = PackageContentSearch.Search(package, pattern, progressDialog, cancellationToken);
                results.AddRange(matches);

                Log.Info(nameof(PackageViewer), $"Found {results.Count} matches");

                return Task.CompletedTask;
            };
            progressDialog.ShowDialog();
        }

        private Task<Dictionary<string, SortedSet<string>>?> GetSearchDataKeysAsync()
        {
            if (searchDataBuilt)
            {
                return Task.FromResult(searchDataKeys);
            }

            return Task.Run(GetSearchDataKeys);
        }

        /// <summary>
        /// Loads the tools asset info and indexes its searchable user data, returning the keys with their values.
        /// </summary>
        private Dictionary<string, SortedSet<string>>? GetSearchDataKeys()
        {
            if (searchDataBuilt)
            {
                return searchDataKeys;
            }

            searchDataBuilt = true;

            var toolsAssetInfo = vrfGuiContext.GetOrLoadToolsAssetInfo();

            if (toolsAssetInfo == null)
            {
                return null;
            }

            var data = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            var keys = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var (filePath, file) in toolsAssetInfo.Files)
            {
                if (file.SearchableUserData.Count == 0)
                {
                    continue;
                }

                var converted = new Dictionary<string, string>(file.SearchableUserData.Count, StringComparer.OrdinalIgnoreCase);

                foreach (var (key, value) in file.SearchableUserData)
                {
                    var stringValue = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                    converted[key] = stringValue;

                    if (!keys.TryGetValue(key, out var values))
                    {
                        values = new SortedSet<string>(SearchWindow.NumericComparer);
                        keys[key] = values;
                    }

                    if (values.Count < 100)
                    {
                        values.Add(stringValue);
                    }
                }

                data[filePath] = converted;
            }

            if (keys.Count > 0)
            {
                assetSearchData = data;
                searchDataKeys = keys;
            }

            return searchDataKeys;
        }

        private bool MatchesAssetFilter(PackageEntry entry, string filterKey, string? filterValue)
        {
            Debug.Assert(assetSearchData is not null);

            var filePath = entry.GetFullPath();

            if (!assetSearchData.TryGetValue(filePath, out var searchData))
            {
                if (!filePath.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
                {
                    return false;
                }

                var uncompiledPath = filePath.AsSpan(0, filePath.Length - GameFileLoader.CompiledFileSuffix.Length);

                if (!assetSearchData.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(uncompiledPath, out searchData))
                {
                    return false;
                }
            }

            if (!searchData.TryGetValue(filterKey, out var value))
            {
                return false;
            }

            return filterValue == null || value == filterValue;
        }

        #endregion

        public void Dispose()
        {
            previewTokenSource?.Cancel();
            previewTokenSource?.Dispose();
            previewTokenSource = null;

            previewTab?.Dispose();
            previewTab = null;

            searchWindow?.Close();
            searchWindow = null;
        }
    }
}
