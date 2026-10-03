using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using GUI.Utils;

namespace GUI.Controls;

/// <summary>
/// Lays out the main window tabs in one row like the WinForms MainTabs: every tab is 200 wide, and they all
/// shrink evenly once they no longer fit, down to just the icon.
/// </summary>
sealed class MainTabStripPanel : Panel
{
    public const double TabHeight = 32;

    private const double BaseTabWidth = 200;
    private const double TabGap = 2;

    // Same padding around the icon as its offset from the top, so an icon only tab is square
    private const double MinTabWidth = TabHeight;

    private int CountVisibleChildren()
    {
        var count = 0;

        foreach (var child in Children)
        {
            if (child.IsVisible)
            {
                count++;
            }
        }

        return count;
    }

    private static double GetTabWidth(double availableWidth, int count)
    {
        if (count == 0 || double.IsInfinity(availableWidth))
        {
            return BaseTabWidth;
        }

        var idealWidth = Math.Floor((availableWidth - TabGap * (count - 1)) / count);
        return Math.Clamp(idealWidth, MinTabWidth, BaseTabWidth);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = CountVisibleChildren();
        var tabWidth = GetTabWidth(availableSize.Width, count);

        foreach (var child in Children)
        {
            child.Measure(new Size(tabWidth, TabHeight));
        }

        var totalWidth = count * tabWidth + Math.Max(0, count - 1) * TabGap;

        if (!double.IsInfinity(availableSize.Width))
        {
            totalWidth = Math.Min(totalWidth, availableSize.Width);
        }

        return new Size(totalWidth, TabHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var tabWidth = GetTabWidth(finalSize.Width, CountVisibleChildren());
        var x = 0.0;

        foreach (var child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            child.Arrange(new Rect(x, 0, tabWidth, finalSize.Height));
            x += tabWidth + TabGap;
        }

        return finalSize;
    }
}

/// <summary>
/// The icon, title and close button of a main window tab, colored by the tab's state like the WinForms MainTabs.
/// </summary>
sealed class MainTabHeader : DockPanel
{
    private readonly TabItem tab;
    private readonly TextBlock title;
    private readonly TabCloseButton? closeButton;
    private readonly MainTabShape shape;

    public MainTabHeader(TabItem tab, TextBlock title, TabCloseButton? closeButton, MainTabShape shape)
    {
        this.tab = tab;
        this.title = title;
        this.closeButton = closeButton;
        this.shape = shape;

        // Transparent rather than null so the whole tab can be clicked, not just the icon and text
        Background = Brushes.Transparent;

        tab.PropertyChanged += OnTabPropertyChanged;
        UpdateState();
    }

    private bool IsHighlighted => tab.IsSelected || tab.IsPointerOver;

    private void OnTabPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TabItem.IsSelectedProperty || e.Property == IsPointerOverProperty)
        {
            UpdateState();
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => UpdateState();

    private void UpdateState()
    {
        var colors = Themer.CurrentThemeColors;
        var foreground = Themer.GetBrush(IsHighlighted ? colors.Contrast : colors.ContrastSoft);

        title.Foreground = foreground;
        closeButton?.SetForeground(foreground);

        shape.InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Themer.ThemeChanged += OnThemeChanged;
        UpdateState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Themer.ThemeChanged -= OnThemeChanged;
    }
}

/// <summary>
/// The background of a main window tab: the selected tab takes the page color with rounded top corners that
/// curve out into the page, and a hovered tab is a rounded box outlined in the accent color.
/// </summary>
sealed class MainTabShape(TabItem tab) : Control
{
    private const double Radius = 8;

