using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using GUI.Controls;
using GUI.Utils;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace GUI.Types.Viewers
{
    /// <summary>
    /// Browsable list of a map's entities with filters, and the selected entity's properties and connections.
    /// </summary>
    sealed class EntityViewer : Grid
    {
        public enum ObjectsToInclude
        {
            Everything,
            MeshEntities,
            PointEntities
        }

        public sealed record EntityRow(string Classname, string Targetname)
        {
            internal required Entity Entity { get; init; }
        }

        private readonly List<Entity> Entities;
        private readonly Action<Entity>? SelectEntityFunc;
        private readonly DataGrid entityGrid;
        private readonly EntityInfoControl entityInfo;
        private readonly TextBlock propertiesHeader;
        private readonly TextBox classFilter;
        private readonly TextBox keyFilter;
        private readonly TextBox valueFilter;
        private readonly CheckBox matchWholeValue;
        private ObjectsToInclude objectsToInclude = ObjectsToInclude.Everything;

        internal EntityViewer(VrfGuiContext guiContext, List<Entity> entities, Action<Entity>? selectAndFocusEntity = null)
        {
            Entities = entities;
            SelectEntityFunc = selectAndFocusEntity;

            classFilter = new TextBox { PlaceholderText = "Class name" };
            keyFilter = new TextBox { PlaceholderText = "Key" };
            valueFilter = new TextBox { PlaceholderText = "Value" };
            matchWholeValue = new CheckBox { Content = "Match whole value" };

            classFilter.TextChanged += (_, _) => UpdateGrid();
            keyFilter.TextChanged += (_, _) => UpdateGrid();
            valueFilter.TextChanged += (_, _) => UpdateGrid();
            matchWholeValue.IsCheckedChanged += (_, _) => UpdateGrid();

            var include = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            foreach (var (label, value) in new[] { ("Everything", ObjectsToInclude.Everything), ("Mesh entities", ObjectsToInclude.MeshEntities), ("Point entities", ObjectsToInclude.PointEntities) })
            {
                var radio = new RadioButton { Content = label, GroupName = "EntityViewerInclude", IsChecked = value == ObjectsToInclude.Everything };
                radio.IsCheckedChanged += (_, _) =>
                {
                    if (radio.IsChecked == true)
                    {
                        objectsToInclude = value;
                        UpdateGrid();
                    }
                };
                include.Children.Add(radio);
            }

            var keyValueRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 4 };
            SetColumn(valueFilter, 1);
            keyValueRow.Children.Add(keyFilter);
            keyValueRow.Children.Add(valueFilter);

            var filters = new StackPanel
            {
                Margin = new(6),
                Spacing = 4,
                Children = { include, classFilter, keyValueRow, matchWholeValue },
            };

            entityGrid = new DataGrid
            {
                IsReadOnly = true,
                CanUserSortColumns = true,
                CanUserResizeColumns = true,
                GridLinesVisibility = DataGridGridLinesVisibility.None,
                Columns =
                {
                    new DataGridTextColumn { Header = "Classname", Binding = new Avalonia.Data.Binding(nameof(EntityRow.Classname)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) },
                    new DataGridTextColumn { Header = "Targetname", Binding = new Avalonia.Data.Binding(nameof(EntityRow.Targetname)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) },
                },
            };
            entityGrid.SelectionChanged += (_, _) =>
            {
                if (entityGrid.SelectedItem is EntityRow row)
                {
                    ShowEntityProperties(row.Entity);
                }
            };
            entityGrid.DoubleTapped += (_, _) =>
            {
                if (entityGrid.SelectedItem is EntityRow row && row.Classname != "worldspawn")
                {
                    SelectEntityFunc?.Invoke(row.Entity);
                }
            };

            var left = new DockPanel();
            DockPanel.SetDock(filters, Dock.Top);
            left.Children.Add(filters);
            left.Children.Add(entityGrid);

            entityInfo = new EntityInfoControl(guiContext);
            entityInfo.OutputTargetActivated += ShowEntityByTargetName;
            entityInfo.InputSourceActivated += entity =>
            {
                ShowEntityProperties(entity);
                entityInfo.ShowPropertiesTab();
            };

            propertiesHeader = new TextBlock { Margin = new(6, 6, 6, 2), FontWeight = Avalonia.Media.FontWeight.SemiBold, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };

            var right = new DockPanel();
            DockPanel.SetDock(propertiesHeader, Dock.Top);
            right.Children.Add(propertiesHeader);
            right.Children.Add(entityInfo);

            ColumnDefinitions = new ColumnDefinitions("*,4,*");
            var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns };
            SetColumn(splitter, 1);
            SetColumn(right, 2);
            Children.Add(left);
            Children.Add(splitter);
            Children.Add(right);

            UpdateGrid();
        }

        private void UpdateGrid()
        {
            var classSearch = classFilter.Text ?? string.Empty;
            var keySearch = keyFilter.Text ?? string.Empty;
            var valueSearch = valueFilter.Text ?? string.Empty;
            var wholeValue = matchWholeValue.IsChecked == true;

            var rows = new List<EntityRow>(Entities.Count);

            foreach (var entity in Entities)
            {
                var classname = entity.GetStringProperty("classname", string.Empty);

                if (classSearch.Length > 0 && !classname.Contains(classSearch, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var hasModel = entity.ContainsKey("model");

                if ((objectsToInclude == ObjectsToInclude.MeshEntities && !hasModel) || (objectsToInclude == ObjectsToInclude.PointEntities && hasModel))
                {
                    continue;
                }

                if (!MatchesKeyValue(entity, keySearch, valueSearch, wholeValue))
                {
                    continue;
                }

                rows.Add(new EntityRow(classname, entity.TargetName ?? string.Empty) { Entity = entity });
            }

            rows.Sort(static (a, b) =>
            {
                var compare = string.Compare(a.Classname, b.Classname, StringComparison.OrdinalIgnoreCase);
                return compare != 0 ? compare : string.Compare(a.Targetname, b.Targetname, StringComparison.OrdinalIgnoreCase);
            });

            entityGrid.ItemsSource = rows;

            // when search changes set first entity as selected in entity props
            if (rows.Count > 0)
            {
                ShowEntityProperties(rows[0].Entity);
            }
        }

        private static bool MatchesKeyValue(Entity entity, string key, string value, bool wholeValue)
        {
            if (key.Length == 0 && value.Length == 0)
            {
                return true;
            }

            foreach (var prop in entity.Children)
            {
                if (key.Length > 0 && !prop.Key.Contains(key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (value.Length == 0)
                {
                    return true;
                }

                var stringValue = prop.Value.ToString() ?? string.Empty;

                if (wholeValue ? stringValue == value : stringValue.Contains(value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void ShowEntityByTargetName(string entityName)
        {
            var entity = Entities.FirstOrDefault(e => e.GetStringProperty("targetname", string.Empty) == entityName);

            if (entity != null)
            {
                ShowEntityProperties(entity);
                entityInfo.ShowPropertiesTab();
            }
        }

        private void ShowEntityProperties(Entity entity)
        {
            entityInfo.Clear();
            entityInfo.PopulateFromEntity(Entities, entity);
            entityInfo.ShowPopulatedTabs();

            var header = "Entity Properties";

            var targetname = entity.GetStringProperty("targetname", string.Empty);
            var classname = entity.GetStringProperty("classname", string.Empty);

            if (!string.IsNullOrEmpty(targetname))
            {
                header += $" - {targetname}";
            }
            else if (!string.IsNullOrEmpty(classname))
            {
                header += $" - {classname}";
            }

            if (entity.ParentLump.Resource is { } parentResource)
            {
                header += $" - Entity Lump: {parentResource.FileName}";
            }

            propertiesHeader.Text = header;
        }
    }
}
