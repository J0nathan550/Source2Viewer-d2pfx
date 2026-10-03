using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Drawing;
using SkiaSharp;

namespace GUI.Types.Exporter.CharacterAssets
{
    public static class VmatTextureRecolorer
    {
        /// <summary>
        /// Перекрашивает 100% текстуры диффуза (Photoshop Black & White / Colorize) без исключений и артефактов.
        /// </summary>
        public static byte[] RecolorDiffuse(byte[] colorPngBytes, Color targetColor)
        {
            using var colorStream = new MemoryStream(colorPngBytes);
            using var colorBitmap = SKBitmap.Decode(colorStream);

            if (colorBitmap == null)
            {
                return colorPngBytes;
            }

            var info = colorBitmap.Info;
            using var resultBitmap = new SKBitmap(info);

            int width = colorBitmap.Width;
            int height = colorBitmap.Height;

            float tR = targetColor.R / 255f;
            float tG = targetColor.G / 255f;
            float tB = targetColor.B / 255f;

            float maxTarget = Math.Max(tR, Math.Max(tG, tB));
            float minTarget = Math.Min(tR, Math.Min(tG, tB));
            float targetSat = maxTarget == 0 ? 0 : (maxTarget - minTarget) / maxTarget;
            float targetHue = targetColor.GetHue();

            // Если выбран черный, серый или белый -> режим Ч/Б как на картинке 3
            bool isMonochrome = targetSat < 0.08f;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var origColor = colorBitmap.GetPixel(x, y);

                    // Прозрачные пиксели фона не трогаем
                    if (origColor.Alpha == 0)
                    {
                        resultBitmap.SetPixel(x, y, origColor);
                        continue;
                    }

                    // Чистая фотошоповская яркость пикселя (Luminance)
                    float lum = 0.299f * origColor.Red + 0.587f * origColor.Green + 0.114f * origColor.Blue;

                    SKColor finalColor;

                    if (isMonochrome)
                    {
                        // 1. ПОЛНЫЙ Ч/Б ЭФФЕКТ КАК НА КАРТИНКЕ 3 (Photoshop Grayscale)
                        byte gray = (byte)Math.Clamp(lum, 0, 255);
                        finalColor = new SKColor(gray, gray, gray, origColor.Alpha);
                    }
                    else
                    {
                        // 2. ЦВЕТНОЕ ТОНИРОВАНИЕ НА ВСЮ МОДЕЛЬ (Photoshop Tint / Colorize)
                        float normLum = lum / 255f;
                        float finalVal = Math.Clamp(normLum * (0.35f + 0.65f * maxTarget), 0.05f, 1.0f);
                        float finalSat = Math.Clamp(targetSat * 1.15f, 0.35f, 1.0f);

                        finalColor = HsvToSKColor(targetHue, finalSat, finalVal, origColor.Alpha);
                    }

                    resultBitmap.SetPixel(x, y, finalColor);
                }
            }

            using var image = SKImage.FromBitmap(resultBitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

        /// <summary>
        /// Перекрашивает .vmat файл и связанную с ним диффузную текстуру прямо на диске на месте.
        /// </summary>
        public static void RecolorVmatInPlace(string vmatPath, Color targetColor)
        {
            var fullVmatPath = Path.GetFullPath(vmatPath);
            if (!File.Exists(fullVmatPath))
            {
                return;
            }

            var vmatText = File.ReadAllText(fullVmatPath);
            var colorRelPath = FindColorTexturePath(vmatText);

            if (string.IsNullOrEmpty(colorRelPath))
            {
                return;
            }

            // Ищем текстуру цвета в той же папке или относительно корня
            var vmatDir = Path.GetDirectoryName(fullVmatPath)!;
            var textureName = Path.GetFileName(colorRelPath);
            var sameDirPng = Path.Combine(vmatDir, textureName);

            string? targetPngPath = null;
            if (File.Exists(sameDirPng))
            {
                targetPngPath = sameDirPng;
            }
            else
            {
                // Проверяем относительный путь от корня
                var rootGuess = fullVmatPath.Substring(0, fullVmatPath.IndexOf("materials", StringComparison.OrdinalIgnoreCase));
                var fullFromRoot = Path.Combine(rootGuess, colorRelPath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(fullFromRoot))
                {
                    targetPngPath = fullFromRoot;
                }
            }

            if (targetPngPath == null)
            {
                return;
            }

            // Перекрашиваем PNG файл на месте
            RecolorImageFileInPlace(targetPngPath, targetColor);
        }

        /// <summary>
        /// Перекрашивает любой PNG файл текстуры прямо на диске и перезаписывает его.
        /// </summary>
        public static void RecolorImageFileInPlace(string pngPath, Color targetColor)
        {
            var fullPath = Path.GetFullPath(pngPath);
            if (!File.Exists(fullPath))
            {
                return;
            }

            var bytes = File.ReadAllBytes(fullPath);
            var recolored = RecolorDiffuse(bytes, targetColor);
            File.WriteAllBytes(fullPath, recolored);
        }

        /// <summary>
        /// Загружает последний сохраненный цвет материалов из реестра Windows.
        /// </summary>
        public static Color LoadLastColor()
        {
            if (!OperatingSystem.IsWindows())
            {
                return Color.Red;
            }

            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Source2Viewer");
                if (key?.GetValue("CharacterLastColor") is int argb)
                {
                    return Color.FromArgb(argb);
                }
            }
            catch
            {
            }
            return Color.Red;
        }

        /// <summary>
        /// Сохраняет последний выбранный цвет материалов в реестр Windows.
        /// </summary>
        public static void SaveLastColor(Color color)
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Source2Viewer");
                key?.SetValue("CharacterLastColor", color.ToArgb(), Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch
            {
            }
        }

        private static SKColor HsvToSKColor(float hue, float sat, float val, byte alpha)
        {
            float c = val * sat;
            float x = c * (1 - Math.Abs((hue / 60f % 2) - 1));
            float m = val - c;

            float r = 0, g = 0, b = 0;
            if (hue < 60) { r = c; g = x; b = 0; }
            else if (hue < 120) { r = x; g = c; b = 0; }
            else if (hue < 180) { r = 0; g = c; b = x; }
            else if (hue < 240) { r = 0; g = x; b = c; }
            else if (hue < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }

            byte red = (byte)Math.Clamp((r + m) * 255f, 0, 255);
            byte green = (byte)Math.Clamp((g + m) * 255f, 0, 255);
            byte blue = (byte)Math.Clamp((b + m) * 255f, 0, 255);

            return new SKColor(red, green, blue, alpha);
        }

        public static string? FindColorTexturePath(string vmatText)
        {
            var match = Regex.Match(vmatText, @"""TextureColor""\s+""([^""]+\.(?:png|tga))""", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        public static string RedirectColorTextureInVmat(string vmatText, string newTexturePath)
        {
            return Regex.Replace(
                vmatText,
                @"""TextureColor""(\s+)""[^""]+""",
                $"\"TextureColor\"$1\"{newTexturePath}\"",
                RegexOptions.IgnoreCase);
        }
    }
}
