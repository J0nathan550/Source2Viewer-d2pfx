using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Threading;
using GUI.Utils;
using ValveResourceFormat.Renderer;

namespace GUI
{
    static class Program
    {
#nullable disable
        public static MainWindow MainForm { get; internal set; }
        public static Assembly Assembly { get; private set; }
        public static string ProductVersion { get; private set; }
        public static string DisplayVersion { get; private set; }
#nullable enable

        /// <summary>Whether this build was produced by the CI for a tagged stable release.</summary>
        public const bool IsReleaseBuild =
#if CI_RELEASE_BUILD // For CI builds, it is set in Directory.Build.props
            true;
#else
            false;
#endif

        /// <summary>The update channel that produced this build.</summary>
        public static Settings.UpdateChannel BuildChannel => IsReleaseBuild ? Settings.UpdateChannel.Stable : Settings.UpdateChannel.Dev;

        /// <summary>Files passed on the command line, opened once the main window exists.</summary>
        public static string[] StartupFiles { get; private set; } = [];

        [STAThread]
        internal static int Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += UnhandledException;

#if DEBUG
            // Touching Trace.Listeners reroutes Debug.Assert through the listeners,
            // which prevents the default behavior of Environment.FailFast when no debugger is attached
            Trace.Listeners.Clear();
            Trace.Listeners.Add(new AssertTraceListener());
#endif

            // Set invariant culture so we have consistent localization (e.g. dots do not get encoded as commas)
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

            Assembly = Assembly.GetExecutingAssembly();

            var attribute = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            ProductVersion = attribute?.InformationalVersion ?? throw new InvalidDataException("Failed to find version number");
            DisplayVersion = FormatDisplayVersion(ProductVersion);

            StartupFiles = args;

            Settings.Load();

            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();

        private static string FormatDisplayVersion(string version)
        {
            var versionPlus = version.IndexOf('+', StringComparison.Ordinal);

            if (versionPlus < 0)
            {
                return version;
            }

            var commit = version.AsSpan(versionPlus + 1);

            if (commit.Length > 9)
            {
                commit = commit[..9];
            }

            return string.Concat(version.AsSpan(0, versionPlus), " ", commit);
        }

        private static void UnhandledException(object sender, UnhandledExceptionEventArgs ex)
        {
            ShowError((Exception)ex.ExceptionObject);
        }

        public static void ShowError(Exception exception)
        {
            Log.Error(nameof(Program), exception.ToString());

            var output = new StringBuilder(512);
            AppendExceptionWithVersion(output, exception);
            var outputText = output.ToString();

            var title = exception is ValveResourceFormat.Renderer.Shaders.ShaderLoader.ShaderCompilerException
                ? "Failed to compile shader"
                : $"Unhandled exception: {exception.GetType()}";

            // Errors can be raised from loader and render threads, the dialog must be created on the UI thread
            if (Dispatcher.UIThread.CheckAccess())
            {
                ErrorDialog.Show(title, exception.Message, outputText);
            }
            else
            {
                Dispatcher.UIThread.Post(() => ErrorDialog.Show(title, exception.Message, outputText));
            }
        }

        public static void AppendExceptionWithVersion(StringBuilder output, Exception exception)
        {
            var version = Program.ProductVersion;

            output.AppendLine("```");
            output.AppendLine(exception.ToString());
            output.AppendLine("```");

            output.Append("*S2V ");
            output.Append(version.Replace('+', ' '));
            output.Append(CultureInfo.InvariantCulture, $" on {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");

            if (GLEnvironment.GpuRendererAndDriver != null)
            {
                output.Append(CultureInfo.InvariantCulture, $" ({GLEnvironment.GpuRendererAndDriver})");
            }

            output.AppendLine("*");
        }
    }
}
