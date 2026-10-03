using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using GUI.Controls;
using GUI.Types.GLViewers;
using GUI.Utils;
using ValveKeyValue;
using ValveResourceFormat.NavMesh;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Viewers
{
    class NavView(VrfGuiContext guiContext) : IViewer, IDisposable
    {
        private readonly NavMeshFile navMeshFile = new();
        private GLNavMeshViewer? glViewer;

        public static bool IsAccepted(uint magic)
        {
            return magic == NavMeshFile.MAGIC;
        }

        public async Task LoadAsync(Stream? stream)
        {
            if (stream != null)
            {
                navMeshFile.Read(stream);
            }
            else
            {
                navMeshFile.Read(guiContext.FileName);
            }

            RendererContext? rendererContext = null;

            try
            {
                rendererContext = guiContext.CreateRendererContext();

                glViewer = new GLNavMeshViewer(guiContext, rendererContext, navMeshFile);
                glViewer.InitializeLoad();
                rendererContext = null;
            }
            finally
            {
                rendererContext?.Dispose();
            }
        }

        public Control Create()
        {
            var tabControl = ViewerContentPresenter.CreateTabControl();

            tabControl.Items.Add(new TabItem { Header = "NAV MESH", Content = glViewer!.InitializeUiControls() });
            tabControl.Items.Add(new TabItem { Header = "NAV INFO", Content = new DeferredContent(() => CodeTextBox.Create(navMeshFile.ToString(), HighlightLanguage.None)) });

            AddKVTab(tabControl, "NAV CUSTOM DATA", navMeshFile.CustomData);
            AddKVTab(tabControl, "NAV UNKNOWN KV3 1", navMeshFile.KV3Unknown1);
            AddKVTab(tabControl, "NAV UNKNOWN KV3 2", navMeshFile.KV3Unknown2);
            AddKVTab(tabControl, "NAV UNKNOWN KV3 3", navMeshFile.KV3Unknown3);

            tabControl.SelectedIndex = 0;
            return tabControl;
        }

        public void NotifyVisible() => glViewer?.NotifyVisible();

        public void Dispose()
        {
            glViewer?.Dispose();
        }

        private static void AddKVTab(TabControl tabControl, string tabName, KVDocument? kvDocument)
        {
            if (kvDocument == null)
            {
                return;
            }

            tabControl.Items.Add(new TabItem { Header = tabName, Content = new DeferredContent(() => CodeTextBox.Create(kvDocument.ToKV3String(), HighlightLanguage.None)) });
        }
    }
}
