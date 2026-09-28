using System.Globalization;
using System.Linq;
using System.Text;

namespace GUI.Types.Exporter.CharacterAssets
{
    /// <summary>
    /// One of the hero's default items, written over with the definition of the item equipped in its slot, see
    /// <see cref="CharacterExportOptions.DefaultItemsInItemsGame"/>.
    /// </summary>
    /// <param name="DefaultItem">The default item's definition index.</param>
    /// <param name="Item">The equipped item's definition index, or null to restore the default item's own definition.</param>
    /// <param name="Style">The equipped item's style, which is made the only one the definition has.</param>
    /// <param name="Description">What is written, by the items' names, e.g. to report it.</param>
    sealed record DefaultItemSwap(string DefaultItem, string? Item, int Style, string Description);

    /// <summary>
    /// Rewrites item definitions in items_game.txt, so the hero wears other items in place of its default ones. Only the
    /// definitions it changes are rewritten, the rest of the text is kept as it is.
    /// </summary>
    static class ItemsGameEditor
    {
        // The default item keeps what makes it the hero's default item for the slot, and its name, which item sets and
        // bundles refer to it by
        private static readonly string[] DefaultItemKeys = ["name", "prefab", "baseitem", "item_slot", "used_by_heroes"];

        /// <summary>
        /// Pairs each of the hero's default items with the item equipped in its slot. Every default item is listed, so
        /// the ones of slots left at their default are restored in a file an earlier export changed.
        /// </summary>
        /// <remarks>Equipped items of slots without a default item are left out.</remarks>
        public static List<DefaultItemSwap> GetSwaps(ItemsGameCatalog catalog, CharacterLoadout loadout)
        {
            var swaps = new List<DefaultItemSwap>();

            foreach (var slot in catalog.GetSlots(loadout.Hero))
            {
                if (catalog.GetDefaultItem(loadout.Hero, slot.Name) is not { } defaultItem
                    || swaps.Any(swap => swap.DefaultItem == defaultItem.DefIndex))
                {
                    continue;
                }

                var item = loadout.Items.LastOrDefault(item => !item.Item.IsDefault
                    && item.Item.Slot.Equals(slot.Name, StringComparison.OrdinalIgnoreCase));

                var description = item == null
                    ? $"{defaultItem.Name} restored"
                    : $"{defaultItem.Name} <- {item.Item.Name}{(item.StyleName is { } style ? $" ({style})" : string.Empty)}";

                swaps.Add(new DefaultItemSwap(defaultItem.DefIndex, item?.Item.DefIndex, item?.Style ?? 0, description));
            }

            return swaps;
        }

        /// <summary>
        /// Writes the swaps into items_game.txt.
        /// </summary>
        /// <param name="original">The game's own items_game.txt, which the definitions are taken from.</param>
        /// <param name="current">The text to write them into: the game's own, or a copy an earlier export changed.</param>
        /// <returns>The rewritten text, and the swaps left out because a definition was not found.</returns>
        public static (string Text, List<DefaultItemSwap> Missing) ReplaceDefaultItems(string original, string current,
            IReadOnlyList<DefaultItemSwap> swaps)
        {
            var needed = swaps
                .SelectMany(static swap => swap.Item == null ? [swap.DefaultItem] : new[] { swap.DefaultItem, swap.Item })
                .ToHashSet(StringComparer.Ordinal);
            var originalItems = FindItems(original, needed);
            var currentItems = FindItems(current, swaps.Select(static swap => swap.DefaultItem).ToHashSet(StringComparer.Ordinal));
            var newLine = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

            var edits = new List<(Range Target, string Text)>();
            var missing = new List<DefaultItemSwap>();

            foreach (var swap in swaps)
            {
                if (!originalItems.TryGetValue(swap.DefaultItem, out var defaultItem)
                    || !currentItems.TryGetValue(swap.DefaultItem, out var target))
                {
                    missing.Add(swap);
                    continue;
                }

                if (swap.Item == null)
                {
                    edits.Add((target, original[defaultItem]));
                    continue;
                }

                if (!originalItems.TryGetValue(swap.Item, out var item))
                {
                    missing.Add(swap);
                    continue;
                }

                var definition = CreateDefinition(Parse(original[defaultItem]), Parse(original[item]), swap.Style);
                var text = new StringBuilder();
                Write(definition, text, 2, newLine);
                edits.Add((target, text.ToString()));
            }

            var result = new StringBuilder(current);

            foreach (var (target, text) in edits.OrderByDescending(edit => edit.Target.Start.Value))
            {
                result.Remove(target.Start.Value, target.End.Value - target.Start.Value);
                result.Insert(target.Start.Value, text);
            }

            return (result.ToString(), missing);
        }

