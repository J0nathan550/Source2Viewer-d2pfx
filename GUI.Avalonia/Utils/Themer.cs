using Avalonia;
using Avalonia.Styling;

namespace GUI.Utils
{
    public static class Themer
    {
        // This enum is used to store the setting of which theme the user has selected, keep consistent with the WinForms GUI.
        public enum AppTheme
        {
            System = 0,
            Light = 1,
            Dark = 2,
            Gray = 3,
        }

        /// <summary>
        /// The WinForms GUI's palette. The viewers use it for what they draw themselves, such as the texture
        /// checkerboard and node graphs, so those look the same in both front-ends.
        /// </summary>
        public sealed class ThemeColors
        {
            public required System.Drawing.Color App { get; init; }
            public required System.Drawing.Color AppMiddle { get; init; }
            public required System.Drawing.Color AppSoft { get; init; }
            public required System.Drawing.Color Border { get; init; }
            public required System.Drawing.Color Contrast { get; init; }
            public required System.Drawing.Color ContrastSoft { get; init; }
            public required System.Drawing.Color HoverAccent { get; init; }
            public required System.Drawing.Color Accent { get; init; }
            public required System.Drawing.Color Attention { get; init; }
        }

        private static readonly ThemeColors DarkTheme = new()
        {
            App = System.Drawing.Color.FromArgb(22, 25, 32),
            AppMiddle = System.Drawing.Color.FromArgb(34, 39, 51),
            AppSoft = System.Drawing.Color.FromArgb(44, 49, 61),
            Border = System.Drawing.Color.FromArgb(51, 57, 74),
            Contrast = System.Drawing.Color.White,
            ContrastSoft = System.Drawing.Color.FromArgb(158, 159, 164),
            HoverAccent = System.Drawing.Color.FromArgb(0, 66, 151),
            Accent = System.Drawing.Color.FromArgb(99, 161, 255),
            Attention = System.Drawing.Color.FromArgb(214, 55, 55),
        };

        private static readonly ThemeColors LightTheme = new()
        {
            App = System.Drawing.Color.FromArgb(218, 218, 218),
            AppMiddle = System.Drawing.Color.FromArgb(236, 236, 236),
            AppSoft = System.Drawing.Color.FromArgb(251, 251, 251),
            Border = System.Drawing.Color.FromArgb(188, 188, 188),
            Contrast = System.Drawing.Color.Black,
            ContrastSoft = System.Drawing.Color.FromArgb(80, 80, 80),
            HoverAccent = System.Drawing.Color.FromArgb(140, 191, 255),
            Accent = System.Drawing.Color.FromArgb(99, 161, 255),
            Attention = System.Drawing.Color.FromArgb(200, 40, 40),
        };

        private static readonly ThemeColors GrayTheme = new()
        {
            App = System.Drawing.Color.FromArgb(20, 20, 20),
            AppMiddle = System.Drawing.Color.FromArgb(32, 32, 32),
            AppSoft = System.Drawing.Color.FromArgb(45, 45, 45),
            Border = System.Drawing.Color.FromArgb(60, 60, 60),
            Contrast = System.Drawing.Color.White,
            ContrastSoft = System.Drawing.Color.FromArgb(160, 160, 160),
            HoverAccent = System.Drawing.Color.FromArgb(70, 70, 70),
            Accent = System.Drawing.Color.FromArgb(110, 110, 110),
            Attention = System.Drawing.Color.FromArgb(214, 55, 55),
        };

        public static AppTheme CurrentTheme { get; private set; } = AppTheme.Light;

        public static ThemeColors CurrentThemeColors => CurrentTheme switch
        {
            AppTheme.Dark => DarkTheme,
            AppTheme.Gray => GrayTheme,
            _ => LightTheme,
        };

        public static bool IsDarkModeEnabled => CurrentTheme != AppTheme.Light;

        public static event EventHandler? ThemeChanged;

        public static void InitializeTheme()
        {
            var theme = (AppTheme)Settings.Config.Theme;

            if (!Enum.IsDefined(theme))
            {
                theme = AppTheme.System;
            }

            ApplyTheme(theme);
        }

        public static void ApplyTheme(AppTheme theme)
        {
            if (Application.Current is not { } app)
            {
                return;
            }

            app.RequestedThemeVariant = theme switch
            {
                AppTheme.Light => ThemeVariant.Light,
                AppTheme.Dark or AppTheme.Gray => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };

            CurrentTheme = theme == AppTheme.System
                ? (app.ActualThemeVariant == ThemeVariant.Dark ? AppTheme.Dark : AppTheme.Light)
                : theme;

            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }
    }
}
