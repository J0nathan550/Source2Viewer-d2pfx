using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;

namespace GUI.Forms
{
    public enum SvgExportFormat
    {
        Svg,
        Png,
        Jpg,
    }

    /// <summary>
    /// Asks which format to export an svg as, and (for raster formats) the resolution to rasterize at.
    /// The chosen resolution is a uniform scale that preserves aspect ratio.
    /// </summary>
    public sealed class SvgExportForm : Window
    {
        private static readonly int[] PresetLongEdges = [64, 128, 256, 512, 1024, 2048, 4096];

        private readonly float sourceWidth;
        private readonly float sourceHeight;
        private readonly List<SvgExportFormat> formats = [];
        // Long edge in pixels for each resolution item; 0 means the svg's native size, -1 means the custom field.
        private readonly List<int> itemLongEdges = [];
        private readonly ComboBox formatComboBox;
        private readonly ComboBox presetComboBox;
        private readonly NumericUpDown customNumeric;
        private readonly TextBlock resolutionLabel;
        private readonly StackPanel customRow;
        private readonly TextBlock outputLabel;
        private bool accepted;

        public SvgExportFormat SelectedFormat => formats[Math.Max(0, formatComboBox.SelectedIndex)];

        // Guard against a degenerate zero-size svg so the scale math never divides by zero.
        private float SourceLongEdge => MathF.Max(MathF.Max(sourceWidth, sourceHeight), 1f);

        /// <summary>The scale to rasterize at, relative to the svg's native resolution (1 = native).</summary>
        public float SelectedScale => ChosenLongEdge / SourceLongEdge;

        private float ChosenLongEdge => itemLongEdges[Math.Max(0, presetComboBox.SelectedIndex)] switch
        {
            0 => SourceLongEdge,
            -1 => (float)(customNumeric.Value ?? 1024),
            var longEdge => longEdge,
        };

        public SvgExportForm(float sourceWidth, float sourceHeight, bool canExportSvg)
        {
            this.sourceWidth = sourceWidth;
            this.sourceHeight = sourceHeight;

            Title = "Export image";
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var nativeLongEdge = (int)MathF.Ceiling(MathF.Max(sourceWidth, sourceHeight));
            var maxLongEdge = Math.Max(nativeLongEdge, 8192);

            formatComboBox = new ComboBox { Width = 220 };
            var formatLabels = new List<string>();

            foreach (var (format, label) in BuildFormats(canExportSvg))
            {
                formats.Add(format);
                formatLabels.Add(label);
            }

            formatComboBox.ItemsSource = formatLabels;

            // Default to SVG when available (lossless), otherwise PNG.
            var defaultFormat = formats.Contains(SvgExportFormat.Svg) ? SvgExportFormat.Svg : SvgExportFormat.Png;
            formatComboBox.SelectedIndex = formats.IndexOf(defaultFormat);

            var presetLabels = new List<string>();
            itemLongEdges.Add(0);
            presetLabels.Add($"Original  ({(int)sourceWidth} x {(int)sourceHeight})");

            foreach (var preset in PresetLongEdges.Where(p => p > nativeLongEdge))
            {
                itemLongEdges.Add(preset);
                var (width, height) = DimensionsForLongEdge(preset);
                presetLabels.Add($"{width} x {height}");
            }

            itemLongEdges.Add(-1);
            presetLabels.Add("Custom...");

            presetComboBox = new ComboBox { Width = 220, ItemsSource = presetLabels, SelectedIndex = 0 };

            customNumeric = new NumericUpDown
            {
                Minimum = nativeLongEdge,
                Maximum = maxLongEdge,
                Increment = 1,
                FormatString = "0",
                Value = Math.Clamp(1024, nativeLongEdge, maxLongEdge),
                Width = 120,
            };

            customRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { customNumeric, new TextBlock { Text = "px (long edge)", VerticalAlignment = VerticalAlignment.Center } },
            };

            resolutionLabel = new TextBlock { Text = "Resolution:", VerticalAlignment = VerticalAlignment.Center };
            outputLabel = new TextBlock { Margin = new(0, 10, 0, 0) };

            var layout = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,Auto"),
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"),
                ColumnSpacing = 12,
                RowSpacing = 6,
                Margin = new(20, 18, 20, 14),
            };

            void Place(Control control, int row, int column, int span = 1)
            {
                Grid.SetRow(control, row);
                Grid.SetColumn(control, column);
                Grid.SetColumnSpan(control, span);
                layout.Children.Add(control);
            }

            var saveButton = new Button { Content = "Save", MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center, IsDefault = true };
            var cancelButton = new Button { Content = "Cancel", MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center, IsCancel = true };
            saveButton.Click += (_, _) =>
            {
                accepted = true;
                Close();
            };
            cancelButton.Click += (_, _) => Close();

            Place(new TextBlock { Text = "Format:", VerticalAlignment = VerticalAlignment.Center }, 0, 0);
            Place(formatComboBox, 0, 1);
            Place(resolutionLabel, 1, 0);
            Place(presetComboBox, 1, 1);
            Place(customRow, 2, 1);
            Place(outputLabel, 3, 0, 2);
            Place(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new(0, 18, 0, 0),
                Children = { saveButton, cancelButton },
            }, 4, 0, 2);

            Content = layout;

            formatComboBox.SelectionChanged += (_, _) => UpdateResolutionVisibility();
            presetComboBox.SelectionChanged += (_, _) =>
            {
                UpdateResolutionVisibility();
                UpdateOutputLabel();
            };
            customNumeric.ValueChanged += (_, _) => UpdateOutputLabel();

            UpdateResolutionVisibility();
            UpdateOutputLabel();
        }

        /// <summary>Shows the dialog and returns whether the user chose to save.</summary>
        public async Task<bool> ShowDialogAsync(Window owner)
        {
            await ShowDialog(owner).ConfigureAwait(true);
            return accepted;
        }

        private static (SvgExportFormat Format, string Label)[] BuildFormats(bool canExportSvg)
            => canExportSvg
                ? [(SvgExportFormat.Svg, "SVG (vector)"), (SvgExportFormat.Png, "PNG"), (SvgExportFormat.Jpg, "JPG")]
                : [(SvgExportFormat.Png, "PNG"), (SvgExportFormat.Jpg, "JPG")];

        private void UpdateResolutionVisibility()
        {
            var isRaster = SelectedFormat != SvgExportFormat.Svg;
            var isCustom = itemLongEdges[Math.Max(0, presetComboBox.SelectedIndex)] == -1;

            resolutionLabel.IsVisible = isRaster;
            presetComboBox.IsVisible = isRaster;
            outputLabel.IsVisible = isRaster;
            customRow.IsVisible = isRaster && isCustom;
        }

        private void UpdateOutputLabel()
        {
            var (width, height) = DimensionsForLongEdge(ChosenLongEdge);
            outputLabel.Text = $"Output: {width} x {height} px";
        }

        // Output dimensions when the svg is scaled so its long edge becomes longEdge, preserving aspect ratio.
        private (int Width, int Height) DimensionsForLongEdge(float longEdge)
        {
            var scale = longEdge / SourceLongEdge;
            return ((int)(sourceWidth * scale), (int)(sourceHeight * scale));
        }
    }
}
