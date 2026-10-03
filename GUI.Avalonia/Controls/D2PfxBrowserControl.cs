using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using GUI.Forms;
using GUI.Utils;

namespace GUI.Controls;

/// <summary>
/// The dota2prnfx mod site in a tab, like the WinForms D2PfxBrowserControl. Downloads are saved to
/// Documents/Source2Viewer/VPK, archives are unpacked there, and the downloaded package can be opened right away.
/// </summary>
sealed class D2PfxBrowserControl : Border, IDisposable
{
    public const string TargetUrl = "https://h6rd.github.io/Dota2PornFxWeb/";

    // Blob downloads arrive in pieces this large, a multiple of 3 so every piece is whole base64
    private const int BlobChunkSize = 3 * 256 * 1024;

    // The site starts downloads by clicking a link with a download attribute, often one that is not in the page,
    // so link clicks are caught before the browser handles them. Files on the web are fetched by the app,
    // files the page builds itself (blob: links) are read here and sent over in pieces.
    private const string DownloadScript = """
        (function () {
            if (window.__s2vDownloads || typeof invokeCSharpAction !== 'function') {
                return;
            }

            window.__s2vDownloads = true;

            var blobs = new Map();
            var createObjectURL = URL.createObjectURL;
            var revokeObjectURL = URL.revokeObjectURL;

            URL.createObjectURL = function (obj) {
                var url = createObjectURL.call(URL, obj);

                if (obj instanceof Blob) {
                    blobs.set(url, obj);
                }

                return url;
            };

            URL.revokeObjectURL = function (url) {
                blobs.delete(url);
                return revokeObjectURL.call(URL, url);
            };

            function post(message) {
                invokeCSharpAction(JSON.stringify(message));
            }

            function sendBlob(blob, name) {
                var id = Date.now().toString(36) + Math.random().toString(36).slice(2);
                var offset = 0;

                post({ type: 'blob-start', id: id, name: name });

                function next() {
                    if (offset >= blob.size) {
                        post({ type: 'blob-end', id: id });
                        return;
                    }

                    var reader = new FileReader();
                    reader.onload = function () {
                        var data = reader.result;
                        post({ type: 'blob-chunk', id: id, data: data.substring(data.indexOf(',') + 1) });
                        offset += CHUNK_SIZE;
                        next();
                    };
                    reader.onerror = function () {
                        post({ type: 'blob-error', id: id });
                    };
                    reader.readAsDataURL(blob.slice(offset, offset + CHUNK_SIZE));
                }

                next();
            }

            function handleDownloadLink(link) {
                if (!link || !link.hasAttribute('download') || !link.href) {
                    return false;
                }

                var name = link.getAttribute('download') || '';

                if (link.href.indexOf('blob:') === 0) {
                    var blob = blobs.get(link.href);

                    if (!blob) {
                        return false;
                    }

                    sendBlob(blob, name);
                    return true;
                }

                if (link.href.indexOf('https:') === 0 || link.href.indexOf('http:') === 0) {
                    post({ type: 'download', url: link.href, name: name });
                    return true;
                }

                return false;
            }

            var anchorClick = HTMLAnchorElement.prototype.click;
            HTMLAnchorElement.prototype.click = function () {
                if (!handleDownloadLink(this)) {
                    return anchorClick.call(this);
                }
            };

            document.addEventListener('click', function (e) {
                var link = e.target && e.target.closest ? e.target.closest('a[download]') : null;

                if (handleDownloadLink(link)) {
                    e.preventDefault();
                }
            }, true);
        })();
        """;

