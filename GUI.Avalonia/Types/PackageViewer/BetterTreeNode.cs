using System.Linq;
using Avalonia.Controls;
using GUI.Utils;
using ValvePak;

namespace GUI.Types.PackageViewer
{
    /// <summary>
    /// A folder or file in the package browser tree. Children are created when a folder is first
    /// expanded, packages can have hundreds of thousands of files.
    /// </summary>
    public sealed class BetterTreeNode : TreeViewItem, IBetterBaseItem
    {
        private static readonly object Placeholder = new();

        private BetterTreeNode(string text, string iconName)
        {
            Header = AppIcons.CreateHeader(iconName, text);
        }

        /// <summary>Creates a folder node.</summary>
        public BetterTreeNode(string text, VirtualPackageNode node, string iconName = "Folder") : this(text, iconName)
        {
            PkgNode = node;

            if (node.Folders.Count > 0 || node.Files.Count > 0)
            {
                Items.Add(Placeholder);
            }
        }

        /// <summary>Creates a file node.</summary>
        public BetterTreeNode(PackageEntry entry) : this(entry.GetFileName(), AppIcons.GetExtensionIconName(entry.TypeName))
        {
            PackageEntry = entry;
        }

        protected override Type StyleKeyOverride => typeof(TreeViewItem);

        public PackageEntry? PackageEntry { get; }

        public VirtualPackageNode? PkgNode { get; }

        public bool IsFolder => PackageEntry == null;

        public BetterTreeNode? ParentNode => Parent as BetterTreeNode;

        public IEnumerable<BetterTreeNode> ChildNodes => Items.OfType<BetterTreeNode>();

        protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == IsExpandedProperty && IsExpanded)
            {
                EnsureChildren();

                // A folder that only holds one folder is expanded through, like the WinForms tree
                if (Items is [BetterTreeNode { IsFolder: true } only])
                {
                    only.IsExpanded = true;
                }
            }
        }

        public void EnsureChildren()
        {
            if (PkgNode == null || Items.Count != 1 || Items[0] != Placeholder)
            {
                return;
            }

            Items.Clear();
            PkgNode.CreatedNode = this;

            var folders = new List<VirtualPackageNode>(PkgNode.Folders.Values);
            folders.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            foreach (var folder in folders)
            {
                Items.Add(new BetterTreeNode(folder.Name, folder));
            }

            var files = new List<PackageEntry>(PkgNode.Files);
            files.Sort(static (a, b) => string.Compare(a.GetFileName(), b.GetFileName(), StringComparison.OrdinalIgnoreCase));

            foreach (var file in files)
            {
                Items.Add(new BetterTreeNode(file));
            }
        }

        /// <summary>Drops created children so they are rebuilt from the virtual tree on next expand.</summary>
        public void Invalidate()
        {
            Items.Clear();

            if (PkgNode != null && (PkgNode.Folders.Count > 0 || PkgNode.Files.Count > 0))
            {
                Items.Add(Placeholder);
            }

            if (IsExpanded)
            {
                EnsureChildren();
            }
        }
    }
}
