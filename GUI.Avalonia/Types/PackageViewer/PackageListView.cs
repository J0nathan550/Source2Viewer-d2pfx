using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using GUI.Utils;

namespace GUI.Types.PackageViewer
{
    /// <summary>
    /// The details view of a folder, like the WinForms BetterListView: a header that sorts by the clicked column and
    /// shows the sorted column's icon, and rows with the icon and name, size and type of each item.
    /// </summary>
    sealed class PackageListView : DockPanel
    {
        private const double RowHeight = 25;

        private static readonly string[] ColumnNames = ["Name", "Size", "Type"];

        private readonly ThemedIcon[] sortIcons = new ThemedIcon[ColumnNames.Length];
        private List<PackageViewer.ListRow> rows = [];
        private int sortColumn;
        private bool sortDescending;

        /// <summary>The list of rows, for selection and input.</summary>
        public ListBox ItemList { get; }

        public PackageListView()
        {
            var header = new Grid { ColumnDefinitions = ColumnDefinitionsForRows(), Height = RowHeight };
            header.Classes.Add("listHeader");

            for (var column = 0; column < ColumnNames.Length; column++)
            {
                header.Children.Add(CreateHeaderCell(column));
            }

            var headerBorder = new Border { Child = header };
            headerBorder.Classes.Add("listHeader");
            DockPanel.SetDock(headerBorder, Dock.Top);
            Children.Add(headerBorder);

            ItemList = new ListBox
            {
                SelectionMode = SelectionMode.Multiple,
                ItemTemplate = new FuncDataTemplate<PackageViewer.ListRow>(static (_, _) => CreateRow(), supportsRecycling: true),
            };
            ItemList.Classes.Add("packageList");
            ScrollViewer.SetHorizontalScrollBarVisibility(ItemList, ScrollBarVisibility.Disabled);
            Children.Add(ItemList);

            UpdateSortIcons();
        }

        // The name takes what the fixed size and type columns leave, like WinForms AdjustColumnWidths
        private static ColumnDefinitions ColumnDefinitionsForRows() => new("*,100,100");

        private Border CreateHeaderCell(int column)
        {
            var text = new TextBlock
            {
                Text = ColumnNames[column],
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            var sortIcon = AppIcons.Create("SortUp");
            sortIcon.VerticalAlignment = VerticalAlignment.Center;
            sortIcon.Margin = new(0, 0, 4, 0);
            sortIcons[column] = sortIcon;

            var content = new DockPanel { Margin = new(3, 0, 0, 0) };
            DockPanel.SetDock(sortIcon, Dock.Right);
            content.Children.Add(sortIcon);
            content.Children.Add(text);

            var cell = new Border
            {
                Child = content,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            cell.Classes.Add(column < ColumnNames.Length - 1 ? "listHeaderCell" : "listHeaderLastCell");
            Grid.SetColumn(cell, column);

            cell.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton == MouseButton.Left)
                {
                    OnColumnClick(column);
                }
            };

            return cell;
        }

        private static Grid CreateRow()
        {
            var icon = AppIcons.Create("File");
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.Margin = new(3, 0, 3, 0);

            var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var size = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new(6, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            var type = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new(6, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };

            var nameCell = new DockPanel();
            DockPanel.SetDock(icon, Dock.Left);
            nameCell.Children.Add(icon);
            nameCell.Children.Add(name);

            Grid.SetColumn(size, 1);
            Grid.SetColumn(type, 2);

            var row = new Grid
            {
                ColumnDefinitions = ColumnDefinitionsForRows(),
                Height = RowHeight,
                Background = Brushes.Transparent,
                Children = { nameCell, size, type },
            };

            // Rows are recycled, so follow the row's item instead of binding once
            row.DataContextChanged += (_, _) =>
            {
                if (row.DataContext is PackageViewer.ListRow item)
                {
                    icon.IconName = item.IconName;
                    name.Text = item.Name;
                    size.Text = item.SizeText;
                    type.Text = item.Type;
                }
            };

            return row;
        }

        /// <summary>Shows the rows, sorted by the chosen column.</summary>
        public void SetRows(List<PackageViewer.ListRow> newRows)
        {
            rows = newRows;
            ApplySort();
        }

        private void OnColumnClick(int column)
        {
            if (column == sortColumn)
            {
                sortDescending = !sortDescending;
            }
            else
            {
                sortColumn = column;

                // The biggest first is what sorting by size is usually for
                sortDescending = column == 1;
            }

            UpdateSortIcons();
            ApplySort();
        }

        private void UpdateSortIcons()
        {
            for (var column = 0; column < sortIcons.Length; column++)
            {
                sortIcons[column].IsVisible = column == sortColumn;
                sortIcons[column].IconName = sortDescending ? "SortDown" : "SortUp";
            }
        }

        private void ApplySort()
        {
            ItemList.ItemsSource = rows.Order(Comparer<PackageViewer.ListRow>.Create(Compare)).ToList();
        }

        /// <summary>
        /// Sorts like the WinForms ListViewColumnSorter: the parent folder link stays on top, and by name the folders
        /// come before the files whichever way it is sorted.
        /// </summary>
        private int Compare(PackageViewer.ListRow x, PackageViewer.ListRow y)
        {
            if (x.IsParent || y.IsParent)
            {
                return x.IsParent == y.IsParent ? 0 : x.IsParent ? -1 : 1;
            }

            var result = 0;

            switch (sortColumn)
            {
                case 0:
                    var folderX = x.PackageEntry == null ? -1 : 1;
                    var folderY = y.PackageEntry == null ? -1 : 1;

                    if (folderX != folderY)
                    {
                        return folderX - folderY;
                    }

                    result = string.CompareOrdinal(x.Name, y.Name);
                    break;

                case 1:
                    result = x.Size.CompareTo(y.Size);
                    break;

                default:
                    result = string.CompareOrdinal(x.Type, y.Type);
                    break;
            }

            return sortDescending ? -result : result;
        }
    }
}
