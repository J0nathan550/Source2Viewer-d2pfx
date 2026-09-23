using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Forms;
using GUI.Types.PackageViewer;
using GUI.Utils;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using VmdlExtractor;

namespace GUI.Types.Exporter.CharacterAssets
{
    /// <summary>
    /// Exports a Dota 2 hero wearing a chosen set of cosmetic items, as read from the package's items_game.txt, into an
    /// addon. Sources go into its content folder, laid out like the package: models through the custom model extractor
    /// so they recompile as is, everything else through the built-in decompiler. Icons go into its game folder as the
    /// compiled images the game has, renamed over the hero's own.
    /// </summary>
    static class CharacterAssetsExporter
    {
        public static bool CanExport(Control? owner)
            => GetContext(owner)?.CurrentPackage is { } package && ItemsGameCatalog.IsAvailable(package);

        public static async Task ExportFromContextMenu(object sender)
        {
            if (sender is not ToolStripMenuItem { Owner: ContextMenuStrip { SourceControl: var owner } })
            {
                throw new InvalidDataException("Invalid context menu structure");
            }

            var context = GetContext(owner);

            if (context?.CurrentPackage == null)
            {
                return;
            }

            var vpkPath = context.FileName;

            if (string.IsNullOrEmpty(vpkPath) || !File.Exists(vpkPath))
            {
                await AppMessageDialogs.ShowMessageAsync(
                    "Character assets can only be exported from a package opened from disk.",
                    "Cannot export character assets",
                    MessageIcon.Warning).ConfigureAwait(true);
                return;
            }

            using var package = new Package();
            package.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);

            try
            {
                package.Read(vpkPath);
            }
            catch (Exception ex)
            {
                Log.Error(nameof(CharacterAssetsExporter), $"Failed to open VPK: {ex.Message}");
                return;
            }

            var catalog = await LoadCatalogAsync(package).ConfigureAwait(true);

            if (catalog == null)
            {
                return;
            }

            if (catalog.Heroes.Count == 0)
            {
                await AppMessageDialogs.ShowMessageAsync(
                    "No heroes were found in the package's hero scripts.",
                    "Cannot export character assets",
                    MessageIcon.Warning).ConfigureAwait(true);
                return;
            }

            CharacterLoadout loadout;
            CharacterExportOptions options;
            string contentRoot;
            string? gameRoot;

            using (var form = new CharacterSelectForm(catalog, context, package))
            {
                if (await form.ShowDialogAsync().ConfigureAwait(true) != DialogResult.OK || form.SelectedHero == null || form.ContentFolder == null)
                {
                    return;
                }

                loadout = form.CreateLoadout();
                options = form.Options;
                contentRoot = form.ContentFolder;
                gameRoot = form.GameFolder;
            }

            await RunInDialogAsync(vpkPath, package, loadout, options, contentRoot, gameRoot).ConfigureAwait(true);
        }

        /// <summary>
        /// The game folder of an addon from its content folder, which mirror each other under the game's install:
        /// "content/dota_addons/name" goes with "game/dota_addons/name".
        /// </summary>
        public static string? GetGameFolder(string contentFolder)
        {
            var parts = Path.TrimEndingDirectorySeparator(Path.GetFullPath(contentFolder)).Split(Path.DirectorySeparatorChar);
            var content = Array.FindLastIndex(parts, static part => part.Equals("content", StringComparison.OrdinalIgnoreCase));

            if (content < 0)
            {
                return null;
            }

            parts[content] = "game";

            return string.Join(Path.DirectorySeparatorChar, parts);
        }

        private static VrfGuiContext? GetContext(Control? owner) => owner switch
        {
            BetterTreeView tree => tree.VrfGuiContext,
            BetterListView listView => listView.VrfGuiContext,
            _ => null,
        };

