using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace GUI.Controls;

/// <summary>
/// Offset, hex and ASCII columns for a byte array, drawing only the visible rows.
/// </summary>
sealed class HexViewer : DockPanel
{
    private const int BytesPerRow = 16;

    private readonly ScrollBar scrollBar;
    private readonly HexCanvas canvas;

    public HexViewer(byte[] bytes)
    {
        canvas = new HexCanvas(bytes);
        scrollBar = new ScrollBar
        {
            Orientation = Orientation.Vertical,
            Minimum = 0,
            SmallChange = 1,
            AllowAutoHide = false,
        };

        SetDock(scrollBar, Dock.Right);
        Children.Add(scrollBar);
        Children.Add(canvas);

        scrollBar.ValueChanged += (_, e) => canvas.FirstRow = (int)e.NewValue;
        canvas.VisibleRowsChanged += UpdateScrollRange;
        canvas.PointerWheelChanged += (_, e) =>
        {
            scrollBar.Value = Math.Clamp(scrollBar.Value - e.Delta.Y * 3, 0, scrollBar.Maximum);
            e.Handled = true;
        };

        Focusable = true;
        KeyDown += OnKeyDown;
    }

    private void UpdateScrollRange()
    {
        var totalRows = canvas.RowCount;
        var visibleRows = Math.Max(1, canvas.VisibleRows);

        scrollBar.Maximum = Math.Max(0, totalRows - visibleRows);
        scrollBar.ViewportSize = visibleRows;
        scrollBar.LargeChange = visibleRows;
        scrollBar.IsVisible = totalRows > visibleRows;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var delta = e.Key switch
        {
            Key.Down => 1,
            Key.Up => -1,
            Key.PageDown => canvas.VisibleRows,
            Key.PageUp => -canvas.VisibleRows,
            Key.Home => int.MinValue / 2,
            Key.End => int.MaxValue / 2,
            _ => 0,
        };

        if (delta != 0)
        {
            scrollBar.Value = Math.Clamp(scrollBar.Value + delta, 0, scrollBar.Maximum);
            e.Handled = true;
        }
    }

    private sealed class HexCanvas(byte[] bytes) : Control
    {
        private static readonly Typeface Typeface = new(CodeTextBox.MonospaceFont);
        private const double FontSize = 13;

        private double rowHeight = 18;

        public event Action? VisibleRowsChanged;

        public int RowCount => (bytes.Length + BytesPerRow - 1) / BytesPerRow;

        public int VisibleRows { get; private set; }

        public int FirstRow
        {
            get;
            set
            {
                field = value;
                InvalidateVisual();
            }
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);

            VisibleRows = (int)(e.NewSize.Height / rowHeight);
            VisibleRowsChanged?.Invoke();
        }

        public override void Render(DrawingContext context)
        {
            context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

            var foreground = TextElement.GetForeground(this) ?? Brushes.Gray;
            var dim = new ImmutableSolidColorBrush(Colors.Gray);
            var line = new char[8 + 2 + BytesPerRow * 3 + 1 + BytesPerRow];

            for (var row = 0; row <= VisibleRows; row++)
            {
                var rowIndex = FirstRow + row;
                var offset = rowIndex * BytesPerRow;

                if (offset >= bytes.Length)
                {
                    break;
                }

                var count = Math.Min(BytesPerRow, bytes.Length - offset);
                var position = 0;

                offset.TryFormat(line.AsSpan(position), out var written, "X8", CultureInfo.InvariantCulture);
                position += written;
                line[position++] = ' ';
                line[position++] = ' ';

                var hexStart = position;

                for (var i = 0; i < BytesPerRow; i++)
                {
                    if (i < count)
                    {
                        bytes[offset + i].TryFormat(line.AsSpan(position), out _, "X2", CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        line[position] = ' ';
                        line[position + 1] = ' ';
                    }

                    line[position + 2] = ' ';
                    position += 3;
                }

                line[position++] = ' ';

                for (var i = 0; i < BytesPerRow; i++)
                {
                    var b = i < count ? bytes[offset + i] : (byte)' ';
                    line[position++] = b is >= 0x20 and < 0x7F ? (char)b : '.';
                }

                var y = row * rowHeight;

                var offsetText = new FormattedText(new string(line, 0, hexStart), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface, FontSize, dim);
                var dataText = new FormattedText(new string(line, hexStart, position - hexStart), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface, FontSize, foreground);

                rowHeight = Math.Max(rowHeight, Math.Ceiling(dataText.Height));

                context.DrawText(offsetText, new Point(6, y));
                context.DrawText(dataText, new Point(6 + offsetText.WidthIncludingTrailingWhitespace, y));
            }
        }
    }
}
