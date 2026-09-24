using System.IO;
using System.Linq;
using GUI.Utils;
using ValveKeyValue;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Exporter.CharacterAssets
{
    /// <summary>
    /// One version of a sound event.
    /// </summary>
    /// <param name="Event">The sound event that plays.</param>
    /// <param name="DisplayName">What it is, the items it comes from for anything but the game's own sound.</param>
    sealed record SoundChoice(string Event, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// A sound event written over with another one's definition, both as event names.
    /// </summary>
    sealed record SoundReplacement(string Source, string Target);

    /// <summary>
    /// A sound event the game plays for the hero, which items can swap for one of their own.
    /// </summary>
    sealed class SoundSlot
    {
        /// <summary>The event the game plays, e.g. "Hero_EarthShaker.Totem".</summary>
        public required string Event { get; init; }

        /// <summary>The event without the hero's prefix, e.g. "Totem".</summary>
        public required string DisplayName { get; init; }

        /// <summary>The sound event file that defines the event, as a source path.</summary>
        public required string File { get; init; }

        /// <summary>
        /// Whether the event is not the hero's own, like the blink dagger's, so writing over it changes it for every hero.
        /// </summary>
        public bool Shared { get; init; }

        /// <summary>The versions to choose from, the game's own one first.</summary>
        public List<SoundChoice> Choices { get; } = [];

        public SoundChoice Default => Choices[0];
    }

    /// <summary>
    /// A voice the hero speaks with.
    /// </summary>
    /// <param name="Criteria">The response criteria an item sets to switch the hero to other lines, null for the hero's own voice.</param>
    sealed record VoiceChoice(string? Criteria, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// Which of the hero's voice lines are written over with which lines of another voice.
    /// </summary>
    /// <param name="Lines">The voice line each of the hero's own lines becomes, as sound event names.</param>
    /// <param name="HeroLines">How many lines the hero's own voice has.</param>
    sealed record VoiceMapping(IReadOnlyDictionary<string, string> Lines, int HeroLines);

    /// <summary>
    /// A sound event file with some of its events written over with other events' definitions.
    /// </summary>
    sealed class SoundEventEdit(string file, IReadOnlySet<string> fileEvents)
    {
        /// <summary>The sound event file, as a source path.</summary>
        public string File { get; } = file;

        /// <summary>The events the file defines.</summary>
        public IReadOnlySet<string> FileEvents { get; } = fileEvents;

        /// <summary>
        /// The new definitions by event name: the events written over, and the ones they play that the file lacks.
        /// </summary>
        public Dictionary<string, KVObject> Events { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>What was written over with what, for the export log.</summary>
        public List<string> Details { get; } = [];

        /// <summary>
        /// The file's text, as decompiled from its compiled resource, with the edits applied.
        /// </summary>
        public string Apply(Resource resource)
        {
            var data = resource.DataBlock ?? throw new InvalidDataException("The file has no sound events");
            var events = data.AsKeyValueCollection();
            var edited = KVObject.ListCollection();

            foreach (var (name, definition) in events)
            {
                edited.Add(name, Events.TryGetValue(name, out var replacement) ? replacement : definition);
            }

            foreach (var (name, definition) in Events)
            {
                if (!FileEvents.Contains(name))
                {
                    edited.Add(name, definition);
                }
            }

            return edited.ToKV3String(data is BinaryKV3 kv3 ? kv3.Data.Header?.Format : null);
        }
    }

    /// <summary>
    /// A hero's response rules: which of its voice lines it speaks in which situation. Items switch the hero to other
    /// lines by setting a "customresponse" criteria, which the rules for those lines require.
    /// </summary>
    sealed class HeroResponseRules
    {
        private const string CustomResponseKey = "customresponse";

        private readonly List<(string[] Requirements, string[] Lines)> groups;

        /// <summary>The expression each "customresponse" criteria matches, by criteria name, e.g. "!=drow_arcana".</summary>
        private readonly Dictionary<string, string> customCriteria;

        private HeroResponseRules(List<(string[] Requirements, string[] Lines)> groups, Dictionary<string, string> customCriteria)
        {
            this.groups = groups;
            this.customCriteria = customCriteria;
        }

        /// <summary>
        /// The compiled response rules of a hero, which are named after its voice line file, e.g.
        /// "scripts/talker/response_rules_crystalmaiden.vrr_c" for "game_sounds_vo_crystalmaiden.vsndevts".
        /// </summary>
        public static string? GetPath(HeroDefinition hero)
        {
            const string VoicePrefix = "game_sounds_vo_";

            var name = hero.VoiceFile != null ? Path.GetFileNameWithoutExtension(hero.VoiceFile) : null;

            return name != null && name.StartsWith(VoicePrefix, StringComparison.OrdinalIgnoreCase)
                ? $"scripts/talker/response_rules_{name[VoicePrefix.Length..]}.vrr{GameFileLoader.CompiledFileSuffix}"
                : null;
        }

        /// <summary>
        /// Reads the hero's response rules, or null when the hero has none.
        /// </summary>
        public static HeroResponseRules? Load(Package package, HeroDefinition hero)
        {
            var path = GetPath(hero);

            if (path == null || package.FindEntry(path) is not { } entry)
            {
                return null;
            }

            try
            {
                using var resource = new Resource { FileName = path };
                resource.Read(GameFileLoader.GetPackageEntryStream(package, entry));

                return resource.DataBlock != null ? Read(resource.DataBlock.AsKeyValueCollection()) : null;
            }
            catch (Exception e)
            {
                Log.Warn(nameof(HeroResponseRules), $"Failed to read \"{path}\": {e.Message}");
                return null;
            }
        }

        private static HeroResponseRules Read(KVObject data)
        {
            var customCriteria = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var criteria in data.GetArray("m_Requirements") ?? [])
            {
                if (string.Equals(criteria.GetStringProperty("m_matchKey"), CustomResponseKey, StringComparison.OrdinalIgnoreCase)
                    && criteria.GetStringProperty("m_name") is { Length: > 0 } name)
                {
                    customCriteria[name] = criteria.GetStringProperty("m_matchExpr", string.Empty);
                }
            }

            var groups = new List<(string[] Requirements, string[] Lines)>();

            foreach (var group in data.GetArray("m_ResponseGroups") ?? [])
            {
                var requirements = group.GetSubCollection("m_pEmbeddedRule")?.GetArray<string>("m_Requirements");

                if (requirements is not { Length: > 0 })
                {
                    continue;
                }

                var lines = (group.GetArray("m_responses") ?? [])
                    .Where(static response => string.Equals(response.GetStringProperty("m_type"), "SPEAK", StringComparison.OrdinalIgnoreCase))
                    .Select(static response => response.GetStringProperty("m_value"))
                    .Where(static line => !string.IsNullOrEmpty(line))
                    .ToArray();

                if (lines.Length > 0)
                {
                    groups.Add((requirements, lines));
                }
            }

            return new HeroResponseRules(groups, customCriteria);
        }

        /// <summary>
        /// Matches the hero's own lines to the lines of the voice an item switches it to, by the situation they are
        /// spoken in: the rules they are picked by, besides which voice it is. A situation the voice has no lines for
        /// takes the lines of the closest one it has, a more general one when it can, so the hero keeps speaking with
        /// that voice. The voice's lines are handed out in turn when it has fewer lines for a situation.
        /// </summary>
        /// <param name="criteria">The criteria the item sets, e.g. "arcana".</param>
        public VoiceMapping MapVoice(string criteria)
        {
            var voiceCriteria = customCriteria
                .Where(pair => pair.Value.Equals(criteria, StringComparison.OrdinalIgnoreCase))
                .Select(static pair => pair.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Every voice has its own criteria, the hero's own lines require none of them
            var anyVoiceCriteria = customCriteria
                .Where(static pair => !pair.Value.StartsWith('!'))
                .Select(static pair => pair.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var heroGroups = groups.Where(group => !group.Requirements.Any(anyVoiceCriteria.Contains)).ToList();
            var heroLines = heroGroups.SelectMany(static group => group.Lines).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var lines = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (voiceCriteria.Count == 0)
            {
                return new VoiceMapping(lines, heroLines.Count);
            }

            var situations = new List<(HashSet<string> Situation, List<string> Lines)>();

            foreach (var (requirements, groupLines) in groups.Where(group => group.Requirements.Any(voiceCriteria.Contains)))
            {
                var situation = GetSituation(requirements);
                var existing = situations.FindIndex(voiceSituation => voiceSituation.Situation.SetEquals(situation));

                if (existing < 0)
                {
                    situations.Add((situation, []));
                    existing = situations.Count - 1;
                }

                foreach (var line in groupLines)
                {
                    if (!situations[existing].Lines.Contains(line, StringComparer.OrdinalIgnoreCase))
                    {
                        situations[existing].Lines.Add(line);
                    }
                }
            }

            var voiceLines = situations.SelectMany(static situation => situation.Lines).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var (requirements, groupLines) in heroGroups)
            {
                var situation = GetSituation(requirements);

                // The situation's concept, e.g. "Kill", comes first and has to stay the same
                var concept = requirements[0];
                var candidates = situations.Where(voiceSituation => voiceSituation.Situation.Contains(concept)).ToList();

                var match = candidates.Find(candidate => candidate.Situation.SetEquals(situation)).Lines
                    ?? candidates.Where(candidate => candidate.Situation.IsProperSubsetOf(situation))
                        .OrderByDescending(static candidate => candidate.Situation.Count)
                        .Select(static candidate => candidate.Lines)
                        .FirstOrDefault()
                    ?? candidates.Where(candidate => candidate.Situation.IsProperSupersetOf(situation))
                        .OrderBy(static candidate => candidate.Situation.Count)
                        .Select(static candidate => candidate.Lines)
                        .FirstOrDefault();

                if (match == null)
                {
                    continue;
                }

                for (var i = 0; i < groupLines.Length; i++)
                {
                    // Some voices reuse lines of the hero's own, which have to stay as they are
                    if (!voiceLines.Contains(groupLines[i]))
                    {
                        lines.TryAdd(groupLines[i], match[i % match.Count]);
                    }
                }
            }

            return new VoiceMapping(lines, heroLines.Count);
        }

        private HashSet<string> GetSituation(string[] requirements)
            => requirements.Where(requirement => !customCriteria.ContainsKey(requirement)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Finds the sounds items swap for the hero, and the voices they switch it to.
    /// </summary>
    static class CharacterSounds
    {
        public const string HeroGroup = "Hero sounds";
        public const string SharedGroup = "Shared sounds";

        public const string SoundEventsTypeName = "vsndevts_c";
        public const string VoiceScriptsFolder = "soundevents/voscripts/";

        /// <summary>Sound event files that never hold hero or cosmetic sounds.</summary>
        public static readonly IReadOnlyList<string> IgnoredFolders =
        [
            "soundevents/music/",
            "soundevents/teamfandom/",
            "soundevents/team_fandom/",
            "soundevents/stickers/",
        ];

        /// <summary>
        /// The file each sound event is defined in, as source paths, leaving out voice lines, which items do not swap
        /// one by one, and the files in <see cref="IgnoredFolders"/>.
        /// </summary>
        public static Dictionary<string, string> GetEventFiles(Package package)
        {
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (package.Entries == null || !package.Entries.TryGetValue(SoundEventsTypeName, out var entries))
            {
                return files;
            }

            foreach (var entry in entries)
            {
                var path = entry.GetFullPath();

                if (path.StartsWith(VoiceScriptsFolder, StringComparison.OrdinalIgnoreCase)
                    || IgnoredFolders.Any(folder => path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                try
                {
                    using var resource = new Resource { FileName = path };
                    resource.Read(GameFileLoader.GetPackageEntryStream(package, entry));

                    if (resource.DataBlock == null)
                    {
                        continue;
                    }

                    var sourcePath = path[..^GameFileLoader.CompiledFileSuffix.Length];

                    foreach (var (eventName, _) in resource.DataBlock.AsKeyValueCollection())
                    {
                        files.TryAdd(eventName, sourcePath);
                    }
                }
                catch (Exception e)
                {
                    Log.Warn(nameof(CharacterSounds), $"Failed to read sound events from \"{path}\": {e.Message}");
                }
            }

            return files;
        }

        /// <summary>
        /// The sound events the hero's items swap for one of their own, the hero's own ones first.
        /// </summary>
        /// <param name="eventFiles">The file each sound event is defined in, see <see cref="GetEventFiles"/>. Swaps of events it does not have are left out.</param>
        public static List<SoundSlot> GetSlots(ItemsGameCatalog catalog, HeroDefinition hero, IReadOnlyDictionary<string, string> eventFiles)
        {
            var heroFile = hero.GameSoundsFile != null ? CharacterLoadout.NormalizePath(hero.GameSoundsFile) : null;
            var swaps = new Dictionary<string, List<(string Event, List<string> Items)>>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in catalog.GetItems(hero))
            {
                foreach (var modifier in item.AssetModifiers)
                {
                    if (modifier is not { Type: "sound", LoadoutOnly: false, Asset: { Length: > 0 } asset, Modifier: { Length: > 0 } sound }
                        || asset.Equals(sound, StringComparison.OrdinalIgnoreCase)
                        || !eventFiles.ContainsKey(asset)
                        || !eventFiles.ContainsKey(sound))
                    {
                        continue;
                    }

                    if (!swaps.TryGetValue(asset, out var choices))
                    {
                        choices = [];
                        swaps.Add(asset, choices);
                    }

                    var index = choices.FindIndex(choice => choice.Event.Equals(sound, StringComparison.OrdinalIgnoreCase));

                    if (index < 0)
                    {
                        choices.Add((sound, []));
                        index = choices.Count - 1;
                    }

                    var itemName = GetItemName(item, modifier.Style);

                    if (!choices[index].Items.Contains(itemName))
                    {
                        choices[index].Items.Add(itemName);
                    }
                }
            }

            var slots = new List<SoundSlot>(swaps.Count);

            foreach (var (asset, choices) in swaps)
            {
                var file = eventFiles[asset];

                var slot = new SoundSlot
                {
                    Event = asset,
                    DisplayName = GetShortName(asset),
                    File = file,
                    Shared = !file.Equals(heroFile, StringComparison.OrdinalIgnoreCase),
                };

                slot.Choices.Add(new SoundChoice(asset, "Default"));

                foreach (var (sound, items) in choices)
                {
                    slot.Choices.Add(new SoundChoice(sound, string.Join(", ", items)));
                }

                slots.Add(slot);
            }

            slots.Sort(static (a, b) => a.Shared != b.Shared
                ? a.Shared.CompareTo(b.Shared)
                : StringComparer.OrdinalIgnoreCase.Compare(a.Event, b.Event));

            return slots;
        }

        /// <summary>
        /// The hero's own voice, followed by the voices its items switch it to that the hero's response rules have
        /// lines for.
        /// </summary>
        public static List<VoiceChoice> GetVoices(ItemsGameCatalog catalog, HeroDefinition hero, HeroResponseRules? rules)
        {
            var voices = new List<VoiceChoice> { new(null, "Default") };

            if (rules == null)
            {
                return voices;
            }

            var itemsByCriteria = new List<(string Criteria, List<string> Items)>();

            foreach (var item in catalog.GetItems(hero))
            {
                foreach (var modifier in item.AssetModifiers)
                {
                    if (modifier is not { Type: "response_criteria", LoadoutOnly: false, Asset: { Length: > 0 } criteria })
                    {
                        continue;
                    }

                    var index = itemsByCriteria.FindIndex(existing => existing.Criteria.Equals(criteria, StringComparison.OrdinalIgnoreCase));

                    if (index < 0)
                    {
                        itemsByCriteria.Add((criteria, []));
                        index = itemsByCriteria.Count - 1;
                    }

                    var itemName = GetItemName(item, modifier.Style);

                    if (!itemsByCriteria[index].Items.Contains(itemName))
                    {
                        itemsByCriteria[index].Items.Add(itemName);
                    }
                }
            }

            foreach (var (criteria, items) in itemsByCriteria)
            {
                var mapping = rules.MapVoice(criteria);

                if (mapping.Lines.Count > 0)
                {
                    voices.Add(new VoiceChoice(criteria, $"{string.Join(", ", items)} ({mapping.Lines.Count} of {mapping.HeroLines} lines)"));
                }
            }

            return voices;
        }

        /// <summary>
        /// A sound event without the prefix that names whose it is, e.g. "Totem.Attack" for "Hero_EarthShaker.Totem.Attack".
        /// </summary>
        private static string GetShortName(string eventName)
        {
            var dot = eventName.IndexOf('.', StringComparison.Ordinal);

            return dot > 0 && dot < eventName.Length - 1 ? eventName[(dot + 1)..] : eventName;
        }

        private static string GetItemName(EconItem item, int? style)
        {
            var styleName = style is { } index && item.Styles.Count > 1
                ? item.Styles.FirstOrDefault(itemStyle => itemStyle.Index == index)?.Name
                : null;

            return styleName != null ? $"{item.Name} ({styleName})" : item.Name;
        }
    }
}
