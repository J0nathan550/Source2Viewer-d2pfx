using System.Windows.Forms;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using AvaloniaControls = Avalonia.Controls;

namespace GUI.Controls;

/// <summary>A combo box entry; headers are shown as non-selectable group titles.</summary>
public class ThemedComboBoxItem
{
    public string Text { get; set; } = string.Empty;
    public bool IsHeader { get; set; }

    public override string ToString() => Text;
}

/// <summary>
/// A 0 to 1 slider. Like the WinForms one, <see cref="ValueChanged"/> only reports user interaction,
/// setting <see cref="Value"/> from code does not raise it.
/// </summary>
internal sealed class Slider : System.Windows.Forms.Control
{
    private readonly AvaloniaControls.Slider slider;
    private bool settingValue;

    public Slider() : this(new AvaloniaControls.Slider
    {
        Minimum = 0,
        Maximum = 1,
        SmallChange = 0.01,
        LargeChange = 0.1,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    })
    {
    }

    private Slider(AvaloniaControls.Slider slider) : base(slider)
    {
        this.slider = slider;

        slider.ValueChanged += (_, e) =>
        {
            if (!settingValue)
            {
                ValueChanged?.Invoke((float)e.NewValue);
            }
        };

        // The thumb handles pointer presses itself, so listen to handled events as well
        slider.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            Clicked = true;
            MouseDown?.Invoke(this, new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        slider.AddHandler(InputElement.PointerReleasedEvent, (_, e) =>
        {
            Clicked = false;
            MouseUp?.Invoke(this, new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    public Action<float>? ValueChanged;

    public event MouseEventHandler? MouseDown;
    public event MouseEventHandler? MouseUp;

    public bool Clicked { get; private set; }

    public float Value
    {
        get => OnUIThread(() => (float)slider.Value);
        set
        {
            var clamped = Math.Clamp(value, 0f, 1f);

            SetOnUIThread(() =>
            {
                settingValue = true;

                try
                {
                    slider.Value = clamped;
                }
                finally
                {
                    settingValue = false;
                }
            });
        }
    }
}

internal sealed class GLViewerSliderControl : System.Windows.Forms.Control
{
    public GLViewerSliderControl() : this(new Slider())
    {
    }

    private GLViewerSliderControl(Slider slider) : base(slider.Native)
    {
        Slider = slider;
    }

    public Slider Slider { get; }
}

internal sealed class GLViewerMultiSelectionControl : System.Windows.Forms.Control
{
    public GLViewerMultiSelectionControl(string name) : this(name, new CheckedListBox())
    {
    }

    private GLViewerMultiSelectionControl(string name, CheckedListBox listBox) : base(new AvaloniaControls.StackPanel
    {
        Spacing = 2,
        Children =
        {
            new AvaloniaControls.TextBlock { Text = name },
            new AvaloniaControls.Border
            {
                BorderThickness = new(1),
                BorderBrush = Avalonia.Media.Brushes.Gray,
                CornerRadius = new(3),
                Padding = new(2),
                Child = listBox.Native,
            },
        },
    })
    {
        CheckedListBox = listBox;
    }

    public CheckedListBox CheckedListBox { get; }
}

internal sealed class GLViewerSelectionControl : System.Windows.Forms.Control
{
    /// <param name="fill">Kept for the WinForms signature, the combo box always stretches to the sidebar width.</param>
    public GLViewerSelectionControl(string name, bool horizontal, bool fill = false) : this(name, horizontal, new System.Windows.Forms.ComboBox())
    {
    }

    private GLViewerSelectionControl(string name, bool horizontal, System.Windows.Forms.ComboBox comboBox) : base(CreateLayout(name, horizontal, comboBox.Native))
    {
        ComboBox = comboBox;
    }

    public System.Windows.Forms.ComboBox ComboBox { get; }

    private static AvaloniaControls.Control CreateLayout(string name, bool horizontal, AvaloniaControls.Control comboBox)
    {
        var label = new AvaloniaControls.TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };

        if (!horizontal)
        {
            return new AvaloniaControls.StackPanel { Spacing = 2, Children = { label, comboBox } };
        }

        var grid = new AvaloniaControls.Grid { ColumnDefinitions = new AvaloniaControls.ColumnDefinitions("Auto,*") };
        label.Margin = new(0, 0, 8, 0);
        AvaloniaControls.Grid.SetColumn(comboBox, 1);
        grid.Children.Add(label);
        grid.Children.Add(comboBox);
        return grid;
    }
}

/// <summary>A labelled checkbox row, for viewers that create and place checkboxes themselves.</summary>
internal sealed class GLViewerCheckboxControl : System.Windows.Forms.Control
{
    public GLViewerCheckboxControl(string name, bool isChecked) : this(new System.Windows.Forms.CheckBox())
    {
        CheckBox.Text = name;
        CheckBox.Checked = isChecked;
    }

    private GLViewerCheckboxControl(System.Windows.Forms.CheckBox checkBox) : base(checkBox.Native)
    {
        CheckBox = checkBox;
    }

    public System.Windows.Forms.CheckBox CheckBox { get; }
}
