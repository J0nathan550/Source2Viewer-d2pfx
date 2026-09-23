using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.Exporter.CharacterAssets;
using GUI.Types.GLViewers;
using GUI.Utils;
using SkiaSharp;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Forms
{
    /// <summary>
    /// Picks a hero, the cosmetic item it wears in each loadout slot and the icons it shows, with a preview of the
    /// result, for <see cref="CharacterAssetsExporter"/>.
    /// </summary>
    partial class CharacterSelectForm : ThemedForm
    {
        private const string PersonaSelectorSlot = "persona_selector";

        // Remembered for the next time the dialog opens
        private static string? lastHeroName;
        private static CharacterExportOptions? lastOptions;

        private readonly ItemsGameCatalog catalog;
        private readonly VrfGuiContext guiContext;
        private readonly Package package;
        private readonly List<(HeroSlot Slot, ComboBox ComboBox, ComboBox StyleComboBox, ComboBox SkinComboBox)> slotRows = [];
        private readonly Dictionary<string, List<(string Name, string[] Materials)>> materialGroups = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(IconSlot Slot, ComboBox ComboBox, PictureBox Picture)> iconRows = [];
        private readonly HashSet<IconSlot> pickedIcons = [];
        private readonly Dictionary<string, Image?> thumbnails = new(StringComparer.OrdinalIgnoreCase);
        private GLCharacterPreviewViewer? previewViewer;
        private int heroIndex = -1;
        private bool updatingSelection;
        private bool updatingIcons;
        private bool updatingSkins;
        private bool heroChangedSincePreview = true;
        private bool previewLoading;
        private string? derivedGameFolder;
        private Font? groupFont;

        public HeroDefinition? SelectedHero => heroIndex >= 0 ? catalog.Heroes[heroIndex] : null;

        /// <summary>The addon's content folder, which gets the sources.</summary>
        public string? ContentFolder => contentFolderTextBox.Text.Trim() is { Length: > 0 } folder ? folder : null;

        /// <summary>The addon's game folder, which gets the compiled icons.</summary>
        public string? GameFolder => gameFolderTextBox.Text.Trim() is { Length: > 0 } folder ? folder : null;

        public CharacterExportOptions Options => new()
        {
            HeroModel = heroModelCheckBox.Checked,
            ItemModels = itemModelsCheckBox.Checked,
            ItemParticles = itemParticlesCheckBox.Checked,
            HeroParticles = heroParticlesCheckBox.Checked,
            ItemSounds = itemSoundsCheckBox.Checked,
            HeroSounds = heroSoundsCheckBox.Checked,
            HeroVoice = heroVoiceCheckBox.Checked,
            IncludeAudio = includeAudioCheckBox.Checked,
            Icons = iconsCheckBox.Checked,
            IconReplacements = GetIconReplacements(),
            Pedestal = pedestalCheckBox.Checked && pedestalCheckBox.Enabled,
            ReplaceDefaults = replaceDefaultsCheckBox.Checked,
            ReplaceSharedParticles = replaceSharedParticlesCheckBox.Checked,
        };

        public CharacterSelectForm(ItemsGameCatalog catalog, VrfGuiContext guiContext, Package package)
        {
            this.catalog = catalog;
            this.guiContext = guiContext;
            this.package = package;

            InitializeComponent();

            toolTip.SetToolTip(heroParticlesCheckBox, "Every particle in the hero's particle folder, which covers the effects of its abilities");
            toolTip.SetToolTip(iconsCheckBox,
                "Copy the icons picked on the Icons tab into the addon's game folder, under the names of the hero's own icons.\n" +
                "They are the compiled images the game already has, so they show as they are.");
            toolTip.SetToolTip(pedestalCheckBox,
                "The model the hero stands on in the loadout screen, and the particles items only play there.\n" +
                "Only available when an equipped item comes with one.");
            toolTip.SetToolTip(heroSoundsCheckBox, "The hero's game_sounds file, which points at the sounds in the game");
            toolTip.SetToolTip(heroVoiceCheckBox, "The hero's game_sounds_vo file, which points at the voice lines in the game");
            toolTip.SetToolTip(itemSoundsCheckBox, "The files the sound events the items swap in are defined in");
            toolTip.SetToolTip(includeAudioCheckBox, "Also export every sound the exported sound events play, which is most of the export's size");
            toolTip.SetToolTip(replaceDefaultsCheckBox,
                "Write the chosen look over the hero's default assets, so it shows without the items being equipped:\n" +
                "the arcana or persona model as the hero's model, chosen items over the default items' models,\n" +
                "particles the items swap in over the ones they replace, and particles items create added to their models");
            toolTip.SetToolTip(replaceSharedParticlesCheckBox, "Also replace particles every hero uses, like the blink dagger, stun and status effects");

            if (lastOptions != null)
            {
                ApplyOptions(lastOptions);
            }

            replaceSharedParticlesCheckBox.Enabled = replaceDefaultsCheckBox.Checked;

            // The game folder goes first, so one chosen by hand is not replaced by the one that goes with the content folder
            gameFolderTextBox.Text = Settings.Config.CharacterExportGameDir;
            contentFolderTextBox.Text = Settings.Config.CharacterExportContentDir;
            UpdateFolderToolTips();

            var searchNames = new AutoCompleteStringCollection();
            searchNames.AddRange([.. catalog.Heroes.Select(static hero => hero.DisplayName)]);
            heroSearchTextBox.AutoCompleteCustomSource = searchNames;
            heroSearchTextBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            heroSearchTextBox.AutoCompleteSource = AutoCompleteSource.CustomSource;

            var lastHeroIndex = FindHero(hero => hero.Name.Equals(lastHeroName, StringComparison.OrdinalIgnoreCase));
            SelectHero(Math.Max(lastHeroIndex, 0));
        }

        /// <summary>
        /// The item chosen in every slot, its style and the skin picked for it, skipping slots left empty.
        /// </summary>
        public List<EquippedItem> GetEquippedItems()
        {
            var items = new List<EquippedItem>();

            foreach (var (_, comboBox, styleComboBox, skinComboBox) in slotRows)
            {
                if (GetItem(comboBox) is not { } item)
                {
                    continue;
                }

                var equipped = new EquippedItem(item, (styleComboBox.SelectedItem as ItemStyle)?.Index ?? 0);

                if (skinComboBox.SelectedItem is SkinChoice skin && skin.Index != equipped.DefaultSkin)
                {
                    equipped = equipped with { SkinOverride = skin.Index };
                }

                items.Add(equipped);
            }

            return items;
        }

        public CharacterLoadout CreateLoadout()
            => CharacterLoadout.Create(catalog, SelectedHero ?? throw new InvalidOperationException("No hero is selected"), GetEquippedItems());

        private void ApplyOptions(CharacterExportOptions options)
        {
            heroModelCheckBox.Checked = options.HeroModel;
            itemModelsCheckBox.Checked = options.ItemModels;
            itemParticlesCheckBox.Checked = options.ItemParticles;
            heroParticlesCheckBox.Checked = options.HeroParticles;
            itemSoundsCheckBox.Checked = options.ItemSounds;
            heroSoundsCheckBox.Checked = options.HeroSounds;
            heroVoiceCheckBox.Checked = options.HeroVoice;
            includeAudioCheckBox.Checked = options.IncludeAudio;
            iconsCheckBox.Checked = options.Icons;
            pedestalCheckBox.Checked = options.Pedestal;
            replaceDefaultsCheckBox.Checked = options.ReplaceDefaults;
            replaceSharedParticlesCheckBox.Checked = options.ReplaceSharedParticles;
        }

        private void ReplaceDefaultsCheckBox_CheckedChanged(object? sender, EventArgs e)
        {
            replaceSharedParticlesCheckBox.Enabled = replaceDefaultsCheckBox.Checked;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            ShowPathEnd(contentFolderTextBox);
            ShowPathEnd(gameFolderTextBox);

            _ = LoadPreviewAsync();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);

            // The render loop only resumes on its own when the main window is activated, painting reattaches it
            previewViewer?.GLControl?.Invalidate();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            previewTimer.Stop();
            previewViewer?.Dispose();
            previewViewer = null;

            if (SelectedHero != null)
            {
                lastHeroName = SelectedHero.Name;
            }

            lastOptions = Options;

            foreach (var (_, _, picture) in iconRows)
            {
                picture.Image = null;
            }

            foreach (var thumbnail in thumbnails.Values)
            {
                thumbnail?.Dispose();
            }

            thumbnails.Clear();

            base.OnFormClosed(e);
        }

        private async Task LoadPreviewAsync()
        {
            GLCharacterPreviewViewer? viewer = null;
            previewLoading = true;

            try
            {
                viewer = new GLCharacterPreviewViewer(guiContext, guiContext.CreateRendererContext());
                viewer.SetModels(GetPreviewModels(), frameCamera: true);
                heroChangedSincePreview = false;

                // Loads models and textures, which would otherwise freeze the dialog
                await Task.Run(viewer.InitializeLoad).ConfigureAwait(true);

                if (IsDisposed)
                {
                    viewer.Dispose();
                    return;
                }

                var control = viewer.InitializeUiControls(isPreview: true);
                control.Dock = DockStyle.Fill;

                previewPanel.Controls.Remove(previewStatusLabel);
                previewPanel.Controls.Add(control);

                previewViewer = viewer;
                viewer = null;

                // The selection may have changed while the preview was loading
                SchedulePreviewUpdate();
            }
            catch (Exception ex)
            {
                viewer?.Dispose();

                Log.Error(nameof(CharacterSelectForm), $"Failed to load the character preview: {ex}");

                if (!IsDisposed)
                {
                    previewStatusLabel.Text = $"Preview is not available: {ex.Message}";
                }
            }
            finally
            {
                previewLoading = false;
            }
        }

        private void SelectHero(int index)
        {
            if (catalog.Heroes.Count == 0)
            {
                return;
            }

            heroIndex = (index % catalog.Heroes.Count + catalog.Heroes.Count) % catalog.Heroes.Count;

            var hero = catalog.Heroes[heroIndex];
            heroNameLabel.Text = hero.DisplayName;

            updatingSelection = true;

            try
            {
                BuildSlotRows(hero);
                BuildItemSets(hero);
                BuildIconRows(hero);
                ResetLoadout(persona: false);
            }
            finally
            {
                updatingSelection = false;
            }

            heroChangedSincePreview = true;

            UpdateSummary();
            SchedulePreviewUpdate();
        }

        private void BuildSlotRows(HeroDefinition hero)
        {
            slotsTable.SuspendLayout();

            foreach (var control in slotsTable.Controls.Cast<Control>().ToList())
            {
                control.Dispose();
            }

            slotsTable.Controls.Clear();
            slotsTable.RowStyles.Clear();
            slotsTable.RowCount = 0;
            slotRows.Clear();

            var slots = catalog.GetSlots(hero)
                .Select(slot => (Slot: slot, Items: catalog.GetItems(hero, slot.Name), Text: GetSlotText(slot)))
                .Where(static slot => slot.Items.Count > 0)
                .ToList();

            // Some heroes name two slots the same, e.g. the heads of both of Alchemist's riders
            var duplicateSlotTexts = slots
                .GroupBy(static slot => slot.Text, StringComparer.OrdinalIgnoreCase)
                .Where(static group => group.Count() > 1)
                .Select(static group => group.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var (slot, items, text) in slots)
            {
                var label = new Label
                {
                    AutoSize = true,
                    Anchor = AnchorStyles.Left,
                    MaximumSize = new System.Drawing.Size(this.AdjustForDPI(150), 0),
                    Text = duplicateSlotTexts.Contains(text) ? $"{text} ({slot.Name})" : text,
                    Margin = new Padding(3, 6, 6, 3),
                };

                toolTip.SetToolTip(label, slot.Name);

                var comboBox = new ThemedComboBox
                {
                    Dock = DockStyle.Fill,
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    MaxDropDownItems = 20,
                    Tag = slot,
                };

                comboBox.Items.Add(ItemChoice.None);

                // Items from different years can share a name, the def index tells them apart
                var duplicateNames = items
                    .GroupBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .Where(static group => group.Count() > 1)
                    .Select(static group => group.Key)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var item in items)
                {
                    comboBox.Items.Add(new ItemChoice(item, duplicateNames.Contains(item.Name)));
                }

                comboBox.SelectedIndex = 0;
                comboBox.SelectedIndexChanged += SlotComboBox_SelectedIndexChanged;

                var styleComboBox = new ThemedComboBox
                {
                    Anchor = AnchorStyles.Left | AnchorStyles.Right,
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    DisplayMember = nameof(ItemStyle.Name),
                    Width = this.AdjustForDPI(110),
                    DropDownWidth = this.AdjustForDPI(240),
                    Visible = false,
                };

                styleComboBox.SelectedIndexChanged += StyleComboBox_SelectedIndexChanged;
                toolTip.SetToolTip(styleComboBox, "Item style");

                var skinComboBox = new ThemedComboBox
                {
                    Anchor = AnchorStyles.Left | AnchorStyles.Right,
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Width = this.AdjustForDPI(100),
                    DropDownWidth = this.AdjustForDPI(320),
                    Visible = false,
                };

                skinComboBox.SelectedIndexChanged += SkinComboBox_SelectedIndexChanged;
                toolTip.SetToolTip(skinComboBox, "The material the item's model is shown with, which versions of the same item often differ in");

                var row = slotsTable.RowCount++;
                slotsTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                slotsTable.Controls.Add(label, 0, row);
                slotsTable.Controls.Add(comboBox, 1, row);
                slotsTable.Controls.Add(styleComboBox, 2, row);
                slotsTable.Controls.Add(skinComboBox, 3, row);

                slotRows.Add((slot, comboBox, styleComboBox, skinComboBox));
            }

            Themer.ThemeControl(slotsTable);
            slotsTable.ResumeLayout(true);
        }

        private void BuildItemSets(HeroDefinition hero)
        {
            itemSetComboBox.BeginUpdate();
            itemSetComboBox.Items.Clear();
            itemSetComboBox.Items.Add("Default items");

            foreach (var set in catalog.GetSets(hero))
            {
                itemSetComboBox.Items.Add(set);
            }

            itemSetComboBox.SelectedIndex = 0;
            itemSetComboBox.EndUpdate();
        }

        /// <summary>
        /// Equips the default item in every slot the hero uses in its normal form or as its persona, and nothing in the
        /// slots of the other form.
        /// </summary>
        /// <param name="persona">Whether to equip the hero's persona.</param>
        /// <param name="keepPersonaSelector">Leave the persona selector as the user picked it.</param>
        private void ResetLoadout(bool persona, bool keepPersonaSelector = false)
        {
            foreach (var (slot, comboBox, _, _) in slotRows)
            {
                if (slot.Name.Equals(PersonaSelectorSlot, StringComparison.OrdinalIgnoreCase))
                {
                    if (!keepPersonaSelector)
                    {
                        SelectItem(comboBox, item => persona ? IsPersonaItem(item) : item.IsDefault);
                    }
                }
                else if (IsSharedSlot(slot.Name) || IsPersonaSlot(slot.Name) == persona)
                {
                    SelectItem(comboBox, static item => item.IsDefault);
                }
                else
                {
                    comboBox.SelectedIndex = 0;
                }
            }
        }

        /// <summary>
        /// Persona slots often reuse the text of the slot they mirror, e.g. both are "Ambient Effects".
        /// </summary>
        private static string GetSlotText(HeroSlot slot)
            => IsPersonaSlot(slot.Name) && !slot.DisplayName.Contains("persona", StringComparison.OrdinalIgnoreCase)
                ? $"{slot.DisplayName} (Persona)"
                : slot.DisplayName;

        /// <summary>
        /// Slots of the hero's persona, which only apply while the persona is equipped.
        /// </summary>
        private static bool IsPersonaSlot(string slotName) => PersonaSlotRegex().IsMatch(slotName);

        /// <summary>
        /// Slots that apply to both the hero and its persona.
        /// </summary>
        private static bool IsSharedSlot(string slotName) => slotName.StartsWith("ability_effects", StringComparison.OrdinalIgnoreCase);

        private static bool IsPersonaItem(EconItem? item) => item?.AssetModifiers.Any(static modifier => modifier.Type == "persona") == true;

        private static EconItem? GetItem(ComboBox comboBox) => (comboBox.SelectedItem as ItemChoice)?.Item;

        /// <summary>
        /// Selects the first item that matches, or nothing when none does.
        /// </summary>
        private static void SelectItem(ComboBox comboBox, Func<EconItem, bool> predicate)
        {
            for (var i = 0; i < comboBox.Items.Count; i++)
            {
                if (comboBox.Items[i] is ItemChoice { Item: { } item } && predicate(item))
                {
                    comboBox.SelectedIndex = i;
                    return;
                }
            }

            comboBox.SelectedIndex = 0;
        }

        private void ItemSetComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (updatingSelection || itemSetComboBox.SelectedIndex < 0)
            {
                return;
            }

            updatingSelection = true;

            try
            {
                var set = itemSetComboBox.SelectedItem as ItemSet;

                // A set made for the persona only makes sense with the persona equipped
                ResetLoadout(persona: set?.Items.Any(static item => IsPersonaSlot(item.Slot)) == true);

                if (set != null)
                {
                    foreach (var item in set.Items)
                    {
                        var (_, comboBox, _, _) = slotRows.FirstOrDefault(row => row.Slot.Name.Equals(item.Slot, StringComparison.OrdinalIgnoreCase));

                        if (comboBox != null)
                        {
                            SelectItem(comboBox, candidate => candidate == item);
                        }
                    }
                }
            }
            finally
            {
                updatingSelection = false;
            }

            UpdateSummary();
            SchedulePreviewUpdate();
        }

        private void SlotComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (sender is ComboBox changedComboBox)
            {
                UpdateStyles(changedComboBox);
            }

            if (updatingSelection)
            {
                return;
            }

            updatingSelection = true;

            try
            {
                // Switching between the hero and its persona swaps out the whole loadout
                if (sender is ComboBox { Tag: HeroSlot slot } comboBox && slot.Name.Equals(PersonaSelectorSlot, StringComparison.OrdinalIgnoreCase))
                {
                    ResetLoadout(IsPersonaItem(GetItem(comboBox)), keepPersonaSelector: true);
                }

                // A hand picked item means the loadout no longer is the set that was chosen
                itemSetComboBox.SelectedIndex = -1;
            }
            finally
            {
                updatingSelection = false;
            }

            UpdateSummary();
            SchedulePreviewUpdate();
        }

        /// <summary>
        /// Offers the styles and skins of the item the slot's combo box now shows, starting from the first style and
        /// the skin the item shows in it.
        /// </summary>
        private void UpdateStyles(ComboBox comboBox)
        {
            var (_, _, styleComboBox, skinComboBox) = slotRows.FirstOrDefault(row => row.ComboBox == comboBox);

            if (styleComboBox == null)
            {
                return;
            }

            var item = GetItem(comboBox);
            var styles = item?.Styles ?? [];

            updatingSkins = true;

            try
            {
                skinComboBox.BeginUpdate();
                skinComboBox.Items.Clear();

                if (item != null && GetShownModel(item) is { } model)
                {
                    foreach (var skin in GetSkinChoices(model))
                    {
                        skinComboBox.Items.Add(skin);
                    }
                }

                skinComboBox.Visible = skinComboBox.Items.Count > 1;
                skinComboBox.EndUpdate();

                styleComboBox.BeginUpdate();
                styleComboBox.Items.Clear();

                foreach (var style in styles)
                {
                    styleComboBox.Items.Add(style);
                }

                styleComboBox.SelectedIndex = styles.Count > 0 ? 0 : -1;
                styleComboBox.Visible = styles.Count > 1;
                styleComboBox.EndUpdate();
            }
            finally
            {
                updatingSkins = false;
            }

            SelectDefaultSkin(comboBox, styleComboBox, skinComboBox);
        }

        private void StyleComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            // A style comes with its own skin
            var (_, comboBox, styleComboBox, skinComboBox) = slotRows.FirstOrDefault(row => row.StyleComboBox == sender);

            if (comboBox != null && !updatingSkins)
            {
                SelectDefaultSkin(comboBox, styleComboBox, skinComboBox);
            }

            if (!updatingSelection)
            {
                UpdateSummary();
            }

            SchedulePreviewUpdate();
        }

        private void SkinComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (!updatingSkins)
            {
                SchedulePreviewUpdate();
            }
        }

        /// <summary>
        /// Selects the skin the item shows in its chosen style.
        /// </summary>
        private void SelectDefaultSkin(ComboBox comboBox, ComboBox styleComboBox, ComboBox skinComboBox)
        {
            if (GetItem(comboBox) is not { } item || skinComboBox.Items.Count == 0)
            {
                return;
            }

            var skin = new EquippedItem(item, (styleComboBox.SelectedItem as ItemStyle)?.Index ?? 0).DefaultSkin;

            updatingSkins = true;

            try
            {
                skinComboBox.SelectedItem = skinComboBox.Items.OfType<SkinChoice>().FirstOrDefault(choice => choice.Index == skin) ?? skinComboBox.Items[0];
            }
            finally
            {
                updatingSkins = false;
            }
        }

        /// <summary>
        /// The model an item shows its skin on: its own, or the one it gives the hero or a unit the hero creates.
        /// </summary>
        private static string? GetShownModel(EconItem item)
            => item.ModelPlayer
                ?? item.AssetModifiers.FirstOrDefault(static modifier => modifier.Type == "entity_model"
                    && modifier.Modifier?.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase) == true)?.Modifier;

        /// <summary>
        /// The material groups of a model, named after the items and styles that show them, or after the material that
        /// sets them apart from the default group when no item does.
        /// </summary>
        private List<SkinChoice> GetSkinChoices(string model)
        {
            var groups = GetMaterialGroups(model);
            var choices = new List<SkinChoice>(groups.Count);

            if (groups.Count < 2 || SelectedHero == null)
            {
                return choices;
            }

            var itemNames = new Dictionary<int, List<string>>();
            var folder = Path.GetDirectoryName(CharacterLoadout.NormalizePath(model));

            void AddName(int skin, string name)
            {
                if (!itemNames.TryGetValue(skin, out var names))
                {
                    names = [];
                    itemNames.Add(skin, names);
                }

                if (!names.Contains(name))
                {
                    names.Add(name);
                }
            }

            // Versions of an item often come with their own copy of the model next to it, e.g. one with an extra material
            void AddItemSkin(string itemModel, int skin, string name)
            {
                if (CharacterLoadout.IsSamePath(itemModel, model))
                {
                    AddName(skin, name);
                    return;
                }

                if (GetMaterialGroups(itemModel).ElementAtOrDefault(skin).Materials is not { Length: > 0 } itemMaterials)
                {
                    return;
                }

                var match = groups.FindIndex(group => itemMaterials.All(material => group.Materials.Contains(material, StringComparer.OrdinalIgnoreCase)));

                if (match >= 0)
                {
                    AddName(match, name);
                }
            }

            foreach (var item in catalog.GetItems(SelectedHero))
            {
                if (GetShownModel(item) is not { } itemModel
                    || !string.Equals(Path.GetDirectoryName(CharacterLoadout.NormalizePath(itemModel)), folder, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AddItemSkin(itemModel, item.Skin, item.Name);

                foreach (var style in item.Styles)
                {
                    if (style.Skin is { } styleSkin && styleSkin != item.Skin)
                    {
                        AddItemSkin(itemModel, styleSkin, $"{item.Name} ({style.Name})");
                    }
                }
            }

            for (var i = 0; i < groups.Count; i++)
            {
                var (name, materials) = groups[i];
                var description = itemNames.TryGetValue(i, out var names)
                    ? string.Join(", ", names)
                    : materials.Where((material, index) => index >= groups[0].Materials.Length || !material.Equals(groups[0].Materials[index], StringComparison.OrdinalIgnoreCase))
                        .Select(static material => Path.GetFileNameWithoutExtension(material))
                        .FirstOrDefault() ?? name;

                choices.Add(new SkinChoice(i, $"{i}: {description}"));
            }

            return choices;
        }

        private List<(string Name, string[] Materials)> GetMaterialGroups(string model)
        {
            if (materialGroups.TryGetValue(model, out var cached))
            {
                return cached;
            }

            var groups = new List<(string Name, string[] Materials)>();
            var path = CharacterLoadout.NormalizePath(model) + GameFileLoader.CompiledFileSuffix;

            try
            {
                if (package.FindEntry(path) is { } entry)
                {
                    using var resource = new Resource { FileName = path };
                    resource.Read(GameFileLoader.GetPackageEntryStream(package, entry));

                    if (resource.DataBlock is Model modelData)
                    {
                        groups.AddRange(modelData.GetMaterialGroups());
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warn(nameof(CharacterSelectForm), $"Failed to read the skins of \"{model}\": {e.Message}");
            }

            materialGroups[model] = groups;

            return groups;
        }

        private void PreviousHeroButton_Click(object? sender, EventArgs e) => SelectHero(heroIndex - 1);

        private void NextHeroButton_Click(object? sender, EventArgs e) => SelectHero(heroIndex + 1);

        private void HeroSearchTextBox_TextChanged(object? sender, EventArgs e)
        {
            var text = heroSearchTextBox.Text.Trim();

            if (text.Length == 0)
            {
                return;
            }

            var index = FindHero(hero => hero.DisplayName.StartsWith(text, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
            {
                index = FindHero(hero => hero.ShortName.StartsWith(text, StringComparison.OrdinalIgnoreCase));
            }

            if (index < 0)
            {
                index = FindHero(hero => hero.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase));
            }

            if (index >= 0 && index != heroIndex)
            {
                SelectHero(index);
            }
        }

        private int FindHero(Func<HeroDefinition, bool> predicate)
        {
            var heroes = catalog.Heroes;

            for (var i = 0; i < heroes.Count; i++)
            {
                if (predicate(heroes[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        private void ExportButton_Click(object? sender, EventArgs e)
        {
            if (SelectedHero == null)
            {
                return;
            }

            if (ContentFolder == null || !Path.IsPathFullyQualified(ContentFolder))
            {
                PickContentFolder();

                if (ContentFolder == null)
                {
                    return;
                }
            }

            if (GameFolder == null && iconsCheckBox.Checked && GetIconReplacements().Count > 0)
            {
                PickGameFolder();

                if (GameFolder == null)
                {
                    return;
                }
            }

            Settings.Config.CharacterExportContentDir = ContentFolder;
            Settings.Config.CharacterExportGameDir = GameFolder ?? string.Empty;
            Settings.Save();

            DialogResult = DialogResult.OK;
        }

        private void ContentFolderButton_Click(object? sender, EventArgs e) => PickContentFolder();

        private void GameFolderButton_Click(object? sender, EventArgs e) => PickGameFolder();

        private void PickContentFolder()
        {
            if (AppFileDialogs.PickFolder("Choose the addon's content folder, e.g. content/dota_addons/<addon>", AppFileDialogs.RememberIn.SaveDirectory) is { } folder)
            {
                SetFolder(contentFolderTextBox, folder);
            }
        }

        private void PickGameFolder()
        {
            if (AppFileDialogs.PickFolder("Choose the addon's game folder, e.g. game/dota_addons/<addon>", AppFileDialogs.RememberIn.SaveDirectory) is { } folder)
            {
                SetFolder(gameFolderTextBox, folder);
            }
        }

        private static void SetFolder(TextBox textBox, string folder)
        {
            textBox.Text = folder;
            ShowPathEnd(textBox);
        }

        /// <summary>
        /// Scrolls a path that does not fit to its end, which names the addon.
        /// </summary>
        private static void ShowPathEnd(TextBox textBox)
        {
            if (textBox.IsHandleCreated)
            {
                textBox.Select(textBox.TextLength, 0);
                textBox.ScrollToCaret();
            }
        }

        private void GameFolderTextBox_TextChanged(object? sender, EventArgs e) => UpdateFolderToolTips();

        /// <summary>
        /// Shows the whole path in the tool tips, the boxes are too narrow for most.
        /// </summary>
        private void UpdateFolderToolTips()
        {
            toolTip.SetToolTip(contentFolderTextBox, "The addon's content folder, which gets the models, particles and sound events as sources." +
                (ContentFolder is { } content ? $"\n{content}" : string.Empty));
            toolTip.SetToolTip(gameFolderTextBox, "The addon's game folder, which gets the compiled icons. Filled in from the content folder when it can be." +
                (GameFolder is { } game ? $"\n{game}" : string.Empty));
        }

        /// <summary>
        /// Fills in the game folder that goes with the content folder, unless another one was chosen by hand.
        /// </summary>
        private void ContentFolderTextBox_TextChanged(object? sender, EventArgs e)
        {
            var derived = ContentFolder is { } content && Path.IsPathFullyQualified(content)
                ? CharacterAssetsExporter.GetGameFolder(content)
                : null;

            if (GameFolder == null || GameFolder == derivedGameFolder)
            {
                SetFolder(gameFolderTextBox, derived ?? string.Empty);
            }

            derivedGameFolder = derived;

            UpdateFolderToolTips();
        }

        private void UpdateSummary()
        {
            var count = slotRows.Count(static row => GetItem(row.ComboBox) != null);
            summaryLabel.Text = $"{count} item{(count == 1 ? "" : "s")} equipped";

            if (SelectedHero == null)
            {
                return;
            }

            var loadout = CreateLoadout();

            pedestalCheckBox.Enabled = loadout.Pedestals.Any();
            UpdateIconChoices(loadout);
        }

        /// <summary>
        /// Lists the hero's icons that items come with other versions of, grouped by kind.
        /// </summary>
        private void BuildIconRows(HeroDefinition hero)
        {
            iconsTable.SuspendLayout();

            foreach (var control in iconsTable.Controls.Cast<Control>().ToList())
            {
                control.Dispose();
            }

            iconsTable.Controls.Clear();
            iconsTable.RowStyles.Clear();
            iconsTable.RowCount = 0;
            iconRows.Clear();
            pickedIcons.Clear();

            var slots = CharacterIcons.GetSlots(catalog, hero, Exists);
            string? group = null;

            void AddFullRow(Control control)
            {
                var row = iconsTable.RowCount++;
                iconsTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                iconsTable.Controls.Add(control, 0, row);
                iconsTable.SetColumnSpan(control, 3);
            }

            if (slots.Count == 0)
            {
                AddFullRow(new Label
                {
                    AutoSize = true,
                    Text = $"No item comes with other icons for {hero.DisplayName}.",
                    Margin = new Padding(3, 8, 3, 3),
                });
            }

            foreach (var slot in slots)
            {
                if (slot.Group != group)
                {
                    group = slot.Group;

                    AddFullRow(new Label
                    {
                        AutoSize = true,
                        Text = group,
                        Font = groupFont ??= new Font(Font, FontStyle.Bold),
                        Margin = new Padding(3, 8, 3, 3),
                    });
                }

                var picture = new PictureBox
                {
                    Size = new Size(this.AdjustForDPI(64), this.AdjustForDPI(48)),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Margin = new Padding(3, 2, 6, 2),
                };

                var label = new Label
                {
                    AutoSize = true,
                    Anchor = AnchorStyles.Left,
                    MaximumSize = new Size(this.AdjustForDPI(130), 0),
                    Text = slot.DisplayName,
                    Margin = new Padding(3, 6, 6, 3),
                };

                var comboBox = new ThemedComboBox
                {
                    Anchor = AnchorStyles.Left | AnchorStyles.Right,
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    DropDownWidth = this.AdjustForDPI(320),
                    MaxDropDownItems = 20,
                    Tag = slot,
                };

                toolTip.SetToolTip(label, slot.Asset);

                foreach (var choice in slot.Choices)
                {
                    comboBox.Items.Add(choice);
                }

                comboBox.SelectedIndexChanged += IconComboBox_SelectedIndexChanged;

                var row = iconsTable.RowCount++;
                iconsTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                iconsTable.Controls.Add(picture, 0, row);
                iconsTable.Controls.Add(label, 1, row);
                iconsTable.Controls.Add(comboBox, 2, row);

                iconRows.Add((slot, comboBox, picture));
            }

            Themer.ThemeControl(iconsTable);
            iconsTable.ResumeLayout(true);
        }

        /// <summary>
        /// Shows the icons the equipped items come with, except where one was picked by hand.
        /// </summary>
        private void UpdateIconChoices(CharacterLoadout loadout)
        {
            updatingIcons = true;

            try
            {
                foreach (var (slot, comboBox, _) in iconRows)
                {
                    if (!pickedIcons.Contains(slot) || comboBox.SelectedIndex < 0)
                    {
                        comboBox.SelectedItem = loadout.GetIcon(slot);
                    }
                }
            }
            finally
            {
                updatingIcons = false;
            }
        }

        private void IconComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (sender is not ComboBox { Tag: IconSlot slot } comboBox)
            {
                return;
            }

            if (!updatingIcons)
            {
                pickedIcons.Add(slot);
            }

            var (_, _, picture) = iconRows.FirstOrDefault(row => row.ComboBox == comboBox);

            if (picture != null && comboBox.SelectedItem is IconChoice choice)
            {
                picture.Image = GetThumbnail(CharacterIcons.GetImagePath(slot.Folders[0], choice.Icon), picture.Size);
            }
        }

        private List<IconReplacement> GetIconReplacements()
            => [.. iconRows.SelectMany(row => row.ComboBox.SelectedItem is IconChoice choice ? row.Slot.GetReplacements(choice, Exists) : [])];

        private bool Exists(string path) => package.FindEntry(path) != null;

        /// <summary>
        /// Decodes an icon to show next to its choice, scaled down to the box it is shown in.
        /// </summary>
        private Image? GetThumbnail(string path, Size size)
        {
            if (thumbnails.TryGetValue(path, out var cached))
            {
                return cached;
            }

            Image? thumbnail = null;

            try
            {
                if (package.FindEntry(path) is { } entry)
                {
                    using var resource = new Resource { FileName = path };
                    resource.Read(GameFileLoader.GetPackageEntryStream(package, entry));

                    if (resource.DataBlock is Texture texture)
                    {
                        using var bitmap = texture.GenerateBitmap();
                        var scale = Math.Min(1f, Math.Min((float)size.Width / bitmap.Width, (float)size.Height / bitmap.Height));
                        var info = new SKImageInfo(Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale)));

                        using var resized = bitmap.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                        thumbnail = (resized ?? bitmap).ToBitmap();
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warn(nameof(CharacterSelectForm), $"Failed to load the icon \"{path}\": {e.Message}");
            }

            thumbnails[path] = thumbnail;

            return thumbnail;
        }

        private void SchedulePreviewUpdate()
        {
            previewTimer.Stop();
            previewTimer.Start();
        }

        private void PreviewTimer_Tick(object? sender, EventArgs e)
        {
            previewTimer.Stop();

            // Picked up once the preview finishes loading
            if (previewViewer == null || previewLoading)
            {
                return;
            }

            previewViewer.SetModels(GetPreviewModels(), heroChangedSincePreview);
            heroChangedSincePreview = false;
        }

        /// <summary>
        /// The hero's model, or the one an equipped item swaps it for, followed by the models the equipped items show,
        /// with the body groups the items switch.
        /// </summary>
        private List<PreviewModel> GetPreviewModels()
        {
            if (SelectedHero == null)
            {
                return [];
            }

            var loadout = CreateLoadout();
            var models = new List<PreviewModel>();

            void Add(string model, int skin)
            {
                if (!models.Any(existing => CharacterLoadout.IsSamePath(existing.Path, model)))
                {
                    models.Add(new PreviewModel(model, skin, loadout.GetBodyGroupChoices(model)));
                }
            }

            if (loadout.HeroModel != null)
            {
                Add(loadout.HeroModel, loadout.HeroSkin);
            }

            foreach (var item in loadout.Items)
            {
                if (loadout.GetItemModel(item) is { } model)
                {
                    Add(model, item.Skin);
                }
            }

            foreach (var (item, wearable) in loadout.AdditionalWearables)
            {
                Add(wearable, item.Skin);
            }

            return models;
        }

        [GeneratedRegex(@"_persona_\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex PersonaSlotRegex();

        /// <summary>
        /// One of the material groups of an item's model.
        /// </summary>
        private sealed record SkinChoice(int Index, string DisplayName)
        {
            public override string ToString() => DisplayName;
        }

        private sealed class ItemChoice
        {
            public static readonly ItemChoice None = new(null, showDefIndex: false);

            public EconItem? Item { get; }

            private readonly string text;

            public ItemChoice(EconItem? item, bool showDefIndex)
            {
                Item = item;

                if (item == null)
                {
                    text = "(none)";
                    return;
                }

                text = item.Name;

                if (item.IsDefault)
                {
                    text += " (default)";
                }
                else if (item.Rarity is { Length: > 0 } rarity && !rarity.Equals("common", StringComparison.OrdinalIgnoreCase))
                {
                    text += $" [{rarity}]";
                }

                if (showDefIndex)
                {
                    text += $" #{item.DefIndex}";
                }
            }

            public override string ToString() => text;
        }
    }
}
