using Avalonia.Controls;
using ValvePak;

namespace GUI.Types.PackageViewer
{
    /// <summary>
    /// A folder in the package browser tree. Children are created when the folder is first expanded,
    /// packages can have hundreds of thousands of files.
    /// </summary>
    public sealed class BetterTreeNode : TreeViewItem, IBetterBaseItem
    {
        private static readonly object Placeholder = new();

        public BetterTreeNode(VirtualPackageNode node)
        {
            PkgNode = node;
            node.CreatedNode = this;
            Header = node.Parent == null ? "(root)" : node.Name;

            if (node.Folders.Count > 0)
            {
                Items.Add(Placeholder);
            }
        }

        protected override Type StyleKeyOverride => typeof(TreeViewItem);

        public PackageEntry? PackageEntry => null;

        public VirtualPackageNode PkgNode { get; }

        VirtualPackageNode? IBetterBaseItem.PkgNode => PkgNode;

        protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == IsExpandedProperty && IsExpanded)
            {
                EnsureChildren();
            }
        }

        public void EnsureChildren()
        {
            if (Items.Count != 1 || Items[0] != Placeholder)
            {
                return;
            }

            Items.Clear();

            var folders = new List<VirtualPackageNode>(PkgNode.Folders.Values);
            folders.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));

            foreach (var folder in folders)
            {
                Items.Add(new BetterTreeNode(folder));
            }
        }
    }
}