        private static async Task<ItemsGameCatalog?> LoadCatalogAsync(Package package)
        {
            ItemsGameCatalog? catalog = null;

            using var dialog = new GenericProgressForm { Text = "Reading items_game.txt..." };

            dialog.OnProcess = cancellationToken =>
            {
                try
                {
                    catalog = ItemsGameCatalog.Load(package, dialog, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    catalog = null;
                }
                return Task.CompletedTask;
            };

            await dialog.ShowDialogAsync().ConfigureAwait(true);

            return catalog;
        }

        private static async Task RunInDialogAsync(string vpkPath, Package package, CharacterLoadout loadout, CharacterExportOptions options,
            string contentRoot, string? gameRoot)
        {
            var hero = loadout.Hero;
            var items = loadout.Items;

            using var dialog = new CustomVmdlExtractProgressForm
            {
                StayOpenOnCompletion = true,
                Text = $"Exporting {hero.DisplayName}...",
            };

            dialog.AppendLine($"Exporting {hero.DisplayName} with {items.Count} item{(items.Count == 1 ? "" : "s")} to \"{contentRoot}\"");

            if (gameRoot != null)
            {
                dialog.AppendLine($"Compiled files go to \"{gameRoot}\"");
            }

            foreach (var item in items)
            {
                dialog.AppendLine($"  [{item.Item.Slot}] {item.Item.Name}{(item.StyleName != null ? $" ({item.StyleName})" : string.Empty)}");
            }

            dialog.OnProcess = cancellationToken =>
            {
                Export(vpkPath, package, loadout, options, contentRoot, gameRoot, dialog, cancellationToken);
                return Task.CompletedTask;
            };

            await dialog.ShowDialogAsync().ConfigureAwait(true);
        }

        /// <param name="contentRoot">The addon's content folder, which gets the sources.</param>
        /// <param name="gameRoot">The addon's game folder, which gets the compiled icons, or null to leave them out.</param>
        internal static void Export(string vpkPath, Package package, CharacterLoadout loadout, CharacterExportOptions options,
            string contentRoot, string? gameRoot, IProgress<string> progress, CancellationToken cancellationToken)
        {
            var startTimestamp = Stopwatch.GetTimestamp();

            Log.Info(nameof(CharacterAssetsExporter), $"Character export of {loadout.Hero.Name} started to \"{contentRoot}\"");

            var originalOut = Console.Out;
            var originalError = Console.Error;
            using var stdOutWriter = new CustomVmdlExporter.LogTextWriter(isError: false, progress);
            using var stdErrWriter = new CustomVmdlExporter.LogTextWriter(isError: true, progress);
            Console.SetOut(stdOutWriter);
            Console.SetError(stdErrWriter);

            try
            {
                using var fileLoader = new GameFileLoader(package, vpkPath);

                progress.Report("Collecting dependencies...");

                var plan = new CharacterDependencyCollector(package, fileLoader, progress).Collect(loadout, options, cancellationToken);

                progress.Report($"Found {plan.Models.Count} models, {plan.Resources.Count} particles and sound events, {plan.RawFiles.Count} other files and {plan.IconReplacements.Count} icons");

                foreach (var missing in plan.Missing)
                {
                    progress.Report($"  ! not found: {missing}");
                }

                var writtenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var failed = 0;

                failed += ExportModels(plan.Models, vpkPath, contentRoot, fileLoader, progress, cancellationToken);
                failed += ExportResources(plan.Resources, contentRoot, fileLoader, progress, writtenFiles, cancellationToken);
                failed += ExportRawFiles(plan.RawFiles, contentRoot, fileLoader, progress, writtenFiles, cancellationToken);
                failed += ApplyReplacements(plan, contentRoot, fileLoader, progress);
                failed += ExportIcons(plan.IconReplacements, gameRoot, fileLoader, progress, cancellationToken);

                foreach (var note in plan.Notes)
                {
                    progress.Report($"  - {note}");
                }

                var completedText = $"Export completed in {GenericProgressForm.FormatTime(Stopwatch.GetElapsedTime(startTimestamp))}";

                if (failed > 0)
                {
                    completedText += $", {failed} file{(failed == 1 ? "" : "s")} failed";
                }

                if (plan.Missing.Count > 0)
                {
                    completedText += $", {plan.Missing.Count} reference{(plan.Missing.Count == 1 ? " was" : "s were")} not found";
                }

                Log.Info(nameof(CharacterAssetsExporter), completedText);
                progress.Report(completedText);
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
        }

        private static int ExportModels(List<string> models, string vpkPath, string outputRoot, GameFileLoader fileLoader,
            IProgress<string> progress, CancellationToken cancellationToken)
        {
            if (models.Count == 0)
            {
                return 0;
            }

            progress.Report($"Exporting {models.Count} models with the custom VMDL extractor...");

            var targets = new List<(string RelativePath, Resource Resource)>(models.Count);
            var failed = 0;

            try
            {
                foreach (var model in models)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var resource = fileLoader.LoadFile(model);

                    if (resource == null)
                    {
                        failed++;
                        progress.Report($"  FAILED {model}: could not be read");
                        continue;
                    }

                    targets.Add((model, resource));
                }

                var result = CustomModelExporter.ExtractModels(targets, outputRoot, fileLoader, new CustomModelExportOptions
                {
                    DonorVpkPath = vpkPath,
                });

                foreach (var failure in result.Failures)
                {
                    progress.Report($"  FAILED {failure.File}: {failure.Error}");
                    Log.Error(nameof(CharacterAssetsExporter), $"{failure.File}: {failure.Error}");
                }

                progress.Report($"  {result.Succeeded} models exported");

                return failed + result.Failures.Count;
            }
            finally
            {
                foreach (var (_, resource) in targets)
                {
                    resource.Dispose();
                }
            }
        }

        private static int ApplyReplacements(CharacterExportPlan plan, string outputRoot, IFileLoader fileLoader, IProgress<string> progress)
        {
            if (plan.ModelReplacements.Count == 0 && plan.ParticleReplacements.Count == 0 && plan.SkippedSharedParticles.Count == 0 && plan.UnplacedModels.Count == 0)
            {
                return 0;
            }

            progress.Report("Replacing the hero's default assets...");

            var failed = 0;

            foreach (var replacement in plan.ModelReplacements)
            {
                try
                {
                    var details = new List<string>();
                    var vmdl = ReadModel(outputRoot, replacement.Source, replacement.Skin, replacement.BodyGroups, progress, details);

                    foreach (var merged in replacement.Merged)
                    {
                        try
                        {
                            var mergedVmdl = ReadModel(outputRoot, merged.Model, merged.Skin, merged.BodyGroups, progress, details: null);

                            vmdl = ModelDocEditor.MergeModel(vmdl, mergedVmdl, Path.GetFileNameWithoutExtension(merged.Model));
                            details.Add($"with the meshes of {merged.Model}");
                        }
                        catch (Exception e)
                        {
                            failed++;
                            progress.Report($"  FAILED to add {merged.Model} to {replacement.Target}: {e.Message}");
                            Log.Error(nameof(CharacterAssetsExporter), $"Failed to add '{merged.Model}' to '{replacement.Target}': {e}");
                        }
                    }

                    if (replacement.Particles.Count > 0)
                    {
                        try
                        {
                            var particles = replacement.Particles.Select(particle => ModelDocEditor.ResolveParticle(fileLoader, particle)).ToList();

                            vmdl = ModelDocEditor.AddParticles(vmdl, particles);

                            foreach (var particle in particles)
                            {
                                details.Add($"creates {particle.Name} on {(particle.AttachmentPoint.Length > 0 ? particle.AttachmentPoint : "origin")}");
                            }
                        }
                        catch (Exception e)
                        {
                            progress.Report($"  ! {replacement.Target}: particles were not added: {e.Message}");
                        }
                    }

                    var targetPath = GetOutputPath(outputRoot, Path.ChangeExtension(replacement.Target, "vmdl"));
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                    File.WriteAllText(targetPath, vmdl);

                    progress.Report($"  {replacement.Target} <- {replacement.Source}{(details.Count > 0 ? $" ({string.Join(", ", details)})" : string.Empty)}");
                }
                catch (Exception e)
                {
                    failed++;
                    progress.Report($"  FAILED {replacement.Target} <- {replacement.Source}: {e.Message}");
                    Log.Error(nameof(CharacterAssetsExporter), $"Failed to replace '{replacement.Target}': {e}");
                }
            }

            foreach (var replacement in plan.ParticleReplacements)
            {
                try
                {
                    var targetPath = GetOutputPath(outputRoot, replacement.Target);
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                    File.Copy(GetExportedPath(outputRoot, replacement.Source, "vpcf"), targetPath, overwrite: true);

                    progress.Report($"  {replacement.Target} <- {replacement.Source}");
                }
                catch (Exception e)
                {
                    failed++;
                    progress.Report($"  FAILED {replacement.Target} <- {replacement.Source}: {e.Message}");
                    Log.Error(nameof(CharacterAssetsExporter), $"Failed to replace '{replacement.Target}': {e}");
                }
            }

            foreach (var unplaced in plan.UnplacedModels)
            {
                progress.Report($"  - {unplaced} is not worn by the hero and has no default model to write it over, it was exported as is");
            }

            foreach (var skipped in plan.SkippedSharedParticles)
            {
                progress.Report($"  - left {skipped.Target} alone, every hero uses it (it would become {skipped.Source})");
            }

            return failed;
        }

        /// <summary>
        /// Reads an exported model, with the skin and body group choices the loadout shows it with made its defaults.
        /// </summary>
        private static string ReadModel(string outputRoot, string model, int skin, Dictionary<string, int> bodyGroups, IProgress<string> progress, List<string>? details)
        {
            var vmdl = File.ReadAllText(GetExportedPath(outputRoot, model, "vmdl"));

            if (skin != 0)
            {
                try
                {
                    vmdl = ModelDocEditor.MakeMaterialGroupDefault(vmdl, skin);
                    details?.Add($"skin {skin} as default");
                }
                catch (Exception e)
                {
                    progress.Report($"  ! {model}: skin {skin} was not made the default: {e.Message}");
                }
            }

            if (bodyGroups.Count > 0)
            {
                try
                {
                    vmdl = ModelDocEditor.SelectBodyGroupChoices(vmdl, bodyGroups, details);
                }
                catch (Exception e)
                {
                    progress.Report($"  ! {model}: body groups were not picked: {e.Message}");
                }
            }

            return vmdl;
        }

        private static string GetExportedPath(string outputRoot, string sourcePath, string extension)
        {
            var path = GetOutputPath(outputRoot, Path.ChangeExtension(sourcePath, extension));

            return File.Exists(path) ? path : throw new FileNotFoundException($"\"{Path.ChangeExtension(sourcePath, extension)}\" was not exported");
        }

        private static int ExportResources(List<string> resources, string outputRoot, GameFileLoader fileLoader,
            IProgress<string> progress, HashSet<string> writtenFiles, CancellationToken cancellationToken)
        {
            if (resources.Count == 0)
            {
                return 0;
            }

            progress.Report($"Decompiling {resources.Count} particles, sound events and sounds...");

            var failed = 0;

            foreach (var path in resources)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using var resource = fileLoader.LoadFile(path) ?? throw new FileNotFoundException("Could not be read");

                    ExportResource(resource, StripCompiledSuffix(path), outputRoot, fileLoader, progress, writtenFiles);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    failed++;
                    progress.Report($"  FAILED {path}: {e.Message}");
                    Log.Error(nameof(CharacterAssetsExporter), $"Failed to export '{path}': {e}");
                }
            }