    public override void Render(DrawingContext context)
    {
        var colors = Themer.CurrentThemeColors;
        var width = Bounds.Width;
        var height = Bounds.Height;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (tab.IsSelected)
        {
            context.DrawGeometry(Themer.GetBrush(colors.AppMiddle), null, CreateSelectedGeometry(width, height));
        }
        else if (tab.IsPointerOver)
        {
            // Two pixels short of the bottom so the outline does not touch the page
            var rect = new Rect(0, 0, width, Math.Max(0, height - 2)).Deflate(0.5);
            var pen = new ImmutablePen(Themer.GetBrush(colors.Accent), 1);
            context.DrawRectangle(Themer.GetBrush(colors.HoverAccent), pen, rect, Radius, Radius);
        }
    }

    /// <summary>
    /// Rounded top corners, and at the bottom inverted corners reaching past the tab so it blends into the page.
    /// </summary>
    private static StreamGeometry CreateSelectedGeometry(double width, double height)
    {
        var radius = Math.Min(Radius, Math.Min(width, height) / 2);
        var size = new Size(radius, radius);
        var geometry = new StreamGeometry();

        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(-radius, height), true);
            ctx.ArcTo(new Point(0, height - radius), size, 0, false, SweepDirection.CounterClockwise);
            ctx.LineTo(new Point(0, radius));
            ctx.ArcTo(new Point(radius, 0), size, 0, false, SweepDirection.Clockwise);
            ctx.LineTo(new Point(width - radius, 0));
            ctx.ArcTo(new Point(width, radius), size, 0, false, SweepDirection.Clockwise);
            ctx.LineTo(new Point(width, height - radius));
            ctx.ArcTo(new Point(width + radius, height), size, 0, false, SweepDirection.CounterClockwise);
            ctx.EndFigure(true);
        }

        return geometry;
    }
}

/// <summary>
/// The close cross of a main window tab, with a circle behind it while hovered.
/// </summary>
sealed class TabCloseButton : Control
{
    private const double CrossSize = 8;

    private IImmutableBrush foreground = Brushes.Gray;

    public event EventHandler? Click;

    static TabCloseButton()
    {
        AffectsRender<TabCloseButton>(IsPointerOverProperty);
    }

    public TabCloseButton()
    {
        Width = CrossSize + 10;
        Height = CrossSize + 10;
        Focusable = false;
        ToolTip.SetTip(this, "Close tab");
    }

    public void SetForeground(IImmutableBrush brush)
    {
        foreground = brush;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);

        if (IsPointerOver)
        {
            context.DrawEllipse(Themer.GetBrush(Themer.CurrentThemeColors.Attention), null, center, Bounds.Width / 2, Bounds.Height / 2);
        }

        // White on the red circle while hovered, so the cross stays readable in every theme
        var pen = new ImmutablePen(IsPointerOver ? Brushes.White : foreground, 1);
        var half = CrossSize / 2;
        context.DrawLine(pen, new Point(center.X - half, center.Y - half), new Point(center.X + half, center.Y + half));
        context.DrawLine(pen, new Point(center.X - half, center.Y + half), new Point(center.X + half, center.Y - half));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        // Keep the tab from being selected by the press that closes it
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (e.InitialPressMouseButton == MouseButton.Left && new Rect(Bounds.Size).Contains(e.GetPosition(this)))
        {
            e.Handled = true;
            Click?.Invoke(this, EventArgs.Empty);
        }
    }
}

/// <summary>
/// Lays out menu bar items in a row and leaves out the ones that no longer fit, like a WinForms menu strip,
/// instead of drawing them under whatever is next to the menu.
/// </summary>
sealed class MenuStripPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = 0.0;
        var height = 0.0;

        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));

            if (width + child.DesiredSize.Width <= availableSize.Width)
            {
                width += child.DesiredSize.Width;
                height = Math.Max(height, child.DesiredSize.Height);
            }
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        var full = false;

        foreach (var child in Children)
        {
            var width = child.DesiredSize.Width;
            full |= x + width > finalSize.Width;

            child.Arrange(full ? default : new Rect(x, 0, width, finalSize.Height));
            x += width;
        }

        return finalSize;
    }
}
