using System.IO;
using System.Linq;
using System.Windows.Forms;
using GUI.Types.Exporter.CharacterAssets;
using GUI.Types.PackageViewer;
using GUI.Utils;

namespace GUI
{
    partial class MainForm
    {
        private void OnToolsMenuDropDownOpening(object? sender, EventArgs e)
        {
            UpdatePackageToolItem(toolsExportCharacterAssetsToolStripMenuItem, CharacterAssetsExporter.CanExport,
                $"Open Dota 2's pak01_dir.vpk, or another package with {ItemsGameCatalog.ItemsGamePath}, to export characters.");
        }

        private async void OnToolsExportCharacterAssetsClick(object sender, EventArgs e)
        {
            var context = FindPackageForTool(CharacterAssetsExporter.CanExport);

            if (context != null)
            {
                await CharacterAssetsExporter.ExportAsync(context).ConfigureAwait(true);
            }
        }

        /// <summary>
        /// Enables a tool's menu item only when an open package has what the tool reads, and says in its tooltip
        /// which package it will use or what to open to use it.
        /// </summary>
        private void UpdatePackageToolItem(ToolStripMenuItem item, Func<VrfGuiContext, bool> canRun, string unavailableHint)
        {
            var context = FindPackageForTool(canRun);

            item.Enabled = context != null;
            item.ToolTipText = context != null ? $"Uses {Path.GetFileName(context.FileName)}" : unavailableHint;
        }

        /// <summary>
        /// The package a tool runs on: the selected tab's one when it can, otherwise the first open one that can.
        /// </summary>
        private VrfGuiContext? FindPackageForTool(Func<VrfGuiContext, bool> canRun)
        {
            var tabs = mainTabs.TabPages.Cast<TabPage>().OrderByDescending(tab => tab == mainTabs.SelectedTab);

            foreach (var tab in tabs)
            {
                var context = tab.Controls.OfType<TreeViewWithSearchResults>().FirstOrDefault()?.VrfGuiContext;

                if (context != null && canRun(context))
                {
                    return context;
                }
            }

            return null;
        }
    }
}
