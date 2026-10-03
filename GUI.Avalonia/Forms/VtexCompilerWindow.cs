using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GUI.Controls;
using GUI.Utils;
using ValveResourceFormat.IO;

namespace GUI.Forms
{
    /// <summary>
    /// Turns a PNG into a panorama texture: writes it with a .vtex next to it into the Source2Viewer addon of Dota 2,
    /// and compiles that with the game's resourcecompiler.exe.
    /// </summary>
    sealed class VtexCompilerWindow : Window
    {
        private const int DotaAppId = 570;
        private const string AddonName = "Source2Viewer";

        private readonly TextBox pathTextBox;
        private readonly Button compileButton;

        public VtexCompilerWindow()
        {
            Title = "VTEX Compiler";
            Width = 520;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            CanMinimize = false;
            CanMaximize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Icon = Program.MainForm.Icon;

            pathTextBox = new TextBox();

            var browseButton = new Button { Content = "Browse...", Width = 80, Margin = new(8, 0, 0, 0) };
            browseButton.Click += async (_, _) => await BrowseAsync().ConfigureAwait(true);

            compileButton = new Button
            {
                Content = "Create / Compile",
                Width = 150,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new(0, 16, 0, 0),
            };
            compileButton.Click += async (_, _) => await CompileAsync().ConfigureAwait(true);

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
                    new TextBlock { Text = "Create Panorama VTEX", FontSize = 16, FontWeight = FontWeight.Bold, Margin = new(0, 4, 0, 14) },
                    new TextBlock { Text = "PNG File:" },
                    pathRow,
                    compileButton,
                },
            };
        }

        private async Task BrowseAsync()
        {
            var files = await AppFileDialogs.OpenFilesAsync("Choose a PNG image", "PNG Files (*.png)|*.png|All Files (*.*)|*.*", multiselect: false).ConfigureAwait(true);

            if (files is [var file, ..])
            {
                pathTextBox.Text = file;
            }
        }

        private async Task CompileAsync()
        {
            var filePath = pathTextBox.Text?.Trim('"', ' ');

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                Log.Error(nameof(VtexCompilerWindow), "Choose an existing PNG file.");
                Program.MainForm.FocusLogPage();
                return;
            }

            if (!filePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                Log.Error(nameof(VtexCompilerWindow), "The file must be a .png.");
                Program.MainForm.FocusLogPage();
                return;
            }

            Program.MainForm.FocusLogPage();
            compileButton.IsEnabled = false;

            try
            {
                await Task.Run(() => ProcessCompilation(filePath)).ConfigureAwait(true);
            }
            finally
            {
                compileButton.IsEnabled = true;
            }
        }

        private static void ProcessCompilation(string filePath)
        {
            try
            {
                if (GameFolderLocator.FindSteamGameByAppId(DotaAppId) is not { } dota)
                {
                    Log.Error(nameof(VtexCompilerWindow), "Dota 2 was not found in any Steam library.");
                    return;
                }

                var gameDir = Path.Combine(dota.GamePath, "game", "dota");

                if (!File.Exists(Path.Combine(gameDir, "gameinfo.gi")))
                {
                    Log.Error(nameof(VtexCompilerWindow), $"Could not find gameinfo.gi in \"{gameDir}\".");
                    return;
                }

                var contentVtexDir = Path.Combine(dota.GamePath, "content", "dota_addons", AddonName, "panorama", "vtex");
                var gameVtexDir = Path.Combine(dota.GamePath, "game", "dota_addons", AddonName, "panorama", "vtex");

                Directory.CreateDirectory(contentVtexDir);
                Directory.CreateDirectory(gameVtexDir);

                var fileName = Path.GetFileName(filePath);
                File.Copy(filePath, Path.Combine(contentVtexDir, fileName), overwrite: true);

                var vtexFileName = Path.ChangeExtension(fileName, ".vtex");
                var targetVtexPath = Path.Combine(contentVtexDir, vtexFileName);

                File.WriteAllText(targetVtexPath, CreateVtex($"panorama/vtex/{fileName}"));
                Log.Info(nameof(VtexCompilerWindow), $"Prepared {targetVtexPath}");

                var compilerPath = Path.Combine(dota.GamePath, "game", "bin", "win64", "resourcecompiler.exe");

                // The compiler is a Windows program, elsewhere the source is left ready to be compiled there
                if (!OperatingSystem.IsWindows())
                {
                    Log.Warn(nameof(VtexCompilerWindow), "resourcecompiler.exe only runs on Windows, the .vtex and its image were written to the addon's content folder to be compiled there.");
                    ExplorerControl.RevealInFileManager(targetVtexPath);
                    return;
                }

                if (!File.Exists(compilerPath))
                {
                    Log.Error(nameof(VtexCompilerWindow), $"resourcecompiler.exe was not found at \"{compilerPath}\", install the Dota 2 Workshop Tools.");
                    return;
                }

                Log.Info(nameof(VtexCompilerWindow), $"Running resourcecompiler.exe for {vtexFileName}...");

                var processInfo = new ProcessStartInfo
                {
                    FileName = compilerPath,
                    WorkingDirectory = contentVtexDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                processInfo.ArgumentList.Add("-game");
                processInfo.ArgumentList.Add(gameDir);
                processInfo.ArgumentList.Add("-i");
                processInfo.ArgumentList.Add(targetVtexPath);
                processInfo.ArgumentList.Add("-outdir");
                processInfo.ArgumentList.Add(gameVtexDir);

                using var process = Process.Start(processInfo);

                if (process == null)
                {
                    Log.Error(nameof(VtexCompilerWindow), "Failed to start resourcecompiler.exe.");
                    return;
                }

                // Both streams are read at once, so neither can fill up and stall the compiler
                var errorTask = process.StandardError.ReadToEndAsync();
                var output = process.StandardOutput.ReadToEnd();
                var error = errorTask.GetAwaiter().GetResult();
                process.WaitForExit();

                if (!string.IsNullOrWhiteSpace(output))
                {
                    Log.Info(nameof(VtexCompilerWindow), $"[ResourceCompiler Output]:\n{output}");
                }

                if (!string.IsNullOrWhiteSpace(error))
                {
                    Log.Error(nameof(VtexCompilerWindow), $"[ResourceCompiler Error]:\n{error}");
                }

                if (process.ExitCode == 0)
                {
                    Log.Info(nameof(VtexCompilerWindow), $"Compiled into {gameVtexDir}");
                    ExplorerControl.RevealInFileManager(Path.Combine(gameVtexDir, vtexFileName + GameFileLoader.CompiledFileSuffix));
                }
                else
                {
                    Log.Error(nameof(VtexCompilerWindow), $"Compiling failed with exit code {process.ExitCode}.");
                }
            }
            catch (Exception ex)
            {
                Log.Error(nameof(VtexCompilerWindow), $"Failed to prepare or compile the texture: {ex.Message}");
            }
        }

        /// <summary>
        /// A texture that is the image as is, without mipmap filtering or compression, like panorama images are.
        /// </summary>
        private static string CreateVtex(string imagePath) => $$"""
            <!-- dmx encoding keyvalues2_noids 1 format vtex 1 -->
            "CDmeVtex"
            {
                "m_inputTextureArray" "element_array"
                [
                    "CDmeInputTexture"
                    {
                        "m_name" "string" "InputTexture0"
                        "m_fileName" "string" "{{imagePath}}"
                        "m_colorSpace" "string" "srgb"
                        "m_typeString" "string" "2D"
                        "m_imageProcessorArray" "element_array"
                        [
                            "CDmeImageProcessor"
                            {
                                "m_algorithm" "string" "None"
                                "m_stringArg" "string" ""
                                "m_vFloat4Arg" "vector4" "0 0 0 0"
                            }
                        ]
                    }
                ]
                "m_outputTypeString" "string" "2D"
                "m_outputFormat" "string" "RGBA8888"
                "m_outputClearColor" "vector4" "0 0 0 0"
                "m_nOutputMinDimension" "int" "0"
                "m_nOutputMaxDimension" "int" "0"
                "m_textureOutputChannelArray" "element_array"
                [
                    "CDmeTextureOutputChannel"
                    {
                        "m_inputTextureArray" "string_array" [ "InputTexture0" ]
                        "m_srcChannels" "string" "rgba"
                        "m_dstChannels" "string" "rgba"
                        "m_mipAlgorithm" "CDmeImageProcessor"
                        {
                            "m_algorithm" "string" "Box"
                            "m_stringArg" "string" ""
                            "m_vFloat4Arg" "vector4" "0 0 0 0"
                        }
                        "m_outputColorSpace" "string" "srgb"
                    }
                ]
                "m_vClamp" "vector3" "0 0 0"
                "m_bNoLod" "bool" "0"
            }
            """;
    }
}
