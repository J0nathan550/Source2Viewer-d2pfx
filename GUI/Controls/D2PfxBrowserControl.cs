using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace GUI.Controls
{
    public class D2PfxBrowserControl : UserControl
    {
        private readonly WebView2 webView;
        private const string TargetUrl = "https://h6rd.github.io/Dota2PornFxWeb/";
        private bool isInitialized;

        public D2PfxBrowserControl()
        {
            Dock = DockStyle.Fill;
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            BackColor = Color.FromArgb(24, 24, 24);

            webView = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = Color.FromArgb(24, 24, 24),
            };

            Controls.Add(webView);
        }

        /// <summary>
        /// Загружает векторную SVG иконку dota2pornfx, уменьшает её и центрирует с отступами.
        /// </summary>
        public static Bitmap? GetTabIcon(int width, int height)
        {
            try
            {
                using var svg = new Svg.Skia.SKSvg();
                using var stream = Program.Assembly.GetManifestResourceStream("GUI.Icons.dota2pornfx.svg");
                if (stream != null)
                {
                    svg.Load(stream);

                    const float scale = 0.72f;
                    var iconW = Math.Max(1, (int)(width * scale));
                    var iconH = Math.Max(1, (int)(height * scale));

                    using var smallBitmap = Utils.Themer.SvgToBitmap(svg, iconW, iconH);
                    var finalBitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(finalBitmap))
                    {
                        g.Clear(Color.Transparent);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = SmoothingMode.HighQuality;

                        var x = (width - iconW) / 2;
                        var y = (height - iconH) / 2;
                        g.DrawImage(smallBitmap, x, y, iconW, iconH);
                    }

                    return finalBitmap;
                }
            }
            catch
            {
            }

            return null;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            if (!isInitialized)
            {
                isInitialized = true;
                _ = InitializeWebViewAsync();
            }
        }

        private async Task InitializeWebViewAsync()
        {
            try
            {
                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Source2Viewer",
                    "WebView2Profile");

                Directory.CreateDirectory(userDataFolder);

                var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder).ConfigureAwait(true);
                await webView.EnsureCoreWebView2Async(environment).ConfigureAwait(true);

                webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                webView.CoreWebView2.Settings.IsWebMessageEnabled = true;
                webView.CoreWebView2.Settings.IsScriptEnabled = true;

                webView.CoreWebView2.NavigationCompleted += (s, e) =>
                {
                    if (!e.IsSuccess)
                    {
                        Utils.Log.Error(nameof(D2PfxBrowserControl), $"Ошибка загрузки страницы: {e.WebErrorStatus} (код {e.HttpStatusCode})");
                    }
                    else
                    {
                        Utils.Log.Info(nameof(D2PfxBrowserControl), "Сайт Dota2PornFx успешно загружен.");
                    }
                };

                webView.CoreWebView2.DownloadStarting += OnDownloadStarting;
                webView.CoreWebView2.Navigate(TargetUrl);
            }
            catch (Exception ex)
            {
                Utils.Log.Error(nameof(D2PfxBrowserControl), $"Сбой инициализации WebView2: {ex.Message}");
            }
        }

        private static string GetTargetVpkDirectory()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Source2Viewer",
                "VPK");

            Directory.CreateDirectory(dir);
            return dir;
        }

        private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
        {
            var targetDir = GetTargetVpkDirectory();
            var fileName = Path.GetFileName(e.ResultFilePath);
            e.ResultFilePath = Path.Combine(targetDir, fileName);

            var downloadOperation = e.DownloadOperation;
            downloadOperation.StateChanged += OnDownloadStateChanged;
        }

        private void OnDownloadStateChanged(object? sender, object e)
        {
            if (sender is not CoreWebView2DownloadOperation download || download.State != CoreWebView2DownloadState.Completed)
            {
                return;
            }

            var downloadedFilePath = download.ResultFilePath;
            if (!File.Exists(downloadedFilePath))
            {
                return;
            }

            _ = HandleCompletedDownloadAsync(downloadedFilePath);
        }

        private async Task HandleCompletedDownloadAsync(string downloadedFilePath)
        {
            string fileToOpen = downloadedFilePath;

            // Если скачан архив .zip — распаковываем его и удаляем сам zip
            if (downloadedFilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var vpkBaseDir = GetTargetVpkDirectory();
                    var folderName = Path.GetFileNameWithoutExtension(downloadedFilePath);
                    var extractDir = Path.Combine(vpkBaseDir, folderName);

                    Directory.CreateDirectory(extractDir);
                    await ZipFile.ExtractToDirectoryAsync(downloadedFilePath, extractDir, overwriteFiles: true, CancellationToken.None).ConfigureAwait(true);

                    // Удаляем исходный архив после успешной распаковки
                    try
                    {
                        File.Delete(downloadedFilePath);
                    }
                    catch
                    {
                    }

                    // Ищем .vpk внутри распакованной папки
                    var vpkFiles = Directory.GetFiles(extractDir, "*.vpk", SearchOption.AllDirectories);
                    fileToOpen = vpkFiles.Length > 0 ? vpkFiles[0] : extractDir;

                    Utils.Log.Info(nameof(D2PfxBrowserControl), $"Архив распакован в: {extractDir}");
                }
                catch (Exception ex)
                {
                    Utils.Log.Error(nameof(D2PfxBrowserControl), $"Ошибка распаковки архива: {ex.Message}");
                }
            }

            await Task.Delay(100).ConfigureAwait(true);

            if (ShouldOpenFile(fileToOpen))
            {
                await Program.MainForm.InvokeAsync(() =>
                {
                    try
                    {
                        Program.MainForm.OpenFile(fileToOpen);
                    }
                    catch (Exception ex)
                    {
                        Utils.Log.Error(nameof(D2PfxBrowserControl), $"Не удалось открыть файл: {ex.Message}");
                    }
                }, CancellationToken.None).ConfigureAwait(true);
            }
        }

        private static bool ShouldOpenFile(string filePath)
        {
            int pref = GetAutoOpenPreference();
            if (pref == 1) return true;
            if (pref == 2) return false;

            var fileName = Path.GetFileName(filePath);

            using var promptForm = new ThemedForm
            {
                Text = "Source2Viewer — Загрузка завершена",
                ClientSize = new Size(540, 215),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false
            };

            // Вопрос без двойных переносов
            var label = new Label
            {
                Text = $"Файл успешно скачан:\n{fileName}",
                Location = new Point(16, 16),
                Size = new Size(505, 45),
                AutoEllipsis = true
            };

            var pathTitleLabel = new Label
            {
                Text = "Файл сохранён по пути (нажмите, чтобы показать в папке):",
                Location = new Point(16, 68),
                AutoSize = true,
                ForeColor = Color.Gray
            };

            // Кликабельная ссылка для открытия проводника
            var pathLink = new LinkLabel
            {
                Text = filePath,
                Location = new Point(16, 88),
                Size = new Size(505, 30),
                AutoEllipsis = true,
                LinkColor = Color.FromArgb(99, 161, 255),
                ActiveLinkColor = Color.FromArgb(140, 190, 255),
                VisitedLinkColor = Color.FromArgb(99, 161, 255),
                Cursor = Cursors.Hand
            };

            using var linkTip = new ToolTip();
            linkTip.SetToolTip(pathLink, filePath);

            pathLink.LinkClicked += (s, e) =>
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        Process.Start("explorer.exe", $"/select,\"{filePath}\"");
                    }
                    else
                    {
                        var dir = Directory.Exists(filePath) ? filePath : Path.GetDirectoryName(filePath);
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        {
                            Process.Start("explorer.exe", $"\"{dir}\"");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Utils.Log.Error(nameof(D2PfxBrowserControl), $"Не удалось открыть папку: {ex.Message}");
                }
            };

            var dontAskCheckBox = new CheckBox
            {
                Text = "Don't ask again (запомнить выбор)",
                Location = new Point(16, 130),
                AutoSize = true
            };

            var yesButton = new Button
            {
                Text = "Открыть",
                DialogResult = DialogResult.Yes,
                Location = new Point(330, 170),
                Width = 90,
                Height = 28
            };

            var noButton = new Button
            {
                Text = "Отмена",
                DialogResult = DialogResult.No,
                Location = new Point(430, 170),
                Width = 90,
                Height = 28
            };

            promptForm.Controls.Add(label);
            promptForm.Controls.Add(pathTitleLabel);
            promptForm.Controls.Add(pathLink);
            promptForm.Controls.Add(dontAskCheckBox);
            promptForm.Controls.Add(yesButton);
            promptForm.Controls.Add(noButton);
            promptForm.AcceptButton = yesButton;
            promptForm.CancelButton = noButton;

            var result = promptForm.ShowDialog(Program.MainForm) == DialogResult.Yes;

            if (dontAskCheckBox.Checked)
            {
                SaveAutoOpenPreference(result ? 1 : 2);
            }

            return result;
        }

        private static int GetAutoOpenPreference()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Source2Viewer");
                if (key?.GetValue("D2PfxAutoOpenAction") is int val)
                {
                    return val;
                }
            }
            catch
            {
            }
            return 0;
        }

        private static void SaveAutoOpenPreference(int val)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Source2Viewer");
                key?.SetValue("D2PfxAutoOpenAction", val, Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch
            {
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                webView?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
