using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Rendering;
using GUI.Utils;
using ValveKeyValue;

namespace GUI.Controls
{
    internal sealed partial class CodeTextBox : TextEditor
    {
        public static FontFamily MonospaceFont { get; } = new("Cascadia Mono,Consolas,DejaVu Sans Mono,Liberation Mono,monospace");

        // Avalonia already uses this as the type key, but be explicit so restyling the subclass keeps the editor template
        protected override Type StyleKeyOverride => typeof(TextEditor);

        public CodeTextBox(string text, HighlightLanguage highlightSyntax = HighlightLanguage.KeyValues, IReadOnlyList<KvSourceSpan>? sourceMap = null)
        {
            IsReadOnly = true;
            ShowLineNumbers = true;
            FontFamily = MonospaceFont;
            FontSize = Settings.Config.TextViewerFontSize > 0 ? Settings.Config.TextViewerFontSize * 4 / 3.0 : 13;
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
            Options.EnableHyperlinks = false;
            Options.EnableEmailHyperlinks = false;
            Options.AllowScrollBelowDocument = false;

            Document = new TextDocument(text);

            var dark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

            if (sourceMap != null)
            {
                TextArea.TextView.LineTransformers.Add(new SourceMapColorizer(sourceMap, dark));
            }
            else if (highlightSyntax is HighlightLanguage.KeyValues or HighlightLanguage.Default)
            {
                TextArea.TextView.LineTransformers.Add(RegexColorizer.KeyValues(dark));
            }
            else if (highlightSyntax == HighlightLanguage.Shaders)
            {
                TextArea.TextView.LineTransformers.Add(RegexColorizer.Shader(dark));
            }
            else if (highlightSyntax == HighlightLanguage.CSS)
            {
                TextArea.TextView.LineTransformers.Add(RegexColorizer.Css(dark));
            }
            else if (highlightSyntax is HighlightLanguage.XML or HighlightLanguage.JS)
            {
                SyntaxHighlighting = HighlightingManager.Instance.GetDefinition(highlightSyntax == HighlightLanguage.XML ? "XML" : "JavaScript");
            }
        }

        public static TextEditor Create(string text, HighlightLanguage language = HighlightLanguage.KeyValues, IReadOnlyList<KvSourceSpan>? sourceMap = null)
        {
            return new CodeTextBox(text, language, sourceMap);
        }

        public static CodeTextBox CreateFromException(Exception exception, string? context = null)
        {
            var output = new StringBuilder(512);
            output.AppendLine("Unhandled exception occurred while trying to open this file:");
            output.AppendLine(exception.Message);
            output.AppendLine();

            output.AppendLine("Try using latest dev build to see if the issue persists.");
            output.AppendLine();

            if (context != null)
            {
                output.Append("Context: ");
                output.AppendLine(context);
                output.AppendLine();
            }

            Program.AppendExceptionWithVersion(output, exception);

            return new CodeTextBox(output.ToString(), HighlightLanguage.None)
            {
                WordWrap = true,
            };
        }

        private static ImmutableSolidColorBrush Brush(Color light, Color dark, bool isDark) => new(isDark ? dark : light);

        /// <summary>
        /// Colors KV3 text using the token spans the serializer produced, so highlighting is exact instead of regex based.
        /// Spans are sorted by start offset, which both serializer and parser produced maps guarantee.
        /// </summary>
        private sealed class SourceMapColorizer : DocumentColorizingTransformer
        {
            private readonly IReadOnlyList<KvSourceSpan> spans;
            private readonly IBrush?[] palette;
            private readonly bool[] italic;

            public SourceMapColorizer(IReadOnlyList<KvSourceSpan> spans, bool dark)
            {
                this.spans = spans;
                (palette, italic) = BuildPalette(dark);
            }

            protected override void ColorizeLine(DocumentLine line)
            {
                var lineStart = line.Offset;
                var lineEnd = line.EndOffset;

                for (var i = FirstSpanEndingAfter(lineStart); i < spans.Count; i++)
                {
                    var span = spans[i];

                    if (span.Start >= lineEnd)
                    {
                        break;
                    }

                    var index = (int)span.TokenType;

                    if ((uint)index >= (uint)palette.Length || palette[index] is not { } brush)
                    {
                        continue;
                    }

                    var start = Math.Max(span.Start, lineStart);
                    var end = Math.Min(span.End, lineEnd);

                    if (end <= start)
                    {
                        continue;
                    }

                    var isItalic = italic[index];

                    ChangeLinePart(start, end, element =>
                    {
                        element.TextRunProperties.SetForegroundBrush(brush);

                        if (isItalic)
                        {
                            element.TextRunProperties.SetTypeface(new Typeface(element.TextRunProperties.Typeface.FontFamily, FontStyle.Italic));
                        }
                    });
                }
            }