        /// <summary>
        /// The equipped item's definition under the default item's index, with what makes the default item the hero's
        /// default for its slot. Default items have no style to pick, so only the picked style's modifiers are kept, and
        /// its model and skin are made the item's own.
        /// </summary>
        private static KvNode CreateDefinition(KvNode defaultItem, KvNode item, int style)
        {
            var definition = item.Clone();
            definition.Key = defaultItem.Key;

            foreach (var key in DefaultItemKeys)
            {
                definition.Replace(key, defaultItem.Get(key));
            }

            if (definition.Get("visuals") is not { Children: { } visuals } visualsNode)
            {
                return definition;
            }

            var styleKey = style.ToString(CultureInfo.InvariantCulture);

            visuals.RemoveAll(child => IsModifier(child) && child.Get("style")?.Value is { } modifierStyle && modifierStyle != styleKey);

            foreach (var modifier in visuals.Where(IsModifier))
            {
                modifier.Replace("style", null);
            }

            if (visualsNode.Get("styles") is { } styles)
            {
                var picked = styles.Get(styleKey);

                if (picked?.Get("model_player") is { Value: not null } model)
                {
                    definition.Replace("model_player", model);
                }

                if (picked?.Get("skin") is { Value: not null } skin)
                {
                    visualsNode.Replace("skin", skin);
                }

                visualsNode.Replace("styles", null);
            }

            return definition;
        }

        private static bool IsModifier(KvNode node) => node.Key.StartsWith("asset_modifier", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Finds the definitions of items in the "items" section, from the opening quote of their index to their closing brace.
        /// </summary>
        private static Dictionary<string, Range> FindItems(string text, HashSet<string> defIndices)
        {
            var items = new Dictionary<string, Range>(StringComparer.Ordinal);
            var depth = 0;
            var inItems = false;
            var expectValue = false;
            string? key = null;
            var keyStart = 0;
            string? item = null;
            var itemStart = 0;

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];

                if (char.IsWhiteSpace(c))
                {
                    continue;
                }

                if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    i = text.IndexOf('\n', i) is var end and >= 0 ? end : text.Length;
                    continue;
                }

                if (c == '{')
                {
                    depth++;
                    expectValue = false;

                    if (depth == 2)
                    {
                        inItems = key == "items";
                    }
                    else if (depth == 3 && inItems && key != null && defIndices.Contains(key))
                    {
                        item = key;
                        itemStart = keyStart;
                    }

                    continue;
                }

                if (c == '}')
                {
                    if (depth == 3 && item != null)
                    {
                        items[item] = new Range(itemStart, i + 1);
                        item = null;
                    }

                    depth--;
                    expectValue = false;

                    if (depth == 1 && inItems)
                    {
                        break;
                    }

                    continue;
                }

                var start = i;
                var token = ReadToken(text, ref i);

                // Conditionals like [$WIN32] follow a value, they are neither a key nor a value
                if (token.StartsWith('['))
                {
                    continue;
                }

                if (!expectValue)
                {
                    key = token;
                    keyStart = start;
                }

                expectValue = !expectValue;
            }

