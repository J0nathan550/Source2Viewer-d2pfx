using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using GUI.Utils;
using AvaloniaColor = Avalonia.Media.Color;
using DrawingColor = System.Drawing.Color;

namespace GUI.Forms
{
    /// <summary>
    /// Asks for an RGB color, in place of the WinForms ColorDialog.
    /// </summary>
    static class ColorPickerDialog
    {
        /// <returns>The picked color, or null when cancelled.</returns>
        public static async Task<DrawingColor?> ShowAsync(DrawingColor initialColor, string title = "Color")
        {
            if (AppMessageDialogs.GetOwner() is not { } owner)
            {
                return null;
            }

            DrawingColor? result = null;

            var colorView = new ColorView
            {
                Color = AvaloniaColor.FromRgb(initialColor.R, initialColor.G, initialColor.B),
                IsAlphaEnabled = false,
                IsAlphaVisible = false,
            };

            var window = new Window
            {
                Title = title,
                SizeToContent = SizeToContent.WidthAndHeight,
                CanResize = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };

            var okButton = new Button { Content = "OK", Width = 88, IsDefault = true };
            okButton.Click += (_, _) =>
            {
                var color = colorView.Color;
                result = DrawingColor.FromArgb(color.R, color.G, color.B);
                window.Close();
            };

            var cancelButton = new Button { Content = "Cancel", Width = 88, IsCancel = true };
            cancelButton.Click += (_, _) => window.Close();

            window.Content = new StackPanel
            {
                Margin = new(12),
                Spacing = 12,
                Children =
                {
                    colorView,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { okButton, cancelButton },
                    },
                },
            };

            await window.ShowDialog(owner).ConfigureAwait(true);

            return result;
        }
    }
}
