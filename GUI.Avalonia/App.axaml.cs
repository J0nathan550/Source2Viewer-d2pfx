using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using GUI.Utils;

namespace GUI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Themer.InitializeTheme();

        // The WinForms GUI uses Segoe UI at 10pt, the theme defaults to the heavier Inter at 14px
        Resources["ContentControlThemeFontFamily"] = OperatingSystem.IsWindows() ? new FontFamily("Segoe UI") : FontFamily.Default;
        Resources["ControlContentThemeFontSize"] = 13d;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow();
            Program.MainForm = mainWindow;
            desktop.MainWindow = mainWindow;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
