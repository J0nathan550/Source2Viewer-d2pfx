using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Exporter.WeaponSkins
{
    /// <summary>
    /// A paint kit baked into textures for a weapon's own material.
    /// </summary>
    sealed class BakedWeaponSkin : IDisposable
    {
        /// <summary>The painted color, for the material's color texture.</summary>
        public required SKBitmap Color { get; init; }

        /// <summary>The painted roughness.</summary>
        public required SKBitmap Roughness { get; init; }

        /// <summary>The painted metalness.</summary>
        public required SKBitmap Metalness { get; init; }

        /// <summary>The paint's own normal map, which replaces the weapon's, or null to keep the weapon's.</summary>
        public string? Normal { get; init; }

        /// <summary>The paint's own ambient occlusion, which replaces the weapon's, or null to keep the weapon's.</summary>
        public string? AmbientOcclusion { get; init; }

        public void Dispose()
        {
            Color.Dispose();
            Roughness.Dispose();
            Metalness.Dispose();
        }
    }

    /// <summary>
    /// Paints a weapon's textures with a paint kit on the CPU, the way the game composites them when it shows a weapon
    /// with a finish. This is an approximation: patterns, colors, masks and wear are applied, but not the finer
    /// shading of each style, such as pearlescence or the exact way paint chips off.
    /// </summary>
    static class WeaponSkinBaker
    {
        // Projected patterns are sized for a weapon this long, longer ones get them larger
        private const float ReferenceWeaponLength = 36f;

        private const float DefaultPaintRoughness = 0.6f;

        public static BakedWeaponSkin Bake(IFileLoader fileLoader, WeaponDefinition weapon, PaintKitMaterial paint, Material weaponMaterial,
            Material? compositeInputs, float wear, CancellationToken cancellationToken)
        {
            var size = Math.Clamp(weapon.TextureSize, 256, 4096);

            TextureSampler? Load(string? path) => path != null ? TextureSampler.Load(fileLoader, path, size) : null;

            string? Texture(Material? material, string name) => material?.TextureParams.GetValueOrDefault(name);

            var baseColor = Load(Texture(weaponMaterial, "g_tColor"));
            var baseRoughnessMetalness = Load(Texture(weaponMaterial, "g_tMetalness"));

            var style = paint.Style;
            var isProjected = style is PaintStyle.Hydrographic or PaintStyle.SprayPaint or PaintStyle.AnodizedMulticolored;
            var position = isProjected ? Load(Texture(compositeInputs, "g_tPosition")) : null;
            var surface = isProjected ? Load(Texture(compositeInputs, "g_tSurface")) : null;

            // Finishes that bring their own masks paint all of the weapon, the masks only split it into color regions
            var ownMasks = paint.GetBool("g_bOverrideDefaultMasks") ? paint.GetTexture("g_tPaintByNumberMasks") : null;
            var masks = Load(ownMasks ?? Texture(compositeInputs, "g_tMasks"));

            var pattern = style is PaintStyle.SolidColor or PaintStyle.Anodized ? null : Load(paint.GetTexture("g_tPattern"));
            var paintRoughness = paint.GetBool("g_bUseRoughness", paint.GetBool("F_ROUGHNESS_TEXTURE")) ? Load(paint.GetTexture("g_tPaintRoughness")) : null;
            var wearTexture = wear > 0f ? Load(paint.GetTexture("g_tWear")) : null;
            var grunge = wear > 0f ? Load(paint.GetTexture("g_tGrunge")) : null;

            cancellationToken.ThrowIfCancellationRequested();

            var colors = new[]
            {
                AsColor(paint.GetVector("g_vColor0", Vector4.One)),
                AsColor(paint.GetVector("g_vColor1", Vector4.One)),
                AsColor(paint.GetVector("g_vColor2", Vector4.One)),
                AsColor(paint.GetVector("g_vColor3", Vector4.One)),
            };

            var brightness = paint.GetFloat("g_flColorBrightness", 1f);
            var roughness = paint.GetFloat("g_flPaintRoughness", DefaultPaintRoughness);
            var isMetallic = style is PaintStyle.Anodized or PaintStyle.AnodizedMulticolored or PaintStyle.AnodizedAirbrushed or PaintStyle.Patina;

            var patternScale = paint.GetFloat("g_flPatternTexCoordScale", 1f);

            if (isProjected && !paint.GetBool("g_bIgnoreWeaponSizeScale"))
            {
                patternScale *= weapon.WeaponLength / ReferenceWeaponLength;
            }

            var patternTransform = new UvTransform(
                patternScale,
                paint.GetFloat("g_flPatternTexCoordRotation"),
                AsVector2(paint.GetVector("g_vPatternTexCoordOffset")));
            var wearTransform = new UvTransform(
                paint.GetFloat("g_flWearTexCoordScale", 1f),
                paint.GetFloat("g_flWearTexCoordRotation"),
                AsVector2(paint.GetVector("g_vWearTexCoordOffset")));
            var grungeTransform = new UvTransform(
                paint.GetFloat("g_flGrungeTexCoordScale", 1f),
                paint.GetFloat("g_flGrungeTexCoordRotation"),
                AsVector2(paint.GetVector("g_vGrungeTexCoordOffset")));

            var colorPixels = new byte[size * size * 4];
            var roughnessPixels = new byte[size * size];
            var metalnessPixels = new byte[size * size];

            Parallel.For(0, size, new ParallelOptions { CancellationToken = cancellationToken }, y =>
            {
                var v = (y + 0.5f) / size;

                for (var x = 0; x < size; x++)
                {
                    var u = (x + 0.5f) / size;

                    var baseSample = baseColor?.Sample(u, v) ?? Vector4.One;
                    var baseRm = baseRoughnessMetalness?.Sample(u, v) ?? new Vector4(0.5f, 0f, 0f, 1f);
                    var mask = masks?.Sample(u, v) ?? new Vector4(1f, 0f, 0f, 0f);

                    var patternUv = patternTransform.Apply(isProjected && position != null
                        ? Project(position.SampleNearest(u, v), surface?.SampleNearest(u, v))
                        : new Vector2(u, v));

                    var patternSample = pattern?.Sample(patternUv.X, patternUv.Y) ?? Vector4.One;

                    Vector3 color;
                    float coverage;
                    var paintMetal = isMetallic ? 1f : 0f;

                    switch (style)
                    {
                        case PaintStyle.SolidColor or PaintStyle.Anodized:
                            color = PaintByNumber(colors, mask);
                            coverage = 1f;
                            break;

                        case PaintStyle.Hydrographic or PaintStyle.SprayPaint or PaintStyle.AnodizedMulticolored:
                            color = Layer(colors, patternSample);
                            coverage = 1f;
                            break;

                        case PaintStyle.Gunsmith when ownMasks != null:
                            // Metal parts stay metal, the pattern already shows them
                            color = AsColor(patternSample);
                            coverage = 1f;
                            paintMetal = baseRm.Y;
                            break;

                        case PaintStyle.Gunsmith:
                            color = AsColor(patternSample);
                            coverage = mask.X;
                            paintMetal = patternSample.W;
                            break;

                        default:
                            // Patterns laid out in the weapon's texture space, which leave its unmasked parts bare
                            color = AsColor(patternSample);
                            coverage = mask.X;
                            break;
                    }

                    color *= brightness;

                    var paintRough = paintRoughness?.Sample(patternUv.X, patternUv.Y).X ?? roughness;

                    if (wearTexture != null)
                    {
                        var wearUv = wearTransform.Apply(new Vector2(u, v));
                        var wearSample = wearTexture.Sample(wearUv.X, wearUv.Y).X;

                        // Paint comes off where the wear texture is below the wear amount, with a soft edge
                        coverage *= Math.Clamp((wearSample - wear) / 0.05f + 0.5f, 0f, 1f);
                    }

                    if (grunge != null)
                    {
                        var grungeUv = grungeTransform.Apply(new Vector2(u, v));
                        color *= Vector3.Lerp(Vector3.One, AsColor(grunge.Sample(grungeUv.X, grungeUv.Y)), wear);
                    }

                    var final = Vector3.Lerp(AsColor(baseSample), color, coverage);
                    var finalRoughness = float.Lerp(baseRm.X, paintRough, coverage);
                    var finalMetalness = float.Lerp(baseRm.Y, paintMetal, coverage);

                    var index = (y * size) + x;
                    colorPixels[index * 4] = ToByte(final.X);
                    colorPixels[(index * 4) + 1] = ToByte(final.Y);
                    colorPixels[(index * 4) + 2] = ToByte(final.Z);
                    colorPixels[(index * 4) + 3] = 255;

                    roughnessPixels[index] = ToByte(finalRoughness);
                    metalnessPixels[index] = ToByte(finalMetalness);
                }
            });

            return new BakedWeaponSkin
            {
                Color = ToBitmap(colorPixels, size, SKColorType.Rgba8888),
                Roughness = ToBitmap(roughnessPixels, size, SKColorType.Gray8),
                Metalness = ToBitmap(metalnessPixels, size, SKColorType.Gray8),
                Normal = paint.GetBool("g_bUseNormalMap", defaultValue: true) ? paint.GetTexture("g_tNormal") : null,
                AmbientOcclusion = paint.GetBool("g_bOverrideAmbientOcclusion") ? paint.GetTexture("g_tFinalAmbientOcclusion") : null,
            };
        }

        /// <summary>
        /// Where a projected pattern lands on a texel: its object space position seen along the axis its surface faces
        /// most, so no side of the weapon gets the pattern smeared across it. Positions span the weapon from 0 to 1,
        /// along its width, barrel and height.
        /// </summary>
        private static Vector2 Project(Vector4 position, Vector4? surfaceNormal)
        {
            var normal = surfaceNormal is { } encoded ? Vector3.Abs((AsColor(encoded) * 2f) - Vector3.One) : Vector3.UnitX;

            if (normal.X >= normal.Y && normal.X >= normal.Z)
            {
                return new Vector2(position.Y, 1f - position.Z);
            }

            return normal.Z >= normal.Y
                ? new Vector2(position.Y, position.X)
                : new Vector2(position.X, 1f - position.Z);
        }

        /// <summary>
        /// Picks a color by the weapon's masks: the first where none is set, the others where red, green and blue are.
        /// </summary>
        private static Vector3 PaintByNumber(Vector3[] colors, Vector4 mask)
        {
            var color = colors[0];
            color = Vector3.Lerp(color, colors[1], mask.X);
            color = Vector3.Lerp(color, colors[2], mask.Y);
            color = Vector3.Lerp(color, colors[3], mask.Z);

            return color;
        }

        /// <summary>
        /// Paints the four colors over each other, the pattern's channels masking the last three.
        /// </summary>
        private static Vector3 Layer(Vector3[] colors, Vector4 pattern)
        {
            var color = colors[0];
            color = Vector3.Lerp(color, colors[1], pattern.X);
            color = Vector3.Lerp(color, colors[2], pattern.Y);
            color = Vector3.Lerp(color, colors[3], pattern.Z);

            return color;
        }

        private static Vector3 AsColor(Vector4 value) => new(value.X, value.Y, value.Z);

        private static Vector2 AsVector2(Vector4 value) => new(value.X, value.Y);

        private static byte ToByte(float value) => (byte)Math.Clamp(MathF.Round(value * 255f), 0f, 255f);

        private static SKBitmap ToBitmap(byte[] pixels, int size, SKColorType colorType)
        {
            var bitmap = new SKBitmap(size, size, colorType, colorType == SKColorType.Gray8 ? SKAlphaType.Opaque : SKAlphaType.Unpremul);
            Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);

            return bitmap;
        }

        /// <summary>
        /// Scales and rotates texture coordinates around the texture's center, then offsets them.
        /// </summary>
        private readonly struct UvTransform(float scale, float rotationDegrees, Vector2 offset)
        {
            private readonly float cos = MathF.Cos(float.DegreesToRadians(rotationDegrees)) * scale;
            private readonly float sin = MathF.Sin(float.DegreesToRadians(rotationDegrees)) * scale;

            public Vector2 Apply(Vector2 uv)
            {
                var centered = uv - new Vector2(0.5f);

                return new Vector2(
                    (centered.X * cos) - (centered.Y * sin) + 0.5f + offset.X,
                    (centered.X * sin) + (centered.Y * cos) + 0.5f + offset.Y);
            }
        }
    }
}
