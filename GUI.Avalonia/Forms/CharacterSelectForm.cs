using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using GUI.Controls;
using GUI.Types.Exporter.CharacterAssets;
using GUI.Types.GLViewers;
using GUI.Utils;
using SkiaSharp;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using DrawingColor = System.Drawing.Color;
using Path = System.IO.Path;
using WinForms = System.Windows.Forms;

namespace GUI.Forms
{
    /// <summary>
    /// Picks a hero, the cosmetic item it wears in each loadout slot and the icons it shows, with a preview of the
    /// result, for <see cref="CharacterAssetsExporter"/>. Same API as the WinForms one so the exporter is shared.
    /// </summary>
    sealed class CharacterSelectForm : IDisposable
    {
        private const string PersonaSelectorSlot = "persona_selector";
        private const string NoUnusualEffect = "(none)";
        private const string RecolorResetHint = "right click to reset";

        // Width of the controls next to the preview, until the divider is dragged
        private const int DefaultControlsWidth = 540;
        private const int MinimumControlsWidth = 440;

        private readonly CharacterExportPreferences preferences = CharacterExportPreferences.Load();
        private readonly ItemsGameCatalog catalog;
        private readonly VrfGuiContext guiContext;
        private readonly Package package;
        private readonly List<(HeroSlot Slot, ComboBox ComboBox, ComboBox StyleComboBox, ComboBox SkinComboBox)> slotRows = [];

        // Slots whose item was picked by hand, which choosing a set leaves alone unless the set has an item for them
        private readonly HashSet<string> pickedSlots = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DrawingColor> slotRecolors = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DrawingColor> effectRecolors = new(StringComparer.OrdinalIgnoreCase);
        private DrawingColor lastPickedColor;
        private readonly Dictionary<string, List<(string Name, string[] Materials)>> materialGroups = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(IconSlot Slot, ComboBox ComboBox, Image Picture)> iconRows = [];
        private readonly HashSet<IconSlot> pickedIcons = [];
        private readonly List<(SoundSlot Slot, ComboBox ComboBox)> soundRows = [];
        private readonly HashSet<SoundSlot> pickedSounds = [];
        private readonly List<(CreatedEffect Effect, CheckBox CheckBox)> effectRows = [];
        private readonly Dictionary<string, bool> pickedEffects = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> loadoutStagedEffects = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<EconItem, UnusualEffect> pickedUnusuals = [];
        private readonly Dictionary<string, Bitmap?> thumbnails = new(StringComparer.OrdinalIgnoreCase);
        private readonly DispatcherTimer previewTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        private Dictionary<string, string>? soundEventFiles;
        private ComboBox? voiceComboBox;
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
        private bool closed;
        private string? derivedGameFolder;
        private WinForms.DialogResult result = WinForms.DialogResult.Cancel;

        private readonly Window window;
        private readonly Grid mainGrid;
        private readonly Border previewPanel;
        private readonly TextBlock previewStatusLabel;
        private readonly TextBlock heroNameLabel;
        private readonly AutoCompleteBox heroSearchTextBox;
        private readonly SearchableComboBox itemSetComboBox;
        private readonly Grid slotsTable;
        private readonly Grid iconsTable;
        private readonly Grid soundsTable;
        private readonly StackPanel effectsTable;
        private readonly CheckBox previewEffectsCheckBox;
        private readonly CheckBox heroModelCheckBox;
        private readonly CheckBox itemModelsCheckBox;
        private readonly CheckBox itemParticlesCheckBox;
        private readonly CheckBox heroParticlesCheckBox;
        private readonly CheckBox itemSoundsCheckBox;
        private readonly CheckBox heroSoundsCheckBox;
        private readonly CheckBox heroVoiceCheckBox;
        private readonly CheckBox iconsCheckBox;
        private readonly CheckBox soundsCheckBox;
        private readonly CheckBox includeAudioCheckBox;
        private readonly CheckBox pedestalCheckBox;
        private readonly CheckBox replaceDefaultsCheckBox;
        private readonly CheckBox replaceSharedParticlesCheckBox;
        private readonly CheckBox materialsCheckBox;
        private readonly CheckBox mergeWearablesCheckBox;
        private readonly CheckBox renameModelsCheckBox;
        private readonly CheckBox animatePartsCheckBox;
        private readonly CheckBox spriteSheetCheckBox;
        private readonly CheckBox itemsGameCheckBox;
        private readonly TextBox contentFolderTextBox;
        private readonly TextBox gameFolderTextBox;
        private readonly TextBlock summaryLabel;

        public HeroDefinition? SelectedHero => heroIndex >= 0 ? catalog.Heroes[heroIndex] : null;

        /// <summary>The addon's content folder, which gets the sources.</summary>
        public string? ContentFolder => contentFolderTextBox.Text?.Trim() is { Length: > 0 } folder ? folder : null;

        /// <summary>The addon's game folder, which gets the compiled icons.</summary>
        public string? GameFolder => gameFolderTextBox.Text?.Trim() is { Length: > 0 } folder ? folder : null;

