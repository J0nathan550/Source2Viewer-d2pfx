// See Keys.cs for why this namespace exists in an Avalonia project.

using System.Collections;
using System.Linq;
using Avalonia.Layout;
using Avalonia.Threading;
using GUI.Controls;

namespace System.Windows.Forms;

/// <summary>Item list of <see cref="ComboBox"/> and <see cref="CheckedListBox"/>, mirroring the WinForms ObjectCollection.</summary>
public sealed class ObjectCollection : IEnumerable<object>
{
    private readonly List<object> items = [];
    private readonly Action<int, object, bool> inserted;
    private readonly Action<int> removed;
    private readonly Action cleared;

    internal ObjectCollection(Action<int, object, bool> inserted, Action<int> removed, Action cleared)
    {
        this.inserted = inserted;
        this.removed = removed;
        this.cleared = cleared;
    }

    public int Count => items.Count;

    public object this[int index] => items[index];

    public int Add(object item) => Add(item, false);

    public int Add(object item, bool isChecked)
    {
        Dispatcher.UIThread.VerifyAccess();
        items.Add(item);
        inserted(items.Count - 1, item, isChecked);
        return items.Count - 1;
    }

    public void AddRange(object[] newItems)
    {
        foreach (var item in newItems)
        {
            Add(item);
        }
    }

    public void Insert(int index, object item)
    {
        Dispatcher.UIThread.VerifyAccess();
        items.Insert(index, item);
        inserted(index, item, false);
    }

    public void RemoveAt(int index)
    {
        Dispatcher.UIThread.VerifyAccess();
        items.RemoveAt(index);
        removed(index);
    }

    public void Remove(object item)
    {
        var index = items.IndexOf(item);

        if (index >= 0)
        {
            RemoveAt(index);
        }
    }

    public void Clear()
    {
        Dispatcher.UIThread.VerifyAccess();
        items.Clear();
        cleared();
    }

    public int IndexOf(object item) => items.IndexOf(item);

    public bool Contains(object item) => items.Contains(item);

    public IEnumerator<object> GetEnumerator() => items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => items.GetEnumerator();

    internal static string GetItemText(object? item) => item switch
    {
        null => string.Empty,
        ThemedComboBoxItem themed => themed.Text,
        _ => item.ToString() ?? string.Empty,
    };

    internal int FindStringExact(string? text)
    {
        if (text == null)
        {
            return -1;
        }

        // WinForms matches case insensitively
        return items.FindIndex(i => string.Equals(GetItemText(i), text, StringComparison.OrdinalIgnoreCase));
    }
}

public class ComboBox : Control
{
    private readonly Avalonia.Controls.ComboBox comboBox;

    public ComboBox() : this(new Avalonia.Controls.ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 480 })
    {
    }

    private ComboBox(Avalonia.Controls.ComboBox comboBox) : base(comboBox)
    {
        this.comboBox = comboBox;

        Items = new ObjectCollection(
            (index, item, _) =>
            {
                var element = new Avalonia.Controls.ComboBoxItem { Content = ObjectCollection.GetItemText(item) };

                if (item is ThemedComboBoxItem { IsHeader: true })
                {
                    element.Classes.Add("header");
                }

                comboBox.Items.Insert(index, element);
            },
            index => comboBox.Items.RemoveAt(index),
            comboBox.Items.Clear);

        comboBox.SelectionChanged += (_, _) => SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? SelectedIndexChanged;

    public ObjectCollection Items { get; }

    public int SelectedIndex
    {
        get => OnUIThread(() => comboBox.SelectedIndex);
        set => SetOnUIThread(() => comboBox.SelectedIndex = value);
    }

    public object? SelectedItem
    {
        get => OnUIThread(() => comboBox.SelectedIndex is var i && i >= 0 && i < Items.Count ? Items[i] : null);
        set => SetOnUIThread(() => comboBox.SelectedIndex = value == null ? -1 : Items.IndexOf(value));
    }

    public override string Text
    {
        get => ObjectCollection.GetItemText(SelectedItem);
        set => SelectedIndex = Items.FindStringExact(value);
    }

    public int FindStringExact(string? text) => Items.FindStringExact(text);

#pragma warning disable CA1822 // Mirrors the WinForms API, Avalonia batches layout on its own
    public void BeginUpdate()
    {
    }

    public void EndUpdate()
    {
    }
#pragma warning restore CA1822
}

