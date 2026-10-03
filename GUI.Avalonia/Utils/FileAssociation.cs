using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace GUI.Utils;

/// <summary>
/// Registers this app as the handler of .vpk files and of "vpk:" links.
/// </summary>
static partial class FileAssociation
{
    public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    public static async Task<bool> RegisterAsync()
    {
        var applicationPath = Environment.ProcessPath;

        if (applicationPath == null)
        {
            Log.Error(nameof(FileAssociation), "Could not determine the application path.");
            return false;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                await RegisterWindowsAsync(applicationPath).ConfigureAwait(true);
            }
            else if (OperatingSystem.IsLinux())
            {
                await RegisterLinuxAsync(applicationPath).ConfigureAwait(true);
            }
            else
            {
                return false;
            }
        }
        catch (Exception e)
        {
            Log.Error(nameof(FileAssociation), $"Failed to register file association: {e}");
            await AppMessageDialogs.ShowMessageAsync(e.Message, "Failed to register file association", MessageIcon.Error).ConfigureAwait(true);
            return false;
        }

        await AppMessageDialogs.ShowMessageAsync(
            $"Registered .vpk file association as well as \"vpk:\" protocol link handling.{Environment.NewLine}{Environment.NewLine}If you move {Path.GetFileName(applicationPath)}, you will have to register it again.",
            "File association registered").ConfigureAwait(true);

        return true;
    }

    [SupportedOSPlatform("windows")]
    private static async Task RegisterWindowsAsync(string applicationPath)
    {
        const string Extension = ".vpk";
        const string ProgId = $"VRF.Source2Viewer{Extension}";

        var vpkIconPath = await ExtractVpkIconAsync("vpk.ico").ConfigureAwait(true);

        using var reg = Registry.CurrentUser.CreateSubKey(@$"Software\Classes\{Extension}\OpenWithProgids");
        reg.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);

        using var reg2 = Registry.CurrentUser.CreateSubKey(@$"Software\Classes\{ProgId}");
        reg2.SetValue(null, "Valve Pak File");

        using var reg3 = reg2.CreateSubKey(@"shell\open\command");
        reg3.SetValue(null, $"\"{applicationPath}\" \"%1\"");

        using var regIco = reg2.CreateSubKey("DefaultIcon");
        regIco.SetValue(null, vpkIconPath);

        using var regProtocol = Registry.CurrentUser.CreateSubKey(@"Software\Classes\vpk");
        regProtocol.SetValue(string.Empty, "URL:Valve Pak protocol");
        regProtocol.SetValue("URL Protocol", string.Empty);

        using var regProtocolOpen = regProtocol.CreateSubKey(@"shell\open\command");
        regProtocolOpen.SetValue(null, $"\"{applicationPath}\" \"%1\"");

        const int SHCNE_ASSOCCHANGED = 0x08000000;
        const uint SHCNF_FLUSH = 0x1000;
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_FLUSH, IntPtr.Zero, IntPtr.Zero);
    }

    [LibraryImport("shell32.dll")]
    [SupportedOSPlatform("windows")]
    private static partial void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    /// <summary>
    /// Follows the freedesktop.org specs: a mime type for *.vpk, a desktop entry handling it and the
    /// vpk: scheme, and both set as the user's default.
    /// </summary>
    [SupportedOSPlatform("linux")]
    private static async Task RegisterLinuxAsync(string applicationPath)
    {
        const string MimeType = "application/x-valve-vpk";
        const string DesktopFile = "source2viewer.desktop";

        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");

        if (string.IsNullOrEmpty(dataHome))
        {
            dataHome = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        var mimePackages = Path.Join(dataHome, "mime", "packages");
        var applications = Path.Join(dataHome, "applications");
        Directory.CreateDirectory(mimePackages);
        Directory.CreateDirectory(applications);

        await File.WriteAllTextAsync(Path.Join(mimePackages, "source2viewer-vpk.xml"), $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <mime-info xmlns="http://www.freedesktop.org/standards/shared-mime-info">
              <mime-type type="{MimeType}">
                <comment>Valve Pak File</comment>
                <glob pattern="*.vpk"/>
              </mime-type>
            </mime-info>
            """).ConfigureAwait(true);

        await File.WriteAllTextAsync(Path.Join(applications, DesktopFile), $"""
            [Desktop Entry]
            Type=Application
            Name=Source 2 Viewer
            Comment=View and extract Source 2 resources
            Exec="{applicationPath}" %u
            Terminal=false
            Categories=Utility;Development;
            MimeType={MimeType};x-scheme-handler/vpk;

            """).ConfigureAwait(true);

        await RunAsync("update-mime-database", Path.Join(dataHome, "mime")).ConfigureAwait(true);
        await RunAsync("update-desktop-database", applications).ConfigureAwait(true);
        await RunAsync("xdg-mime", "default", DesktopFile, MimeType).ConfigureAwait(true);
        await RunAsync("xdg-mime", "default", DesktopFile, "x-scheme-handler/vpk").ConfigureAwait(true);
    }

    private static async Task RunAsync(string fileName, params string[] arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(fileName, arguments) { UseShellExecute = false });

            if (process != null)
            {
                await process.WaitForExitAsync().ConfigureAwait(true);
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The desktop database tools are optional, the files are picked up on next login without them
            Log.Warn(nameof(FileAssociation), $"'{fileName}' is not available, skipped it.");
        }
    }

    private static async Task<string> ExtractVpkIconAsync(string fileName)
    {
        var path = Path.Join(Settings.SettingsFolder, fileName);

        if (!File.Exists(path))
        {
            using var iconStream = Program.Assembly.GetManifestResourceStream("GUI.Utils.vpk.ico")
                ?? throw new InvalidOperationException("The vpk icon is not embedded.");
            using var iconDiskStream = File.Create(path);
            await iconStream.CopyToAsync(iconDiskStream).ConfigureAwait(true);
        }

        return path;
    }
}
