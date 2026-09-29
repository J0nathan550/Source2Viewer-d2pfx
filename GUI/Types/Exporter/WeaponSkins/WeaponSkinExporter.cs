using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Forms;
using GUI.Types.Exporter.CharacterAssets;
using GUI.Utils;
using SkiaSharp;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using VmdlExtractor;

namespace GUI.Types.Exporter.WeaponSkins
{
    /// <summary>
    /// What to export: a weapon, the paint kit to paint it with, and how worn.
    /// </summary>
    sealed record WeaponSkinExportRequest(WeaponDefinition Weapon, PaintKit PaintKit, float Wear, bool ReplaceIcon);

    /// <summary>
    /// Exports a Counter-Strike 2 weapon painted with a finish into an addon, over the weapon's own assets so it shows
    /// the finish wherever the game shows the weapon. The game paints finishes on at runtime from composite materials,
    /// which the addon cannot use in place of a material, so the finish is baked into the textures of the weapon's own
    /// material. Finishes made for the weapon's legacy mesh also get its model, set to show that mesh.
    /// </summary>
    static partial class WeaponSkinExporter
    {
        private const string CompositeInputsAttribute = "composite_inputs";
        private const string LegacyMeshName = "legacy";

        public static bool CanExport(VrfGuiContext? context)
            => context?.CurrentPackage is { } package && WeaponSkinCatalog.IsAvailable(package);

