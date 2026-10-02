using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using WinForms = System.Windows.Forms;

namespace GUI.Controls;

/// <summary>
/// A GL viewport with a sidebar of viewer options, the Avalonia counterpart of the WinForms RendererControl.
/// </summary>
sealed class RendererControl : Grid
{
    private const double SidebarWidth = 240;

    private readonly StackPanel controlsPanel;
    private readonly Panel controlsHost;
    private readonly ScrollViewer sidebar;
    private readonly GridSplitter splitter;
    private readonly TextBlock moveSpeed;
    private readonly Dictionary<string, StackPanel> namedGroups = [];
    private readonly bool isPreview;
    private Panel? currentControlsTarget;

    public Panel GLControlContainer { get; }

    public RendererControl(bool isPreview = false)
    {
        this.isPreview = isPreview;

        controlsPanel = new StackPanel { Margin = new(6, 4, 8, 8) };
        controlsPanel.Classes.Add("sidebar");

        sidebar = new ScrollViewer
        {
            Content = controlsPanel,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };

        controlsHost = new DockPanel { Children = { sidebar } };

        GLControlContainer = new Panel { ClipToBounds = true, Background = Brushes.Black };

        moveSpeed = new TextBlock
        {
            Text = "Move speed: 1.0x (scroll to change)",
            Margin = new(6, 2),
            Opacity = 0.75,
            FontSize = 11,
        };

        var viewportHost = new DockPanel();
        DockPanel.SetDock(moveSpeed, Dock.Bottom);
        viewportHost.Children.Add(moveSpeed);
        viewportHost.Children.Add(GLControlContainer);

        splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, Background = Brushes.Transparent };

        // The full viewer has options on the left, a package preview keeps the list on the left and options on the right
        ColumnDefinitions = isPreview
            ? new ColumnDefinitions($"*,4,{SidebarWidth - 20}")
            : new ColumnDefinitions($"{SidebarWidth},4,*");

        SetColumn(controlsHost, isPreview ? 2 : 0);
        SetColumn(splitter, 1);
        SetColumn(viewportHost, isPreview ? 0 : 2);

