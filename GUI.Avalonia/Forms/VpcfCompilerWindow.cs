using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GUI.Controls;
using GUI.Types.Exporter.CharacterAssets;
using GUI.Utils;
using DrawingColor = System.Drawing.Color;

namespace GUI.Forms
{
    /// <summary>
    /// Recolors .vpcf particle sources on disk, and every child particle they reference, to one color.
    /// </summary>
    sealed class VpcfCompilerWindow : Window
    {
        private readonly List<string> selectedFiles = [];
        private readonly TextBox pathTextBox;
        private readonly Button colorButton;
        private readonly TextBlock colorLabel;
        private readonly Button saveButton;
        private DrawingColor selectedColor;

        public VpcfCompilerWindow()
        {
            Title = "VPCF Particle Recolor";
            Width = 540;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            CanMinimize = false;
            CanMaximize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Icon = Program.MainForm.Icon;

            var preferences = CharacterExportPreferences.Load();
            selectedColor = preferences.LastParticleColor is { } lastColor ? DrawingColor.FromArgb(lastColor) : DrawingColor.Red;

            pathTextBox = new TextBox { IsReadOnly = true };

            var browseButton = new Button { Content = "Browse...", Width = 85, Margin = new(8, 0, 0, 0) };
            browseButton.Click += async (_, _) => await BrowseAsync().ConfigureAwait(true);

            colorButton = new Button
            {
                Content = AppIcons.CreateHeader("ColorEyeDropper", "Pick Color", 16),
                Width = 120,
                Height = 28,
            };
            colorButton.Click += async (_, _) => await PickColorAsync().ConfigureAwait(true);

            colorLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 0, 0) };

            saveButton = new Button
            {
                Content = "Recolor & Save",
                Width = 155,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new(0, 14, 0, 0),
            };
            saveButton.Click += async (_, _) => await SaveAsync().ConfigureAwait(true);

            var pathRow = new DockPanel();
            DockPanel.SetDock(browseButton, Dock.Right);
            pathRow.Children.Add(browseButton);
            pathRow.Children.Add(pathTextBox);

            Content = new StackPanel
            {
                Margin = new(16),
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = "Recolor VPCF Particle Files", FontSize = 16, FontWeight = FontWeight.Bold, Margin = new(0, 0, 0, 10) },
                    new TextBlock { Text = "VPCF File(s):" },
                    pathRow,
                    new TextBlock { Text = "Target Color (Prismatic):", Margin = new(0, 8, 0, 0) },
                    new StackPanel { Orientation = Orientation.Horizontal, Children = { colorButton, colorLabel } },
                    saveButton,
                },
            };

            UpdateColorDisplay();
        }

        private async Task BrowseAsync()
        {
            var files = await AppFileDialogs.OpenFilesAsync("Choose the VPCF particle file(s)", "VPCF Particle Files (*.vpcf)|*.vpcf|All Files (*.*)|*.*").ConfigureAwait(true);

            if (files == null)
            {
                return;
            }

            selectedFiles.Clear();
            selectedFiles.AddRange(files);

            if (selectedFiles.Count == 1)
            {
                pathTextBox.Text = selectedFiles[0];
                ToolTip.SetTip(pathTextBox, selectedFiles[0]);
            }
            else
            {
                pathTextBox.Text = $"{selectedFiles.Count} files selected ({string.Join(", ", selectedFiles.Select(Path.GetFileName))})";
                ToolTip.SetTip(pathTextBox, string.Join(Environment.NewLine, selectedFiles));
            }
        }

        private async Task PickColorAsync()
        {
            if (await ColorPickerDialog.ShowAsync(selectedColor, "Target color").ConfigureAwait(true) is not { } color)
            {
                return;
            }

            selectedColor = color;
            UpdateColorDisplay();

            var preferences = CharacterExportPreferences.Load();
            preferences.LastParticleColor = color.ToArgb();
            preferences.Save();
        }

        private void UpdateColorDisplay()
        {
            var light = (selectedColor.R * 0.299) + (selectedColor.G * 0.587) + (selectedColor.B * 0.114) > 150;

            colorButton.Background = Themer.GetBrush(selectedColor);
            colorButton.Foreground = light ? Brushes.Black : Brushes.White;
            colorLabel.Text = string.Create(CultureInfo.InvariantCulture,
                $"RGB: {selectedColor.R}, {selectedColor.G}, {selectedColor.B} (#{selectedColor.R:X2}{selectedColor.G:X2}{selectedColor.B:X2})");
        }

        private async Task SaveAsync()
        {
            var filesToProcess = new List<string>(selectedFiles);

            if (filesToProcess.Count == 0 && pathTextBox.Text?.Trim('"', ' ') is { Length: > 0 } manualPath && File.Exists(manualPath))
            {
                filesToProcess.Add(manualPath);
            }

            Program.MainForm.FocusLogPage();

            if (filesToProcess.Count == 0)
            {
                Log.Error(nameof(VpcfCompilerWindow), "Choose at least one .vpcf file with the Browse button.");
                return;
            }

            saveButton.IsEnabled = false;

            var color = selectedColor;

            try
            {
                await Task.Run(() =>
                {
                    var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var count = 0;

                    foreach (var file in filesToProcess)
                    {
                        if (!File.Exists(file) || !file.EndsWith(".vpcf", StringComparison.OrdinalIgnoreCase))
                        {
                            Log.Warn(nameof(VpcfCompilerWindow), $"Skipped \"{file}\", it does not exist or is not a .vpcf");
                            continue;
                        }

                        Log.Info(nameof(VpcfCompilerWindow), $"[{count + 1}/{filesToProcess.Count}] Recoloring {Path.GetFileName(file)} to RGB({color.R}, {color.G}, {color.B})...");
                        VpcfColorEditor.RecolorFileInPlace(file, color, processed);
                        count++;
                    }

                    Log.Info(nameof(VpcfCompilerWindow), $"Recolored {count} file{(count == 1 ? "" : "s")}.");

                    if (count > 0)
                    {
                        ExplorerControl.RevealInFileManager(filesToProcess[0]);
                    }
                }).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Log.Error(nameof(VpcfCompilerWindow), $"Failed to recolor: {ex.Message}");
            }
            finally
            {
                saveButton.IsEnabled = true;
            }
        }
    }
}
