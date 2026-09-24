using System.Linq;

namespace GUI.Types.Exporter.CharacterAssets
{
    /// <summary>
    /// An item equipped in one of the hero's slots, in one of its styles.
    /// </summary>
    /// <param name="SkinOverride">The material group picked by hand instead of the one the item and its style show.</param>
    /// <param name="Unusual">The unusual effect the item plays on its model, for items that come in unusual versions.</param>
    sealed record EquippedItem(EconItem Item, int Style, int? SkinOverride = null, UnusualEffect? Unusual = null)
    {
        /// <summary>
        /// The item's asset modifiers that apply in the chosen style, followed by the one that creates its unusual effect.
        /// </summary>
        public IEnumerable<AssetModifier> Modifiers
        {
            get
            {
                var modifiers = Item.AssetModifiers.Where(modifier => modifier.Style == null || modifier.Style == Style);

                return Unusual == null
                    ? modifiers
                    : modifiers.Append(new AssetModifier("particle_create", null, Unusual.Particle, null, LoadoutOnly: false) { IsUnusual = true });
            }
        }

        /// <summary>
        /// The material group the item's model is shown with.
        /// </summary>
        public int Skin => SkinOverride ?? DefaultSkin;

        /// <summary>
        /// The material group the item shows its model with in the chosen style, when none is picked by hand.
        /// </summary>
        public int DefaultSkin => Item.Styles.FirstOrDefault(style => style.Index == Style)?.Skin ?? Item.Skin;

        /// <summary>
        /// The chosen style's name, or null when the item has only the one look.
        /// </summary>
        public string? StyleName => Item.Styles.Count > 1 ? Item.Styles.FirstOrDefault(style => style.Index == Style)?.Name : null;
    }

    /// <summary>
    /// A particle an equipped item creates on the hero or on its own model, like an ambient effect.
    /// </summary>
    sealed record CreatedEffect(EquippedItem Item, AssetModifier Modifier)
    {
        /// <summary>The particle, as a package source path.</summary>
        public string Particle => CharacterLoadout.NormalizePath(Modifier.Modifier!);

        /// <summary>Whether this is the item's unusual effect, see <see cref="EquippedItem.Unusual"/>.</summary>
        public bool IsUnusual => Modifier.IsUnusual;
    }

    /// <summary>
    /// A hero with the items it wears, and what that makes it look like: which model the hero ends up with, and which
    /// model each item shows once other items have swapped it.
    /// </summary>
    sealed class CharacterLoadout
    {
        /// <summary>
        /// The body group the game switches on every model the hero wears to the arcana level, which is how arcanas pick
        /// their style's meshes and hide the parts of other items they replace.
        /// </summary>
        public const string ArcanaBodyGroup = "arcana";

        private readonly Dictionary<string, string> defaultModels;
        private readonly Dictionary<string, HeroSlot> slots;
        private readonly Dictionary<string, string> unitModels;

        public HeroDefinition Hero { get; }
        public IReadOnlyList<EquippedItem> Items { get; }

        private CharacterLoadout(HeroDefinition hero, IReadOnlyList<EquippedItem> items, Dictionary<string, string> defaultModels,
            Dictionary<string, HeroSlot> slots, Dictionary<string, string> unitModels)
        {
            Hero = hero;
            Items = items;
            this.defaultModels = defaultModels;
            this.slots = slots;
            this.unitModels = unitModels;
        }

        public static CharacterLoadout Create(ItemsGameCatalog catalog, HeroDefinition hero, IReadOnlyList<EquippedItem> items)
        {
            var defaultModels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var slots = new Dictionary<string, HeroSlot>(StringComparer.OrdinalIgnoreCase);
            var unitModels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var slot in catalog.GetSlots(hero))
            {
                slots.TryAdd(slot.Name, slot);

                if (catalog.GetDefaultItem(hero, slot.Name)?.ModelPlayer is { } model)
                {
                    defaultModels[slot.Name] = model;
                }

                foreach (var unit in slot.Units ?? [])
                {
                    if (catalog.GetUnitModel(unit) is { } unitModel)
                    {
                        unitModels.TryAdd(unit, unitModel);
                    }
                }
            }

            return new CharacterLoadout(hero, items, defaultModels, slots, unitModels);
        }