            private int FirstSpanEndingAfter(int offset)
            {
                // Spans do not overlap, so ends are sorted too
                int low = 0, high = spans.Count;

                while (low < high)
                {
                    var mid = (low + high) >>> 1;

                    if (spans[mid].End <= offset)
                    {
                        low = mid + 1;
                    }
                    else
                    {
                        high = mid;
                    }
                }

                return low;
            }

            // Roughly tracks the VS Code Dark+/Light+ palettes, matching the WinForms GUI.
            private static (IBrush?[] Palette, bool[] Italic) BuildPalette(bool dark)
            {
                var p = new IBrush?[(int)KVTokenType.BinaryBlob + 1];
                var it = new bool[p.Length];

                var brace = Brush(Colors.DimGray, Colors.Silver, dark);
                var comment = Brush(Colors.Green, Colors.MediumSeaGreen, dark);
                var inclusion = Brush(Colors.DarkGoldenrod, Colors.Goldenrod, dark);

                p[(int)KVTokenType.Key] = Brush(Colors.SteelBlue, Colors.LightSkyBlue, dark);
                p[(int)KVTokenType.String] = Brush(Colors.Firebrick, Colors.SandyBrown, dark);
                p[(int)KVTokenType.Identifier] = Brush(Colors.DarkGreen, Colors.PaleGreen, dark);
                p[(int)KVTokenType.Flag] = Brush(Colors.DarkGoldenrod, Colors.Goldenrod, dark);
                p[(int)KVTokenType.BinaryBlob] = Brush(Colors.SaddleBrown, Colors.DarkKhaki, dark);
                p[(int)KVTokenType.Condition] = Brush(Colors.DarkOrchid, Colors.Plum, dark);

                p[(int)KVTokenType.ObjectStart] = brace;
                p[(int)KVTokenType.ObjectEnd] = brace;
                p[(int)KVTokenType.ArrayStart] = brace;
                p[(int)KVTokenType.ArrayEnd] = brace;
                p[(int)KVTokenType.Assignment] = brace;
                p[(int)KVTokenType.Comma] = brace;

                p[(int)KVTokenType.Comment] = comment;
                p[(int)KVTokenType.CommentBlock] = comment;
                p[(int)KVTokenType.Header] = comment;

                p[(int)KVTokenType.IncludeAndAppend] = inclusion;
                p[(int)KVTokenType.IncludeAndMerge] = inclusion;

                it[(int)KVTokenType.Flag] = true;
                it[(int)KVTokenType.Condition] = true;
                it[(int)KVTokenType.Comment] = true;
                it[(int)KVTokenType.CommentBlock] = true;
                it[(int)KVTokenType.Header] = true;

                return (p, it);
            }
        }

        /// <summary>
        /// Line based regex highlighting, a port of the FastColoredTextBox highlighters. Rules are applied in order,
        /// later rules win where they overlap, same as FCTB style precedence.
        /// </summary>
        private sealed partial class RegexColorizer(params (Regex Regex, IBrush Brush, bool Bold, bool Italic)[] rules) : DocumentColorizingTransformer
        {
            protected override void ColorizeLine(DocumentLine line)
            {
                var text = CurrentContext.Document.GetText(line);

                foreach (var (regex, brush, bold, italic) in rules)
                {
                    foreach (var match in regex.EnumerateMatches(text))
                    {
                        if (match.Length == 0)
                        {
                            continue;
                        }

                        var start = line.Offset + match.Index;

                        ChangeLinePart(start, start + match.Length, element =>
                        {
                            element.TextRunProperties.SetForegroundBrush(brush);

                            if (bold || italic)
                            {
                                var typeface = element.TextRunProperties.Typeface;
                                element.TextRunProperties.SetTypeface(new Typeface(
                                    typeface.FontFamily,
                                    italic ? FontStyle.Italic : typeface.Style,
                                    bold ? FontWeight.Bold : typeface.Weight));
                            }
                        });
                    }
                }
            }

            private static ImmutableSolidColorBrush StringBrush(bool dark) => Brush(Colors.Blue, Colors.DeepSkyBlue, dark);
            private static ImmutableSolidColorBrush NumberBrush(bool dark) => Brush(Colors.Magenta, Colors.MediumPurple, dark);
            private static ImmutableSolidColorBrush CommentBrush(bool dark) => Brush(Colors.Green, Colors.YellowGreen, dark);
            private static ImmutableSolidColorBrush KeywordBrush(bool dark) => Brush(Colors.Blue, Colors.CornflowerBlue, dark);

