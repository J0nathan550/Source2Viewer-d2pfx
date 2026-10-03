using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Types.PackageViewer;
using GUI.Utils;

namespace GUI.Types.Exporter.CharacterAssets
{
    static partial class CharacterAssetsExporter
    {
        public static bool CanExport(Control? owner) => CanExport(GetContext(owner));

        public static async Task ExportFromContextMenu(object sender)
        {
            if (sender is not ToolStripMenuItem { Owner: ContextMenuStrip { SourceControl: var owner } })
            {
                throw new InvalidDataException("Invalid context menu structure");
            }

            var context = GetContext(owner);

            if (context != null)
            {
                await ExportAsync(context).ConfigureAwait(true);
            }
        }

        private static VrfGuiContext? GetContext(Control? owner) => owner switch
        {
            BetterTreeView tree => tree.VrfGuiContext,
            BetterListView listView => listView.VrfGuiContext,
            _ => null,
        };
    }
}