            return items;
        }

        /// <summary>
        /// Reads a quoted or bare token starting at <paramref name="i"/>, leaving <paramref name="i"/> on its last
        /// character. Quoted tokens are returned without their quotes and with their escape sequences as written.
        /// </summary>
        private static string ReadToken(string text, ref int i)
        {
            if (text[i] == '"')
            {
                var start = i + 1;
                var end = start;

                while (end < text.Length && text[end] != '"')
                {
                    end += text[end] == '\\' ? 2 : 1;
                }

                i = Math.Min(end, text.Length - 1);

                return text[start..Math.Min(end, text.Length)];
            }

            var bareStart = i;

            while (i + 1 < text.Length && !char.IsWhiteSpace(text[i + 1]) && text[i + 1] is not ('{' or '}' or '"'))
            {
                i++;
            }

            return text[bareStart..(i + 1)];
        }

        private static KvNode Parse(string definition)
        {
            var i = 0;

            return ParseNode(definition, ref i) ?? throw new FormatException("The item definition is empty");
        }

        private static KvNode? ParseNode(string text, ref int i)
        {
            string? key = null;

            for (; i < text.Length; i++)
            {
                var c = text[i];

                if (char.IsWhiteSpace(c))
                {
                    continue;
                }

                if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    i = text.IndexOf('\n', i) is var end and >= 0 ? end : text.Length;
                    continue;
                }

                if (c == '}')
                {
                    return null;
                }

                if (c == '{')
                {
                    var node = new KvNode(key ?? string.Empty) { Children = [] };
                    i++;

                    while (ParseNode(text, ref i) is { } child)
                    {
                        node.Children.Add(child);
                        i++;
                    }

                    return node;
                }

                var token = ReadToken(text, ref i);

                if (token.StartsWith('['))
                {
                    continue;
                }

                if (key == null)
                {
                    key = token;
                    continue;
                }

                return new KvNode(key) { Value = token };
            }

            return null;
        }

        /// <summary>
        /// Writes a node the way items_game.txt is written, from its key on. The key is not indented, as it replaces text
        /// that starts where the indentation ends.
        /// </summary>
        private static void Write(KvNode node, StringBuilder text, int depth, string newLine)
        {
            text.Append('"').Append(node.Key).Append('"');

            if (node.Children == null)
            {
                text.Append("\t\t\"").Append(node.Value).Append('"');
                return;
            }

            text.Append(newLine).Append('\t', depth).Append('{').Append(newLine);

            foreach (var child in node.Children)
            {
                text.Append('\t', depth + 1);
                Write(child, text, depth + 1, newLine);
                text.Append(newLine);
            }

            text.Append('\t', depth).Append('}');
        }

        /// <summary>
        /// A key with either a value or children, with keys and values kept as written.
        /// </summary>
        private sealed class KvNode(string key)
        {
            public string Key { get; set; } = key;

            public string? Value { get; init; }

            public List<KvNode>? Children { get; init; }

            public KvNode? Get(string childKey) => Children?.FirstOrDefault(child => child.Key.Equals(childKey, StringComparison.OrdinalIgnoreCase));

            /// <summary>
            /// Puts a copy of <paramref name="replacement"/> in place of the child with the key, or removes the child
            /// when it is null.
            /// </summary>
            public void Replace(string childKey, KvNode? replacement)
            {
                if (Children == null)
                {
                    return;
                }

                var index = Children.FindIndex(child => child.Key.Equals(childKey, StringComparison.OrdinalIgnoreCase));

                if (replacement == null)
                {
                    if (index >= 0)
                    {
                        Children.RemoveAt(index);
                    }

                    return;
                }

                var copy = replacement.Clone();
                copy.Key = childKey;

                if (index >= 0)
                {
                    Children[index] = copy;
                }
                else
                {
                    Children.Add(copy);
                }
            }

            public KvNode Clone() => new(Key)
            {
                Value = Value,
                Children = Children?.Select(static child => child.Clone()).ToList(),
            };
        }
    }
}
