using System.Linq;
using System.Text.RegularExpressions;

namespace GUI.Types.Exporter.CharacterAssets
{
    /// <summary>
    /// One version of an icon.
    /// </summary>
    /// <param name="Icon">The image as items_game.txt names it: relative to the icon's folders and without extension.</param>
    /// <param name="DisplayName">What it is, the item it comes from for anything but the game's own icon.</param>
    sealed record IconChoice(string Icon, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// A compiled panorama image written over another one, both as package paths.
    /// </summary>
    sealed record IconReplacement(string Source, string Target);

    /// <summary>
    /// A sprite of the game's sprite sheets shown in place of another one, both as their names in
    /// <see cref="CharacterIcons.SpriteSheetFile"/>.
    /// </summary>
    sealed record SpriteReplacement(string Source, string Target);

    /// <summary>
    /// A panorama image the game shows for the hero, which items can swap for one of their own.
    /// </summary>
    sealed class IconSlot
    {
        /// <summary>What the image shows, e.g. "Portrait" or an ability's name.</summary>
        public required string DisplayName { get; init; }

        /// <summary>What kind of image it is, which the slots are listed by.</summary>
        public required string Group { get; init; }

        /// <summary>The asset modifier type that swaps the image.</summary>
        public required string ModifierType { get; init; }

        /// <summary>Whose image it is: the hero, the ability or the shop item.</summary>
        public required string Asset { get; init; }

        /// <summary>
        /// The ability whose swaps apply to this one's image too, for an ability no item swaps that shows the same
        /// picture, e.g. one that took over another's place in the hero's abilities along with its icon.
        /// </summary>
        public string? SharesSwapsOf { get; init; }

        /// <summary>What the asset modifiers that swap the image name as the asset.</summary>
        public string SwappedAsset => SharesSwapsOf ?? Asset;

        /// <summary>
        /// The folders under panorama/images the image is shown from, e.g. both the portrait and the hero selection
        /// folder for the hero's portrait.
        /// </summary>
        public required IReadOnlyList<string> Folders { get; init; }

        /// <summary>
        /// What the names of the image's sprites in <see cref="CharacterIcons.SpriteSheetFile"/> start with, for images
        /// the game also draws from its sprite sheets, e.g. the hero's icon on the minimap.
        /// </summary>
        public string? SpritePrefix { get; init; }

        /// <summary>The versions to choose from, the game's own one first.</summary>
        public List<IconChoice> Choices { get; } = [];

        public IconChoice Default => Choices[0];

        /// <summary>
        /// The compiled images written for a version, one for each folder that has that version.
        /// </summary>
        public IEnumerable<IconReplacement> GetReplacements(IconChoice choice, Func<string, bool> exists)
        {
            if (choice == Default)
            {
                yield break;
            }

            foreach (var folder in Folders)
            {
                var source = CharacterIcons.GetImagePath(folder, choice.Icon);

                if (exists(source))
                {
                    yield return new IconReplacement(source, CharacterIcons.GetImagePath(folder, Default.Icon));
                }
            }
        }

        /// <summary>
        /// The sprite shown in place of the game's own one for a version, or null when the image has no sprite. The
        /// game's own version replaces the sprite with itself, which restores it in a copy an earlier export changed.
        /// </summary>
        public SpriteReplacement? GetSpriteReplacement(IconChoice choice)
            => SpritePrefix == null ? null : new SpriteReplacement(SpritePrefix + choice.Icon, SpritePrefix + Default.Icon);
    }

    /// <summary>
    /// Finds the icons shown for a hero, and the versions of them its items come with.
    /// </summary>
    static class CharacterIcons
    {
        public const string HeroGroup = "Hero";
        public const string AbilityGroup = "Abilities";
        public const string ShopItemGroup = "Shop items";

        /// <summary>
        /// Where the sprites the game draws parts of its HUD with are cut out of its sprite sheets. The minimap draws hero
        /// icons from these rather than from the panorama images.
        /// </summary>
        public const string SpriteSheetFile = "scripts/mod_textures.txt";

        /// <summary>
        /// The compiled image of an icon, e.g. "panorama/images/spellicons/drow_ranger_multishot_png.vtex_c".
        /// </summary>
        public static string GetImagePath(string folder, string icon) => $"panorama/images/{folder}/{CharacterLoadout.NormalizePath(icon)}_png.vtex_c";

        /// <summary>
        /// The hero's icons that at least one of its items swaps for a version of its own.
        /// </summary>
        /// <param name="exists">Whether a package path exists, versions without an image are left out.</param>
        /// <param name="readPixels">
        /// The decoded pixels of a package image, or null when it cannot be read, to find abilities that show the same
        /// picture.
        /// </param>
        public static List<IconSlot> GetSlots(ItemsGameCatalog catalog, HeroDefinition hero, Func<string, bool> exists,
            Func<string, byte[]?> readPixels)
        {
            var items = catalog.GetItems(hero);
            var slots = new List<IconSlot>();

            IEnumerable<string> GetAssets(string type) => items
                .SelectMany(static item => item.AssetModifiers)
                .Where(modifier => modifier.Type == type && !string.IsNullOrEmpty(modifier.Asset))
                .Select(static modifier => modifier.Asset!)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            void AddSlot(string displayName, string group, string type, string asset, string[] folders, string? spritePrefix = null,
                string? sharesSwapsOf = null)
            {
                var slot = new IconSlot
                {
                    DisplayName = displayName,
                    Group = group,
                    ModifierType = type,
                    Asset = asset,
                    SharesSwapsOf = sharesSwapsOf,
                    Folders = folders,
                    SpritePrefix = spritePrefix,
                };

                slot.Choices.Add(new IconChoice(asset, "Default"));

                foreach (var item in items)
                {
                    foreach (var modifier in item.AssetModifiers)
                    {
                        if (modifier.Type != type
                            || modifier.Modifier is not { Length: > 0 } icon
                            || !string.Equals(modifier.Asset, slot.SwappedAsset, StringComparison.OrdinalIgnoreCase)
                            || slot.Choices.Any(choice => choice.Icon.Equals(icon, StringComparison.OrdinalIgnoreCase))
                            || !exists(GetImagePath(folders[0], icon)))
                        {
                            continue;
                        }

                        var styleName = modifier.Style is { } style && item.Styles.Count > 1
                            ? item.Styles.FirstOrDefault(itemStyle => itemStyle.Index == style)?.Name
                            : null;

                        slot.Choices.Add(new IconChoice(icon, styleName != null ? $"{item.Name} ({styleName})" : item.Name));
                    }
                }

                if (slot.Choices.Count > 1)
                {
                    slots.Add(slot);
                }
            }

            AddSlot("Portrait", HeroGroup, "icon_replacement_hero", hero.Name, ["heroes", "heroes/selection"]);
            AddSlot("Minimap icon", HeroGroup, "icon_replacement_hero_minimap", hero.Name, ["heroes/icons"], "minimap_heroicon_");

            var swappedAbilities = GetAssets("ability_icon").ToList();
            var pixels = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);

            byte[]? GetPixels(string ability)
            {
                if (!pixels.TryGetValue(ability, out var abilityPixels))
                {
                    var path = GetImagePath("spellicons", ability);
                    pixels[ability] = abilityPixels = exists(path) ? readPixels(path) : null;
                }

                return abilityPixels;
            }

            // An ability can take over another's place along with its icon, which items were only ever made to swap
            // under the other ability's name, e.g. a passive that replaced one that became innate
            string? FindSamePicture(string ability)
            {
                if (swappedAbilities.Contains(ability, StringComparer.OrdinalIgnoreCase) || GetPixels(ability) is not { } abilityPixels)
                {
                    return null;
                }

                return swappedAbilities.FirstOrDefault(other => GetPixels(other) is { } otherPixels && otherPixels.AsSpan().SequenceEqual(abilityPixels));
            }

            // Items can also swap icons of abilities the hero script no longer lists, such as ones a facet grants
            foreach (var ability in hero.Abilities.Concat(swappedAbilities).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                AddSlot(catalog.Localize($"DOTA_Tooltip_ability_{ability}") ?? ability, AbilityGroup, "ability_icon", ability, ["spellicons"],
                    sharesSwapsOf: FindSamePicture(ability));
            }

            foreach (var shopItem in GetAssets("inventory_icon"))
            {
                AddSlot(catalog.Localize($"DOTA_Tooltip_ability_item_{shopItem}") ?? shopItem, ShopItemGroup, "inventory_icon", shopItem, ["items"]);
            }

            return slots;
        }

        /// <summary>
        /// Rewrites the sprite sheet definitions so each replaced sprite is cut out where its replacement is.
        /// </summary>
        /// <param name="original">The game's own <see cref="SpriteSheetFile"/>, which the sprites are taken from.</param>
        /// <param name="current">The text to write them into: the game's own, or a copy an earlier export changed.</param>
        /// <returns>The rewritten text, and the replacements left out because a sprite is not defined.</returns>
        public static (string Text, List<SpriteReplacement> Missing) ReplaceSprites(string original, string current,
            IEnumerable<SpriteReplacement> replacements)
        {
            var text = current;
            var missing = new List<SpriteReplacement>();

            foreach (var replacement in replacements)
            {
                var source = FindSprite(original, replacement.Source);
                var target = FindSprite(text, replacement.Target);

                if (source == null || target == null)
                {
                    missing.Add(replacement);
                    continue;
                }

                text = string.Concat(text.AsSpan(0, target.Index), source.Value, text.AsSpan(target.Index + target.Length));
            }

            return (text, missing);
        }

        // A sprite's definition only holds values, so it ends at the first closing brace
        private static Group? FindSprite(string definitions, string name)
        {
            var match = Regex.Match(definitions, $"\"{Regex.Escape(name)}\"\\s*(\\{{[^{{}}]*\\}})",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

            return match.Success ? match.Groups[1] : null;
        }
    }
}