        public CharacterExportOptions Options => new()
        {
            HeroModel = IsChecked(heroModelCheckBox),
            ItemModels = IsChecked(itemModelsCheckBox),
            Materials = IsChecked(materialsCheckBox) || slotRecolors.Count > 0,
            MergeAdditionalWearables = IsChecked(mergeWearablesCheckBox),
            ItemParticles = IsChecked(itemParticlesCheckBox),
            ItemEffects = GetItemEffects(),
            HeroParticles = IsChecked(heroParticlesCheckBox),
            ItemSounds = IsChecked(itemSoundsCheckBox),
            HeroSounds = IsChecked(heroSoundsCheckBox),
            HeroVoice = IsChecked(heroVoiceCheckBox),
            IncludeAudio = IsChecked(includeAudioCheckBox),
            Icons = IsChecked(iconsCheckBox),
            IconReplacements = GetIconReplacements(),
            SpriteSheet = IsChecked(spriteSheetCheckBox),
            SpriteReplacements = GetSpriteReplacements(),
            DefaultItemsInItemsGame = IsChecked(itemsGameCheckBox),
            DefaultItemSwaps = SelectedHero != null ? ItemsGameEditor.GetSwaps(catalog, CreateLoadout()) : [],
            Sounds = IsChecked(soundsCheckBox),
            SoundReplacements = GetSoundReplacements(),
            Voice = voiceComboBox?.SelectedItem is VoiceChoice { Criteria: not null } voice ? voice : null,
            Pedestal = IsChecked(pedestalCheckBox) && pedestalCheckBox.IsEnabled,
            ReplaceDefaults = IsChecked(replaceDefaultsCheckBox),
            ParticleRecolorOptions = new Dictionary<string, DrawingColor>(effectRecolors, StringComparer.OrdinalIgnoreCase),
            ReplaceSharedParticles = IsChecked(replaceSharedParticlesCheckBox),

            RenameModels = IsChecked(renameModelsCheckBox),
            AnimateOwnParts = IsChecked(animatePartsCheckBox),

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

            lastPickedColor = preferences.LastColor is { } lastColor ? DrawingColor.FromArgb(lastColor) : DrawingColor.Red;

            // Preview
            previewStatusLabel = new TextBlock
            {
                Text = "Loading preview...",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new(16),
            };

            previewPanel = new Border { Child = previewStatusLabel, MinWidth = 200 };

            // Hero navigation
            var previousHeroButton = new Button { Content = "<", FontSize = 18.5, FontWeight = FontWeight.Bold, Margin = new(3) };
            previousHeroButton.Click += (_, _) => SelectHero(heroIndex - 1);

            var nextHeroButton = new Button { Content = ">", FontSize = 18.5, FontWeight = FontWeight.Bold, Margin = new(3) };
            nextHeroButton.Click += (_, _) => SelectHero(heroIndex + 1);

            heroNameLabel = new TextBlock
            {
                FontSize = 21.5,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            var heroNavigationTable = new Grid { ColumnDefinitions = new ColumnDefinitions("48,*,48"), RowDefinitions = new RowDefinitions("*"), Height = 48 };
            AddCell(heroNavigationTable, previousHeroButton, 0, 0);
            AddCell(heroNavigationTable, heroNameLabel, 0, 1);
            AddCell(heroNavigationTable, nextHeroButton, 0, 2);
            previousHeroButton.HorizontalAlignment = HorizontalAlignment.Stretch;
            previousHeroButton.VerticalAlignment = VerticalAlignment.Stretch;
            nextHeroButton.HorizontalAlignment = HorizontalAlignment.Stretch;
            nextHeroButton.VerticalAlignment = VerticalAlignment.Stretch;

            heroSearchTextBox = new AutoCompleteBox
            {
                PlaceholderText = "Search heroes",
                ItemsSource = catalog.Heroes.Select(static hero => hero.DisplayName).ToList(),
                FilterMode = AutoCompleteFilterMode.StartsWith,
                IsTextCompletionEnabled = true,
                Margin = new(3),
            };
            heroSearchTextBox.TextChanged += (_, _) => HeroSearchTextBox_TextChanged();

            var itemSetLabel = new TextBlock { Text = "Item set", Margin = new(3, 6, 3, 0) };

            itemSetComboBox = new SearchableComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new(3),
            };
            itemSetComboBox.SelectionChanged += (_, _) => ItemSetComboBox_SelectedIndexChanged();

            // Loadout tabs
            slotsTable = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,34") };
            iconsTable = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*") };
            soundsTable = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            effectsTable = new StackPanel();

            previewEffectsCheckBox = new CheckBox { Content = "Show effects in the preview", Margin = new(6, 6, 0, 0), IsChecked = true };
            previewEffectsCheckBox.IsCheckedChanged += (_, _) => SchedulePreviewUpdate();

            var effectsPanel = new DockPanel();
            DockPanel.SetDock(previewEffectsCheckBox, Dock.Top);
            effectsPanel.Children.Add(previewEffectsCheckBox);
            effectsPanel.Children.Add(ScrollPage(effectsTable));

            var loadoutTabControl = new TabControl { Margin = new(3) };
            loadoutTabControl.Classes.Add("content");
            loadoutTabControl.Items.Add(new TabItem { Header = "Items", Content = ScrollPage(slotsTable) });
            loadoutTabControl.Items.Add(new TabItem { Header = "Icons", Content = ScrollPage(iconsTable) });
            loadoutTabControl.Items.Add(new TabItem { Header = "Sounds", Content = ScrollPage(soundsTable) });
            loadoutTabControl.Items.Add(new TabItem { Header = "Effects", Content = effectsPanel });

            // Export switches
            heroModelCheckBox = Check("Hero base model", true);
            itemModelsCheckBox = Check("Item models", true);
            pedestalCheckBox = Check("Pedestal", false);
            itemParticlesCheckBox = Check("Item particles", true);
            heroParticlesCheckBox = Check("All hero particles", false);
            iconsCheckBox = Check("Replace icons", true);
            itemSoundsCheckBox = Check("Item sound events", true);
            heroSoundsCheckBox = Check("Hero sound events", true);
            heroVoiceCheckBox = Check("Voice line events", true);
            includeAudioCheckBox = Check("Include the sounds they play (.mp3, .wav)", false);
            soundsCheckBox = Check("Replace sounds", true);
            replaceDefaultsCheckBox = Check("Replace default assets", true);
            replaceSharedParticlesCheckBox = Check("Also shared particles", false);
            materialsCheckBox = Check("Materials and textures", true);
            mergeWearablesCheckBox = Check("Extra meshes", true);
            renameModelsCheckBox = Check("Rename models over the defaults, disabling unused styles", true);
            animatePartsCheckBox = Check("Add items that animate parts of their own to the hero's model", true);
            spriteSheetCheckBox = Check("Replace the minimap icon (writes a copy of mod_textures.txt)", false);
            itemsGameCheckBox = Check("Make the items the hero's default items (writes a copy of items_game.txt)", false);

            replaceDefaultsCheckBox.IsCheckedChanged += (_, _) => UpdateReplaceDefaultsDependents();

            var includeTable = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") };
            AddCell(includeTable, heroModelCheckBox, 0, 0);
            AddCell(includeTable, itemModelsCheckBox, 0, 1);
            AddCell(includeTable, pedestalCheckBox, 0, 2);
            AddCell(includeTable, itemParticlesCheckBox, 1, 0);
            AddCell(includeTable, heroParticlesCheckBox, 1, 1);
            AddCell(includeTable, iconsCheckBox, 1, 2);
            AddCell(includeTable, itemSoundsCheckBox, 2, 0);
            AddCell(includeTable, heroSoundsCheckBox, 2, 1);
            AddCell(includeTable, heroVoiceCheckBox, 2, 2);
            AddCell(includeTable, includeAudioCheckBox, 3, 0, columnSpan: 2);
            AddCell(includeTable, soundsCheckBox, 3, 2);
            AddCell(includeTable, replaceDefaultsCheckBox, 4, 0, columnSpan: 2);
            AddCell(includeTable, replaceSharedParticlesCheckBox, 4, 2);
            AddCell(includeTable, materialsCheckBox, 5, 0, columnSpan: 2);
            AddCell(includeTable, mergeWearablesCheckBox, 5, 2);
            AddCell(includeTable, renameModelsCheckBox, 6, 0, columnSpan: 3);
            AddCell(includeTable, animatePartsCheckBox, 7, 0, columnSpan: 3);
            AddCell(includeTable, spriteSheetCheckBox, 8, 0, columnSpan: 3);
            AddCell(includeTable, itemsGameCheckBox, 9, 0, columnSpan: 3);

            var includeGroupBox = Controls.GroupBox.Create("Export", includeTable);
            includeGroupBox.Margin = new(3);

            // Addon folders
            contentFolderTextBox = new TextBox { PlaceholderText = "dota 2 beta/content/dota_addons/<addon>", Margin = new(3) };
            gameFolderTextBox = new TextBox { PlaceholderText = "dota 2 beta/game/dota_addons/<addon>", Margin = new(3) };

            var contentFolderButton = new Button { Content = "...", Width = 36, Margin = new(3) };
            contentFolderButton.Click += async (_, _) => await PickContentFolderAsync().ConfigureAwait(true);

            var gameFolderButton = new Button { Content = "...", Width = 36, Margin = new(3) };
            gameFolderButton.Click += async (_, _) => await PickGameFolderAsync().ConfigureAwait(true);

            var outputTable = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            AddCell(outputTable, new TextBlock { Text = "Content", Margin = new(3, 0, 6, 0) }, 0, 0);
            AddCell(outputTable, contentFolderTextBox, 0, 1);
            AddCell(outputTable, contentFolderButton, 0, 2);
            AddCell(outputTable, new TextBlock { Text = "Game", Margin = new(3, 0, 6, 0) }, 1, 0);
            AddCell(outputTable, gameFolderTextBox, 1, 1);
            AddCell(outputTable, gameFolderButton, 1, 2);

            var outputGroupBox = Controls.GroupBox.Create("Addon folders", outputTable);
            outputGroupBox.Margin = new(3);

            // Buttons
            summaryLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new(3, 0) };

