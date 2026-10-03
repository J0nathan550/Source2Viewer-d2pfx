using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Types.PackageViewer;

namespace GUI.Types.Exporter
{
    static partial class CustomVmdlExporter
    {
        public static async Task ExtractSelection(object sender)
        {
            if (sender is not ToolStripMenuItem { Owner: ContextMenuStrip { SourceControl: var owner } })
            {
                throw new InvalidDataException("Invalid context menu structure");
            }

            var context = owner switch
            {
                BetterTreeView tree => tree.VrfGuiContext,
                BetterListView listView => listView.VrfGuiContext,
                _ => throw new InvalidDataException("Unknown state"),
            };

            await ExtractAsync(context, ContextMenuSelection.GetSelectedItems(owner)).ConfigureAwait(true);
        }
    }
}
