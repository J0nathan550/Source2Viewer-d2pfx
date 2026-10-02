using Avalonia.Controls;
using Avalonia.Media;

namespace GUI.Controls;

/// <summary>A titled, framed group of sidebar rows.</summary>
sealed class GLViewerGroupedSectionControl : StackPanel
{
    private readonly StackPanel rows = new() { Spacing = 2 };

    public GLViewerGroupedSectionControl(string title)
    {
        var header = new TextBlock { Text = title };
        header.Classes.Add("groupHeader");

        Children.Add(header);
        Children.Add(new Border
        {
            BorderThickness = new(1, 0, 0, 0),
            BorderBrush = new SolidColorBrush(Colors.Gray, 0.5),
            Padding = new(6, 0, 0, 0),
            Child = rows,
        });
    }

    public void AddRow(Control control) => rows.Children.Add(control);

    public void AddRow(System.Windows.Forms.Control control) => rows.Children.Add(control.Native);

    public void ClearRows() => rows.Children.Clear();
}