            var exportButton = new Button { Content = "Export...", Width = 96, Height = 37, Margin = new(3), IsDefault = true };
            exportButton.Click += async (_, _) => await ExportAsync().ConfigureAwait(true);

            var cancelButton = new Button { Content = "Cancel", Width = 96, Height = 37, Margin = new(3), IsCancel = true };
            cancelButton.Click += (_, _) => window.Close();

            var buttonsTable = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
            AddCell(buttonsTable, summaryLabel, 0, 0);
            AddCell(buttonsTable, exportButton, 0, 1);
            AddCell(buttonsTable, cancelButton, 0, 2);

            var controlsTable = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*,Auto,Auto,Auto") };
            AddCell(controlsTable, heroNavigationTable, 0, 0);
            AddCell(controlsTable, heroSearchTextBox, 1, 0);
            AddCell(controlsTable, itemSetLabel, 2, 0);
            AddCell(controlsTable, itemSetComboBox, 3, 0);
            AddCell(controlsTable, loadoutTabControl, 4, 0);
            AddCell(controlsTable, includeGroupBox, 5, 0);
            AddCell(controlsTable, outputGroupBox, 6, 0);
            AddCell(controlsTable, buttonsTable, 7, 0);
            heroNavigationTable.VerticalAlignment = VerticalAlignment.Stretch;
            loadoutTabControl.VerticalAlignment = VerticalAlignment.Stretch;

            // Preview and controls, divided by a splitter with a grip in its middle to show it can be dragged
            var savedWidth = Settings.Config.CharacterExportControlsWidth;

            mainGrid = new Grid
            {
                RowDefinitions = new RowDefinitions("*"),
                ColumnDefinitions =
                {
                    new ColumnDefinition(1, GridUnitType.Star) { MinWidth = 200 },
                    new ColumnDefinition(8, GridUnitType.Pixel),
                    new ColumnDefinition(savedWidth > 0 ? savedWidth : DefaultControlsWidth, GridUnitType.Pixel) { MinWidth = MinimumControlsWidth },
                },
            };

            var grip = new StackPanel { Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

            for (var i = 0; i < 5; i++)
            {
                var dot = new Ellipse { Width = 3, Height = 3 };
                dot.Classes.Add("grip");
                grip.Children.Add(dot);
            }

            var gripLine = new Border { Width = 1, HorizontalAlignment = HorizontalAlignment.Center };
            gripLine.Classes.Add("gripLine");

            var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, Background = Brushes.Transparent };

            AddCell(mainGrid, previewPanel, 0, 0);
            AddCell(mainGrid, gripLine, 0, 1);
            AddCell(mainGrid, grip, 0, 1);
            AddCell(mainGrid, splitter, 0, 1);
            AddCell(mainGrid, controlsTable, 0, 2);
            previewPanel.VerticalAlignment = VerticalAlignment.Stretch;
            gripLine.VerticalAlignment = VerticalAlignment.Stretch;
            splitter.VerticalAlignment = VerticalAlignment.Stretch;
            controlsTable.VerticalAlignment = VerticalAlignment.Stretch;

            window = new Window
            {
                Title = "Choose Character",
                Width = 1224,
                Height = 840,
                MinWidth = 900,
                MinHeight = 700,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Icon = Program.MainForm.Icon,
                Content = new Border { Padding = new(8), Child = mainGrid },
            };

            window.Opened += OnShown;
            window.Activated += (_, _) =>
            {
                // The render loop pauses while none of our windows is active
                RenderLoopThread.SetWindowActive(window, true);
                previewViewer?.GLControl?.Invalidate();
            };
            window.Deactivated += (_, _) => RenderLoopThread.SetWindowActive(window, false);
            window.Closed += OnClosed;

            previewTimer.Tick += (_, _) => PreviewTimer_Tick();

            SetToolTips();

