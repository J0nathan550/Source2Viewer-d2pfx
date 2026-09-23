using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using ValveKeyValue;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Exporter.CharacterAssets
{
    /// <summary>
    /// What to export for a character besides the selected items' own models.
    /// </summary>
    sealed class CharacterExportOptions
    {
        /// <summary>The hero's model, or the one an equipped item swaps it for.</summary>
        public bool HeroModel { get; set; } = true;

        /// <summary>Models the selected items wear or swap in.</summary>
        public bool ItemModels { get; set; } = true;

        /// <summary>Particles the selected items create or swap in.</summary>
        public bool ItemParticles { get; set; } = true;

        /// <summary>Every particle in the hero's particle folder, which covers its abilities.</summary>
        public bool HeroParticles { get; set; }

        /// <summary>The sound event files of the sound events the selected items swap in.</summary>
        public bool ItemSounds { get; set; } = true;

        /// <summary>The hero's game sound events file.</summary>
        public bool HeroSounds { get; set; } = true;

        /// <summary>The hero's voice line events file.</summary>
        public bool HeroVoice { get; set; } = true;

        /// <summary>
        /// The sounds the exported sound events play. Without them, only the sound event files are written, which keep
        /// pointing at the sounds in the game.
        /// </summary>
        public bool IncludeAudio { get; set; }

        /// <summary>
        /// Writes <see cref="IconReplacements"/> into the game folder: the compiled images the game already has, renamed
        /// over the hero's own icons.
        /// </summary>
        public bool Icons { get; set; } = true;

        /// <summary>The icons to write when <see cref="Icons"/> is set.</summary>
        public IReadOnlyList<IconReplacement> IconReplacements { get; set; } = [];

        /// <summary>
        /// The models the hero stands on in the loadout screen, and the particles items only play there.
        /// </summary>
        public bool Pedestal { get; set; }

        /// <summary>
        /// Writes the equipped look over the hero's default assets, so it shows without the items being equipped: the
        /// arcana or persona model as the hero's model, chosen items over the default items' models, particles the items
        /// swap in over the ones they replace, and the particles items create added to their models.
        /// </summary>
        public bool ReplaceDefaults { get; set; } = true;

        /// <summary>
        /// Also replaces particles every hero uses, like the blink dagger or stun effects, which then change for all heroes.
        /// </summary>
        public bool ReplaceSharedParticles { get; set; }
    }

    /// <summary>
    /// A model written over another one, see <see cref="CharacterExportOptions.ReplaceDefaults"/>.
    /// </summary>
    /// <param name="Source">The model whose decompiled source is written.</param>
    /// <param name="Target">The model it is written as.</param>
    /// <param name="Skin">The material group to make the default one.</param>
    /// <param name="Particles">Particles the model should create itself, since the items that create them are not equipped.</param>
    sealed record ModelReplacement(string Source, string Target, int Skin, List<string> Particles)
    {
        /// <summary>The body group choices to pick, since nothing switches them without the items equipped.</summary>
        public Dictionary<string, int> BodyGroups { get; init; } = [];

        /// <summary>Models whose meshes are added to this one, since they have no model of their own to be written over.</summary>
        public List<MergedModel> Merged { get; init; } = [];
    }

    /// <summary>
    /// A model whose meshes are added to another one, see <see cref="ModelReplacement.Merged"/>.
    /// </summary>
    sealed record MergedModel(string Model, int Skin, Dictionary<string, int> BodyGroups);

    /// <summary>
    /// A particle written over another one, see <see cref="CharacterExportOptions.ReplaceDefaults"/>.
    /// </summary>
    sealed record ParticleReplacement(string Source, string Target);

    /// <summary>
    /// Every file a character export writes, grouped by how it gets decompiled. Paths are package paths of compiled files
    /// (ending in "_c"), except <see cref="RawFiles"/> and the replacements, which use source paths.
    /// </summary>
    sealed class CharacterExportPlan
    {
        /// <summary>Models, decompiled with the custom model extractor, which also writes their meshes, animations and physics.</summary>
        public List<string> Models { get; } = [];

        /// <summary>Everything else that is compiled, decompiled with the built-in decompiler.</summary>
        public List<string> Resources { get; } = [];

        /// <summary>Files that are not compiled, copied as they are.</summary>
        public List<string> RawFiles { get; } = [];

        /// <summary>Models written over the default ones once everything is exported.</summary>
        public List<ModelReplacement> ModelReplacements { get; } = [];

        /// <summary>Particles written over the default ones once everything is exported.</summary>
        public List<ParticleReplacement> ParticleReplacements { get; } = [];

        /// <summary>Particle swaps left out because every hero uses the particle they replace.</summary>
        public List<ParticleReplacement> SkippedSharedParticles { get; } = [];

        /// <summary>Equipped models that are neither worn by the hero nor written over another model, e.g. a summon's.</summary>
        public List<string> UnplacedModels { get; } = [];

        /// <summary>Compiled icons copied into the game folder, as package paths.</summary>
        public List<IconReplacement> IconReplacements { get; } = [];

        /// <summary>Things worth knowing about how the loadout was exported.</summary>
        public List<string> Notes { get; } = [];

        /// <summary>References that could not be found in the game files.</summary>
        public List<string> Missing { get; } = [];
    }

    /// <summary>
    /// Works out which files make up a hero with a set of items: the models, particles and sound events the items name
    /// in items_game.txt. Nothing they reference is exported on top, the addon loads it from the game like the game does.
    /// </summary>
    sealed class CharacterDependencyCollector
    {
        private static readonly HashSet<string> AssetExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".vmdl", ".vpcf", ".vsnap", ".vsndevts", ".vsnd",
        };

        // Sound event files that never hold hero or cosmetic sounds
        private static readonly string[] IgnoredSoundEventFolders =
        [
            "soundevents/music/",
            "soundevents/teamfandom/",
            "soundevents/team_fandom/",
            "soundevents/stickers/",
        ];

        private const string VoiceScriptsFolder = "soundevents/voscripts/";
        private const string SoundEventsTypeName = "vsndevts_c";

        private readonly Package package;
        private readonly GameFileLoader fileLoader;
        private readonly IProgress<string>? progress;

        private readonly Queue<(string Path, bool IncludeSounds)> queue = new();
        private readonly HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, (string File, KVObject Data)>? soundEvents;

        public CharacterDependencyCollector(Package package, GameFileLoader fileLoader, IProgress<string>? progress)
        {
            this.package = package;
            this.fileLoader = fileLoader;
            this.progress = progress;
        }

        public CharacterExportPlan Collect(CharacterLoadout loadout, CharacterExportOptions options, CancellationToken cancellationToken)
        {
            var plan = new CharacterExportPlan();

            AddHeroRoots(loadout, options);

            var equippedAssets = GetEquippedAssets(loadout);

            foreach (var item in loadout.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddItemRoots(loadout, item, options, equippedAssets, plan);
            }

            if (options.ReplaceDefaults)
            {
                AddReplacements(loadout, options, equippedAssets, plan);
            }
            else
            {
                AddWornModels(loadout, options);
            }

            if (options.Pedestal)
            {
                foreach (var pedestal in loadout.Pedestals)
                {
                    Enqueue(pedestal);
                }
            }

            if (options.Icons)
            {
                foreach (var icon in options.IconReplacements)
                {
                    if (Exists(icon.Source))
                    {
                        plan.IconReplacements.Add(icon);
                    }
                    else
                    {
                        plan.Missing.Add(icon.Source);
                    }
                }
            }

            while (queue.TryDequeue(out var next))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Visit(next.Path, next.IncludeSounds, plan);
            }

            return plan;
        }

        private void AddHeroRoots(CharacterLoadout loadout, CharacterExportOptions options)
        {
            var hero = loadout.Hero;

            if (options.HeroSounds && hero.GameSoundsFile != null)
            {
                Enqueue(hero.GameSoundsFile, includeSounds: options.IncludeAudio);
            }

            if (options.HeroVoice && hero.VoiceFile != null)
            {
                Enqueue(hero.VoiceFile, includeSounds: options.IncludeAudio);
            }

            if (options.HeroParticles && hero.ParticleFolder != null)
            {
                var folder = NormalizePath(hero.ParticleFolder).TrimEnd('/');

                foreach (var entry in GetPackageEntries("vpcf_c"))
                {
                    var directory = entry.DirectoryName.Replace('\\', '/');

                    if (directory.Equals(folder, StringComparison.OrdinalIgnoreCase)
                        || directory.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        Enqueue(entry.GetFullPath());
                    }
                }
            }
        }

        /// <summary>
        /// Models and particles the hero ends up with, which decides whether swaps that only apply on top of another item
        /// are needed.
        /// </summary>
        private static HashSet<string> GetEquippedAssets(CharacterLoadout loadout)
        {
            var assets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (loadout.HeroModel != null)
            {
                assets.Add(NormalizePath(loadout.HeroModel));
            }

            foreach (var item in loadout.Items)
            {
                if (loadout.GetItemModel(item) is { } model)
                {
                    assets.Add(NormalizePath(model));
                }

                foreach (var modifier in item.Modifiers)
                {
                    if (!IsConditionalSwap(modifier.Type) && IsAssetPath(modifier.Modifier))
                    {
                        assets.Add(NormalizePath(modifier.Modifier));
                    }
                }
            }

            return assets;
        }

        /// <summary>
        /// Swaps that replace another item's asset with a version made to go with this item, e.g. a refit of a head that
        /// clips with an arcana. They only matter when that other item is equipped too.
        /// </summary>
        private static bool IsConditionalSwap(string type) => type is "model" or "particle_combined";

        /// <summary>
        /// Queues what an item swaps in or creates, other than models the hero wears, which depend on whether the default
        /// assets get replaced. Only the kinds of assets a hero addon can use are looked at: the rest of what items_game.txt
        /// names, like loading screens, couriers or map effects, has nothing to do with the hero.
        /// </summary>
        private void AddItemRoots(CharacterLoadout loadout, EquippedItem item, CharacterExportOptions options, HashSet<string> equippedAssets, CharacterExportPlan plan)
        {
            foreach (var modifier in item.Modifiers)
            {
                // Only shown in the loadout screen, which a custom game does not have
                if (modifier.LoadoutOnly && !options.Pedestal)
                {
                    continue;
                }

                switch (modifier.Type)
                {
                    case "sound" when options.ItemSounds && modifier.Modifier != null:
                        AddSoundEvent(loadout.Hero, modifier.Modifier, options.IncludeAudio, plan);
                        break;

                    // Swapped in particles are exported as the replacements of what they swap when defaults get replaced
                    case "particle" or "particle_combined" when options.ItemParticles && !options.ReplaceDefaults:
                        if (IsAssetPath(modifier.Modifier)
                            && (!IsConditionalSwap(modifier.Type) || (modifier.Asset != null && equippedAssets.Contains(NormalizePath(modifier.Asset)))))
                        {
                            Enqueue(modifier.Modifier);
                        }

                        break;

                    case "particle_create" or "particle_snapshot" when options.ItemParticles && IsAssetPath(modifier.Modifier):
                        Enqueue(modifier.Modifier);
                        break;

                    // Other units, like summons, which have no default model of the hero's to be written over
                    case "entity_model" when options.ItemModels && IsAssetPath(modifier.Modifier)
                        && !loadout.Hero.Name.Equals(modifier.Asset, StringComparison.OrdinalIgnoreCase):
                        Enqueue(modifier.Modifier);
                        break;

                    case "hero_model_change" when options.ItemModels && !options.ReplaceDefaults && IsAssetPath(modifier.Modifier):
                        Enqueue(modifier.Modifier);
                        break;
                }
            }
        }

        /// <summary>
        /// Queues the models the hero wears, as they are, when they are not written over the default ones.
        /// </summary>
        private void AddWornModels(CharacterLoadout loadout, CharacterExportOptions options)
        {
            if (options.HeroModel)
            {
                if (loadout.Hero.Model != null)
                {
                    Enqueue(loadout.Hero.Model);
                }

                if (loadout.HeroModel != null)
                {
                    Enqueue(loadout.HeroModel);
                }
            }

            if (!options.ItemModels)
            {
                return;
            }

            foreach (var item in loadout.Items)
            {
                if (loadout.GetItemModel(item) is { } model)
                {
                    Enqueue(model);
                }
            }

            foreach (var (_, wearable) in loadout.AdditionalWearables)
            {
                Enqueue(wearable);
            }

            foreach (var unitModel in loadout.UnitDefaultModels)
            {
                Enqueue(unitModel);
            }
        }

        /// <summary>
        /// Works out what gets written over the hero's default assets, see <see cref="CharacterExportOptions.ReplaceDefaults"/>,
        /// and queues what that needs. What default items do is left alone, the game still applies it.
        /// </summary>
        private void AddReplacements(CharacterLoadout loadout, CharacterExportOptions options, HashSet<string> equippedAssets, CharacterExportPlan plan)
        {
            var hero = loadout.Hero;

            // Particles of items without a model of their own play on the hero, as do those of models added to the hero's
            var heroParticles = new List<string>();
            var merged = new List<MergedModel>();
            var replacedParticles = new Dictionary<string, ParticleReplacement>(StringComparer.OrdinalIgnoreCase);
            var usedParticles = GetUsedParticles(loadout, equippedAssets);
            var unitSwaps = options.ItemModels ? loadout.UnitModelSwaps.ToList() : [];

            if (options.ItemModels)
            {
                foreach (var unitModel in loadout.UnitDefaultModels)
                {
                    Enqueue(unitModel);
                }
            }

            foreach (var item in loadout.Items)
            {
                var createdParticles = options.ItemParticles && !item.Item.IsDefault
                    ? item.Modifiers
                        .Where(static modifier => modifier is { Type: "particle_create", LoadoutOnly: false } && IsAssetPath(modifier.Modifier))
                        .Select(static modifier => NormalizePath(modifier.Modifier!))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                    : [];

                var model = options.ItemModels ? loadout.GetItemModel(item) : null;
                var itemUnitSwaps = unitSwaps.Where(swap => swap.Item == item).ToList();

                if (itemUnitSwaps.Count > 0)
                {
                    // Dresses a unit the hero creates, its own model is only what the loadout screen shows
                    foreach (var (_, unitModel, defaultModel) in itemUnitSwaps)
                    {
                        var (skin, bodyGroups) = GetModelLook(loadout, unitModel, item.Skin);

                        Enqueue(unitModel);

                        // Default items name the unit's own model too
                        if (!CharacterLoadout.IsSamePath(unitModel, defaultModel) || skin != 0 || createdParticles.Count > 0 || bodyGroups.Count > 0)
                        {
                            plan.ModelReplacements.Add(new ModelReplacement(NormalizePath(unitModel), NormalizePath(defaultModel), skin, createdParticles)
                            {
                                BodyGroups = bodyGroups,
                            });
                        }
                    }
                }
                else if (model == null)
                {
                    if (loadout.IsWornByHero(item.Item.Slot))
                    {
                        heroParticles.AddRange(createdParticles);
                    }
                }
                else
                {
                    Enqueue(model);

                    if (loadout.GetDefaultModel(item.Item.Slot) is { } defaultModel)
                    {
                        var (skin, bodyGroups) = GetModelLook(loadout, model, item.Skin);

                        // Also covers default items that another item swaps for a refit, or whose parts an arcana hides
                        if (!CharacterLoadout.IsSamePath(model, defaultModel) || skin != 0 || createdParticles.Count > 0 || bodyGroups.Count > 0)
                        {
                            plan.ModelReplacements.Add(new ModelReplacement(NormalizePath(model), NormalizePath(defaultModel), skin, createdParticles)
                            {
                                BodyGroups = bodyGroups,
                            });
                        }
                    }
                    else if (!item.Item.IsDefault && loadout.IsWornByHero(item.Item.Slot))
                    {
                        // Nothing of the hero's to write it over, e.g. a head for a hero without a default one
                        var (skin, bodyGroups) = GetModelLook(loadout, model, item.Skin);
                        merged.Add(new MergedModel(NormalizePath(model), skin, bodyGroups));
                        heroParticles.AddRange(createdParticles);
                    }
                    else if (!item.Item.IsDefault)
                    {
                        plan.UnplacedModels.Add(NormalizePath(model));
                    }
                }

                if (options.ItemModels)
                {
                    AddTransformationReplacements(loadout, item, plan);
                }

                if (!options.ItemParticles || item.Item.IsDefault)
                {
                    continue;
                }

                foreach (var modifier in item.Modifiers)
                {
                    if (modifier.Type is not ("particle" or "particle_combined")
                        || !IsParticlePath(modifier.Asset)
                        || !IsParticlePath(modifier.Modifier)
                        || CharacterLoadout.IsSamePath(modifier.Asset, modifier.Modifier))
                    {
                        continue;
                    }

                    if (modifier.Type == "particle_combined" && !equippedAssets.Contains(NormalizePath(modifier.Asset)))
                    {
                        continue;
                    }

                    var replacement = new ParticleReplacement(NormalizePath(modifier.Modifier), NormalizePath(modifier.Asset));

                    // Items also refit the particles of other items, which only matters with those equipped
                    if (IsEconParticle(replacement.Target) && !usedParticles.Contains(replacement.Target))
                    {
                        continue;
                    }

                    if (!options.ReplaceSharedParticles && !IsHeroParticle(hero, replacement.Target))
                    {
                        plan.SkippedSharedParticles.Add(replacement);
                        continue;
                    }

                    // A style can swap the same particle twice, the file can only hold one of them
                    if (replacedParticles.TryGetValue(replacement.Target, out var existing))
                    {
                        if (!CharacterLoadout.IsSamePath(existing.Source, replacement.Source))
                        {
                            plan.Notes.Add($"{replacement.Target} is swapped for both {existing.Source} and {replacement.Source}, the first one is used");
                        }

                        continue;
                    }

                    replacedParticles.Add(replacement.Target, replacement);
                    plan.ParticleReplacements.Add(replacement);
                    Enqueue(replacement.Source);
                }
            }

            if (options.ItemModels)
            {
                foreach (var (item, wearable) in loadout.AdditionalWearables.Where(static wearable => !wearable.Item.Item.IsDefault))
                {
                    var (skin, bodyGroups) = GetModelLook(loadout, wearable, item.Skin);
                    merged.Add(new MergedModel(NormalizePath(wearable), skin, bodyGroups));
                    Enqueue(wearable);
                }
            }

            if (!options.HeroModel || hero.Model == null)
            {
                foreach (var model in merged)
                {
                    plan.UnplacedModels.Add(model.Model);
                    plan.Notes.Add($"{model.Model} has no slot of its own and can only be added to the hero's model, which is not exported");
                }

                return;
            }

            var heroModel = loadout.HeroModel ?? hero.Model;
            var (heroSkin, heroBodyGroups) = GetModelLook(loadout, heroModel, loadout.HeroSkin);

            Enqueue(heroModel);

            if (!CharacterLoadout.IsSamePath(heroModel, hero.Model) || heroSkin != 0 || heroParticles.Count > 0 || merged.Count > 0 || heroBodyGroups.Count > 0)
            {
                plan.ModelReplacements.Add(new ModelReplacement(
                    NormalizePath(heroModel),
                    NormalizePath(hero.Model),
                    heroSkin,
                    [.. heroParticles.Distinct(StringComparer.OrdinalIgnoreCase)])
                {
                    BodyGroups = heroBodyGroups,
                    Merged = merged,
                });
            }
        }

        /// <summary>
        /// Models of forms the hero transforms into, which items can swap like the hero's own.
        /// </summary>
        private void AddTransformationReplacements(CharacterLoadout loadout, EquippedItem item, CharacterExportPlan plan)
        {
            foreach (var modifier in item.Modifiers)
            {
                if (modifier.Type != "hero_model_change" || !IsAssetPath(modifier.Asset) || !IsAssetPath(modifier.Modifier))
                {
                    continue;
                }

                var (skin, bodyGroups) = GetModelLook(loadout, modifier.Modifier, item.Skin);

                if (CharacterLoadout.IsSamePath(modifier.Asset, modifier.Modifier) && skin == 0 && bodyGroups.Count == 0)
                {
                    continue;
                }

                plan.ModelReplacements.Add(new ModelReplacement(NormalizePath(modifier.Modifier), NormalizePath(modifier.Asset), skin, [])
                {
                    BodyGroups = bodyGroups,
                });

                Enqueue(modifier.Modifier);
            }
        }

        /// <summary>
        /// How the loadout shows a model: the skin, unless the model has no such material group, since a style's skin
        /// also applies to models that come without skins, and the body group choices it sets that change what the
        /// model shows, i.e. of body groups the model has and to other choices than the one it shows by default.
        /// </summary>
        private (int Skin, Dictionary<string, int> BodyGroups) GetModelLook(CharacterLoadout loadout, string model, int skin)
        {
            var choices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using var resource = fileLoader.LoadFileCompiled(NormalizePath(model));

                if (resource?.DataBlock is not Model modelData)
                {
                    return (skin, choices);
                }

                if (skin >= modelData.GetMaterialGroups().Count())
                {
                    skin = 0;
                }

                var bodyGroups = modelData.MeshGroups.BodyGroups;

                foreach (var (name, choice) in loadout.GetBodyGroupChoices(model))
                {
                    if (choice > 0 && bodyGroups.Any(group => group.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && group.Choices.Count > 1))
                    {
                        choices[name] = choice;
                    }
                }
            }
            catch (Exception e)
            {
                progress?.Report($"  ! failed to read \"{model}\": {e.Message}");
            }

            return (skin, choices);
        }

        /// <summary>
        /// Particles the loadout plays itself: the ones its items create or swap in, and the ones the models it wears
        /// play, e.g. from their animations.
        /// </summary>
        private HashSet<string> GetUsedParticles(CharacterLoadout loadout, HashSet<string> equippedAssets)
        {
            var particles = new HashSet<string>(equippedAssets.Where(IsParticlePath), StringComparer.OrdinalIgnoreCase);

            foreach (var model in equippedAssets.Where(static asset => asset.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase))
                .Concat(loadout.AdditionalWearables.Select(static wearable => wearable.Model)))
            {
                try
                {
                    using var resource = fileLoader.LoadFileCompiled(NormalizePath(model));

                    foreach (var reference in resource?.ExternalReferences?.ResourceRefInfoList ?? [])
                    {
                        if (IsParticlePath(reference.Name))
                        {
                            particles.Add(NormalizePath(reference.Name));
                        }
                    }
                }
                catch (Exception e)
                {
                    progress?.Report($"  ! failed to read the references of \"{model}\": {e.Message}");
                }
            }

            return particles;
        }

        private static bool IsEconParticle(string path) => path.StartsWith("particles/econ/", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Whether only this hero uses the particle, as opposed to effects every hero plays, like items' or stuns.
        /// </summary>
        private static bool IsHeroParticle(HeroDefinition hero, string path)
            => IsEconParticle(path)
                || (hero.ParticleFolder != null && path.StartsWith(NormalizePath(hero.ParticleFolder).TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))
                || path.Contains(hero.ShortName, StringComparison.OrdinalIgnoreCase);

        private static bool IsParticlePath([NotNullWhen(true)] string? value)
            => IsAssetPath(value) && value.EndsWith(".vpcf", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Adds the file a single sound event is defined in, and when <paramref name="includeAudio"/> is set the sounds it
        /// plays. The file is exported for its text, but not the other events' sounds, since it usually holds a lot of them.
        /// </summary>
        private void AddSoundEvent(HeroDefinition hero, string eventName, bool includeAudio, CharacterExportPlan plan)
        {
            soundEvents ??= BuildSoundEventIndex(hero);

            if (!soundEvents.TryGetValue(eventName, out var soundEvent))
            {
                plan.Missing.Add($"sound event {eventName}");
                return;
            }

            Enqueue(soundEvent.File);

            if (!includeAudio)
            {
                return;
            }

            var sounds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectEventSounds(soundEvent.Data, sounds, depth: 0);

            foreach (var sound in sounds)
            {
                Enqueue(sound);
            }
        }

        private void CollectEventSounds(KVObject soundEvent, HashSet<string> sounds, int depth)
        {
            CollectSoundPaths(soundEvent, sounds);

            // An event may inherit its sounds from a base event
            if (depth < 8
                && soundEvent.GetStringProperty("base") is { Length: > 0 } baseName
                && soundEvents!.TryGetValue(baseName, out var baseEvent))
            {
                CollectEventSounds(baseEvent.Data, sounds, depth + 1);
            }
        }

        private static void CollectSoundPaths(KVObject value, HashSet<string> sounds)
        {
            switch (value.ValueType)
            {
                case KVValueType.String:
                    var text = value.ToString();

                    if (text != null && text.EndsWith(".vsnd", StringComparison.OrdinalIgnoreCase))
                    {
                        sounds.Add(text);
                    }

                    break;

                case KVValueType.Collection:
                case KVValueType.Array:
                    foreach (var child in value.Values)
                    {
                        CollectSoundPaths(child, sounds);
                    }

                    break;
            }
        }

        private Dictionary<string, (string File, KVObject Data)> BuildSoundEventIndex(HeroDefinition hero)
        {
            progress?.Report("Indexing sound events...");

            var index = new Dictionary<string, (string File, KVObject Data)>(StringComparer.OrdinalIgnoreCase);
            var heroVoiceFile = hero.VoiceFile != null ? NormalizePath(hero.VoiceFile) + GameFileLoader.CompiledFileSuffix : null;

            // Hero files go first so their definitions win over same named events elsewhere
            var entries = GetPackageEntries(SoundEventsTypeName)
                .Where(entry =>
                {
                    var path = entry.GetFullPath();

                    if (path.StartsWith(VoiceScriptsFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        return path.Equals(heroVoiceFile, StringComparison.OrdinalIgnoreCase);
                    }

                    return !IgnoredSoundEventFolders.Any(folder => path.StartsWith(folder, StringComparison.OrdinalIgnoreCase));
                })
                .OrderByDescending(entry => entry.GetFullPath().Contains(hero.ShortName, StringComparison.OrdinalIgnoreCase));

            foreach (var entry in entries)
            {
                var path = entry.GetFullPath();

                try
                {
                    using var resource = new Resource { FileName = path };
                    resource.Read(GameFileLoader.GetPackageEntryStream(package, entry));

                    if (resource.DataBlock == null)
                    {
                        continue;
                    }

                    var sourcePath = path[..^GameFileLoader.CompiledFileSuffix.Length];

                    foreach (var (eventName, eventData) in resource.DataBlock.AsKeyValueCollection())
                    {
                        if (eventData.ValueType == KVValueType.Collection)
                        {
                            index.TryAdd(eventName, (sourcePath, eventData));
                        }
                    }
                }
                catch (Exception e)
                {
                    progress?.Report($"  ! failed to read sound events from \"{path}\": {e.Message}");
                }
            }

            return index;
        }

        private void Visit(string path, bool includeSounds, CharacterExportPlan plan)
        {
            var sourcePath = path.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.OrdinalIgnoreCase)
                ? path[..^GameFileLoader.CompiledFileSuffix.Length]
                : path;
            var compiledPath = sourcePath + GameFileLoader.CompiledFileSuffix;

            if (!Exists(compiledPath))
            {
                if (Exists(sourcePath))
                {
                    plan.RawFiles.Add(sourcePath);
                }
                else
                {
                    plan.Missing.Add(sourcePath);
                }

                return;
            }

            if (sourcePath.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase))
            {
                plan.Models.Add(compiledPath);
            }
            else
            {
                plan.Resources.Add(compiledPath);
            }

            if (includeSounds && sourcePath.EndsWith(".vsndevts", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var sound in ReadSounds(compiledPath))
                {
                    Enqueue(sound);
                }
            }
        }

        /// <summary>
        /// The sounds a sound event file plays, which it does not always list as references.
        /// </summary>
        private HashSet<string> ReadSounds(string compiledPath)
        {
            var sounds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using var resource = fileLoader.LoadFile(compiledPath);

                if (resource == null)
                {
                    return sounds;
                }

                if (resource.ExternalReferences is { } externalReferences)
                {
                    foreach (var reference in externalReferences.ResourceRefInfoList)
                    {
                        if (reference.Name?.EndsWith(".vsnd", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            sounds.Add(reference.Name);
                        }
                    }
                }

                if (resource.DataBlock != null)
                {
                    CollectSoundPaths(resource.DataBlock.AsKeyValueCollection(), sounds);
                }
            }
            catch (Exception e)
            {
                progress?.Report($"  ! failed to read the sounds of \"{compiledPath}\": {e.Message}");
            }

            return sounds;
        }

        private void Enqueue(string path, bool includeSounds = false)
        {
            path = NormalizePath(path);

            if (path.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                path = path[..^GameFileLoader.CompiledFileSuffix.Length];
            }

            if (visited.Add(path))
            {
                queue.Enqueue((path, includeSounds));
            }
        }

        private bool Exists(string path)
        {
            var (pathOnDisk, _, packageEntry) = fileLoader.FindFile(path, logNotFound: false);
            return pathOnDisk != null || packageEntry != null;
        }

        private List<PackageEntry> GetPackageEntries(string typeName)
            => package.Entries != null && package.Entries.TryGetValue(typeName, out var entries) ? entries : [];

        private static bool IsAssetPath([NotNullWhen(true)] string? value)
            => value != null && value.Contains('/', StringComparison.Ordinal) && AssetExtensions.Contains(Path.GetExtension(value));

        private static string NormalizePath(string path) => CharacterLoadout.NormalizePath(path);
    }
}
