// See Keys.cs for why this namespace exists in an Avalonia project.

using System.Threading;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaControl = Avalonia.Controls.Control;

namespace System.Windows.Forms;

public class MouseEventArgs(MouseButtons button, int clicks, int x, int y, int delta) : EventArgs
{
    public MouseButtons Button { get; } = button;
    public int Clicks { get; } = clicks;
    public int X { get; } = x;
    public int Y { get; } = y;
    public int Delta { get; } = delta;
    public Drawing.Point Location => new(X, Y);
}

public delegate void MouseEventHandler(object? sender, MouseEventArgs e);

/// <summary>
/// Base of the compatibility controls. Each wraps an Avalonia control in <see cref="Native"/>, which is what
/// gets placed in the visual tree. Property setters may be called from any thread, like the shared code
/// sometimes does, and are marshalled to the UI thread.
/// Avalonia controls own nothing that needs disposing, <see cref="Dispose()"/> only exists because shared code calls it.
/// </summary>
#pragma warning disable CA1822 // Instance members mirror the WinForms API the shared code calls
public abstract class Control
{
    private static int modifierKeys;

    protected Control(AvaloniaControl native)
    {
        Native = native;
    }

    /// <summary>The Avalonia control that represents this control on screen.</summary>
    public AvaloniaControl Native { get; }

    /// <summary>Modifier keys currently held, as last reported by keyboard or pointer input.</summary>
    public static Keys ModifierKeys => (Keys)Volatile.Read(ref modifierKeys);

    internal static void UpdateModifierKeys(KeyModifiers modifiers)
    {
        var keys = Keys.None;

        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            keys |= Keys.Shift;
        }

        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            keys |= Keys.Control;
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            keys |= Keys.Alt;
        }

        Volatile.Write(ref modifierKeys, (int)keys);
    }

    public string Name { get; set; } = string.Empty;
    public object? Tag { get; set; }
    public DockStyle Dock { get; set; }
    public bool AutoSize { get; set; }
    public bool IsDisposed { get; private set; }

    public bool Enabled
    {
        get => OnUIThread(() => Native.IsEnabled);
        set => SetOnUIThread(() => Native.IsEnabled = value);
    }

    public bool Visible
    {
        get => OnUIThread(() => Native.IsVisible);
        set => SetOnUIThread(() => Native.IsVisible = value);
    }

    public virtual string Text { get; set; } = string.Empty;

    public bool InvokeRequired => !Dispatcher.UIThread.CheckAccess();

    public void BeginInvoke(Action action) => Dispatcher.UIThread.Post(action);

    public void Invoke(Action action) => Dispatcher.UIThread.Invoke(action);

    public void Focus() => SetOnUIThread(() => Native.Focus());

    public void Dispose()
    {
        IsDisposed = true;
    }

    protected static void SetOnUIThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    protected static T OnUIThread<T>(Func<T> func)
        => Dispatcher.UIThread.CheckAccess() ? func() : Dispatcher.UIThread.Invoke(func);
}
#pragma warning restore CA1822

public class Label : Control
{
    private readonly Avalonia.Controls.TextBlock textBlock;

    public Label() : this(new Avalonia.Controls.TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap })
    {
    }

    private Label(Avalonia.Controls.TextBlock textBlock) : base(textBlock)
    {
        this.textBlock = textBlock;
    }

    public override string Text
    {
        get => OnUIThread(() => textBlock.Text ?? string.Empty);
        set => SetOnUIThread(() => textBlock.Text = value);
    }
}

public class CheckBox : Control
{
    private readonly Avalonia.Controls.CheckBox checkBox;

    public CheckBox() : this(new Avalonia.Controls.CheckBox())
    {
    }

    private CheckBox(Avalonia.Controls.CheckBox checkBox) : base(checkBox)
    {
        this.checkBox = checkBox;
        checkBox.IsCheckedChanged += (_, _) => CheckedChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => OnUIThread(() => checkBox.IsChecked == true);
        set => SetOnUIThread(() => checkBox.IsChecked = value);
    }

    public override string Text
    {
        get => OnUIThread(() => checkBox.Content as string ?? string.Empty);
        set => SetOnUIThread(() => checkBox.Content = value);
    }
}
