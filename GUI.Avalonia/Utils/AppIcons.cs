using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using SkiaSharp;
using Svg.Skia;
using ValveResourceFormat.IO;

namespace GUI.Utils;

/// <summary>
/// The SVG icons of GUI/Icons/, rasterized for Avalonia. Icons with a "_light" variant use it in the light theme.
/// </summary>
static class AppIcons
{
    // Rasterized larger than shown so icons stay sharp on high DPI displays
    private const int DefaultRenderSize = 64;

    /// <summary>
    /// Size icons are shown at in menus, tabs, trees and lists, like the WinForms image list.
    /// </summary>
    public const double DefaultSize = 24;

    private const string ResourcePrefix = "GUI.Icons.";
    private const string AssetTypesPrefix = "AssetTypes.";
    private const string AliasesResource = "GUI.Icons.AssetTypes.aliases.txt";

    private static readonly Dictionary<(string Name, bool Light, int Size), Bitmap?> Cache = [];
    private static readonly Dictionary<string, string> ExtensionAliases = LoadAliases();
    private static readonly HashSet<string> Resources = [.. Program.Assembly.GetManifestResourceNames().Where(static r => r.StartsWith(ResourcePrefix, StringComparison.Ordinal))];

    /// <summary>
    /// Steam library icons by app id, loaded by the Explorer.
    /// </summary>
    public static ConcurrentDictionary<int, Bitmap> GameIcons { get; } = new();

    /// <summary>Gets an icon by its name in GUI/Icons/, e.g. "Folder" or "AssetTypes.vpk".</summary>
    public static Bitmap? Get(string name, int renderSize = DefaultRenderSize)
    {
        var light = !Themer.IsDarkModeEnabled;

        if (Cache.TryGetValue((name, light, renderSize), out var bitmap))
        {
            return bitmap;
        }

        bitmap = Load(name, light, renderSize);
        Cache[(name, light, renderSize)] = bitmap;
        return bitmap;
    }

    /// <summary>Raster size for an icon shown at <paramref name="displaySize"/>, in a few buckets to share the cache.</summary>
    public static int GetRenderSize(double displaySize) => displaySize switch
    {
        <= 32 => DefaultRenderSize,
        <= 64 => 128,
        <= 128 => 256,
        _ => 512,
    };

    /// <summary>Gets the icon name for a file extension, without the leading dot, falling back to "File".</summary>
    public static string GetExtensionIconName(string? extension)
    {
        if (string.IsNullOrEmpty(extension))
        {
            return "File";
        }

        extension = extension.TrimStart('.').ToLowerInvariant();

        if (extension.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
        {
            extension = extension[..^GameFileLoader.CompiledFileSuffix.Length];
        }

        if (TryResolveExtension(extension, out var name))
        {
            return name;
        }

        // vmdl -> mdl, vtex -> tex
        if (extension.Length > 1 && extension[0] == 'v' && TryResolveExtension(extension[1..], out name))
        {
            return name;
        }

        return "File";
    }

    /// <summary>Gets the icon name for the extension of <paramref name="fileName"/>.</summary>
    public static string GetFileIconName(string fileName) => GetExtensionIconName(Path.GetExtension(fileName));

    /// <summary>Creates an icon control that follows theme changes.</summary>
    public static ThemedIcon Create(string name, double size = DefaultSize) => new()
    {
        IconName = name,
        Width = size,
        Height = size,
    };

    /// <summary>Creates an icon control showing a fixed picture, such as a game icon.</summary>
    public static ThemedIcon Create(Bitmap image, double size = DefaultSize) => new()
    {
        FixedSource = image,
        Width = size,
        Height = size,
    };

    /// <summary>Creates a header with an icon in front of the text, for tabs, tree items and buttons.</summary>
    public static StackPanel CreateHeader(Control icon, string text) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        Children =
        {
            icon,
            new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center },
        },
    };

    /// <summary>Creates a header with an icon in front of the text, for tabs, tree items and buttons.</summary>
    public static StackPanel CreateHeader(string iconName, string text, double size = DefaultSize) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        Children =
        {
            Create(iconName, size),
            new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center },
        },
    };

    private static bool TryResolveExtension(string extension, out string name)
    {
        if (ExtensionAliases.TryGetValue(extension, out var alias))
        {
            extension = alias;
        }

        name = AssetTypesPrefix + extension;
        return Resources.Contains($"{ResourcePrefix}{name}.svg");
    }

    private static Bitmap? Load(string name, bool light, int renderSize)
    {
        var resource = $"{ResourcePrefix}{name}_light.svg";

        if (!light || !Resources.Contains(resource))
        {
            resource = $"{ResourcePrefix}{name}.svg";
        }

        if (!Resources.Contains(resource))
        {
            return null;
        }

        using var stream = Program.Assembly.GetManifestResourceStream(resource);

        if (stream == null)
        {
            return null;
        }

        using var svg = new SKSvg();
        svg.Load(stream);

        if (svg.Picture is not { } picture)
        {
            return null;
        }

        using var skBitmap = new SKBitmap(renderSize, renderSize, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(skBitmap))
        {
            canvas.Clear(SKColors.Transparent);

            var bounds = picture.CullRect;
            var scale = MathF.Min(renderSize / bounds.Width, renderSize / bounds.Height);
            canvas.Scale(scale);
            canvas.Translate(-bounds.Left, -bounds.Top);
            canvas.DrawPicture(picture);
            canvas.Flush();
        }

        using var image = SKImage.FromBitmap(skBitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        using var pngStream = png.AsStream();
        return new Bitmap(pngStream);
    }

    private static Dictionary<string, string> LoadAliases()
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);

        using var stream = Program.Assembly.GetManifestResourceStream(AliasesResource);

        if (stream == null)
        {
            return aliases;
        }

        using var reader = new StreamReader(stream);

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var space = line.IndexOf(' ', StringComparison.Ordinal);

            if (space > 0)
            {
                aliases.TryAdd(line[..space], line[(space + 1)..].Trim());
            }
        }

        return aliases;
    }
}

/// <summary>An <see cref="Image"/> showing one of <see cref="AppIcons"/>, updated when the theme changes.</summary>
sealed class ThemedIcon : Image
{
    private string? iconName;
    private Bitmap? fixedSource;

    protected override Type StyleKeyOverride => typeof(Image);

    public string? IconName
    {
        get => iconName;
        set
        {
            iconName = value;
            Refresh();
        }
    }

    /// <summary>A picture shown instead of a named icon, it does not change with the theme.</summary>
    public Bitmap? FixedSource
    {
        get => fixedSource;
        set
        {
            fixedSource = value;
            Refresh();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Themer.ThemeChanged += OnThemeChanged;
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Themer.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh() => Source = fixedSource ?? (iconName == null ? null : AppIcons.Get(iconName, AppIcons.GetRenderSize(double.IsNaN(Width) ? AppIcons.DefaultSize : Width)));
}
