using System.IO;
using System.Runtime.InteropServices;
using SkiaSharp;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Exporter.WeaponSkins
{
    /// <summary>
    /// A decoded texture that can be sampled with filtering, kept as floats for HDR images and as bytes otherwise.
    /// </summary>
    sealed class TextureSampler
    {
        private readonly float[]? hdr;
        private readonly byte[]? ldr;
        private readonly bool isBgra;

        public int Width { get; }
        public int Height { get; }

        private TextureSampler(SKBitmap bitmap)
        {
            Width = bitmap.Width;
            Height = bitmap.Height;

            if (bitmap.ColorType == SKColorType.RgbaF32)
            {
                hdr = MemoryMarshal.Cast<byte, float>(bitmap.GetPixelSpan()).ToArray();
                return;
            }

            using var converted = bitmap.ColorType is SKColorType.Bgra8888 or SKColorType.Rgba8888 ? null : bitmap.Copy(SKColorType.Rgba8888);
            var source = converted ?? bitmap;

            ldr = source.GetPixelSpan().ToArray();
            isBgra = source.ColorType == SKColorType.Bgra8888;
        }

        /// <summary>
        /// Loads a texture, decoded from its smallest mip level that is still at least <paramref name="minimumSize"/> wide.
        /// </summary>
        public static TextureSampler Load(IFileLoader fileLoader, string path, int minimumSize)
        {
            using var resource = fileLoader.LoadFileCompiled(path) ?? throw new FileNotFoundException($"\"{path}\" was not found");

            if (resource.DataBlock is not Texture texture)
            {
                throw new InvalidDataException($"\"{path}\" is not a texture");
            }

            var mipLevel = 0u;

            while (mipLevel + 1 < texture.NumMipLevels && Math.Max(1, texture.Width >> (int)(mipLevel + 1)) >= minimumSize)
            {
                mipLevel++;
            }

            using var bitmap = texture.GenerateBitmap(mipLevel: mipLevel);

            return new TextureSampler(bitmap);
        }

        /// <summary>
        /// Samples the texture at a texture coordinate with bilinear filtering, repeating it outside of 0 to 1.
        /// </summary>
        public Vector4 Sample(float u, float v)
        {
            var x = (u - MathF.Floor(u)) * Width - 0.5f;
            var y = (v - MathF.Floor(v)) * Height - 0.5f;

            var x0 = (int)MathF.Floor(x);
            var y0 = (int)MathF.Floor(y);
            var fx = x - x0;
            var fy = y - y0;

            var a = Fetch(x0, y0);
            var b = Fetch(x0 + 1, y0);
            var c = Fetch(x0, y0 + 1);
            var d = Fetch(x0 + 1, y0 + 1);

            return Vector4.Lerp(Vector4.Lerp(a, b, fx), Vector4.Lerp(c, d, fx), fy);
        }

        /// <summary>
        /// Reads the texel nearest to a texture coordinate, for data that must not be blended, like positions at UV seams.
        /// </summary>
        public Vector4 SampleNearest(float u, float v)
            => Fetch((int)((u - MathF.Floor(u)) * Width), (int)((v - MathF.Floor(v)) * Height));

        private Vector4 Fetch(int x, int y)
        {
            x = ((x % Width) + Width) % Width;
            y = ((y % Height) + Height) % Height;

            var index = ((y * Width) + x) * 4;

            if (hdr != null)
            {
                return new Vector4(hdr[index], hdr[index + 1], hdr[index + 2], hdr[index + 3]);
            }

            const float Scale = 1f / 255f;
            var pixels = ldr!;

            return isBgra
                ? new Vector4(pixels[index + 2] * Scale, pixels[index + 1] * Scale, pixels[index] * Scale, pixels[index + 3] * Scale)
                : new Vector4(pixels[index] * Scale, pixels[index + 1] * Scale, pixels[index + 2] * Scale, pixels[index + 3] * Scale);
        }
    }
}
