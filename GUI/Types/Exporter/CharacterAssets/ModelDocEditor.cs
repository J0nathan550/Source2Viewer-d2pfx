using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.Particles;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Exporter.CharacterAssets
{
    /// <summary>
    /// A particle a model creates itself, as its "particle_cfg_list" game data.
    /// </summary>
    /// <param name="Name">Full path of the particle system.</param>
    /// <param name="Config">The particle's control point configuration that places it on the model, empty when it has none.</param>
    sealed record ModelParticle(string Name, string Config);

    /// <summary>
    /// An activity modifier set on a model, which makes it play the sequences made for it over the other ones of the activity.
    /// </summary>
    /// <param name="Activity">The activity it applies to, e.g. "ACT_DOTA_TAUNT", or null when it applies to every activity.</param>
    /// <param name="Name">The modifier, e.g. "arcana".</param>
    sealed record ActivityModifier(string? Activity, string Name);

    /// <summary>
    /// Edits decompiled .vmdl files in place, keeping the rest of the text exactly as the model extractor wrote it.
    /// </summary>
    static partial class ModelDocEditor
    {
        /// <summary>
        /// Makes a material group the default one, so the model shows that skin without anything picking it.
        /// </summary>
        /// <param name="vmdl">The .vmdl text.</param>
        /// <param name="skin">Index of the material group, 0 being the default one.</param>
        public static string MakeMaterialGroupDefault(string vmdl, int skin)
        {
            var groups = GetRootChildren(vmdl)
                .FirstOrDefault(static node => node.GetStringProperty("_class") == "MaterialGroupList")?
                .GetArray("children");

            if (groups == null || skin <= 0 || skin >= groups.Count)
            {
                throw new InvalidDataException($"The model has no material group {skin}");
            }

            var remaps = new List<MaterialRemap>();

            // The skin's remaps win over anything the default group already remapped the same material to
            AddRemaps(remaps, groups[skin]);
            AddRemaps(remaps, groups[0]);

            return SetDefaultRemaps(vmdl, remaps);
        }

        /// <summary>
        /// Adds material swaps to the model's default material group, creating the group when the model has none. Swaps
        /// the group already has for the same material are kept.
        /// </summary>
        /// <param name="vmdl">The .vmdl text.</param>
        /// <param name="materialRemaps">The material each material is swapped for.</param>
        public static string AddDefaultMaterialRemaps(string vmdl, IReadOnlyDictionary<string, string> materialRemaps)
        {
            if (materialRemaps.Count == 0)
            {
                return vmdl;
            }

            var remaps = new List<MaterialRemap>();
            var groups = GetRootChildren(vmdl)
                .FirstOrDefault(static node => node.GetStringProperty("_class") == "MaterialGroupList")?
                .GetArray("children");

            if (groups is { Count: > 0 })
            {
                AddRemaps(remaps, groups[0]);
            }

            foreach (var (from, to) in materialRemaps.OrderBy(static remap => remap.Key, StringComparer.OrdinalIgnoreCase))
            {
                AddRemap(remaps, new MaterialRemap("BaseMaterialRemap", from, to));
            }

            if (groups is { Count: > 0 })
            {
                return SetDefaultRemaps(vmdl, remaps);
            }

            var entries = new StringBuilder();

            foreach (var (remapClass, from, to) in remaps)
            {
                entries.Append(CultureInfo.InvariantCulture, $$"""

                    {
                        _class = "{{remapClass}}"
                        from = "{{from}}"
                        to = "{{to}}"
                    },
                    """);
            }

            var node = $$"""

                {
                    _class = "MaterialGroupList"
                    children =
                    [
                        {
                            _class = "DefaultMaterialGroup"
                            remaps =
                            [{{Indent(entries.ToString(), 4)}}
                            ]
                        },
                    ]
                },
                """;

            var rootChildren = RootNodeChildrenRegex().Match(vmdl);

            if (!rootChildren.Success)
            {
                throw new InvalidDataException("The model's root node was not found");
            }

            return Validate(vmdl.Insert(rootChildren.Index + rootChildren.Length, Indent(node, 3)));
        }

        /// <summary>
        /// Picks body group choices, like the game does when it switches a body group, e.g. the arcana one to the
        /// arcana level. The other choices are removed along with the meshes only they show, so the model shows the
        /// picked meshes without anything having to switch the body group. The arcana body group keeps its first choice
        /// too: a full mesh of the item the model shows by default, which <see cref="AddDefaultMaterialRemaps"/> gives
        /// the style's materials. Models reduced to the picked style alone render partly transparent in game.
        /// </summary>
        /// <param name="vmdl">The .vmdl text.</param>
        /// <param name="choices">The index of the choice to pick, by body group name. Groups the model does not have are skipped.</param>
        /// <param name="details">Receives a description of every choice that was picked.</param>
        public static string SelectBodyGroupChoices(string vmdl, IReadOnlyDictionary<string, int> choices, ICollection<string>? details = null)
        {
            var bodyGroups = GetBodyGroups(GetRootChildren(vmdl));
            var removedChoices = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var removedMeshes = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (name, index) in choices)
            {
                var bodyGroup = bodyGroups.FirstOrDefault(group => group.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

                if (bodyGroup == null || bodyGroup.Choices.Count < 2)
                {
                    continue;
                }

                // Items with fewer choices than there are arcana levels show their last choice for the higher levels
                var picked = Math.Min(index, bodyGroup.Choices.Count - 1);

                // The first choice is the one shown already
                if (picked <= 0)
                {
                    continue;
                }

                var keepFirst = bodyGroup.Name.Equals(CharacterLoadout.ArcanaBodyGroup, StringComparison.OrdinalIgnoreCase);
                var kept = bodyGroup.Choices.Where((_, choiceIndex) => choiceIndex == picked || (keepFirst && choiceIndex == 0));

                var keptMeshes = bodyGroups
                    .Where(group => group != bodyGroup)
                    .SelectMany(static group => group.Choices)
                    .Concat(kept)
                    .SelectMany(static choice => choice.Meshes)
                    .ToHashSet(StringComparer.Ordinal);

                var others = bodyGroup.Choices.Where((_, choiceIndex) => choiceIndex != picked && !(keepFirst && choiceIndex == 0)).ToList();

                if (others.Count == 0)
                {
                    continue;
                }

                removedChoices[bodyGroup.Name] = [.. others.Select(static choice => choice.Name)];
                removedMeshes.UnionWith(others.SelectMany(static choice => choice.Meshes).Where(mesh => !keptMeshes.Contains(mesh)));

                details?.Add($"{bodyGroup.Name} body group shows {bodyGroup.Choices[picked].Name}");
            }

            if (removedChoices.Count == 0)
            {
                return vmdl;
            }

            var objects = FindObjects(vmdl);
            var spans = new List<(int Start, int End)>();

            foreach (var (start, end) in objects)
            {
                if (MeshReferenceRegex().Match(vmdl, start) is { Success: true } reference)
                {
                    if (removedMeshes.Contains(reference.Groups["name"].Value))
                    {
                        spans.Add((start, end));
                    }

                    continue;
                }

                if (ObjectHeaderRegex().Match(vmdl, start) is not { Success: true } header)
                {
                    continue;
                }

                var nodeClass = header.Groups["class"].Value;
                var nodeName = header.Groups["name"].Value;

                if (nodeClass == "RenderMeshFile" && removedMeshes.Contains(nodeName))
                {
                    spans.Add((start, end));
                }
                else if (nodeClass == "BodyGroup" && removedChoices.TryGetValue(nodeName, out var removedChoiceNames))
                {
                    // Choices are only told apart within their own group, different groups often name them the same
                    foreach (var (choiceStart, choiceEnd) in objects)
                    {
                        if (choiceStart > start && choiceEnd <= end
                            && ObjectHeaderRegex().Match(vmdl, choiceStart) is { Success: true } choiceHeader
                            && choiceHeader.Groups["class"].Value == "BodyGroupChoice"
                            && removedChoiceNames.Contains(choiceHeader.Groups["name"].Value))
                        {
                            spans.Add((choiceStart, choiceEnd));
                        }
                    }
                }
            }

            foreach (var (start, end) in spans.OrderByDescending(static span => span.Start))
            {
                var lineStart = GetLineStart(vmdl, start);
                vmdl = vmdl.Remove(lineStart, GetLineEnd(vmdl, end) - lineStart);
            }

            return Validate(vmdl);
        }

        /// <summary>
        /// Picks body group choices like <see cref="SelectBodyGroupChoices"/>, but disables the other choices and the
        /// meshes only they show instead of removing them, so they can be turned back on in ModelDoc. Only their
        /// levels of detail references go, which the compiler rejects for disabled meshes. The "_dummy" choices, which
        /// hold another full copy of the item, are removed along with their meshes.
        /// </summary>
        /// <param name="vmdl">The .vmdl text.</param>
        /// <param name="choices">The index of the choice to pick, by body group name. Groups the model does not have are skipped.</param>
        /// <param name="details">Receives a description of every choice that was picked.</param>
        public static string DisableBodyGroupChoices(string vmdl, IReadOnlyDictionary<string, int> choices, ICollection<string>? details = null)
        {
            var bodyGroups = GetBodyGroups(GetRootChildren(vmdl));
            var disabledChoices = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var removedChoices = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var otherMeshes = new HashSet<string>(StringComparer.Ordinal);
            var keptMeshes = new HashSet<string>(StringComparer.Ordinal);

            foreach (var bodyGroup in bodyGroups)
            {
                var index = choices.FirstOrDefault(choice => choice.Key.Equals(bodyGroup.Name, StringComparison.OrdinalIgnoreCase)).Value;

                if (index <= 0 || bodyGroup.Choices.Count < 2)
                {
                    keptMeshes.UnionWith(bodyGroup.Choices.SelectMany(static choice => choice.Meshes));
                    continue;
                }

                // Items with fewer choices than there are arcana levels show their last choice for the higher levels
                var picked = bodyGroup.Choices[Math.Min(index, bodyGroup.Choices.Count - 1)];
                var others = bodyGroup.Choices.Where(choice => choice != picked).ToList();
                var dummies = others.Where(IsDummy).ToList();

                keptMeshes.UnionWith(picked.Meshes);
                otherMeshes.UnionWith(others.SelectMany(static choice => choice.Meshes));
                disabledChoices[bodyGroup.Name] = [.. others.Except(dummies).Select(static choice => choice.Name)];
                removedChoices[bodyGroup.Name] = [.. dummies.Select(static choice => choice.Name)];

                details?.Add($"{bodyGroup.Name} body group shows {picked.Name}" +
                    (others.Count > dummies.Count ? $", {others.Count - dummies.Count} other choices disabled" : string.Empty) +
                    (dummies.Count > 0 ? ", _dummy removed" : string.Empty));
            }

            if (disabledChoices.Count == 0)
            {
                return vmdl;
            }

            // Meshes of the choices that stay, even disabled, are only disabled
            var disabledChoiceMeshes = bodyGroups
                .Where(group => disabledChoices.ContainsKey(group.Name))
                .SelectMany(group => group.Choices.Where(choice => disabledChoices[group.Name].Contains(choice.Name)))
                .SelectMany(static choice => choice.Meshes)
                .ToHashSet(StringComparer.Ordinal);
            var removedMeshes = otherMeshes.Where(mesh => !keptMeshes.Contains(mesh) && !disabledChoiceMeshes.Contains(mesh)).ToHashSet(StringComparer.Ordinal);
            var disabledMeshes = otherMeshes.Where(mesh => !keptMeshes.Contains(mesh) && !removedMeshes.Contains(mesh)).ToHashSet(StringComparer.Ordinal);

            var objects = FindObjects(vmdl);
            var removals = new List<(int Start, int End)>();
            var disables = new List<Match>();

            void AddChoiceEdits(int start, int end, HashSet<string> names, bool remove)
            {
                // Choices are only told apart within their own group, different groups often name them the same
                foreach (var (choiceStart, choiceEnd) in objects)
                {
                    if (choiceStart > start && choiceEnd <= end
                        && ObjectHeaderRegex().Match(vmdl, choiceStart) is { Success: true } choiceHeader
                        && choiceHeader.Groups["class"].Value == "BodyGroupChoice"
                        && names.Contains(choiceHeader.Groups["name"].Value))
                    {
                        if (remove)
                        {
                            removals.Add((choiceStart, choiceEnd));
                        }
                        else if (!IsDisabled(choiceStart, choiceEnd))
                        {
                            disables.Add(choiceHeader);
                        }
                    }
                }
            }

            bool IsDisabled(int start, int end) => DisabledRegex().Match(vmdl, start, end - start).Success;

            foreach (var (start, end) in objects)
            {
                // Levels of detail cannot reference disabled meshes, the compiler does not know them
                if (MeshReferenceRegex().Match(vmdl, start) is { Success: true } reference)
                {
                    if (removedMeshes.Contains(reference.Groups["name"].Value) || disabledMeshes.Contains(reference.Groups["name"].Value))
                    {
                        removals.Add((start, end));
                    }

                    continue;
                }

                if (ObjectHeaderRegex().Match(vmdl, start) is not { Success: true } header || !header.Groups["name"].Success)
                {
                    continue;
                }

                var nodeClass = header.Groups["class"].Value;
                var nodeName = header.Groups["name"].Value;

                if (nodeClass == "RenderMeshFile" && removedMeshes.Contains(nodeName))
                {
                    removals.Add((start, end));
                }
                else if (nodeClass == "RenderMeshFile" && disabledMeshes.Contains(nodeName) && !IsDisabled(start, end))
                {
                    disables.Add(header);
                }
                else if (nodeClass == "BodyGroup" && disabledChoices.TryGetValue(nodeName, out var disabledNames))
                {
                    AddChoiceEdits(start, end, disabledNames, remove: false);
                    AddChoiceEdits(start, end, removedChoices[nodeName], remove: true);
                }
            }

            var edits = removals
                .Select(span => (Start: GetLineStart(vmdl, span.Start), End: GetLineEnd(vmdl, span.End), Text: string.Empty))
                .Concat(disables.Select(header =>
                {
                    var index = header.Index + header.Length;
                    return (Start: index, End: index, Text: $"\n{new string('\t', GetIndentation(vmdl, header.Index) + 1)}disabled = true");
                }));

            foreach (var (start, end, text) in edits.OrderByDescending(static edit => edit.Start))
            {
                vmdl = string.Concat(vmdl.AsSpan(0, start), text, vmdl.AsSpan(end));
            }

            return Validate(vmdl);
        }

        private static bool IsDummy(BodyGroupChoiceNode choice)
            => choice.Name.Equals("_dummy", StringComparison.OrdinalIgnoreCase)
                || (choice.Meshes.Length > 0 && choice.Meshes.All(static mesh => mesh.Equals("_dummy", StringComparison.OrdinalIgnoreCase)));

        /// <summary>
        /// Makes the model play what it plays with activity modifiers set, without anything setting them. Of the sequences
        /// of an activity that only differ in those modifiers, the ones with the most of them win like they do in game:
        /// they lose the modifiers so they become the plain sequences, and the others no longer play for the activity.
        /// </summary>
        /// <param name="vmdl">The .vmdl text.</param>
        /// <param name="modifiers">The modifiers to apply.</param>
        /// <param name="details">Receives a description of what now plays.</param>
        public static string ApplyActivityModifiers(string vmdl, IReadOnlyCollection<ActivityModifier> modifiers, ICollection<string>? details = null)
        {
            if (modifiers.Count == 0)
            {
                return vmdl;
            }

            var objects = FindObjects(vmdl);
            var sequences = new List<(Group Activity, List<(string Name, int Start, int End)> Modifiers)>();

            foreach (var (start, end) in objects)
            {
                if (ObjectHeaderRegex().Match(vmdl, start) is not { Success: true } header || header.Groups["class"].Value != "AnimFile")
                {
                    continue;
                }

                var children = objects.Where(child => child.Start > start && child.End <= end).ToList();
                var sequenceModifiers = new List<(string Name, int Start, int End)>();
                Group? activity = null;

                foreach (var (childStart, childEnd) in children)
                {
                    if (ObjectHeaderRegex().Match(vmdl, childStart) is { Success: true } childHeader
                        && childHeader.Groups["class"].Value == "ActivityModifier"
                        && ActivityNameRegex().Match(vmdl, childStart, childEnd - childStart) is { Success: true } modifierName)
                    {
                        sequenceModifiers.Add((modifierName.Groups["name"].Value, childStart, childEnd));
                    }
                }

                // The sequence's own activity, as opposed to its modifiers' names
                for (var match = ActivityNameRegex().Match(vmdl, start, end - start); match.Success; match = match.NextMatch())
                {
                    if (!children.Any(child => match.Index > child.Start && match.Index < child.End))
                    {
                        activity = match.Groups["name"];
                        break;
                    }
                }

                if (activity is { Length: > 0 })
                {
                    sequences.Add((activity, sequenceModifiers));
                }
            }

            var edits = new List<(int Start, int End)>();
            var appliedNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var played = 0;
            var replaced = 0;

            var candidates = sequences.Select(sequence =>
            {
                var active = modifiers
                    .Where(modifier => modifier.Activity == null || modifier.Activity.Equals(sequence.Activity.Value, StringComparison.OrdinalIgnoreCase))
                    .Select(static modifier => modifier.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var matched = sequence.Modifiers.Where(modifier => active.Contains(modifier.Name)).ToList();
                var others = sequence.Modifiers
                    .Where(modifier => !active.Contains(modifier.Name))
                    .Select(static modifier => modifier.Name.ToUpperInvariant())
                    .Order(StringComparer.Ordinal);

                return (sequence.Activity, sequence.Modifiers, Matched: matched, Key: $"{sequence.Activity.Value.ToUpperInvariant()}|{string.Join('|', others)}");
            });

            foreach (var group in candidates.GroupBy(static candidate => candidate.Key, StringComparer.Ordinal))
            {
                var best = group.Max(static candidate => candidate.Matched.Count);

                if (best == 0)
                {
                    continue;
                }

                foreach (var candidate in group)
                {
                    if (candidate.Matched.Count == best)
                    {
                        played++;
                        appliedNames.UnionWith(candidate.Matched.Select(static modifier => modifier.Name));
                        edits.AddRange(candidate.Matched.Select(modifier => (GetLineStart(vmdl, modifier.Start), GetLineEnd(vmdl, modifier.End))));
                    }
                    else
                    {
                        // Still there to be played by name, just not picked for the activity any more
                        replaced++;
                        edits.Add((candidate.Activity.Index, candidate.Activity.Index + candidate.Activity.Length));
                        edits.AddRange(candidate.Modifiers.Select(modifier => (GetLineStart(vmdl, modifier.Start), GetLineEnd(vmdl, modifier.End))));
                    }
                }
            }

            if (edits.Count == 0)
            {
                return vmdl;
            }

            foreach (var (start, end) in edits.OrderByDescending(static edit => edit.Start))
            {
                vmdl = vmdl.Remove(start, end - start);
            }

            details?.Add($"{played} sequences made for the {string.Join(", ", appliedNames)} activity modifiers play by default" +
                (replaced > 0 ? $" over {replaced} others" : string.Empty));

            return Validate(vmdl);
        }

        /// <summary>
        /// Adds another model's meshes to this one, for items that have no model of the hero's own to be written over
        /// but should still show on it. Wearables share the hero's skeleton, so their meshes follow it once they are
        /// part of it. Their attachments and the remaps of their default material group come along, so particles and
        /// skins they use keep working. Meshes the other model hides by default are left out.
        /// </summary>
        /// <param name="vmdl">The .vmdl text of the model that gets the meshes.</param>
        /// <param name="otherVmdl">The .vmdl text of the model whose meshes are added, already with its skin and body groups picked.</param>
        /// <param name="prefix">Prepended to the names of meshes that clash with this model's meshes.</param>
        public static string MergeModel(string vmdl, string otherVmdl, string prefix)
        {
            var children = GetRootChildren(vmdl);
            var otherChildren = GetRootChildren(otherVmdl);

            var meshNames = GetNodeNames(children, "RenderMeshList");
            var attachmentNames = GetNodeNames(children, "AttachmentList");
            var lodGroups = GetLodGroups(children);
            var otherLodGroups = GetLodGroups(otherChildren);

            // Only the first choice of a body group is shown by default
            var hiddenMeshes = GetBodyGroups(otherChildren)
                .SelectMany(static group => group.Choices.Skip(1).SelectMany(static choice => choice.Meshes)
                    .Except(group.Choices[0].Meshes, StringComparer.Ordinal))
                .ToHashSet(StringComparer.Ordinal);

            var meshes = new StringBuilder();
            var attachments = new StringBuilder();
            var lodReferences = lodGroups.Select(static _ => new List<string>()).ToList();

            foreach (var (start, end) in FindObjects(otherVmdl))
            {
                if (ObjectHeaderRegex().Match(otherVmdl, start) is not { Success: true } header)
                {
                    continue;
                }

                var name = header.Groups["name"];

                if (header.Groups["class"].Value == "RenderMeshFile" && !hiddenMeshes.Contains(name.Value))
                {
                    var newName = meshNames.Contains(name.Value) ? $"{prefix}_{name.Value}" : name.Value;

                    meshes.Append('\n').Append(GetNodeText(otherVmdl, start, end, name, newName));

                    foreach (var lod in GetLodTargets(lodGroups, otherLodGroups, name.Value))
                    {
                        lodReferences[lod].Add(newName);
                    }
                }
                else if (header.Groups["class"].Value == "Attachment" && !attachmentNames.Contains(name.Value))
                {
                    attachments.Append('\n').Append(GetNodeText(otherVmdl, start, end, name, name.Value));
                }
            }

            if (meshes.Length == 0)
            {
                throw new InvalidDataException("The model has no meshes to add");
            }

            var renderMeshList = RenderMeshListChildrenRegex().Match(vmdl);

            if (!renderMeshList.Success)
            {
                throw new InvalidDataException("The model's render mesh list was not found");
            }

            var insertions = new List<(int Index, string Text)>
            {
                (renderMeshList.Index + renderMeshList.Length, meshes.ToString()),
            };

            if (attachments.Length > 0 && AttachmentListChildrenRegex().Match(vmdl) is { Success: true } attachmentList)
            {
                insertions.Add((attachmentList.Index + attachmentList.Length, attachments.ToString()));
            }

            var lodIndex = 0;

            foreach (var (start, end) in FindObjects(vmdl))
            {
                if (lodIndex >= lodGroups.Count
                    || ObjectHeaderRegex().Match(vmdl, start) is not { Success: true } header
                    || header.Groups["class"].Value is not ("LODGroup" or "LODGroupAll"))
                {
                    continue;
                }

                var references = lodReferences[lodIndex++];
                var meshReferences = MeshReferencesRegex().Match(vmdl, start, end - start);

                if (references.Count == 0 || !meshReferences.Success)
                {
                    continue;
                }

                var indent = new string('\t', GetIndentation(vmdl, start) + 2);
                var text = new StringBuilder();

                foreach (var reference in references)
                {
                    text.Append(CultureInfo.InvariantCulture, $"\n{indent}{{\n{indent}\tmesh_name = \"{reference}\"\n{indent}}},");
                }

                insertions.Add((meshReferences.Index + meshReferences.Length, text.ToString()));
            }

            foreach (var (index, text) in insertions.OrderByDescending(static insertion => insertion.Index))
            {
                vmdl = vmdl.Insert(index, text);
            }

            // Materials the other model's skin swaps have to be swapped on its meshes here too
            var otherRemaps = new List<MaterialRemap>();

            if (GetMaterialGroups(otherChildren) is [var otherDefaultGroup, ..])
            {
                AddRemaps(otherRemaps, otherDefaultGroup);
            }

            if (otherRemaps.Count > 0 && GetMaterialGroups(children) is [var defaultGroup, ..])
            {
                var remaps = new List<MaterialRemap>();
                AddRemaps(remaps, defaultGroup);

                foreach (var remap in otherRemaps)
                {
                    AddRemap(remaps, remap);
                }

                vmdl = SetDefaultRemaps(vmdl, remaps);
            }

            return Validate(vmdl);
        }

        /// <summary>
        /// Adds particles to the model's game data, so the model creates them when it spawns.
        /// </summary>
        public static string AddParticles(string vmdl, IEnumerable<ModelParticle> particles)
        {
            var entries = new StringBuilder();

            foreach (var particle in particles)
            {
                entries.Append(CultureInfo.InvariantCulture, $$"""

                    {
                        _class = "GenericGameData"
                        name = "{{Path.GetFileNameWithoutExtension(particle.Name)}}"
                        game_class = "particle_cfg"
                        game_keys =
                        {
                            name = resource:"{{particle.Name}}"
                            config = "{{particle.Config}}"
                        }
                    },
                    """);
            }

            if (entries.Length == 0)
            {
                return vmdl;
            }

            // Into the model's game data when it has some, or into a new game data list otherwise
            var gameDataList = GameDataListChildrenRegex().Match(vmdl);

            if (gameDataList.Success)
            {
                return Validate(vmdl.Insert(gameDataList.Index + gameDataList.Length, Indent(entries.ToString(), 5)));
            }

            var rootChildren = RootNodeChildrenRegex().Match(vmdl);

            if (!rootChildren.Success)
            {
                throw new InvalidDataException("The model's root node was not found");
            }

            var node = $$"""

                {
                    _class = "GameDataList"
                    children =
                    [{{Indent(entries.ToString(), 2)}}
                    ]
                },
                """;

            return Validate(vmdl.Insert(rootChildren.Index + rootChildren.Length, Indent(node, 3)));
        }

        /// <summary>
        /// Picks the control point configuration a particle plays under in game, which drives all of its control points
        /// from the model's attachments: the "game" one, else one made for something other than the preview, else the
        /// preview one, which most item particles only have. Configurations staged for the loadout screen are skipped,
        /// see <see cref="IsStagedForLoadout"/>, so without another one the particle follows the model.
        /// </summary>
        public static ModelParticle ResolveParticle(IFileLoader fileLoader, string particle)
        {
            var names = GetControlPointConfigurations(fileLoader, particle)
                .Where(static configuration => !configuration.StagedForLoadout && configuration.Name.Length > 0)
                .Select(static configuration => configuration.Name)
                .ToList();

            var config = names.FirstOrDefault(static name => name.Equals("game", StringComparison.OrdinalIgnoreCase))
                ?? names.FirstOrDefault(static name => !name.Equals("preview", StringComparison.OrdinalIgnoreCase))
                ?? names.FirstOrDefault()
                ?? string.Empty;

            return new ModelParticle(particle, config);
        }

        /// <summary>
        /// Whether a particle only comes with control point configurations for the loadout screen that put all of its
        /// control points at the world origin, where the hero stands in it. Created on a model in game, it stays behind
        /// at the map's origin, e.g. the ground glow of Drow Ranger's arcana.
        /// </summary>
        public static bool IsStagedForLoadout(IFileLoader fileLoader, string particle)
        {
            var configurations = GetControlPointConfigurations(fileLoader, particle);

            return configurations.Count > 0 && configurations.All(static configuration => configuration.StagedForLoadout);
        }

        private static List<(string Name, bool StagedForLoadout)> GetControlPointConfigurations(IFileLoader fileLoader, string particle)
        {
            using var resource = fileLoader.LoadFileCompiled(particle);

            if (resource?.DataBlock is not ParticleSystem particleSystem)
            {
                return [];
            }

            static bool IsAtWorldOrigin(KVObject driver)
                => driver.ContainsKey("m_iAttachType")
                    && driver.GetEnumValue<ParticleAttachment>("m_iAttachType") == ParticleAttachment.PATTACH_WORLDORIGIN;

            return [.. (particleSystem.GetUpgradedData().GetArray("m_controlPointConfigurations") ?? [])
                .Select(static configuration =>
                {
                    var name = configuration.GetStringProperty("m_name", string.Empty);
                    var drivers = configuration.GetArray("m_drivers") ?? [];

                    return (name, name.Contains("loadout", StringComparison.OrdinalIgnoreCase) && drivers.Count > 0 && drivers.All(IsAtWorldOrigin));
                })];
        }

        /// <summary>
        /// Indents text written with four spaces per level the way the model extractor does, with tabs.
        /// </summary>
        private static string Indent(string text, int depth)
        {
            var lines = text.Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                if (line.Length == 0)
                {
                    continue;
                }

                var spaces = line.Length - line.TrimStart(' ').Length;
                lines[i] = new string('\t', depth + spaces / 4) + line.TrimStart(' ');
            }

            return string.Join('\n', lines);
        }

        private static IReadOnlyList<KVObject> GetRootChildren(string vmdl)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(vmdl));
            KVObject root = KVDocumentExtensions.ParseKV3(stream);

            return root.GetSubCollection("rootNode")?.GetArray("children") ?? [];
        }

        /// <summary>
        /// Makes sure an edit left the file readable, it is better not to edit it at all than to break it.
        /// </summary>
        private static string Validate(string vmdl)
        {
            GetRootChildren(vmdl);
            return vmdl;
        }

        private sealed record MaterialRemap(string Class, string From, string To);

        private sealed record BodyGroupNode(string Name, List<BodyGroupChoiceNode> Choices);

        private sealed record BodyGroupChoiceNode(string Name, string[] Meshes);

        /// <param name="All">Whether this is the group of meshes shown at every level of detail.</param>
        private sealed record LodGroupNode(bool All, HashSet<string> Meshes);

        /// <summary>
        /// Adds a group's remaps, unless an earlier remap already swaps the same material.
        /// </summary>
        private static void AddRemaps(List<MaterialRemap> remaps, KVObject group)
        {
            foreach (var remap in group.GetArray("remaps") ?? [])
            {
                var from = remap.GetStringProperty("from");

                if (!string.IsNullOrEmpty(from))
                {
                    AddRemap(remaps, new MaterialRemap(remap.GetStringProperty("_class", "BaseMaterialRemap"), from, remap.GetStringProperty("to", string.Empty)));
                }
            }
        }

        private static void AddRemap(List<MaterialRemap> remaps, MaterialRemap remap)
        {
            if (!remaps.Any(existing => existing.From.Equals(remap.From, StringComparison.OrdinalIgnoreCase)))
            {
                remaps.Add(remap);
            }
        }

        private static string SetDefaultRemaps(string vmdl, List<MaterialRemap> remaps)
        {
            var text = new StringBuilder("[\n");

            foreach (var (remapClass, from, to) in remaps)
            {
                text.Append(CultureInfo.InvariantCulture, $"\t\t\t\t\t\t\t{{\n\t\t\t\t\t\t\t\t_class = \"{remapClass}\"\n\t\t\t\t\t\t\t\tfrom = \"{from}\"\n\t\t\t\t\t\t\t\tto = \"{to}\"\n\t\t\t\t\t\t\t}},\n");
            }

            text.Append("\t\t\t\t\t\t]");

            var match = DefaultMaterialGroupRemapsRegex().Match(vmdl);

            if (!match.Success)
            {
                throw new InvalidDataException("The model's default material group was not found");
            }

            var remapsArray = match.Groups["remaps"];

            return Validate(string.Concat(vmdl.AsSpan(0, remapsArray.Index), text.ToString(), vmdl.AsSpan(remapsArray.Index + remapsArray.Length)));
        }

        /// <summary>
        /// The nodes of a list node, e.g. the render meshes of the "RenderMeshList", looking into folders.
        /// </summary>
        private static IEnumerable<KVObject> GetListNodes(IReadOnlyList<KVObject> rootChildren, string listClass)
        {
            static IEnumerable<KVObject> Flatten(IEnumerable<KVObject> nodes)
                => nodes.SelectMany(static node => node.GetStringProperty("_class") == "Folder"
                    ? Flatten(node.GetArray("children") ?? [])
                    : [node]);

            return Flatten(rootChildren
                .Where(node => node.GetStringProperty("_class") == listClass)
                .SelectMany(static node => node.GetArray("children") ?? []));
        }

        private static HashSet<string> GetNodeNames(IReadOnlyList<KVObject> rootChildren, string listClass)
            => GetListNodes(rootChildren, listClass)
                .Select(static node => node.GetStringProperty("name", string.Empty))
                .ToHashSet(StringComparer.Ordinal);

        private static List<BodyGroupNode> GetBodyGroups(IReadOnlyList<KVObject> rootChildren)
            => [.. GetListNodes(rootChildren, "BodyGroupList")
                .Where(static node => node.GetStringProperty("_class") == "BodyGroup")
                .Select(static node => new BodyGroupNode(
                    node.GetStringProperty("name", string.Empty),
                    [.. (node.GetArray("children") ?? [])
                        .Where(static choice => choice.GetStringProperty("_class") == "BodyGroupChoice")
                        .Select(static choice => new BodyGroupChoiceNode(choice.GetStringProperty("name", string.Empty), choice.GetArray<string>("meshes") ?? []))]))
                .Where(static group => group.Choices.Count > 0)];

        private static List<LodGroupNode> GetLodGroups(IReadOnlyList<KVObject> rootChildren)
            => [.. GetListNodes(rootChildren, "LODGroupList")
                .Where(static node => node.GetStringProperty("_class") is "LODGroup" or "LODGroupAll")
                .Select(static node => new LodGroupNode(
                    node.GetStringProperty("_class") == "LODGroupAll",
                    (node.GetArray("mesh_references") ?? [])
                        .Select(static reference => reference.GetStringProperty("mesh_name", string.Empty))
                        .ToHashSet(StringComparer.Ordinal)))];

        private static IReadOnlyList<KVObject> GetMaterialGroups(IReadOnlyList<KVObject> rootChildren)
            => [.. GetListNodes(rootChildren, "MaterialGroupList")];

        /// <summary>
        /// Which of a model's levels of detail a mesh from another model goes into: the same level it is in there, or
        /// every level when it is shown at all of them there.
        /// </summary>
        private static List<int> GetLodTargets(List<LodGroupNode> lodGroups, List<LodGroupNode> otherLodGroups, string mesh)
        {
            var levels = lodGroups.Select((group, index) => (group, index)).Where(static lod => !lod.group.All).Select(static lod => lod.index).ToList();
            var allLevels = lodGroups.FindIndex(static group => group.All);
            var otherLevel = otherLodGroups.Where(static group => !group.All).ToList().FindIndex(group => group.Meshes.Contains(mesh));

            if (otherLevel >= 0 && levels.Count > 0)
            {
                return [levels[Math.Min(otherLevel, levels.Count - 1)]];
            }

            return allLevels >= 0 ? [allLevels] : levels;
        }

        /// <summary>
        /// Where every object in the text starts and ends, the end being past its closing brace, outer objects first.
        /// </summary>
        private static List<(int Start, int End)> FindObjects(string text)
        {
            var objects = new List<(int Start, int End)>();
            var open = new Stack<int>();

            // The header comment holds braces of its own
            var i = text.StartsWith("<!--", StringComparison.Ordinal) ? text.IndexOf("-->", StringComparison.Ordinal) + 3 : 0;

            for (; i < text.Length; i++)
            {
                switch (text[i])
                {
                    case '"':
                        if (string.CompareOrdinal(text, i, "\"\"\"", 0, 3) == 0)
                        {
                            var end = text.IndexOf("\"\"\"", i + 3, StringComparison.Ordinal);
                            i = end < 0 ? text.Length : end + 2;
                            break;
                        }

                        for (i++; i < text.Length && text[i] != '"'; i++)
                        {
                            if (text[i] == '\\')
                            {
                                i++;
                            }
                        }

                        break;

                    case '/' when i + 1 < text.Length && text[i + 1] == '/':
                        i = text.IndexOf('\n', i);
                        i = i < 0 ? text.Length : i;
                        break;

                    case '{':
                        open.Push(i);
                        break;

                    case '}' when open.Count > 0:
                        objects.Add((open.Pop(), i + 1));
                        break;
                }
            }

            objects.Sort(static (a, b) => a.Start.CompareTo(b.Start));

            return objects;
        }

        /// <summary>
        /// Moves back over the indentation before a node.
        /// </summary>
        private static int GetLineStart(string text, int index)
        {
            while (index > 0 && text[index - 1] is ' ' or '\t')
            {
                index--;
            }

            return index;
        }

        /// <summary>
        /// Moves past the comma separating a node from the next one and the rest of its line.
        /// </summary>
        private static int GetLineEnd(string text, int index)
        {
            while (index < text.Length && text[index] is ' ' or '\t')
            {
                index++;
            }

            if (index < text.Length && text[index] == ',')
            {
                index++;
            }

            while (index < text.Length && text[index] is ' ' or '\t' or '\r')
            {
                index++;
            }

            return index < text.Length && text[index] == '\n' ? index + 1 : index;
        }

        private static int GetIndentation(string text, int index) => index - GetLineStart(text, index);

        /// <summary>
        /// A node's text with its indentation, to be copied into another model, renamed on the way.
        /// </summary>
        private static string GetNodeText(string text, int start, int end, Group name, string newName)
        {
            var lineStart = GetLineStart(text, start);
            var nodeText = name.Success
                ? string.Concat(text.AsSpan(lineStart, name.Index - lineStart), newName, text.AsSpan(name.Index + name.Length, end - name.Index - name.Length))
                : text[lineStart..end];

            return nodeText + ",";
        }

        [GeneratedRegex(@"\G\{\s*_class\s*=\s*""(?<class>[^""]*)""(?:\s*name\s*=\s*""(?<name>[^""]*)"")?", RegexOptions.CultureInvariant)]
        private static partial Regex ObjectHeaderRegex();

        [GeneratedRegex(@"\bdisabled\s*=\s*true\b", RegexOptions.CultureInvariant)]
        private static partial Regex DisabledRegex();

        [GeneratedRegex(@"activity_name\s*=\s*""(?<name>[^""]*)""", RegexOptions.CultureInvariant)]
        private static partial Regex ActivityNameRegex();

        [GeneratedRegex(@"\G\{\s*mesh_name\s*=\s*""(?<name>[^""]*)""\s*\}", RegexOptions.CultureInvariant)]
        private static partial Regex MeshReferenceRegex();

        [GeneratedRegex(@"mesh_references\s*=\s*\[", RegexOptions.CultureInvariant)]
        private static partial Regex MeshReferencesRegex();

        [GeneratedRegex(@"_class\s*=\s*""RenderMeshList""\s*children\s*=\s*\[", RegexOptions.CultureInvariant)]
        private static partial Regex RenderMeshListChildrenRegex();

        [GeneratedRegex(@"_class\s*=\s*""AttachmentList""\s*children\s*=\s*\[", RegexOptions.CultureInvariant)]
        private static partial Regex AttachmentListChildrenRegex();

        [GeneratedRegex(@"_class\s*=\s*""DefaultMaterialGroup""[^\[\]{}]*?remaps\s*=\s*(?<remaps>\[[^\[\]]*\])", RegexOptions.CultureInvariant)]
        private static partial Regex DefaultMaterialGroupRemapsRegex();

        [GeneratedRegex(@"_class\s*=\s*""GameDataList""\s*children\s*=\s*\[", RegexOptions.CultureInvariant)]
        private static partial Regex GameDataListChildrenRegex();

        [GeneratedRegex(@"_class\s*=\s*""RootNode""\s*children\s*=\s*\[", RegexOptions.CultureInvariant)]
        private static partial Regex RootNodeChildrenRegex();
    }
}
