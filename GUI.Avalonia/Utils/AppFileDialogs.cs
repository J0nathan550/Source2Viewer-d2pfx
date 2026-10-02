using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace GUI.Utils;

// File and folder picker wrapper. Filters use the WinForms "Name|*.ext;*.ext2|Name2|*.*" syntax
// so call sites are shared with the WinForms front-end.
public static class AppFileDialogs
{
    public enum RememberIn
    {
        None,
        OpenDirectory,
        SaveDirectory,
    }

    // updateRemembered: set to false when the caller validates the picked path first and
    // wants to remember the directory only when the pick is actually accepted.
    public static string? PickFolder(string? title, RememberIn remember = RememberIn.None, bool updateRemembered = true)
        => DispatcherHelpers.WaitOnUIThread(() => PickFolderAsync(title, remember, updateRemembered));

    public static string? OpenFile(string? title, string? filter, RememberIn remember = RememberIn.OpenDirectory, bool updateRemembered = true)
    {
        var files = OpenFiles(title, filter, remember, updateRemembered, multiselect: false);
        return files is { Length: > 0 } ? files[0] : null;
    }

    public static string[]? OpenFiles(string? title, string? filter, RememberIn remember = RememberIn.OpenDirectory, bool updateRemembered = true)
        => OpenFiles(title, filter, remember, updateRemembered, multiselect: true);

    private static string[]? OpenFiles(string? title, string? filter, RememberIn remember, bool updateRemembered, bool multiselect)
        => DispatcherHelpers.WaitOnUIThread(() => OpenFilesAsync(title, filter, multiselect, remember, updateRemembered));

    public static string? SaveFile(string title, string? defaultFileName, string? defaultExtension, string filter, RememberIn remember = RememberIn.SaveDirectory)
    {
        return SaveFile(title, defaultFileName, defaultExtension, filter, out _, remember);
    }

    // selectedFilterIndex is 1 based, 0 means user canceled
    public static string? SaveFile(string title, string? defaultFileName, string? defaultExtension, string filter, out int selectedFilterIndex, RememberIn remember = RememberIn.SaveDirectory)
    {
        var (path, filterIndex) = DispatcherHelpers.WaitOnUIThread(() => SaveFileAsync(title, defaultFileName, defaultExtension, filter, remember));
        selectedFilterIndex = filterIndex;
        return path;
    }

    public static async Task<string?> PickFolderAsync(string? title, RememberIn remember = RememberIn.None, bool updateRemembered = true)
    {
        if (GetStorageProvider() is not { } storage)
        {
            return null;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            SuggestedStartLocation = await GetStartFolderAsync(storage, remember).ConfigureAwait(true),
        }).ConfigureAwait(true);

        var path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;

        if (path != null && updateRemembered)
        {
            SetRememberedDirectory(remember, path);
        }

        return path;
    }

    public static async Task<string[]?> OpenFilesAsync(string? title, string? filter, bool multiselect = true, RememberIn remember = RememberIn.OpenDirectory, bool updateRemembered = true)
    {
        if (GetStorageProvider() is not { } storage)
        {
            return null;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = multiselect,
            FileTypeFilter = ParseFilter(filter),
            SuggestedStartLocation = await GetStartFolderAsync(storage, remember).ConfigureAwait(true),
        }).ConfigureAwait(true);

        var paths = files.Select(static f => f.TryGetLocalPath()).OfType<string>().ToArray();

        if (paths.Length < 1)
        {
            return null;
        }

        if (updateRemembered && Path.GetDirectoryName(paths[0]) is { Length: > 0 } directory)
        {
            SetRememberedDirectory(remember, directory);
        }

        return paths;
    }

    public static async Task<(string? Path, int FilterIndex)> SaveFileAsync(string title, string? defaultFileName, string? defaultExtension, string filter, RememberIn remember = RememberIn.SaveDirectory)
    {
        if (GetStorageProvider() is not { } storage)
        {
            return (null, 0);
        }

        var fileTypes = ParseFilter(filter);

        var result = await storage.SaveFilePickerWithResultAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = defaultFileName,
            DefaultExtension = defaultExtension,
            FileTypeChoices = fileTypes,
            ShowOverwritePrompt = true,
            SuggestedStartLocation = await GetStartFolderAsync(storage, remember).ConfigureAwait(true),
        }).ConfigureAwait(true);

        var path = result.File?.TryGetLocalPath();

        if (path == null)
        {
            return (null, 0);
        }

        var filterIndex = 1;

        if (fileTypes != null && result.SelectedFileType is { } selectedType)
        {
            filterIndex = Math.Max(0, fileTypes.IndexOf(selectedType)) + 1;
        }
        else if (fileTypes != null)
        {
            // Not every platform reports the chosen type, infer it from the extension instead
            var extension = Path.GetExtension(path);
            var index = fileTypes.FindIndex(t => t.Patterns?.Any(p => p.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) == true);
            filterIndex = index >= 0 ? index + 1 : 1;
        }

        if (Path.GetDirectoryName(path) is { Length: > 0 } directory)
        {
            SetRememberedDirectory(remember, directory);
        }

        return (path, filterIndex);
    }

    private static IStorageProvider? GetStorageProvider() => AppMessageDialogs.GetOwner()?.StorageProvider;

    private static async Task<IStorageFolder?> GetStartFolderAsync(IStorageProvider storage, RememberIn remember)
    {
        var directory = GetRememberedDirectory(remember);

        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return null;
        }

        return await storage.TryGetFolderFromPathAsync(directory).ConfigureAwait(true);
    }

    internal static List<FilePickerFileType>? ParseFilter(string? filter)
    {
        if (string.IsNullOrEmpty(filter))
        {
            return null;
        }

        var parts = filter.Split('|');
        var types = new List<FilePickerFileType>(parts.Length / 2);

        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            var patterns = parts[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            types.Add(new FilePickerFileType(parts[i])
            {
                // GTK and macOS pickers treat "*.*" as files with a dot only, "*" is the portable way to say any file
                Patterns = patterns.Select(static p => p == "*.*" ? "*" : p).ToArray(),
            });
        }

        return types;
    }

    private static string GetRememberedDirectory(RememberIn remember) => remember switch
    {
        RememberIn.OpenDirectory => Settings.Config.OpenDirectory,
        RememberIn.SaveDirectory => Settings.Config.SaveDirectory,
        _ => string.Empty,
    };

    private static void SetRememberedDirectory(RememberIn remember, string path)
    {
        switch (remember)
        {
            case RememberIn.OpenDirectory:
                Settings.Config.OpenDirectory = path;
                break;
            case RememberIn.SaveDirectory:
                Settings.Config.SaveDirectory = path;
                break;
            case RememberIn.None:
                break;
        }
    }
}
