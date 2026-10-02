using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using GUI.Types.Exporter;

namespace GUI.Controls;

/// <summary>
/// A closable document tab of the main window. Owns the viewer and the file context shown inside it.
/// </summary>
sealed class DocumentTab : TabItem, IDisposable
{
    private readonly TextBlock title;

    public ExportData? ExportData { get; set; }

    public string? ToolTipText
    {
        get => ToolTip.GetTip(this) as string;
        set => ToolTip.SetTip(this, value);
    }

    public bool IsDisposed { get; private set; }

    public event EventHandler? CloseRequested;

    // Themes key templates by type, a subclass has to ask for the TabItem one
    protected override Type StyleKeyOverride => typeof(TabItem);

    public DocumentTab(string text, bool closable = true)
    {
        title = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { title },
        };

        if (closable)
        {
            var closeButton = new Button
            {
                Content = "✕",
                FontSize = 10,
                Padding = new(4, 0),
                MinHeight = 0,
                Background = Avalonia.Media.Brushes.Transparent,
                BorderThickness = new(0),
                VerticalAlignment = VerticalAlignment.Center,
                Focusable = false,
            };
            ToolTip.SetTip(closeButton, "Close tab");
            closeButton.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
            header.Children.Add(closeButton);

            header.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton == MouseButton.Middle)
                {
                    CloseRequested?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                }
            };
        }

        Header = header;
    }

    public string Text
    {
        get => title.Text ?? string.Empty;
        set => title.Text = value;
    }

    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;

        var exportData = ExportData;
        ExportData = null;
        Content = null;

        if (exportData != null)
        {
            // Contents first: disposing them cancels loading and waits for it, and the context
            // disposes the resources that loading is still reading until it does
            exportData.DisposableContents?.Dispose();
            exportData.VrfGuiContext.Dispose();
        }
    }
}
