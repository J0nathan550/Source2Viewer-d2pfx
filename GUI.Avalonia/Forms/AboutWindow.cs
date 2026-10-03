using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using GUI.Controls;
using GUI.Types.GLViewers;
using GUI.Utils;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.TextureDecoders;

namespace GUI.Forms
{
    /// <summary>
    /// The About dialog: credits, links and version and update information, laid out like the WinForms AboutForm.
    /// </summary>
    sealed class AboutWindow : Window
    {
        private readonly TextBlock newVersionLabel;
        private readonly Button downloadButton;
        private readonly Button viewReleaseNotesButton;
        private readonly ComboBox updateChannelComboBox;

        public AboutWindow()
        {
            Title = "About";
            Width = 684;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            CanMinimize = false;
            CanMaximize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Icon = Program.MainForm.Icon;

            // Start the decoder thread so that it fetches the opengl version and is ready for the version copy
            if (GLEnvironment.GpuRendererAndDriver == null && HardwareAcceleratedTextureDecoder.Decoder is GLTextureDecoder decoder)
            {
                decoder.StartThread();
            }

            // Credits, laid out like the WinForms AboutForm: the text, then a row of link buttons
            var credits = new TextBlock
            {
                Text = "Source2Viewer - d2pfx is a modded build of Source 2 Viewer, made for the d2pfx community.\n" +
                    "Based on Source 2 Viewer / ValveResourceFormat by its original authors.\n" +
                    "This is open-source software under the MIT license.\n" +
                    "This project is not affiliated with Valve Software. Source 2 is a trademark and/or registered trademark of Valve Corporation.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new(0, 0, 0, 12),
            };

            var links = new UniformGrid { Columns = 4 };
            links.Children.Add(LinkButton("_D2pfx", () => OpenUrl("https://h6rd.github.io/Dota2PornFxWeb/")));
            links.Children.Add(LinkButton("_GitHub", () => OpenUrl("https://github.com/J0nathan550")));
            links.Children.Add(LinkButton("_Discord", () => OpenUrl("https://discord.com/invite/PBvG8D9MxT")));
            links.Children.Add(LinkButton("_Licenses", ShowLicenses));

            var creditsGroup = Controls.GroupBox.Create("Source2Viewer d2pfx", new StackPanel { Children = { credits, links } }, new Avalonia.Thickness(16, 10, 13, 13));

            var logo = AppIcons.Create("Logo", 148);
            logo.Margin = new(6, -4, 0, 0);
            logo.VerticalAlignment = VerticalAlignment.Top;

            var top = new DockPanel();
            DockPanel.SetDock(logo, Dock.Right);
            top.Children.Add(logo);
            top.Children.Add(creditsGroup);

            // Version
            var copyVersion = new Button { Content = "Copy _version", Width = 100, Height = 30, Padding = new(4, 0) };
            copyVersion.Click += (_, _) =>
            {
                CopyVersion();
                copyVersion.Content = "Copied!";
            };

            newVersionLabel = new TextBlock { Text = "version", FontWeight = FontWeight.Bold };

            var versionGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("134,*,Auto"),
                RowDefinitions = new RowDefinitions("30,30"),
            };
            AddCell(versionGrid, new TextBlock { Text = "Current version: " }, 0, 0);
            AddCell(versionGrid, new TextBlock { Text = Program.DisplayVersion, FontWeight = FontWeight.Bold }, 0, 1);
            AddCell(versionGrid, copyVersion, 0, 2);
            AddCell(versionGrid, new TextBlock { Text = "New version: " }, 1, 0);
            AddCell(versionGrid, newVersionLabel, 1, 1);

            downloadButton = new Button { Content = "Download new version", Height = 30, Margin = new(3), HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = false };
            downloadButton.Click += (_, _) => OnDownloadButtonClick();

            viewReleaseNotesButton = new Button { Content = "View release notes", Height = 30, Margin = new(3), HorizontalAlignment = HorizontalAlignment.Stretch };
            viewReleaseNotesButton.Click += (_, _) => OpenUrl(UpdateChecker.ReleaseNotesUrl ?? $"https://github.com/{UpdateChecker.Repository}/releases");

            var buttons = new UniformGrid { Columns = 2, Margin = new(-3, 8, -3, 6) };
            buttons.Children.Add(downloadButton);
            buttons.Children.Add(viewReleaseNotesButton);

            var checkForUpdates = new CheckBox { Content = "Automatically check for updates daily", IsChecked = Settings.Config.Update.CheckAutomatically };
            checkForUpdates.IsCheckedChanged += (_, _) =>
            {
                Settings.Config.Update.CheckAutomatically = checkForUpdates.IsChecked == true;
                Settings.Config.Update.NextCheck = string.Empty;
            };

