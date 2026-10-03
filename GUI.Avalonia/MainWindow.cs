using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using GUI.Controls;
using GUI.Forms;
using GUI.Types.Exporter;
using GUI.Types.GLViewers;
using GUI.Types.Viewers;
using GUI.Utils;
using ValvePak;
using ValveResourceFormat.TextureDecoders;

namespace GUI
{
    /// <summary>
    /// The main window, laid out like the WinForms MainForm: the logo and menu bar on top, the console and
    /// file tabs, and the status bar with the window title, keybindings and version.
    /// </summary>
    sealed class MainWindow : Window
    {
        private const string OpenFileFilter = "Valve Resource Format (*.*_c, *.vpk)|*.*_c;*.vpk;*.vcs|All files (*.*)|*.*";
        private const string AppTitle = "Source 2 Viewer";

        private readonly TabControl mainTabs;
        private readonly DocumentTab consoleTab;
        private readonly MainBottomPanel bottomPanel;
        private ExplorerControl? explorerControl;
        private string windowTitle = AppTitle;

        // The web view is destroyed whenever it leaves the visual tree, which a tab page does each time another
        // tab is selected. So it lives over the pages instead, and is only shown while its tab is selected.
        private readonly Decorator pageOverlay;
        private ContentPresenter? pageHost;
        private DocumentTab? d2pfxTab;

        public MainWindow()
        {
            Title = AppTitle;
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Source2Viewer.Avalonia/Assets/source2viewer.ico")));
            MinWidth = 347;
            MinHeight = 380;

            RestoreWindowPlacement();

            // Like the WinForms custom frame, the logo and menu bar are the title bar, with the window
            // buttons drawn at its right end. Other platforms keep their own window decorations.
            if (OperatingSystem.IsWindows())
            {
                ExtendClientAreaToDecorationsHint = true;
                ExtendClientAreaTitleBarHeightHint = Themer.MainTitleBarHeight;
            }

            var console = new ConsoleTab();
            Log.SetConsoleTab(console);

            mainTabs = ViewerContentPresenter.CreateTabControl();
            mainTabs.Classes.Remove("content");
            mainTabs.Classes.Add("main");
            mainTabs.ItemsPanel = new FuncTemplate<Panel?>(static () => new MainTabStripPanel());
            mainTabs.SelectionChanged += OnMainSelectedTabChanged;
            mainTabs.AddHandler(ContextRequestedEvent, OnTabContextRequested, RoutingStrategies.Bubble);
            mainTabs.TemplateApplied += (_, e) => pageHost = e.NameScope.Find<ContentPresenter>("PART_SelectedContentHost");
            mainTabs.LayoutUpdated += (_, _) => UpdatePageOverlayBounds();

            pageOverlay = new Decorator
            {
                IsVisible = false,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            };

            consoleTab = new DocumentTab("Console", "Log", closable: false)
            {
                ToolTipText = "Console",
                Content = new ConsoleView(console),
            };
            mainTabs.Items.Add(consoleTab);

            bottomPanel = new MainBottomPanel();
            bottomPanel.SetVersionText(Program.DisplayVersion);
            bottomPanel.AboutRequested += async (_, _) => await ShowAboutDialogAsync().ConfigureAwait(true);

            var root = new DockPanel();
            var topBar = CreateTopBar();
            DockPanel.SetDock(topBar, Dock.Top);
            DockPanel.SetDock(bottomPanel, Dock.Bottom);
            root.Children.Add(topBar);
            root.Children.Add(bottomPanel);
            root.Children.Add(new Panel { Children = { mainTabs, pageOverlay } });
            Content = root;

            AddKeyBindings();

            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DragOverEvent, OnDragOver);
            AddHandler(DragDrop.DropEvent, OnDrop);

            Opened += OnOpened;
            Closing += OnClosing;
            Activated += (_, _) => RenderLoopThread.SetWindowActive(this, true);
            Deactivated += (_, _) => RenderLoopThread.SetWindowActive(this, false);

            // Let the explorer start scanning games before the window is even shown
            if (Program.StartupFiles.Length == 0 && (Settings.IsFirstStartup || Settings.Config.OpenExplorerOnStart != 0))
            {
                EnsureExplorerControl();
            }

            UpdateWindowTitle(null);

            Log.Info(nameof(MainWindow), $"{AppTitle} {Program.DisplayVersion} on {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
        }

        private DockPanel CreateTopBar()
        {
            var logo = AppIcons.Create("Logo", 32);
            logo.Margin = new(4, 8);
            logo.VerticalAlignment = VerticalAlignment.Center;
            logo.Cursor = new Cursor(StandardCursorType.Hand);
            logo.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton == MouseButton.Left)
                {
                    OpenSystemMenu(logo);
                }
            };

