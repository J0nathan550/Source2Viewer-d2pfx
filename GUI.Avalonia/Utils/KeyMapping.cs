using System.Windows.Forms;
using Avalonia.Input;

namespace GUI.Utils;

static class KeyMapping
{
    /// <summary>Converts an Avalonia key to the WinForms key code the shared viewers compare against.</summary>
    public static Keys ToKeys(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
        {
            return Keys.A + (key - Key.A);
        }

        if (key is >= Key.D0 and <= Key.D9)
        {
            return Keys.D0 + (key - Key.D0);
        }

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return Keys.NumPad0 + (key - Key.NumPad0);
        }

        if (key is >= Key.F1 and <= Key.F24)
        {
            return Keys.F1 + (key - Key.F1);
        }

        return key switch
        {
            Key.Back => Keys.Back,
            Key.Tab => Keys.Tab,
            Key.Enter => Keys.Enter,
            Key.Pause => Keys.Pause,
            Key.CapsLock => Keys.CapsLock,
            Key.Escape => Keys.Escape,
            Key.Space => Keys.Space,
            Key.PageUp => Keys.PageUp,
            Key.PageDown => Keys.PageDown,
            Key.End => Keys.End,
            Key.Home => Keys.Home,
            Key.Left => Keys.Left,
            Key.Up => Keys.Up,
            Key.Right => Keys.Right,
            Key.Down => Keys.Down,
            Key.PrintScreen => Keys.PrintScreen,
            Key.Insert => Keys.Insert,
            Key.Delete => Keys.Delete,
            Key.LWin => Keys.LWin,
            Key.RWin => Keys.RWin,
            Key.Apps => Keys.Apps,
            Key.Multiply => Keys.Multiply,
            Key.Add => Keys.Add,
            Key.Separator => Keys.Separator,
            Key.Subtract => Keys.Subtract,
            Key.Decimal => Keys.Decimal,
            Key.Divide => Keys.Divide,
            Key.NumLock => Keys.NumLock,
            Key.Scroll => Keys.Scroll,
            // The shared code checks the generic modifier keys, not the side specific ones
            Key.LeftShift or Key.RightShift => Keys.ShiftKey,
            Key.LeftCtrl or Key.RightCtrl => Keys.ControlKey,
            Key.LeftAlt or Key.RightAlt => Keys.Menu,
            Key.OemSemicolon => Keys.OemSemicolon,
            Key.OemPlus => Keys.Oemplus,
            Key.OemComma => Keys.Oemcomma,
            Key.OemMinus => Keys.OemMinus,
            Key.OemPeriod => Keys.OemPeriod,
            Key.OemQuestion => Keys.OemQuestion,
            Key.OemTilde => Keys.Oemtilde,
            Key.OemOpenBrackets => Keys.OemOpenBrackets,
            Key.OemPipe => Keys.OemPipe,
            Key.OemCloseBrackets => Keys.OemCloseBrackets,
            Key.OemQuotes => Keys.OemQuotes,
            Key.OemBackslash => Keys.OemBackslash,
            _ => Keys.None,
        };
    }

    public static Keys ToModifiers(KeyModifiers modifiers)
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

        return keys;
    }
}
