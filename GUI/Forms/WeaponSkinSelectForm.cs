using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.Exporter.CharacterAssets;
using GUI.Types.Exporter.WeaponSkins;
using GUI.Utils;
using SkiaSharp;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Forms
{
    /// <summary>
    /// Picks a weapon, the finish to paint it with and how worn, with the game's own image of the result, for
    /// <see cref="WeaponSkinExporter"/>.
    /// </summary>
    sealed class WeaponSkinSelectForm : ThemedForm
    {
        private const int PreviewSize = 384;

        // Where the game's wear names start, from factory new to battle-scarred
        private static readonly (float Start, string Name)[] WearNames =
        [
            (0f, "Factory New"),
            (0.07f, "Minimal Wear"),
            (0.15f, "Field-Tested"),
            (0.38f, "Well-Worn"),
            (0.45f, "Battle-Scarred"),
        ];

        private readonly WeaponSkinCatalog catalog;
        private readonly Package package;
        private readonly Dictionary<string, Image?> previews = new(StringComparer.OrdinalIgnoreCase);
        private readonly ThemedComboBox weaponComboBox;
        private readonly ThemedTextBox searchTextBox;
        private readonly ListBox paintKitListBox;
        private readonly ThemedFloatNumeric wearNumeric;
        private readonly Label wearNameLabel;
        private readonly Label detailsLabel;
        private readonly CheckBox iconCheckBox;
        private readonly ThemedTextBox contentFolderTextBox;
        private readonly ThemedTextBox gameFolderTextBox;
        private readonly PictureBox previewPictureBox;
        private readonly ToolTip toolTip = new();
        private string? derivedGameFolder;

        public string? ContentFolder => contentFolderTextBox.Text.Trim() is { Length: > 0 } folder ? folder : null;

        public string? GameFolder => gameFolderTextBox.Text.Trim() is { Length: > 0 } folder ? folder : null;

        private WeaponDefinition? SelectedWeapon => weaponComboBox.SelectedItem is WeaponItem item ? item.Weapon : null;

        private PaintKit? SelectedPaintKit => paintKitListBox.SelectedItem is PaintKitItem item ? item.PaintKit : null;

        public WeaponSkinSelectForm(WeaponSkinCatalog catalog, Package package)
        {
            this.catalog = catalog;
            this.package = package;

            Text = "Export CS2 weapon skin";
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7F, 17F);
            Font = new Font("Segoe UI", 10F);
            ClientSize = new Size(900, 620);
            MinimumSize = new Size(760, 520);

            weaponComboBox = new ThemedComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 4, 0, 4),
            };
            weaponComboBox.Items.AddRange([.. catalog.Weapons.Select(static weapon => new WeaponItem(weapon))]);

            searchTextBox = new ThemedTextBox
            {
                Dock = DockStyle.Fill,
                PlaceholderText = "Search finishes",
                Margin = new Padding(0, 4, 0, 4),
            };

            paintKitListBox = new ListBox
            {
                Dock = DockStyle.Fill,
                IntegralHeight = false,
                Margin = new Padding(0, 4, 0, 4),
            };

            wearNumeric = new ThemedFloatNumeric
            {
                MinValue = 0f,
                MaxValue = 1f,
                DecimalMax = 3,
                Width = 80,
                Margin = new Padding(0, 4, 8, 4),
            };

            wearNameLabel = MakeLabel(string.Empty);
            detailsLabel = MakeLabel(string.Empty);
            detailsLabel.Margin = new Padding(0, 6, 0, 6);

            iconCheckBox = new CheckBox
            {
                Text = "Replace the weapon's inventory icon with this finish's",
                AutoSize = true,
                Checked = true,
                Margin = new Padding(0, 6, 0, 6),
            };

            contentFolderTextBox = new ThemedTextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 4, 4) };
            gameFolderTextBox = new ThemedTextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 4, 4) };

            previewPictureBox = new PictureBox
            {
                Size = new Size(PreviewSize, PreviewSize),
                SizeMode = PictureBoxSizeMode.Zoom,
                Margin = new Padding(12, 4, 0, 4),
                Anchor = AnchorStyles.Top,
            };

            var wearRow = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = Padding.Empty,
            };
            wearRow.Controls.Add(wearNumeric);
            wearRow.Controls.Add(wearNameLabel);

            var exportButton = MakeButton("Export");
            var cancelButton = MakeButton("Cancel");
            cancelButton.DialogResult = DialogResult.Cancel;

            var buttonRow = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 8, 0, 0),
            };
            buttonRow.Controls.Add(cancelButton);
            buttonRow.Controls.Add(exportButton);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                Padding = new Padding(14),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            void AddRow(string? label, Control control, SizeType sizeType = SizeType.AutoSize, Control? button = null)
            {
                var row = layout.RowCount++;
                layout.RowStyles.Add(sizeType == SizeType.Percent ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));

                if (label != null)
                {
                    layout.Controls.Add(MakeLabel(label), 0, row);
                }

                layout.Controls.Add(control, 1, row);
                layout.SetColumnSpan(control, button == null ? 2 : 1);

                if (button != null)
                {
                    layout.Controls.Add(button, 2, row);
                }
            }

            AddRow("Weapon:", weaponComboBox);
            AddRow("Finish:", searchTextBox);
            AddRow(null, paintKitListBox, SizeType.Percent);
            AddRow("Wear:", wearRow);
            AddRow(null, detailsLabel);
            AddRow(null, iconCheckBox);
            AddRow("Content folder:", contentFolderTextBox, button: MakeBrowseButton(PickContentFolder));
            AddRow("Game folder:", gameFolderTextBox, button: MakeBrowseButton(PickGameFolder));
            AddRow(null, buttonRow);

            layout.Controls.Add(previewPictureBox, 3, 0);
            layout.SetRowSpan(previewPictureBox, layout.RowCount);

            Controls.Add(layout);

            AcceptButton = exportButton;
            CancelButton = cancelButton;

            weaponComboBox.SelectedIndexChanged += (_, _) => UpdatePaintKits();
            searchTextBox.TextChanged += (_, _) => UpdatePaintKits();
            paintKitListBox.SelectedIndexChanged += (_, _) => OnPaintKitChanged();
            wearNumeric.ValueChanged += (_, _) => UpdateWear();
            wearNumeric.CustomTextChanged += (_, _) => UpdateWear();
            exportButton.Click += ExportButton_Click;
            contentFolderTextBox.TextChanged += ContentFolderTextBox_TextChanged;
            gameFolderTextBox.TextChanged += (_, _) => UpdateFolderToolTips();

            gameFolderTextBox.Text = Settings.Config.WeaponSkinExportGameDir;
            contentFolderTextBox.Text = Settings.Config.WeaponSkinExportContentDir;
            UpdateFolderToolTips();

            toolTip.SetToolTip(wearNumeric, "How worn the finish is, within the range the finish can have. Drag or type a value.");
            toolTip.SetToolTip(iconCheckBox, "Copies the game's compiled image of the weapon with this finish over the weapon's icon in the game folder.");

            var lastWeapon = catalog.Weapons.ToList().FindIndex(weapon => weapon.Name.Equals(Settings.Config.WeaponSkinExportWeapon, StringComparison.OrdinalIgnoreCase));
            weaponComboBox.SelectedIndex = Math.Max(0, lastWeapon);
        }

        public WeaponSkinExportRequest? CreateRequest()
            => SelectedWeapon is { } weapon && SelectedPaintKit is { } paintKit
                ? new WeaponSkinExportRequest(weapon, paintKit, ClampWear(paintKit, wearNumeric.Value), iconCheckBox.Checked)
                : null;

        private void UpdatePaintKits()
        {
            var previous = SelectedPaintKit;
            var search = searchTextBox.Text.Trim();

            paintKitListBox.BeginUpdate();
            paintKitListBox.Items.Clear();

            if (SelectedWeapon is { } weapon)
            {
                foreach (var paintKit in catalog.GetPaintKits(weapon))
                {
                    if (search.Length == 0
                        || paintKit.DisplayName.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                        || paintKit.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                    {
                        paintKitListBox.Items.Add(new PaintKitItem(paintKit));
                    }
                }
            }

            paintKitListBox.EndUpdate();

            var index = previous != null ? paintKitListBox.Items.Cast<PaintKitItem>().ToList().FindIndex(item => item.PaintKit == previous) : -1;

            if (paintKitListBox.Items.Count > 0)
            {
                paintKitListBox.SelectedIndex = Math.Max(0, index);
            }
            else
            {
                OnPaintKitChanged();
            }
        }

        private void OnPaintKitChanged()
        {
            if (SelectedPaintKit is not { } paintKit)
            {
                detailsLabel.Text = "No finish matches the search.";
                previewPictureBox.Image = null;
                return;
            }

            wearNumeric.MinValue = paintKit.WearMin;
            wearNumeric.MaxValue = paintKit.WearMax;
            wearNumeric.Value = paintKit.WearMin;

            detailsLabel.Text = $"{paintKit.Name} (#{paintKit.Id}), {DescribeStyle(paintKit.Style)}"
                + (paintKit.UseLegacyModel ? ", painted on the legacy model, which is exported too" : string.Empty)
                + $"\nWear {paintKit.WearMin:0.00} to {paintKit.WearMax:0.00}";

            UpdateWear();
        }

        private void UpdateWear()
        {
            if (SelectedPaintKit is not { } paintKit || SelectedWeapon is not { } weapon)
            {
                return;
            }

            var wear = ClampWear(paintKit, wearNumeric.Value);
            wearNameLabel.Text = WearNames.Last(name => wear >= name.Start).Name;

            // The game renders each finish at three amounts of wear
            var icon = WeaponSkinCatalog.GetPaintedIcon(weapon, paintKit, wear switch
            {
                < 0.15f => "light",
                < 0.38f => "medium",
                _ => "heavy",
            });

            previewPictureBox.Image = GetPreview(icon) ?? GetPreview(WeaponSkinCatalog.GetPaintedIcon(weapon, paintKit));
        }

        private static float ClampWear(PaintKit paintKit, float wear) => Math.Clamp(wear, paintKit.WearMin, Math.Max(paintKit.WearMin, paintKit.WearMax));

        private static string DescribeStyle(int style) => (PaintStyle)(style - 1) switch
        {
            PaintStyle.SolidColor => "solid color",
            PaintStyle.Hydrographic => "hydrographic",
            PaintStyle.SprayPaint => "spray-paint",
            PaintStyle.Anodized => "anodized",
            PaintStyle.AnodizedMulticolored => "anodized multicolored",
            PaintStyle.AnodizedAirbrushed => "anodized airbrushed",
            PaintStyle.CustomPaintJob => "custom paint job",
            PaintStyle.Patina => "patina",
            PaintStyle.Gunsmith => "gunsmith",
            _ => $"style {style}",
        };

        private Image? GetPreview(string path)
        {
            if (previews.TryGetValue(path, out var cached))
            {
                return cached;
            }

            Image? preview = null;

            try
            {
                if (package.FindEntry(path) is { } entry)
                {
                    using var resource = new Resource { FileName = path };
                    resource.Read(GameFileLoader.GetPackageEntryStream(package, entry));

                    if (resource.DataBlock is Texture texture)
                    {
                        using var bitmap = texture.GenerateBitmap();
                        preview = bitmap.ToBitmap();
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warn(nameof(WeaponSkinSelectForm), $"Failed to load the preview \"{path}\": {e.Message}");
            }

            previews[path] = preview;

            return preview;
        }

        private void ExportButton_Click(object? sender, EventArgs e)
        {
            if (CreateRequest() == null)
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

            if (GameFolder == null && iconCheckBox.Checked)
            {
                PickGameFolder();

                if (GameFolder == null)
                {
                    return;
                }
            }

            Settings.Config.WeaponSkinExportContentDir = ContentFolder;
            Settings.Config.WeaponSkinExportGameDir = GameFolder ?? string.Empty;
            Settings.Config.WeaponSkinExportWeapon = SelectedWeapon?.Name ?? string.Empty;
            Settings.Save();

            DialogResult = DialogResult.OK;
        }

        private void PickContentFolder()
        {
            if (AppFileDialogs.PickFolder("Choose the addon's content folder, e.g. content/csgo_addons/<addon>", AppFileDialogs.RememberIn.SaveDirectory) is { } folder)
            {
                contentFolderTextBox.Text = folder;
            }
        }

        private void PickGameFolder()
        {
            if (AppFileDialogs.PickFolder("Choose the addon's game folder, e.g. game/csgo_addons/<addon>", AppFileDialogs.RememberIn.SaveDirectory) is { } folder)
            {
                gameFolderTextBox.Text = folder;
            }
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
                gameFolderTextBox.Text = derived ?? string.Empty;
            }

            derivedGameFolder = derived;

            UpdateFolderToolTips();
        }

        private void UpdateFolderToolTips()
        {
            toolTip.SetToolTip(contentFolderTextBox, "The addon's content folder, which gets the painted material, its textures and, for legacy finishes, the model." +
                (ContentFolder is { } content ? $"\n{content}" : string.Empty));
            toolTip.SetToolTip(gameFolderTextBox, "The addon's game folder, which gets the compiled icon. Filled in from the content folder when it can be." +
                (GameFolder is { } game ? $"\n{game}" : string.Empty));
        }

        private static Label MakeLabel(string text) => new()
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 12, 0),
        };

        private static ThemedButton MakeButton(string text) => new()
        {
            Text = text,
            Size = new Size(90, 30),
            Margin = new Padding(8, 0, 0, 0),
        };

        private static ThemedButton MakeBrowseButton(Action pick)
        {
            var button = new ThemedButton
            {
                Text = "...",
                Size = new Size(36, 27),
                Margin = new Padding(0, 3, 0, 3),
            };
            button.Click += (_, _) => pick();

            return button;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                toolTip.Dispose();
                weaponComboBox.Dispose();
                searchTextBox.Dispose();
                paintKitListBox.Dispose();
                wearNumeric.Dispose();
                wearNameLabel.Dispose();
                detailsLabel.Dispose();
                iconCheckBox.Dispose();
                contentFolderTextBox.Dispose();
                gameFolderTextBox.Dispose();
                previewPictureBox.Dispose();

                foreach (var preview in previews.Values)
                {
                    preview?.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        private sealed record WeaponItem(WeaponDefinition Weapon)
        {
            public override string ToString() => $"{Weapon.DisplayName} ({Weapon.Name})";
        }

        private sealed record PaintKitItem(PaintKit PaintKit)
        {
            public override string ToString()
                => (PaintKit.DisplayName == PaintKit.Name ? PaintKit.Name : $"{PaintKit.DisplayName}  ({PaintKit.Name})")
                    + (PaintKit.UseLegacyModel ? "  [legacy]" : string.Empty);
        }
    }
}
