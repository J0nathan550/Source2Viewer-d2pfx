// See Keys.cs for why this namespace exists in an Avalonia project.

using Avalonia.Controls;

namespace System.Windows.Forms;

/// <summary>An entry of a <see cref="GUI.Controls.ThemedContextMenuStrip"/>.</summary>
public abstract class ToolStripItem
{
    internal abstract Avalonia.Controls.Control Native { get; }

    /// <summary>Nothing to release, exists because shared code disposes menu items it removes.</summary>
#pragma warning disable CA1822 // Mirrors the WinForms API
    public void Dispose()
#pragma warning restore CA1822
    {
    }
}

public sealed class ToolStripMenuItem : ToolStripItem
{
    private readonly MenuItem menuItem;

    public ToolStripMenuItem(string text)
    {
        menuItem = new MenuItem { Header = text };
        menuItem.Click += (_, _) => Click?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Click;

    public string Text
    {
        get => menuItem.Header as string ?? string.Empty;
        set => menuItem.Header = value;
    }

    public bool Enabled
    {
        get => menuItem.IsEnabled;
        set => menuItem.IsEnabled = value;
    }

    internal override Avalonia.Controls.Control Native => menuItem;
}

public sealed class ToolStripSeparator : ToolStripItem
{
    internal override Avalonia.Controls.Control Native { get; } = new Separator();
}
