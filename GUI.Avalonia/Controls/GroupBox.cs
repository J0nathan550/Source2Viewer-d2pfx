using Avalonia.Controls;

namespace GUI.Controls
{
    /// <summary>
    /// A titled, bordered section like the WinForms ThemedGroupBox.
    /// </summary>
    static class GroupBox
    {
        public static Border Create(string title, Control content)
        {
            var header = new TextBlock { Text = title };
            header.Classes.Add("groupHeader");

            var panel = new DockPanel();
            DockPanel.SetDock(header, Dock.Top);
            panel.Children.Add(header);
            panel.Children.Add(content);

            var border = new Border { Child = panel };
            border.Classes.Add("groupBox");
            return border;
        }
    }
}
