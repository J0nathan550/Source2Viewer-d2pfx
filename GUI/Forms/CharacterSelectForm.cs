using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Linq;
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
        private const string NoUnusualEffect = "(none)";

        // Width of the controls next to the preview at 96 DPI, until the divider is dragged
        private const int DefaultControlsWidth = 540;

        private readonly CharacterExportPreferences preferences = CharacterExportPreferences.Load();
        private readonly ItemsGameCatalog catalog;
        private readonly VrfGuiContext guiContext;
        private readonly Package package;
        private readonly List<(HeroSlot Slot, ComboBox ComboBox, ComboBox StyleComboBox, ComboBox SkinComboBox)> slotRows = [];

        // Slots whose item was picked by hand, which choosing a set leaves alone unless the set has an item for them
        private readonly HashSet<string> pickedSlots = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Color> slotRecolors = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Color> effectRecolors = new(StringComparer.OrdinalIgnoreCase);
        private static Color lastPickedColor = VmatTextureRecolorer.LoadLastColor();
        private readonly Dictionary<string, List<(string Name, string[] Materials)>> materialGroups = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(IconSlot Slot, ComboBox ComboBox, PictureBox Picture)> iconRows = [];
        private readonly HashSet<IconSlot> pickedIcons = [];
        private readonly List<(SoundSlot Slot, ComboBox ComboBox)> soundRows = [];
        private readonly HashSet<SoundSlot> pickedSounds = [];
        private readonly List<(CreatedEffect Effect, CheckBox CheckBox)> effectRows = [];
        private readonly Dictionary<string, bool> pickedEffects = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> loadoutStagedEffects = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(string Particle, CheckBox CheckBox)> modelEffectRows = [];
        private readonly Dictionary<string, bool> pickedModelEffects = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> modelParticles = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> stagingParticles = new(StringComparer.OrdinalIgnoreCase);
#pragma warning disable CA2213 // Disposed with the export group box they are added to
        private readonly CheckBox skipUnchangedCheckBox;
        private readonly CheckBox arcanaMaterialsCheckBox;
        private readonly RadioButton recommendedRadioButton;
        private readonly RadioButton customRadioButton;
        private readonly Label exportHintLabel;
#pragma warning restore CA2213
        private CharacterExportOptions customOptions = new();
        private bool loadoutHasPedestal;
        private bool isRecommended;
        private readonly Dictionary<EconItem, UnusualEffect> pickedUnusuals = [];
        private readonly Dictionary<string, Image?> thumbnails = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string>? soundEventFiles;
#pragma warning disable CA2213 // Disposed with the sounds table it is added to
        private ComboBox? voiceComboBox;
