using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace GUI.Utils;

public enum MessageIcon
{
    Info,
    Warning,
    Error,
    Question
}

public enum ConfirmButtons
{
    OkCancel,
    YesNo
}

public static class AppMessageDialogs
{
    public static async Task ShowMessageAsync(string message, string title, MessageIcon icon = MessageIcon.Info)
    {
        await ShowAsync(message, title, icon, [("OK", (bool?)true)]).ConfigureAwait(true);
    }

    public static async Task<bool> ConfirmAsync(string message, string title, MessageIcon icon = MessageIcon.Question, ConfirmButtons buttons = ConfirmButtons.OkCancel)
    {
        // Settings are loaded before any window exists, there is nobody to ask yet
        if (GetOwner() == null)
        {
            Log.Warn(title, message);
            return true;
        }

        (string, bool?)[] choices = buttons == ConfirmButtons.YesNo
            ? [("Yes", true), ("No", false)]
            : [("OK", true), ("Cancel", false)];

        var result = await ShowAsync(message, title, icon, choices).ConfigureAwait(true);
        return result == true;
    }

    /// <summary>
    /// Asks a yes or no question that can also be cancelled.
    /// </summary>
    /// <returns><see langword="null"/> when cancelled.</returns>
    public static Task<bool?> AskYesNoCancelAsync(string message, string title, MessageIcon icon = MessageIcon.Question)
    {
        return ShowAsync(message, title, icon, [("Yes", true), ("No", false), ("Cancel", null)]);
    }

    internal static Window? GetOwner()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return null;
        }

        foreach (var window in desktop.Windows)
        {
            if (window.IsActive)
            {
                return window;
            }
        }

        return desktop.MainWindow;
    }

    private static async Task<bool?> ShowAsync(string message, string title, MessageIcon icon, (string Text, bool? Result)[] choices)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(() => ShowAsync(message, title, icon, choices)).ConfigureAwait(false);
        }

        var owner = GetOwner();

        if (owner == null)
        {
            Log.Info(title, message);
            return choices[0].Result;
        }

        bool? result = null;

        var window = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 320,
            MaxWidth = 720,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };

        foreach (var (text, choiceResult) in choices)
        {
            var button = new Button { Content = text, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
            button.Click += (_, _) =>
            {
                result = choiceResult;
                window.Close();
            };
            buttons.Children.Add(button);
        }

        var iconText = icon switch
        {
            MessageIcon.Warning => "⚠",
            MessageIcon.Error => "⛔",
            MessageIcon.Question => "?",
            _ => "i",
        };

        var body = new DockPanel { Margin = new(16), LastChildFill = true };

        DockPanel.SetDock(buttons, Dock.Bottom);
        buttons.Margin = new(0, 16, 0, 0);
        body.Children.Add(buttons);

        var iconBlock = new TextBlock
        {
            Text = iconText,
            FontSize = 28,
            Margin = new(0, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Foreground = icon == MessageIcon.Error ? Brushes.IndianRed : null,
        };
        DockPanel.SetDock(iconBlock, Dock.Left);
        body.Children.Add(iconBlock);

        body.Children.Add(new SelectableTextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        });

        window.Content = body;

        await window.ShowDialog(owner).ConfigureAwait(true);

        // Closing through the title bar is the same as picking the last (dismissive) choice
        return result ?? choices[^1].Result;
    }
}