            return failed;
        }

        private static void ExportResource(Resource resource, string sourcePath, string outputRoot, IFileLoader fileLoader,
            IProgress<string> progress, HashSet<string> writtenFiles)
        {
            using var contentFile = FileExtract.Extract(resource, fileLoader);

            var mainPath = Path.ChangeExtension(sourcePath, FileExtract.GetExtension(resource));

            if (contentFile.Data != null)
            {
                WriteFile(outputRoot, mainPath, contentFile.Data, progress, writtenFiles);
            }

            WriteSubFiles(outputRoot, GetFolder(sourcePath), contentFile, progress, writtenFiles);

            foreach (var additionalFile in contentFile.AdditionalFiles)
            {
                var additionalPath = additionalFile.FileName.Replace('\\', '/');

                if (additionalFile.Data != null)
                {
                    WriteFile(outputRoot, additionalPath, additionalFile.Data, progress, writtenFiles);
                }

                WriteSubFiles(outputRoot, GetFolder(additionalPath), additionalFile, progress, writtenFiles);
            }
        }

        private static void WriteSubFiles(string outputRoot, string folder, ContentFile contentFile, IProgress<string> progress, HashSet<string> writtenFiles)
        {
            foreach (var subFile in contentFile.SubFiles)
            {
                var subFilePath = folder.Length == 0 ? subFile.FileName : $"{folder}/{subFile.FileName}";

                if (subFile.Extract == null || writtenFiles.Contains(GetOutputPath(outputRoot, subFilePath)))
                {
                    continue;
                }

                var data = subFile.Extract();

                if (data.Length > 0)
                {
                    WriteFile(outputRoot, subFilePath, data, progress, writtenFiles);
                }
            }
        }

        private static int ExportRawFiles(List<string> files, string outputRoot, GameFileLoader fileLoader,
            IProgress<string> progress, HashSet<string> writtenFiles, CancellationToken cancellationToken)
        {
            var failed = 0;

            foreach (var path in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    WriteFile(outputRoot, path, ReadRaw(fileLoader, path), progress, writtenFiles);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    failed++;
                    progress.Report($"  FAILED {path}: {e.Message}");
                    Log.Error(nameof(CharacterAssetsExporter), $"Failed to export '{path}': {e}");
                }
            }

            return failed;
        }

        /// <summary>
        /// Copies the compiled icons into the game folder under the names of the ones they replace. The game only reads
        /// panorama images compiled, and these already are.
        /// </summary>
        private static int ExportIcons(List<IconReplacement> icons, string? gameRoot, GameFileLoader fileLoader,
            IProgress<string> progress, CancellationToken cancellationToken)
        {
            if (icons.Count == 0)
            {
                return 0;
            }

            if (gameRoot == null)
            {
                progress.Report($"  ! {icons.Count} icon{(icons.Count == 1 ? " was" : "s were")} not replaced, no game folder was chosen");
                return 0;
            }

            progress.Report($"Replacing {icons.Count} icons in \"{gameRoot}\"...");

            var failed = 0;

            foreach (var icon in icons)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var outputPath = GetOutputPath(gameRoot, icon.Target);
                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                    File.WriteAllBytes(outputPath, ReadRaw(fileLoader, icon.Source));

                    progress.Report($"  {icon.Target} <- {icon.Source}");
                }
                catch (Exception e)
                {
                    failed++;
                    progress.Report($"  FAILED {icon.Target} <- {icon.Source}: {e.Message}");
                    Log.Error(nameof(CharacterAssetsExporter), $"Failed to replace '{icon.Target}': {e}");
                }
            }

            return failed;
        }

        private static byte[] ReadRaw(GameFileLoader fileLoader, string path)
        {
            using var stream = fileLoader.GetFileStream(path) ?? throw new FileNotFoundException("Could not be read");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);

            return buffer.ToArray();
        }

        private static void WriteFile(string outputRoot, string relativePath, byte[] data, IProgress<string> progress, HashSet<string> writtenFiles)
        {
            var outputPath = GetOutputPath(outputRoot, relativePath);

            if (!writtenFiles.Add(outputPath))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllBytes(outputPath, data);
            progress.Report($"+ {relativePath}");
        }

        private static string GetOutputPath(string outputRoot, string relativePath)
        {
            var outputPath = Path.GetFullPath(Path.Combine(outputRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

            if (CustomVmatExporter.GetContentRelativePath(outputRoot, outputPath) == null)
            {
                throw new InvalidDataException($"\"{relativePath}\" points outside of the export folder");
            }

            return outputPath;
        }

        private static string GetFolder(string path) => Path.GetDirectoryName(path)?.Replace('\\', '/') ?? string.Empty;

        private static string StripCompiledSuffix(string path)
            => path.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.OrdinalIgnoreCase)
                ? path[..^GameFileLoader.CompiledFileSuffix.Length]
                : path;
    }
}
