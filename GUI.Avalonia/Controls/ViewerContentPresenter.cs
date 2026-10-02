using System.Collections;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Data;
using GUI.Types.Viewers;

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
            Content = CreateControl(tab.Content, out var contentFailed),
        };

        tabControl.Items.Add(page);

        // Do not focus a tab whose content failed to produce, it only contains the error text
        if (tab.Select && !contentFailed)
        {
            tabControl.SelectedItem = page;
        }

        return page;
    }

    public static Control CreateControl(ViewerContent content) => CreateControl(content, out _);

    private static Control CreateControl(ViewerContent content, out bool contentFailed)
    {
        contentFailed = false;

        switch (content)
        {
            case ViewerContent.Text text:
                return CodeTextBox.Create(text.Content, text.Language, text.SourceMap);

            case ViewerContent.LazyText lazy:
            {
                string producedText;

                try
                {
                    producedText = lazy.GetContent();
                }
                catch (Exception e)
                {
                    producedText = e.ToString();
                    contentFailed = true;
                }

                return CodeTextBox.Create(producedText, lazy.Language);
            }

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
