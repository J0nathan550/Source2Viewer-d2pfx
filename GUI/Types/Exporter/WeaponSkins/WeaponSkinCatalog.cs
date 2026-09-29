using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using GUI.Utils;
using ValveKeyValue;
using ValvePak;
using ValveResourceFormat.IO;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Exporter.WeaponSkins
{
    /// <summary>
    /// A Counter-Strike 2 weapon that can wear paint kits.
    /// </summary>
    /// <param name="Name">Its class name, e.g. "weapon_famas".</param>
    /// <param name="DisplayName">Its localized name.</param>
    /// <param name="Model">The model it is shown with, in first and third person.</param>
    /// <param name="WeaponLength">Its length in units, which projected patterns are sized by.</param>
    /// <param name="UvScale">How much of its texture space its model covers, which patterns laid out in it are sized by.</param>
    /// <param name="TextureSize">Size of the textures the game generates to paint it.</param>
    /// <param name="Icon">Its inventory image.</param>
    sealed record WeaponDefinition(string Name, string DisplayName, string Model, float WeaponLength, float UvScale, int TextureSize, string? Icon);

    /// <summary>
    /// A weapon finish, as items_game.txt declares it.
    /// </summary>
    /// <param name="Id">Its paint kit index.</param>
    /// <param name="Name">Its internal name, e.g. "cu_famas_pulse".</param>
    /// <param name="DisplayName">Its localized name.</param>
    /// <param name="Style">How it is painted on, see <see cref="PaintStyle"/>.</param>
    /// <param name="UseLegacyModel">Whether it paints the weapon's legacy mesh, whose texture layout it was made for.</param>
    /// <param name="WearMin">The least wear it can have.</param>
    /// <param name="WearMax">The most wear it can have.</param>
    /// <param name="CompositeMaterial">The composite material that paints it on the weapon.</param>
    sealed record PaintKit(int Id, string Name, string DisplayName, int Style, bool UseLegacyModel, float WearMin, float WearMax, string CompositeMaterial);

    /// <summary>
    /// The weapons and paint kits of a Counter-Strike 2 package, read from its items_game.txt.
    /// </summary>
    sealed partial class WeaponSkinCatalog
    {
        public const string ItemsGamePath = "scripts/items/items_game.txt";

        private const string LocalizationPath = "resource/csgo_english.txt";
        private const string PaintsFolder = "weapons/paints/";
        private const string GeneratedIconsFolder = "panorama/images/econ/default_generated";
        private const string CompositeMaterialType = "vcompmat_c";

        private readonly Dictionary<string, List<PaintKit>> paintKitsByWeapon;

        public IReadOnlyList<WeaponDefinition> Weapons { get; }

        private WeaponSkinCatalog(List<WeaponDefinition> weapons, Dictionary<string, List<PaintKit>> paintKitsByWeapon)
        {
            Weapons = weapons;
            this.paintKitsByWeapon = paintKitsByWeapon;
        }

        /// <summary>
        /// Whether the package holds the items_game.txt and the composite materials weapon finishes are made of,
        /// which tells it apart from Dota 2's.
        /// </summary>
        public static bool IsAvailable(Package package)
            => package.FindEntry(ItemsGamePath) != null && package.Entries?.ContainsKey(CompositeMaterialType) == true;

        /// <summary>
        /// The paint kits the game has for a weapon, by name.
        /// </summary>
        public IReadOnlyList<PaintKit> GetPaintKits(WeaponDefinition weapon)
            => paintKitsByWeapon.TryGetValue(weapon.Name, out var paintKits) ? paintKits : [];

        /// <summary>
        /// The inventory image the game generated for a weapon with a paint kit.
        /// </summary>
        /// <param name="wear">"light", "medium" or "heavy".</param>
        public static string GetPaintedIcon(WeaponDefinition weapon, PaintKit paintKit, string wear = "light")
            => $"{GeneratedIconsFolder}/{weapon.Name}_{paintKit.Name}_{wear}_png.vtex_c";

        public static WeaponSkinCatalog Load(Package package, IProgress<string>? progress, CancellationToken cancellationToken)
        {
            progress?.Report("Reading localization...");
            var localization = LoadLocalization(package);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Reading {ItemsGamePath}...");

            var itemsGame = ReadKeyValues(package, ItemsGamePath)
                ?? throw new FileNotFoundException($"\"{ItemsGamePath}\" was not found in the package");

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("Collecting weapons and paint kits...");

            // The file repeats its sections, every copy adds to the others
            var prefabs = new Dictionary<string, KVObject>(StringComparer.OrdinalIgnoreCase);

            foreach (var prefab in GetSections(itemsGame, "prefabs"))
            {
                prefabs.TryAdd(prefab.Key, prefab.Value);
            }

            var weapons = new Dictionary<string, WeaponDefinition>(StringComparer.OrdinalIgnoreCase);

            foreach (var (_, itemData) in GetSections(itemsGame, "items"))
            {
                if (ReadWeapon(itemData, prefabs, localization) is { } weapon)
                {
                    weapons.TryAdd(weapon.Name, weapon);
                }
            }

            var compositeMaterials = FindCompositeMaterials(package);
            var paintKits = new Dictionary<string, PaintKit>(StringComparer.OrdinalIgnoreCase);

            foreach (var (id, paintKitData) in GetSections(itemsGame, "paint_kits"))
            {
                if (ReadPaintKit(id, paintKitData, compositeMaterials, localization) is { } paintKit)
                {
                    paintKits.TryAdd(paintKit.Name, paintKit);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            var pairs = FindWeaponPaintKits(package, itemsGame, weapons, paintKits);
            var paintKitsByWeapon = new Dictionary<string, List<PaintKit>>(StringComparer.OrdinalIgnoreCase);

            foreach (var (weaponName, paintKitName) in pairs)
            {
                if (!paintKitsByWeapon.TryGetValue(weaponName, out var list))
                {
                    list = [];
                    paintKitsByWeapon.Add(weaponName, list);
                }

                list.Add(paintKits[paintKitName]);
            }

            foreach (var list in paintKitsByWeapon.Values)
            {
                list.Sort(static (a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.DisplayName, b.DisplayName));
            }

            var weaponList = weapons.Values.Where(weapon => paintKitsByWeapon.ContainsKey(weapon.Name)).ToList();
            weaponList.Sort(static (a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.DisplayName, b.DisplayName));

            Log.Info(nameof(WeaponSkinCatalog), $"Found {weaponList.Count} weapons and {paintKits.Count} paint kits, {pairs.Count} of them made for a weapon");

            return new WeaponSkinCatalog(weaponList, paintKitsByWeapon);
        }

        private static IEnumerable<KeyValuePair<string, KVObject>> GetSections(KVObject itemsGame, string name)
        {
            foreach (var (key, section) in itemsGame)
            {
                if (!key.Equals(name, StringComparison.OrdinalIgnoreCase) || section.ValueType != KVValueType.Collection)
                {
                    continue;
                }

                foreach (var (childKey, child) in section)
                {
                    if (child.ValueType == KVValueType.Collection)
                    {
                        yield return new(childKey, child);
                    }
                }
            }
        }

        private static WeaponDefinition? ReadWeapon(KVObject itemData, Dictionary<string, KVObject> prefabs, Dictionary<string, string> localization)
        {
            var name = GetValue(itemData, "name");

            if (name == null || !name.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var model = FindInPrefabs(itemData, prefabs, data => GetValue(data, "model_player"));
            var paintableMaterial = FindInPrefabs(itemData, prefabs,
                data => data.GetSubCollection("paint_data") is { ValueType: KVValueType.Collection } paintData
                    ? paintData.GetSubCollection("PaintableMaterial0")
                    : null);

            if (model == null || paintableMaterial is not { ValueType: KVValueType.Collection })
            {
                return null;
            }

            var displayName = Localize(localization, FindInPrefabs(itemData, prefabs, data => GetValue(data, "item_name"))) ?? name;
            var image = FindInPrefabs(itemData, prefabs, data => GetValue(data, "image_inventory"));

            return new WeaponDefinition(
                name,
                displayName,
                model,
                ParseFloat(GetValue(paintableMaterial, "WeaponLength")) ?? 32f,
                ParseFloat(GetValue(paintableMaterial, "UVScale")) ?? 1f,
                (int)(ParseFloat(GetValue(paintableMaterial, "ViewmodelDim")) ?? 2048f),
                image != null ? $"panorama/images/{image}_png.vtex_c" : null);
        }

        private static PaintKit? ReadPaintKit(string id, KVObject data, Dictionary<string, string> compositeMaterials, Dictionary<string, string> localization)
        {
            var name = GetValue(data, "name");

            if (name == null || !int.TryParse(id, CultureInfo.InvariantCulture, out var index) || index == 0)
            {
                return null;
            }

            // Legacy finishes do not name their composite material, it is the one named after them
            var compositeMaterial = GetValue(data, "composite_material_path") ?? compositeMaterials.GetValueOrDefault(name);

            if (compositeMaterial == null)
            {
                return null;
            }

            return new PaintKit(
                index,
                name,
                Localize(localization, GetValue(data, "description_tag")) ?? name,
                (int)(ParseFloat(GetValue(data, "style")) ?? 0f),
                GetValue(data, "use_legacy_model") == "1",
                ParseFloat(GetValue(data, "wear_remap_min")) ?? 0f,
                ParseFloat(GetValue(data, "wear_remap_max")) ?? 1f,
                compositeMaterial);
        }

        /// <summary>
        /// The composite materials of weapon finishes by file name, without the compiled suffix.
        /// </summary>
        private static Dictionary<string, string> FindCompositeMaterials(Package package)
        {
            var materials = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (package.Entries?.TryGetValue(CompositeMaterialType, out var entries) != true || entries == null)
            {
                return materials;
            }

            foreach (var entry in entries)
            {
                var path = entry.GetFullPath();

                if (path.StartsWith(PaintsFolder, StringComparison.OrdinalIgnoreCase))
                {
                    materials.TryAdd(entry.FileName, path[..^2]);
                }
            }

            return materials;
        }

        /// <summary>
        /// Which weapon each paint kit is made for. The game generates an inventory image for every such pair, and loot
        /// lists name them as "[paint kit]weapon".
        /// </summary>
        private static HashSet<(string Weapon, string PaintKit)> FindWeaponPaintKits(Package package, KVObject itemsGame,
            Dictionary<string, WeaponDefinition> weapons, Dictionary<string, PaintKit> paintKits)
        {
            var pairs = new HashSet<(string, string)>();

            // Longest names first, "weapon_m4a1_silencer" has to win over "weapon_m4a1"
            var weaponNames = weapons.Keys.OrderByDescending(static name => name.Length).ToList();

            if (package.Entries?.TryGetValue("vtex_c", out var textures) == true && textures != null)
            {
                const string Suffix = "_light_png";

                foreach (var entry in textures)
                {
                    if (!entry.DirectoryName.Equals(GeneratedIconsFolder, StringComparison.OrdinalIgnoreCase)
                        || !entry.FileName.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var name = entry.FileName[..^Suffix.Length];

                    foreach (var weaponName in weaponNames)
                    {
                        if (name.Length > weaponName.Length + 1 && name.StartsWith(weaponName, StringComparison.OrdinalIgnoreCase) && name[weaponName.Length] == '_'
                            && paintKits.ContainsKey(name[(weaponName.Length + 1)..]))
                        {
                            pairs.Add((weaponName, paintKits[name[(weaponName.Length + 1)..]].Name));
                            break;
                        }
                    }
                }
            }

            foreach (var (_, lootList) in GetSections(itemsGame, "client_loot_lists"))
            {
                foreach (var (key, _) in lootList)
                {
                    var match = LootListEntryRegex().Match(key);

                    if (match.Success && weapons.TryGetValue(match.Groups["weapon"].Value, out var weapon)
                        && paintKits.TryGetValue(match.Groups["paintKit"].Value, out var paintKit))
                    {
                        pairs.Add((weapon.Name, paintKit.Name));
                    }
                }
            }

            return pairs;
        }

        /// <summary>
        /// Reads something from an item, or else from the prefabs it is made from, the nearest first.
        /// </summary>
        private static T? FindInPrefabs<T>(KVObject data, Dictionary<string, KVObject> prefabs, Func<KVObject, T?> read, int depth = 0) where T : class
        {
            if (read(data) is { } value)
            {
                return value;
            }

            if (depth > 16 || GetValue(data, "prefab") is not { } prefabNames)
            {
                return null;
            }

            foreach (var prefabName in prefabNames.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (prefabs.TryGetValue(prefabName, out var prefab) && FindInPrefabs(prefab, prefabs, read, depth + 1) is { } prefabValue)
                {
                    return prefabValue;
                }
            }

            return null;
        }

        /// <summary>
        /// Reads the localized names line by line. The file has escape sequences the KeyValues reader rejects, and
        /// every token sits on a line of its own.
        /// </summary>
        private static Dictionary<string, string> LoadLocalization(Package package)
        {
            var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (package.FindEntry(LocalizationPath) is not { } entry)
                {
                    return tokens;
                }

                using var stream = GameFileLoader.GetPackageEntryStream(package, entry);
                using var reader = new StreamReader(stream);

                while (reader.ReadLine() is { } line)
                {
                    var match = LocalizationTokenRegex().Match(line);

                    if (match.Success)
                    {
                        tokens.TryAdd(match.Groups["key"].Value, match.Groups["value"].Value.Replace("\\\"", "\"", StringComparison.Ordinal));
                    }
                }
            }
            catch (Exception e)
            {
                // Names fall back to the raw keys, which is still usable
                Log.Warn(nameof(WeaponSkinCatalog), $"Failed to read \"{LocalizationPath}\": {e.Message}");
            }

            return tokens;
        }

        private static string? Localize(Dictionary<string, string> localization, string? token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return null;
            }

            var key = token.StartsWith('#') ? token[1..] : token;

            return localization.TryGetValue(key, out var text) && text.Length > 0 ? text : null;
        }

        private static KVObject? ReadKeyValues(Package package, string path)
        {
            var entry = package.FindEntry(path);

            if (entry == null)
            {
                return null;
            }

            using var stream = GameFileLoader.GetPackageEntryStream(package, entry);

            return KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream, KVSerializerOptions.DefaultOptions);
        }

        private static float? ParseFloat(string? value)
            => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : null;

        /// <summary>
        /// Reads a value as text. The KV1 reader turns numeric looking values into numbers.
        /// </summary>
        private static string? GetValue(KVObject data, string key)
            => data.TryGetValue(key, out var value) ? ToText(value) : null;

        private static string? ToText(KVObject value)
            => value.ValueType is KVValueType.Collection or KVValueType.Array or KVValueType.Null ? null : value.ToString(CultureInfo.InvariantCulture);

        [GeneratedRegex(@"^\s*""(?<key>[^""]+)""\s+""(?<value>(?:[^""\\]|\\.)*)""", RegexOptions.CultureInvariant)]
        private static partial Regex LocalizationTokenRegex();

        [GeneratedRegex(@"^\[(?<paintKit>[^\]]+)\](?<weapon>weapon_\w+)$", RegexOptions.CultureInvariant)]
        private static partial Regex LootListEntryRegex();
    }
}