        /// <summary>
        /// Asks for a weapon and a finish from the context's package, then exports them.
        /// </summary>
        public static async Task ExportAsync(VrfGuiContext context)
        {
            var vpkPath = context.FileName;

            if (context.CurrentPackage == null || string.IsNullOrEmpty(vpkPath) || !File.Exists(vpkPath))
            {
                await AppMessageDialogs.ShowMessageAsync(
                    "Weapon skins can only be exported from a package opened from disk.",
                    "Cannot export weapon skins",
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
                Log.Error(nameof(WeaponSkinExporter), $"Failed to open VPK: {ex.Message}");
                return;
            }

            var catalog = await LoadCatalogAsync(package).ConfigureAwait(true);

            if (catalog == null)
            {
                return;
            }

            if (catalog.Weapons.Count == 0)
            {
                await AppMessageDialogs.ShowMessageAsync(
                    "No weapons with finishes were found in the package's items_game.txt.",
                    "Cannot export weapon skins",
                    MessageIcon.Warning).ConfigureAwait(true);
                return;
            }

            WeaponSkinExportRequest request;
            string contentRoot;
            string? gameRoot;

            using (var form = new WeaponSkinSelectForm(catalog, package))
            {
                if (await form.ShowDialogAsync().ConfigureAwait(true) != DialogResult.OK || form.CreateRequest() is not { } created || form.ContentFolder == null)
                {
                    return;
                }

                request = created;
                contentRoot = form.ContentFolder;
                gameRoot = form.GameFolder;
            }

            using var dialog = new CustomVmdlExtractProgressForm
            {
                StayOpenOnCompletion = true,
                Text = $"Exporting {request.Weapon.DisplayName} | {request.PaintKit.DisplayName}...",
            };

            dialog.OnProcess = cancellationToken =>
            {
                Export(vpkPath, package, request, contentRoot, gameRoot, dialog, cancellationToken);
                return Task.CompletedTask;
            };

            await dialog.ShowDialogAsync().ConfigureAwait(true);
        }

        private static async Task<WeaponSkinCatalog?> LoadCatalogAsync(Package package)
        {
            WeaponSkinCatalog? catalog = null;

            using var dialog = new GenericProgressForm { Text = "Reading items_game.txt..." };

            dialog.OnProcess = cancellationToken =>
            {
                try
                {
                    catalog = WeaponSkinCatalog.Load(package, dialog, cancellationToken);
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

        /// <param name="contentRoot">The addon's content folder, which gets the material, its textures and the model.</param>
        /// <param name="gameRoot">The addon's game folder, which gets the compiled icon, or null to leave it out.</param>
        internal static void Export(string vpkPath, Package package, WeaponSkinExportRequest request, string contentRoot, string? gameRoot,
            IProgress<string> progress, CancellationToken cancellationToken)
        {
            var startTimestamp = Stopwatch.GetTimestamp();
            var (weapon, paintKit, wear, _) = request;

            Log.Info(nameof(WeaponSkinExporter), $"Weapon skin export of {weapon.Name} with {paintKit.Name} started to \"{contentRoot}\"");

            progress.Report($"Exporting {weapon.DisplayName} ({weapon.Name}) painted with {paintKit.DisplayName} ({paintKit.Name}), wear {wear:0.00}, to \"{contentRoot}\"");

            // The model extractor writes to the console
            var originalOut = Console.Out;
            var originalError = Console.Error;
            using var stdOutWriter = new CustomVmdlExporter.LogTextWriter(isError: false, progress);
            using var stdErrWriter = new CustomVmdlExporter.LogTextWriter(isError: true, progress);
            Console.SetOut(stdOutWriter);
            Console.SetError(stdErrWriter);

            var failed = 0;

            try
            {
                using var fileLoader = new GameFileLoader(package, vpkPath);

                var materialPath = FindPaintableMaterial(fileLoader, weapon.Model, paintKit.UseLegacyModel);
                progress.Report($"Painting {materialPath}{(paintKit.UseLegacyModel ? ", the legacy mesh's material" : string.Empty)}");

                var paint = PaintKitMaterial.Load(fileLoader, paintKit.CompositeMaterial, paintKit.Style - 1);
                progress.Report($"  {paint.Style} finish from {paint.PaintMaterial}");

                using var materialResource = fileLoader.LoadFileCompiled(materialPath) ?? throw new FileNotFoundException($"\"{materialPath}\" was not found");
                var material = materialResource.DataBlock as Material ?? throw new InvalidDataException($"\"{materialPath}\" is not a material");

                using var compositeInputsResource = material.StringAttributes.TryGetValue(CompositeInputsAttribute, out var compositeInputsPath)
                    ? fileLoader.LoadFileCompiled(compositeInputsPath)
                    : null;
                var compositeInputs = compositeInputsResource?.DataBlock as Material;

                if (compositeInputs == null)
                {
                    progress.Report("  ! the material has no composite inputs, projected patterns and masks are left out");
                }

                progress.Report($"Baking the finish into {weapon.TextureSize}x{weapon.TextureSize} textures...");
                using var baked = WeaponSkinBaker.Bake(fileLoader, weapon, paint, material, compositeInputs, wear, cancellationToken);

                var writtenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var vmatPath = GetOutputPath(contentRoot, Path.ChangeExtension(materialPath, "vmat"));

                progress.Report($"Writing {Path.ChangeExtension(materialPath, "vmat")}...");
                CustomVmatExporter.ExportMaterial(materialResource, vmatPath, contentRoot, fileLoader, progress, writtenFiles, cancellationToken);

                WriteBakedMaterial(vmatPath, contentRoot, paintKit, baked, fileLoader, progress);

                if (paintKit.UseLegacyModel)
                {
                    failed += ExportLegacyModel(weapon, vpkPath, contentRoot, fileLoader, progress);
                }

                if (request.ReplaceIcon)
                {
                    failed += ReplaceIcon(weapon, paintKit, gameRoot, fileLoader, progress);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                failed++;
                progress.Report($"FAILED: {e.Message}");
                Log.Error(nameof(WeaponSkinExporter), $"Failed to export {weapon.Name} with {paintKit.Name}: {e}");
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }

            var completedText = $"Export completed in {GenericProgressForm.FormatTime(Stopwatch.GetElapsedTime(startTimestamp))}";

            if (failed > 0)
            {
                completedText += $", {failed} step{(failed == 1 ? "" : "s")} failed";
            }

            Log.Info(nameof(WeaponSkinExporter), completedText);
            progress.Report(completedText);
        }

        /// <summary>
        /// The material a finish paints: the one of the model's regular or legacy mesh that the game composites finishes
        /// onto, which names its composite inputs.
        /// </summary>
        internal static string FindPaintableMaterial(IFileLoader fileLoader, string modelPath, bool legacy)
        {
            using var resource = fileLoader.LoadFileCompiled(modelPath) ?? throw new FileNotFoundException($"\"{modelPath}\" was not found");
            var model = resource.DataBlock as Model ?? throw new InvalidDataException($"\"{modelPath}\" is not a model");

            var meshes = model.GetEmbeddedMeshes()
                .Where(mesh => mesh.Name.Contains(LegacyMeshName, StringComparison.OrdinalIgnoreCase) == legacy)
                .ToList();

            if (meshes.Count == 0)
            {
                throw new InvalidDataException($"\"{modelPath}\" has no {(legacy ? "legacy" : "regular")} mesh");
            }

            string? firstMaterial = null;

            foreach (var mesh in meshes)
            {
                foreach (var sceneObject in mesh.Mesh.Data.GetArray("m_sceneObjects") ?? [])
                {
                    foreach (var drawCall in sceneObject.GetArray("m_drawCalls") ?? [])
                    {
                        if (Mesh.GetMaterialName(drawCall) is not { } materialName)
                        {
                            continue;
                        }

                        firstMaterial ??= materialName;

                        using var materialResource = fileLoader.LoadFileCompiled(materialName);

                        if (materialResource?.DataBlock is Material material && material.StringAttributes.ContainsKey(CompositeInputsAttribute))
                        {
                            return materialName;
                        }
                    }
                }
            }

            return firstMaterial ?? throw new InvalidDataException($"\"{modelPath}\" has no materials");
        }

        /// <summary>
        /// Writes the baked textures next to the decompiled material and points it at them instead of the weapon's own,
        /// which are deleted when nothing else uses them.
        /// </summary>
        private static void WriteBakedMaterial(string vmatPath, string contentRoot, PaintKit paintKit, BakedWeaponSkin baked,
            IFileLoader fileLoader, IProgress<string> progress)
        {
            var folder = Path.GetDirectoryName(vmatPath)!;
            var addonFolder = CustomVmatExporter.GetContentRelativePath(contentRoot, folder) ?? throw new InvalidDataException($"\"{folder}\" is not inside \"{contentRoot}\"");
            var baseName = $"{Path.GetFileNameWithoutExtension(vmatPath)}_{paintKit.Name}";

            var textures = new Dictionary<string, string>(StringComparer.Ordinal);

            string Write(string suffix, byte[] data)
            {
                var fileName = $"{baseName}_{suffix}.png";
                File.WriteAllBytes(Path.Combine(folder, fileName), data);

                var addonPath = addonFolder.Length == 0 ? fileName : $"{addonFolder}/{fileName}";
                progress.Report($"+ {addonPath}");

                return addonPath;
            }

            textures["TextureColor1"] = Write("color", Encode(baked.Color));
            textures["TextureRoughness1"] = Write("rough", Encode(baked.Roughness));
            textures["TextureMetalness1"] = Write("metal", Encode(baked.Metalness));

            if (baked.Normal != null)
            {
                textures["TextureNormal"] = Write("normal", ExtractTexture(fileLoader, baked.Normal));
            }

            if (baked.AmbientOcclusion != null)
            {
                textures["TextureAmbientOcclusion"] = Write("ao", ExtractTexture(fileLoader, baked.AmbientOcclusion));
            }

            var vmat = File.ReadAllText(vmatPath);
            var replaced = new List<string>();

            foreach (var (input, path) in textures)
            {
                var found = false;

                vmat = VmatTextureRegex().Replace(vmat, match =>
                {
                    if (!match.Groups["input"].Value.Equals(input, StringComparison.OrdinalIgnoreCase))
                    {
                        return match.Value;
                    }

                    found = true;
                    replaced.Add(match.Groups["path"].Value);

                    return $"{match.Groups["prefix"].Value}\"{path}\"";
                });

                if (!found)
                {
                    progress.Report($"  ! the material has no {input}, {path} is not used");
                }
            }

            File.WriteAllText(vmatPath, vmat);
            progress.Report($"  {Path.GetFileName(vmatPath)} now uses the painted textures");

            var stillUsed = VmatTextureRegex().Matches(vmat).Select(static match => match.Groups["path"].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var oldPath in replaced.Distinct(StringComparer.OrdinalIgnoreCase).Where(path => !stillUsed.Contains(path)))
            {
                var oldFile = Path.Combine(contentRoot, oldPath.Replace('/', Path.DirectorySeparatorChar));

                if (CustomVmatExporter.GetContentRelativePath(contentRoot, oldFile) != null && File.Exists(oldFile))
                {
                    File.Delete(oldFile);
                    progress.Report($"  - removed {oldPath}, it was painted over");
                }
            }
        }

        /// <summary>
        /// Exports the weapon's model with its regular mesh swapped for the legacy one, which the finish was made for.
        /// The body group keeps its choices, so whatever the game picks shows the legacy mesh.
        /// </summary>
        private static int ExportLegacyModel(WeaponDefinition weapon, string vpkPath, string contentRoot, GameFileLoader fileLoader, IProgress<string> progress)
        {
            progress.Report($"Exporting {weapon.Model} to show its legacy mesh...");

            var modelPath = weapon.Model + GameFileLoader.CompiledFileSuffix;
            using var resource = fileLoader.LoadFile(modelPath);

            if (resource == null)
            {
                progress.Report($"  FAILED {weapon.Model}: could not be read");
                return 1;
            }

            var result = CustomModelExporter.ExtractModels([(modelPath, resource)], contentRoot, fileLoader, new CustomModelExportOptions
            {
                DonorVpkPath = vpkPath,
            });

            foreach (var failure in result.Failures)
            {
                progress.Report($"  FAILED {failure.File}: {failure.Error}");
            }

            if (result.Failures.Count > 0)
            {
                return result.Failures.Count;
            }

            try
            {
                var vmdlPath = GetOutputPath(contentRoot, weapon.Model);
                var vmdl = ModelDocEditor.ShowMeshInsteadOf(File.ReadAllText(vmdlPath), static mesh => mesh.Contains(LegacyMeshName, StringComparison.OrdinalIgnoreCase), out var swapped);

                if (swapped.Count == 0)
                {
                    progress.Report($"  ! {weapon.Model} has no legacy mesh to show, it was exported as is");
                    return 0;
                }

                File.WriteAllText(vmdlPath, vmdl);
                progress.Report($"  {weapon.Model} ({string.Join(", ", swapped)})");

                return 0;
            }
            catch (Exception e)
            {
                progress.Report($"  FAILED to show the legacy mesh of {weapon.Model}: {e.Message}");
                Log.Error(nameof(WeaponSkinExporter), $"Failed to edit '{weapon.Model}': {e}");

                return 1;
            }
        }

        /// <summary>
        /// Copies the inventory image the game generated for the weapon with the finish over the weapon's own. The
        /// game only reads panorama images compiled, and it already is.
        /// </summary>
        private static int ReplaceIcon(WeaponDefinition weapon, PaintKit paintKit, string? gameRoot, GameFileLoader fileLoader, IProgress<string> progress)
        {
            if (weapon.Icon == null)
            {
                return 0;
            }

            if (gameRoot == null)
            {
                progress.Report("  ! the icon was not replaced, no game folder was chosen");
                return 0;
            }

            var source = WeaponSkinCatalog.GetPaintedIcon(weapon, paintKit);

            try
            {
                using var stream = fileLoader.GetFileStream(source) ?? throw new FileNotFoundException($"\"{source}\" was not found");
                var outputPath = GetOutputPath(gameRoot, weapon.Icon);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

                using (var output = File.Create(outputPath))
                {
                    stream.CopyTo(output);
                }

                progress.Report($"Replacing the inventory icon in \"{gameRoot}\"...");
                progress.Report($"  {weapon.Icon} <- {source}");

                return 0;
            }
            catch (Exception e)
            {
                progress.Report($"  FAILED {weapon.Icon} <- {source}: {e.Message}");
                Log.Error(nameof(WeaponSkinExporter), $"Failed to replace '{weapon.Icon}': {e}");

                return 1;
            }
        }

        private static byte[] ExtractTexture(IFileLoader fileLoader, string path)
        {
            using var resource = fileLoader.LoadFileCompiled(path) ?? throw new FileNotFoundException($"\"{path}\" was not found");
            var texture = resource.DataBlock as Texture ?? throw new InvalidDataException($"\"{path}\" is not a texture");
            using var bitmap = texture.GenerateBitmap();

            return TextureExtract.ToPngImage(bitmap);
        }

        private static byte[] Encode(SKBitmap bitmap)
        {
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);

            return data.ToArray();
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

        [GeneratedRegex(@"(?<prefix>""(?<input>Texture\w+)""\s+)""(?<path>[^""]*)""", RegexOptions.CultureInvariant)]
        private static partial Regex VmatTextureRegex();
    }
}
