using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GUI.Controls;
using GUI.Types.Exporter;
using GUI.Types.GLViewers;
using GUI.Types.Viewers;
using GUI.Utils;
using ValvePak;
using ValveResourceFormat.TextureDecoders;

namespace GUI
{
    sealed class MainWindow : Window
    {
        private const string OpenFileFilter = "Valve Resource Format (*.*_c, *.vpk)|*.*_c;*.vpk;*.vcs|All files (*.*)|*.*";

        private readonly TabControl mainTabs;
        private readonly MenuItem recentFilesMenu;
        private readonly TextBlock statusText;
        private readonly ConsoleView consoleView;
        private readonly TabItem consoleTab;
        private readonly TabItem explorerTab;

        public MainWindow()
        {
            Title = "Source 2 Viewer";
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Source2Viewer.Avalonia/Assets/source2viewer.ico")));
            MinWidth = 640;
            MinHeight = 400;

            RestoreWindowPlacement();

            var console = new ConsoleTab();
            Log.SetConsoleTab(console);
            consoleView = new ConsoleView(console);

            mainTabs = ViewerContentPresenter.CreateTabControl();
            mainTabs.SelectionChanged += OnMainSelectedTabChanged;

            explorerTab = new TabItem { Header = "Explorer", Content = new ExplorerControl() };
            consoleTab = new TabItem { Header = "Console", Content = consoleView };
            mainTabs.Items.Add(explorerTab);
            mainTabs.Items.Add(consoleTab);

            recentFilesMenu = new MenuItem { Header = "Open _Recent" };
            recentFilesMenu.SubmenuOpened += (_, _) => PopulateRecentFiles();
            recentFilesMenu.Items.Add(new MenuItem { Header = "(none)", IsEnabled = false });

            statusText = new TextBlock
            {
                Margin = new(8, 2),
                Opacity = 0.75,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Text = $"Source 2 Viewer {Program.DisplayVersion}",
            };

            var root = new DockPanel();
            var menu = CreateMenu();
            DockPanel.SetDock(menu, Dock.Top);
            DockPanel.SetDock(statusText, Dock.Bottom);
            root.Children.Add(menu);
            root.Children.Add(statusText);
            root.Children.Add(mainTabs);
            Content = root;

            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DragOverEvent, OnDragOver);
            AddHandler(DragDrop.DropEvent, OnDrop);

            Opened += OnOpened;
            Closing += OnClosing;
            Activated += (_, _) => RenderLoopThread.SetWindowActive(this, true);
            Deactivated += (_, _) => RenderLoopThread.SetWindowActive(this, false);

            Log.Info(nameof(MainWindow), $"Source 2 Viewer {Program.DisplayVersion} on {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
        }

        private Menu CreateMenu()
        {
            var openItem = new MenuItem { Header = "_Open...", InputGesture = new KeyGesture(Key.O, KeyModifiers.Control) };
            openItem.Click += async (_, _) => await OpenFilesFromDialogAsync().ConfigureAwait(true);

            var closeTabItem = new MenuItem { Header = "_Close tab", InputGesture = new KeyGesture(Key.W, KeyModifiers.Control) };
            closeTabItem.Click += (_, _) => CloseTab(mainTabs.SelectedItem as DocumentTab);

            var closeAllItem = new MenuItem { Header = "Close _all tabs" };
            closeAllItem.Click += (_, _) => CloseAllTabs();

            var exitItem = new MenuItem { Header = "E_xit" };
            exitItem.Click += (_, _) => Close();

            var consoleItem = new MenuItem { Header = "_Console" };
            consoleItem.Click += (_, _) => FocusLogPage();

            var explorerItem = new MenuItem { Header = "_Explorer" };
            explorerItem.Click += (_, _) => mainTabs.SelectedItem = explorerTab;

            var themeItem = new MenuItem { Header = "_Theme" };

            foreach (var theme in Enum.GetValues<Themer.AppTheme>())
            {
                var item = new MenuItem { Header = theme.ToString() };
                item.Click += (_, _) =>
                {
                    Settings.Config.Theme = (int)theme;
                    Settings.Save();
                    Themer.ApplyTheme(theme);
                };
                themeItem.Items.Add(item);
            }

            var settingsItem = new MenuItem { Header = "_Settings..." };
            settingsItem.Click += async (_, _) => await new SettingsWindow().ShowDialog(this).ConfigureAwait(true);

            var aboutItem = new MenuItem { Header = "_About" };
            aboutItem.Click += async (_, _) => await AppMessageDialogs.ShowMessageAsync(
                $"Source 2 Viewer {Program.DisplayVersion}\n\nCross-platform viewer for Source 2 resources.\nhttps://s2v.app/",
                "About Source 2 Viewer").ConfigureAwait(true);

            KeyBindings.Add(new KeyBinding { Gesture = openItem.InputGesture!, Command = new ActionCommand(async () => await OpenFilesFromDialogAsync().ConfigureAwait(true)) });
            KeyBindings.Add(new KeyBinding { Gesture = closeTabItem.InputGesture!, Command = new ActionCommand(() => CloseTab(mainTabs.SelectedItem as DocumentTab)) });

            return new Menu
            {
                Items =
                {
                    new MenuItem
                    {
                        Header = "_File",
                        Items = { openItem, recentFilesMenu, new Separator(), closeTabItem, closeAllItem, new Separator(), exitItem },
                    },
                    new MenuItem
                    {
                        Header = "_View",
                        Items = { explorerItem, consoleItem, themeItem },
                    },
                    new MenuItem
                    {
                        Header = "_Tools",
                        Items = { settingsItem },
                    },
                    new MenuItem
                    {
                        Header = "_Help",
                        Items = { aboutItem },
                    },
                },
            };
        }

