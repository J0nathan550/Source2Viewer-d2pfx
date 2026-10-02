using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace GUI.Utils;

static class ErrorDialog
{
    public static void Show(string title, string message, string details)
    {
        var owner = AppMessageDialogs.GetOwner();

        if (owner == null)
        {
            // Failed before the UI came up, the console is all there is
            Console.Error.WriteLine(details);
            return;
        }

        var window = new Window
        {
            Title = title,
            Width = 760,
            Height = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var copyButton = new Button { Content = "Copy to clipboard" };
        copyButton.Click += (_, _) => AppClipboard.SetText(details);

        var closeButton = new Button { Content = "Close", MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        closeButton.Click += (_, _) => window.Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new(0, 12, 0, 0),
            Children = { copyButton, closeButton },
        };

        var header = new SelectableTextBlock
        {
            Text = $"{message}{Environment.NewLine}{Environment.NewLine}Use copy button when sharing this error, and also mention your exact steps. Details also available in console.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new(0, 0, 0, 12),
        };

        var detailsBox = new TextBox
        {
            Text = details,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,monospace"),
        };

        var body = new DockPanel { Margin = new(16) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        body.Children.Add(header);
        body.Children.Add(buttons);
        body.Children.Add(detailsBox);

        window.Content = body;
        window.Show(owner);
    }
}
