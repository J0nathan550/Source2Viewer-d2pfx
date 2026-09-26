using System.Diagnostics;
using ValvePak;

namespace GUI.Types.PackageViewer
{
    [DebuggerDisplay("{Name,nq} (created={CreatedNode is not null})")]
    public class VirtualPackageNode(string name, uint size, VirtualPackageNode? parent)
    {
        /// <summary>
        /// Virtual node was converted to real nodes.
        /// </summary>
        public BetterTreeNode? CreatedNode { get; set; }

        /// <summary>
        /// Folder name.
        /// </summary>
        public string Name { get; } = name;

        /// <summary>
        /// Summed up size of all the files in this folder (recursively).
        /// </summary>
        public long TotalSize { get; set; } = size;

        /// <summary>
        /// Parent folder.
        /// </summary>
        public VirtualPackageNode? Parent { get; } = parent;

        /// <summary>
        /// Folders in this folder.
        /// </summary>
        public Dictionary<string, VirtualPackageNode> Folders { get; } = [];

        /// <summary>
        /// Files in this folder.
        /// </summary>
        public List<PackageEntry> Files { get; } = [];

        /// <summary>
        /// Path of this folder inside the package, empty for the root.
        /// </summary>
        public string GetFullPath()
        {
            var names = new Stack<string>();

            for (var node = this; node.Parent != null; node = node.Parent)
            {
                names.Push(node.Name);
            }

            return string.Join(Package.DirectorySeparatorChar, names);
        }
    }
}