            if (preferences.Options != null)
            {
                ApplyOptions(preferences.Options);
            }

            previewEffectsCheckBox.IsChecked = preferences.PreviewEffects;

            UpdateReplaceDefaultsDependents();

            // The game folder goes first, so one chosen by hand is not replaced by the one that goes with the content folder
            gameFolderTextBox.Text = Settings.Config.CharacterExportGameDir;
            contentFolderTextBox.Text = Settings.Config.CharacterExportContentDir;
            ContentFolderTextBox_TextChanged();
            contentFolderTextBox.TextChanged += (_, _) => ContentFolderTextBox_TextChanged();
            gameFolderTextBox.TextChanged += (_, _) => UpdateFolderToolTips();
            UpdateFolderToolTips();

            var lastHeroIndex = FindHero(hero => hero.Name.Equals(preferences.LastHero, StringComparison.OrdinalIgnoreCase));
            SelectHero(Math.Max(lastHeroIndex, 0));
        }

        private static bool IsChecked(CheckBox checkBox) => checkBox.IsChecked == true;

        private static CheckBox Check(string text, bool isChecked) => new()
        {
            Content = text,
            IsChecked = isChecked,
            Margin = new(3, 1),
        };

        private static void AddCell(Grid grid, Control control, int row, int column, int columnSpan = 1)
        {
            while (grid.RowDefinitions.Count <= row)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            }

            Grid.SetRow(control, row);
            Grid.SetColumn(control, column);

            if (columnSpan > 1)
            {
                Grid.SetColumnSpan(control, columnSpan);
            }

