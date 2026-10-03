using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using GUI.Utils;

namespace GUI.Controls
{
    /// <summary>
    /// A titled section framed like the WinForms ThemedGroupBox: a rounded border whose top line runs through the
    /// middle of the title and is broken by it.
    /// </summary>
    static class GroupBox
    {
        /// <summary>Space between the frame and the content.</summary>
        public static readonly Thickness DefaultPadding = new(16, 10, 16, 16);

        public static Grid Create(string title, Control content, Thickness? padding = null)
        {
            var header = new TextBlock
            {
                Text = title,
                Margin = new(10, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            };
            header.Classes.Add("groupHeader");

            var body = new Border { Child = content, Padding = padding ?? DefaultPadding };
            Grid.SetRow(body, 1);

            var frame = new GroupBoxFrame(header);
            Grid.SetRowSpan(frame, 2);

            var grid = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*"),
                Children = { frame, header, body },
            };
            grid.Classes.Add("groupBox");

            return grid;
        }
    }

    /// <summary>
    /// Draws the frame of a <see cref="GroupBox"/> behind it, leaving a gap for its title.
    /// </summary>
    sealed class GroupBoxFrame(TextBlock header) : Control
    {
        private const double Radius = 5;
        private const double Thickness = 2;
        private const double TitleGap = 2;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            Themer.ThemeChanged += OnThemeChanged;
            header.SizeChanged += OnHeaderSizeChanged;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            Themer.ThemeChanged -= OnThemeChanged;
            header.SizeChanged -= OnHeaderSizeChanged;
        }

        private void OnThemeChanged(object? sender, EventArgs e) => InvalidateVisual();

        private void OnHeaderSizeChanged(object? sender, SizeChangedEventArgs e) => InvalidateVisual();

        public override void Render(DrawingContext context)
        {
            var half = Thickness / 2;
            var top = Math.Round(header.Bounds.Height / 2);
            var left = half;
            var right = Bounds.Width - half;
            var bottom = Bounds.Height - half;

            if (right - left < Radius * 2 || bottom - top < Radius * 2)
            {
                return;
            }

            var gapStart = header.Bounds.Left - TitleGap;
            var gapEnd = header.Bounds.Right + TitleGap;
            var size = new Size(Radius, Radius);
            var geometry = new StreamGeometry();

            using (var ctx = geometry.Open())
            {
                // From the end of the title around the frame back to its start, the line under the title is left out
                ctx.BeginFigure(new Point(Math.Min(gapEnd, right - Radius), top), false);
                ctx.LineTo(new Point(right - Radius, top));
                ctx.ArcTo(new Point(right, top + Radius), size, 0, false, SweepDirection.Clockwise);
                ctx.LineTo(new Point(right, bottom - Radius));
                ctx.ArcTo(new Point(right - Radius, bottom), size, 0, false, SweepDirection.Clockwise);
                ctx.LineTo(new Point(left + Radius, bottom));
                ctx.ArcTo(new Point(left, bottom - Radius), size, 0, false, SweepDirection.Clockwise);
                ctx.LineTo(new Point(left, top + Radius));
                ctx.ArcTo(new Point(left + Radius, top), size, 0, false, SweepDirection.Clockwise);
                ctx.LineTo(new Point(Math.Max(gapStart, left + Radius), top));
                ctx.EndFigure(false);
            }

            var pen = new ImmutablePen(Themer.GetBrush(Themer.CurrentThemeColors.Border), Thickness);
            context.DrawGeometry(null, pen, geometry);
        }
    }
}
