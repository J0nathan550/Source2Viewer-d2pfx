using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using GUI.Controls;
using GUI.Types.GLViewers;
using GUI.Types.Graphs;
using GUI.Utils;
using ValveKeyValue;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Viewers;

/// <summary>
/// Uncompiled KV3 text files. Shows the document as syntax highlighted KV3 plus a hex tab, and
/// when the root is an animation graph editor document it adds the graph viewer alongside.
/// </summary>
class TextKeyValues3(VrfGuiContext vrfGuiContext) : IViewer, IDisposable
{
    // A KV3 text file opens with its encoding and format header comment: "<!-- kv3 encoding:...".
    private const uint CommentMagic = 0x2D2D213C; // "<!--"
    private const ushort Kv3Magic = 0x6B20; // " k" of " kv3"

    private string? text;
    private byte[]? bytes;
    private GLGraphViewer? graphViewer;
    private string? graphTabName;
    private RendererContext? rendererContext;

    public static bool IsAccepted(uint magic, ushort magicSecond)
    {
        return magic == CommentMagic && magicSecond == Kv3Magic;
    }

    public async Task LoadAsync(Stream? stream)
    {
        if (stream != null)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer).ConfigureAwait(false);
            bytes = buffer.ToArray();
        }
        else
        {
            bytes = await File.ReadAllBytesAsync(vrfGuiContext.FileName!).ConfigureAwait(false);
        }

        text = Encoding.UTF8.GetString(bytes);

        KVObject root;

        try
        {
            using var parseStream = new MemoryStream(bytes, writable: false);
            root = KVSerializer.Create(KVSerializationFormat.KeyValues3Text).Deserialize(parseStream);
        }
        catch (Exception e)
        {
            // The text stays viewable even when it is a KV3 dialect we cannot read yet.
            Log.Warn(nameof(TextKeyValues3), $"Failed to parse {Path.GetFileName(vrfGuiContext.FileName)}: {e.Message}");
            return;
        }

        CreateGraphViewer(root);
    }

    private void CreateGraphViewer(KVObject root)
    {
        var className = root.GetStringProperty("_class");

        if (className is not ("CAnimationGraph" or "CAnimationSubGraph"))
        {
            return;
        }

        rendererContext = vrfGuiContext.CreateRendererContext();
        graphViewer = new AG1GraphViewer(vrfGuiContext, rendererContext, root);
        graphTabName = "AG1 ANIMATION GRAPH";
    }

    public Control Create()
    {
        Debug.Assert(text is not null);
        Debug.Assert(bytes is not null);

        var tabs = ViewerContentPresenter.CreateTabControl();

        // A recognised graph gets its rendered viewer first, then the raw KV3 text; a plain KV3
        // file just gets the text. Both keep a hex tab so nothing is lost versus the byte viewer.
        if (graphViewer != null)
        {
            Debug.Assert(graphTabName is not null);

            graphViewer.InitializeLoad();
            tabs.Items.Add(new TabItem { Header = graphTabName, Content = graphViewer.InitializeUiControls(isPreview: false) });
        }

        ViewerContentPresenter.AddContentTab(tabs, graphViewer != null ? "DATA" : "KV3", new ViewerContent.Text(text, HighlightLanguage.KeyValues));
        ViewerContentPresenter.AddContentTab(tabs, "Hex", new ViewerContent.HexDump(bytes));

        tabs.SelectedIndex = 0;
        return tabs;
    }

    public void Dispose()
    {
        graphViewer?.Dispose();
        graphViewer = null;
        rendererContext?.Dispose();
        rendererContext = null;
        GC.SuppressFinalize(this);
    }
}
