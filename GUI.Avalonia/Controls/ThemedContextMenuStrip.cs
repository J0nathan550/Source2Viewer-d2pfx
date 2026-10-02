using System.Collections;
using System.Windows.Forms;
using Avalonia.Controls;

namespace GUI.Controls;

/// <summary>
/// A context menu with the item API of the WinForms one, so shared viewers can add their own entries to it.
/// </summary>
public sealed class ThemedContextMenuStrip : IDisposable
{
    private readonly ContextMenu menu = new() { Placement = PlacementMode.Pointer };

    public ThemedContextMenuStrip()
    {
        Items = new ItemCollection(menu);
    }

    public ItemCollection Items { get; }

    /// <summary>Opens the menu at the pointer over <paramref name="owner"/>; the location is where it was clicked.</summary>
    public void Show(Avalonia.Controls.Control owner, System.Drawing.Point location)
    {
        menu.Open(owner);
    }

    public void Dispose()
    {
        menu.Close();
    }

    public sealed class ItemCollection(ContextMenu menu) : IEnumerable<ToolStripItem>
    {
        private readonly List<ToolStripItem> items = [];

        public int Count => items.Count;

        public ToolStripItem this[int index] => items[index];

        public void Add(ToolStripItem item)
        {
            items.Add(item);
            menu.Items.Add(item.Native);
        }

        public void RemoveAt(int index)
        {
            items.RemoveAt(index);
            menu.Items.RemoveAt(index);
        }

        public IEnumerator<ToolStripItem> GetEnumerator() => items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => items.GetEnumerator();
    }
}
