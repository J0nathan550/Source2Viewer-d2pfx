using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GUI.Types.PackageViewer;
using GUI.Utils;
using ValvePak;
using ValveResourceFormat.IO;
using Resource = ValveResourceFormat.Resource;

namespace GUI.Types.Exporter
{
    static class ExportFile
    {
        public static async Task ExtractFileFromPackageEntry(PackageEntry file, VrfGuiContext vrfGuiContext, bool decompile)
        {
            var currentPackage = vrfGuiContext.CurrentPackage;
            if (currentPackage == null)
            {
                Log.Error(nameof(ExportFile), "CurrentPackage is null, cannot extract file");
                return;
            }

            var stream = GameFileLoader.GetPackageEntryStream(currentPackage, file);

            await ExtractFileFromStream(file.GetFullPath(), stream, vrfGuiContext, decompile).ConfigureAwait(true);
        }

        public static async Task ExtractFileFromStream(string fileName, Stream stream, VrfGuiContext vrfGuiContext, bool decompile)
        {
            if (decompile && !await PreExportDisclaimer(Path.GetExtension(fileName)).ConfigureAwait(true))
            {
                return;
            }

            if (decompile && fileName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
            {
                var exportData = new ExportData
                {
                    VrfGuiContext = new VrfGuiContext(fileName, vrfGuiContext),
                };

                var resourceTemp = new Resource
                {
                    FileName = fileName,
                };
                var resource = resourceTemp;
                string filaNameToSave;

                try
                {
                    resource.Read(stream);

                    var extension = FileExtract.GetExtension(resource);

                    if (extension == null)
                    {
                        await stream.DisposeAsync().ConfigureAwait(true);
                        Log.Error(nameof(ExportFile), $"Export for \"{fileName}\" has no suitable extension");
                        return;
                    }

                    var filter = $"{extension} file|*.{extension}";

                    if (GltfModelExporter.CanExport(resource))
                    {
                        const string gltfFilter = "glTF|*.gltf";
                        const string glbFilter = "glTF Binary|*.glb";

                        filter = $"{filter}|{gltfFilter}|{glbFilter}";
                    }

                    var fileNameForSave = Path.GetFileNameWithoutExtension(fileName);

                    if (Path.GetExtension(fileName) == ".vmap_c")
                    {
                        // When exporting a vmap, suggest saving with a suffix like de_dust2_d,
                        // to reduce conflicts when users end up recompiling the map with the same name as it exists in the game
                        fileNameForSave += "_d";
                    }

                    var pickedFileName = AppFileDialogs.SaveFile("Choose where to save the file", fileNameForSave, extension, filter);

                    if (pickedFileName == null)
                    {
                        return;
                    }

                    filaNameToSave = pickedFileName;
                    resourceTemp = null;
                }
                finally
                {
                    resourceTemp?.Dispose();
                }

                var directory = Path.GetDirectoryName(filaNameToSave);

                try
                {
                    var exporter = new PackageExporter(exportData, directory ?? string.Empty, true);

                    // Cancelling closes the dialog while the extract is still winding down, so wait for it to
                    // stop before the finally pulls the resource out from under it
                    await exporter.ExecuteSingleFileExtractAsync(resource, fileName, filaNameToSave, $"Extracting {fileName} to \"{Path.GetFileName(filaNameToSave)}\"").ConfigureAwait(true);
                }
                finally
                {
                    await stream.DisposeAsync().ConfigureAwait(true);
                    resource.Dispose();
                    exportData.VrfGuiContext.Dispose();
                }
            }
            else
            {
                if (decompile && FileExtract.TryExtractNonResource(stream, fileName, out var content))
                {
                    if (content.Data == null)
                    {
                        // Content has no data to extract, only potentially subfiles
                        content.Dispose();
                        await stream.DisposeAsync().ConfigureAwait(true);
                        Log.Info(nameof(ExportFile), $"File \"{fileName}\" has no extractable data");
                        return;
                    }

                    var extension = Path.GetExtension(content.FileName);
                    fileName = Path.ChangeExtension(fileName, extension);
                    await stream.DisposeAsync().ConfigureAwait(true);

                    stream = new MemoryStream(content.Data);
                    content.Dispose();
                }

                var saveFileName = AppFileDialogs.SaveFile("Choose where to save the file", Path.GetFileName(fileName), null, "All files (*.*)|*.*");

                if (saveFileName != null)
                {
                    Log.Info(nameof(ExportFile), $"Saved \"{Path.GetFileName(saveFileName)}\"");

                    using var streamOutput = File.Create(saveFileName);
                    await stream.CopyToAsync(streamOutput).ConfigureAwait(true);
                }

                await stream.DisposeAsync().ConfigureAwait(true);
            }
        }

        /// <summary>
        /// Extracts the files and folders selected in the package viewer, asking whether to recurse into subfolders
        /// and whether to recreate the package folder structure when those choices matter.
        /// </summary>
        public static async Task ExtractSelectedItems(List<IBetterBaseItem> items, VrfGuiContext vrfGuiContext, bool decompile)
        {
            if (items.Count == 0)
            {
                return;
            }

            var includeSubfolders = true;

            if (items.Any(static item => item.PkgNode is { Folders.Count: > 0 }))
            {
                var answer = await AppMessageDialogs.AskYesNoCancelAsync(
                    """
                    The selection contains folders with subfolders.

                    Yes: export everything, including all subfolders and their files.
                    No: export only the files directly inside the selected folders.
                    """,
                    "Export subfolders").ConfigureAwait(true);

                if (answer == null)
                {
                    return;
                }

                includeSubfolders = answer.Value;
            }

            // Only files and folders below a top level folder have parent folders to recreate
            var examplePath = items
                .Select(static item => item.PackageEntry?.GetFullPath() ?? $"{item.PkgNode?.GetFullPath()}{Package.DirectorySeparatorChar}")
                .FirstOrDefault(static path => path.TrimEnd(Package.DirectorySeparatorChar).Contains(Package.DirectorySeparatorChar, StringComparison.Ordinal));

            var keepPackageStructure = false;

            if (examplePath != null)
            {
                var answer = await AppMessageDialogs.AskYesNoCancelAsync(
                    $"""
                    Export with the folder structure of the package?

                    Yes: recreate the parent folders, e.g. "{examplePath}".
                    No: place the selected files and folders directly in the chosen location.
                    """,
                    "Export folder structure").ConfigureAwait(true);

                if (answer == null)
                {
                    return;
                }

                keepPackageStructure = answer.Value;
            }

            // A single file placed as is gets the regular save dialog, so it can be renamed or exported as another format
            if (!keepPackageStructure && items is [{ PackageEntry: { } singleFile }])
            {
                await ExtractFileFromPackageEntry(singleFile, vrfGuiContext, decompile).ConfigureAwait(true);
                return;
            }

            var exportData = new ExportData
            {
                VrfGuiContext = vrfGuiContext,
            };

            var exporter = new PackageExporter(exportData, null, decompile);

            foreach (var item in items)
            {
                exporter.QueueSelection(item, includeSubfolders, keepPackageStructure);
            }

            exporter.ExecuteMultipleFileExtract();
        }

        public static async Task<bool> PreExportDisclaimer(string fileExtension)
        {
            var messageString = "";

            switch (fileExtension)
            {
                case ".vmap_c":

                    messageString =
                    """
                    Decompiling Source2 maps is a difficult process, as such the output will be messy and imperfect, and will not resemble how
                    real .vmap files are made!

                    - Models will be merged by material across the map.
                    - Parts of the skybox mesh might be missing.
                    - The collision of the map will be merged into one mesh using special materials.
                    - The map will lack lightmap resolution volumes.
                    - Hammer meshes will be triangulated.

                    It is NOT ADVISED to work on decompiled maps as your first map if you are new to mapping!
                    """;
                    break;

                default:
                    break;
            }

            if (!string.IsNullOrEmpty(messageString))
            {
                return await AppMessageDialogs.ConfirmAsync(messageString, "Decompile warning", MessageIcon.Warning).ConfigureAwait(true);
            }

            return true;
        }
    }
}