            updateChannelComboBox = new ComboBox
            {
                ItemsSource = Enum.GetNames<Settings.UpdateChannel>(),
                SelectedIndex = (int)Settings.Config.Update.Channel,
                Width = 188,
                Height = 26,
            };
            updateChannelComboBox.SelectionChanged += (_, _) =>
            {
                if (updateChannelComboBox.SelectedIndex >= 0 && (int)Settings.Config.Update.Channel != updateChannelComboBox.SelectedIndex)
                {
                    Settings.Config.Update.Channel = (Settings.UpdateChannel)updateChannelComboBox.SelectedIndex;
                    CheckForUpdates();
                }
            };

            var updateRow = new DockPanel();
            var channel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { new TextBlock { Text = "Update channel:", VerticalAlignment = VerticalAlignment.Center }, updateChannelComboBox },
            };
            DockPanel.SetDock(channel, Dock.Right);
            updateRow.Children.Add(channel);
            updateRow.Children.Add(checkForUpdates);

            var versionGroup = Controls.GroupBox.Create("Version", new StackPanel { Children = { versionGrid, buttons, updateRow } }, new Avalonia.Thickness(16, 4, 16, 12));

            Content = new StackPanel
            {
                Margin = new(16),
                Spacing = 12,
                Children = { top, versionGroup },
            };

            Closed += (_, _) => Settings.Save();

            CheckForUpdates();
        }

        private static void AddCell(Grid grid, Control control, int row, int column)
        {
            Grid.SetRow(control, row);
            Grid.SetColumn(control, column);
            control.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(control);
        }

        private static Button LinkButton(string text, Action onClick)
        {
            var button = new Button { Content = text, Height = 30, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new(3) };
            button.Click += (_, _) => onClick();
            return button;
        }

        private async void CheckForUpdates()
        {
            newVersionLabel.Text = "Checking for updates\u2026";
            downloadButton.IsEnabled = false;

            try
            {
                await UpdateChecker.CheckForUpdates().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                var message = $"Failed to check for updates: {ex.Message}";
                Log.Error(nameof(AboutWindow), message);
                newVersionLabel.Text = message;
                return;
            }

            OnUpdateChecked();
        }

        private void OnUpdateChecked()
        {
            var installed = UpdateInstaller.InstalledVersionText;
            var newVersion = UpdateChecker.NewVersionText;

            if (installed != null)
            {
                newVersionLabel.Text = $"{installed} (installed)";
                downloadButton.Content = "Restart to update";
                downloadButton.IsEnabled = true;
            }
            else if (UpdateChecker.IsNewVersionAvailable)
            {
                newVersionLabel.Text = newVersion;
                downloadButton.Content = UpdateChecker.IsNewer
                    ? $"Download {newVersion}"
                    : $"Switch to {(UpdateChecker.IsNewVersionStableBuild ? "stable " : "")}{newVersion}";
                downloadButton.IsEnabled = true;
            }
            else
            {
                newVersionLabel.Text = newVersion;
                downloadButton.Content = UpdateChecker.NewVersion == null ? "Not available" : "Up to date";
                downloadButton.IsEnabled = false;
            }

            // Switching channels would not change what the pending restart installs
            updateChannelComboBox.IsEnabled = installed == null;

            if (!string.IsNullOrEmpty(UpdateChecker.ReleaseNotesUrl))
            {
                viewReleaseNotesButton.Content = $"View release notes for {UpdateChecker.ReleaseNotesVersion}";
            }
        }

        private async void OnDownloadButtonClick()
        {
            if (UpdateInstaller.InstalledVersionText != null)
            {
                UpdateInstaller.Restart();
                return;
            }

            // Builds that can not replace themselves, and platforms without a download, get the release page instead
            if (!UpdateInstaller.CanInstall || UpdateChecker.DownloadUrl == null)
            {
                OpenUrl(UpdateChecker.ReleaseNotesUrl ?? $"https://github.com/{UpdateChecker.Repository}/releases");
                return;
            }

            downloadButton.IsEnabled = false;

            try
            {
                await UpdateInstaller.InstallAsync(this).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Program.ShowError(ex);
            }
            finally
            {
                OnUpdateChecked();
            }
        }

        private async void ShowLicenses()
        {
            using var stream = Program.Assembly.GetManifestResourceStream("GUI.Utils.THIRD_PARTY_NOTICES.txt");

            if (stream == null)
            {
                return;
            }

            using var reader = new StreamReader(stream);

            var window = new Window
            {
                Title = "Third party licenses",
                Icon = Icon,
                Width = 800,
                Height = 600,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new CodeTextBox(await reader.ReadToEndAsync().ConfigureAwait(true), HighlightLanguage.None),
            };

            await window.ShowDialog(this).ConfigureAwait(true);
        }

        internal static void OpenUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new ArgumentException($"Refusing to open \"{url}\".", nameof(url));
            }

            Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true,
            });
        }

        private static void CopyVersion()
        {
            var output = new StringBuilder(192);
            output.Append(Program.DisplayVersion);
            output.Append(CultureInfo.InvariantCulture, $" on {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");

            if (GLEnvironment.GpuRendererAndDriver != null)
            {
                output.Append(CultureInfo.InvariantCulture, $" ({GLEnvironment.GpuRendererAndDriver})");
            }

            AppClipboard.SetText(output.ToString());
        }
    }
}