public sealed class ItemCheckEventArgs(int index, CheckState newCheckValue, CheckState currentValue) : EventArgs
{
    public int Index { get; } = index;
    public CheckState NewValue { get; set; } = newCheckValue;
    public CheckState CurrentValue { get; } = currentValue;
}

public delegate void ItemCheckEventHandler(object? sender, ItemCheckEventArgs e);

public class CheckedListBox : Control
{
    private readonly Avalonia.Controls.StackPanel panel;
    private readonly List<bool> checkedStates = [];
    private bool suppressEvents;

    public CheckedListBox() : this(new Avalonia.Controls.StackPanel())
    {
    }

    private CheckedListBox(Avalonia.Controls.StackPanel panel) : base(new Avalonia.Controls.ScrollViewer
    {
        Content = panel,
        MaxHeight = 220,
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
    })
    {
        this.panel = panel;

        Items = new ObjectCollection(
            (index, item, isChecked) =>
            {
                var checkBox = new Avalonia.Controls.CheckBox
                {
                    Content = ObjectCollection.GetItemText(item),
                    IsChecked = isChecked,
                    MinHeight = 0,
                    Padding = new(4, 0),
                };
                checkBox.IsCheckedChanged += OnItemCheckedChanged;
                checkedStates.Insert(index, isChecked);
                panel.Children.Insert(index, checkBox);
            },
            index =>
            {
                checkedStates.RemoveAt(index);
                panel.Children.RemoveAt(index);
            },
            () =>
            {
                checkedStates.Clear();
                panel.Children.Clear();
            });
    }

    public event ItemCheckEventHandler? ItemCheck;

    public ObjectCollection Items { get; }

    public IEnumerable<object> CheckedItems => Items.Where((_, i) => checkedStates[i]);

    public IEnumerable<int> CheckedIndices => Enumerable.Range(0, checkedStates.Count).Where(i => checkedStates[i]);

    public bool GetItemChecked(int index) => checkedStates[index];

    public void SetItemChecked(int index, bool value)
    {
        Dispatcher.UIThread.VerifyAccess();

        if (index < 0 || index >= checkedStates.Count || checkedStates[index] == value)
        {
            return;
        }

        // Like WinForms, a programmatic change raises ItemCheck too
        RaiseItemCheck(index, value);

        suppressEvents = true;

        try
        {
            ((Avalonia.Controls.CheckBox)panel.Children[index]).IsChecked = value;
        }
        finally
        {
            suppressEvents = false;
        }
    }

    public int FindStringExact(string? text) => Items.FindStringExact(text);

#pragma warning disable CA1822 // Mirrors the WinForms API, Avalonia batches layout on its own
    public void BeginUpdate()
    {
    }

    public void EndUpdate()
    {
    }
#pragma warning restore CA1822

    private void OnItemCheckedChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (suppressEvents || sender is not Avalonia.Controls.CheckBox checkBox)
        {
            return;
        }

        var index = panel.Children.IndexOf(checkBox);

        if (index >= 0)
        {
            RaiseItemCheck(index, checkBox.IsChecked == true);
        }
    }

    private void RaiseItemCheck(int index, bool newValue)
    {
        // ItemCheck happens before the state is committed, which RendererControl relies on
        ItemCheck?.Invoke(this, new ItemCheckEventArgs(index, newValue ? CheckState.Checked : CheckState.Unchecked, checkedStates[index] ? CheckState.Checked : CheckState.Unchecked));
        checkedStates[index] = newValue;
    }
}
