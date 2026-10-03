using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using GUI.Utils;

namespace GUI.Controls
{
    /// <summary>
    /// The Settings tab, laid out like the WinForms SettingsControl: full width group boxes one under another, with
    /// their controls where the WinForms designer puts them. Changes are saved when the tab is left.
    /// </summary>
    sealed class SettingsControl : ScrollViewer
    {
        private static readonly int[] AntiAliasingSampleOptions = [0, 2, 4, 8, 16];
        private static readonly int[] ShadowQualityResolutions = [512, 1024, 2048, 4096];
        private static readonly string[] ShadowQualityNames = ["Low", "Medium", "High", "Very High"];

        // WinForms places group box contents from the top of the box, a little above the frame's line, where the
        // group boxes here start their contents below the title
        private const double GroupTitleOffset = 15;

        private readonly ListBox gamePaths;

        // Without this the ScrollViewer theme is not applied, so the page has no template and can not scroll
        protected override Type StyleKeyOverride => typeof(ScrollViewer);

        public SettingsControl()
        {
            Classes.Add("page");
            Classes.Add("settings");

            var config = Settings.Config;

            // Game content search paths
            gamePaths = new ListBox { Height = 123, ItemsSource = config.GameSearchPaths.ToList() };
            gamePaths.Classes.Add("settingsList");

            var gamePathsAdd = new Button { Content = "Add .vpk or gameinfo.gi", Width = 212, Height = 30, Margin = new(0, 9, 8, 9) };
            gamePathsAdd.Click += async (_, _) =>
            {
                var files = await AppFileDialogs.OpenFilesAsync(null, "Valve Pak (*.vpk) or gameinfo.gi|*.vpk;gameinfo.gi|All files (*.*)|*.*", multiselect: false, updateRemembered: false).ConfigureAwait(true);

                if (files is not { Length: > 0 })
                {
                    return;
                }

                var fileName = files[0];

                if (Regexes.VpkNumberArchive().IsMatch(fileName))
                {
                    fileName = $"{fileName[..^8]}_dir.vpk";
                }

                if (Path.GetDirectoryName(fileName) is { Length: > 0 } directory)
                {
                    config.OpenDirectory = directory;
                }

                AddGamePath(fileName);
            };

            var gamePathsAddFolder = new Button { Content = "Add folder", Width = 88, Height = 30, Margin = new(8, 9, 8, 9) };
            gamePathsAddFolder.Click += async (_, _) =>
            {
                var folder = await AppFileDialogs.PickFolderAsync(null, AppFileDialogs.RememberIn.OpenDirectory, updateRemembered: false).ConfigureAwait(true);

                if (folder != null)
                {
                    config.OpenDirectory = folder;
                    AddGamePath(folder);
                }
            };

            var gamePathsRemove = new Button { Content = "Remove", Width = 88, Height = 30, Margin = new(8, 9, 0, 9) };
            gamePathsRemove.Click += (_, _) =>
            {
                if (gamePaths.SelectedItem is string path)
                {
                    config.GameSearchPaths.Remove(path);
                    gamePaths.ItemsSource = config.GameSearchPaths.ToList();
                }
            };

            var gamePathsButtons = new DockPanel();
            DockPanel.SetDock(gamePathsAdd, Dock.Left);
            DockPanel.SetDock(gamePathsAddFolder, Dock.Left);
            DockPanel.SetDock(gamePathsRemove, Dock.Right);
            gamePathsButtons.Children.Add(gamePathsAdd);
            gamePathsButtons.Children.Add(gamePathsAddFolder);
            gamePathsButtons.Children.Add(gamePathsRemove);
            gamePathsButtons.Children.Add(new Panel());

            var gamePathsGroup = GroupBox.Create("Game content search paths", new StackPanel
            {
                Margin = new(16, 36 - GroupTitleOffset, 16, 0),
                Height = 243 - 36 - 2,
                Children = { gamePaths, gamePathsButtons },
            }, new Avalonia.Thickness(0));

            // Video settings
            var antiAliasingIndex = Array.FindLastIndex(AntiAliasingSampleOptions, s => config.AntiAliasingSamples >= s);
            var antiAliasing = Combo(AntiAliasingSampleOptions.Select(static s => $"{s}x").ToArray(), antiAliasingIndex, i => config.AntiAliasingSamples = AntiAliasingSampleOptions[i]);

            var fov = Numeric((decimal)config.FieldOfView, 1, 170, 1, v => config.FieldOfView = (float)v, "0.######");
            var viewmodelFov = Numeric((decimal)config.ViewmodelFieldOfView, 50, 70, 1, v => config.ViewmodelFieldOfView = (float)v, "0.######");
            var maxTextureSize = Numeric(config.MaxTextureSize, 16, 10240, 64, v => config.MaxTextureSize = (int)v);

            var sensitivityValue = new TextBlock();
            var sensitivity = Slider(config.MouseSensitivity * 10, 80, tickFrequency: 5, height: 32, sensitivityValue, v =>
            {
                config.MouseSensitivity = (float)(v / 10);
                return config.MouseSensitivity.ToString("0.0", CultureInfo.InvariantCulture);
            });

            var shadowQualityIndex = Array.FindIndex(ShadowQualityResolutions, r => config.ShadowResolution <= r);
            var shadowQuality = Combo(ShadowQualityNames, shadowQualityIndex < 0 ? ShadowQualityResolutions.Length - 1 : shadowQualityIndex, i => config.ShadowResolution = ShadowQualityResolutions[i]);

            var videoGroup = CanvasGroup("Video settings", 442,
                (Label("Anti-aliasing:"), 15, 36),
                (antiAliasing, 170, 33),
                (Label("Camera FOV:"), 15, 83),
                (fov, 170, 81),
                (Label("Viewmodel FOV:"), 15, 134),
                (viewmodelFov, 170, 132),
                (Label("Max texture size:"), 15, 185),
                (maxTextureSize, 170, 183),
                (Label("Mouse sensitivity:"), 15, 224),
                (sensitivity, 170, 221),
                (sensitivityValue, 340, 227),
                (Label("Shadow quality:"), 15, 268),
                (shadowQuality, 170, 266),
                (Check("Vertical Sync", config.Vsync != 0, v => config.Vsync = v ? 1 : 0), 15, 301),
                (Check("Display FPS", config.DisplayFps != 0, v => config.DisplayFps = v ? 1 : 0), 15, 333),
                (Check("Smooth camera", config.SmoothCameraEnabled, v => config.SmoothCameraEnabled = v), 15, 401));

            // Quick file preview
            var quickPreviewFlags = (Settings.QuickPreviewFlags)config.QuickFilePreview;

            void SetQuickPreviewFlag(Settings.QuickPreviewFlags flag, bool enabled)
            {
                var flags = (Settings.QuickPreviewFlags)config.QuickFilePreview;
                config.QuickFilePreview = (int)(enabled ? flags | flag : flags & ~flag);
            }

            var quickPreviewGroup = CanvasGroup("Quick file preview", 138,
                (Check("Preview files after selecting", (quickPreviewFlags & Settings.QuickPreviewFlags.Enabled) != 0, v => SetQuickPreviewFlag(Settings.QuickPreviewFlags.Enabled, v)), 16, 39),
                (Check("Auto play sounds", (quickPreviewFlags & Settings.QuickPreviewFlags.AutoPlaySounds) != 0, v => SetQuickPreviewFlag(Settings.QuickPreviewFlags.AutoPlaySounds, v)), 16, 78));

            // Audio
            var volumeValue = new TextBlock();
            var volume = Slider(MathF.Round(Math.Clamp(config.Volume, 0, 1) * 100), 100, tickFrequency: 0, height: 32, volumeValue, v =>
            {
                config.Volume = (float)(v / 100);
                return string.Create(CultureInfo.InvariantCulture, $"{(int)v}%");
            });

            var audioGroup = CanvasGroup("Audio", 99,
                (Label("Volume:"), 15, 46),
                (volume, 170, 42),
                (volumeValue, 340, 46));

            // Explorer
            var themes = Enum.GetValues<Themer.AppTheme>();
            var theme = Combo(themes.Select(static theme => theme.ToString()).ToArray(), Math.Clamp(config.Theme, 0, themes.Length - 1), i =>
            {
                config.Theme = i;
                Themer.ApplyTheme(themes[i]);
            });

            var explorerItems = new List<(Control, double, double)>
            {
                (Label("Theme:"), 15, 32),
                (theme, 209, 29),
                (Label("Text viewer font size:"), 15, 78),
                (Numeric(config.TextViewerFontSize, 8, 24, 1, v => config.TextViewerFontSize = (int)v), 209, 76),
                (Check("Open explorer on start", config.OpenExplorerOnStart != 0, v => config.OpenExplorerOnStart = v ? 1 : 0), 15, 124),
            };

            if (FileAssociation.IsSupported)
            {
                var registerAssociation = new Button { Content = "Register .vpk file association", Width = 208, Height = 30 };
                registerAssociation.Click += async (_, _) => await FileAssociation.RegisterAsync().ConfigureAwait(true);
                explorerItems.Add((registerAssociation, 15, 174));
            }

            var explorerGroup = CanvasGroup("Explorer", 243, [.. explorerItems]);

            var footer = new TextBlock
            {
                Text = "No regrets, Mr. Freeman",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            footer.Classes.Add("settingsFooter");

            Content = new StackPanel
            {
                Margin = new(16, 18, 16, 18),
                Children =
                {
                    gamePathsGroup,
                    videoGroup,
                    quickPreviewGroup,
                    audioGroup,
                    explorerGroup,
                    new Border { Height = 100, Child = footer },
                },
            };

            LostFocus += OnLostFocus;
            DetachedFromVisualTree += (_, _) => Settings.Save();
        }

        private void OnLostFocus(object? sender, RoutedEventArgs e)
        {
            if (!IsKeyboardFocusWithin)
            {
                Settings.Save();
            }
        }

        private void AddGamePath(string path)
        {
            if (Settings.Config.GameSearchPaths.Contains(path))
            {
                return;
            }

            Settings.Config.GameSearchPaths.Add(path);
            gamePaths.ItemsSource = Settings.Config.GameSearchPaths.ToList();
        }

        /// <summary>
        /// A group box of the given height in WinForms, with its controls where the WinForms designer puts them. Controls
        /// of the same row, which WinForms places a few pixels apart, are centered on one line so they sit straight.
        /// </summary>
        private static Grid CanvasGroup(string title, double height, params (Control Control, double X, double Y)[] items)
        {
            const double RowHeight = 34;
            const double SameRowDistance = 10;

            var canvas = new Canvas { Height = height - GroupTitleOffset - 2 };
            var rowTops = new List<double>();

            foreach (var (control, x, y) in items.OrderBy(static item => item.Y))
            {
                var rowTop = rowTops.Count > 0 && y - rowTops[^1] <= SameRowDistance ? rowTops[^1] : y;

                if (rowTops.Count == 0 || rowTops[^1] != rowTop)
                {
                    rowTops.Add(rowTop);
                }

                control.VerticalAlignment = VerticalAlignment.Center;

                var cell = new Panel { Height = Math.Max(RowHeight, double.IsNaN(control.Height) ? 0 : control.Height), Children = { control } };
                Canvas.SetLeft(cell, x);
                Canvas.SetTop(cell, rowTop - GroupTitleOffset - 6);
                canvas.Children.Add(cell);
            }

            return GroupBox.Create(title, canvas, new Avalonia.Thickness(0));
        }

        private static TextBlock Label(string text) => new() { Text = text };

        private static CheckBox Check(string label, bool current, Action<bool> set)
        {
            var check = new CheckBox { Content = label, IsChecked = current };
            check.Classes.Add("settings");
            check.IsCheckedChanged += (_, _) => set(check.IsChecked == true);
            return check;
        }

        private static ComboBox Combo(string[] items, int selectedIndex, Action<int> set)
        {
            var combo = new ComboBox
            {
                ItemsSource = items,
                SelectedIndex = selectedIndex,
                Width = 100,
                Height = 26,
            };
            combo.Classes.Add("settings");
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedIndex >= 0)
                {
                    set(combo.SelectedIndex);
                }
            };
            return combo;
        }