        /// <summary>
        /// Models the equipped items give the units the hero creates, e.g. a different boar for Beastmaster, with the
        /// unit's own model they replace.
        /// </summary>
        public IEnumerable<(EquippedItem Item, string Model, string DefaultModel)> UnitModelSwaps => Items
            .SelectMany(item => item.Modifiers
                .Where(modifier => modifier.Type == "entity_model" && IsModelPath(modifier.Modifier)
                    && modifier.Asset != null && unitModels.ContainsKey(modifier.Asset))
                .Select(modifier => (Item: item, Model: modifier.Modifier!, DefaultModel: unitModels[modifier.Asset!])))
            .DistinctBy(static swap => swap.DefaultModel, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The units' own models, for the units of slots something is equipped in that no item gives another model.
        /// </summary>
        public IEnumerable<string> UnitDefaultModels
        {
            get
            {
                var swapped = UnitModelSwaps.Select(static swap => swap.DefaultModel).ToHashSet(StringComparer.OrdinalIgnoreCase);

                return Items
                    .SelectMany(item => slots.GetValueOrDefault(item.Item.Slot)?.Units ?? [])
                    .Select(unit => unitModels.GetValueOrDefault(unit))
                    .OfType<string>()
                    .Where(model => !swapped.Contains(model))
                    .Distinct(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// The model the hero wears in the slot when nothing else is equipped, if it has one.
        /// </summary>
        public string? GetDefaultModel(string slot) => defaultModels.GetValueOrDefault(slot);

        /// <summary>
        /// Whether items of the slot are worn on the hero's body, see <see cref="HeroSlot.WornByHero"/>.
        /// </summary>
        public bool IsWornByHero(string slot) => slots.GetValueOrDefault(slot)?.WornByHero ?? true;

        /// <summary>
        /// The arcana level the equipped items' styles set, 0 without an arcana.
        /// </summary>
        public int ArcanaLevel => Items
            .SelectMany(static item => item.Modifiers)
            .Where(static modifier => modifier.Type == "arcana_level")
            .Select(static modifier => modifier.Value ?? 0)
            .DefaultIfEmpty(0)
            .Max();

        /// <summary>
        /// The particles the equipped items create in game, leaving out the ones only shown in the loadout screen.
        /// </summary>
        public IEnumerable<CreatedEffect> CreatedEffects => Items
            .SelectMany(static item => item.Modifiers
                .Where(static modifier => modifier is { Type: "particle_create", LoadoutOnly: false, Modifier: { } particle }
                    && particle.EndsWith(".vpcf", StringComparison.OrdinalIgnoreCase))
                .Select(modifier => new CreatedEffect(item, modifier)))
            .DistinctBy(static effect => (effect.Item, effect.Particle));

        /// <summary>
        /// Whether the game shows an effect with the equipped items. Items made to go with an arcana come with a version
        /// of the effect for each arcana level, and the one for the closest level at or below the equipped one shows,
        /// e.g. an arm's glow for arcana level 1 also shows at level 2 when there is none made for it.
        /// </summary>
        public bool IsShown(CreatedEffect effect)
        {
            if (effect.Modifier.RequiredArcanaLevel is not { } required)
            {
                return true;
            }

            var arcanaLevel = ArcanaLevel;
            var shownLevel = effect.Item.Modifiers
                .Where(modifier => modifier.Type == effect.Modifier.Type && modifier.RequiredArcanaLevel <= arcanaLevel)
                .Max(static modifier => modifier.RequiredArcanaLevel);

            return required == shownLevel;
        }

        /// <summary>
        /// The body group choices the equipped items set on a model, by body group name. The arcana body group is
        /// always included, so models that have one get it set to <see cref="ArcanaLevel"/>.
        /// </summary>
        public Dictionary<string, int> GetBodyGroupChoices(string model)
        {
            var choices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                [ArcanaBodyGroup] = ArcanaLevel,
            };

            foreach (var modifier in Items.SelectMany(static item => item.Modifiers))
            {
                if (modifier is { Type: "bodygroup_visibility", Modifier: { Length: > 0 } bodyGroup, Value: { } value } && IsSamePath(modifier.Asset, model))
                {
                    choices[bodyGroup] = value;
                }
            }

            return choices;
        }

        /// <summary>
        /// Models the equipped items add to the hero besides their own, which have no slot of their own.
        /// </summary>
        public IEnumerable<(EquippedItem Item, string Model)> AdditionalWearables => Items
            .SelectMany(static item => item.Modifiers
                .Where(static modifier => modifier.Type == "additional_wearable" && IsModelPath(modifier.Asset))
                .Select(modifier => (item, modifier.Asset!)));

        /// <summary>
        /// The models the hero stands on in the loadout screen, which the equipped items bring.
        /// </summary>
        public IEnumerable<string> Pedestals => Items
            .SelectMany(static item => item.Modifiers)
            .Where(static modifier => modifier.Type == "portrait_background_model" && IsModelPath(modifier.Asset))
            .Select(static modifier => modifier.Asset!)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The version of an icon the equipped items show, the game's own one when none of them swaps it.
        /// </summary>
        public IconChoice GetIcon(IconSlot slot)
        {
            var icon = Items
                .SelectMany(static item => item.Modifiers)
                .LastOrDefault(modifier => modifier.Type == slot.ModifierType && modifier.Modifier != null
                    && string.Equals(modifier.Asset, slot.Asset, StringComparison.OrdinalIgnoreCase))?
                .Modifier;

            return slot.Choices.FirstOrDefault(choice => choice.Icon.Equals(icon, StringComparison.OrdinalIgnoreCase)) ?? slot.Default;
        }

        /// <summary>
        /// The version of a sound event the equipped items play, the game's own one when none of them swaps it.
        /// </summary>
        public SoundChoice GetSound(SoundSlot slot)
        {
            var sound = Items
                .SelectMany(static item => item.Modifiers)
                .LastOrDefault(modifier => modifier is { Type: "sound", LoadoutOnly: false, Modifier: not null }
                    && string.Equals(modifier.Asset, slot.Event, StringComparison.OrdinalIgnoreCase))?
                .Modifier;

            return slot.Choices.FirstOrDefault(choice => choice.Event.Equals(sound, StringComparison.OrdinalIgnoreCase)) ?? slot.Default;
        }

        /// <summary>
        /// The response criteria the equipped items set, which switches the hero to another voice, e.g. an arcana's, or
        /// null when the hero keeps its own.
        /// </summary>
        public string? VoiceCriteria => Items
            .SelectMany(static item => item.Modifiers)
            .LastOrDefault(static modifier => modifier is { Type: "response_criteria", LoadoutOnly: false, Asset.Length: > 0 })?
            .Asset;

        /// <summary>
        /// The item that swaps the hero's own model, such as an arcana or a persona.
        /// </summary>
        public EquippedItem? HeroModelItem => Items.LastOrDefault(item => GetHeroModelSwap(item) != null);

        /// <summary>
        /// The model the hero is shown with.
        /// </summary>
        public string? HeroModel => HeroModelItem is { } item ? GetHeroModelSwap(item) : Hero.Model;

        /// <summary>
        /// The material group of the hero's model. A style's skin applies to the model the item swaps the hero's for,
        /// and to the hero's own model when the item has no model of its own. Items can also pick the hero's skin while
        /// wearing a model of their own, e.g. an arcana hair that sets the hero's body alight.
        /// </summary>
        public int HeroSkin => HeroModelItem?.Skin is { } skin and not 0
            ? skin
            : HeroSkinModifier
                ?? Items.LastOrDefault(item => item.Item.ModelPlayer == null && item.Skin != 0 && IsWornByHero(item.Item.Slot))?.Skin
                ?? 0;

        private int? HeroSkinModifier => Items
            .Where(item => IsWornByHero(item.Item.Slot))
            .SelectMany(static item => item.Modifiers)
            .LastOrDefault(static modifier => modifier is { Type: "model_skin", Asset: null, LoadoutOnly: false, Value: > 0 })?
            .Value;

        /// <summary>
        /// The activity modifiers the items worn by the hero set on it, which make the hero play the sequences made for
        /// them, e.g. an arcana's spawn and teleport animations.
        /// </summary>
        public List<ActivityModifier> ActivityModifiers => [.. Items
            .Where(item => IsWornByHero(item.Item.Slot))
            .SelectMany(static item => item.Modifiers)
            .Where(static modifier => modifier is { Type: "activity", LoadoutOnly: false, Modifier.Length: > 0 })
            .Select(static modifier => new ActivityModifier(
                modifier.Asset is null || modifier.Asset.Equals("ALL", StringComparison.OrdinalIgnoreCase) ? null : modifier.Asset,
                modifier.Modifier!))
            .Distinct()];

        /// <summary>
        /// The model an item shows. Items can swap their own model for a style, and other items can swap it for a
        /// version made to fit them, e.g. a refit of a head that would clip with an arcana.
        /// </summary>
        public string? GetItemModel(EquippedItem item)
        {
            var model = item.Item.ModelPlayer;

            if (model == null)
            {
                return null;
            }

            foreach (var modifier in Items.SelectMany(static equipped => equipped.Modifiers))
            {
                if (modifier is { Type: "model", Modifier: not null } && IsSamePath(modifier.Asset, model))
                {
                    return modifier.Modifier;
                }
            }

            return model;
        }

        private string? GetHeroModelSwap(EquippedItem item)
            => item.Modifiers.LastOrDefault(modifier => modifier is { Type: "entity_model", Modifier: not null }
                && Hero.Name.Equals(modifier.Asset, StringComparison.OrdinalIgnoreCase))?.Modifier;

        public static bool IsSamePath(string? a, string? b)
            => a != null && b != null && NormalizePath(a).Equals(NormalizePath(b), StringComparison.OrdinalIgnoreCase);

        private static bool IsModelPath(string? path) => path != null && path.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase);

        public static string NormalizePath(string path) => path.Replace('\\', '/').TrimStart('/');
    }
}