            public static RegexColorizer KeyValues(bool dark) => new(
                (CommentRegex(), CommentBrush(dark), false, dark),
                (XmlCommentRegex(), CommentBrush(dark), false, dark),
                (StringRegex(), StringBrush(dark), false, false),
                (NumberRegex(), NumberBrush(dark), false, false));

            public static RegexColorizer Css(bool dark) => new(
                (StringRegex(), StringBrush(dark), false, false),
                (CssPropertyRegex(), KeywordBrush(dark), false, false),
                (CssCommentRegex(), CommentBrush(dark), false, dark));

            public static RegexColorizer Shader(bool dark) => new(
                (CommentRegex(), CommentBrush(dark), false, dark),
                (StringRegex(), Brush(Colors.Brown, Colors.DeepSkyBlue, dark), false, false),
                (WordRegex(), Brush(Colors.Blue, Colors.DeepSkyBlue, dark), false, false),
                (UnknownVarRegex(), Brush(Colors.Gray, Colors.Gray, dark), false, false),
                (NumberRegex(), NumberBrush(dark), false, false),
                (DirectiveRegex(), Brush(Colors.Goldenrod, Colors.Gold, dark), true, false),
                (KeywordRegex(), KeywordBrush(dark), false, false));

            [GeneratedRegex(@"^<!--.*-->\s*$")]
            private static partial Regex XmlCommentRegex();

            // Block comments spanning lines are only colored on lines that contain both ends
            [GeneratedRegex(@"/\*.*?\*/")]
            private static partial Regex CssCommentRegex();

            [GeneratedRegex("([a-z-]+(?:[a-z0-9-]*[a-z0-9]+)?)\\s*:", RegexOptions.IgnoreCase)]
            private static partial Regex CssPropertyRegex();

            [GeneratedRegex(@"(?:#[a-z]+|\b[A-Z][A-Z0-9_]+)\b")]
            private static partial Regex DirectiveRegex();

            [GeneratedRegex(@"\b(?:g|m|gl)_[A-Za-z0-9_]+\b")]
            private static partial Regex WordRegex();

            [GeneratedRegex(@"\b_[0-9]+\b")]
            private static partial Regex UnknownVarRegex();

            // This should be sorted alphabetically.
            [GeneratedRegex(@"\b(?:Allow[0-9]+|BoolAttribute|ByteAddressBuffer|ChildOf1|CreateInputTexture2D|DWORD|DynamicCombo|DynamicComboRule|DynamicComboFromFeature|ExternalDescriptorSet|Feature|FeatureRule|Float2Attribute|Float3Attribute|FloatAttribute|IntAttribute|RenderState|Requires[0-9]+|SamplerComparisonState|SamplerState|StaticCombo|StaticComboRule|StringAttribute|StructuredBuffer|Texture1D|Texture2D|Texture2DArray|Texture3D|TextureAttribute|TextureCube|TextureCubeArray|atomic_uint|attribute|binding|bool|bool2|bool3|bool4|break|buffer|bvec2|bvec3|bvec4|case|cbuffer|centroid|coherent|const|continue|default|discard|do|double|else|false|fixed|fixed2|fixed3|fixed4|flat|float|float1x1|float1x2|float1x3|float1x4|float2|float2x1|float2x2|float2x3|float2x4|float3|float3x1|float3x2|float3x3|float3x4|float4|float4x1|float4x2|float4x3|float4x4|for|half|half2|half3|half4|if|in|inout|int|int2|int3|int4|invariant|ivec2|ivec3|ivec4|layout|mat2|mat3|mat4|matrix|noperspective|out|patch|precise|precision|readonly|restrict|return|sample|sampler|sampler1D|sampler2D|sampler3D|samplerCube|samplerShadow|shared|smooth|static|struct|subroutine|switch|texture|texture2D|texture2DArray|texture3D|textureCube|textureCubeArray|true|uint|uint2|uint3|uint4|uniform|uvec2|uvec3|uvec4|varying|vec2|vec3|vec4|vector|void|volatile|while|writeonly)\b", RegexOptions.ExplicitCapture)]
            private static partial Regex KeywordRegex();

            [GeneratedRegex(@"""""|"".*?[^\\]""")]
            private static partial Regex StringRegex();

            [GeneratedRegex(@"\b(?:[0-9]+[\.]?[0-9]*f?|0x[0-9A-Fa-f]+|true|false|null)\b")]
            private static partial Regex NumberRegex();

            [GeneratedRegex(@"//.*$")]
            private static partial Regex CommentRegex();
        }
    }
}