            var menu = CreateMenu();
            menu.Classes.Add("main");
            menu.VerticalAlignment = VerticalAlignment.Center;
            menu.HorizontalAlignment = HorizontalAlignment.Left;

            var topBar = new DockPanel { Height = Themer.MainTitleBarHeight, Margin = new(0, 0, 0, 4) };
            topBar.Classes.Add("topBar");
            DockPanel.SetDock(logo, Dock.Left);
            topBar.Children.Add(logo);
            topBar.Children.Add(menu);

            // The empty part of the bar drags the window, the logo and the menu stay clickable
            WindowDecorationProperties.SetElementRole(topBar, WindowDecorationsElementRole.TitleBar);
            WindowDecorationProperties.SetElementRole(logo, WindowDecorationsElementRole.User);
            WindowDecorationProperties.SetElementRole(menu, WindowDecorationsElementRole.User);

            return topBar;
        }

        /// <summary>
        /// The window menu, opened from the logo like the WinForms title bar opens the system menu,
        /// with the same website, settings and about entries added to it.
        /// </summary>
        private void OpenSystemMenu(Control target)
        {
            var maximized = WindowState == WindowState.Maximized;
            var menu = new ContextMenu
            {
                Placement = PlacementMode.BottomEdgeAlignedLeft,
                PlacementTarget = target,
            };

            void Item(string header, Action onClick, bool enabled = true, KeyGesture? gesture = null)
            {
                var item = new MenuItem { Header = header, IsEnabled = enabled, InputGesture = gesture };
                item.Click += (_, _) => onClick();
                menu.Items.Add(item);
            }

            Item("_Restore", () => WindowState = WindowState.Normal, enabled: maximized);
            Item("Mi_nimize", () => WindowState = WindowState.Minimized);
            Item("Ma_ximize", () => WindowState = WindowState.Maximized, enabled: !maximized);
            menu.Items.Add(new Separator());
            Item("_Close", Close, gesture: new KeyGesture(Key.F4, KeyModifiers.Alt));
            menu.Items.Add(new Separator());
            Item("Website", () => AboutWindow.OpenUrl(D2PfxBrowserControl.TargetUrl));
            Item("Settings", OpenSettings);
            Item("About", () => _ = ShowAboutDialogAsync());

            menu.Open(target);
        }

        /// <summary>
        /// A menu bar item with its icon in front of the text, the theme only shows icons in drop downs.
        /// </summary>
        private static MenuItem CreateTopLevelItem(string header, string icon)
        {
            return new MenuItem
            {
                Header = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children =
                    {
                        AppIcons.Create(icon),
                        new AccessText { Text = header, VerticalAlignment = VerticalAlignment.Center },
                    },
                },
            };
        }

        private Menu CreateMenu()
        {
            static MenuItem Item(string header, string icon, Action onClick, KeyGesture? gesture = null)
            {
                var item = new MenuItem { Header = header, Icon = AppIcons.Create(icon), InputGesture = gesture };
                item.Click += (_, _) => onClick();
                return item;
            }

            static MenuItem TopLevelItem(string header, string icon, Action onClick)
            {
                var item = CreateTopLevelItem(header, icon);
                item.Click += (_, _) => onClick();
                return item;
            }

            var file = CreateTopLevelItem("F_ile", "Folder");
            file.Items.Add(Item("_Open", "Open", () => _ = OpenFilesFromDialogAsync(), new KeyGesture(Key.O, KeyModifiers.Control)));
            file.Items.Add(new Separator());

            if (FileAssociation.IsSupported)
            {
                file.Items.Add(Item("Open VPKs with this app", "VPKLink", () => _ = FileAssociation.RegisterAsync()));
            }

            file.Items.Add(Item("Create VPK from folder", "VPKCreate", CreateVpkFromFolder));
            file.Items.Add(new Separator());
            file.Items.Add(Item("Open welcome screen", "WelcomeScreen", OpenWelcome));
#if DEBUG
            file.Items.Add(Item("Validate shaders", "ValidateShaders", ValidateShaders));
#endif

            var tools = CreateTopLevelItem("_Tools", "Tools");

            // These tools are WinForms dialogs that have not been ported yet
            foreach (var (header, icon) in new[]
            {
                ("Export character assets (items_game.txt)...", "Decompile"),
                ("VTEX Create", "AssetTypes.tex"),
                ("Particles recolor", "AssetTypes.pcf"),
            })
            {
                var item = new MenuItem { Header = header, Icon = AppIcons.Create(icon), IsEnabled = false };
                ToolTip.SetTip(item, "Not available in the cross-platform build yet.");
                ToolTip.SetShowOnDisabled(item, true);
                tools.Items.Add(item);
            }

            return new Menu
            {
                Items =
                {
                    file,
                    TopLevelItem("Explorer", "Explorer", OpenExplorer),
                    TopLevelItem("_Find", "Find", () => _ = FindAsync()),
                    tools,
                    TopLevelItem("_Settings", "Settings", OpenSettings),
                    TopLevelItem("_About", "About", () => _ = ShowAboutDialogAsync()),
                    TopLevelItem("dota2prnfx", "dota2pornfx", ShowD2PfxTab),
                },
            };
        }

        private void AddKeyBindings()
        {
            void Bind(Key key, KeyModifiers modifiers, Action action) => KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(key, modifiers),
                Command = new ActionCommand(action),
            });

            Bind(Key.O, KeyModifiers.Control, () => _ = OpenFilesFromDialogAsync());
            Bind(Key.F, KeyModifiers.Control, () => _ = FindAsync());
            Bind(Key.W, KeyModifiers.Control, () => CloseTab(SelectedTab));
            Bind(Key.Q, KeyModifiers.Control, CloseAllTabs);
            Bind(Key.E, KeyModifiers.Control, () => CloseTabsToRight(SelectedTab));
            Bind(Key.R, KeyModifiers.Control, CloseAndReOpenActiveTab);
            Bind(Key.F5, KeyModifiers.None, CloseAndReOpenActiveTab);
        }

        private DocumentTab? SelectedTab => mainTabs.SelectedItem as DocumentTab;

        private void OnOpened(object? sender, EventArgs e)
        {
            try
            {
                HardwareAcceleratedTextureDecoder.Decoder = new GLTextureDecoder(VrfGuiContext.Logger);
            }
            catch (Exception ex)
            {
                // Without a GL 4.6 context nothing 3D works, the text viewers still do
                Log.Error(nameof(MainWindow), $"Failed to create an OpenGL {ValveResourceFormat.Renderer.GLEnvironment.RequiredVersion} context, textures will be decoded in software and 3D viewers will not work: {ex.Message}");
            }

            if (Settings.IsFirstStartup)
            {
                OpenWelcome();
            }
            else if (Program.StartupFiles.Length > 0)
            {
                OpenCommandLineArgFiles(Program.StartupFiles);
            }
            else if (Settings.Config.OpenExplorerOnStart != 0)
            {
                OpenExplorer();
            }

            _ = CheckForUpdatesIfNecessaryAsync();
        }

        private async Task CheckForUpdatesIfNecessaryAsync()
        {
            try
            {
                await UpdateChecker.CheckForUpdatesIfNecessary().ConfigureAwait(true);
            }
            catch (Exception e)
            {
                Log.Warn(nameof(MainWindow), $"Failed to check for updates: {e.Message}");
            }

            bottomPanel.RefreshUpdateState();
        }

        public void ShowUpdateAfterError() => bottomPanel.ShowUpdateAfterError();

        private async Task ShowAboutDialogAsync()
        {
            await new AboutWindow().ShowDialog(this).ConfigureAwait(true);
            bottomPanel.RefreshUpdateState();
        }

        public void OpenCommandLineArgFiles(string[] args)
        {
            foreach (var arg in args)
            {
                // Handle vpk: protocol
                if (arg.StartsWith("vpk:", StringComparison.InvariantCulture))
                {
                    OpenPackageProtocolPath(System.Net.WebUtility.UrlDecode(arg[4..]));
                    continue;
                }

                if (!File.Exists(arg))
                {
                    Log.Error(nameof(MainWindow), $"File '{arg}' does not exist.");
                    FocusLogPage();
                    continue;
                }

                OpenFile(Path.GetFullPath(arg));
            }
        }

        private void OpenPackageProtocolPath(string file)
        {
            // Every ".vpk:" separates a package from the path inside it, so nested packages
            // can be addressed as "outer_dir.vpk:maps/inner.vpk:models/file.vmdl_c"
            var packagePaths = new List<string>();
            var innerFile = file;
            int separator;

            while ((separator = innerFile.IndexOf(".vpk:", StringComparison.OrdinalIgnoreCase)) != -1)
            {
                packagePaths.Add(innerFile[..(separator + 4)]);
                innerFile = innerFile[(separator + 5)..];
            }

            if (packagePaths.Count == 0)
            {
                Log.Error(nameof(MainWindow), $"For vpk: protocol to work, specify a file path inside of the package, for example: \"vpk:C:/path/pak01_dir.vpk:inner/file.vmdl_c\"");

                OpenFile(file);
                return;
            }

            file = packagePaths[0];

            if (!File.Exists(file))
            {
                var dirFile = string.Concat(file.AsSpan(0, file.Length - 4), "_dir.vpk");

                if (!File.Exists(dirFile))
                {
                    Log.Error(nameof(MainWindow), $"File '{file}' does not exist.");
                    FocusLogPage();
                    return;
                }

                file = dirFile;
            }

            file = Path.GetFullPath(file);
            Log.Info(nameof(MainWindow), $"Opening {file}");

            VrfGuiContext? packageContext = null;

            try
            {
                var package = new Package();
                try
                {
                    package.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);
                    package.Read(file);
                    packageContext = new VrfGuiContext(file, null)
                    {
                        CurrentPackage = package
                    };
                    package = null;
                }
                finally
                {
                    package?.Dispose();
                }

                for (var depth = 1; depth < packagePaths.Count; depth++)
                {
                    var nestedPath = packagePaths[depth];
                    var nestedEntry = packageContext.CurrentPackage!.FindEntry(nestedPath);

                    if (nestedEntry == null)
                    {
                        Log.Error(nameof(MainWindow), $"File '{nestedPath}' does not exist in package '{packageContext.FileName}'.");
                        FocusLogPage();
                        return;
                    }

                    var nestedPackage = new Package();
                    try
                    {
                        nestedPackage.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);
                        nestedPackage.SetFileName(nestedPath);
                        nestedPackage.Read(ValveResourceFormat.IO.GameFileLoader.GetPackageEntryStream(packageContext.CurrentPackage!, nestedEntry));
                        packageContext = new VrfGuiContext(nestedPath, packageContext)
                        {
                            CurrentPackage = nestedPackage
                        };
                        nestedPackage = null;
                    }
                    finally
                    {
                        nestedPackage?.Dispose();
                    }
                }

                var packageFile = packageContext.CurrentPackage!.FindEntry(innerFile)
                    ?? packageContext.CurrentPackage.FindEntry(innerFile + ValveResourceFormat.IO.GameFileLoader.CompiledFileSuffix);

                if (packageFile == null)
                {
                    Log.Error(nameof(MainWindow), $"File '{innerFile}' does not exist in package '{packageContext.FileName}'.");
                    FocusLogPage();
                    return;
                }

                innerFile = packageFile.GetFullPath();

                Log.Info(nameof(MainWindow), $"Opening {innerFile}");

                var fileContext = new VrfGuiContext(innerFile, packageContext);

                try
                {
                    OpenFile(fileContext, packageFile);
                    fileContext = null;
                }
                finally
                {
                    fileContext?.Dispose();
                }
            }
            finally
            {
                // Contexts still referenced by an opened tab are only marked here and dispose when the tab closes
                for (var context = packageContext; context != null; context = context.ParentGuiContext)
                {
                    context.Dispose();
                }
            }
        }

        private void OnClosing(object? sender, WindowClosingEventArgs e)
        {
            SaveWindowPlacement();
            CloseAllTabs();
        }

        private void RestoreWindowPlacement()
        {
            var config = Settings.Config;

            Width = config.WindowWidth > 0 ? config.WindowWidth : 1280;
            Height = config.WindowHeight > 0 ? config.WindowHeight : 800;

            if (config.WindowWidth > 0 && config.WindowHeight > 0)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Position = new PixelPoint(config.WindowLeft, config.WindowTop);
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            // WinForms FormWindowState.Maximized is 2
            if (config.WindowState == 2)
            {
                WindowState = WindowState.Maximized;
            }
        }

        private void SaveWindowPlacement()
        {
            var config = Settings.Config;

            config.WindowState = WindowState == WindowState.Maximized ? 2 : 0;

            if (WindowState == WindowState.Normal)
            {
                config.WindowLeft = Position.X;
                config.WindowTop = Position.Y;
                config.WindowWidth = (int)Width;
                config.WindowHeight = (int)Height;
            }

            Settings.Save();
        }

        #region Tabs

        private void OnMainSelectedTabChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(e.Source, mainTabs))
            {
                return;
            }

            pageOverlay.IsVisible = d2pfxTab != null && SelectedTab == d2pfxTab;
            ShowSelectedTabStatus();
        }

        /// <summary>Keeps <see cref="pageOverlay"/> exactly over the page of the selected tab.</summary>
        private void UpdatePageOverlayBounds()
        {
            if (pageHost?.TranslatePoint(default, mainTabs) is not { } origin)
            {
                return;
            }

            var margin = new Thickness(origin.X, origin.Y, 0, 0);
            var size = pageHost.Bounds.Size;

            if (pageOverlay.Margin != margin)
            {
                pageOverlay.Margin = margin;
            }

            if (pageOverlay.Width != size.Width || pageOverlay.Height != size.Height)
            {
                pageOverlay.Width = size.Width;
                pageOverlay.Height = size.Height;
            }
        }

        private void UpdateWindowTitle(string? toolTipText)
        {
            windowTitle = string.IsNullOrEmpty(toolTipText) ? AppTitle : $"{AppTitle} - {toolTipText}";
            Title = windowTitle;
            bottomPanel?.Text = windowTitle;
        }

        /// <summary>Shows the title and keybindings of the selected tab, after a package preview stops being shown.</summary>
        public void ShowSelectedTabStatus()
        {
            UpdateWindowTitle(SelectedTab?.ToolTipText);
            bottomPanel?.UpdateKeybindings(KeybindingRegistry.GetKeybindingsForViewer(KeybindingRegistry.GetViewerTypeFromTab(SelectedTab)));
        }

        /// <summary>Shows the title and keybindings of a file previewed inside a package.</summary>
        public void ShowPreviewStatus(DocumentTab previewTab)
        {
            UpdateWindowTitle(previewTab.ToolTipText);
            bottomPanel.UpdateKeybindings(KeybindingRegistry.GetKeybindingsForViewer(KeybindingRegistry.GetViewerTypeFromTab(previewTab)));
        }

        /// <summary>Shows a status message in place of the window title until the selected tab changes.</summary>
        public void SetStatus(string text) => bottomPanel.Text = $"{AppTitle} - {text}";

        public void FocusLogPage() => mainTabs.SelectedItem = consoleTab;

        /// <summary>Adds a tab after the selected one and selects it.</summary>
        public void AddTab(DocumentTab tab)
        {
            tab.CloseRequested += (_, _) => CloseTab(tab);

            var index = Math.Clamp(mainTabs.SelectedIndex + 1, 1, mainTabs.ItemCount);
            mainTabs.Items.Insert(index, tab);
            mainTabs.SelectedItem = tab;
        }

        /// <summary>Selects the tab with the given title if it is open.</summary>
        private bool OpenTab(string text)
        {
            var tab = mainTabs.Items.OfType<DocumentTab>().FirstOrDefault(t => t.Text == text);

            if (tab == null)
            {
                return false;
            }

            mainTabs.SelectedItem = tab;
            return true;
        }

        private void InsertSpecialTab(DocumentTab tab, int index)
        {
            tab.CloseRequested += (_, _) => CloseTab(tab);
            mainTabs.Items.Insert(Math.Min(index, mainTabs.ItemCount), tab);
            mainTabs.SelectedItem = tab;
        }

        /// <summary>
        /// The one explorer, shared by the Explorer and Welcome tabs. It is taken out of wherever it is shown,
        /// since a control can only have one parent.
        /// </summary>
        private ExplorerControl EnsureExplorerControl()
        {
            explorerControl ??= new ExplorerControl();

            switch (explorerControl.Parent)
            {
                case Panel panel:
                    panel.Children.Remove(explorerControl);
                    break;
                case ContentControl contentControl:
                    contentControl.Content = null;
                    break;
            }

            return explorerControl;
        }

        private void OpenExplorer()
        {
            if (OpenTab("Explorer"))
            {
                return;
            }

            InsertSpecialTab(new DocumentTab("Explorer", "Explorer") { ToolTipText = "Explorer", Content = EnsureExplorerControl() }, 1);
        }

        private void OpenWelcome()
        {
            if (OpenTab("Welcome"))
            {
                return;
            }

            InsertSpecialTab(new DocumentTab("Welcome", "WelcomeScreen") { ToolTipText = "Welcome", Content = new WelcomeControl(EnsureExplorerControl()) }, mainTabs.ItemCount);
        }

        private void OpenSettings()
        {
            if (OpenTab("Settings"))
            {
                return;
            }

            InsertSpecialTab(new DocumentTab("Settings", "Settings") { ToolTipText = "Settings", Content = new SettingsControl() }, 1);
        }

        /// <summary>Opens the dota2prnfx site in a tab, or selects that tab if it is already open.</summary>
        public void ShowD2PfxTab()
        {
            if (d2pfxTab != null)
            {
                mainTabs.SelectedItem = d2pfxTab;
                return;
            }

            // The page itself is shown by the overlay, the tab only holds its place
            var tab = new DocumentTab("dota2prnfx", "dota2pornfx") { Content = new Border() };

            // The icon is a picture that fills its whole square, shrink it to sit like the other icons
            tab.SetIconScale(0.72);

            pageOverlay.Child = new D2PfxBrowserControl();
            d2pfxTab = tab;

            InsertSpecialTab(tab, mainTabs.ItemCount);
        }

        /// <summary>Opens an empty package that folders and files can be added to, then saved as a VPK.</summary>
        private void CreateVpkFromFolder()
        {
            var context = new VrfGuiContext("new.vpk", null);

            try
            {
#pragma warning disable CA2000 // Ownership is transferred to the tab, which disposes its contents
                var viewer = new Types.PackageViewer.PackageViewer(context);
                var contents = new OwningDecorator(viewer, context) { Child = viewer.CreateEmpty() };
                context = null;

                InsertSpecialTab(new DocumentTab("New VPK", "AssetTypes.vpk") { ToolTipText = "New VPK", Content = contents }, mainTabs.ItemCount);
#pragma warning restore CA2000
            }
            finally
            {
                context?.Dispose();
            }
        }

        private void CloseTab(DocumentTab? tab)
        {
            if (tab == null || !tab.Closable)
            {
                return;
            }

            var index = mainTabs.Items.IndexOf(tab);
            var wasSelected = mainTabs.SelectedItem == tab;

            if (tab == d2pfxTab)
            {
                d2pfxTab = null;
                pageOverlay.IsVisible = false;

                var browser = pageOverlay.Child as D2PfxBrowserControl;
                pageOverlay.Child = null;
                browser?.Dispose();
            }

            // The explorer is kept alive and reused when it is opened again
            if (explorerControl != null && (tab.Content == explorerControl || tab.Content is WelcomeControl))
            {
                EnsureExplorerControl();
                tab.Content = null;
            }

            mainTabs.Items.Remove(tab);

            if (wasSelected && index > 0)
            {
                mainTabs.SelectedIndex = Math.Min(index, mainTabs.ItemCount) - 1;
            }

            tab.Dispose();
        }

        private List<DocumentTab> Tabs => [.. mainTabs.Items.OfType<DocumentTab>()];

        private void CloseAllTabs()
        {
            mainTabs.SelectedItem = consoleTab;

            foreach (var tab in Tabs)
            {
                CloseTab(tab);
            }
        }

        private void CloseTabsToLeft(DocumentTab? basePage)
        {
            if (basePage == null)
            {
                return;
            }

            foreach (var tab in Tabs.TakeWhile(tab => tab != basePage))
            {
                CloseTab(tab);
            }
        }

        private void CloseTabsToRight(DocumentTab? basePage)
        {
            if (basePage == null)
            {
                return;
            }

            foreach (var tab in Tabs.SkipWhile(tab => tab != basePage).Skip(1))
            {
                CloseTab(tab);
            }
        }

        private void CloseAndReOpenActiveTab()
        {
            if (SelectedTab is not { ExportData: { } exportData } tab)
            {
                return;
            }

            var (newFileContext, packageEntry) = exportData.VrfGuiContext.FindFileWithContext(
                exportData.PackageEntry?.GetFullPath() ?? exportData.VrfGuiContext.FileName);

            if (newFileContext != null)
            {
                OpenFile(newFileContext, packageEntry);
                CloseTab(tab);
            }
        }

        private void OnTabContextRequested(object? sender, ContextRequestedEventArgs e)
        {
            // Page contents are not inside the tab header visually, so this only finds clicks on main tab headers
            if (e.Source is not Visual source
                || source.FindAncestorOfType<DocumentTab>(includeSelf: true) is not { } tab
                || tab.Parent != mainTabs)
            {
                return;
            }

            e.Handled = true;

            var tabIndex = mainTabs.Items.IndexOf(tab);
            var menu = new ContextMenu();

            void Item(string header, string icon, Action onClick, KeyGesture? gesture = null)
            {
                var item = new MenuItem { Header = header, Icon = AppIcons.Create(icon), InputGesture = gesture };
                item.Click += (_, _) => onClick();
                menu.Items.Add(item);
            }

            if (tabIndex != 0)
            {
                Item("Close _tab", "CloseTab", () => CloseTab(tab), new KeyGesture(Key.W, KeyModifiers.Control));
            }

            Item("Close _all tabs", "CloseAllTabs", CloseAllTabs, new KeyGesture(Key.Q, KeyModifiers.Control));

            if (tabIndex != mainTabs.ItemCount - 1)
            {
                Item("Close all tabs to _right", "CloseAllTabsRight", () => CloseTabsToRight(tab), new KeyGesture(Key.E, KeyModifiers.Control));
            }

            if (tabIndex > 1)
            {
                Item("Close all tabs to _left", "CloseAllTabsLeft", () => CloseTabsToLeft(tab));
            }

            // Only tabs that got a sound player (world and model viewers) have anything to mute
            if (tab.ExportData?.DisposableContents is Resource { GLViewer: GLSceneViewer { HasSoundPlayer: true } sceneViewer })
            {
                Item(sceneViewer.Muted ? "_Unmute tab" : "_Mute tab", "AudioVolume", () => sceneViewer.Muted = !sceneViewer.Muted);
            }

            if (tab.ExportData is { } exportData)
            {
                menu.Items.Add(new Separator());
                Item("Export as is", "Export", () => _ = ExportTabAsync(exportData, decompile: false));
                Item("Decompile & export", "Decompile", () => _ = ExportTabAsync(exportData, decompile: true));
            }
            else if (tab == consoleTab)
            {
                menu.Items.Add(new Separator());
                Item("Clear console", "ClearLog", Log.ClearConsole);
            }

            menu.Open(tab);
        }

        private static async Task ExportTabAsync(ExportData exportData, bool decompile)
        {
            if (exportData.PackageEntry != null)
            {
                await ExportFile.ExtractFileFromPackageEntry(exportData.PackageEntry, exportData.VrfGuiContext, decompile).ConfigureAwait(true);
                return;
            }

            // ExtractFileFromStream disposes the stream when done
            var fileStream = File.OpenRead(exportData.VrfGuiContext.FileName);
            await ExportFile.ExtractFileFromStream(Path.GetFileName(exportData.VrfGuiContext.FileName), fileStream, exportData.VrfGuiContext, decompile).ConfigureAwait(true);
        }

        #endregion

        #region Find

        private async Task FindAsync()
        {
            if (SelectedTab is not { } tab)
            {
                return;
            }

            if (FindVisibleTextEditor(tab) is { } editor)
            {
                editor.SearchPanel.Open();
                return;
            }

            if (tab.ExportData?.DisposableContents is Types.PackageViewer.PackageViewer packageViewer)
            {
                await packageViewer.ShowSearchAsync().ConfigureAwait(true);
                return;
            }

            if (tab.Content == explorerControl)
            {
                explorerControl?.FocusFilter();
            }
        }

        private static TextEditor? FindVisibleTextEditor(Control container)
        {
            return container.GetVisualDescendants()
                .OfType<TextEditor>()
                .FirstOrDefault(static editor => editor.IsEffectivelyVisible);
        }

        #endregion

        #region Opening files

        public async Task OpenFilesFromDialogAsync()
        {
            var files = await AppFileDialogs.OpenFilesAsync(null, OpenFileFilter).ConfigureAwait(true);

            if (files == null)
            {
                return;
            }

            foreach (var file in files)
            {
                OpenFile(file);
            }
        }

        public void OpenFile(string fileName)
        {
            Log.Info(nameof(MainWindow), $"Opening {fileName}");

            if (Regexes.VpkNumberArchive().IsMatch(fileName))
            {
                var fixedPackage = $"{fileName[..^8]}_dir.vpk";

                if (File.Exists(fixedPackage))
                {
                    Log.Warn(nameof(MainWindow), $"You opened \"{Path.GetFileName(fileName)}\" but there is \"{Path.GetFileName(fixedPackage)}\"");
                    fileName = fixedPackage;
                }
            }

            var vrfGuiContext = new VrfGuiContext(fileName, null);
            OpenFile(vrfGuiContext, null);

            Settings.TrackRecentFile(fileName);
        }

        /// <summary>
        /// Loads a file into a new tab, or into <paramref name="previewHost"/> when previewing from a package.
        /// </summary>
        public void OpenFile(VrfGuiContext vrfGuiContext, PackageEntry? file, IPreviewHost? previewHost = null, bool withoutViewer = false)
        {
            var isPreview = previewHost != null;

            var viewMode = (isPreview, withoutViewer) switch
            {
                (true, _) => ResourceViewMode.ViewerOnly,
                (_, true) => ResourceViewMode.ResourceBlocksOnly,
                (_, _) => ResourceViewMode.Default,
            };

#pragma warning disable CA2000 // Ownership is transferred to the tab control or preview host, which dispose it
            var tab = new DocumentTab(Path.GetFileName(vrfGuiContext.FileName), AppIcons.GetFileIconName(vrfGuiContext.FileName), closable: !isPreview)
            {
                ToolTipText = vrfGuiContext.FileName,
                ExportData = new ExportData
                {
                    PackageEntry = file,
                    VrfGuiContext = vrfGuiContext,
                },
            };
#pragma warning restore CA2000

            for (var parentContext = vrfGuiContext.ParentGuiContext; parentContext != null; parentContext = parentContext.ParentGuiContext)
            {
                tab.ToolTipText = $"{tab.ToolTipText} \u2190 {parentContext.FileName}";
            }

            // Packages of installed games get the game's icon
            if (vrfGuiContext.FileName.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase))
            {
                var game = ExplorerControl.SteamGames.FirstOrDefault(game => vrfGuiContext.FileName.StartsWith(game.GamePath, StringComparison.OrdinalIgnoreCase));

                if (game != null && AppIcons.GameIcons.TryGetValue(game.AppID, out var gameIcon))
                {
                    tab.SetIconImage(gameIcon);
                }
            }

            var loadingFile = new LoadingFile(vrfGuiContext.FileName);
            tab.Content = loadingFile;
            vrfGuiContext.LoadingProgress = new Progress<string>(loadingFile.SetStatus);

            if (isPreview)
            {
                previewHost!.ShowPreview(tab);
            }
            else
            {
                AddTab(tab);
            }

            _ = LoadIntoTabAsync(tab, vrfGuiContext, file, viewMode);
        }

        private async Task LoadIntoTabAsync(DocumentTab tab, VrfGuiContext vrfGuiContext, PackageEntry? file, ResourceViewMode viewMode)
        {
            IViewer viewer;

            try
            {
                viewer = await Task.Run(() => ViewerFactory.CreateAndLoadAsync(vrfGuiContext, file, viewMode)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                vrfGuiContext.GLPostLoadAction = null;
                vrfGuiContext.LoadingProgress = null;

                Log.Error(nameof(MainWindow), ex.ToString());

                if (!tab.IsDisposed)
                {
                    tab.Content = CodeTextBox.CreateFromException(ex, tab.ToolTipText);
                }

                ShowUpdateAfterError();
                return;
            }

            vrfGuiContext.LoadingProgress = null;

            if (tab.IsDisposed)
            {
                viewer.Dispose();
                return; // closed tab before it loaded
            }

            Debug.Assert(tab.ExportData != null);
            tab.ExportData.DisposableContents = viewer;

            try
            {
                tab.Content = viewer.Create();
            }
            catch (Exception ex)
            {
                Log.Error(nameof(MainWindow), ex.ToString());
                tab.Content = CodeTextBox.CreateFromException(ex, tab.ToolTipText);
            }
            finally
            {
                vrfGuiContext.GLPostLoadAction = null;
            }

            viewer.NotifyVisible();

            if (mainTabs.SelectedItem == tab)
            {
                ShowSelectedTabStatus();
            }
        }

        private void OnDragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnDrop(object? sender, DragEventArgs e)
        {
            if (e.DataTransfer.TryGetFiles() is not { } files)
            {
                return;
            }

            foreach (var item in files)
            {
                if (item.TryGetLocalPath() is { } path && File.Exists(path))
                {
                    OpenFile(path);
                }
            }
        }

        #endregion

#if DEBUG
        private static void ValidateShaders()
        {
            using var progressDialog = new GenericProgressForm
            {
                Text = "Compiling shaders\u2026",
            };
            progressDialog.OnProcess = _ =>
            {
                var window = NativeWindowFactory.Create(new()
                {
                    APIVersion = ValveResourceFormat.Renderer.GLEnvironment.RequiredVersion,
                    Flags = GLBaseControl.Flags | OpenTK.Windowing.Common.ContextFlags.Offscreen,
                    StartVisible = false,
                    Title = "Source 2 Viewer Shader Validator",
                });

                try
                {
                    window.MakeCurrent();
                    ValveResourceFormat.Renderer.Shaders.ShaderLoader.ValidateShaders(new Progress<string>(progressDialog.SetProgress), VrfGuiContext.Logger);
                }
                finally
                {
                    NativeWindowFactory.Destroy(window);
                }

                return Task.CompletedTask;
            };
            progressDialog.ShowDialog();
        }
#endif

#pragma warning disable CA1822 // Shared code calls these on Program.MainForm like on the WinForms form
        /// <summary>Runs <paramref name="action"/> on the UI thread and waits for it, like WinForms Control.Invoke.</summary>
        public void Invoke(Action action) => Dispatcher.UIThread.Invoke(action);

        /// <summary>Queues <paramref name="action"/> on the UI thread, like WinForms Control.BeginInvoke.</summary>
        public void BeginInvoke(Action action) => Dispatcher.UIThread.Post(action);
#pragma warning restore CA1822
    }
}
