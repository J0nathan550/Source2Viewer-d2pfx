using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GUI.Utils;

namespace GUI.Controls;

/// <summary>
/// The status bar of the main window: the window title, the keybindings of the shown viewer,
/// the version and the update notice, laid out like the WinForms MainFormBottomPanel.
/// </summary>
sealed class MainBottomPanel : Border
{
    private readonly TextBlock titleText;
    private readonly StackPanel keybindingsPanel;
    private readonly Button versionButton;
    private readonly Button updateButton;

    public event EventHandler? AboutRequested;

    public MainBottomPanel()
    {
        Classes.Add("statusBar");
        Height = 30;

        titleText = new TextBlock
        {
            Margin = new(8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        keybindingsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new(8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
        };

        versionButton = new Button { Content = "Version", VerticalAlignment = VerticalAlignment.Stretch };
        versionButton.Classes.Add("flat");
        versionButton.Click += (_, _) => AboutRequested?.Invoke(this, EventArgs.Empty);

        updateButton = new Button { Content = "Update Available", IsVisible = false, VerticalAlignment = VerticalAlignment.Stretch };
        updateButton.Classes.Add("flat");
        updateButton.Classes.Add("highlighted");
        updateButton.Click += (_, _) => AboutRequested?.Invoke(this, EventArgs.Empty);

        var root = new DockPanel();
        DockPanel.SetDock(updateButton, Dock.Right);
        DockPanel.SetDock(versionButton, Dock.Right);
        DockPanel.SetDock(keybindingsPanel, Dock.Right);
        root.Children.Add(updateButton);
        root.Children.Add(versionButton);
        root.Children.Add(keybindingsPanel);
        root.Children.Add(titleText);

        Child = root;
    }

    public string Text
    {
        get => titleText.Text ?? string.Empty;
        set => titleText.Text = value;
    }

    public void SetVersionText(string text) => versionButton.Content = text;

    public void RefreshUpdateState()
    {
        updateButton.Content = "Update Available";
        updateButton.IsVisible = Settings.Config.Update.CheckAutomatically && Settings.Config.Update.UpdateAvailable;
    }

    /// <summary>
    /// Points at a build that may already contain a fix for an error that was just shown.
    /// </summary>
    public void ShowUpdateAfterError()
    {
        RefreshUpdateState();

        if (updateButton.IsVisible || UpdateChecker.NewerDevBuild == null)
        {
            return;
        }

        // A stable channel user is not offered dev builds, but the fix is most likely there
        updateButton.Content = "Newer dev build available";
        updateButton.IsVisible = true;
    }

    public void UpdateKeybindings(List<KeybindingInfo> keybindings)
    {
        keybindingsPanel.Children.Clear();
        keybindingsPanel.IsVisible = keybindings.Count > 0;

        foreach (var keybinding in keybindings)
        {
            keybindingsPanel.Children.Add(CreateKeycap(keybinding));
        }
    }

    private static StackPanel CreateKeycap(KeybindingInfo keybinding)
    {
        var key = new Border
        {
            Child = new TextBlock
            {
                Text = keybinding.KeyCombination,
                FontWeight = FontWeight.Bold,
                FontSize = 11,
            },
        };
        key.Classes.Add("keycap");

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children =
            {
                key,
                new TextBlock { Text = keybinding.Description, FontSize = 11, VerticalAlignment = VerticalAlignment.Center },
            },
        };
    }
}
