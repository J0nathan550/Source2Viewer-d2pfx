using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using GUI.Utils;

namespace GUI.Types.Exporter.CharacterAssets
{
    /// <summary>
    /// What the character export dialog remembers between runs of the app: its options, the last hero and the loadout
    /// picked for each hero.
    /// </summary>
    sealed class CharacterExportPreferences
    {
        private const string FileName = "character_export.json";

        public string? LastHero { get; set; }

        public bool PreviewEffects { get; set; } = true;

        public CharacterExportOptions? Options { get; set; }

        /// <summary>The loadout last picked for each hero, by hero name.</summary>
        public Dictionary<string, SavedLoadout> Loadouts { get; set; } = [];

        private static string FilePath => Path.Combine(Settings.SettingsFolder, FileName);

        /// <summary>
        /// Reads the saved preferences, or starts over when there are none or they cannot be read.
        /// </summary>
        public static CharacterExportPreferences Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    using var stream = File.OpenRead(FilePath);

                    if (JsonSerializer.Deserialize(stream, CharacterExportPreferencesContext.Default.CharacterExportPreferences) is { } preferences)
                    {
                        preferences.Loadouts ??= [];
                        return preferences;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(nameof(CharacterExportPreferences), $"Failed to read '{FilePath}', starting over: {e.Message}");
            }

            return new CharacterExportPreferences();
        }

        public void Save()
        {
            try
            {
                var tempFile = Path.GetTempFileName();

                using (var stream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, this, CharacterExportPreferencesContext.Default.CharacterExportPreferences);
                }

                File.Move(tempFile, FilePath, overwrite: true);
            }
            catch (Exception e)
            {
                Log.Error(nameof(CharacterExportPreferences), $"Failed to save '{FilePath}': {e.Message}");
            }
        }
    }

    /// <summary>
    /// A hero's loadout as picked in the character export dialog.
    /// </summary>
    sealed class SavedLoadout
    {
        /// <summary>What is equipped in each slot, by slot name, including the slots left empty.</summary>
        public Dictionary<string, SavedSlot> Slots { get; set; } = [];

        /// <summary>The effects ticked or unticked by hand, by particle.</summary>
        public Dictionary<string, bool> Effects { get; set; } = [];

        /// <summary>The icons picked by hand, by <see cref="GetIconKey"/>.</summary>
        public Dictionary<string, string> Icons { get; set; } = [];

        /// <summary>The sounds picked by hand, by the event they are written over.</summary>
        public Dictionary<string, string> Sounds { get; set; } = [];

        /// <summary>Whether the voice was picked by hand.</summary>
        public bool VoicePicked { get; set; }

        /// <summary>The criteria of the voice picked by hand, null for the hero's own.</summary>
        public string? Voice { get; set; }

        public static string GetIconKey(IconSlot slot) => $"{slot.ModifierType}:{slot.Asset}";
    }

    /// <summary>
    /// The item equipped in a slot, see <see cref="EquippedItem"/>.
    /// </summary>
    sealed class SavedSlot
    {
        /// <summary>The item's def index, null when the slot is left empty.</summary>
        public string? Item { get; set; }

        public int Style { get; set; }

        /// <summary>See <see cref="EquippedItem.SkinOverride"/>.</summary>
        public int? Skin { get; set; }

        /// <summary>The id of the item's unusual effect.</summary>
        public int? Unusual { get; set; }
    }

    [JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(CharacterExportPreferences))]
    partial class CharacterExportPreferencesContext : JsonSerializerContext
    {
    }
}
