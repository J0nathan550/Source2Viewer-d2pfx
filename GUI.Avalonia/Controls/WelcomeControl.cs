using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GUI.Utils;

namespace GUI.Controls
{
    /// <summary>
    /// First start page: an introduction on the left and the Explorer on the right.
    /// </summary>
    sealed class WelcomeControl : Grid
    {
        public WelcomeControl(ExplorerControl explorerControl)
        {
            ColumnDefinitions = new ColumnDefinitions("380,4,*");

            var intro = new StackPanel { Margin = new(16), Spacing = 12 };

            intro.Children.Add(GroupBox.Create("Welcome to Source 2 Viewer", Paragraph(
                "On the right, you'll find the Explorer panel. It automatically detects and displays all your installed and compatible Steam content.\n\n" +
                "It will also list your recently opened files and bookmarks. Right click a file to add it to bookmarks.\n\n" +
                "The Explorer opens by default when you launch Source 2 Viewer. For easy access anytime, simply click the \"Explorer\" button at the top of the screen.")));

            intro.Children.Add(GroupBox.Create("Check for updates", Paragraph(
                "Automatic update checks are enabled. Updates are checked daily by connecting to github.com. This can be changed in the About menu.")));

            if (OperatingSystem.IsWindows())
            {
                var fileAssociationButton = new Button
                {
                    Content = "Set Source 2 Viewer as the default program for .VPK files",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                fileAssociationButton.Click += async (_, _) =>
                {
                    if (await FileAssociation.RegisterAsync().ConfigureAwait(true))
                    {
                        fileAssociationButton.Content = "File association has been registered";
                    }
                };

                intro.Children.Add(GroupBox.Create("File association", fileAssociationButton));
            }

            var left = new ScrollViewer { Content = intro };
            left.Classes.Add("page");

            var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns };
            SetColumn(splitter, 1);
            SetColumn(explorerControl, 2);

            Children.Add(left);
            Children.Add(splitter);
            Children.Add(explorerControl);
        }

        private static TextBlock Paragraph(string text) => new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
        };
    }
}
