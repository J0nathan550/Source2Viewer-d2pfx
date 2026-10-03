using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

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
            public required System.Drawing.Color ControlBoxHighlight { get; init; }
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
            ControlBoxHighlight = System.Drawing.Color.FromArgb(67, 67, 67),
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
            ControlBoxHighlight = System.Drawing.Color.FromArgb(170, 170, 170),
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
            ControlBoxHighlight = System.Drawing.Color.FromArgb(67, 67, 67),
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

        /// <summary>Height of the main window's logo and menu bar, which is also its title bar.</summary>
        public const int MainTitleBarHeight = 48;

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

        /// <summary>Display name of a theme in menus and settings.</summary>
        public static string GetDisplayName(AppTheme theme) => theme switch
        {
            AppTheme.System => "System",
            AppTheme.Light => "Light",
            AppTheme.Dark => "Dark (Source 2 Viewer)",
            AppTheme.Gray => "Gray",
            _ => theme.ToString(),
        };

        public static AppTheme SelectedTheme { get; private set; } = AppTheme.System;

        private static bool FollowingSystem;

        public static void ApplyTheme(AppTheme theme)
        {
            if (Application.Current is not { } app)
            {
                return;
            }

            SelectedTheme = theme;

            if (!FollowingSystem)
            {
                // Re-resolves the System theme when the OS switches between light and dark
                app.ActualThemeVariantChanged += (_, _) =>
                {
                    if (SelectedTheme == AppTheme.System)
                    {
                        ApplyTheme(AppTheme.System);
                    }
                };
                FollowingSystem = true;
            }

            app.RequestedThemeVariant = theme switch
            {
                AppTheme.Light => ThemeVariant.Light,
                AppTheme.Dark or AppTheme.Gray => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };

            var resolved = theme == AppTheme.System
                ? (app.ActualThemeVariant == ThemeVariant.Dark ? AppTheme.Dark : AppTheme.Light)
                : theme;

            if (resolved == CurrentTheme && ThemeApplied)
            {
                return;
            }

            CurrentTheme = resolved;
            ThemeApplied = true;

            ApplyPalette(app, CurrentThemeColors, resolved == AppTheme.Light);

            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        private static bool ThemeApplied;

        /// <summary>A lighter color of the same hue, like WinForms ControlPaint.Light.</summary>
        private static System.Drawing.Color Lighter(System.Drawing.Color color, float amount)
        {
            var lightness = color.GetBrightness();
            return FromHsl(color.GetHue(), color.GetSaturation(), lightness + ((1 - lightness) * 0.5f * amount));
        }

        /// <summary>A darker color of the same hue, like WinForms ControlPaint.Dark.</summary>
        private static System.Drawing.Color Darker(System.Drawing.Color color, float amount)
        {
            return FromHsl(color.GetHue(), color.GetSaturation(), color.GetBrightness() * (2f / 3f) * (1 - amount));
        }

        private static System.Drawing.Color FromHsl(float hue, float saturation, float lightness)
        {
            lightness = Math.Clamp(lightness, 0, 1);

            var chroma = (1 - MathF.Abs((2 * lightness) - 1)) * saturation;
            var sector = hue / 60f;
            var x = chroma * (1 - MathF.Abs((sector % 2) - 1));

            var (r, g, b) = (int)sector switch
            {
                0 => (chroma, x, 0f),
                1 => (x, chroma, 0f),
                2 => (0f, chroma, x),
                3 => (0f, x, chroma),
                4 => (x, 0f, chroma),
                _ => (chroma, 0f, x),
            };

            var m = lightness - (chroma / 2);

            static int ToByte(float value) => (int)MathF.Round(Math.Clamp(value, 0, 1) * 255);

            return System.Drawing.Color.FromArgb(ToByte(r + m), ToByte(g + m), ToByte(b + m));
        }

        private static Color ToColor(System.Drawing.Color color, byte? alpha = null) => Color.FromArgb(alpha ?? color.A, color.R, color.G, color.B);

        /// <summary>A brush of a palette color, for controls that draw themselves.</summary>
        public static IImmutableSolidColorBrush GetBrush(System.Drawing.Color color, byte? alpha = null) => new ImmutableSolidColorBrush(ToColor(color, alpha));

        /// <summary>
        /// Recolors the Fluent theme with the WinForms palette, and publishes the palette as brushes
        /// (S2vApp, S2vAppMiddle, ...) for our own styles.
        /// </summary>
        private static void ApplyPalette(Application app, ThemeColors colors, bool light)
        {
            var fluent = app.Styles.OfType<FluentTheme>().FirstOrDefault();

            if (fluent != null)
            {
                var variant = light ? ThemeVariant.Light : ThemeVariant.Dark;

                var palette = new ColorPaletteResources();
                palette.Accent = ToColor(colors.Accent);
                palette.RegionColor = ToColor(colors.App);
                palette.ErrorText = ToColor(colors.Attention);

                palette.AltHigh = ToColor(colors.App);
                palette.AltMediumHigh = ToColor(colors.App, 0xCC);
                palette.AltMedium = ToColor(colors.App, 0x99);
                palette.AltMediumLow = ToColor(colors.App, 0x66);
                palette.AltLow = ToColor(colors.App, 0x33);

                palette.BaseHigh = ToColor(colors.Contrast);
                palette.BaseMediumHigh = ToColor(colors.Contrast, 0xCC);
                palette.BaseMedium = ToColor(colors.Contrast, 0x99);
                palette.BaseMediumLow = ToColor(colors.Contrast, 0x66);
                palette.BaseLow = ToColor(colors.Contrast, 0x33);

                palette.ChromeLow = ToColor(colors.App);
                palette.ChromeMediumLow = ToColor(colors.AppSoft);
                palette.ChromeMedium = ToColor(colors.AppMiddle);
                palette.ChromeHigh = ToColor(colors.Border);
                palette.ChromeGray = ToColor(colors.ContrastSoft);
                palette.ChromeAltLow = ToColor(colors.Contrast);
                palette.ChromeWhite = ToColor(colors.Contrast);
                palette.ChromeDisabledHigh = ToColor(colors.Border);
                palette.ChromeDisabledLow = ToColor(colors.ContrastSoft);

                palette.ListLow = ToColor(colors.HoverAccent, 0x66);
                palette.ListMedium = ToColor(colors.HoverAccent, 0xAA);

                // Replaced rather than edited so the theme picks up the new colors
                fluent.Palettes[variant] = palette;
            }

            var resources = app.Resources;

            void SetBrush(string key, Color color) => resources[key] = new SolidColorBrush(color);

            SetBrush("S2vAppBrush", ToColor(colors.App));
            SetBrush("S2vAppMiddleBrush", ToColor(colors.AppMiddle));
            SetBrush("S2vAppSoftBrush", ToColor(colors.AppSoft));
            SetBrush("S2vBorderBrush", ToColor(colors.Border));
            SetBrush("S2vContrastBrush", ToColor(colors.Contrast));
            SetBrush("S2vContrastSoftBrush", ToColor(colors.ContrastSoft));
            SetBrush("S2vHoverAccentBrush", ToColor(colors.HoverAccent));
            SetBrush("S2vAccentBrush", ToColor(colors.Accent));
            SetBrush("S2vAttentionBrush", ToColor(colors.Attention));

            // Buttons are drawn like the WinForms ThemedButton: filled with the border color and outlined a little
            // lighter, or a little darker in the light theme, and dimmed while disabled
            var disabledButton = light ? Lighter(colors.Border, 0.6f) : Darker(colors.Border, 0.2f);
            SetBrush("S2vButtonBrush", ToColor(colors.Border));
            SetBrush("S2vButtonBorderBrush", ToColor(light ? Darker(colors.Border, 0.01f) : Lighter(colors.Border, 0.4f)));
            SetBrush("S2vButtonDisabledBrush", ToColor(disabledButton));
            SetBrush("S2vButtonDisabledBorderBrush", ToColor(light ? Darker(disabledButton, 0.01f) : Lighter(disabledButton, 0.4f)));
            SetBrush("S2vButtonDisabledForegroundBrush", ToColor(light ? Lighter(colors.Contrast, 0.6f) : Darker(colors.Contrast, 0.2f)));

            // Tab headers look like the WinForms tab strip: flat, selected tab lifted to the page color
            SetBrush("TabItemHeaderBackgroundUnselected", ToColor(colors.App));
            SetBrush("TabItemHeaderBackgroundUnselectedPointerOver", ToColor(colors.AppSoft));
            SetBrush("TabItemHeaderBackgroundSelected", ToColor(colors.AppMiddle));
            SetBrush("TabItemHeaderBackgroundSelectedPointerOver", ToColor(colors.AppMiddle));
            SetBrush("TabItemHeaderSelectedPipeFill", ToColor(colors.Accent));
            SetBrush("TabItemHeaderForegroundUnselected", ToColor(colors.ContrastSoft));
            SetBrush("TabItemHeaderForegroundUnselectedPointerOver", ToColor(colors.Contrast));
            SetBrush("TabItemHeaderForegroundSelected", ToColor(colors.Contrast));

            SetBrush("MenuFlyoutPresenterBackground", ToColor(colors.AppMiddle));
            SetBrush("MenuFlyoutPresenterBorderBrush", ToColor(colors.Border));
            SetBrush("MenuFlyoutItemBackgroundPointerOver", ToColor(colors.HoverAccent));
            SetBrush("MenuFlyoutSubItemBackgroundPointerOver", ToColor(colors.HoverAccent));
            SetBrush("MenuFlyoutSubItemBackgroundSubMenuOpened", ToColor(colors.HoverAccent));

            // Window buttons drawn into the extended title bar, colored like the WinForms ControlsBoxPanel
            SetBrush("CaptionButtonBackground", ToColor(colors.ControlBoxHighlight));
            SetBrush("CaptionButtonBorderBrush", ToColor(colors.Border));
            SetBrush("CaptionButtonForeground", ToColor(colors.Contrast));

            SetBrush("TreeViewItemBackgroundPointerOver", ToColor(colors.HoverAccent, 0x66));
            SetBrush("TreeViewItemBackgroundSelected", ToColor(colors.HoverAccent));
            SetBrush("TreeViewItemBackgroundSelectedPointerOver", ToColor(colors.HoverAccent));
        }
    }
}