        private void PopulateRecentFiles()
        {
            recentFilesMenu.Items.Clear();

            var recent = Settings.Config.RecentFiles;

            if (recent.Count == 0)
            {
                recentFilesMenu.Items.Add(new MenuItem { Header = "(none)", IsEnabled = false });
                return;
            }

            // Most recent last in settings, shown first in the menu
            for (var i = recent.Count - 1; i >= 0; i--)
            {
                var path = recent[i];
                var item = new MenuItem { Header = path.Replace("_", "__", StringComparison.Ordinal) };
                item.Click += (_, _) => OpenFile(path);
                recentFilesMenu.Items.Add(item);
            }

            recentFilesMenu.Items.Add(new Separator());

            var clear = new MenuItem { Header = "Clear recent files" };
            clear.Click += (_, _) => Settings.ClearRecentFiles();
            recentFilesMenu.Items.Add(clear);
        }

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

            OpenCommandLineArgFiles(Program.StartupFiles);
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

        private void OnMainSelectedTabChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(e.Source, mainTabs))
            {
                return;
            }

            var tooltip = (mainTabs.SelectedItem as DocumentTab)?.ToolTipText;
            Title = string.IsNullOrEmpty(tooltip) ? "Source 2 Viewer" : $"Source 2 Viewer - {tooltip}";
        }

        public void SetStatus(string text) => statusText.Text = text;

        public void FocusLogPage() => mainTabs.SelectedItem = consoleTab;

#pragma warning disable CA1822 // Shared code calls these on Program.MainForm like on the WinForms form
        /// <summary>Runs <paramref name="action"/> on the UI thread and waits for it, like WinForms Control.Invoke.</summary>
        public void Invoke(Action action) => Dispatcher.UIThread.Invoke(action);

        /// <summary>Queues <paramref name="action"/> on the UI thread, like WinForms Control.BeginInvoke.</summary>
        public void BeginInvoke(Action action) => Dispatcher.UIThread.Post(action);
#pragma warning restore CA1822

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
            var tab = new DocumentTab(Path.GetFileName(vrfGuiContext.FileName), closable: !isPreview)
            {
                ToolTipText = vrfGuiContext.FileName,
                ExportData = new ExportData
                {
                    PackageEntry = file,
                    VrfGuiContext = vrfGuiContext,
                },
            };

#pragma warning restore CA2000

            var parentContext = vrfGuiContext.ParentGuiContext;

            while (parentContext != null)
            {
                tab.ToolTipText = $"{tab.ToolTipText} ← {parentContext.FileName}";
                parentContext = parentContext.ParentGuiContext;
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
                tab.CloseRequested += (_, _) => CloseTab(tab);

                var index = Math.Min(mainTabs.SelectedIndex + 1, mainTabs.ItemCount);
                mainTabs.Items.Insert(Math.Max(index, 0), tab);
                mainTabs.SelectedItem = tab;
            }

            _ = LoadIntoTabAsync(tab, vrfGuiContext, file, viewMode);
        }

        private static async Task LoadIntoTabAsync(DocumentTab tab, VrfGuiContext vrfGuiContext, PackageEntry? file, ResourceViewMode viewMode)
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
        }

        private void CloseTab(DocumentTab? tab)
        {
            if (tab == null)
            {
                return;
            }

            var index = mainTabs.Items.IndexOf(tab);
            mainTabs.Items.Remove(tab);

            if (index > 0 && mainTabs.SelectedItem == null)
            {
                mainTabs.SelectedIndex = Math.Min(index, mainTabs.ItemCount) - 1;
            }

            tab.Dispose();
        }

        private void CloseAllTabs()
        {
            foreach (var tab in mainTabs.Items.OfType<DocumentTab>().ToList())
            {
                CloseTab(tab);
            }
        }

        private void OnDragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnDrop(object? sender, DragEventArgs e)
        {
            var files = e.DataTransfer.TryGetFiles();

            if (files == null)
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
    }
}
