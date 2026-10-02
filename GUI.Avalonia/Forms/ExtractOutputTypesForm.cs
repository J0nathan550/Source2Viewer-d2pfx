using Avalonia.Controls;
using Avalonia.Layout;
using GUI.Utils;
using WinForms = System.Windows.Forms;

namespace GUI.Forms
{
    /// <summary>
    /// Asks which format each file type is exported as, with the same API as the WinForms one so the exporters are shared.
    /// </summary>
    sealed class ExtractOutputTypesForm : IDisposable
    {
        private readonly Grid typesTable;
        private readonly Window window;
        private WinForms.DialogResult result = WinForms.DialogResult.Cancel;

        /// <summary>Raised with the changed <see cref="WinForms.ComboBox"/> as sender, its Tag is the file type.</summary>
        public event EventHandler? ChangeTypeEvent;

        public ExtractOutputTypesForm()
        {
            typesTable = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = 12,
                RowSpacing = 4,
            };

            var continueButton = new Button { Content = "Continue", MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
            var cancelButton = new Button { Content = "Cancel", MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };

            window = new Window
            {
                Title = "Choose output formats",
                Width = 520,
                Height = 420,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };

            continueButton.Click += (_, _) =>
            {
                result = WinForms.DialogResult.Continue;
                window.Close();
            };
            cancelButton.Click += (_, _) => window.Close();

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new(0, 12, 0, 0),
                Children = { continueButton, cancelButton },
            };

            var root = new DockPanel { Margin = new(16) };
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);
            root.Children.Add(new ScrollViewer { Content = typesTable });
            window.Content = root;
        }

        public void AddTypeToTable(string type, int count, List<string> outputTypes, int defaultType)
        {
            var row = typesTable.RowDefinitions.Count;
            typesTable.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var label = new TextBlock { Text = type, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(label, row);
            typesTable.Children.Add(label);

            var dropdown = new WinForms.ComboBox { Tag = type };
            dropdown.Items.AddRange([.. outputTypes]);
            dropdown.SelectedIndex = defaultType;
            dropdown.SelectedIndexChanged += (sender, e) => ChangeTypeEvent?.Invoke(sender, e);

            Grid.SetRow(dropdown.Native, row);
            Grid.SetColumn(dropdown.Native, 1);
            typesTable.Children.Add(dropdown.Native);

            var countLabel = new TextBlock { Text = $"{count} file{(count == 1 ? "" : "s")}", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75 };
            Grid.SetRow(countLabel, row);
            Grid.SetColumn(countLabel, 2);
            typesTable.Children.Add(countLabel);
        }

        public WinForms.DialogResult ShowDialog()
        {
            if (AppMessageDialogs.GetOwner() is not { } owner)
            {
                return WinForms.DialogResult.Cancel;
            }

            DispatcherHelpers.WaitOnUIThread(() => window.ShowDialog(owner));
            return result;
        }

        public void Dispose()
        {
        }
    }
}
