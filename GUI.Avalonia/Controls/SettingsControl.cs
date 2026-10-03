using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using GUI.Utils;

namespace GUI.Controls
{
    /// <summary>
    /// The Settings tab, grouped like the WinForms SettingsControl. Changes are saved when the tab is left.
    /// </summary>
    sealed class SettingsControl : ScrollViewer
    {
        private static readonly int[] AntiAliasingSampleOptions = [0, 2, 4, 8, 16];
        private static readonly int[] ShadowQualityResolutions = [512, 1024, 2048, 4096];
        private static readonly string[] ShadowQualityNames = ["Low", "Medium", "High", "Very High"];

        private readonly ListBox gamePaths;

        public SettingsControl()
        {
            Classes.Add("page");

            var config = Settings.Config;

            // Game content search paths
            gamePaths = new ListBox { Height = 140, ItemsSource = config.GameSearchPaths.ToList() };

            var gamePathsAdd = new Button { Content = AppIcons.CreateHeader("FileAdd", "Add .vpk or gameinfo.gi") };
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

            var gamePathsAddFolder = new Button { Content = AppIcons.CreateHeader("FolderAdd", "Add folder") };
            gamePathsAddFolder.Click += async (_, _) =>
            {
                var folder = await AppFileDialogs.PickFolderAsync(null, AppFileDialogs.RememberIn.OpenDirectory, updateRemembered: false).ConfigureAwait(true);

                if (folder != null)
                {
                    config.OpenDirectory = folder;
                    AddGamePath(folder);
                }
            };

            var gamePathsRemove = new Button { Content = AppIcons.CreateHeader("FolderRemove", "Remove") };
            gamePathsRemove.Click += (_, _) =>
            {
                if (gamePaths.SelectedItem is string path)
                {
                    config.GameSearchPaths.Remove(path);
                    gamePaths.ItemsSource = config.GameSearchPaths.ToList();
                }
            };

            var gamePathsGroup = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    gamePaths,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { gamePathsAdd, gamePathsAddFolder, gamePathsRemove } },
                },
            };

            // Video settings
            var maxTextureSize = Numeric(config.MaxTextureSize, 64, 16384, 64, v => config.MaxTextureSize = (int)v);
            var fov = Numeric((decimal)config.FieldOfView, 10, 170, 1, v => config.FieldOfView = (float)v, "0.######");
            var viewmodelFov = Numeric((decimal)config.ViewmodelFieldOfView, 10, 170, 1, v => config.ViewmodelFieldOfView = (float)v, "0.######");

            var shadowQualityIndex = Array.FindIndex(ShadowQualityResolutions, r => config.ShadowResolution <= r);
            var shadowQuality = Combo(ShadowQualityNames, shadowQualityIndex < 0 ? ShadowQualityResolutions.Length - 1 : shadowQualityIndex, i => config.ShadowResolution = ShadowQualityResolutions[i]);

            var antiAliasingIndex = Array.FindLastIndex(AntiAliasingSampleOptions, s => config.AntiAliasingSamples >= s);
            var antiAliasing = Combo(AntiAliasingSampleOptions.Select(static s => $"{s}x").ToArray(), antiAliasingIndex, i => config.AntiAliasingSamples = AntiAliasingSampleOptions[i]);

            var sensitivityValue = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 32 };
            var sensitivity = SliderRow(config.MouseSensitivity * 10, 0, 80, sensitivityValue, v =>
            {
                config.MouseSensitivity = (float)(v / 10);
                return config.MouseSensitivity.ToString("0.0", CultureInfo.InvariantCulture);
            });

            var videoGroup = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    Check("Vertical Sync", config.Vsync != 0, v => config.Vsync = v ? 1 : 0),
                    Check("Display FPS", config.DisplayFps != 0, v => config.DisplayFps = v ? 1 : 0),
                    Check("Smooth camera", config.SmoothCameraEnabled, v => config.SmoothCameraEnabled = v),
                    Row("Camera FOV:", fov),
                    Row("Viewmodel FOV:", viewmodelFov),
                    Row("Max texture size:", maxTextureSize),
                    Row("Anti-aliasing:", antiAliasing),
                    Row("Shadow quality:", shadowQuality),
                    Row("Mouse sensitivity:", sensitivity),
                },
            };

            // Quick file preview
            var quickPreviewFlags = (Settings.QuickPreviewFlags)config.QuickFilePreview;

            void SetQuickPreviewFlag(Settings.QuickPreviewFlags flag, bool enabled)
            {
                var flags = (Settings.QuickPreviewFlags)config.QuickFilePreview;
                config.QuickFilePreview = (int)(enabled ? flags | flag : flags & ~flag);
            }

            var quickPreviewGroup = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    Check("Preview files after selecting", (quickPreviewFlags & Settings.QuickPreviewFlags.Enabled) != 0, v => SetQuickPreviewFlag(Settings.QuickPreviewFlags.Enabled, v)),
                    Check("Auto play sounds", (quickPreviewFlags & Settings.QuickPreviewFlags.AutoPlaySounds) != 0, v => SetQuickPreviewFlag(Settings.QuickPreviewFlags.AutoPlaySounds, v)),
                },
            };

            // Audio
            var volumeValue = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 32 };
            var volume = SliderRow(MathF.Round(Math.Clamp(config.Volume, 0, 1) * 100), 0, 100, volumeValue, v =>
            {
                config.Volume = (float)(v / 100);
                return string.Create(CultureInfo.InvariantCulture, $"{(int)v}%");
            });

            // Explorer
            var themes = Enum.GetValues<Themer.AppTheme>();
            var theme = Combo(themes.Select(Themer.GetDisplayName).ToArray(), Math.Clamp(config.Theme, 0, themes.Length - 1), i =>
            {
                config.Theme = i;
                Themer.ApplyTheme(themes[i]);
            });

            var explorerGroup = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    Check("Open explorer on start", config.OpenExplorerOnStart != 0, v => config.OpenExplorerOnStart = v ? 1 : 0),
                    Row("Text viewer font size:", Numeric(config.TextViewerFontSize, 7, 30, 1, v => config.TextViewerFontSize = (int)v)),
                    Row("Theme:", theme),
                },
            };

            if (FileAssociation.IsSupported)
            {
                var registerAssociation = new Button { Content = AppIcons.CreateHeader("VPKLink", "Register .vpk file association") };
                registerAssociation.Click += async (_, _) => await FileAssociation.RegisterAsync().ConfigureAwait(true);
                explorerGroup.Children.Add(registerAssociation);
            }

            var footer = new TextBlock
            {
                Text = "No regrets, Mr. Freeman",
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new(0, 32),
                Opacity = 0.5,
            };

            Content = new StackPanel
            {
                Margin = new(16),
                Spacing = 12,
                MaxWidth = 640,
                HorizontalAlignment = HorizontalAlignment.Left,
                Children =
                {
                    GroupBox.Create("Game content search paths", gamePathsGroup),
                    GroupBox.Create("Video settings", videoGroup),
                    GroupBox.Create("Quick file preview", quickPreviewGroup),
                    GroupBox.Create("Audio", Row("Volume:", volume)),
                    GroupBox.Create("Explorer", explorerGroup),
                    footer,
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

        private static Grid Row(string label, Control control)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*") };
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(control, 1);
            grid.Children.Add(text);
            grid.Children.Add(control);
            return grid;
        }

        private static CheckBox Check(string label, bool current, Action<bool> set)
        {
            var check = new CheckBox { Content = label, IsChecked = current };
            check.IsCheckedChanged += (_, _) => set(check.IsChecked == true);
            return check;
        }

        private static ComboBox Combo(string[] items, int selectedIndex, Action<int> set)
        {
            var combo = new ComboBox
            {
                ItemsSource = items,
                SelectedIndex = selectedIndex,
                MinWidth = 150,
            };
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedIndex >= 0)
                {
                    set(combo.SelectedIndex);
                }
            };
            return combo;
        }

        private static NumericUpDown Numeric(decimal value, decimal min, decimal max, decimal increment, Action<decimal> set, string format = "0")
        {
            var numeric = new NumericUpDown
            {
                Value = Math.Clamp(value, min, max),
                Minimum = min,
                Maximum = max,
                Increment = increment,
                FormatString = format,
                Width = 150,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            numeric.ValueChanged += (_, e) =>
            {
                if (e.NewValue is { } newValue)
                {
                    set(newValue);
                }
            };
            return numeric;
        }

        private static DockPanel SliderRow(double value, double min, double max, TextBlock valueLabel, Func<double, string> set)
        {
            var slider = new Avalonia.Controls.Slider
            {
                Minimum = min,
                Maximum = max,
                Value = value,
                IsSnapToTickEnabled = true,
                TickFrequency = 1,
                Width = 200,
            };

            valueLabel.Margin = new(8, 0, 0, 0);
            valueLabel.Text = set(value);
            slider.ValueChanged += (_, e) => valueLabel.Text = set(e.NewValue);

            var row = new DockPanel { HorizontalAlignment = HorizontalAlignment.Left };
            DockPanel.SetDock(slider, Dock.Left);
            row.Children.Add(slider);
            row.Children.Add(valueLabel);
            return row;
        }
    }
}
