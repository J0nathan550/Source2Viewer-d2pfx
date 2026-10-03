using System.Collections;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Data;
using GUI.Types.Viewers;
using GUI.Utils;

namespace GUI.Controls;

// renders the UI agnostic ViewerContent model into Avalonia controls
static class ViewerContentPresenter
{
    public static TabItem AddContentTab(TabControl tabControl, string name, ViewerContent content, bool select = false)
    {
        return AddContentTab(tabControl, new ViewerTab(name, content, select));
    }

    public static TabItem AddContentTab(TabControl tabControl, ViewerTab tab)
    {
        var page = new TabItem
        {
            Header = tab.Name,
            Content = CreateControl(tab.Content),
        };

        tabControl.Items.Add(page);

        if (tab.Select)
        {
            tabControl.SelectedItem = page;
        }

        return page;
    }

    public static Control CreateControl(ViewerContent content)
    {
        switch (content)
        {
            // Editors (and lazily produced text) are only created when their tab is first shown,
            // a resource with many blocks would otherwise hold a full text document per block
            case ViewerContent.Text text:
                return new DeferredContent(() => CodeTextBox.Create(text.Content, text.Language, text.SourceMap));

            case ViewerContent.LazyText lazy:
                return new DeferredContent(
                    () => CodeTextBox.Create(lazy.GetContent(), lazy.Language),
                    static e => CodeTextBox.Create(e.ToString(), HighlightLanguage.None));

            case ViewerContent.HexDump hex:
                return new HexViewer(hex.Bytes);

            case ViewerContent.Grid grid:
                return CreateGrid(grid.Rows);

            case ViewerContent.Tabs tabs:
            {
                var tabControl = CreateTabControl();

                foreach (var tab in tabs.Items)
                {
                    AddContentTab(tabControl, tab);
                }

                if (tabControl.SelectedIndex < 0 && tabControl.ItemCount > 0)
                {
                    tabControl.SelectedIndex = 0;
                }

                return tabControl;
            }

            default:
                throw new NotSupportedException($"Unknown content type {content.GetType().Name}");
        }
    }

    public static TabControl CreateTabControl()
    {
        var tabControl = new TabControl();
        tabControl.Classes.Add("content");
        return tabControl;
    }

    /// <summary>
    /// A read only table with one column per public property of the row type, like a WinForms DataGridView with AutoGenerateColumns.
    /// </summary>
    public static DataGrid CreateGrid(IList rows)
    {
        var grid = new DataGrid
        {
            IsReadOnly = true,
            CanUserResizeColumns = true,
            CanUserSortColumns = true,
            GridLinesVisibility = DataGridGridLinesVisibility.All,
            ItemsSource = rows,
        };

        var rowType = rows.GetType().GetInterfaces()
            .FirstOrDefault(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IList<>))
            ?.GetGenericArguments()[0]
            ?? (rows.Count > 0 ? rows[0]?.GetType() : null);

        if (rowType == null)
        {
            return grid;
        }

        foreach (var property in rowType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = property.Name,
                // Rows are arbitrary model types, reflection binding is the only option
#pragma warning disable IL2026
                Binding = new ReflectionBinding(property.Name) { Mode = BindingMode.OneWay },
#pragma warning restore IL2026
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            });
        }

        return grid;
    }
}
