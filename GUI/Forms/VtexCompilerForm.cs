using GUI.Utils;
using Svg.Skia;
using GUI.Controls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using static GUI.Types.Viewers.ViewerContent;

namespace GUI.Forms
{
    public partial class VtexCompilerForm : ThemedForm
    {
        private TextBox pathTextBox = null!;
        private Button browseButton = null!;
        private Button compileButton = null!;
        private Label titleLabel = null!;

        public VtexCompilerForm()
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
                compileButton?.Dispose();
                titleLabel?.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            ClientSize = new Size(520, 180);
            Text = "VTEX Compiler";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            titleLabel = new Label
            {
                Text = "Create Panorama VTEX",
                Font = new Font(Font.FontFamily, 12, FontStyle.Bold),
                Location = new Point(16, 20),
                AutoSize = true
            };

            var fileLabel = new Label
            {
                Text = "PNG File:",
                Location = new Point(16, 64),
                AutoSize = true
            };

            pathTextBox = new TextBox
            {
                Location = new Point(16, 88),
                Width = 390
            };

            browseButton = new Button
            {
                Text = "Browse...",
                Location = new Point(414, 86),
                Width = 80,
                Height = 25
            };
            browseButton.Click += OnBrowseButtonClick;

            compileButton = new Button
            {
                Text = "Create / Compile",
                Location = new Point(344, 130),
                Width = 150,
                Height = 32
            };
            compileButton.Click += OnCompileButtonClick;

            Controls.Add(titleLabel);
            Controls.Add(fileLabel);
            Controls.Add(pathTextBox);
            Controls.Add(browseButton);
            Controls.Add(compileButton);
        }

        private void OnBrowseButtonClick(object? sender, EventArgs e)
        {
#pragma warning disable RS0030 // Запрещенный API
            using var dialog = new OpenFileDialog
            {
                Filter = "PNG Files (*.png)|*.png|All Files (*.*)|*.*",
                Title = "Выберите PNG изображение"
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                pathTextBox.Text = dialog.FileName;
            }
#pragma warning restore RS0030
        }

        private async void OnCompileButtonClick(object? sender, EventArgs e)
        {
            var filePath = pathTextBox.Text?.Trim('"', ' ');

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                Log.Error(nameof(VtexCompilerForm), "Укажите корректный путь к существующему PNG-файлу.");
                return;
            }

            if (!filePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                Log.Error(nameof(VtexCompilerForm), "Файл должен иметь расширение .png.");
                return;
            }

            Program.MainForm.FocusLogPage();
            compileButton.Enabled = false;

