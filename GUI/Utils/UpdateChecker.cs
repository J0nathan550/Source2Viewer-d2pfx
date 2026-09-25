//#define TEST_NON_LOCAL_BUILD // Pretend to have been built on a CI as a dev version

using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace GUI.Utils;

static partial class UpdateChecker
{
    /// <summary>
    /// A downloadable file for one runtime identifier.
    /// </summary>
    public class UpdateAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("size")]
        public long? Size { get; set; }

        [JsonPropertyName("sha256")]
        public string? Sha256 { get; set; }
    }

    /// <summary>
    /// The latest tagged release.
    /// </summary>
    public class StableUpdate
    {
        [JsonPropertyName("version")]
        public string? Version { get; set; }

        /// <summary>The git tag the release was built from, which its build provenance is attested on.</summary>
        [JsonPropertyName("tag")]
        public string? Tag { get; set; }

        [JsonPropertyName("releaseNotesUrl")]
        public string? ReleaseNotesUrl { get; set; }

        [JsonPropertyName("assets")]
        public Dictionary<string, UpdateAsset>? Assets { get; set; }
    }

    /// <summary>
    /// The latest automated build of the master branch.
    /// </summary>
    public class DevUpdate
    {
        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("buildNumber")]
        public int BuildNumber { get; set; }

        [JsonPropertyName("assets")]
        public Dictionary<string, UpdateAsset>? Assets { get; set; }
    }

    /// <summary>
    /// The update manifest describing every channel we can offer.
    /// </summary>
    public class UpdateManifest
    {
        [JsonPropertyName("stable")]
        public StableUpdate? Stable { get; set; }

        [JsonPropertyName("dev")]
        public DevUpdate? Dev { get; set; }
    }

    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(UpdateManifest))]
    partial class SourceGenerationContext : JsonSerializerContext
    {
    }

    /// <summary>The GitHub repository that builds, releases and attests the updates.</summary>
    public const string Repository = "J0nathan550/Source2Viewer-d2pfx";
    public const string RepositoryId = "1379849563";

    // Every release carries the manifest for both channels, so the newest release always has the current one
    private const string ManifestUrl = $"https://github.com/{Repository}/releases/latest/download/latest.json";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);
    private static readonly TimeSpan FailedCheckRetryDelay = TimeSpan.FromMinutes(30);

    // The manifest describes both channels, so it is fetched once and re-evaluated when the channel changes
    private static Task<UpdateManifest?>? ManifestTask;
    private static readonly Lock CheckLock = new();
    /// <summary>Whether the selected channel offers a build to install, either newer or a switch to that channel.</summary>
    public static bool IsNewVersionAvailable { get; private set; }
    /// <summary>Whether the offered build is newer than the running one.</summary>
    public static bool IsNewer { get; private set; }
    public static bool IsNewVersionStableBuild { get; private set; }
    public static string? NewVersion { get; private set; }
    /// <summary>The offered version as shown to the user, e.g. "1.1.0.0" or "dev build 1.0.1.1".</summary>
    public static string? NewVersionText { get; private set; }
    public static string? ReleaseNotesUrl { get; private set; }
    public static string? ReleaseNotesVersion { get; private set; }
    public static string? DownloadUrl { get; private set; }
    public static long? DownloadSize { get; private set; }
    public static string? DownloadSha256 { get; private set; }
    /// <summary>The git ref whose workflow run must have attested the offered build.</summary>
    public static string? ProvenanceRef { get; private set; }

    /// <summary>
    /// The newest dev build known from this session's check, if it is newer than the running build, regardless of the selected channel.
    /// Never performs a request, so it is safe to consult from error handlers.
    /// </summary>
    public static Version? NewerDevBuild
    {
        get
        {
            Task<UpdateManifest?>? manifestTask;

            using (CheckLock.EnterScope())
            {
                manifestTask = ManifestTask;
            }

            if (manifestTask is not { IsCompletedSuccessfully: true })
            {
                return null;
            }

            if (IsLocalBuild())
            {
                return null;
            }

            var dev = manifestTask.Result?.Dev;

            return Version.TryParse(dev?.Version, out var devVersion) && devVersion > GetCurrentVersion() ? devVersion : null;
        }
    }

    public static async Task CheckForUpdates()
    {
        Task<UpdateManifest?> manifestTask;

        using (CheckLock.EnterScope())
        {
            // Offloaded so that the request setup does not run on the ui thread
            manifestTask = ManifestTask ??= Task.Run(GetManifestAsync);
        }

        UpdateManifest? manifest;

        try
        {
            manifest = await manifestTask.ConfigureAwait(false);
        }
        catch
        {
            // Let the next check retry instead of remembering the failure for the rest of the session
            using (CheckLock.EnterScope())
            {
                if (ManifestTask == manifestTask)
                {
                    ManifestTask = null;
                }
            }

            throw;
        }

        Evaluate(manifest, Settings.Config.Update.Channel);
    }

    /// <summary>
    /// Performs the automatic update check if it is enabled and due. The outcome is persisted in the settings.
    /// </summary>
    public static async Task CheckForUpdatesIfNecessary()
    {
        if (!Settings.Config.Update.CheckAutomatically || Settings.Config.Update.UpdateAvailable)
        {
            return;
        }

        var now = DateTime.UtcNow;

        if (DateTime.TryParseExact(Settings.Config.Update.NextCheck, "s", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var nextCheck) && now < nextCheck)
        {
            return;
        }

        try
        {
            await CheckForUpdates().ConfigureAwait(false);

            Settings.Config.Update.NextCheck = (now + CheckInterval).ToString("s", CultureInfo.InvariantCulture);
        }
        catch (Exception e)
        {
            // Being offline should not interrupt startup, a manual check from the About dialog reports its errors
            Log.Error(nameof(UpdateChecker), $"Failed to check for updates: {e.Message}");
            Settings.Config.Update.NextCheck = (now + FailedCheckRetryDelay).ToString("s", CultureInfo.InvariantCulture);
        }
    }

    private static Version GetCurrentVersion()
    {
        var version = Program.ProductVersion;
        var versionPlus = version.IndexOf('+', StringComparison.InvariantCulture); // Drop the git commit

        return new Version(versionPlus > 0 ? version[..versionPlus] : version);
    }

    private static bool IsLocalBuild()
    {
#if TEST_NON_LOCAL_BUILD || CI_BUILD
        return false;
#else
        return true;
#endif
    }

    private static void Evaluate(UpdateManifest? manifest, Settings.UpdateChannel channel)
    {
        var currentVersion = GetCurrentVersion();

        if (IsLocalBuild())
        {
            Settings.Config.Update.UpdateAvailable = false;
            IsNewVersionAvailable = false;
            IsNewVersionStableBuild = true;
            NewVersion = ":)";
            NewVersionText = NewVersion;
            return; // This was not built on the CI
        }

        if (manifest == null)
        {
            return;
        }

        var stable = manifest.Stable;
        var dev = manifest.Dev;

        // Release notes are always for the stable release, no matter which channel is selected
        ReleaseNotesUrl = stable?.ReleaseNotesUrl;
        ReleaseNotesVersion = stable?.Version;

        IsNewVersionStableBuild = channel == Settings.UpdateChannel.Stable;
        IsNewer = false;
        NewVersion = null;
        ProvenanceRef = null;
        Dictionary<string, UpdateAsset>? assets = null;

        if (IsNewVersionStableBuild)
        {
            if (stable is { Version.Length: > 0 })
            {
                NewVersion = stable.Version;
                var releaseVersion = Version.TryParse(NewVersion, out var parsed) ? parsed : new Version(0, 0);
                IsNewer = releaseVersion > currentVersion;
                ProvenanceRef = $"refs/tags/{stable.Tag ?? stable.Version}";
                assets = stable.Assets;
            }
        }
        else if (dev is { Version.Length: > 0 })
        {
            NewVersion = dev.Version;

            // Tag and branch builds share one version sequence, so this compares across channels too
            var devVersion = Version.TryParse(NewVersion, out var parsed) ? parsed : new Version(0, 0);
            IsNewer = devVersion > currentVersion;
            ProvenanceRef = "refs/heads/master";
            assets = dev.Assets;
        }

        // An older or equal build on the other channel is offered from the About dialog, but never announced
        IsNewVersionAvailable = IsNewer || (NewVersion != null && channel != Program.BuildChannel);
        NewVersionText = NewVersion == null ? "Not available" : IsNewVersionStableBuild ? NewVersion : $"dev build {NewVersion}";

        var asset = assets?.GetValueOrDefault(RuntimeInformation.RuntimeIdentifier);

        // The file is verified after download, but the request itself should not go out in the clear either
        DownloadUrl = asset?.Url?.StartsWith("https://", StringComparison.Ordinal) == true ? asset.Url : null;
        DownloadSize = asset?.Size;
        DownloadSha256 = asset?.Sha256;

        Settings.Config.Update.UpdateAvailable = IsNewer;
    }

    private static async Task<UpdateManifest?> GetManifestAsync()
    {
        if (IsLocalBuild())
        {
            return null; // Local builds have nothing to compare against
        }

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("User-Agent", $"Source2Viewer/{Program.ProductVersion} (+https://github.com/{Repository})");

        using var response = await httpClient.GetAsync(new Uri(ManifestUrl)).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var jsonStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

        return await JsonSerializer.DeserializeAsync(jsonStream, SourceGenerationContext.Default.UpdateManifest).ConfigureAwait(false);
    }
}