        /// <summary>A number typed into a box, like the WinForms ThemedNumeric, which has no spin buttons.</summary>
        private static NumericUpDown Numeric(decimal value, decimal min, decimal max, decimal increment, Action<decimal> set, string format = "0")
        {
            var numeric = new NumericUpDown
            {
                Value = Math.Clamp(value, min, max),
                Minimum = min,
                Maximum = max,
                Increment = increment,
                FormatString = format,
                ShowButtonSpinner = false,
                Width = 100,
                Height = 25,
            };
            numeric.Classes.Add("settings");
            numeric.ValueChanged += (_, e) =>
            {
                if (e.NewValue is { } newValue)
                {
                    set(newValue);
                }
            };
            return numeric;
        }

        private static Avalonia.Controls.Slider Slider(double value, double max, double tickFrequency, double height, TextBlock valueLabel, Func<double, string> set)
        {
            var slider = new Avalonia.Controls.Slider
            {
                Minimum = 0,
                Maximum = max,
                Value = value,
                IsSnapToTickEnabled = true,
                TickFrequency = tickFrequency > 0 ? tickFrequency : 1,
                SmallChange = 1,
                TickPlacement = tickFrequency > 0 ? TickPlacement.BottomRight : TickPlacement.None,
                Width = 160,
                Height = height,
            };

            // Ticks are drawn sparser than the steps the value snaps to
            if (tickFrequency > 0)
            {
                slider.IsSnapToTickEnabled = false;
                slider.ValueChanged += (_, _) => slider.Value = Math.Round(slider.Value);
            }

            valueLabel.Text = set(value);
            slider.ValueChanged += (_, e) => valueLabel.Text = set(Math.Round(e.NewValue));

            return slider;
        }
    }
}
