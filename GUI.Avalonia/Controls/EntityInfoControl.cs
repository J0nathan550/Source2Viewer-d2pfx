using System.Linq;
using Avalonia.Controls;
using GUI.Utils;
using ValveKeyValue;
using ValveResourceFormat.Graphs;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace GUI.Controls
{
    /// <summary>
    /// Tabs with an entity's properties, outputs and inputs.
    /// </summary>
    sealed class EntityInfoControl : TabControl
    {
        public sealed record PropertyRow(string Name, string Value)
        {
            internal string? ExternalReference { get; init; }
        }

        public sealed record OutputRow(string Output, string Target, string Input, string Parameter, float Delay, string TimesToFire);

        public sealed record InputRow(string Source, string Output, string Input, string Parameter, float Delay, string TimesToFire)
        {
            internal Entity? SourceEntity { get; init; }
        }

        private readonly List<PropertyRow> properties = [];
        private readonly List<OutputRow> outputs = [];
        private readonly List<InputRow> inputs = [];
        private readonly DataGrid propertiesGrid;
        private readonly DataGrid outputsGrid;
        private readonly DataGrid inputsGrid;
        private readonly TabItem propertiesTab;
        private readonly TabItem outputsTab;
        private readonly TabItem inputsTab;

        /// <summary>Raised when an output's target entity name is double clicked.</summary>
        public event Action<string>? OutputTargetActivated;

        /// <summary>Raised when an input's source entity is double clicked.</summary>
        public event Action<Entity>? InputSourceActivated;

        /// <summary>Raised when a property value that references a resource was opened.</summary>
        public event Action? ExternalReferenceOpened;

        protected override Type StyleKeyOverride => typeof(TabControl);

        public EntityInfoControl(VrfGuiContext vrfGuiContext)
        {
            Classes.Add("content");

            propertiesGrid = ViewerContentPresenter.CreateGrid(properties);
            outputsGrid = ViewerContentPresenter.CreateGrid(outputs);
            inputsGrid = ViewerContentPresenter.CreateGrid(inputs);

            propertiesGrid.DoubleTapped += (_, _) =>
            {
                if (propertiesGrid.SelectedItem is PropertyRow row
                    && Types.Viewers.Resource.OpenExternalReference(vrfGuiContext, row.ExternalReference ?? row.Value))
                {
                    ExternalReferenceOpened?.Invoke();
                }
            };

            outputsGrid.DoubleTapped += (_, _) =>
            {
                if (outputsGrid.SelectedItem is OutputRow { Target.Length: > 0 } row)
                {
                    OutputTargetActivated?.Invoke(row.Target);
                }
            };

            inputsGrid.DoubleTapped += (_, _) =>
            {
                if (inputsGrid.SelectedItem is InputRow { SourceEntity: { } entity })
                {
                    InputSourceActivated?.Invoke(entity);
                }
            };

            propertiesTab = new TabItem { Header = "Properties", Content = propertiesGrid };
            outputsTab = new TabItem { Header = "Outputs", Content = outputsGrid };
            inputsTab = new TabItem { Header = "Inputs", Content = inputsGrid };

            Items.Add(propertiesTab);
        }

        public void ShowPropertiesTab() => SelectedItem = propertiesTab;

        public void Clear()
        {
            properties.Clear();
            outputs.Clear();
            inputs.Clear();
        }

        public void PopulateFromEntity(Entity entity)
        {
            foreach (var child in entity.Children)
            {
                var resourcePath = ResourcePath(child.Value);
                AddProperty(child.Key, resourcePath ?? KVGraphNode.StringifyValue(child.Value), resourcePath);
            }

            if (entity.Connections != null)
            {
                foreach (var connection in entity.Connections)
                {
                    AddOutputConnection(connection);
                }
            }
        }

        public void PopulateFromEntity(List<Entity> entities, Entity entity)
        {
            PopulateFromEntity(entity);

            foreach (var connection in entity.GetInputConnections(entities))
            {
                AddInputConnection(connection);
            }
        }

        public void AddProperty(string name, string value, string? externalReference = null)
        {
            properties.Add(new PropertyRow(name, value) { ExternalReference = externalReference });
        }

        /// <summary>
        /// The bare text of a string property. The KV3 form a value serializes to carries its quotes
        /// and, for a resource, its type prefix (<c>resource_name:"particles/foo.vpcf"</c>), which is
        /// neither what the grid should show nor a path anything can be looked up by.
        /// </summary>
        private static string? ResourcePath(KVObject value)
            => value.ValueType == KVValueType.String ? (string)value : null;

        public void AddOutputConnection(Connection connectionData)
        {
            outputs.Add(new OutputRow(
                connectionData.OutputName,
                connectionData.TargetName,
                connectionData.InputName,
                connectionData.OverrideParam,
                connectionData.Delay,
                GetStringTimesToFire(connectionData.TimesToFire)));
        }

        public void AddInputConnection(Connection connectionData)
        {
            inputs.Add(new InputRow(
                connectionData.SourceEntity.TargetName ?? "",
                connectionData.OutputName,
                connectionData.InputName,
                connectionData.OverrideParam,
                connectionData.Delay,
                GetStringTimesToFire(connectionData.TimesToFire))
            {
                SourceEntity = connectionData.SourceEntity,
            });
        }

        /// <summary>Refreshes the grids after the rows changed and shows only the tabs that have rows.</summary>
        public void ShowPopulatedTabs()
        {
            propertiesGrid.ItemsSource = properties.ToList();
            outputsGrid.ItemsSource = outputs.ToList();
            inputsGrid.ItemsSource = inputs.ToList();

            SetTabVisible(outputsTab, outputs.Count > 0, 1);
            SetTabVisible(inputsTab, inputs.Count > 0, Items.Contains(outputsTab) ? 2 : 1);

            SelectedItem ??= propertiesTab;
        }

        private void SetTabVisible(TabItem page, bool shouldShow, int index)
        {
            var isShown = Items.Contains(page);

            if (shouldShow && !isShown)
            {
                Items.Insert(index, page);
            }
            else if (!shouldShow && isShown)
            {
                if (SelectedItem == page)
                {
                    SelectedItem = propertiesTab;
                }

                Items.Remove(page);
            }
        }

        private static string GetStringTimesToFire(int timesToFire)
        {
            return timesToFire switch
            {
                1 => "Only Once",
                >= 2 => $"Only {timesToFire} Times",
                _ => "Infinite",
            };
        }
    }
}
