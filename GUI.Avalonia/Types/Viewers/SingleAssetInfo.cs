using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using GUI.Controls;
using GUI.Utils;
using ValveKeyValue;
using ValvePak;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;

namespace GUI.Types.Viewers
{
    /// <summary>
    /// The "View asset info" tab: package entry details, and the tools asset info of the file with what references it.
    /// </summary>
    static class SingleAssetInfo
    {
        private sealed record FileReference(string Type, string File);

        public static DocumentTab Create(VrfGuiContext guiContext, PackageEntry entry)
        {
            var folder = Path.GetDirectoryName(guiContext.FileName);
            var filePath = entry.GetFullPath();

            var toolsAssetInfo = guiContext.GetOrLoadToolsAssetInfo();
            ValveResourceFormat.ToolsAssetInfo.ToolsAssetInfo.File? assetInfo = null;

            if (toolsAssetInfo != null && !toolsAssetInfo.Files.TryGetValue(filePath, out assetInfo))
            {
                (filePath, assetInfo) = FindByGameRootPath(toolsAssetInfo, folder, filePath);
            }

            // If there is no exact match in the tools info, try the same file without the "_c" suffix
            if (assetInfo == null && toolsAssetInfo != null && filePath.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
            {
                var filePathUncompiled = filePath[..^2];

                if (toolsAssetInfo.Files.TryGetValue(filePathUncompiled, out assetInfo))
                {
                    filePath = filePathUncompiled;
                }
                else
                {
                    var (foundPath, foundInfo) = FindByGameRootPath(toolsAssetInfo, folder, filePathUncompiled);

                    if (foundInfo != null)
                    {
                        filePath = foundPath;
                        assetInfo = foundInfo;
                    }
                }
            }

            var tab = new DocumentTab(Path.GetFileName(filePath), "Info")
            {
                ToolTipText = filePath,
            };

            var fileInfo = new StringBuilder();
            fileInfo.AppendLine(CultureInfo.InvariantCulture, $"Name: {filePath}");
            fileInfo.AppendLine(CultureInfo.InvariantCulture, $"CRC: {entry.CRC32:X2}");
            fileInfo.AppendLine(CultureInfo.InvariantCulture, $"Archive: {entry.ArchiveIndex}");
            fileInfo.AppendLine(CultureInfo.InvariantCulture, $"Offset: {entry.Offset}");
            fileInfo.AppendLine(CultureInfo.InvariantCulture, $"Size: {entry.Length} ({HumanReadableByteSizeFormatter.Format(entry.Length)})");
            fileInfo.AppendLine(CultureInfo.InvariantCulture, $"Preloaded bytes: {entry.SmallData.Length}");

            var fileControl = new CodeTextBox(fileInfo.ToString());

            if (assetInfo == null || toolsAssetInfo == null)
            {
                tab.Content = fileControl;
                return tab;
            }

            var referencedBy = new List<FileReference>();

            foreach (var (otherPath, other) in toolsAssetInfo.Files)
            {
                if (other.ChildResources.Contains(filePath))
                {
                    referencedBy.Add(new FileReference("Child Resource", otherPath));
                }

                if (other.ExternalReferences.Contains(filePath))
                {
                    referencedBy.Add(new FileReference("External Reference", otherPath));
                }

                if (other.WeakReferences.Contains(filePath))
                {
                    referencedBy.Add(new FileReference("Weak Reference", otherPath));
                }

                if (other.AdditionalRelatedFiles.Contains(filePath))
                {
                    referencedBy.Add(new FileReference("Additional Related File", otherPath));
                }

                if (other.InputDependencies.Exists(f => f.Filename == filePath))
                {
                    referencedBy.Add(new FileReference("Input Dependency", otherPath));
                }

                if (other.AdditionalInputDependencies.Exists(f => f.Filename == filePath))
                {
                    referencedBy.Add(new FileReference("Additional Input Dependency", otherPath));
                }
            }

            var externalReferences = referencedBy
                .Select(static r => new ResourceExtRefList.ResourceReferenceInfo { Name = r.File })
                .DistinctBy(static x => x.Name)
                .ToList();
            var referencedControl = Resource.BuildExternalRefTree(guiContext, externalReferences);

            using var ms = new MemoryStream();
            KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Serialize(ms, assetInfo, "Asset Info");
            var infoControl = new CodeTextBox(Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length));

            // File info over referenced by | asset info
            var left = new Grid { RowDefinitions = new RowDefinitions("*,4,*") };
            var leftSplitter = new GridSplitter { ResizeDirection = GridResizeDirection.Rows };
            Grid.SetRow(leftSplitter, 1);
            Grid.SetRow(referencedControl, 2);
            left.Children.Add(fileControl);
            left.Children.Add(leftSplitter);
            left.Children.Add(referencedControl);

            var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,4,*") };
            var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns };
            Grid.SetColumn(splitter, 1);
            Grid.SetColumn(infoControl, 2);
            root.Children.Add(left);
            root.Children.Add(splitter);
            root.Children.Add(infoControl);

            tab.Content = root;
            return tab;
        }

        private static (string Path, ValveResourceFormat.ToolsAssetInfo.ToolsAssetInfo.File? Info) FindByGameRootPath(ValveResourceFormat.ToolsAssetInfo.ToolsAssetInfo toolsAssetInfo, string? folder, string filePath)
        {
            var gameRootPath = string.Concat(Path.GetFileName(folder), "/", filePath);

            foreach (var (otherPath, other) in toolsAssetInfo.Files)
            {
                if (other.SearchPathsGameRoot.Exists(f => f.Filename == gameRootPath))
                {
                    return (otherPath, other);
                }
            }

            return (filePath, null);
        }
    }
}
