using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using GUI.Types.Exporter;
using GUI.Utils;

namespace GUI.Controls;

/// <summary>
/// A tab of the main window: an icon, the title and a close button, like the WinForms MainTabs.
/// Owns the viewer and the file context shown inside it.
/// </summary>
sealed class DocumentTab : System.Windows.Forms.TabPage, IDisposable
{
    private readonly TextBlock title;
    private readonly ThemedIcon icon;

    /// <summary>The opened file, stored in Tag like the WinForms tabs so shared code finds it the same way.</summary>
    public ExportData? ExportData
    {
        get => Tag as ExportData;
        set => Tag = value;
    }

    public string? ToolTipText
    {
        get => ToolTip.GetTip(this) as string;
        set => ToolTip.SetTip(this, value);
    }

    public bool Closable { get; }

    public bool IsDisposed { get; private set; }

    public event EventHandler? CloseRequested;

    public DocumentTab(string text, string? iconName = null, bool closable = true)
    {
        Closable = closable;
        Classes.Add("document");

        title = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        icon = AppIcons.Create(iconName ?? "File");
        icon.IsVisible = iconName != null;
        icon.VerticalAlignment = VerticalAlignment.Center;

        var header = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(icon, Dock.Left);
        header.Children.Add(icon);

        if (closable)
        {
            var closeButton = new Button
            {
                Content = AppIcons.Create("CloseTab", 12),
                VerticalAlignment = VerticalAlignment.Center,
                Focusable = false,
            };
            closeButton.Classes.Add("tabClose");
            ToolTip.SetTip(closeButton, "Close tab");
            closeButton.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);

            DockPanel.SetDock(closeButton, Dock.Right);
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

        title.Margin = new(8, 0, 4, 0);
        header.Children.Add(title);

        Header = header;
    }

    public string Text
    {
        get => title.Text ?? string.Empty;
        set => title.Text = value;
    }

    public string? IconName
    {
        get => icon.IconName;
        set
        {
            icon.IconName = value;
            icon.IsVisible = value != null;
        }
    }

    /// <summary>Shows a picture that is not one of the app icons, such as a Steam game icon.</summary>
    public void SetIconImage(Bitmap image)
    {
        icon.FixedSource = image;
        icon.IsVisible = true;
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

        var content = Content;
        Content = null;

        if (exportData != null)
        {
            // Contents first: disposing them cancels loading and waits for it, and the context
            // disposes the resources that loading is still reading until it does
            exportData.DisposableContents?.Dispose();
            exportData.VrfGuiContext.Dispose();
        }
        else if (content is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
