using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using GUI.Controls;
using GUI.Types.Audio;
using GUI.Types.GLViewers;
using GUI.Types.Graphs;
using GUI.Utils;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.GenericData.CS2;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Viewers
{
    enum ResourceViewMode
    {
        Default,
        ViewerOnly,
        ResourceBlocksOnly,
    };

    /// <summary>
    /// Compiled resources: the matching GL viewer when there is one, plus a tab per block and the decompiled content.
    /// Port of the WinForms Resource viewer, viewer selection is kept in step with it.
    /// </summary>
    class Resource(VrfGuiContext vrfGuiContext, ResourceViewMode viewMode, bool verifyFileSize) : IViewer, IDisposable
    {
        private ValveResourceFormat.Resource? resource;
        private RendererContext? rendererContext;
        private readonly List<(GLGraphViewer Viewer, string TabName)> preparedGraphViewers = [];
        public GLBaseControl? GLViewer { get; private set; }
        private Exception? GLViewerError;
        private string? GLViewerTabName;

        // Controls that hold native resources, Avalonia does not dispose controls when their tab goes away
        private readonly List<IDisposable> ownedControls = [];

        public static bool IsAccepted(uint magic)
        {
            return magic == ValveResourceFormat.Resource.KnownHeaderVersion;
        }

        public async Task LoadAsync(Stream? stream)
        {
            var resourceTemp = new ValveResourceFormat.Resource
            {
                FileName = vrfGuiContext.FileName,
            };
            resource = resourceTemp;

            try
            {
                if (stream != null)
                {
                    resource.Read(stream, verifyFileSize);
                }
                else
                {
                    resource.Read(vrfGuiContext.FileName);
                }

                resourceTemp = null;
            }
            finally
            {
                // Only dispose resource if it throws within Read(), tough luck below this
                resourceTemp?.Dispose();
            }

            if (viewMode != ResourceViewMode.ResourceBlocksOnly)
            {
                try
                {
                    InitializeSpecialViewer(vrfGuiContext, resource);
                }
                catch (Exception ex)
                {
                    Log.Error(nameof(Resource), ex.ToString());
                    GLViewerError = ex;
                }
            }
        }

        private void InitializeSpecialViewer(VrfGuiContext vrfGuiContext, ValveResourceFormat.Resource resource)
        {
            rendererContext = vrfGuiContext.CreateRendererContext();

            switch (resource.ResourceType)
            {
                case ResourceType.Texture:
                case ResourceType.PanoramaVectorGraphic:
                    GLViewer = new GLTextureViewer(vrfGuiContext, rendererContext, resource);
                    GLViewerTabName = "TEXTURE";
                    break;

                case ResourceType.Particle:
                    if (resource.DataBlock is ParticleSystem particleData)
                    {
                        GLViewer = new GLParticleViewer(vrfGuiContext, rendererContext, particleData);
                        GLViewerTabName = "PARTICLE";
                    }
                    break;

                case ResourceType.ParticleSnapshot:
                    if (resource.GetBlockByType(BlockType.SNAP) is ParticleSnapshot snapshot && SnapshotParticleSystem.CanPreview(snapshot))
                    {
                        GLViewer = new GLParticleViewer(vrfGuiContext, rendererContext, SnapshotParticleSystem.Create(snapshot), snapshot);
                        GLViewerTabName = "SNAPSHOT";
                    }
                    break;

                case ResourceType.Map:
                {
                    var worldResource = vrfGuiContext.LoadFileCompiled(WorldLoader.GetWorldNameFromMap(resource.FileName!));
                    var mapExternalReferences = resource.ExternalReferences;

                    if (worldResource != null && worldResource.DataBlock is World mapWorldData)
                    {
                        GLViewer = new GLWorldViewer(vrfGuiContext, rendererContext, mapWorldData, mapExternalReferences);
                        GLViewerTabName = "MAP";
                    }
                    else
                    {
                        worldResource?.Dispose();
                    }
                    break;
                }

                case ResourceType.World:
                    if (resource.DataBlock is World worldData)
                    {
                        GLViewer = new GLWorldViewer(vrfGuiContext, rendererContext, worldData);
                        GLViewerTabName = "MAP";
                    }
                    break;

                case ResourceType.WorldNode:
                    if (resource.DataBlock is WorldNode worldNodeData)
                    {
                        GLViewer = new GLWorldViewer(vrfGuiContext, rendererContext, worldNodeData, resource.ExternalReferences);
                        GLViewerTabName = "WORLD NODE";
                    }
                    break;

                case ResourceType.Model:
                    if (resource.DataBlock is Model modelData)
                    {
                        GLViewer = new GLModelViewer(vrfGuiContext, rendererContext, modelData);
                        GLViewerTabName = "MODEL";
                    }
                    break;

                case ResourceType.Mesh:
                    if (resource.DataBlock is Mesh meshData)
                    {
                        GLViewer = new GLMeshViewer(vrfGuiContext, rendererContext, meshData);
                        GLViewerTabName = "MESH";
                    }
                    break;

                case ResourceType.SmartProp:
                    if (resource.DataBlock is SmartProp smartPropData)
                    {
                        GLViewer = new GLSmartPropViewer(vrfGuiContext, rendererContext, smartPropData);
                        GLViewerTabName = "SMART PROP";
                    }
                    break;

                case ResourceType.AnimationGraph:
                    if (resource.DataBlock is AnimGraph animGraphData)
                    {
                        GLViewer = new AG1GraphViewer(vrfGuiContext, rendererContext, animGraphData.Data);
                        GLViewerTabName = "AG1 ANIMATION GRAPH";
                    }
                    break;

                case ResourceType.NmClip:
                    GLViewer = new GLAnimationViewer(vrfGuiContext, rendererContext, resource);
                    GLViewerTabName = "ANIMATION CLIP";
                    break;

                case ResourceType.NmSkeleton:
                    GLViewer = new GLAnimationViewer(vrfGuiContext, rendererContext, resource);
                    GLViewerTabName = "SKELETON";
                    break;

                case ResourceType.NmGraph:
                    if (resource.DataBlock is BinaryKV3 binaryKV3)
                    {
                        GLViewer = new AG2GraphViewer(vrfGuiContext, rendererContext, binaryKV3.Data);
                        GLViewerTabName = "AG2 ANIMATION GRAPH";
                    }
                    break;

                case ResourceType.PulseGraphDef:
                    if (resource.DataBlock is BinaryKV3 graphDefKV3)
                    {
                        GLViewer = new PulseGraphViewer(vrfGuiContext, rendererContext, graphDefKV3.Data);
                        GLViewerTabName = "PULSE GRAPH";
                    }
                    break;

                case ResourceType.EntityLump:
                    if (resource.DataBlock is EntityLump entityLumpData)
                    {
                        GLViewer = new EntityIOGraphViewer(vrfGuiContext, rendererContext, entityLumpData);
                        GLViewerTabName = "ENTITY I/O GRAPH";
                    }
                    break;

                case ResourceType.Material:
                {
                    if (resource.DataBlock is Material { ShaderName: "sky.vfx" })
                    {
                        GLViewer = new GLSkyboxViewer(vrfGuiContext, rendererContext, resource);
                        GLViewerTabName = "SKYBOX";
                    }
                    else
                    {
                        GLViewer = new GLMaterialViewer(vrfGuiContext, rendererContext, resource);
                        GLViewerTabName = "MATERIAL";
                    }
                    break;
                }

                case ResourceType.PhysicsCollisionMesh:
                    if (resource.DataBlock is PhysAggregateData physAggregateData)
                    {
                        GLViewer = new GLModelViewer(vrfGuiContext, rendererContext, physAggregateData);
                        GLViewerTabName = "PHYSICS";
                    }
                    break;

                case ResourceType.WorldVisibility:
                    if (VoxelVisibility.GetWorldVisibility(resource) is { } vxvs)
                    {
                        GLViewer = new GLVoxelVisibilityViewer(vrfGuiContext, rendererContext, vxvs);
                        GLViewerTabName = "VISIBILITY";
                    }
                    break;

                case ResourceType.PostProcessing:
                    if (resource.DataBlock is PostProcessing postProcessing && postProcessing.Data.ContainsKey("m_colorCorrectionVolumeData"))
                    {
                        GLViewer = new GLTextureViewer(vrfGuiContext, rendererContext, resource);
                        GLViewerTabName = "LUT";
                    }
                    break;

                case ResourceType.VData:
                    if (resource.DataBlock is BombDamage bombDamage)
                    {
                        GLViewer = new GLBombDamageViewer(vrfGuiContext, rendererContext, bombDamage);
                        GLViewerTabName = "BOMB DAMAGE";
                    }
                    break;
            }

            GLViewer?.InitializeLoad();

            // Preview only ever shows the first tab, so the extra graph tabs would be built and thrown away.
            if (viewMode != ResourceViewMode.ViewerOnly)
            {
                PrepareExtraGraphViewers(vrfGuiContext, resource);
            }
        }

        public void NotifyVisible() => GLViewer?.NotifyVisible();

        public Control Create()
        {
            Debug.Assert(resource is not null);

            var isPreview = viewMode == ResourceViewMode.ViewerOnly;

            var resTabs = ViewerContentPresenter.CreateTabControl();
            var selectData = true;

            if (viewMode != ResourceViewMode.ResourceBlocksOnly)
            {
                if (GLViewerError == null)
                {
                    try
                    {
                        selectData = !AddSpecialViewer(vrfGuiContext, resource, isPreview, resTabs);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(nameof(Resource), ex.ToString());
                        GLViewerError = ex;
                    }
                }

                if (GLViewerError != null)
                {
                    GLViewer?.Dispose();
                    GLViewer = null;
                    DisposeExtraGraphViewers();
                    resTabs.Items.Clear();
                    resTabs.Items.Add(new TabItem { Header = "Viewer Error", Content = CodeTextBox.CreateFromException(GLViewerError) });
                }

                // Entity lumps get the same browsable grid a map's world viewer provides, with or
                // without the graph tab the GL viewer adds.
                if (!isPreview && resource.DataBlock is EntityLump standaloneLump)
                {
                    resTabs.Items.Add(new TabItem { Header = "Entity List", Content = new EntityViewer(vrfGuiContext, standaloneLump.GetEntities()) });
                }
            }

            if (isPreview && !selectData && resTabs.Items.Count > 0 && resTabs.Items[0] is TabItem previewTab)
            {
                // Preview only displays the first tab page
                var content = previewTab.Content as Control;
                previewTab.Content = null;
                return content ?? resTabs;
            }

            List<RawBinary>? binaryBuffers = null;

            foreach (var block in resource.Blocks)
            {
                // They are just binary blobs, and the actual layout of them is stored in CTRL, so the tabs are not useful here
                if (block is RawBinary rawBlock && block.Type is BlockType.MVTX or BlockType.MIDX or BlockType.MADJ)
                {
                    binaryBuffers ??= [];
                    binaryBuffers.Add(rawBlock);
                    continue;
                }

                if (block.Type == BlockType.RERL && block is ResourceExtRefList externalReferences)
                {
                    resTabs.Items.Add(new TabItem { Header = "References", Content = BuildExternalRefTree(vrfGuiContext, externalReferences.ResourceRefInfoList) });
                    continue;
                }

                if (block.Type == BlockType.NTRO)
                {
                    var manifest = (ResourceIntrospectionManifest)block;

                    if (manifest.ReferencedStructs.Count > 0)
                    {
                        resTabs.Items.Add(new TabItem { Header = "Introspection Manifest: Structs", Content = ViewerContentPresenter.CreateGrid(manifest.ReferencedStructs) });
                    }

                    if (manifest.ReferencedEnums.Count > 0)
                    {
                        resTabs.Items.Add(new TabItem { Header = "Introspection Manifest: Enums", Content = ViewerContentPresenter.CreateGrid(manifest.ReferencedEnums) });
                    }
                }

                // Serializing blocks to text is expensive and large, only do it for the tab that is opened
                var blockContent = new DeferredContent(
                    () => CreateTextViewControl(resource, block),
                    _ => CreateByteViewControl(resource, block));

                var blockTab = new TabItem { Header = block.Type.ToString(), Content = blockContent };
                resTabs.Items.Add(blockTab);

                if (block.Type == BlockType.DATA && selectData)
                {
                    resTabs.SelectedItem = blockTab;
                }
            }

            if (binaryBuffers != null)
            {
                var text = new StringBuilder();

                foreach (var block in binaryBuffers)
                {
                    text.AppendLine(CultureInfo.InvariantCulture, $"{block.Type} - {block.Size} bytes");
                }

                resTabs.Items.Add(new TabItem { Header = "Buffers", Content = CodeTextBox.Create(text.ToString()) });
            }

            try
            {
                AddReconstructedContentTab(vrfGuiContext, resource, resTabs);
            }
            catch (Exception ex)
            {
                resTabs.Items.Add(new TabItem { Header = "Decompile Error", Content = CodeTextBox.CreateFromException(ex) });
            }

            if (resTabs.SelectedIndex < 0 && resTabs.ItemCount > 0)
            {
                resTabs.SelectedIndex = 0;
            }

            return resTabs;
        }

        private bool AddSpecialViewer(VrfGuiContext vrfGuiContext, ValveResourceFormat.Resource resource, bool isPreview, TabControl resTabs)
        {
            if (GLViewer != null)
            {
                Debug.Assert(GLViewerTabName != null);

                var glViewerControl = GLViewer.InitializeUiControls(isPreview);

                if (isPreview && glViewerControl is RendererControl rendererControl)
                {
                    // No tab header in preview, so show the file name at the top of the side control panel.
                    rendererControl.AddPreviewFileName(Path.GetFileName(vrfGuiContext.FileName), -1);
                }

                var specialTabPage = new TabItem { Header = GLViewerTabName, Content = glViewerControl };
                resTabs.Items.Add(specialTabPage);
                resTabs.SelectedItem = specialTabPage;

                if (!isPreview && GLViewer is GLMaterialViewer glMaterialViewer)
                {
                    glMaterialViewer.SetTabControl(resTabs);
                }

                if (!isPreview && GLViewer is GLWorldViewer glWorldViewer && glWorldViewer.LoadedWorld is { } loadedWorld)
                {
                    if (resource.ResourceType == ResourceType.Map)
                    {
                        resTabs.Items.Add(new TabItem { Header = "World Data", Content = new DeferredContent(() => CreateTextViewControl(ResourceType.WorldNode, loadedWorld.World)) });
                    }

                    if (loadedWorld.MainWorldNode != null)
                    {
                        resTabs.Items.Add(new TabItem { Header = "Node Data", Content = new DeferredContent(() => CreateTextViewControl(ResourceType.WorldNode, loadedWorld.MainWorldNode)) });
                    }

                    resTabs.Items.Add(new TabItem { Header = "Entity List", Content = new EntityViewer(vrfGuiContext, loadedWorld.Entities, glWorldViewer.SelectAndFocusEntity) });
                }

                if (!isPreview)
                {
                    foreach (var (viewer, tabName) in preparedGraphViewers)
                    {
                        viewer.InitializeLoad();
                        resTabs.Items.Add(new TabItem { Header = tabName, Content = viewer.InitializeUiControls(isPreview: false) });

                        if (GLViewer is GLWorldViewer worldViewerWithGraph && viewer is EntityIOGraphViewer entityGraphViewer)
                        {
                            worldViewerWithGraph.ShowEntityInGraph = entityGraphViewer.ShowEntity;
                            worldViewerWithGraph.EntityHasGraphNode = entityGraphViewer.HasEntity;
                        }
                    }
                }

                return true;
            }

            return AddSpecialViewerData(resource, isPreview, resTabs);
        }

        // Runs on the background load thread: graph construction (entity scans, icon decoding,
        // layout) is expensive and must not block the UI thread's loading indicator. The UI
        // thread later only creates the tabs in AddSpecialViewer.
        private void PrepareExtraGraphViewers(VrfGuiContext vrfGuiContext, ValveResourceFormat.Resource resource)
        {
            if (rendererContext == null)
            {
                return;
            }

            if (GLViewer is GLWorldViewer { LoadedWorld: { } loadedWorld } glWorldViewer)
            {
                var hasConnections = false;

                foreach (var entity in loadedWorld.Entities)
                {
                    if (entity.Connections is { Count: > 0 })
                    {
                        hasConnections = true;
                        break;
                    }
                }

                if (hasConnections)
                {
                    preparedGraphViewers.Add((new EntityIOGraphViewer(vrfGuiContext, rendererContext, loadedWorld.Entities, glWorldViewer.SelectAndFocusEntities), "ENTITY I/O GRAPH"));
                }

                PrepareMapPulseGraphViewers(vrfGuiContext, loadedWorld.Entities);
            }

            if (GLViewer is GLModelViewer && resource.DataBlock is Model model)
            {
                PrepareModelAnimGraphViewers(vrfGuiContext, model);
            }
        }

        // Maps bind pulse scripts through point_pulse entities referencing the graph resource.
        private void PrepareMapPulseGraphViewers(VrfGuiContext vrfGuiContext, List<EntityLump.Entity> entities)
        {
            Debug.Assert(rendererContext != null);

            var scripts = new List<string>();

            foreach (var entity in entities)
            {
                if (entity.GetStringProperty("classname") != "point_pulse")
                {
                    continue;
                }

                var graphDef = entity.GetStringProperty("graph_def");

                if (!string.IsNullOrEmpty(graphDef) && !scripts.Contains(graphDef))
                {
                    scripts.Add(graphDef);
                }
            }

            foreach (var script in scripts)
            {
                if (rendererContext.FileLoader.LoadFileCompiled(script)?.DataBlock is BinaryKV3 pulseData)
                {
                    var tabName = scripts.Count > 1 ? $"PULSE GRAPH ({Path.GetFileNameWithoutExtension(script)})" : "PULSE GRAPH";
                    var viewer = new PulseGraphViewer(vrfGuiContext, rendererContext, pulseData.Data);
                    preparedGraphViewers.Add((viewer, tabName));
                }
            }
        }

        private void PrepareModelAnimGraphViewers(VrfGuiContext vrfGuiContext, Model model)
        {
            Debug.Assert(rendererContext != null);

            var graphPaths = new List<string>();

            void AddGraphPath(string? path)
            {
                if (!string.IsNullOrEmpty(path) && !graphPaths.Contains(path))
                {
                    graphPaths.Add(path);
                }
            }

            if (model.Data.GetArray("m_animGraph2Refs") is { } animGraph2Refs)
            {
                foreach (var graphRef in animGraph2Refs)
                {
                    AddGraphPath(graphRef.GetStringProperty("m_hGraph"));
                }
            }
            else if (model.Data.ContainsKey("m_animGraph2Refs"))
            {
                Log.Warn(nameof(Resource), "Model has a non-array m_animGraph2Refs value, skipping its animation graph tabs.");
            }

            if (model.Data.ContainsKey("m_refAnimGraph"))
            {
                AddGraphPath(model.Data.GetStringProperty("m_refAnimGraph"));
            }

            // HLA/SteamVR-era models and compiled Deadlock AG1 bind their graph through the keyvalues block.
            AddGraphPath(model.KeyValues.GetStringProperty("anim_graph_resource"));

            foreach (var path in graphPaths)
            {
                GLGraphViewer viewer;
                string baseName;

                switch (rendererContext.FileLoader.LoadFileCompiled(path)?.DataBlock)
                {
                    case AnimGraph ag1Data:
                        viewer = new AG1GraphViewer(vrfGuiContext, rendererContext, ag1Data.Data);
                        baseName = "AG1 ANIMATION GRAPH";
                        break;
                    case BinaryKV3 nmGraphData:
                        viewer = new AG2GraphViewer(vrfGuiContext, rendererContext, nmGraphData.Data);
                        baseName = "AG2 ANIMATION GRAPH";
                        break;
                    default:
                        continue;
                }

                var tabName = graphPaths.Count > 1 ? $"{baseName} ({Path.GetFileNameWithoutExtension(path)})" : baseName;
                preparedGraphViewers.Add((viewer, tabName));
            }
        }

        private bool AddSpecialViewerData(ValveResourceFormat.Resource resource, bool isPreview, TabControl resTabs)
        {
            switch (resource.ResourceType)
            {
                case ResourceType.Panorama:
                    if (resource.DataBlock is Panorama { Images.Count: > 0 } panorama)
                    {
                        resTabs.Items.Add(new TabItem { Header = "PANORAMA IMAGES", Content = ViewerContentPresenter.CreateGrid(panorama.Images) });
                    }
                    break;

                case ResourceType.Sound:
                    if (resource.ContainsBlockType(BlockType.DATA))
                    {
                        var autoPlay = ((Settings.QuickPreviewFlags)Settings.Config.QuickFilePreview & Settings.QuickPreviewFlags.AutoPlaySounds) != 0;
                        var panel = new DockPanel();

                        if (resource.DataBlock is Sound soundData)
                        {
                            var info = CreateSoundInfoLabel(soundData);
                            DockPanel.SetDock(info, Dock.Top);
                            panel.Children.Add(info);
                        }

                        try
                        {
                            if (AudioPlayer.CreateWaveStream(resource) is var (waveStream, loopMarkers))
                            {
                                var ownedStream = waveStream;

                                try
                                {
                                    var audio = new AudioPlaybackPanel(ownedStream, isPreview && autoPlay, loopMarkers);
                                    ownedStream = null;
                                    ownedControls.Add(audio);
                                    panel.Children.Add(audio);
                                }
                                finally
                                {
                                    ownedStream?.Dispose();
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error(nameof(AudioPlayer), e.ToString());
                            panel.Children.Add(new SelectableTextBlock { Text = $"Failed to play sound: {e.Message}", Margin = new(6) });
                        }

                        var soundTab = new TabItem { Header = "SOUND", Content = panel };
                        resTabs.Items.Add(soundTab);
                        resTabs.SelectedItem = soundTab;
                        return true;
                    }
                    break;

                case ResourceType.ChoreoSceneFileData:
                {
                    var choreoTab = new TabItem { Header = "VCDLIST", Content = new ChoreoViewer(resource) };
                    resTabs.Items.Add(choreoTab);
                    resTabs.SelectedItem = choreoTab;
                    return true;
                }

                case ResourceType.Shader:
                {
                    var compiledShaderViewer = new CompiledShader(vrfGuiContext);
                    try
                    {
                        var shaderTab = new TabItem { Header = "SHADER", Content = ((IViewer)compiledShaderViewer).Create() };
                        resTabs.Items.Add(shaderTab);
                        resTabs.SelectedItem = shaderTab;
                        compiledShaderViewer = null;
                    }
                    finally
                    {
                        compiledShaderViewer?.Dispose();
                    }
                    return true;
                }
            }

            return false;
        }

        private static SelectableTextBlock CreateSoundInfoLabel(Sound sound)
        {
            var text = new StringBuilder();

            foreach (var (label, value) in sound.GetInfoRows())
            {
                if (text.Length > 0)
                {
                    text.Append("    ");
                }

                text.Append(label).Append(": ").Append(value);
            }

            return new SelectableTextBlock
            {
                Margin = new(6, 6, 6, 2),
                Text = text.ToString(),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            };
        }

        public static bool OpenExternalReference(VrfGuiContext vrfGuiContext, string name)
        {
            Log.Debug(nameof(Resource), $"Opening {name} from external refs");

            var foundFile = vrfGuiContext.FindFileWithContext(name + GameFileLoader.CompiledFileSuffix);
            if (foundFile.Context == null)
            {
                foundFile = vrfGuiContext.FindFileWithContext(name);
            }

            if (foundFile.Context != null)
            {
                Program.MainForm.OpenFile(foundFile.Context, foundFile.PackageEntry);
                return true;
            }

            return false;
        }

        public static TreeView BuildExternalRefTree(VrfGuiContext vrfGuiContext, List<ResourceExtRefList.ResourceReferenceInfo> references)
        {
            var treeView = new TreeView();
            var rootNodes = new Dictionary<string, TreeViewItem>();
            var rootLookup = rootNodes.GetAlternateLookup<ReadOnlySpan<char>>();

            foreach (var refInfo in references)
            {
                var pathSpan = refInfo.Name.AsSpan();
                var slashIndex = pathSpan.IndexOf('/');
                var rootFolderSpan = slashIndex >= 0 ? pathSpan[..slashIndex] : [];

                var fileNode = new TreeViewItem { Header = refInfo.Name, Tag = refInfo };

                TreeViewItem? rootNode = null;

                if (!rootFolderSpan.IsEmpty && !rootLookup.TryGetValue(rootFolderSpan, out rootNode))
                {
                    var rootFolder = rootFolderSpan.ToString();
                    rootNode = new TreeViewItem { Header = rootFolder, IsExpanded = true };
                    rootNodes[rootFolder] = rootNode;
                    treeView.Items.Add(rootNode);
                }

                if (rootNode != null)
                {
                    rootNode.Items.Add(fileNode);
                }
                else
                {
                    treeView.Items.Add(fileNode);
                }
            }

            void OpenSelected()
            {
                if (treeView.SelectedItem is TreeViewItem { Tag: ResourceExtRefList.ResourceReferenceInfo refInfo })
                {
                    OpenExternalReference(vrfGuiContext, refInfo.Name);
                }
            }

            treeView.DoubleTapped += (_, _) => OpenSelected();
            treeView.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    OpenSelected();
                    e.Handled = true;
                }
            };

            var copy = new MenuItem { Header = "Copy name" };
            copy.Click += (_, _) =>
            {
                if (treeView.SelectedItem is TreeViewItem { Tag: ResourceExtRefList.ResourceReferenceInfo refInfo })
                {
                    AppClipboard.SetText(refInfo.Name);
                }
            };
            treeView.ContextMenu = new ContextMenu { Items = { copy } };

            return treeView;
        }

        private static Control CreateByteViewControl(ValveResourceFormat.Resource resource, Block block)
        {
            Debug.Assert(resource.Reader != null);

            resource.Reader.BaseStream.Position = block.Offset;
            var input = resource.Reader.ReadBytes((int)block.Size);

            var text = ByteViewer.GetTextFromBytes(input.AsSpan());

            if (!string.IsNullOrEmpty(text))
            {
                return CodeTextBox.Create(text);
            }

            return new HexViewer(input);
        }

        private Control CreateTextViewControl(ValveResourceFormat.Resource resource, Block block)
        {
            if (resource.ResourceType == ResourceType.SboxShader && block is SboxShader shaderBlock)
            {
                var viewer = new CompiledShader(vrfGuiContext);

                try
                {
                    var control = viewer.Create(
                        shaderBlock.Shaders,
                        Path.GetFileNameWithoutExtension(resource.FileName.AsSpan()),
                        ValveResourceFormat.CompiledShader.VcsProgramType.Features
                    );

                    viewer = null;
                    return control;
                }
                finally
                {
                    viewer?.Dispose();
                }
            }

            return CreateTextViewControl(resource.ResourceType, block);
        }

        private static Control CreateTextViewControl(ResourceType resourceType, Block block)
        {
            return ViewerContentPresenter.CreateControl(GetTextViewContent(resourceType, block));
        }

        private static ViewerContent.Text GetTextViewContent(ResourceType resourceType, Block block)
        {
            if (TryGetKvDataBlock(block, out var kvRoot, out var kvHeader))
            {
                var doc = new KVDocument(kvHeader, name: null, kvRoot);
                var (kv3Text, sourceMap) = KVSerializer.Create(KVSerializationFormat.KeyValues3Text).SerializeWithSourceMap(doc);
                return new ViewerContent.Text(kv3Text, SourceMap: sourceMap);
            }

            var text = block.ToString();
            var language = HighlightLanguage.KeyValues;

            if (resourceType == ResourceType.PanoramaLayout && block.Type == BlockType.DATA)
            {
                language = HighlightLanguage.XML;
            }
            else if (resourceType == ResourceType.PanoramaVectorGraphic && block.Type == BlockType.DATA)
            {
                language = HighlightLanguage.XML;
            }
            else if (resourceType == ResourceType.PanoramaStyle && block.Type == BlockType.DATA)
            {
                language = HighlightLanguage.CSS;
            }
            else if ((resourceType == ResourceType.PanoramaScript || resourceType == ResourceType.PanoramaTypescript) && block.Type == BlockType.DATA)
            {
                language = HighlightLanguage.JS;
            }

            return new ViewerContent.Text(text, language);
        }

        private static bool TryGetKvDataBlock(Block block, [MaybeNullWhen(false)] out KVObject root, out KVHeader? header)
        {
            switch (block)
            {
                case BinaryKV3 kv3:
                    root = kv3.Data.Root;
                    header = kv3.Data.Header;
                    return true;

                case KeyValuesOrNTRO kvOrNtro:
                    root = kvOrNtro.Data;
                    header = null;
                    return true;

                case NTRO ntro:
                    root = ntro.Output;
                    header = null;
                    return true;

                case ResourceEditInfo2 red2 when red2.Data is not null:
                    root = red2.Data.Root;
                    header = red2.Data.Header;
                    return true;

                default:
                    root = null;
                    header = null;
                    return false;
            }
        }

        private static void AddReconstructedContentTab(VrfGuiContext vrfGuiContext, ValveResourceFormat.Resource resource, TabControl resTabs)
        {
            switch (resource.ResourceType)
            {
                case ResourceType.Sound when resource.DataBlock is Sound { Sentence: { } sentence }:
                    ViewerContentPresenter.AddContentTab(resTabs, "Reconstructed phonemes", new ViewerContent.Text(sentence.ToValveSentence()));
                    break;

                case ResourceType.Material:
                    ViewerContentPresenter.AddContentTab(resTabs, "Reconstructed vmat", new ViewerContent.LazyText(new MaterialExtract(resource, vrfGuiContext.FileLoaderNoCache).ToValveMaterial));
                    break;

                case ResourceType.EntityLump:
                    if (resource.DataBlock is EntityLump entityLump)
                    {
                        ViewerContentPresenter.AddContentTab(resTabs, "FGD", new ViewerContent.Text(entityLump.ToForgeGameData()));
                        ViewerContentPresenter.AddContentTab(resTabs, "Entities-Text", new ViewerContent.Text(entityLump.ToEntityDumpString()));
                        resTabs.SelectedIndex = 0;
                    }
                    break;

                case ResourceType.PostProcessing:
                    if (resource.DataBlock is PostProcessing postProcessingData)
                    {
                        ViewerContentPresenter.AddContentTab(resTabs, "Reconstructed vpost", new ViewerContent.Text(postProcessingData.ToValvePostProcessing()));
                    }
                    break;

                case ResourceType.Texture:
                {
                    if (FileExtract.IsChildResource(resource))
                    {
                        break;
                    }

                    var textureExtract = new TextureExtract(resource);
                    ViewerContentPresenter.AddContentTab(resTabs, "Reconstructed vtex", new ViewerContent.Text(textureExtract.ToValveTexture()));

                    if (textureExtract.TryGetMksData(out var _, out var mks))
                    {
                        ViewerContentPresenter.AddContentTab(resTabs, "Reconstructed mks", new ViewerContent.Text(mks));
                    }

                    break;
                }

                case ResourceType.ParticleSnapshot:
                {
                    if (!FileExtract.IsChildResource(resource))
                    {
                        ViewerContentPresenter.AddContentTab(resTabs, "Reconstructed vsnap", new ViewerContent.Text(new SnapshotExtract(resource).ToValveSnap()));
                    }

                    break;
                }
            }
        }

        private void DisposeExtraGraphViewers()
        {
            foreach (var (viewer, _) in preparedGraphViewers)
            {
                viewer.Dispose();
            }

            preparedGraphViewers.Clear();
        }

        public void Dispose()
        {
            // Order matters: nothing may dispose a resource until every thread that could still be
            // reading it has stopped
            GLViewer?.Dispose();
            rendererContext?.Dispose();
            resource?.Dispose();

            DisposeExtraGraphViewers();

            foreach (var control in ownedControls)
            {
                control.Dispose();
            }

            ownedControls.Clear();
        }
    }
}