        Children.Add(controlsHost);
        Children.Add(splitter);
        Children.Add(viewportHost);
    }

    private Panel ControlsPanel => currentControlsTarget ?? controlsPanel;

    public void AddControl(WinForms.Control control) => ControlsPanel.Children.Add(control.Native);

    public void AddControl(Control control) => ControlsPanel.Children.Add(control);

    /// <summary>
    /// Shows the previewed file's name as the first item in the controls panel. Used in preview mode,
    /// where there is no tab header to display the file name.
    /// </summary>
    public void AddPreviewFileName(string fileName, int imageIndex)
    {
        var header = new TextBlock
        {
            Text = fileName,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new(6, 6, 6, 2),
        };
        ToolTip.SetTip(header, fileName);

        DockPanel.SetDock(header, Dock.Top);
        controlsHost.Children.Insert(0, header);
    }

    public static GLViewerCheckboxControl CreateCheckBox(string name, bool defaultChecked, Action<bool> changeCallback)
    {
        var checkbox = new GLViewerCheckboxControl(name, defaultChecked);
        checkbox.CheckBox.CheckedChanged += (_, __) =>
        {
            changeCallback(checkbox.CheckBox.Checked);
        };

        return checkbox;
    }

    public WinForms.CheckBox AddCheckBox(string name, bool defaultChecked, Action<bool> changeCallback)
    {
        var checkbox = CreateCheckBox(name, defaultChecked, changeCallback);
        AddControl(checkbox);

        return checkbox.CheckBox;
    }

    public WinForms.ComboBox AddSelection(string name, Action<string, int> changeCallback, bool horizontal = false, bool fill = false)
    {
        var selectionControl = new GLViewerSelectionControl(name, horizontal);
        AddControl(selectionControl);

        var comboBox = selectionControl.ComboBox;
        comboBox.SelectedIndexChanged += (_, __) =>
        {
            if (comboBox.SelectedItem is string selectedItem)
            {
                changeCallback(selectedItem, comboBox.SelectedIndex);
            }
            else if (comboBox.SelectedItem is ThemedComboBoxItem selectedThemedItem)
            {
                changeCallback(selectedThemedItem.Text, comboBox.SelectedIndex);
            }
        };

        return comboBox;
    }

    public WinForms.CheckedListBox AddMultiSelection(string name, Action<WinForms.CheckedListBox>? initializeCallback, Action<IEnumerable<string>> changeCallback)
        => AddMultiSelectionControl(name, initializeCallback, changeCallback).CheckedListBox;

    public GLViewerMultiSelectionControl AddMultiSelectionControl(string name, Action<WinForms.CheckedListBox>? initializeCallback, Action<IEnumerable<string>> changeCallback)
    {
        var selectionControl = new GLViewerMultiSelectionControl(name);
        var listBox = selectionControl.CheckedListBox;

        initializeCallback?.Invoke(listBox);

        AddControl(selectionControl);

        listBox.ItemCheck += (_, e) =>
        {
            // Manually calculate the new checked items since ItemCheck is called before CheckedItems is updated
            if (listBox.Items[e.Index] is string changedItem)
            {
                var checkedItems = listBox.CheckedItems.OfType<string>().ToHashSet();

                if (e.NewValue == WinForms.CheckState.Checked)
                {
                    checkedItems.Add(changedItem);
                }
                else if (e.NewValue == WinForms.CheckState.Unchecked)
                {
                    checkedItems.Remove(changedItem);
                }

                changeCallback(checkedItems);
            }
        };

        return selectionControl;
    }

    public GLViewerSliderControl AddTrackBar(Action<float> changeCallback, float defaultValue = 0f)
    {
        var trackBar = new GLViewerSliderControl();
        trackBar.Slider.Value = defaultValue;
        trackBar.Slider.ValueChanged = changeCallback;

        AddControl(trackBar);

        return trackBar;
    }

    public static Control CreateFloatInput(string name, Action<float> onValChanged, float startValue = 0, float minValue = 0, float maxValue = 1000)
    {
        var numeric = new NumericUpDown
        {
            Minimum = (decimal)minValue,
            Maximum = (decimal)maxValue,
            Value = (decimal)Math.Clamp(startValue, minValue, maxValue),
            Increment = 0.1m,
            FormatString = "0.###",
            Width = 110,
        };

        numeric.ValueChanged += (_, e) =>
        {
            if (e.NewValue is { } value)
            {
                onValChanged((float)value);
            }
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(numeric, 1);
        grid.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
        grid.Children.Add(numeric);
        return grid;
    }

    public ControlGroup BeginGroup(string title)
    {
        if (!namedGroups.TryGetValue(title, out var content))
        {
            content = new StackPanel();
            content.Classes.Add("sidebar");

            var header = new TextBlock { Text = title };
            header.Classes.Add("groupHeader");

            controlsPanel.Children.Add(new StackPanel
            {
                Classes = { "sidebar" },
                Children =
                {
                    header,
                    new Border
                    {
                        BorderThickness = new(1, 0, 0, 0),
                        BorderBrush = new SolidColorBrush(Colors.Gray, 0.5),
                        Padding = new(6, 0, 0, 0),
                        Child = content,
                    },
                },
            });

            namedGroups[title] = content;
        }

        currentControlsTarget = content;
        return new ControlGroup(this);
    }

    public ref struct ControlGroup(RendererControl? owner)
    {
        public void Dispose()
        {
            owner?.currentControlsTarget = null;
            owner = null;
        }
    }

    public void AddDivider()
    {
        ControlsPanel.Children.Add(new Border
        {
            Height = 1,
            Margin = new(0, 8),
            Background = new SolidColorBrush(Colors.Gray, 0.5),
        });
    }

    public void SetMoveSpeed(string text) => moveSpeed.Text = text;

    public void UseWideSplitter()
    {
        // Do not change the splitter distance if the controls got swapped for preview
        if (isPreview)
        {
            return;
        }

        ColumnDefinitions[0].Width = new GridLength(450);
    }

    public void HideSidebar()
    {
        controlsHost.IsVisible = false;
        splitter.IsVisible = false;
        ColumnDefinitions[isPreview ? 2 : 0].Width = new GridLength(0);
    }

    /// <summary>Avalonia layout is already in device independent units, so this is the identity.</summary>
#pragma warning disable CA1822 // Shared code calls it on the instance like the WinForms extension
    public int AdjustForDPI(float value) => (int)value;
#pragma warning restore CA1822
}