#pragma warning restore CA2213
        private bool pickedVoice;
        private bool updatingSounds;
        private bool updatingEffects;
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
            OnlyWhatChanges = isRecommended,
            SkipUnchangedModels = skipUnchangedCheckBox.Checked,
            RemovedModelParticles = GetRemovedModelParticles(),
            HeroModel = heroModelCheckBox.Checked,
            ItemModels = itemModelsCheckBox.Checked,
            Materials = materialsCheckBox.Checked || slotRecolors.Count > 0,
            ArcanaMaterials = arcanaMaterialsCheckBox.Checked,
            MergeAdditionalWearables = mergeWearablesCheckBox.Checked,
            ItemParticles = itemParticlesCheckBox.Checked,
            ItemEffects = GetItemEffects(),
            HeroParticles = heroParticlesCheckBox.Checked,
            ItemSounds = itemSoundsCheckBox.Checked,
            HeroSounds = heroSoundsCheckBox.Checked,
            HeroVoice = heroVoiceCheckBox.Checked,
            IncludeAudio = includeAudioCheckBox.Checked,
            Icons = iconsCheckBox.Checked,
            IconReplacements = GetIconReplacements(),
            SpriteSheet = spriteSheetCheckBox.Checked,
            SpriteReplacements = GetSpriteReplacements(),
            DefaultItemsInItemsGame = itemsGameCheckBox.Checked,
            DefaultItemSwaps = SelectedHero != null ? ItemsGameEditor.GetSwaps(catalog, CreateLoadout()) : [],
            Sounds = soundsCheckBox.Checked,
            SoundReplacements = GetSoundReplacements(),
            Voice = voiceComboBox?.SelectedItem is VoiceChoice { Criteria: not null } voice ? voice : null,
            Pedestal = pedestalCheckBox.Checked && loadoutHasPedestal,
            ReplaceDefaults = replaceDefaultsCheckBox.Checked,
            ParticleRecolorOptions = new Dictionary<string, Color>(effectRecolors, StringComparer.OrdinalIgnoreCase),
            ReplaceSharedParticles = replaceSharedParticlesCheckBox.Checked,

            RenameModels = renameModelsCheckBox.Checked,
            AnimateOwnParts = animatePartsCheckBox.Checked,

            RecolorOptions = slotRecolors.ToDictionary(
                k => k.Key,
                v => new ItemRecolorOption(v.Value),
                StringComparer.OrdinalIgnoreCase),
        };

        public CharacterSelectForm(ItemsGameCatalog catalog, VrfGuiContext guiContext, Package package)
        {
            this.catalog = catalog;
            this.guiContext = guiContext;
            this.package = package;

            InitializeComponent();

            skipUnchangedCheckBox = new CheckBox { AutoSize = true, Text = "Only models the loadout changes", Checked = true };
            includeTable.RowCount++;
            includeTable.RowStyles.Add(new RowStyle());
            includeTable.Controls.Add(skipUnchangedCheckBox, 0, includeTable.RowCount - 1);
            includeTable.SetColumnSpan(skipUnchangedCheckBox, 3);

            arcanaMaterialsCheckBox = new CheckBox { AutoSize = true, Text = "Materials of arcana models", Checked = true };
            includeTable.RowCount++;
            includeTable.RowStyles.Add(new RowStyle());
            includeTable.Controls.Add(arcanaMaterialsCheckBox, 0, includeTable.RowCount - 1);
            includeTable.SetColumnSpan(arcanaMaterialsCheckBox, 3);
            materialsCheckBox.CheckedChanged += (_, _) => UpdateReplaceDefaultsDependents();

            recommendedRadioButton = new RadioButton { AutoSize = true, Text = "Recommended", Checked = true, Margin = new Padding(3, 3, 12, 3) };
            customRadioButton = new RadioButton { AutoSize = true, Text = "Custom" };
            recommendedRadioButton.CheckedChanged += (_, _) =>
            {
                if (recommendedRadioButton.Checked)
                {
                    SetRecommended(true);
                }
            };
            customRadioButton.CheckedChanged += (_, _) =>
            {
                if (customRadioButton.Checked)
                {
                    SetRecommended(false);
                }
            };

            var modePanel = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Dock = DockStyle.Top,
            };
            modePanel.Controls.Add(recommendedRadioButton);
            modePanel.Controls.Add(customRadioButton);

            exportHintLabel = new Label { AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(3, 0, 3, 4) };

            // Docked in reverse order of how they are added, so the mode goes first, then the hint, then the options
            includeGroupBox.Controls.Add(exportHintLabel);
            includeGroupBox.Controls.Add(modePanel);

            foreach (var checkBox in ExportCheckBoxes)
            {
                checkBox.CheckedChanged += (_, _) => UpdateExportHint();
            }

            toolTip.SetToolTip(heroParticlesCheckBox, "Every particle in the hero's particle folder, which covers the effects of its abilities");
            toolTip.SetToolTip(iconsCheckBox,
                "Copy the icons picked on the Icons tab into the addon's game folder, under the names of the hero's own icons.\n" +
                "They are the compiled images the game already has, so they show as they are.");
            toolTip.SetToolTip(pedestalCheckBox,
                "The model the hero stands on in the loadout screen, and the particles items only play there.\n" +
                "When replacing default assets, it is written over the hero's own pedestal in the style's skin.\n" +
                "Only available when an equipped item comes with one.");
            toolTip.SetToolTip(heroSoundsCheckBox, "The hero's game_sounds file, which points at the sounds in the game");
            toolTip.SetToolTip(heroVoiceCheckBox, "The hero's game_sounds_vo file, which points at the voice lines in the game");
            toolTip.SetToolTip(itemSoundsCheckBox, "The files the sound events the items swap in are defined in");
            toolTip.SetToolTip(includeAudioCheckBox, "Also export every sound the exported sound events play, which is most of the export's size");
            toolTip.SetToolTip(soundsCheckBox,
                "Write the sounds and voice picked on the Sounds tab over the hero's own, in the sound event files that define them,\n" +
                "so they play without the items being equipped");
            toolTip.SetToolTip(replaceDefaultsCheckBox,
                "Write the chosen look over the hero's default assets, so it shows without the items being equipped:\n" +
                "the arcana or persona model as the hero's model, chosen items over the default items' models,\n" +
                "a persona's items over the hero's own default items, hiding the ones it has nothing in place of,\n" +
                "particles the items swap in over the ones they replace, and particles items create added to their models");
            toolTip.SetToolTip(materialsCheckBox,
                "Decompile the materials every exported model uses, with their textures, so the addon compiles its own copies.\n" +
                "Otherwise the models use the game's own materials, which it applies by itself. Recoloring items exports them anyway.");
            toolTip.SetToolTip(arcanaMaterialsCheckBox,
                "Without Materials and textures, still decompile the materials of models that switch their meshes by the arcana level.\n" +
                "Those render semi-transparent in game when the addon uses the game's own.");
            toolTip.SetToolTip(mergeWearablesCheckBox, "When replacing default assets, add the meshes of the extra models items wear, e.g. an arcana's frost overlay,\n" +
                "to the hero's model. They are exported as models of their own either way.");
            toolTip.SetToolTip(replaceSharedParticlesCheckBox, "Also replace particles every hero uses, like the blink dagger, stun and status effects");
            toolTip.SetToolTip(renameModelsCheckBox,
                "When replacing default assets, move each item's exported model over the default model it replaces,\n" +
                "like renaming drow_arcana_weapon to drow_weapon. Body group choices of styles not picked are disabled, not removed,\n" +
                "so they can be turned back on in ModelDoc, and the _dummy choices are removed. The style's skin becomes the default one.\n" +
                "No extra meshes are merged. Particles items create and activity modifiers are still added.");
            toolTip.SetToolTip(animatePartsCheckBox,
                "When replacing default assets, add items that animate parts of their own, e.g. a wind-up key, to the hero's model,\n" +
                "with their animations played on those parts during the hero's, and hide the default model of their slot.\n" +
                "The game combines an addon hero's items into it, where such parts would otherwise stand still.\n" +
                "Untick it for models this does not suit, they are then written over the default models like other items.");
            toolTip.SetToolTip(spriteSheetCheckBox,
                "Point the hero's minimap icon at the one picked on the Icons tab, in a copy of scripts/mod_textures.txt in the game folder.\n" +
                "The minimap draws hero icons from this sprite sheet, not from the icon images.\n" +
                "The copy replaces the whole file, so other mods that change it stop working unless they are in the same folder.\n" +
                "A copy an earlier export wrote there is updated, so several heroes can share it.");
            toolTip.SetToolTip(recommendedRadioButton,
                "Export only what the loadout changes about the hero: the changed models written over the default ones with their materials,\n" +
                "the effects the game shows with the items, and the sounds and icons the items swap. Nothing the game already has as it is.");
            toolTip.SetToolTip(customRadioButton, "Pick what to export yourself. Options that add files the mod most likely does not need are pointed out before exporting.");
            toolTip.SetToolTip(skipUnchangedCheckBox,
                "When replacing default assets, leave out the models the loadout does not change, e.g. the hero's own model when only a weapon is swapped.\n" +
                "They would only be copies of what the game already has.");
            toolTip.SetToolTip(itemsGameCheckBox,
                "Write the equipped items over the hero's default items in a copy of scripts/items/items_game.txt in the game folder,\n" +
                "so the game shows them as the hero's default look, with the picked style. No model has to be replaced for this.\n" +
                "Slots left at their default item are restored. Items in slots without a default item, and unusual effects, are left out.\n" +
                "The copy replaces the whole file, so other mods that change it stop working unless they are in the same folder.\n" +
                "A copy an earlier export wrote there is updated, so several heroes can share it.");

            customOptions = preferences.Options ?? new CharacterExportOptions();

            if (customOptions.OnlyWhatChanges)
            {
                SetRecommended(true, keepTicked: false);
            }
            else
            {
                customRadioButton.Checked = true;
            }

            previewEffectsCheckBox.Checked = preferences.PreviewEffects;
            toolTip.SetToolTip(previewEffectsCheckBox,
                "Play the ticked effects and the unusual effects on the preview. Only roughly how the game shows them,\n" +
                "not everything particles do is supported.");

            // The game folder goes first, so one chosen by hand is not replaced by the one that goes with the content folder
            gameFolderTextBox.Text = Settings.Config.CharacterExportGameDir;
            contentFolderTextBox.Text = Settings.Config.CharacterExportContentDir;
            UpdateFolderToolTips();

            var searchNames = new AutoCompleteStringCollection();
            searchNames.AddRange([.. catalog.Heroes.Select(static hero => hero.DisplayName)]);
            heroSearchTextBox.AutoCompleteCustomSource = searchNames;
            heroSearchTextBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            heroSearchTextBox.AutoCompleteSource = AutoCompleteSource.CustomSource;

            var lastHeroIndex = FindHero(hero => hero.Name.Equals(preferences.LastHero, StringComparison.OrdinalIgnoreCase));
            SelectHero(Math.Max(lastHeroIndex, 0));
        }

        /// <summary>
        /// The item chosen in every slot, its style and the skin picked for it, skipping slots left empty.
        /// </summary>
        public List<EquippedItem> GetEquippedItems() => [.. slotRows.Select(GetEquippedItem).OfType<EquippedItem>()];

        private EquippedItem? GetEquippedItem((HeroSlot Slot, ComboBox ComboBox, ComboBox StyleComboBox, ComboBox SkinComboBox) row)
        {
            if (GetItem(row.ComboBox) is not { } item)
            {
                return null;
            }

            var equipped = new EquippedItem(item, (row.StyleComboBox.SelectedItem as ItemStyle)?.Index ?? 0, Unusual: pickedUnusuals.GetValueOrDefault(item));

            return row.SkinComboBox.SelectedItem is SkinChoice skin && skin.Index != equipped.DefaultSkin
                ? equipped with { SkinOverride = skin.Index }
                : equipped;
        }

        /// <summary>
        /// Remembers the selected hero's loadout, or forgets it when it is back to the default items, so heroes that were
        /// only looked at open with the items the game currently has as their defaults.
        /// </summary>
        private void SaveLoadout()
        {
            if (SelectedHero is not { } hero)
            {
                return;
            }

            SaveHeroColors(hero.Name);

            if (IsLoadoutPicked())
            {
                preferences.Loadouts[hero.Name] = GetSavedLoadout();
            }
            else
            {
                preferences.Loadouts.Remove(hero.Name);
            }
        }

        /// <summary>
        /// Whether anything differs from the default items <see cref="ResetLoadout"/> equips for the hero.
        /// </summary>
        private bool IsLoadoutPicked()
        {
            if (pickedEffects.Count > 0 || pickedModelEffects.Count > 0 || pickedIcons.Count > 0 || pickedSounds.Count > 0 || pickedVoice || pickedUnusuals.Count > 0
                || slotRecolors.Count > 0 || effectRecolors.Count > 0)
            {
                return true;
            }

            foreach (var row in slotRows)
            {
                if (GetEquippedItem(row) is not { } equipped)
                {
                    // Emptied by hand, persona slots are empty without the persona
                    if (!IsPersonaSlot(row.Slot.Name) && row.ComboBox.Items.OfType<ItemChoice>().Any(static choice => choice.Item?.IsDefault == true))
                    {
                        return true;
                    }

                    continue;
                }

                if (!equipped.Item.IsDefault || equipped.SkinOverride != null || equipped.Style != (equipped.Item.Styles.FirstOrDefault()?.Index ?? 0))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The loadout as picked, to be restored the next time the hero is selected.
        /// </summary>
        private SavedLoadout GetSavedLoadout()
        {
            var saved = new SavedLoadout
            {
                Effects = new(pickedEffects),
                ModelEffects = new(pickedModelEffects),
            };

            foreach (var row in slotRows)
            {
                var equipped = GetEquippedItem(row);

                saved.Slots[row.Slot.Name] = new SavedSlot
                {
                    Item = equipped?.Item.DefIndex,
                    Style = equipped?.Style ?? 0,
                    Skin = equipped?.SkinOverride,
                    Unusual = equipped?.Unusual?.Id,
                };
            }

            foreach (var (slot, comboBox, _) in iconRows)
            {
                if (pickedIcons.Contains(slot) && comboBox.SelectedItem is IconChoice choice)
                {
                    saved.Icons[SavedLoadout.GetIconKey(slot)] = choice.Icon;
                }
            }

            foreach (var (slot, comboBox) in soundRows)
            {
                if (pickedSounds.Contains(slot) && comboBox.SelectedItem is SoundChoice choice)
                {
                    saved.Sounds[slot.Event] = choice.Event;
                }
            }

            if (pickedVoice && voiceComboBox?.SelectedItem is VoiceChoice voice)
            {
                saved.VoicePicked = true;
                saved.Voice = voice.Criteria;
            }

            return saved;
        }

        /// <summary>
        /// Picks the loadout saved for the hero, leaving what no longer exists, e.g. a removed item, as it is.
        /// </summary>
        private void RestoreLoadout(SavedLoadout saved)
        {
            foreach (var (slot, comboBox, styleComboBox, skinComboBox) in slotRows)
            {
                if (!saved.Slots.TryGetValue(slot.Name, out var savedSlot))
                {
                    continue;
                }

                // In this order, since picking an item resets its style, and picking a style resets its skin
                SelectItem(comboBox, item => item.DefIndex == savedSlot.Item);

                if (styleComboBox.Items.OfType<ItemStyle>().FirstOrDefault(style => style.Index == savedSlot.Style) is { } savedStyle)
                {
                    styleComboBox.SelectedItem = savedStyle;
                }

                if (skinComboBox.Items.OfType<SkinChoice>().FirstOrDefault(skin => skin.Index == savedSlot.Skin) is { } savedSkin)
                {
                    skinComboBox.SelectedItem = savedSkin;
                }

                if (GetItem(comboBox) is not { } item)
                {
                    continue;
                }

                if (!item.IsDefault)
                {
                    pickedSlots.Add(slot.Name);
                }

                if (catalog.GetUnusualEffects(item).FirstOrDefault(effect => effect.Id == savedSlot.Unusual) is { } unusual)
                {
                    pickedUnusuals[item] = unusual;
                }
            }

            if (pickedSlots.Count > 0)
            {
                itemSetComboBox.SelectedIndex = -1;
            }

            foreach (var (particle, enabled) in saved.Effects)
            {
                pickedEffects[particle] = enabled;
            }

            foreach (var (particle, kept) in saved.ModelEffects ?? [])
            {
                pickedModelEffects[particle] = kept;
            }

            // Picking these by hand marks them picked, so the equipped items' choices do not replace them
            foreach (var (slot, comboBox, _) in iconRows)
            {
                if (saved.Icons.TryGetValue(SavedLoadout.GetIconKey(slot), out var icon)
                    && slot.Choices.FirstOrDefault(choice => choice.Icon.Equals(icon, StringComparison.OrdinalIgnoreCase)) is { } choice)
                {
                    comboBox.SelectedItem = choice;
                }
            }

            foreach (var (slot, comboBox) in soundRows)
            {
                if (saved.Sounds.TryGetValue(slot.Event, out var sound)
                    && slot.Choices.FirstOrDefault(choice => choice.Event.Equals(sound, StringComparison.OrdinalIgnoreCase)) is { } choice)
                {
                    comboBox.SelectedItem = choice;
                }
            }

            if (saved.VoicePicked && voiceComboBox?.Items.OfType<VoiceChoice>()
                .FirstOrDefault(choice => string.Equals(choice.Criteria, saved.Voice, StringComparison.OrdinalIgnoreCase)) is { } voice)
            {
                voiceComboBox.SelectedItem = voice;
            }
        }

        public CharacterLoadout CreateLoadout()
            => CharacterLoadout.Create(catalog, SelectedHero ?? throw new InvalidOperationException("No hero is selected"), GetEquippedItems());

        private void ApplyOptions(CharacterExportOptions options)
        {
            heroModelCheckBox.Checked = options.HeroModel;
            itemModelsCheckBox.Checked = options.ItemModels;
            materialsCheckBox.Checked = options.Materials;
            mergeWearablesCheckBox.Checked = options.MergeAdditionalWearables;
            itemParticlesCheckBox.Checked = options.ItemParticles;
            heroParticlesCheckBox.Checked = options.HeroParticles;
            itemSoundsCheckBox.Checked = options.ItemSounds;
            heroSoundsCheckBox.Checked = options.HeroSounds;
            heroVoiceCheckBox.Checked = options.HeroVoice;
            includeAudioCheckBox.Checked = options.IncludeAudio;
            iconsCheckBox.Checked = options.Icons;
            soundsCheckBox.Checked = options.Sounds;
            pedestalCheckBox.Checked = options.Pedestal;
            replaceDefaultsCheckBox.Checked = options.ReplaceDefaults;
            replaceSharedParticlesCheckBox.Checked = options.ReplaceSharedParticles;
            renameModelsCheckBox.Checked = options.RenameModels;
            animatePartsCheckBox.Checked = options.AnimateOwnParts;
            spriteSheetCheckBox.Checked = options.SpriteSheet;
            itemsGameCheckBox.Checked = options.DefaultItemsInItemsGame;
            skipUnchangedCheckBox.Checked = options.SkipUnchangedModels;
            arcanaMaterialsCheckBox.Checked = options.ArcanaMaterials;
        }

        private CheckBox[] ExportCheckBoxes =>
        [
            heroModelCheckBox, itemModelsCheckBox, pedestalCheckBox, itemParticlesCheckBox, heroParticlesCheckBox, iconsCheckBox,
            itemSoundsCheckBox, heroSoundsCheckBox, heroVoiceCheckBox, includeAudioCheckBox, soundsCheckBox, replaceDefaultsCheckBox,
            replaceSharedParticlesCheckBox, materialsCheckBox, mergeWearablesCheckBox, renameModelsCheckBox, animatePartsCheckBox,
            spriteSheetCheckBox, itemsGameCheckBox, skipUnchangedCheckBox, arcanaMaterialsCheckBox,
        ];

        /// <summary>
        /// The options as ticked, without what the loadout adds to them, to be remembered for <see cref="customOptions"/>.
        /// </summary>
        private CharacterExportOptions GetTickedOptions() => new()
        {
            OnlyWhatChanges = isRecommended,
            HeroModel = heroModelCheckBox.Checked,
            ItemModels = itemModelsCheckBox.Checked,
            Materials = materialsCheckBox.Checked,
            ArcanaMaterials = arcanaMaterialsCheckBox.Checked,
            MergeAdditionalWearables = mergeWearablesCheckBox.Checked,
            ItemParticles = itemParticlesCheckBox.Checked,
            HeroParticles = heroParticlesCheckBox.Checked,
            ItemSounds = itemSoundsCheckBox.Checked,
            HeroSounds = heroSoundsCheckBox.Checked,
            HeroVoice = heroVoiceCheckBox.Checked,
            IncludeAudio = includeAudioCheckBox.Checked,
            Icons = iconsCheckBox.Checked,
            SpriteSheet = spriteSheetCheckBox.Checked,
            DefaultItemsInItemsGame = itemsGameCheckBox.Checked,
            Sounds = soundsCheckBox.Checked,
            Pedestal = pedestalCheckBox.Checked,
            ReplaceDefaults = replaceDefaultsCheckBox.Checked,
            ReplaceSharedParticles = replaceSharedParticlesCheckBox.Checked,
            RenameModels = renameModelsCheckBox.Checked,
            AnimateOwnParts = animatePartsCheckBox.Checked,
            SkipUnchangedModels = skipUnchangedCheckBox.Checked,
        };

        /// <summary>
        /// Switches between the options picked from the loadout, shown ticked but locked, and the ones ticked by hand,
        /// which are kept aside meanwhile.
        /// </summary>
        /// <param name="keepTicked">Whether the options ticked by hand are on show, to be kept aside.</param>
        private void SetRecommended(bool recommended, bool keepTicked = true)
        {
            if (recommended)
            {
                if (keepTicked)
                {
                    customOptions = GetTickedOptions();
                }

                isRecommended = true;
                ApplyOptions(CharacterExportOptions.Recommended);
            }
            else
            {
                isRecommended = false;
                ApplyOptions(customOptions);
            }

            foreach (var checkBox in ExportCheckBoxes)
            {
                checkBox.Enabled = !recommended;
            }

            UpdateReplaceDefaultsDependents();
            UpdateExportHint();
        }

        private void ReplaceDefaultsCheckBox_CheckedChanged(object? sender, EventArgs e) => UpdateReplaceDefaultsDependents();

        private void UpdateReplaceDefaultsDependents()
        {
            var enabled = replaceDefaultsCheckBox.Checked && !isRecommended;
            replaceSharedParticlesCheckBox.Enabled = enabled;
            renameModelsCheckBox.Enabled = enabled;
            animatePartsCheckBox.Enabled = enabled;
            skipUnchangedCheckBox.Enabled = enabled;
            arcanaMaterialsCheckBox.Enabled = !materialsCheckBox.Checked && !isRecommended;
            pedestalCheckBox.Enabled = loadoutHasPedestal && !isRecommended;
        }

        /// <summary>
        /// Says what the recommended options do, or how many of the options ticked by hand add what the mod most likely
        /// does not need, see <see cref="CharacterExportOptions.GetWarnings"/>.
        /// </summary>
        private void UpdateExportHint()
        {
            if (isRecommended)
            {
                exportHintLabel.Text = "Exports only what the loadout changes.";
                toolTip.SetToolTip(exportHintLabel, "Pick Custom to choose what to export yourself.");
                return;
            }

            var warnings = GetTickedOptions().GetWarnings();

            exportHintLabel.Text = warnings.Count switch
            {
                0 => "Exports what is ticked below.",
                1 => "1 option adds what the mod may not need, hover for details.",
                var count => $"{count} options add what the mod may not need, hover for details.",
            };
            toolTip.SetToolTip(exportHintLabel, warnings.Count > 0 ? string.Join("\n", warnings) : null);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            var savedWidth = Settings.Config.CharacterExportControlsWidth;

            mainSplitContainer.Panel1MinSize = this.AdjustForDPI(200);
            SetControlsWidth(this.AdjustForDPI(savedWidth > 0 ? savedWidth : DefaultControlsWidth));
            mainSplitContainer.Panel2MinSize = this.AdjustForDPI(440);
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
                preferences.LastHero = SelectedHero.Name;
                SaveLoadout();
            }

            preferences.Options = isRecommended ? customOptions : GetTickedOptions();
            preferences.Options.OnlyWhatChanges = isRecommended;
            preferences.PreviewEffects = previewEffectsCheckBox.Checked;
            preferences.Save();

            var controlsWidth = (int)MathF.Round(mainSplitContainer.Panel2.Width * 96f / DeviceDpi);

            if (controlsWidth != Settings.Config.CharacterExportControlsWidth)
            {
                Settings.Config.CharacterExportControlsWidth = controlsWidth;
                Settings.Save();
            }

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

        /// <summary>
        /// Moves the divider so the controls get the given width, as far as the preview's minimum width lets them.
        /// </summary>
        private void SetControlsWidth(int width)
        {
            var maximum = mainSplitContainer.Width - mainSplitContainer.SplitterWidth - mainSplitContainer.Panel2MinSize;
            var distance = mainSplitContainer.Width - mainSplitContainer.SplitterWidth - width;

            mainSplitContainer.SplitterDistance = Math.Clamp(distance, mainSplitContainer.Panel1MinSize, Math.Max(mainSplitContainer.Panel1MinSize, maximum));
        }

        private void MainSplitContainer_SplitterMoved(object? sender, SplitterEventArgs e) => mainSplitContainer.Invalidate();

        /// <summary>
        /// Draws the divider between the preview and the controls, which is otherwise the same color as the dialog, with
        /// a grip in its middle to show it can be dragged.
        /// </summary>
        private void MainSplitContainer_Paint(object? sender, PaintEventArgs e)
        {
            var bounds = mainSplitContainer.SplitterRectangle;
            var x = bounds.X + (bounds.Width / 2);
            var y = bounds.Y + (bounds.Height / 2);
            var dot = Math.Max(2, this.AdjustForDPI(3));
            var gap = dot * 2;

            using var pen = new Pen(Themer.CurrentThemeColors.Border, Math.Max(1, this.AdjustForDPI(1)));
            e.Graphics.DrawLine(pen, x, bounds.Top, x, bounds.Bottom);

            using var brush = new SolidBrush(Themer.CurrentThemeColors.Contrast);

            for (var i = -2; i <= 2; i++)
            {
                e.Graphics.FillEllipse(brush, x - (dot / 2f), y + (i * gap) - (dot / 2f), dot, dot);
            }
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

            SaveLoadout();

            heroIndex = (index % catalog.Heroes.Count + catalog.Heroes.Count) % catalog.Heroes.Count;

            var hero = catalog.Heroes[heroIndex];
            heroNameLabel.Text = hero.DisplayName;

            updatingSelection = true;

            try
            {
                LoadHeroColors(hero.Name);
                BuildSlotRows(hero);
                BuildItemSets(hero);
                BuildIconRows(hero);
                BuildSoundRows(hero);
                pickedEffects.Clear();
                pickedModelEffects.Clear();
                pickedUnusuals.Clear();
                pickedSlots.Clear();
                ResetLoadout(persona: false);

                if (preferences.Loadouts.GetValueOrDefault(hero.Name) is { } saved)
                {
                    RestoreLoadout(saved);
                }
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
            slotsTable.ColumnStyles.Clear();
            slotRows.Clear();

            slotsTable.ColumnCount = 5;
            slotsTable.RowCount = 0;

            slotsTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));                // 0: Название слота (Weapon, Head...)
            slotsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));           // 1: Выбор предмета (растягивается)
            slotsTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));                // 2: Стиль
            slotsTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));                // 3: Скин
            slotsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, this.AdjustForDPI(34))); // 4: Колонка под кнопку цвета

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

                var comboBox = new SearchableComboBox
                {
                    Dock = DockStyle.Fill,
                    DropDownWidth = this.AdjustForDPI(360),
                    MaxDropDownItems = 20,
                    Tag = slot,
                };

                var colorButton = new Button
                {
                    Text = "🎨",
                    Width = this.AdjustForDPI(32),
                    Height = this.AdjustForDPI(26),
                    FlatStyle = FlatStyle.Flat,
                    UseVisualStyleBackColor = false,
                    BackColor = slotRecolors.TryGetValue(slot.Name, out var savedColor) ? savedColor : Color.Transparent,
                    Margin = new Padding(2),
                    Cursor = Cursors.Hand,
                };
                colorButton.Font = new Font("Segoe UI Emoji", 9f);

                toolTip.SetToolTip(colorButton, slotRecolors.TryGetValue(slot.Name, out var c)
                    ? $"Recolor: R:{c.R} G:{c.G} B:{c.B} (ПКМ для сброса)"
                    : "Recolor: выбрать кастомный RGB цвет (ПКМ для сброса)");

                // Выбор цвета по клику
                // Выбор цвета по клику с автоподстановкой последнего цвета
                colorButton.Click += (s, e) =>
                {
                    using var colorDialog = new ColorDialog
                    {
                        AllowFullOpen = true,
                        FullOpen = true,
                        // Если цвет слота уже был — открываем его, иначе ПОСЛЕДНИЙ использованный:
                        Color = slotRecolors.TryGetValue(slot.Name, out var currentColor)
                            ? currentColor
                            : lastPickedColor,
                    };

                    if (colorDialog.ShowDialog() == DialogResult.OK)
                    {
                        slotRecolors[slot.Name] = colorDialog.Color;
                        lastPickedColor = colorDialog.Color;
                        VmatTextureRecolorer.SaveLastColor(lastPickedColor); // Запоминаем цвет в реестр

                        colorButton.BackColor = colorDialog.Color;
                        toolTip.SetToolTip(colorButton, $"Recolor: R:{colorDialog.Color.R} G:{colorDialog.Color.G} B:{colorDialog.Color.B} (ПКМ для сброса)");
                    }
                };

                // Сброс цвета по правой кнопке мыши
                colorButton.MouseDown += (s, e) =>
                {
                    if (e.Button == MouseButtons.Right)
                    {
                        slotRecolors.Remove(slot.Name);
                        colorButton.BackColor = Color.Transparent;
                        toolTip.SetToolTip(colorButton, "Recolor: выбрать кастомный RGB цвет (ПКМ для сброса)");
                    }
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
                slotsTable.Controls.Add(colorButton, 4, row);

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
        private static bool IsPersonaSlot(string slotName) => CharacterLoadout.IsPersonaSlot(slotName);

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
                var persona = set?.Items.Any(static item => IsPersonaSlot(item.Slot)) == true;

                // Items picked by hand in slots the set has nothing for stay, e.g. an arcana chosen before the set
                var keptRows = set == null
                    ? []
                    : slotRows
                        .Where(row => pickedSlots.Contains(row.Slot.Name)
                            && !set.Items.Any(item => item.Slot.Equals(row.Slot.Name, StringComparison.OrdinalIgnoreCase))
                            && !row.Slot.Name.Equals(PersonaSelectorSlot, StringComparison.OrdinalIgnoreCase)
                            && (IsSharedSlot(row.Slot.Name) || IsPersonaSlot(row.Slot.Name) == persona))
                        .Select(static row => (Row: row, Item: row.ComboBox.SelectedIndex, Style: row.StyleComboBox.SelectedIndex, Skin: row.SkinComboBox.SelectedIndex))
                        .ToList();

                ResetLoadout(persona);
                pickedSlots.Clear();

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

                foreach (var (row, item, style, skin) in keptRows)
                {
                    // In this order, since picking an item resets its style, and picking a style resets its skin
                    row.ComboBox.SelectedIndex = item;
                    row.StyleComboBox.SelectedIndex = style;
                    row.SkinComboBox.SelectedIndex = skin;
                    pickedSlots.Add(row.Slot.Name);
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
                if (sender is ComboBox { Tag: HeroSlot slot } comboBox)
                {
                    // Switching between the hero and its persona swaps out the whole loadout
                    if (slot.Name.Equals(PersonaSelectorSlot, StringComparison.OrdinalIgnoreCase))
                    {
                        ResetLoadout(IsPersonaItem(GetItem(comboBox)), keepPersonaSelector: true);
                        pickedSlots.Clear();
                    }
                    else if (GetItem(comboBox) is { IsDefault: false })
                    {
                        pickedSlots.Add(slot.Name);
                    }
                    else
                    {
                        pickedSlots.Remove(slot.Name);
                    }
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

            var styles = GetItem(comboBox)?.Styles ?? [];

            updatingSkins = true;

            try
            {
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

            UpdateSkins(comboBox, styleComboBox, skinComboBox);
        }

        /// <summary>
        /// Offers the skins of the model the item shows in its chosen style, starting from the one the style shows.
        /// </summary>
        private void UpdateSkins(ComboBox comboBox, ComboBox styleComboBox, ComboBox skinComboBox)
        {
            var item = GetItem(comboBox);

            updatingSkins = true;

            try
            {
                skinComboBox.BeginUpdate();
                skinComboBox.Items.Clear();

                if (item != null && GetShownModel(item, (styleComboBox.SelectedItem as ItemStyle)?.Index ?? 0) is { } model)
                {
                    foreach (var skin in GetSkinChoices(model))
                    {
                        skinComboBox.Items.Add(skin);
                    }
                }

                skinComboBox.Visible = skinComboBox.Items.Count > 1;
                skinComboBox.EndUpdate();
            }
            finally
            {
                updatingSkins = false;
            }

            SelectDefaultSkin(comboBox, styleComboBox, skinComboBox);
        }

        private void StyleComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            // A style comes with its own skin, and can come with a model of its own too
            var (_, comboBox, styleComboBox, skinComboBox) = slotRows.FirstOrDefault(row => row.StyleComboBox == sender);

            if (comboBox != null && !updatingSkins)
            {
                UpdateSkins(comboBox, styleComboBox, skinComboBox);
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
        /// The model an item shows its skin on in a style: its own, or the one it gives the hero or a unit the hero creates.
        /// </summary>
        private static string? GetShownModel(EconItem item, int style)
            => new EquippedItem(item, style).Model
                ?? item.AssetModifiers.FirstOrDefault(modifier => modifier.Type == "entity_model" && (modifier.Style == null || modifier.Style == style)
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

            bool IsInFolder([NotNullWhen(true)] string? itemModel)
                => itemModel != null && string.Equals(Path.GetDirectoryName(CharacterLoadout.NormalizePath(itemModel)), folder, StringComparison.OrdinalIgnoreCase);

            foreach (var item in catalog.GetItems(SelectedHero))
            {
                var itemModel = GetShownModel(item, style: 0);

                if (IsInFolder(itemModel))
                {
                    AddItemSkin(itemModel, item.Skin, item.Name);
                }

                foreach (var style in item.Styles)
                {
                    var styleModel = GetShownModel(item, style.Index);
                    var styleSkin = style.Skin ?? item.Skin;

                    if (IsInFolder(styleModel) && (styleSkin != item.Skin || !CharacterLoadout.IsSamePath(styleModel, itemModel)))
                    {
                        AddItemSkin(styleModel, styleSkin, $"{item.Name} ({style.Name})");
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

        private async void ExportButton_Click(object? sender, EventArgs e)
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

            if (GameFolder == null && ((iconsCheckBox.Checked && GetIconReplacements().Count > 0)
                || (spriteSheetCheckBox.Checked && GetSpriteReplacements().Count > 0)
                || itemsGameCheckBox.Checked))
            {
                PickGameFolder();

                if (GameFolder == null)
                {
                    return;
                }
            }

            if (!isRecommended && GetTickedOptions().GetWarnings() is { Count: > 0 } warnings
                && !await AppMessageDialogs.ConfirmAsync(CharacterExportOptions.GetWarningsMessage(warnings), "Check the export options",
                    MessageIcon.Warning, ConfirmButtons.YesNo).ConfigureAwait(true))
            {
                return;
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

            loadoutHasPedestal = loadout.Pedestals.Any();
            pedestalCheckBox.Enabled = loadoutHasPedestal && !isRecommended;
            UpdateIconChoices(loadout);
            UpdateSoundChoices(loadout);
            BuildEffectRows(loadout);
        }

        /// <summary>
        /// Lists the effects the equipped items create, by item, ticked when the game shows them with the equipped items
        /// unless they were ticked or unticked by hand, and the unusual effect of items that come in unusual versions.
        /// What default items create is left out, the game shows it anyway.
        /// </summary>
        private void BuildEffectRows(CharacterLoadout loadout)
        {
            effectsTable.SuspendLayout();

            foreach (var control in effectsTable.Controls.Cast<Control>().ToList())
            {
                control.Dispose();
            }

            effectsTable.Controls.Clear();
            effectsTable.RowStyles.Clear();
            effectsTable.RowCount = 0;
            effectRows.Clear();
            modelEffectRows.Clear();

            void AddRow(Control control)
            {
                var row = effectsTable.RowCount++;
                effectsTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                effectsTable.Controls.Add(control, 0, row);
            }

            var effects = loadout.CreatedEffects.Where(static effect => !effect.Item.Item.IsDefault && !effect.IsUnusual).ToList();
            var items = loadout.Items
                .Where(item => !item.Item.IsDefault && (catalog.GetUnusualEffects(item.Item).Count > 0 || effects.Any(effect => effect.Item == item)))
                .ToList();

            if (items.Count == 0)
            {
                AddRow(new Label
                {
                    AutoSize = true,
                    Text = "No equipped item creates effects.",
                    Margin = new Padding(3, 8, 3, 3),
                });
            }

            updatingEffects = true;

            try
            {
                foreach (var item in items)
                {
                    var currentItemEffects = effects.Where(effect => effect.Item == item).ToList();

                    // Панель заголовка предмета (название + кнопка палитры для ВСЕХ эффектов предмета)
                    var headerPanel = new FlowLayoutPanel
                    {
                        AutoSize = true,
                        AutoSizeMode = AutoSizeMode.GrowAndShrink,
                        WrapContents = false,
                        Margin = new Padding(3, 8, 3, 3),
                    };

                    var titleLabel = new Label
                    {
                        AutoSize = true,
                        Anchor = AnchorStyles.Left,
                        Text = item.StyleName is { } styleName ? $"{item.Item.Name} ({styleName})" : item.Item.Name,
                        Font = groupFont ??= new Font(Font, FontStyle.Bold),
                        Margin = new Padding(0, 4, 6, 0),
                    };

                    // Кнопка палитры для ВСЕХ эффектов предмета (как Призматический самоцвет)
                    var itemFxBtn = new Button
                    {
                        Text = "🎨",
                        Font = new Font("Segoe UI Emoji", 9f),
                        Width = this.AdjustForDPI(30),
                        Height = this.AdjustForDPI(24),
                        FlatStyle = FlatStyle.Flat,
                        UseVisualStyleBackColor = false,
                        BackColor = effectRecolors.TryGetValue(item.Item.Name, out var savedFxColor) ? savedFxColor : Color.Transparent,
                        Cursor = Cursors.Hand,
                    };
                    toolTip.SetToolTip(itemFxBtn, "Prismatic Gem: задать цвет для всех эффектов этого предмета (ПКМ для сброса)");

                    itemFxBtn.Click += (s, e) =>
                    {
                        using var cd = new ColorDialog { AllowFullOpen = true, FullOpen = true };
                        if (effectRecolors.TryGetValue(item.Item.Name, out var curCol)) cd.Color = curCol;

                        if (cd.ShowDialog() == DialogResult.OK)
                        {
                            effectRecolors[item.Item.Name] = cd.Color;
                            itemFxBtn.BackColor = cd.Color;

                            // Красим все дочерние партиклы этого предмета
                            foreach (var fx in currentItemEffects)
                            {
                                effectRecolors[fx.Particle] = cd.Color;
                            }
                            SchedulePreviewUpdate();
                        }
                    };

                    itemFxBtn.MouseDown += (s, e) =>
                    {
                        if (e.Button == MouseButtons.Right)
                        {
                            effectRecolors.Remove(item.Item.Name);
                            foreach (var fx in currentItemEffects)
                            {
                                effectRecolors.Remove(fx.Particle);
                            }
                            itemFxBtn.BackColor = Color.Transparent;
                            SchedulePreviewUpdate();
                        }
                    };

                    headerPanel.Controls.Add(titleLabel);
                    headerPanel.Controls.Add(itemFxBtn);
                    AddRow(headerPanel);

                    if (catalog.GetUnusualEffects(item.Item) is { Count: > 0 } unusualEffects)
                    {
                        AddRow(CreateUnusualRow(item, unusualEffects));
                    }

                    // Чекбоксы эффектов с индивидуальной палитрой 🎨 для каждого партикла
                    foreach (var effect in currentItemEffects)
                    {
                        AddRow(CreateEffectRow(loadout, effect));
                    }
                }

                AddModelEffectRows(loadout, AddRow);
            }
            finally
            {
                updatingEffects = false;
            }

            Themer.ThemeControl(effectsTable);
            effectsTable.ResumeLayout(true);
        }

        /// <summary>
        /// Создает строку отдельного партикла с чекбоксом и кнопкой палитры 🎨.
        /// </summary>
        private FlowLayoutPanel CreateEffectRow(CharacterLoadout loadout, CreatedEffect effect)
        {
            var checkBox = CreateEffectCheckBox(loadout, effect);

            var panel = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = new Padding(12, 1, 3, 1),
            };

            var fxBtn = new Button
            {
                Text = "🎨",
                Font = new Font("Segoe UI Emoji", 8f),
                Width = this.AdjustForDPI(26),
                Height = this.AdjustForDPI(20),
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false,
                BackColor = effectRecolors.TryGetValue(effect.Particle, out var c) ? c : Color.Transparent,
                Cursor = Cursors.Hand,
                Margin = new Padding(4, 1, 0, 0),
            };
            toolTip.SetToolTip(fxBtn, "Prismatic: цвет конкретно для этого эффекта (ПКМ для сброса)");

            fxBtn.Click += (s, e) =>
            {
                using var cd = new ColorDialog { AllowFullOpen = true, FullOpen = true };
                if (effectRecolors.TryGetValue(effect.Particle, out var curCol)) cd.Color = curCol;

                if (cd.ShowDialog() == DialogResult.OK)
                {
                    effectRecolors[effect.Particle] = cd.Color;
                    fxBtn.BackColor = cd.Color;
                    SchedulePreviewUpdate();
                }
            };

            fxBtn.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    effectRecolors.Remove(effect.Particle);
                    fxBtn.BackColor = Color.Transparent;
                    SchedulePreviewUpdate();
                }
            };

            panel.Controls.Add(checkBox);
            panel.Controls.Add(fxBtn);

            return panel;
        }

        /// <summary>
        /// A choice of the unusual effect the item plays, none by default like the item's plain version.
        /// </summary>
        private FlowLayoutPanel CreateUnusualRow(EquippedItem item, IReadOnlyList<UnusualEffect> unusualEffects)
        {
            var label = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Text = "Unusual effect",
                Margin = new Padding(0, 6, 6, 3),
            };

            var comboBox = new ThemedComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = this.AdjustForDPI(180),
                DropDownWidth = this.AdjustForDPI(240),
                MaxDropDownItems = 20,
                Tag = item.Item,
            };

            comboBox.Items.Add(NoUnusualEffect);

            foreach (var effect in unusualEffects)
            {
                comboBox.Items.Add(effect);
            }

            comboBox.SelectedItem = item.Unusual ?? (object)NoUnusualEffect;
            comboBox.SelectedIndexChanged += UnusualComboBox_SelectedIndexChanged;

            SetUnusualToolTip(comboBox);

            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = new Padding(12, 2, 3, 2),
            };

            row.Controls.Add(label);
            row.Controls.Add(comboBox);

            return row;
        }

        private void UnusualComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (sender is not ComboBox { Tag: EconItem item } comboBox)
            {
                return;
            }

            if (comboBox.SelectedItem is UnusualEffect effect)
            {
                pickedUnusuals[item] = effect;
            }
            else
            {
                pickedUnusuals.Remove(item);
            }

            SetUnusualToolTip(comboBox);
            SchedulePreviewUpdate();
        }

        private void SetUnusualToolTip(ComboBox comboBox)
            => toolTip.SetToolTip(comboBox, "The effect the item's unusual version plays on it, which is exported like the item's other effects" +
                (comboBox.SelectedItem is UnusualEffect effect ? $"\n{effect.Particle}" : string.Empty));

        private CheckBox CreateEffectCheckBox(CharacterLoadout loadout, CreatedEffect effect)
        {
            var stagedForLoadout = IsStagedForLoadout(effect.Particle);
            var shown = loadout.IsShown(effect) && !stagedForLoadout;
            var required = effect.Modifier.RequiredArcanaLevel switch
            {
                null => string.Empty,
                0 => " (without an arcana)",
                var level => $" (arcana level {level})",
            };

            var checkBox = new CheckBox
            {
                AutoSize = true,
                Text = Path.GetFileNameWithoutExtension(effect.Particle) + required,
                Checked = pickedEffects.TryGetValue(effect.Particle, out var picked) ? picked : shown,
                Margin = new Padding(12, 2, 3, 2),
                Tag = effect,
            };

            toolTip.SetToolTip(checkBox, shown
                ? effect.Particle
                : stagedForLoadout
                    ? $"{effect.Particle}\nIt is set up for the loadout screen at the world origin and left out by default. Ticked, it is exported to follow the model"
                    : $"{effect.Particle}\nThe game does not show it with the equipped items, it is made for another arcana level");

            checkBox.CheckedChanged += EffectCheckBox_CheckedChanged;
            effectRows.Add((effect, checkBox));

            return checkBox;
        }

        /// <summary>
        /// See <see cref="ModelDocEditor.IsStagedForLoadout"/>.
        /// </summary>
        private bool IsStagedForLoadout(string particle)
        {
            if (!loadoutStagedEffects.TryGetValue(particle, out var stagedForLoadout))
            {
                try
                {
                    stagedForLoadout = ModelDocEditor.IsStagedForLoadout(guiContext.FileLoaderNoCache, particle);
                }
                catch (Exception e)
                {
                    Log.Error(nameof(CharacterSelectForm), $"Failed to read '{particle}': {e.Message}");
                }

                loadoutStagedEffects[particle] = stagedForLoadout;
            }

            return stagedForLoadout;
        }

        private void EffectCheckBox_CheckedChanged(object? sender, EventArgs e)
        {
            if (!updatingEffects && sender is CheckBox { Tag: CreatedEffect effect } checkBox)
            {
                pickedEffects[effect.Particle] = checkBox.Checked;
                SchedulePreviewUpdate();
            }
        }

        private void PreviewEffectsCheckBox_CheckedChanged(object? sender, EventArgs e) => SchedulePreviewUpdate();

        /// <summary>
        /// Lists the particles the models the hero wears create themselves, ticked to be kept unless they look like
        /// staging for the loadout screen, e.g. a circle on the ground, see <see cref="ModelDocEditor.LooksLikeStaging"/>.
        /// The unticked ones are taken out of the exported models.
        /// </summary>
        private void AddModelEffectRows(CharacterLoadout loadout, Action<Control> addRow)
        {
            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var first = true;

            foreach (var model in loadout.WornModels)
            {
                var particles = GetModelParticles(model).Where(listed.Add).ToList();

                if (particles.Count == 0)
                {
                    continue;
                }

                if (first)
                {
                    first = false;

                    var header = new Label
                    {
                        AutoSize = true,
                        Text = "Effects the models play themselves",
                        Font = groupFont ??= new Font(Font, FontStyle.Bold),
                        Margin = new Padding(3, 14, 3, 3),
                    };
                    toolTip.SetToolTip(header, "Unticked effects are taken out of the exported models, which are written for it even when nothing else about them changes.");
                    addRow(header);
                }

                addRow(new Label
                {
                    AutoSize = true,
                    Text = Path.GetFileNameWithoutExtension(model),
                    Margin = new Padding(12, 4, 3, 1),
                });

                foreach (var particle in particles)
                {
                    var staging = LooksLikeStaging(particle);

                    var checkBox = new CheckBox
                    {
                        AutoSize = true,
                        Text = Path.GetFileNameWithoutExtension(particle),
                        Checked = pickedModelEffects.TryGetValue(particle, out var kept) ? kept : !staging,
                        Margin = new Padding(24, 2, 3, 2),
                        Tag = particle,
                    };

                    toolTip.SetToolTip(checkBox, staging
                        ? $"{particle}\nIt looks like staging for the loadout screen, e.g. a circle on the ground, so it is taken out by default. Tick it to keep it"
                        : $"{particle}\nUntick it to take it out of the exported model");

                    checkBox.CheckedChanged += (_, _) =>
                    {
                        if (!updatingEffects)
                        {
                            pickedModelEffects[particle] = checkBox.Checked;
                        }
                    };

                    modelEffectRows.Add((particle, checkBox));
                    addRow(checkBox);
                }
            }
        }

        private List<string> GetModelParticles(string model)
        {
            if (!modelParticles.TryGetValue(model, out var particles))
            {
                try
                {
                    particles = ModelDocEditor.GetOwnParticles(guiContext.FileLoaderNoCache, model);
                }
                catch (Exception e)
                {
                    Log.Error(nameof(CharacterSelectForm), $"Failed to read the particles of '{model}': {e.Message}");
                    particles = [];
                }

                modelParticles[model] = particles;
            }

            return particles;
        }

        /// <summary>
        /// See <see cref="ModelDocEditor.LooksLikeStaging"/>.
        /// </summary>
        private bool LooksLikeStaging(string particle)
        {
            if (!stagingParticles.TryGetValue(particle, out var staging))
            {
                try
                {
                    staging = ModelDocEditor.LooksLikeStaging(guiContext.FileLoaderNoCache, particle);
                }
                catch (Exception e)
                {
                    Log.Error(nameof(CharacterSelectForm), $"Failed to read '{particle}': {e.Message}");
                }

                stagingParticles[particle] = staging;
            }

            return staging;
        }

        private HashSet<string> GetRemovedModelParticles()
            => modelEffectRows.Where(static row => !row.CheckBox.Checked).Select(static row => row.Particle).ToHashSet(StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, bool> GetItemEffects()
        {
            var effects = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            // An effect two items create is exported when either of them has it ticked
            foreach (var (effect, checkBox) in effectRows)
            {
                effects[effect.Particle] = checkBox.Checked || effects.GetValueOrDefault(effect.Particle);
            }

            return effects;
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

            var slots = CharacterIcons.GetSlots(catalog, hero, Exists, GetPixels);
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

                toolTip.SetToolTip(label, slot.SharesSwapsOf is { } shared
                    ? $"{slot.Asset}\nShows the same picture as {shared}, so it gets the icons items give that ability"
                    : slot.Asset);

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

        private List<SpriteReplacement> GetSpriteReplacements()
            => [.. iconRows.Select(static row => row.ComboBox.SelectedItem is IconChoice choice ? row.Slot.GetSpriteReplacement(choice) : null).OfType<SpriteReplacement>()];

        /// <summary>
        /// Lists the voices the hero's items switch it to and the sounds they swap, the hero's own sounds before the ones
        /// every hero plays.
        /// </summary>
        private void BuildSoundRows(HeroDefinition hero)
        {
            soundsTable.SuspendLayout();

            foreach (var control in soundsTable.Controls.Cast<Control>().ToList())
            {
                control.Dispose();
            }

            soundsTable.Controls.Clear();
            soundsTable.RowStyles.Clear();
            soundsTable.RowCount = 0;
            soundRows.Clear();
            pickedSounds.Clear();
            voiceComboBox = null;
            pickedVoice = false;

            soundEventFiles ??= CharacterSounds.GetEventFiles(package);

            var voices = CharacterSounds.GetVoices(catalog, hero, HeroResponseRules.Load(package, hero));
            var slots = CharacterSounds.GetSlots(catalog, hero, soundEventFiles);

            void AddFullRow(Control control)
            {
                var row = soundsTable.RowCount++;
                soundsTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                soundsTable.Controls.Add(control, 0, row);
                soundsTable.SetColumnSpan(control, 2);
            }

            void AddGroup(string text, string toolTipText)
            {
                var label = new Label
                {
                    AutoSize = true,
                    Text = text,
                    Font = groupFont ??= new Font(Font, FontStyle.Bold),
                    Margin = new Padding(3, 8, 3, 3),
                };

                toolTip.SetToolTip(label, toolTipText);
                AddFullRow(label);
            }

            ComboBox AddRow(string text, string toolTipText, IEnumerable<object> choices)
            {
                var label = new Label
                {
                    AutoSize = true,
                    Anchor = AnchorStyles.Left,
                    MaximumSize = new Size(this.AdjustForDPI(170), 0),
                    Text = text,
                    Margin = new Padding(3, 6, 6, 3),
                };

                var comboBox = new ThemedComboBox
                {
                    Anchor = AnchorStyles.Left | AnchorStyles.Right,
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    DropDownWidth = this.AdjustForDPI(360),
                    MaxDropDownItems = 20,
                };

                toolTip.SetToolTip(label, toolTipText);

                foreach (var choice in choices)
                {
                    comboBox.Items.Add(choice);
                }

                var row = soundsTable.RowCount++;
                soundsTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                soundsTable.Controls.Add(label, 0, row);
                soundsTable.Controls.Add(comboBox, 1, row);

                return comboBox;
            }

            if (voices.Count < 2 && slots.Count == 0)
            {
                AddFullRow(new Label
                {
                    AutoSize = true,
                    Text = $"No item comes with other sounds for {hero.DisplayName}.",
                    Margin = new Padding(3, 8, 3, 3),
                });
            }

            if (voices.Count > 1)
            {
                AddGroup("Voice", "Voices the hero's items switch it to, like an arcana's");

                voiceComboBox = AddRow("Voice lines",
                    "The voice's lines are written over the hero's own ones, matched by the situation they are spoken in.\n" +
                    "The counts are how many of the hero's lines the voice has a line for.",
                    voices);

                voiceComboBox.SelectedIndexChanged += VoiceComboBox_SelectedIndexChanged;
            }

            string? group = null;

            foreach (var slot in slots)
            {
                var slotGroup = slot.Shared ? CharacterSounds.SharedGroup : CharacterSounds.HeroGroup;

                if (slotGroup != group)
                {
                    group = slotGroup;

                    AddGroup(group, slot.Shared
                        ? "Sounds every hero plays, like the blink dagger's. Writing over them changes them for every hero,\nso they are only replaced when picked here by hand."
                        : "The hero's own sounds, which the equipped items' sounds are written over");
                }

                var comboBox = AddRow(slot.DisplayName, $"{slot.Event}\n{slot.File}", slot.Choices);
                comboBox.Tag = slot;
                comboBox.SelectedIndexChanged += SoundComboBox_SelectedIndexChanged;

                soundRows.Add((slot, comboBox));
            }

            Themer.ThemeControl(soundsTable);
            soundsTable.ResumeLayout(true);
        }

        /// <summary>
        /// Shows the voice and the sounds the equipped items come with, except where one was picked by hand. Sounds every
        /// hero plays are left as they are unless picked by hand.
        /// </summary>
        private void UpdateSoundChoices(CharacterLoadout loadout)
        {
            updatingSounds = true;

            try
            {
                if (voiceComboBox != null && (!pickedVoice || voiceComboBox.SelectedIndex < 0))
                {
                    var criteria = loadout.VoiceCriteria;

                    voiceComboBox.SelectedItem = voiceComboBox.Items.OfType<VoiceChoice>()
                        .FirstOrDefault(choice => string.Equals(choice.Criteria, criteria, StringComparison.OrdinalIgnoreCase))
                        ?? voiceComboBox.Items[0];
                }

                foreach (var (slot, comboBox) in soundRows)
                {
                    if (!pickedSounds.Contains(slot) || comboBox.SelectedIndex < 0)
                    {
                        comboBox.SelectedItem = slot.Shared ? slot.Default : loadout.GetSound(slot);
                    }
                }
            }
            finally
            {
                updatingSounds = false;
            }
        }

        private void VoiceComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (!updatingSounds)
            {
                pickedVoice = true;
            }
        }

        private void SoundComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (!updatingSounds && sender is ComboBox { Tag: SoundSlot slot })
            {
                pickedSounds.Add(slot);
            }
        }

        private List<SoundReplacement> GetSoundReplacements()
            => [.. soundRows
                .Where(static row => row.ComboBox.SelectedItem is SoundChoice choice && choice != row.Slot.Default)
                .Select(static row => new SoundReplacement(((SoundChoice)row.ComboBox.SelectedItem!).Event, row.Slot.Event))];

        private bool Exists(string path) => package.FindEntry(path) != null;

        /// <summary>
        /// Decodes an image to compare its picture with others, since the compiled files of the same picture differ.
        /// </summary>
        private byte[]? GetPixels(string path)
        {
            try
            {
                if (package.FindEntry(path) is not { } entry)
                {
                    return null;
                }

                using var resource = new Resource { FileName = path };
                resource.Read(GameFileLoader.GetPackageEntryStream(package, entry));

                if (resource.DataBlock is not Texture texture)
                {
                    return null;
                }

                using var bitmap = texture.GenerateBitmap();

                return bitmap.Bytes;
            }
            catch (Exception e)
            {
                Log.Warn(nameof(CharacterSelectForm), $"Failed to read the icon \"{path}\": {e.Message}");

                return null;
            }
        }

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
        /// with the body groups the items switch, and the effects the items play on them when those are shown: ticked
        /// ones on the Effects tab, the ones the game shows for items not listed there, and unusual effects.
        /// </summary>
        private List<PreviewModel> GetPreviewModels()
        {
            if (SelectedHero == null)
            {
                return [];
            }

            var loadout = CreateLoadout();
            var models = new List<(string Path, int Skin, List<string>? Particles)>();
            var tickedEffects = GetItemEffects();
            var showEffects = previewEffectsCheckBox.Checked;

            List<string>? Add(string model, int skin)
            {
                var index = models.FindIndex(existing => CharacterLoadout.IsSamePath(existing.Path, model));

                if (index < 0)
                {
                    models.Add((model, skin, showEffects ? [] : null));
                    index = models.Count - 1;
                }

                return models[index].Particles;
            }

            var heroParticles = loadout.HeroModel != null ? Add(loadout.HeroModel, loadout.HeroSkin) : null;

            foreach (var item in loadout.Items)
            {
                var particles = loadout.GetItemModel(item) is { } model
                    ? Add(model, item.Skin)
                    : loadout.IsWornByHero(item.Item.Slot) ? heroParticles : null;

                particles?.AddRange(loadout.CreatedEffects
                    .Where(effect => effect.Item == item
                        && (tickedEffects.TryGetValue(effect.Particle, out var ticked) ? ticked : loadout.IsShown(effect)))
                    .Select(static effect => effect.Particle));
            }

            foreach (var (item, wearable) in loadout.AdditionalWearables)
            {
                Add(wearable, item.Skin);
            }

            return [.. models.Select(model => new PreviewModel(model.Path, model.Skin, loadout.GetBodyGroupChoices(model.Path), model.Particles))];
        }

        private void LoadHeroColors(string heroName)
        {
            slotRecolors.Clear();
            effectRecolors.Clear();

            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Source2Viewer\HeroColors");
                if (key?.GetValue(heroName) is string data && !string.IsNullOrEmpty(data))
                {
                    var entries = data.Split(';', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var entry in entries)
                    {
                        var parts = entry.Split(':');
                        if (parts.Length == 2 && int.TryParse(parts[1], out var argb))
                        {
                            slotRecolors[parts[0]] = Color.FromArgb(argb);
                        }
                    }
                }

                if (slotRecolors.Count > 0)
                {
                    lastPickedColor = slotRecolors.Values.First();
                }
            }
            catch
            {
            }
        }

        private void SaveHeroColors(string heroName)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Source2Viewer\HeroColors");
                if (slotRecolors.Count > 0)
                {
                    var data = string.Join(";", slotRecolors.Select(kv => $"{kv.Key}:{kv.Value.ToArgb()}"));
                    key?.SetValue(heroName, data, Microsoft.Win32.RegistryValueKind.String);
                }
                else
                {
                    key?.DeleteValue(heroName, false);
                }
            }
            catch
            {
            }
        }

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
