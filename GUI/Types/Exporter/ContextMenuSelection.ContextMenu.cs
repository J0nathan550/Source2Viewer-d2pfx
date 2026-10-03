using System.Linq;
using System.Windows.Forms;
using GUI.Types.PackageViewer;

namespace GUI.Types.Exporter
{
    static partial class ContextMenuSelection
    {
        public static List<IBetterBaseItem> GetSelectedItems(Control? owner) => owner switch
        {
            BetterTreeView tree => tree.GetSelectedItems(),

            // The ".." item navigates to the parent folder, it is never meant to be acted on as part of a selection
            BetterListView listView => [.. listView.GetSelectedVirtualItems()
                .Where(static item => item.Tag is not BetterListViewItem.ParentNavigationTag)
                .OfType<IBetterBaseItem>()],
            _ => [],
        };
    }
}
