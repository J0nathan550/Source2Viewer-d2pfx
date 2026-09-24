using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Controls;

/// <summary>
/// A drop down list that opens its items under a search box, which narrows them down to the ones whose text contains
/// every word typed. Typing while it has focus opens it with what was typed. The items and the selection only change
/// once an item is picked, so it behaves like any other drop down list to the code using it.
/// </summary>
public class SearchableComboBox : ThemedComboBox
{
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONDBLCLK = 0x0203;

    private ToolStripDropDown? popup;
    private ToolStripControlHost? host;
    private Panel? content;
    private SearchTextBox? searchTextBox;
    private ListBox? listBox;
    private long closedByClickAt;

    public SearchableComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
    }

    /// <summary>Whether the search is open.</summary>
    public bool SearchOpen => popup?.Visible == true;

    /// <summary>
    /// Opens the search, starting with the given text.
    /// </summary>
    public void OpenSearch(string text = "")
    {
        if (Items.Count == 0 || SearchOpen)
        {
            return;
        }

        CreatePopup();

        searchTextBox.Text = text;
        searchTextBox.SelectionStart = text.Length;
        Filter();

        listBox.ItemHeight = ItemHeight;

        // Sized for every item, so the list does not jump around while it narrows down
        var rows = Math.Clamp(Items.Count, 1, MaxDropDownItems);
        var searchPanel = searchTextBox.Parent!;

        host.Size = new Size(Math.Max(Width, DropDownWidth), searchPanel.Height + (listBox.ItemHeight * rows) + 2);
        popup.Show(this, new Point(0, Height));
        searchTextBox.Focus();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg is WM_LBUTTONDOWN or WM_LBUTTONDBLCLK && Enabled)
        {
            Focus();

            // A click on the box while the search is open closes it, which should not open it again
            if (Environment.TickCount64 - closedByClickAt > 250)
            {
                OpenSearch();
            }

            return;
        }

        base.WndProc(ref m);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is Keys.F4 or (Keys.Alt | Keys.Down) or (Keys.Alt | Keys.Up))
        {
            OpenSearch();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (!char.IsControl(e.KeyChar))
        {
            e.Handled = true;
            OpenSearch(e.KeyChar.ToString());
            return;
        }

        base.OnKeyPress(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            popup?.Dispose();
            popup = null;
            host?.Dispose();
            host = null;
            content?.Dispose();
            content = null;
            searchTextBox?.Dispose();
            searchTextBox = null;
            listBox?.Dispose();
            listBox = null;
        }

        base.Dispose(disposing);
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(popup), nameof(host), nameof(searchTextBox), nameof(listBox))]
    private void CreatePopup()
    {
        if (popup != null && host != null && searchTextBox != null && listBox != null)
        {
            return;
        }

        var padding = this.AdjustForDPI(6);

        searchTextBox = new SearchTextBox
        {
            BorderStyle = BorderStyle.None,
            Dock = DockStyle.Fill,
            PlaceholderText = "Search",
            BackColor = DropDownBackColor,
            ForeColor = DropDownForeColor,
            Font = Font,
            KeyHandler = HandleSearchKey,
        };

        searchTextBox.TextChanged += (_, _) => Filter();

        var searchPanel = new Panel
        {
            Dock = DockStyle.Top,
            Padding = new Padding(padding, padding, padding, padding),
            Height = searchTextBox.PreferredHeight + (padding * 2),
            BackColor = DropDownBackColor,
        };

        searchPanel.Controls.Add(searchTextBox);
        searchPanel.Paint += (_, e) =>
        {
            using var pen = new Pen(HeaderColor);
            e.Graphics.DrawLine(pen, 0, searchPanel.Height - 1, searchPanel.Width, searchPanel.Height - 1);
        };

        listBox = new ListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            DrawMode = DrawMode.OwnerDrawFixed,
            IntegralHeight = false,
            BackColor = DropDownBackColor,
            ForeColor = DropDownForeColor,
            Font = Font,
        };

        listBox.DrawItem += ListBox_DrawItem;
        listBox.MouseMove += (_, e) =>
        {
            var index = listBox.IndexFromPoint(e.Location);

            if (index >= 0 && index != listBox.SelectedIndex)
            {
                listBox.SelectedIndex = index;
            }
        };
        listBox.MouseClick += (_, e) =>
        {
            if (listBox.IndexFromPoint(e.Location) >= 0)
            {
                Pick();
            }
        };

        content = new Panel
        {
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = DropDownBackColor,
        };

        content.Controls.Add(listBox);
        content.Controls.Add(searchPanel);

        host = new ToolStripControlHost(content)
        {
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            AutoSize = false,
        };

        popup = new ToolStripDropDown
        {
            Padding = new Padding(1),
            Renderer = new DarkToolStripRenderer(new CustomColorTable()),
        };

        popup.Items.Add(host);
        popup.Closed += (_, e) =>
        {
            if (e.CloseReason == ToolStripDropDownCloseReason.AppClicked && ClientRectangle.Contains(PointToClient(Cursor.Position)))
            {
                closedByClickAt = Environment.TickCount64;
            }
        };
    }

    /// <summary>
    /// Lists the items that contain every word of the search, selecting the chosen item while nothing is typed and
    /// the first match otherwise, which Enter picks.
    /// </summary>
    private void Filter()
    {
        if (searchTextBox == null || listBox == null)
        {
            return;
        }

        var words = searchTextBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        listBox.BeginUpdate();
        listBox.Items.Clear();

        foreach (var item in Items)
        {
            var text = GetItemText(item) ?? string.Empty;

            if (words.All(word => text.Contains(word, StringComparison.OrdinalIgnoreCase)))
            {
                listBox.Items.Add(item);
            }
        }

        var selected = words.Length == 0 && SelectedItem != null ? listBox.Items.IndexOf(SelectedItem) : -1;
        listBox.SelectedIndex = selected >= 0 ? selected : listBox.Items.Count > 0 ? 0 : -1;
        listBox.EndUpdate();
    }

    private bool HandleSearchKey(Keys keyData)
    {
        if (listBox == null || popup == null)
        {
            return false;
        }

        var page = Math.Max(1, (listBox.ClientSize.Height / Math.Max(1, listBox.ItemHeight)) - 1);

        switch (keyData)
        {
            case Keys.Enter:
            case Keys.Tab:
                Pick();
                return true;

            case Keys.Escape:
                popup.Close(ToolStripDropDownCloseReason.Keyboard);
                Focus();
                return true;

            case Keys.Down:
                MoveSelection(1);
                return true;

            case Keys.Up:
                MoveSelection(-1);
                return true;

            case Keys.PageDown:
                MoveSelection(page);
                return true;

            case Keys.PageUp:
                MoveSelection(-page);
                return true;
        }

        return false;
    }

    private void MoveSelection(int by)
    {
        if (listBox != null && listBox.Items.Count > 0)
        {
            listBox.SelectedIndex = Math.Clamp(listBox.SelectedIndex + by, 0, listBox.Items.Count - 1);
        }
    }

    private void Pick()
    {
        var item = listBox?.SelectedItem;

        popup?.Close(ToolStripDropDownCloseReason.ItemClicked);
        Focus();

        if (item != null)
        {
            SelectedItem = item;
        }
    }

    private void ListBox_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || listBox == null)
        {
            return;
        }

        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

        using (var brush = new SolidBrush(selected ? HighlightColor : DropDownBackColor))
        {
            e.Graphics.FillRectangle(brush, e.Bounds);
        }

        var bounds = e.Bounds;
        var padding = this.AdjustForDPI(4);
        bounds.X += padding;
        bounds.Width -= padding;

        TextRenderer.DrawText(e.Graphics, GetItemText(listBox.Items[e.Index]), e.Font, bounds, DropDownForeColor, Color.Transparent,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    /// <summary>
    /// Hands the keys that move through the list to the list before the drop down handles them itself.
    /// </summary>
    private sealed class SearchTextBox : TextBox
    {
        public Func<Keys, bool>? KeyHandler { get; init; }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            => KeyHandler?.Invoke(keyData) == true || base.ProcessCmdKey(ref msg, keyData);
    }
}
