using GUI.Types.Exporter.CharacterAssets;
using GUI.Utils;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using static GUI.Types.Viewers.ViewerContent;

namespace GUI.Forms
{
    public partial class VpcfCompilerForm : ThemedForm
    {
        private TextBox pathTextBox = null!;
        private Button browseButton = null!;
        private Button colorButton = null!;
        private Label colorLabel = null!;
        private Button saveButton = null!;
        private Label titleLabel = null!;
        private ToolTip formToolTip = null!;

        private readonly List<string> selectedFiles = [];
        private static Color selectedColor = LoadLastColor();

        public VpcfCompilerForm()
        {
            InitializeComponent();
            Icon = Program.MainForm.Icon;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                pathTextBox?.Dispose();
                browseButton?.Dispose();
                colorButton?.Dispose();
                colorLabel?.Dispose();
                saveButton?.Dispose();
                titleLabel?.Dispose();
                formToolTip?.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            ClientSize = new Size(540, 230);
            Text = "VPCF Particle Recolor";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            formToolTip = new ToolTip();

            titleLabel = new Label
            {
                Text = "Recolor VPCF Particle Files",
                Font = new Font(Font.FontFamily, 12, FontStyle.Bold),
                Location = new Point(16, 16),
                AutoSize = true
            };

            var fileLabel = new Label
            {
                Text = "VPCF File(s):",
                Location = new Point(16, 56),
                AutoSize = true
            };

            pathTextBox = new TextBox
            {
                Location = new Point(16, 78),
                Width = 410,
                ReadOnly = true
            };

            browseButton = new Button
            {
                Text = "Browse...",
                Location = new Point(434, 76),
                Width = 85,
                Height = 25
            };
            browseButton.Click += OnBrowseButtonClick;

            var chooseColorLabel = new Label
            {
                Text = "Target Color (Prismatic):",
                Location = new Point(16, 116),
                AutoSize = true
            };

            colorButton = new Button
            {
                Text = "🎨 Pick Color",
                Font = new Font("Segoe UI Emoji", 9f),
                Location = new Point(16, 138),
                Width = 120,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false,
                Cursor = Cursors.Hand
            };
            colorButton.Click += OnColorButtonClick;

            colorLabel = new Label
            {
                Location = new Point(144, 144),
                AutoSize = true
            };

            saveButton = new Button
            {
                Text = "Recolor & Save",
                Location = new Point(364, 180),
                Width = 155,
                Height = 32
            };
            saveButton.Click += OnSaveButtonClick;

            UpdateColorDisplay();

            Controls.Add(titleLabel);
            Controls.Add(fileLabel);
            Controls.Add(pathTextBox);
            Controls.Add(browseButton);
            Controls.Add(chooseColorLabel);
            Controls.Add(colorButton);
            Controls.Add(colorLabel);
            Controls.Add(saveButton);
        }

        private void OnBrowseButtonClick(object? sender, EventArgs e)
        {
#pragma warning disable RS0030
            using var dialog = new OpenFileDialog
            {
                Filter = "VPCF Particle Files (*.vpcf)|*.vpcf|All Files (*.*)|*.*",
                Title = "Выберите файл(ы) партиклов VPCF",
                Multiselect = true // Разрешаем выбор нескольких файлов
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                selectedFiles.Clear();
                selectedFiles.AddRange(dialog.FileNames);

                if (selectedFiles.Count == 1)
                {
                    pathTextBox.Text = selectedFiles[0];
                    formToolTip.SetToolTip(pathTextBox, selectedFiles[0]);
                }
                else
                {
                    pathTextBox.Text = $"Выбрано файлов: {selectedFiles.Count} ({string.Join(", ", selectedFiles.Select(Path.GetFileName))})";
                    formToolTip.SetToolTip(pathTextBox, string.Join(Environment.NewLine, selectedFiles));
                }
            }
#pragma warning restore RS0030
        }

        private void OnColorButtonClick(object? sender, EventArgs e)
        {
            using var dialog = new ColorDialog
            {
                Color = selectedColor,
                AllowFullOpen = true,
                FullOpen = true
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                selectedColor = dialog.Color;
                SaveLastColor(selectedColor); // Сохраняем цвет в реестр
                UpdateColorDisplay();
            }
        }

        private void UpdateColorDisplay()
        {
            colorButton.BackColor = selectedColor;
            colorButton.ForeColor = (selectedColor.R * 0.299 + selectedColor.G * 0.587 + selectedColor.B * 0.114) > 150 ? Color.Black : Color.White;
            colorLabel.Text = $"RGB: {selectedColor.R}, {selectedColor.G}, {selectedColor.B} (#{selectedColor.R:X2}{selectedColor.G:X2}{selectedColor.B:X2})";
        }

        private async void OnSaveButtonClick(object? sender, EventArgs e)
        {
            var filesToProcess = new List<string>(selectedFiles);

            if (filesToProcess.Count == 0 && !string.IsNullOrWhiteSpace(pathTextBox.Text))
            {
                var manualPath = pathTextBox.Text.Trim('"', ' ');
                if (File.Exists(manualPath))
                {
                    filesToProcess.Add(manualPath);
                }
            }

            if (filesToProcess.Count == 0)
            {
                Log.Error(nameof(VpcfCompilerForm), "Выберите хотя бы один файл .vpcf через кнопку Browse.");
                return;
            }

            Program.MainForm.FocusLogPage();
            saveButton.Enabled = false;

            try
            {
                await Task.Run(() =>
                {
                    var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    int count = 0;

                    foreach (var file in filesToProcess)
                    {
                        if (!File.Exists(file) || !file.EndsWith(".vpcf", StringComparison.OrdinalIgnoreCase))
                        {
                            Log.Warn(nameof(VpcfCompilerForm), $"Пропущен файл (не существует или не .vpcf): {file}");
                            continue;
                        }

                        Log.Info(nameof(VpcfCompilerForm), $"[{count + 1}/{filesToProcess.Count}] Перекраска {Path.GetFileName(file)} в RGB({selectedColor.R}, {selectedColor.G}, {selectedColor.B})...");
                        VpcfColorEditor.RecolorFileInPlace(file, selectedColor, processed);
                        count++;
                    }

                    Log.Info(nameof(VpcfCompilerForm), $"Успешно перекрашено файлов: {count}.");

                    if (filesToProcess.Count > 0 && File.Exists(filesToProcess[0]))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filesToProcess[0]}\"");
                    }
                }).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Log.Error(nameof(VpcfCompilerForm), $"Ошибка перекраски: {ex.Message}");
            }
            finally
            {
                saveButton.Enabled = true;
            }
        }

        private static Color LoadLastColor()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Source2Viewer");
                if (key?.GetValue("VpcfLastColor") is int argb)
                {
                    return Color.FromArgb(argb);
                }
            }
            catch
            {
            }
            return Color.Red; // Дефолтный цвет при самом первом запуске
        }

        private static void SaveLastColor(Color color)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Source2Viewer");
                key?.SetValue("VpcfLastColor", color.ToArgb(), Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch
            {
            }
        }
    }
}
