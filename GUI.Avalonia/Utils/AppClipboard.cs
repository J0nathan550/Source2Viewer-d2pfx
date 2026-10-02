using System.IO;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;

namespace GUI.Utils;

public static class AppClipboard
{
    private static IClipboard? Clipboard => AppMessageDialogs.GetOwner()?.Clipboard;

    public static void SetText(string text)
    {
        DispatcherHelpers.WaitOnUIThread(async () =>
        {
            if (Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text).ConfigureAwait(true);
            }
        });
    }

    public static string GetText()
    {
        return DispatcherHelpers.WaitOnUIThread(async () =>
        {
            if (Clipboard is not { } clipboard)
            {
                return string.Empty;
            }

            return await clipboard.TryGetTextAsync().ConfigureAwait(true) ?? string.Empty;
        });
    }

    public static void SetImage(SkiaSharp.SKBitmap bitmap)
    {
        using var pngStream = new MemoryStream();
        using (var pixels = bitmap.PeekPixels())
        {
            pixels.Encode(pngStream, new SkiaSharp.SKPngEncoderOptions(SkiaSharp.SKPngEncoderFilterFlags.Sub, zLibLevel: 1));
        }

        pngStream.Position = 0;

        DispatcherHelpers.WaitOnUIThread(async () =>
        {
            if (Clipboard is not { } clipboard)
            {
                return;
            }

            // Some platforms render clipboard data lazily, so the bitmap is left for the GC instead of disposed here
            var image = new Bitmap(pngStream);
            await clipboard.SetBitmapAsync(image).ConfigureAwait(true);
        });
    }
}
