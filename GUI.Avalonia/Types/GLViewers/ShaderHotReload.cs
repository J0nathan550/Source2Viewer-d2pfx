#if DEBUG
using System.Diagnostics;
using System.IO;
using System.Threading;
using Avalonia.Threading;
using GUI.Utils;
using ValveResourceFormat.Renderer.Shaders;

namespace GUI.Types.GLViewers;

internal sealed class ShaderHotReload : IDisposable
{
    private static readonly TimeSpan changeCoolDown = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan reloadCoolDown = TimeSpan.FromSeconds(0.5); // There is a change that happens right after reload

    // The built-in shader folder, plus every directory mounted through ShaderRegistry
    private List<FileSystemWatcher>? ShaderWatchers;

    private readonly SemaphoreSlim reloadSemaphore = new(1, 1);
    private DateTime lastChanged;
    private DateTime lastReload;
    private bool showingError;

    private readonly GLBaseControl ViewerControl;
    private readonly ShaderLoader ShaderLoader;

    public event EventHandler<string?>? ShadersReloaded;

    public ShaderHotReload(GLBaseControl viewerControl, ShaderLoader shaderLoader)
    {
        ViewerControl = viewerControl;
        ShaderLoader = shaderLoader;
    }

    public void Start()
    {
        if (ShaderWatchers != null)
        {
            return;
        }

        var paths = new List<string>(ShaderRegistry.Directories);

        // Only present when this assembly was built from the shader source files, rather than using the embedded copies
        if (ShaderParser.ShaderSourceDirectory != null)
        {
            paths.Add(ShaderParser.ShaderSourceDirectory);
        }

        ShaderWatchers = new List<FileSystemWatcher>(paths.Count);

        foreach (var path in paths)
        {
            if (!Directory.Exists(path))
            {
                continue;
            }

            var watcher = new FileSystemWatcher
            {
                Path = path,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                IncludeSubdirectories = true,
                Filters = { "*.slang" },
            };

            watcher.Changed += OnFileChanged;
            watcher.Created += OnFileChanged;
            watcher.Renamed += OnFileChanged;
            watcher.EnableRaisingEvents = true;

            ShaderWatchers.Add(watcher);
        }
    }

    public void Dispose()
    {
        if (ShaderWatchers != null)
        {
            foreach (var watcher in ShaderWatchers)
            {
                watcher.Changed -= OnFileChanged;
                watcher.Created -= OnFileChanged;
                watcher.Renamed -= OnFileChanged;
                watcher.Dispose();
            }

            ShaderWatchers = null;
        }

        reloadSemaphore.Dispose();
    }

    public void ReloadShaders(string? name = null)
    {
        using (ViewerControl.MakeCurrent())
        {
            ShaderLoader.ReloadAllShaders(name);
            ShadersReloaded?.Invoke(this, name);
        }

        ViewerControl.GLControl?.Invalidate();
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        Dispatcher.UIThread.Post(() => Hotload(e));
    }

    private void Hotload(FileSystemEventArgs e)
    {
        if (ShaderWatchers == null || ViewerControl.GLControl is not { IsShown: true })
        {
            return;
        }

        if (e.FullPath.EndsWith(".TMP", StringComparison.Ordinal))
        {
            return; // Visual Studio writes to temporary file
        }

        Log.Debug(nameof(ShaderHotReload), $"{e.ChangeType} {e.FullPath}");

        var now = DateTime.Now;

        if (reloadSemaphore.CurrentCount == 0
            || now - lastReload < reloadCoolDown
            || now - lastChanged < changeCoolDown)
        {
            return;
        }

        lastChanged = now;

        if (!reloadSemaphore.Wait(0))
        {
            return;
        }

        var reloadStopwatch = Stopwatch.StartNew();
        string? error = null;

        Program.MainForm.SetStatus("Reloading shaders...");

        try
        {
            ReloadShaders(e.Name);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Log.Error(nameof(ShaderHotReload), error);
        }
        finally
        {
            lastReload = DateTime.Now;
            reloadSemaphore.Release();
            reloadStopwatch.Stop();
            Log.Debug(nameof(ShaderHotReload), $"Shader reload time: {reloadStopwatch.Elapsed}");
            Program.MainForm.SetStatus(error == null ? "Reloaded shaders" : "Failed to reload shaders");
        }

        if (error != null && !showingError)
        {
            showingError = true;
            _ = ShowErrorAsync(error);
        }
    }

    private async System.Threading.Tasks.Task ShowErrorAsync(string error)
    {
        try
        {
            await AppMessageDialogs.ShowMessageAsync(error, "Failed to reload shaders", MessageIcon.Error).ConfigureAwait(true);
        }
        finally
        {
            showingError = false;
        }
    }
}
#endif