    private static readonly string DownloadScriptSource = DownloadScript.Replace("CHUNK_SIZE", BlobChunkSize.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

    // Same dark color as the site, so loading does not flash white
    private static readonly ImmutableSolidColorBrush PageBackground = new(Color.FromRgb(24, 24, 24));

    private static readonly HttpClient DownloadClient = CreateDownloadClient();

    private readonly NativeWebView? webView;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Dictionary<string, BlobDownload> blobDownloads = [];
    private bool isDisposed;

    /// <summary>A file the page built itself, written as its pieces arrive.</summary>
    private sealed class BlobDownload(string path) : IDisposable
    {
        public string Path { get; } = path;

        public FileStream Stream { get; } = File.Create(path);

        public void Dispose() => Stream.Dispose();
    }

    public D2PfxBrowserControl()
    {
        Background = PageBackground;

        if (!IsWebViewAvailable(out var reason))
        {
            Log.Warn(nameof(D2PfxBrowserControl), $"The web view is not available: {reason}");
            Child = CreateUnavailablePanel(reason);
            return;
        }

        webView = new NativeWebView
        {
            Background = PageBackground,
            Source = new Uri(TargetUrl),
        };
        webView.EnvironmentRequested += OnEnvironmentRequested;
        webView.NavigationStarted += OnNavigationStarted;
        webView.NavigationCompleted += OnNavigationCompleted;
        webView.NewWindowRequested += OnNewWindowRequested;
        webView.WebMessageReceived += OnWebMessageReceived;

        Child = webView;
    }

    private static bool IsWebViewAvailable(out string reason)
    {
        WebViewAdapterType[] adapters = [];

        if (OperatingSystem.IsWindows())
        {
            adapters = [WebViewAdapterType.WebView2, WebViewAdapterType.WebView1];
        }
        else if (OperatingSystem.IsMacOS())
        {
            adapters = [WebViewAdapterType.WkWebView];
        }
        else if (OperatingSystem.IsLinux())
        {
            adapters = [WebViewAdapterType.WpeWebKit, WebViewAdapterType.WebKitGtk];
        }

        reason = "This platform has no supported web view.";

        foreach (var adapter in adapters)
        {
            var info = WebViewAdapterInfo.GetAdapterInfo(adapter);

            if (info.IsSupported && info.IsInstalled)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(info.UnavailableReason))
            {
                reason = info.UnavailableReason;
            }
        }

        return false;
    }

    private static StackPanel CreateUnavailablePanel(string reason)
    {
        var openButton = new Button
        {
            Content = AppIcons.CreateHeader("dota2pornfx", "Open in browser", 16),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        openButton.Click += (_, _) => AboutWindow.OpenUrl(TargetUrl);

        return new StackPanel
        {
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = "The dota2prnfx site can not be shown here.",
                    FontSize = 16,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
                new TextBlock
                {
                    Text = reason,
                    Foreground = Brushes.LightGray,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 520,
                    TextAlignment = TextAlignment.Center,
                },
                openButton,
            },
        };
    }

