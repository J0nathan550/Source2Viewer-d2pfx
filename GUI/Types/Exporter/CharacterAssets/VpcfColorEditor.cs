using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ValveResourceFormat.IO;

namespace GUI.Types.Exporter.CharacterAssets
{
    public static class VpcfColorEditor
    {
        private static readonly char[] ArraySeparators = [',', ' ', '\t', '\r', '\n'];

        private static readonly string[] ColorFields =
        [
            "m_ColorMin",
            "m_ColorMax",
            "m_ConstantColor",
            "m_ColorFade",
            "m_vColorTint",
            "m_Color1",
            "m_Color2",
            "m_vCPValue",
            "m_Color"
        ];

        /// <summary>
        /// Рекурсивно перекрашивает .vpcf файл и ВСЕХ его детей (m_ChildRef / m_Children).
        /// </summary>
        public static void RecolorParticleSystem(
            string rootVpcfPath,
            Color targetColor,
            string outputRoot,
            GameFileLoader? fileLoader,
            HashSet<string> processedFiles,
            IProgress<string>? progress = null)
        {
            var normalizedPath = CharacterLoadout.NormalizePath(rootVpcfPath);
            if (!normalizedPath.EndsWith(".vpcf", StringComparison.OrdinalIgnoreCase))
            {
                normalizedPath += ".vpcf";
            }

            var diskPath = Path.GetFullPath(Path.Combine(outputRoot, normalizedPath.Replace('/', Path.DirectorySeparatorChar)));

            if (!processedFiles.Add(diskPath))
            {
                return; // Уже перекрашен ранее
            }

            // 1. Считываем текст .vpcf (с диска или распаковываем из VPK)
            string vpcfText;
            if (File.Exists(diskPath))
            {
                vpcfText = File.ReadAllText(diskPath);
            }
            else if (fileLoader != null)
            {
                var compiledPath = normalizedPath + GameFileLoader.CompiledFileSuffix;
                using var resource = fileLoader.LoadFile(compiledPath) ?? fileLoader.LoadFile(normalizedPath);
                if (resource == null)
                {
                    return;
                }

                using var contentFile = FileExtract.Extract(resource, fileLoader);
                if (contentFile.Data == null)
                {
                    return;
                }

                vpcfText = System.Text.Encoding.UTF8.GetString(contentFile.Data);
            }
            else
            {
                return;
            }

            // 2. ИЩЕМ ВСЕХ ДЕТЕЙ (m_ChildRef = resource:"..." или m_Child = "...")
            var children = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var childMatches = Regex.Matches(vpcfText, @"(?:m_ChildRef|m_Child)\s*=\s*(?:resource:)?[""]([^""]+\.vpcf)[""]", RegexOptions.IgnoreCase);
            foreach (Match m in childMatches)
            {
                children.Add(m.Groups[1].Value);
            }

            // 3. Заменяем цвета в текущем .vpcf (включая раскаленное ядро и CP15 Арканы ТБ)
            var updatedText = ReplaceColorsInVpcfText(vpcfText, targetColor);

            // Сохраняем перекрашенный файл на диск
            Directory.CreateDirectory(Path.GetDirectoryName(diskPath)!);
            File.WriteAllText(diskPath, updatedText);
            progress?.Report($"    ✓ Recolor VPCF: {Path.GetFileName(diskPath)} (children: {children.Count})");

            // 4. Рекурсивно идем по всем дочерним партиклам
            foreach (var child in children)
            {
                RecolorParticleSystem(child, targetColor, outputRoot, fileLoader, processedFiles, progress);
            }
        }

        /// <summary>
        /// Перекрашивает конкретный .vpcf файл прямо на диске без компилятора и перезаписывает его на месте.
        /// </summary>
        public static void RecolorFileInPlace(string filePath, Color targetColor, HashSet<string>? processed = null)
        {
            processed ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath) || !processed.Add(fullPath))
            {
                return;
            }

            var text = File.ReadAllText(fullPath);

            // 1. Ищем всех дочерних партиклов
            var childMatches = Regex.Matches(text, @"(?:m_ChildRef|m_Child)\s*=\s*(?:resource:)?[""]([^""]+\.vpcf)[""]", RegexOptions.IgnoreCase);
            var children = new List<string>();
            foreach (Match m in childMatches)
            {
                children.Add(m.Groups[1].Value);
            }

