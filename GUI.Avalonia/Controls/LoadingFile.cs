using System.IO;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace GUI.Controls;

sealed class LoadingFile : StackPanel
{
    private readonly TextBlock status;

    public LoadingFile(string fileName)
    {
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        Spacing = 10;

        Children.Add(new TextBlock
        {
            Text = $"Loading {Path.GetFileName(fileName)}",
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        Children.Add(new ProgressBar
        {
            IsIndeterminate = true,
            Width = 280,
        });

        status = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0.7,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Children.Add(status);
    }

    public void SetStatus(string text) => status.Text = text;
}