            try
            {
                await Task.Run(() => ProcessCompilation(filePath)).ConfigureAwait(true);
            }
            finally
            {
                compileButton.Enabled = true;
            }
        }

        private void ProcessCompilation(string filePath)
        {
            try
            {
                var compilerPath = FindResourceCompiler();
                if (string.IsNullOrEmpty(compilerPath) || !File.Exists(compilerPath))
                {
                    Log.Error(nameof(VtexCompilerForm), "Не найден resourcecompiler.exe. Проверьте путь к Workshop Tools.");
                    return;
                }

                var gameDir = GetGameDirFromCompiler(compilerPath);
                if (string.IsNullOrEmpty(gameDir))
                {
                    Log.Error(nameof(VtexCompilerForm), "Не удалось определить папку игры (gameinfo.gi).");
                    return;
                }

                var dotaBetaDir = Directory.GetParent(gameDir)?.Parent?.FullName;
                if (string.IsNullOrEmpty(dotaBetaDir))
                {
                    Log.Error(nameof(VtexCompilerForm), "Не удалось определить корневую папку Dota 2.");
                    return;
                }

                const string addonName = "Source2Viewer";
                var contentVtexDir = Path.Combine(dotaBetaDir, "content", "dota_addons", addonName, "panorama", "vtex");
                var gameVtexDir = Path.Combine(dotaBetaDir, "game", "dota_addons", addonName, "panorama", "vtex");

                Directory.CreateDirectory(contentVtexDir);
                Directory.CreateDirectory(gameVtexDir);

                var fileName = Path.GetFileName(filePath);
                var targetPngPath = Path.Combine(contentVtexDir, fileName);

                File.Copy(filePath, targetPngPath, overwrite: true);

                var vtexFileName = Path.ChangeExtension(fileName, ".vtex");
                var targetVtexPath = Path.Combine(contentVtexDir, vtexFileName);

                var relativePngPath = $"panorama/vtex/{fileName}";

                var vtexContent = $@"<!-- dmx encoding keyvalues2_noids 1 format vtex 1 -->""CDmeVtex""
{{
	""m_inputTextureArray"" ""element_array"" 
	[
		""CDmeInputTexture""
		{{
			""m_name"" ""string"" ""InputTexture0""
			""m_fileName"" ""string"" ""{relativePngPath}""
			""m_colorSpace"" ""string"" ""srgb""
			""m_typeString"" ""string"" ""2D""
			""m_imageProcessorArray"" ""element_array"" 
			[
				""CDmeImageProcessor""
				{{
					""m_algorithm"" ""string"" ""None""
					""m_stringArg"" ""string"" """"
					""m_vFloat4Arg"" ""vector4"" ""0 0 0 0""
				}}
			]
		}}
	]
	""m_outputTypeString"" ""string"" ""2D""
	""m_outputFormat"" ""string"" ""RGBA8888""
	""m_outputClearColor"" ""vector4"" ""0 0 0 0""
	""m_nOutputMinDimension"" ""int"" ""0""
	""m_nOutputMaxDimension"" ""int"" ""0""
	""m_textureOutputChannelArray"" ""element_array"" 
	[
		""CDmeTextureOutputChannel""
		{{
			""m_inputTextureArray"" ""string_array"" [ ""InputTexture0"" ]
			""m_srcChannels"" ""string"" ""rgba""
			""m_dstChannels"" ""string"" ""rgba""
			""m_mipAlgorithm"" ""CDmeImageProcessor""
			{{
				""m_algorithm"" ""string"" ""Box""
				""m_stringArg"" ""string"" """"
				""m_vFloat4Arg"" ""vector4"" ""0 0 0 0""
			}}
			""m_outputColorSpace"" ""string"" ""srgb""
		}}
	]
	""m_vClamp"" ""vector3"" ""0 0 0""
	""m_bNoLod"" ""bool"" ""0""
}}";

                File.WriteAllText(targetVtexPath, vtexContent);
                Log.Info(nameof(VtexCompilerForm), $"Файл подготовлен: {targetVtexPath}");

                // Используем конкретный targetVtexPath вместо маски *.vtex
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = compilerPath,
                    Arguments = $"-game \"{gameDir}\" -i \"{targetVtexPath}\" -outdir \"{gameVtexDir}\"",
                    WorkingDirectory = contentVtexDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                Log.Info(nameof(VtexCompilerForm), "Запуск resourcecompiler.exe для файла " + vtexFileName + "...");

                using var process = System.Diagnostics.Process.Start(processInfo);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        Log.Info(nameof(VtexCompilerForm), $"[ResourceCompiler Output]:\n{output}");
                    }

                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        Log.Error(nameof(VtexCompilerForm), $"[ResourceCompiler Error]:\n{error}");
                    }

                    if (process.ExitCode == 0)
                    {
                        Log.Info(nameof(VtexCompilerForm), $"Успешно скомпилировано в: {gameVtexDir}");
                        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{Path.Combine(gameVtexDir, vtexFileName + "_c")}\"");
                    }
                    else
                    {
                        Log.Error(nameof(VtexCompilerForm), $"Компиляция завершилась с ошибкой. Код: {process.ExitCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(nameof(VtexCompilerForm), $"Ошибка в процессе подготовки/компиляции: {ex.Message}");
            }
        }

        private static string? FindResourceCompiler()
        {
            var steamPath = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;

            if (string.IsNullOrEmpty(steamPath))
            {
                steamPath = @"C:\Program Files (x86)\Steam";
            }

            var searchPaths = new List<string>();

            var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdfPath))
            {
                try
                {
                    var lines = File.ReadAllLines(vdfPath);
                    foreach (var line in lines)
                    {
                        if (line.Contains("\"path\"", StringComparison.OrdinalIgnoreCase))
                        {
                            var parts = line.Split('"');
                            if (parts.Length >= 4)
                            {
                                var libPath = parts[3].Replace(@"\\", @"\", StringComparison.Ordinal);
                                searchPaths.Add(libPath);
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            if (!searchPaths.Contains(steamPath))
            {
                searchPaths.Add(steamPath);
            }

            foreach (var path in searchPaths)
            {
                var exePath = Path.Combine(path, "steamapps", "common", "dota 2 beta", "game", "bin", "win64", "resourcecompiler.exe");
                if (File.Exists(exePath))
                {
                    return exePath;
                }
            }

            return null;
        }

        private static string? GetGameDirFromCompiler(string compilerPath)
        {
            var binDir = Directory.GetParent(compilerPath);
            var gameBinDir = binDir?.Parent;
            var gameDir = gameBinDir?.Parent;

            if (gameDir != null)
            {
                var dotaGameDir = Path.Combine(gameDir.FullName, "dota");

                if (File.Exists(Path.Combine(dotaGameDir, "gameinfo.gi")))
                {
                    return dotaGameDir;
                }
            }

            return null;
        }
    }
}