    private static HttpClient CreateDownloadClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Source2Viewer");
        return client;
    }

    private static string GetTargetDirectory()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Source2Viewer",
            "VPK");

        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string GetDownloadPath(string? suggestedName, Uri? source)
    {
        var name = Path.GetFileName(suggestedName ?? string.Empty);

        if (string.IsNullOrWhiteSpace(name) && source != null)
        {
            name = Path.GetFileName(Uri.UnescapeDataString(source.AbsolutePath));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = "download";
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return Path.Combine(GetTargetDirectory(), name);
    }

    private static bool IsDownloadUrl(Uri uri)
    {
        var path = uri.AbsolutePath;
        return path.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    }

    private void OnEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs e)
    {
        // Logins and site settings are kept between runs, in the same profile the WinForms GUI uses
        var profileFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Source2Viewer");

        switch (e)
        {
            case WindowsWebView2EnvironmentRequestedEventArgs webView2:
                webView2.UserDataFolder = Path.Combine(profileFolder, "WebView2Profile");
                Directory.CreateDirectory(webView2.UserDataFolder);
                break;

            case GtkWebViewEnvironmentRequestedEventArgs gtk:
                gtk.BaseDataDirectory = Path.Combine(profileFolder, "WebKitProfile");
                gtk.BaseCacheDirectory = Path.Combine(profileFolder, "WebKitCache");
                break;

            case LinuxWpeWebViewEnvironmentRequestedEventArgs wpe:
                wpe.DataDirectory = Path.Combine(profileFolder, "WebKitProfile");
                wpe.CacheDirectory = Path.Combine(profileFolder, "WebKitCache");
                break;
        }
    }

    private void OnNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
    {
        // Packages linked directly are downloaded instead of shown
        if (e.Request is { } uri && IsDownloadUrl(uri))
        {
            e.Cancel = true;
            _ = DownloadAsync(uri, null);
        }
    }

    private async void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            Log.Error(nameof(D2PfxBrowserControl), $"Failed to load {e.Request}.");
            return;
        }

        Log.Info(nameof(D2PfxBrowserControl), $"Loaded {e.Request}.");

        if (webView == null || isDisposed)
        {
            return;
        }

        try
        {
            await webView.InvokeScript(DownloadScriptSource).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warn(nameof(D2PfxBrowserControl), $"Downloads will not be opened in the app, failed to set up the page: {ex.Message}");
        }
    }

    private void OnNewWindowRequested(object? sender, WebViewNewWindowRequestedEventArgs e)
    {
        e.Handled = true;

        if (e.Request is not { } uri)
        {
            return;
        }

        if (IsDownloadUrl(uri))
        {
            _ = DownloadAsync(uri, null);
            return;
        }

        // Links that open a new window (guides, Discord and so on) go to the default browser
        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            AboutWindow.OpenUrl(uri.AbsoluteUri);
        }
        else
        {
            Log.Warn(nameof(D2PfxBrowserControl), $"Not opening {uri}.");
        }
    }

    private void OnWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
    {
        if (isDisposed || e.Body is not { Length: > 0 } body)
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var message = document.RootElement;
            var type = message.GetProperty("type").GetString();

            switch (type)
            {
                case "download":
                {
                    var url = message.GetProperty("url").GetString();
                    var name = message.GetProperty("name").GetString();

                    if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
                    {
                        _ = DownloadAsync(uri, name);
                    }

                    break;
                }

                case "blob-start":
                {
                    var id = message.GetProperty("id").GetString() ?? string.Empty;
                    var path = GetDownloadPath(message.GetProperty("name").GetString(), null);

                    Log.Info(nameof(D2PfxBrowserControl), $"Saving {Path.GetFileName(path)} to {path}");
                    Program.MainForm.SetStatus($"Downloading {Path.GetFileName(path)}\u2026");

#pragma warning disable CA2000 // Disposed when the download ends or fails, or with this control
                    blobDownloads[id] = new BlobDownload(path);
#pragma warning restore CA2000
                    break;
                }

                case "blob-chunk":
                {
                    var id = message.GetProperty("id").GetString() ?? string.Empty;

                    if (blobDownloads.TryGetValue(id, out var download))
                    {
                        download.Stream.Write(Convert.FromBase64String(message.GetProperty("data").GetString() ?? string.Empty));
                    }

                    break;
                }

                case "blob-end":
                {
                    var id = message.GetProperty("id").GetString() ?? string.Empty;

                    if (blobDownloads.Remove(id, out var download))
                    {
                        download.Dispose();
                        Program.MainForm.ShowSelectedTabStatus();
                        _ = HandleCompletedDownloadAsync(download.Path);
                    }

                    break;
                }

                case "blob-error":
                {
                    var id = message.GetProperty("id").GetString() ?? string.Empty;

                    if (blobDownloads.Remove(id, out var download))
                    {
                        download.Dispose();
                        TryDelete(download.Path);
                        Program.MainForm.ShowSelectedTabStatus();
                        Log.Error(nameof(D2PfxBrowserControl), $"Failed to save {Path.GetFileName(download.Path)}.");
                    }

                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(nameof(D2PfxBrowserControl), $"Failed to handle a message from the page: {ex.Message}");
        }
    }

    private async Task DownloadAsync(Uri uri, string? suggestedName)
    {
        if (isDisposed)
        {
            return;
        }

        var path = GetDownloadPath(suggestedName, uri);
        var token = cancellation.Token;

        Log.Info(nameof(D2PfxBrowserControl), $"Downloading {uri} to {path}");
        Program.MainForm.SetStatus($"Downloading {Path.GetFileName(path)}\u2026");

        try
        {
            using var response = await DownloadClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(true);
            response.EnsureSuccessStatusCode();

            using (var file = File.Create(path))
            {
                await response.Content.CopyToAsync(file, token).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            TryDelete(path);

            if (!token.IsCancellationRequested)
            {
                Log.Error(nameof(D2PfxBrowserControl), $"Failed to download {uri}: {ex.Message}");
                Program.MainForm.ShowSelectedTabStatus();
            }

            return;
        }

        Program.MainForm.ShowSelectedTabStatus();
        await HandleCompletedDownloadAsync(path).ConfigureAwait(true);
    }

    private static async Task HandleCompletedDownloadAsync(string downloadedFilePath)
    {
        var fileToOpen = downloadedFilePath;

        // Archives are unpacked next to them and removed, the package inside is what gets opened
        if (downloadedFilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var extractDirectory = Path.Combine(GetTargetDirectory(), Path.GetFileNameWithoutExtension(downloadedFilePath));

                Directory.CreateDirectory(extractDirectory);
                await ZipFile.ExtractToDirectoryAsync(downloadedFilePath, extractDirectory, overwriteFiles: true, CancellationToken.None).ConfigureAwait(true);

                TryDelete(downloadedFilePath);

                var packages = Directory.GetFiles(extractDirectory, "*.vpk", SearchOption.AllDirectories);
                fileToOpen = packages.FirstOrDefault(static path => path.EndsWith("_dir.vpk", StringComparison.OrdinalIgnoreCase))
                    ?? packages.FirstOrDefault()
                    ?? extractDirectory;

                Log.Info(nameof(D2PfxBrowserControl), $"Unpacked to {extractDirectory}");
            }
            catch (Exception ex)
            {
                Log.Error(nameof(D2PfxBrowserControl), $"Failed to unpack {downloadedFilePath}: {ex.Message}");
            }
        }
        else
        {
            Log.Info(nameof(D2PfxBrowserControl), $"Downloaded {downloadedFilePath}");
        }

        if (!File.Exists(fileToOpen))
        {
            // Nothing that can be opened, at least show where it went
            ExplorerControl.RevealInFileManager(fileToOpen);
            return;
        }

        if (await ShouldOpenFileAsync(fileToOpen).ConfigureAwait(true))
        {
            try
            {
                Program.MainForm.OpenFile(fileToOpen);
            }
            catch (Exception ex)
            {
                Log.Error(nameof(D2PfxBrowserControl), $"Failed to open {fileToOpen}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Asks whether to open a downloaded file, unless the user picked an answer to remember.
    /// </summary>
    private static async Task<bool> ShouldOpenFileAsync(string filePath)
    {
        switch (Settings.Config.D2PfxAutoOpenAction)
        {
            case 1:
                return true;
            case 2:
                return false;
        }

        if (AppMessageDialogs.GetOwner() is not { } owner)
        {
            return false;
        }

        var open = false;

        var window = new Window
        {
            Title = "Download complete",
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 420,
            MaxWidth = 640,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        var pathLink = new Button
        {
            Content = new TextBlock
            {
                Text = filePath,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Themer.GetBrush(Themer.CurrentThemeColors.Accent),
            },
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = new Cursor(StandardCursorType.Hand),
            Padding = new(0, 2),
        };
        pathLink.Classes.Add("flat");
        ToolTip.SetTip(pathLink, filePath);
        pathLink.Click += (_, _) => ExplorerControl.RevealInFileManager(filePath);

        var dontAskCheckBox = new CheckBox { Content = "Don't ask again" };

        var openButton = new Button { Content = "Open", MinWidth = 90, IsDefault = true, HorizontalContentAlignment = HorizontalAlignment.Center };
        openButton.Click += (_, _) =>
        {
            open = true;
            window.Close();
        };

        var cancelButton = new Button { Content = "Cancel", MinWidth = 90, IsCancel = true, HorizontalContentAlignment = HorizontalAlignment.Center };
        cancelButton.Click += (_, _) => window.Close();

        window.Content = new StackPanel
        {
            Margin = new(16),
            Spacing = 6,
            Children =
            {
                new TextBlock
                {
                    Text = $"Downloaded {Path.GetFileName(filePath)}",
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = "Saved to (click to show it in its folder):",
                    Foreground = Themer.GetBrush(Themer.CurrentThemeColors.ContrastSoft),
                    Margin = new(0, 6, 0, 0),
                },
                pathLink,
                dontAskCheckBox,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Margin = new(0, 10, 0, 0),
                    Children = { openButton, cancelButton },
                },
            },
        };

        await window.ShowDialog(owner).ConfigureAwait(true);

        if (dontAskCheckBox.IsChecked == true)
        {
            Settings.Config.D2PfxAutoOpenAction = open ? 1 : 2;
            Settings.Save();
        }

        return open;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn(nameof(D2PfxBrowserControl), $"Failed to delete {path}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;

        cancellation.Cancel();
        cancellation.Dispose();

        foreach (var download in blobDownloads.Values)
        {
            download.Dispose();
            TryDelete(download.Path);
        }

        blobDownloads.Clear();
    }
}
