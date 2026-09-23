using System.Linq;

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

        /// <summary>What the asset modifiers name as the asset: the hero, the ability or the shop item.</summary>
        public required string Asset { get; init; }

        /// <summary>
        /// The folders under panorama/images the image is shown from, e.g. both the portrait and the hero selection
        /// folder for the hero's portrait.
        /// </summary>
        public required IReadOnlyList<string> Folders { get; init; }

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
        /// The compiled image of an icon, e.g. "panorama/images/spellicons/drow_ranger_multishot_png.vtex_c".
        /// </summary>
        public static string GetImagePath(string folder, string icon) => $"panorama/images/{folder}/{CharacterLoadout.NormalizePath(icon)}_png.vtex_c";

        /// <summary>
        /// The hero's icons that at least one of its items swaps for a version of its own.
        /// </summary>
        /// <param name="exists">Whether a package path exists, versions without an image are left out.</param>
        public static List<IconSlot> GetSlots(ItemsGameCatalog catalog, HeroDefinition hero, Func<string, bool> exists)
        {
            var items = catalog.GetItems(hero);
            var slots = new List<IconSlot>();

            IEnumerable<string> GetAssets(string type) => items
                .SelectMany(static item => item.AssetModifiers)
                .Where(modifier => modifier.Type == type && !string.IsNullOrEmpty(modifier.Asset))
                .Select(static modifier => modifier.Asset!)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            void AddSlot(string displayName, string group, string type, string asset, string[] folders)
            {
                var slot = new IconSlot
                {
                    DisplayName = displayName,
                    Group = group,
                    ModifierType = type,
                    Asset = asset,
                    Folders = folders,
                };

                slot.Choices.Add(new IconChoice(asset, "Default"));

                foreach (var item in items)
                {
                    foreach (var modifier in item.AssetModifiers)
                    {
                        if (modifier.Type != type
                            || modifier.Modifier is not { Length: > 0 } icon
                            || !string.Equals(modifier.Asset, asset, StringComparison.OrdinalIgnoreCase)
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
            AddSlot("Minimap icon", HeroGroup, "icon_replacement_hero_minimap", hero.Name, ["heroes/icons"]);

            // Items can also swap icons of abilities the hero script no longer lists, such as ones a facet grants
            foreach (var ability in hero.Abilities.Concat(GetAssets("ability_icon")).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                AddSlot(catalog.Localize($"DOTA_Tooltip_ability_{ability}") ?? ability, AbilityGroup, "ability_icon", ability, ["spellicons"]);
            }

            foreach (var shopItem in GetAssets("inventory_icon"))
            {
                AddSlot(catalog.Localize($"DOTA_Tooltip_ability_item_{shopItem}") ?? shopItem, ShopItemGroup, "inventory_icon", shopItem, ["items"]);
            }

            return slots;
        }
    }
}