            // 2. Перекрашиваем текст файла (сохраняя ядро, плотность и CP15/16)
            var updatedText = ReplaceColorsInVpcfText(text, targetColor);
            File.WriteAllText(fullPath, updatedText);

            // 3. Если рядом или по относительному пути лежат дочерние файлы — перекрашиваем и их
            var dir = Path.GetDirectoryName(fullPath)!;
            foreach (var child in children)
            {
                var childName = Path.GetFileName(child);
                var sameDirChild = Path.Combine(dir, childName);

                if (File.Exists(sameDirChild))
                {
                    RecolorFileInPlace(sameDirChild, targetColor, processed);
                }
            }
        }

        /// <summary>
        /// Заменяет все цветовые операторы и контрольные точки в тексте партикла с сохранением плотности и светящегося ядра.
        /// </summary>
        private static string ReplaceColorsInVpcfText(string vpcfText, Color targetColor)
        {
            var text = vpcfText;

            int r = targetColor.R;
            int g = targetColor.G;
            int b = targetColor.B;

            // Вычисляем яркое раскаленное ядро для m_ColorMax
            int coreR = (int)Math.Clamp(r + (255 - r) * 0.45f, 0, 255);
            int coreG = (int)Math.Clamp(g + (255 - g) * 0.45f, 0, 255);
            int coreB = (int)Math.Clamp(b + (255 - b) * 0.45f, 0, 255);

            foreach (var field in ColorFields)
            {
                bool isMax = field.Equals("m_ColorMax", StringComparison.OrdinalIgnoreCase)
                          || field.Equals("m_Color2", StringComparison.OrdinalIgnoreCase);

                int curR = isMax ? coreR : r;
                int curG = isMax ? coreG : g;
                int curB = isMax ? coreB : b;

                string invR = (curR / 255f).ToString("0.000000", CultureInfo.InvariantCulture);
                string invG = (curG / 255f).ToString("0.000000", CultureInfo.InvariantCulture);
                string invB = (curB / 255f).ToString("0.000000", CultureInfo.InvariantCulture);

                text = Regex.Replace(
                    text,
                    $@"({field}\s*=\s*\[)([^\]]+)(\])",
                    m =>
                    {
                        var prefix = m.Groups[1].Value;
                        var content = m.Groups[2].Value;
                        var suffix = m.Groups[3].Value;

                        var tokens = content.Split(ArraySeparators, StringSplitOptions.RemoveEmptyEntries);
                        if (tokens.Length < 3)
                        {
                            return m.Value;
                        }

                        bool isFloat = tokens[0].Contains('.', StringComparison.Ordinal)
                                    || tokens[1].Contains('.', StringComparison.Ordinal)
                                    || tokens[2].Contains('.', StringComparison.Ordinal);

                        if (isFloat)
                        {
                            if (tokens.Length >= 4)
                            {
                                string alpha = tokens[3];
                                return $"{prefix} {invR}, {invG}, {invB}, {alpha} {suffix}";
                            }
                            else
                            {
                                return $"{prefix} {invR}, {invG}, {invB} {suffix}";
                            }
                        }
                        else
                        {
                            if (tokens.Length >= 4)
                            {
                                string alpha = (tokens[3] == "1" || tokens[3] == "1.0") ? "255" : tokens[3];
                                return $"{prefix} {curR}, {curG}, {curB}, {alpha} {suffix}";
                            }
                            else
                            {
                                return $"{prefix} {curR}, {curG}, {curB} {suffix}";
                            }
                        }
                    },
                    RegexOptions.IgnoreCase);
            }

            // Замена для контрольных точек CP15 / CP16 (Призматический самоцвет)
            text = Regex.Replace(
                text,
                @"(m_iControlPoint\s*=\s*1[56][\s\S]*?m_vCPValue\s*=\s*\[)([^\]]+)(\])",
                m => $"{m.Groups[1].Value} {r}, {g}, {b} {m.Groups[3].Value}",
                RegexOptions.IgnoreCase);

            return text;
        }
    }
}
