using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using GUI.Utils;

namespace GUI
{
    sealed class SettingsWindow : Window
    {
        private readonly ListBox searchPaths;

        public SettingsWindow()
        {
            Title = "Settings";
            Width = 640;
            Height = 640;
            MinWidth = 480;
            MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var config = Settings.Config;
            var panel = new StackPanel { Margin = new(16), Spacing = 6 };

            panel.Children.Add(Header("Game search paths"));
            panel.Children.Add(new TextBlock
            {
                Text = "Folders, gameinfo.gi files or VPKs that are searched when a file references another file (materials, textures, models).",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Opacity = 0.75,
            });

            searchPaths = new ListBox { Height = 140, ItemsSource = config.GameSearchPaths.ToList() };
            panel.Children.Add(searchPaths);

            var addFolder = new Button { Content = AppIcons.CreateHeader("FolderAdd", "Add folder...") };
            addFolder.Click += async (_, _) =>
            {
                if (await AppFileDialogs.PickFolderAsync("Add game search folder").ConfigureAwait(true) is { } folder)
                {
                    AddSearchPath(folder);
                }
            };

            var addFile = new Button { Content = AppIcons.CreateHeader("FileAdd", "Add gameinfo.gi or VPK...") };
            addFile.Click += async (_, _) =>
            {
                var files = await AppFileDialogs.OpenFilesAsync("Add game search file", "Game info or VPK|gameinfo.gi;*.vpk|All files (*.*)|*.*", multiselect: false).ConfigureAwait(true);

                if (files is { Length: > 0 })
                {
                    AddSearchPath(files[0]);
                }
            };

            var remove = new Button { Content = AppIcons.CreateHeader("FolderRemove", "Remove") };
            remove.Click += (_, _) =>
            {
                if (searchPaths.SelectedItem is string path)
                {
                    config.GameSearchPaths.Remove(path);
                    RefreshSearchPaths();
                }
            };

            panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { addFolder, addFile, remove } });

            panel.Children.Add(Header("Renderer"));
            panel.Children.Add(Choice("Max texture size", [256, 512, 1024, 2048, 4096, 10240], config.MaxTextureSize, v => config.MaxTextureSize = v));
            panel.Children.Add(Choice("Shadow resolution", [256, 512, 1024, 2048, 4096], config.ShadowResolution, v => config.ShadowResolution = v));
            panel.Children.Add(Choice("Anti-aliasing samples (MSAA)", [0, 2, 4, 8, 16], config.AntiAliasingSamples, v => config.AntiAliasingSamples = v));
            panel.Children.Add(Choice("Frame pacing (vsync)", [0, 1], config.Vsync, v => config.Vsync = v, v => v == 0 ? "Uncapped" : "Display rate"));

            panel.Children.Add(Header("Camera"));
            panel.Children.Add(Number("Field of view", config.FieldOfView, 1, 170, v => config.FieldOfView = v));
            panel.Children.Add(Number("Viewmodel field of view", config.ViewmodelFieldOfView, 40, 80, v => config.ViewmodelFieldOfView = v));
            panel.Children.Add(Number("Mouse sensitivity", config.MouseSensitivity, 0.1f, 20, v => config.MouseSensitivity = v));
            panel.Children.Add(Toggle("Smooth camera movement", config.SmoothCameraEnabled, v => config.SmoothCameraEnabled = v));
            panel.Children.Add(Toggle("Display FPS", config.DisplayFps != 0, v => config.DisplayFps = v ? 1 : 0));

            panel.Children.Add(Header("Other"));
            panel.Children.Add(Number("Volume", config.Volume, 0, 1, v => config.Volume = v));
            panel.Children.Add(Number("Text viewer font size", config.TextViewerFontSize, 8, 24, v => config.TextViewerFontSize = (int)v));
            panel.Children.Add(new TextBlock { Text = "Renderer changes apply to newly opened files.", Opacity = 0.7, Margin = new(0, 8, 0, 0) });

            var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right, Margin = new(16, 8) };
            close.Click += (_, _) => Close();

            var root = new DockPanel();
            DockPanel.SetDock(close, Dock.Bottom);
            root.Children.Add(close);
            root.Children.Add(new ScrollViewer { Content = panel });
            Content = root;

            Closed += (_, _) => Settings.Save();
        }

        private void AddSearchPath(string path)
        {
            if (!Settings.Config.GameSearchPaths.Contains(path))
            {
                Settings.Config.GameSearchPaths.Add(path);
                RefreshSearchPaths();
            }
        }

        private void RefreshSearchPaths() => searchPaths.ItemsSource = Settings.Config.GameSearchPaths.ToList();

        private static TextBlock Header(string text) => new()
        {
            Text = text,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            FontSize = 15,
            Margin = new(0, 12, 0, 2),
        };

        private static Grid Row(string label, Control control)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*") };
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(control, 1);
            grid.Children.Add(text);
            grid.Children.Add(control);
            return grid;
        }

        private static Grid Choice(string label, int[] values, int current, Action<int> set, Func<int, string>? format = null)
        {
            if (!values.Contains(current))
            {
                values = [.. values.Append(current).Order()];
            }

            var combo = new ComboBox
            {
                ItemsSource = values.Select(v => format?.Invoke(v) ?? v.ToString(CultureInfo.InvariantCulture)).ToList(),
                SelectedIndex = Array.IndexOf(values, current),
                MinWidth = 160,
            };
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedIndex >= 0)
                {
                    set(values[combo.SelectedIndex]);
                }
            };

            return Row(label, combo);
        }

        private static Grid Number(string label, float current, float min, float max, Action<float> set)
        {
            var numeric = new NumericUpDown
            {
                Value = (decimal)Math.Clamp(current, min, max),
                Minimum = (decimal)min,
                Maximum = (decimal)max,
                Increment = max - min > 10 ? 1 : 0.1m,
                FormatString = max - min > 10 ? "0" : "0.0#",
                MinWidth = 160,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            numeric.ValueChanged += (_, e) =>
            {
                if (e.NewValue is { } value)
                {
                    set((float)value);
                }
            };

            return Row(label, numeric);
        }

        private static Grid Toggle(string label, bool current, Action<bool> set)
        {
            var check = new CheckBox { IsChecked = current };
            check.IsCheckedChanged += (_, _) => set(check.IsChecked == true);
            return Row(label, check);
        }
    }
}
