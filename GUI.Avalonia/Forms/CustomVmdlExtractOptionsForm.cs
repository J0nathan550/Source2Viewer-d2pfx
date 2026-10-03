using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GUI.Utils;
using WinForms = System.Windows.Forms;

namespace GUI.Forms
{
    /// <summary>
    /// Collects options for a custom VMDL extractor export: whether to auto-build the result with
    /// resourcecompiler.exe afterwards, and if so, where the compiler and target game live.
    /// Same API as the WinForms one so the exporter is shared.
    /// </summary>
    sealed class CustomVmdlExtractOptionsForm : IDisposable
    {
        private readonly Window window;
        private readonly CheckBox autoBuildCheckBox;
        private readonly TextBox resourceCompilerTextBox;
        private readonly Button browseResourceCompilerButton;
        private readonly TextBox gameDirTextBox;
        private readonly Button browseGameDirButton;
        private readonly CheckBox verboseCheckBox;
        private WinForms.DialogResult result = WinForms.DialogResult.Cancel;

        /// <summary>Whether the export should be auto-built with resourcecompiler.exe afterwards.</summary>
        public bool AutoBuild => autoBuildCheckBox.IsChecked == true;

        /// <summary>Path to resourcecompiler.exe, when <see cref="AutoBuild"/> is set.</summary>
        public string ResourceCompilerPath => resourceCompilerTextBox.Text ?? string.Empty;

        /// <summary>The "-game" directory passed to resourcecompiler.exe, when <see cref="AutoBuild"/> is set.</summary>
        public string GameDir => gameDirTextBox.Text ?? string.Empty;

        /// <summary>Whether to emit detailed per-file diagnostics to the console.</summary>
        public bool Verbose => verboseCheckBox.IsChecked == true;

        public CustomVmdlExtractOptionsForm()
        {
            autoBuildCheckBox = new CheckBox { Content = "Compile output with resourcecompiler.exe (auto-build)" };

            // The compiler only runs on Windows, so there is nothing to build with elsewhere
            if (!OperatingSystem.IsWindows())
            {
                autoBuildCheckBox.IsEnabled = false;
                ToolTip.SetTip(autoBuildCheckBox, "resourcecompiler.exe only runs on Windows.");
                ToolTip.SetShowOnDisabled(autoBuildCheckBox, true);
            }

            resourceCompilerTextBox = new TextBox { Text = Settings.Config.CustomVmdlResourceCompilerPath };
            browseResourceCompilerButton = new Button { Content = "Browse...", Width = 88 };
            browseResourceCompilerButton.Click += async (_, _) =>
            {
                var files = await AppFileDialogs.OpenFilesAsync("Choose resourcecompiler.exe", "resourcecompiler.exe|resourcecompiler.exe|Executable files|*.exe", multiselect: false, AppFileDialogs.RememberIn.None).ConfigureAwait(true);

                if (files is [var path, ..])
                {
                    resourceCompilerTextBox.Text = path;
                }
            };

            gameDirTextBox = new TextBox { Text = Settings.Config.CustomVmdlGameDir };
            browseGameDirButton = new Button { Content = "Browse...", Width = 88 };
            browseGameDirButton.Click += async (_, _) =>
            {
                var path = await AppFileDialogs.PickFolderAsync("Choose the \"-game\" directory (e.g. .../game/csgo)").ConfigureAwait(true);

                if (path != null)
                {
                    gameDirTextBox.Text = path;
                }
            };

            var hintLabel = new TextBlock
            {
                Text = "Auto-build re-compiles every exported .vmdl and, when the original package is still open, patches motion data and transplants morph/physics blocks from it. The output folder must sit under a \"content\" directory next to a sibling \"game\" directory (standard Source 2 addon layout).",
                TextWrapping = TextWrapping.Wrap,
                Margin = new(0, 6, 0, 0),
            };

            verboseCheckBox = new CheckBox { Content = "Verbose logging", Margin = new(0, 6, 0, 0) };

            var cancelButton = new Button { Content = "Cancel", Width = 88, IsCancel = true };
            var submitButton = new Button { Content = "Export", Width = 88, IsDefault = true };

            var buttons = new DockPanel { Margin = new(0, 10, 0, 0) };
            DockPanel.SetDock(cancelButton, Dock.Left);
            DockPanel.SetDock(submitButton, Dock.Right);
            buttons.Children.Add(cancelButton);
            buttons.Children.Add(submitButton);
            buttons.Children.Add(new Panel());

            window = new Window
            {
                Title = "Custom VMDL Extractor Options",
                Width = 459,
                SizeToContent = SizeToContent.Height,
                MinWidth = 380,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new(13),
                    Spacing = 4,
                    Children =
                    {
                        autoBuildCheckBox,
                        new TextBlock { Text = "resourcecompiler.exe:", Margin = new(0, 6, 0, 0) },
                        PathRow(resourceCompilerTextBox, browseResourceCompilerButton),
                        new TextBlock { Text = "\"-game\" directory:", Margin = new(0, 6, 0, 0) },
                        PathRow(gameDirTextBox, browseGameDirButton),
                        hintLabel,
                        verboseCheckBox,
                        buttons,
                    },
                },
            };

            autoBuildCheckBox.IsCheckedChanged += (_, _) => UpdateAutoBuildControlsEnabled();
            cancelButton.Click += (_, _) => window.Close();
            submitButton.Click += async (_, _) => await SubmitAsync().ConfigureAwait(true);

            UpdateAutoBuildControlsEnabled();
        }

        private static DockPanel PathRow(TextBox textBox, Button button)
        {
            var row = new DockPanel();
            button.Margin = new(6, 0, 0, 0);
            DockPanel.SetDock(button, Dock.Right);
            row.Children.Add(button);
            row.Children.Add(textBox);
            return row;
        }

        private void UpdateAutoBuildControlsEnabled()
        {
            var enabled = AutoBuild;
            resourceCompilerTextBox.IsEnabled = enabled;
            browseResourceCompilerButton.IsEnabled = enabled;
            gameDirTextBox.IsEnabled = enabled;
            browseGameDirButton.IsEnabled = enabled;
        }

        private async Task SubmitAsync()
        {
            if (AutoBuild)
            {
                if (string.IsNullOrWhiteSpace(ResourceCompilerPath) || !File.Exists(ResourceCompilerPath))
                {
                    await AppMessageDialogs.ShowMessageAsync("Choose a valid path to resourcecompiler.exe, or turn off auto-build.", "Invalid resource compiler path", MessageIcon.Warning).ConfigureAwait(true);
                    return;
                }

                if (string.IsNullOrWhiteSpace(GameDir) || !Directory.Exists(GameDir))
                {
                    await AppMessageDialogs.ShowMessageAsync("Choose a valid \"-game\" directory, or turn off auto-build.", "Invalid game directory", MessageIcon.Warning).ConfigureAwait(true);
                    return;
                }

                Settings.Config.CustomVmdlResourceCompilerPath = ResourceCompilerPath;
                Settings.Config.CustomVmdlGameDir = GameDir;
                Settings.Save();
            }

            result = WinForms.DialogResult.OK;
            window.Close();
        }

        public async Task<WinForms.DialogResult> ShowDialogAsync()
        {
            if (AppMessageDialogs.GetOwner() is not { } owner)
            {
                return WinForms.DialogResult.Cancel;
            }

            await window.ShowDialog(owner).ConfigureAwait(true);
            return result;
        }

        public void Dispose()
        {
        }
    }
}