            control.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(control);
        }

        private static ScrollViewer ScrollPage(Control content) => new()
        {
            Content = content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        private void SetToolTips()
        {
            ToolTip.SetTip(heroParticlesCheckBox, "Every particle in the hero's particle folder, which covers the effects of its abilities");
            ToolTip.SetTip(iconsCheckBox,
                "Copy the icons picked on the Icons tab into the addon's game folder, under the names of the hero's own icons.\n" +
                "They are the compiled images the game already has, so they show as they are.");
            ToolTip.SetTip(pedestalCheckBox,
                "The model the hero stands on in the loadout screen, and the particles items only play there.\n" +
                "When replacing default assets, it is written over the hero's own pedestal in the style's skin.\n" +
                "Only available when an equipped item comes with one.");
            ToolTip.SetShowOnDisabled(pedestalCheckBox, true);
            ToolTip.SetTip(heroSoundsCheckBox, "The hero's game_sounds file, which points at the sounds in the game");
            ToolTip.SetTip(heroVoiceCheckBox, "The hero's game_sounds_vo file, which points at the voice lines in the game");
            ToolTip.SetTip(itemSoundsCheckBox, "The files the sound events the items swap in are defined in");
            ToolTip.SetTip(includeAudioCheckBox, "Also export every sound the exported sound events play, which is most of the export's size");
            ToolTip.SetTip(soundsCheckBox,
                "Write the sounds and voice picked on the Sounds tab over the hero's own, in the sound event files that define them,\n" +
                "so they play without the items being equipped");
            ToolTip.SetTip(replaceDefaultsCheckBox,
                "Write the chosen look over the hero's default assets, so it shows without the items being equipped:\n" +
                "the arcana or persona model as the hero's model, chosen items over the default items' models,\n" +
                "a persona's items over the hero's own default items, hiding the ones it has nothing in place of,\n" +
                "particles the items swap in over the ones they replace, and particles items create added to their models");
            ToolTip.SetTip(materialsCheckBox,
                "Decompile the materials the exported models use, with their textures, so the addon compiles its own copies.\n" +
                "Some item materials, e.g. of arcanas, render semi-transparent in game when the addon uses the game's own.");
            ToolTip.SetTip(mergeWearablesCheckBox, "When replacing default assets, add the meshes of the extra models items wear, e.g. an arcana's frost overlay,\n" +
                "to the hero's model. They are exported as models of their own either way.");
            ToolTip.SetTip(replaceSharedParticlesCheckBox, "Also replace particles every hero uses, like the blink dagger, stun and status effects");
            ToolTip.SetTip(renameModelsCheckBox,
                "When replacing default assets, move each item's exported model over the default model it replaces,\n" +
                "like renaming drow_arcana_weapon to drow_weapon. Body group choices of styles not picked are disabled, not removed,\n" +
                "so they can be turned back on in ModelDoc, and the _dummy choices are removed. The style's skin becomes the default one.\n" +
                "No extra meshes are merged. Particles items create and activity modifiers are still added.");
            ToolTip.SetTip(animatePartsCheckBox,
                "When replacing default assets, add items that animate parts of their own, e.g. a wind-up key, to the hero's model,\n" +
                "with their animations played on those parts during the hero's, and hide the default model of their slot.\n" +
                "The game combines an addon hero's items into it, where such parts would otherwise stand still.\n" +
                "Untick it for models this does not suit, they are then written over the default models like other items.");
            ToolTip.SetTip(spriteSheetCheckBox,
                "Point the hero's minimap icon at the one picked on the Icons tab, in a copy of scripts/mod_textures.txt in the game folder.\n" +
                "The minimap draws hero icons from this sprite sheet, not from the icon images.\n" +
                "The copy replaces the whole file, so other mods that change it stop working unless they are in the same folder.\n" +
                "A copy an earlier export wrote there is updated, so several heroes can share it.");
            ToolTip.SetTip(itemsGameCheckBox,
                "Write the equipped items over the hero's default items in a copy of scripts/items/items_game.txt in the game folder,\n" +
                "so the game shows them as the hero's default look, with the picked style. No model has to be replaced for this.\n" +
                "Slots left at their default item are restored. Items in slots without a default item, and unusual effects, are left out.\n" +
                "The copy replaces the whole file, so other mods that change it stop working unless they are in the same folder.\n" +
                "A copy an earlier export wrote there is updated, so several heroes can share it.");
            ToolTip.SetTip(previewEffectsCheckBox,
                "Play the ticked effects and the unusual effects on the preview. Only roughly how the game shows them,\n" +
                "not everything particles do is supported.");
        }

        public async Task<WinForms.DialogResult> ShowDialogAsync()
        {
            if (AppMessageDialogs.GetOwner() is not { } owner)
            {
                return WinForms.DialogResult.Cancel;
            }

            await window.ShowDialog(owner).ConfigureAwait(true);
            return result;
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
            if (pickedEffects.Count > 0 || pickedIcons.Count > 0 || pickedSounds.Count > 0 || pickedVoice || pickedUnusuals.Count > 0
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
            heroModelCheckBox.IsChecked = options.HeroModel;
            itemModelsCheckBox.IsChecked = options.ItemModels;
            materialsCheckBox.IsChecked = options.Materials;
            mergeWearablesCheckBox.IsChecked = options.MergeAdditionalWearables;
            itemParticlesCheckBox.IsChecked = options.ItemParticles;
            heroParticlesCheckBox.IsChecked = options.HeroParticles;
            itemSoundsCheckBox.IsChecked = options.ItemSounds;
            heroSoundsCheckBox.IsChecked = options.HeroSounds;
            heroVoiceCheckBox.IsChecked = options.HeroVoice;
            includeAudioCheckBox.IsChecked = options.IncludeAudio;
            iconsCheckBox.IsChecked = options.Icons;
            soundsCheckBox.IsChecked = options.Sounds;
            pedestalCheckBox.IsChecked = options.Pedestal;
            replaceDefaultsCheckBox.IsChecked = options.ReplaceDefaults;
            replaceSharedParticlesCheckBox.IsChecked = options.ReplaceSharedParticles;
            renameModelsCheckBox.IsChecked = options.RenameModels;
            animatePartsCheckBox.IsChecked = options.AnimateOwnParts;
            spriteSheetCheckBox.IsChecked = options.SpriteSheet;
            itemsGameCheckBox.IsChecked = options.DefaultItemsInItemsGame;
        }

        private void UpdateReplaceDefaultsDependents()
        {
            var enabled = IsChecked(replaceDefaultsCheckBox);
            replaceSharedParticlesCheckBox.IsEnabled = enabled;
            renameModelsCheckBox.IsEnabled = enabled;
            animatePartsCheckBox.IsEnabled = enabled;
        }

        private void OnShown(object? sender, EventArgs e)
        {
            ShowPathEnd(contentFolderTextBox);
            ShowPathEnd(gameFolderTextBox);

            _ = LoadPreviewAsync();
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            closed = true;

            previewTimer.Stop();
            RenderLoopThread.SetWindowActive(window, false);

            previewViewer?.Dispose();
            previewViewer = null;

            if (SelectedHero != null)
            {
                preferences.LastHero = SelectedHero.Name;
                SaveLoadout();
            }

            preferences.Options = Options;
            preferences.PreviewEffects = IsChecked(previewEffectsCheckBox);
            preferences.LastColor = lastPickedColor.ToArgb();
            preferences.Save();

            var controlsWidth = (int)Math.Round(mainGrid.ColumnDefinitions[2].ActualWidth);

            if (controlsWidth > 0 && controlsWidth != Settings.Config.CharacterExportControlsWidth)
            {
                Settings.Config.CharacterExportControlsWidth = controlsWidth;
                Settings.Save();
            }

            foreach (var (_, _, picture) in iconRows)
            {
                picture.Source = null;
            }

            foreach (var thumbnail in thumbnails.Values)
            {
                thumbnail?.Dispose();
            }

            thumbnails.Clear();

            // The viewer of the tab under the dialog draws again
            Program.MainForm.InvalidateVisibleViewer();
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

                if (closed)
                {
                    viewer.Dispose();
                    return;
                }

                previewPanel.Child = viewer.InitializeUiControls(isPreview: true);

                previewViewer = viewer;
                viewer = null;

                // The selection may have changed while the preview was loading
                SchedulePreviewUpdate();
            }
            catch (Exception ex)
            {
                viewer?.Dispose();

                Log.Error(nameof(CharacterSelectForm), $"Failed to load the character preview: {ex}");

                if (!closed)
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

        private static string FormatColorToolTip(DrawingColor? color, string target)
            => color is { } c
                ? $"{target}: R:{c.R} G:{c.G} B:{c.B} ({RecolorResetHint})"
                : $"{target}: pick a custom RGB color ({RecolorResetHint})";

        /// <summary>
        /// A small button that picks a recolor, showing the picked color, and forgets it on a right click.
        /// </summary>
        private Button CreateColorButton(DrawingColor? color, string target, double size, Func<DrawingColor> initialColor,
            Action<DrawingColor> picked, Action reset)
        {
            var button = new Button
            {
                Content = AppIcons.Create("ColorEyeDropper", size * 0.6),
                Width = size + 6,
                Height = size,
                Padding = new(0),
                Margin = new(2),
                Cursor = new Cursor(StandardCursorType.Hand),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };

            void Show(DrawingColor? shown)
            {
                if (shown is { } c)
                {
                    button.Background = Themer.GetBrush(c);
                }
                else
                {
                    button.ClearValue(TemplatedControl.BackgroundProperty);
                }

                ToolTip.SetTip(button, FormatColorToolTip(shown, target));
            }

            Show(color);

            button.Click += async (_, _) =>
            {
                if (await ColorPickerDialog.ShowAsync(initialColor(), target).ConfigureAwait(true) is { } pickedColor)
                {
                    picked(pickedColor);
                    Show(pickedColor);
                }
            };

            button.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton == MouseButton.Right)
                {
                    reset();
                    Show(null);
                    e.Handled = true;
                }
            };

            return button;
        }

        private void BuildSlotRows(HeroDefinition hero)
        {
            slotsTable.Children.Clear();
            slotsTable.RowDefinitions.Clear();
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
                var label = new TextBlock
                {
                    MaxWidth = 150,
                    TextWrapping = TextWrapping.Wrap,
                    Text = duplicateSlotTexts.Contains(text) ? $"{text} ({slot.Name})" : text,
                    Margin = new(3, 6, 6, 3),
                };

                ToolTip.SetTip(label, slot.Name);

                var comboBox = new SearchableComboBox
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    DropDownWidth = 360,
                    MaxDropDownItems = 20,
                    Tag = slot,
                    Margin = new(3),
                };

                var colorButton = CreateColorButton(
                    slotRecolors.TryGetValue(slot.Name, out var savedColor) ? savedColor : null,
                    "Recolor",
                    26,
                    () => slotRecolors.TryGetValue(slot.Name, out var currentColor) ? currentColor : lastPickedColor,
                    color =>
                    {
                        slotRecolors[slot.Name] = color;
                        lastPickedColor = color;
                    },
                    () => slotRecolors.Remove(slot.Name));

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
                comboBox.SelectionChanged += SlotComboBox_SelectedIndexChanged;

                var styleComboBox = new ComboBox
                {
                    Width = 110,
                    IsVisible = false,
                    Margin = new(3),
                    ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<object>(
                        static (style, _) => new TextBlock { Text = (style as ItemStyle)?.Name ?? style?.ToString() }, supportsRecycling: false),
                };

                styleComboBox.SelectionChanged += StyleComboBox_SelectedIndexChanged;
                ToolTip.SetTip(styleComboBox, "Item style");

                var skinComboBox = new ComboBox
                {
                    Width = 100,
                    IsVisible = false,
                    Margin = new(3),
                };

                skinComboBox.SelectionChanged += SkinComboBox_SelectedIndexChanged;
                ToolTip.SetTip(skinComboBox, "The material the item's model is shown with, which versions of the same item often differ in");

                var row = slotsTable.RowDefinitions.Count;
                AddCell(slotsTable, label, row, 0);
                AddCell(slotsTable, comboBox, row, 1);
                AddCell(slotsTable, styleComboBox, row, 2);
                AddCell(slotsTable, skinComboBox, row, 3);
                AddCell(slotsTable, colorButton, row, 4);

                slotRows.Add((slot, comboBox, styleComboBox, skinComboBox));
            }
        }

        private void BuildItemSets(HeroDefinition hero)
        {
            itemSetComboBox.Items.Clear();
            itemSetComboBox.Items.Add("Default items");

            foreach (var set in catalog.GetSets(hero))
            {
                itemSetComboBox.Items.Add(set);
            }

            itemSetComboBox.SelectedIndex = 0;
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
            for (var i = 0; i < comboBox.ItemCount; i++)
            {
                if (comboBox.Items[i] is ItemChoice { Item: { } item } && predicate(item))
                {
                    comboBox.SelectedIndex = i;
                    return;
                }
            }

            comboBox.SelectedIndex = 0;
        }

        private void ItemSetComboBox_SelectedIndexChanged()
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

        private void SlotComboBox_SelectedIndexChanged(object? sender, SelectionChangedEventArgs e)
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
                styleComboBox.Items.Clear();

                foreach (var style in styles)
                {
                    styleComboBox.Items.Add(style);
                }

                styleComboBox.SelectedIndex = styles.Count > 0 ? 0 : -1;
                styleComboBox.IsVisible = styles.Count > 1;
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
                skinComboBox.Items.Clear();

                if (item != null && GetShownModel(item, (styleComboBox.SelectedItem as ItemStyle)?.Index ?? 0) is { } model)
                {
                    foreach (var skin in GetSkinChoices(model))
                    {
                        skinComboBox.Items.Add(skin);
                    }
                }

                skinComboBox.IsVisible = skinComboBox.ItemCount > 1;
            }
            finally
            {
                updatingSkins = false;
            }

            SelectDefaultSkin(comboBox, styleComboBox, skinComboBox);
        }

        private void StyleComboBox_SelectedIndexChanged(object? sender, SelectionChangedEventArgs e)
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

        private void SkinComboBox_SelectedIndexChanged(object? sender, SelectionChangedEventArgs e)
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
            if (GetItem(comboBox) is not { } item || skinComboBox.ItemCount == 0)
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

        private void HeroSearchTextBox_TextChanged()
        {
            var text = heroSearchTextBox.Text?.Trim() ?? string.Empty;

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

        private async Task ExportAsync()
        {
            if (SelectedHero == null)
            {
                return;
            }

            if (ContentFolder == null || !Path.IsPathFullyQualified(ContentFolder))
            {
                await PickContentFolderAsync().ConfigureAwait(true);

                if (ContentFolder == null)
                {
                    return;
                }
            }

            if (GameFolder == null && ((IsChecked(iconsCheckBox) && GetIconReplacements().Count > 0)
                || (IsChecked(spriteSheetCheckBox) && GetSpriteReplacements().Count > 0)
                || IsChecked(itemsGameCheckBox)))
            {
                await PickGameFolderAsync().ConfigureAwait(true);

                if (GameFolder == null)
                {
                    return;
                }
            }

            Settings.Config.CharacterExportContentDir = ContentFolder;
            Settings.Config.CharacterExportGameDir = GameFolder ?? string.Empty;
            Settings.Save();

            result = WinForms.DialogResult.OK;
            window.Close();
        }

        private async Task PickContentFolderAsync()
        {
            if (await AppFileDialogs.PickFolderAsync("Choose the addon's content folder, e.g. content/dota_addons/<addon>", AppFileDialogs.RememberIn.SaveDirectory).ConfigureAwait(true) is { } folder)
            {
                SetFolder(contentFolderTextBox, folder);
            }
        }

        private async Task PickGameFolderAsync()
        {
            if (await AppFileDialogs.PickFolderAsync("Choose the addon's game folder, e.g. game/dota_addons/<addon>", AppFileDialogs.RememberIn.SaveDirectory).ConfigureAwait(true) is { } folder)
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
            textBox.CaretIndex = textBox.Text?.Length ?? 0;
        }

        /// <summary>
        /// Shows the whole path in the tool tips, the boxes are too narrow for most.
        /// </summary>
        private void UpdateFolderToolTips()
        {
            ToolTip.SetTip(contentFolderTextBox, "The addon's content folder, which gets the models, particles and sound events as sources." +
                (ContentFolder is { } content ? $"\n{content}" : string.Empty));
            ToolTip.SetTip(gameFolderTextBox, "The addon's game folder, which gets the compiled icons. Filled in from the content folder when it can be." +
                (GameFolder is { } game ? $"\n{game}" : string.Empty));
        }

        /// <summary>
        /// Fills in the game folder that goes with the content folder, unless another one was chosen by hand.
        /// </summary>
        private void ContentFolderTextBox_TextChanged()
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

            pedestalCheckBox.IsEnabled = loadout.Pedestals.Any();
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
            effectsTable.Children.Clear();
            effectRows.Clear();

            var effects = loadout.CreatedEffects.Where(static effect => !effect.Item.Item.IsDefault && !effect.IsUnusual).ToList();
            var items = loadout.Items
                .Where(item => !item.Item.IsDefault && (catalog.GetUnusualEffects(item.Item).Count > 0 || effects.Any(effect => effect.Item == item)))
                .ToList();

            if (items.Count == 0)
            {
                effectsTable.Children.Add(new TextBlock
                {
                    Text = "No equipped item creates effects.",
                    Margin = new(3, 8, 3, 3),
                });
            }

            updatingEffects = true;

            try
            {
                foreach (var item in items)
                {
                    var currentItemEffects = effects.Where(effect => effect.Item == item).ToList();

                    var titleLabel = new TextBlock
                    {
                        Text = item.StyleName is { } styleName ? $"{item.Item.Name} ({styleName})" : item.Item.Name,
                        FontWeight = FontWeight.Bold,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new(0, 0, 6, 0),
                    };

                    // Recolors every effect of the item at once, like a prismatic gem
                    var itemColorButton = CreateColorButton(
                        effectRecolors.TryGetValue(item.Item.Name, out var savedItemColor) ? savedItemColor : null,
                        "Prismatic color of all of this item's effects",
                        24,
                        () => effectRecolors.TryGetValue(item.Item.Name, out var currentColor) ? currentColor : lastPickedColor,
                        color =>
                        {
                            effectRecolors[item.Item.Name] = color;

                            foreach (var effect in currentItemEffects)
                            {
                                effectRecolors[effect.Particle] = color;
                            }

                            SchedulePreviewUpdate();
                        },
                        () =>
                        {
                            effectRecolors.Remove(item.Item.Name);

                            foreach (var effect in currentItemEffects)
                            {
                                effectRecolors.Remove(effect.Particle);
                            }

                            SchedulePreviewUpdate();
                        });

                    effectsTable.Children.Add(new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Margin = new(3, 8, 3, 3),
                        Children = { titleLabel, itemColorButton },
                    });

                    if (catalog.GetUnusualEffects(item.Item) is { Count: > 0 } unusualEffects)
                    {
                        effectsTable.Children.Add(CreateUnusualRow(item, unusualEffects));
                    }

                    foreach (var effect in currentItemEffects)
                    {
                        effectsTable.Children.Add(CreateEffectRow(loadout, effect));
                    }
                }
            }
            finally
            {
                updatingEffects = false;
            }
        }

        /// <summary>
        /// A row of one effect: whether it is exported, and its own prismatic color.
        /// </summary>
        private StackPanel CreateEffectRow(CharacterLoadout loadout, CreatedEffect effect)
        {
            var checkBox = CreateEffectCheckBox(loadout, effect);

            var colorButton = CreateColorButton(
                effectRecolors.TryGetValue(effect.Particle, out var savedColor) ? savedColor : null,
                "Prismatic color of this effect",
                20,
                () => effectRecolors.TryGetValue(effect.Particle, out var currentColor) ? currentColor : lastPickedColor,
                color =>
                {
                    effectRecolors[effect.Particle] = color;
                    SchedulePreviewUpdate();
                },
                () =>
                {
                    effectRecolors.Remove(effect.Particle);
                    SchedulePreviewUpdate();
                });

            colorButton.Margin = new(4, 1, 0, 0);

            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new(12, 1, 3, 1),
                Children = { checkBox, colorButton },
            };
        }

        /// <summary>
        /// A choice of the unusual effect the item plays, none by default like the item's plain version.
        /// </summary>
        private StackPanel CreateUnusualRow(EquippedItem item, IReadOnlyList<UnusualEffect> unusualEffects)
        {
            var label = new TextBlock
            {
                Text = "Unusual effect",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new(0, 0, 6, 0),
            };

            var comboBox = new ComboBox
            {
                Width = 180,
                Tag = item.Item,
            };

            comboBox.Items.Add(NoUnusualEffect);

            foreach (var effect in unusualEffects)
            {
                comboBox.Items.Add(effect);
            }

            comboBox.SelectedItem = item.Unusual ?? (object)NoUnusualEffect;
            comboBox.SelectionChanged += UnusualComboBox_SelectedIndexChanged;

            SetUnusualToolTip(comboBox);

            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new(12, 2, 3, 2),
                Children = { label, comboBox },
            };
        }

        private void UnusualComboBox_SelectedIndexChanged(object? sender, SelectionChangedEventArgs e)
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

        private static void SetUnusualToolTip(ComboBox comboBox)
            => ToolTip.SetTip(comboBox, "The effect the item's unusual version plays on it, which is exported like the item's other effects" +
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
                Content = Path.GetFileNameWithoutExtension(effect.Particle) + required,
                IsChecked = pickedEffects.TryGetValue(effect.Particle, out var picked) ? picked : shown,
                Margin = new(12, 2, 3, 2),
                Tag = effect,
            };

            ToolTip.SetTip(checkBox, shown
                ? effect.Particle
                : stagedForLoadout
                    ? $"{effect.Particle}\nIt is set up for the loadout screen at the world origin and left out by default. Ticked, it is exported to follow the model"
                    : $"{effect.Particle}\nThe game does not show it with the equipped items, it is made for another arcana level");

            checkBox.IsCheckedChanged += EffectCheckBox_CheckedChanged;
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

        private void EffectCheckBox_CheckedChanged(object? sender, RoutedEventArgs e)
        {
            if (!updatingEffects && sender is CheckBox { Tag: CreatedEffect effect } checkBox)
            {
                pickedEffects[effect.Particle] = IsChecked(checkBox);
                SchedulePreviewUpdate();
            }
        }

        private Dictionary<string, bool> GetItemEffects()
        {
            var effects = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            // An effect two items create is exported when either of them has it ticked
            foreach (var (effect, checkBox) in effectRows)
            {
                effects[effect.Particle] = IsChecked(checkBox) || effects.GetValueOrDefault(effect.Particle);
            }

            return effects;
        }

        /// <summary>
        /// Lists the hero's icons that items come with other versions of, grouped by kind.
        /// </summary>
        private void BuildIconRows(HeroDefinition hero)
        {
            iconsTable.Children.Clear();
            iconsTable.RowDefinitions.Clear();
            iconRows.Clear();
            pickedIcons.Clear();

            var slots = CharacterIcons.GetSlots(catalog, hero, Exists, GetPixels);
            string? group = null;

            void AddFullRow(Control control) => AddCell(iconsTable, control, iconsTable.RowDefinitions.Count, 0, columnSpan: 3);

            if (slots.Count == 0)
            {
                AddFullRow(new TextBlock
                {
                    Text = $"No item comes with other icons for {hero.DisplayName}.",
                    Margin = new(3, 8, 3, 3),
                });
            }

            foreach (var slot in slots)
            {
                if (slot.Group != group)
                {
                    group = slot.Group;

                    AddFullRow(new TextBlock
                    {
                        Text = group,
                        FontWeight = FontWeight.Bold,
                        Margin = new(3, 8, 3, 3),
                    });
                }

                var picture = new Image
                {
                    Width = 64,
                    Height = 48,
                    Stretch = Stretch.Uniform,
                    Margin = new(3, 2, 6, 2),
                };

                var label = new TextBlock
                {
                    MaxWidth = 130,
                    TextWrapping = TextWrapping.Wrap,
                    Text = slot.DisplayName,
                    Margin = new(3, 6, 6, 3),
                };

                var comboBox = new ComboBox
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Tag = slot,
                    Margin = new(3),
                };

                ToolTip.SetTip(label, slot.SharesSwapsOf is { } shared
                    ? $"{slot.Asset}\nShows the same picture as {shared}, so it gets the icons items give that ability"
                    : slot.Asset);

                foreach (var choice in slot.Choices)
                {
                    comboBox.Items.Add(choice);
                }

                comboBox.SelectionChanged += IconComboBox_SelectedIndexChanged;

                var row = iconsTable.RowDefinitions.Count;
                AddCell(iconsTable, picture, row, 0);
                AddCell(iconsTable, label, row, 1);
                AddCell(iconsTable, comboBox, row, 2);

                iconRows.Add((slot, comboBox, picture));
            }
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

        private void IconComboBox_SelectedIndexChanged(object? sender, SelectionChangedEventArgs e)
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
                picture.Source = GetThumbnail(CharacterIcons.GetImagePath(slot.Folders[0], choice.Icon), (int)picture.Width, (int)picture.Height);
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
            soundsTable.Children.Clear();
            soundsTable.RowDefinitions.Clear();
            soundRows.Clear();
            pickedSounds.Clear();
            voiceComboBox = null;
            pickedVoice = false;

            soundEventFiles ??= CharacterSounds.GetEventFiles(package);

            var voices = CharacterSounds.GetVoices(catalog, hero, HeroResponseRules.Load(package, hero));
            var slots = CharacterSounds.GetSlots(catalog, hero, soundEventFiles);

            void AddFullRow(Control control) => AddCell(soundsTable, control, soundsTable.RowDefinitions.Count, 0, columnSpan: 2);

            void AddGroup(string text, string toolTipText)
            {
                var label = new TextBlock
                {
                    Text = text,
                    FontWeight = FontWeight.Bold,
                    Margin = new(3, 8, 3, 3),
                };

                ToolTip.SetTip(label, toolTipText);
                AddFullRow(label);
            }

            ComboBox AddRow(string text, string toolTipText, IEnumerable<object> choices)
            {
                var label = new TextBlock
                {
                    MaxWidth = 170,
                    TextWrapping = TextWrapping.Wrap,
                    Text = text,
                    Margin = new(3, 6, 6, 3),
                };

                var comboBox = new ComboBox
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new(3),
                };

                ToolTip.SetTip(label, toolTipText);

                foreach (var choice in choices)
                {
                    comboBox.Items.Add(choice);
                }

                var row = soundsTable.RowDefinitions.Count;
                AddCell(soundsTable, label, row, 0);
                AddCell(soundsTable, comboBox, row, 1);

                return comboBox;
            }

            if (voices.Count < 2 && slots.Count == 0)
            {
                AddFullRow(new TextBlock
                {
                    Text = $"No item comes with other sounds for {hero.DisplayName}.",
                    Margin = new(3, 8, 3, 3),
                });
            }

            if (voices.Count > 1)
            {
                AddGroup("Voice", "Voices the hero's items switch it to, like an arcana's");

                voiceComboBox = AddRow("Voice lines",
                    "The voice's lines are written over the hero's own ones, matched by the situation they are spoken in.\n" +
                    "The counts are how many of the hero's lines the voice has a line for.",
                    voices);

                voiceComboBox.SelectionChanged += (_, _) =>
                {
                    if (!updatingSounds)
                    {
                        pickedVoice = true;
                    }
                };
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
                comboBox.SelectionChanged += SoundComboBox_SelectedIndexChanged;

                soundRows.Add((slot, comboBox));
            }
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

        private void SoundComboBox_SelectedIndexChanged(object? sender, SelectionChangedEventArgs e)
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
        private Bitmap? GetThumbnail(string path, int width, int height)
        {
            if (thumbnails.TryGetValue(path, out var cached))
            {
                return cached;
            }

            Bitmap? thumbnail = null;

            try
            {
                if (package.FindEntry(path) is { } entry)
                {
                    using var resource = new Resource { FileName = path };
                    resource.Read(GameFileLoader.GetPackageEntryStream(package, entry));

                    if (resource.DataBlock is Texture texture)
                    {
                        using var bitmap = texture.GenerateBitmap();

                        // Twice the box size, so the thumbnail stays sharp on high DPI displays
                        var scale = Math.Min(1f, Math.Min(width * 2f / bitmap.Width, height * 2f / bitmap.Height));
                        var info = new SKImageInfo(Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale)));

                        using var resized = bitmap.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                        using var image = SKImage.FromBitmap(resized ?? bitmap);
                        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                        using var pngStream = png.AsStream();
                        thumbnail = new Bitmap(pngStream);
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

        private void PreviewTimer_Tick()
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
            var showEffects = IsChecked(previewEffectsCheckBox);

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

            if (preferences.HeroColors.GetValueOrDefault(heroName) is not { } colors)
            {
                return;
            }

            foreach (var (slot, argb) in colors)
            {
                slotRecolors[slot] = DrawingColor.FromArgb(argb);
            }

            if (slotRecolors.Count > 0)
            {
                lastPickedColor = slotRecolors.Values.First();
            }
        }

        private void SaveHeroColors(string heroName)
        {
            if (slotRecolors.Count > 0)
            {
                preferences.HeroColors[heroName] = slotRecolors.ToDictionary(static pair => pair.Key, static pair => pair.Value.ToArgb(), StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                preferences.HeroColors.Remove(heroName);
            }
        }

        public void Dispose()
        {
            previewTimer.Stop();
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
