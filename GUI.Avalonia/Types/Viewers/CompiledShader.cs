using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AvaloniaEdit;
using GUI.Controls;
using GUI.Utils;
using ValveResourceFormat.IO;
using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;

namespace GUI.Types.Viewers
{
    class CompiledShader : IDisposable, IViewer
    {
        private readonly VrfGuiContext vrfGuiContext;
        private readonly TreeView fileListView;
        private readonly TextEditor textBox;
        private VfxProgramData? featuresProgram;

        public static bool IsAccepted(uint magic)
        {
            return magic == VfxProgramData.MAGIC;
        }

        public CompiledShader(VrfGuiContext vrfGuiContext)
        {
            this.vrfGuiContext = vrfGuiContext;

            fileListView = new TreeView();
            fileListView.SelectionChanged += OnSelectionChanged;

            var exportBytecode = new MenuItem { Header = "Export bytecode" };
            exportBytecode.Click += OnExportBytecodeClick;
            fileListView.ContextMenu = new ContextMenu { Items = { exportBytecode } };
            fileListView.ContextMenu.Opening += (_, e) =>
            {
                e.Cancel = fileListView.SelectedItem is not TreeViewItem { Tag: VfxShaderFile { Bytecode.Length: > 0 } };
            };

            textBox = CodeTextBox.Create(string.Empty, HighlightLanguage.Shaders);
        }

        public async Task LoadAsync(Stream? stream)
        {
            // Creating shader collection doesn't actually use the provided stream which is kind of a waste
            if (stream is not null)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }

        public Control Create()
        {
            var filename = Path.GetFileName(vrfGuiContext.FileName);
            var leadProgramType = ComputeVCSFileName(filename).ProgramType;
            var vcsCollectionName = filename.AsSpan(0, filename.LastIndexOf('_')); // in the form water_dota_pcgl_40

            var shaderCollection = ShaderCollection.GetShaderCollection(vrfGuiContext.FileName, vrfGuiContext.CurrentPackage);

            return Create(shaderCollection, vcsCollectionName, leadProgramType);
        }

        public Control Create(ShaderCollection shaderCollection, ReadOnlySpan<char> vcsCollectionName, VcsProgramType leadProgramType, IDictionary<string, byte>? leadFeatureParams = null)
        {
            var materialCollectionIndex = 0;
            var collectionNode = new TreeViewItem { Header = $"{vcsCollectionName}.vfx" };
            fileListView.Items.Add(collectionNode);

            List<string> comboNamesAbbrev = [];
            List<string> comboNames = [];

            Debug.Assert(shaderCollection.Features != null);
            featuresProgram = shaderCollection.Features;

            foreach (var program in shaderCollection.OrderBy(static x => x.VcsProgramType))
            {
                var truncatedProgramName = program.FilenamePath.AsSpan();

                if (truncatedProgramName.StartsWith(vcsCollectionName))
                {
                    truncatedProgramName = truncatedProgramName[(vcsCollectionName.Length + 1)..];
                }

                var programNode = new TreeViewItem { Header = truncatedProgramName.ToString(), Tag = program };
                collectionNode.Items.Add(programNode);

                if (program.StaticComboEntries.Count == 0)
                {
                    continue;
                }

                var configMapping = new ComboConfigMapping(program);
                var leadStaticComboId = -1L; // Shader file to be displayed for a particular material

                if (leadFeatureParams != null)
                {
                    leadStaticComboId = ShaderDataProvider.GetStaticConfiguration_ForFeatureState(shaderCollection.Features, program, leadFeatureParams).StaticComboId;
                }

                foreach (var staticComboEntry in program.StaticComboEntries)
                {
                    var config = configMapping.GetConfigState(staticComboEntry.Key);

                    comboNames.Clear();
                    comboNamesAbbrev.Clear();

                    for (var i = 0; i < program.StaticComboArray.Length; i++)
                    {
                        if (config[i] == 0)
                        {
                            continue;
                        }

                        var staticCombo = program.StaticComboArray[i];
                        var shortName = ShortenShaderParam(staticCombo.Name).ToLowerInvariant();

                        if (config[i] > 1)
                        {
                            comboNames.Add($"{staticCombo.Name}={config[i]}");
                            comboNamesAbbrev.Add($"{shortName}={config[i]}");
                        }
                        else
                        {
                            comboNames.Add(staticCombo.Name);
                            comboNamesAbbrev.Add(shortName);
                        }
                    }

                    var variantsAbbrev = comboNamesAbbrev.Count > 0 ? $" ({string.Join(", ", comboNamesAbbrev)})" : string.Empty;
                    var variantsTooltip = string.Join(Environment.NewLine, comboNames);

                    var comboNode = new TreeViewItem { Header = $"{staticComboEntry.Key:x08}{variantsAbbrev}", Tag = staticComboEntry.Value };
                    SetTip(comboNode, variantsTooltip);
                    programNode.Items.Add(comboNode);

                    if (staticComboEntry.Key != leadStaticComboId)
                    {
                        continue;
                    }

                    // When viewing from a material, unserialize the correct static combo straight away
                    var combo = staticComboEntry.Value.Unserialize();
                    comboNode.Tag = combo;
                    CreateStaticComboNodes(combo, comboNode);

                    // Program files that do not store any ShaderFiles
                    if (program.VcsProgramType == VcsProgramType.PixelShaderRenderState)
                    {
                        continue;
                    }

                    var shaderFile = combo.ShaderFiles[0];

                    if (shaderFile.Bytecode.Length > 0)
                    {
                        var matNode = new TreeViewItem { Header = $"Material {program.VcsProgramType}{variantsAbbrev}", Tag = combo.ShaderFiles[0] };
                        SetTip(matNode, variantsTooltip);
                        collectionNode.Items.Insert(materialCollectionIndex++, matNode);
                    }
                }

                if (program.VcsProgramType == leadProgramType)
                {
                    programNode.IsExpanded = true;
                }
            }

            collectionNode.IsExpanded = true;

            // ShaderExtract cannot continue without the features file present
            if (shaderCollection.Features is not null)
            {
                var shaderExtract = new ShaderExtract(shaderCollection);

                collectionNode.Tag = shaderExtract;
                DisplayExtractedVfx(shaderExtract);
            }

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("320,4,*") };
            var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns };
            Grid.SetColumn(splitter, 1);
            Grid.SetColumn(textBox, 2);
            grid.Children.Add(fileListView);
            grid.Children.Add(splitter);
            grid.Children.Add(textBox);
            return grid;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }

        private static void SetTip(Control control, string tip)
        {
            if (tip.Length > 0)
            {
                ToolTip.SetTip(control, tip);
            }
        }

        private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (fileListView.SelectedItem is not TreeViewItem node)
            {
                return;
            }

            switch (node.Tag)
            {
                case ShaderExtract shaderExtract:
                    DisplayExtractedVfx(shaderExtract);
                    break;

                case VfxProgramData program:
                {
                    using var output = new IndentedTextWriter();
                    program.PrintSummary(output, featuresProgram);
                    textBox.Text = output.ToString();
                    break;
                }

                case VfxStaticComboVcsEntry comboEntry:
                {
                    var combo = comboEntry.Unserialize();
                    node.Tag = combo; // Replace the entry with unserialized combo data

                    DisplayStaticCombo(combo);
                    CreateStaticComboNodes(combo, node);
                    break;
                }

                case VfxStaticComboData combo:
                    DisplayStaticCombo(combo);
                    break;

                case VfxShaderFile shaderFile:
                    textBox.Text = shaderFile.GetDecompiledFile();
                    break;
            }
        }

        private static void CreateStaticComboNodes(VfxStaticComboData combo, TreeViewItem treeNode)
        {
            List<string> comboNamesAbbrev = [];
            List<string> comboNames = [];
            var shaderFileIdToRenderStateInfo = new Dictionary<int, VfxRenderStateInfo>(combo.ShaderFiles.Length);

            // We are only taking the first render state info currently
            foreach (var renderStateInfo in combo.DynamicComboRenderStates)
            {
                shaderFileIdToRenderStateInfo.TryAdd(renderStateInfo.ShaderFileId, renderStateInfo);
            }

            Debug.Assert(combo.ParentProgramData != null);

            foreach (var shaderFile in combo.ShaderFiles)
            {
                // KV3 resources may leave unreferenced shader file slots null
                if (shaderFile is null || shaderFile.Size == 0)
                {
                    continue;
                }

                var config = combo.ParentProgramData.GetDynamicComboConfig(shaderFileIdToRenderStateInfo[shaderFile.ShaderFileId].DynamicComboId);

                comboNames.Clear();
                comboNamesAbbrev.Clear();

                for (var i = 0; i < combo.ParentProgramData.DynamicComboArray.Length; i++)
                {
                    if (config[i] == 0)
                    {
                        continue;
                    }

                    var dynamicCombo = combo.ParentProgramData.DynamicComboArray[i];
                    var shortName = ShortenShaderParam(dynamicCombo.Name).ToLowerInvariant();

                    if (config[i] > 1)
                    {
                        comboNames.Add($"{dynamicCombo.Name}={config[i]}");
                        comboNamesAbbrev.Add($"{shortName}={config[i]}");
                    }
                    else
                    {
                        comboNames.Add(dynamicCombo.Name);
                        comboNamesAbbrev.Add(shortName);
                    }
                }

                var node = new TreeViewItem
                {
                    Header = $"{shaderFile.ShaderFileId:X2}{(comboNamesAbbrev.Count > 0 ? $" ({string.Join(", ", comboNamesAbbrev)})" : string.Empty)}",
                    Tag = shaderFile,
                };
                SetTip(node, string.Join(Environment.NewLine, comboNames));
                treeNode.Items.Add(node);
            }

            treeNode.IsExpanded = true;
        }

        private void DisplayExtractedVfx(ShaderExtract shaderExtract)
        {
            try
            {
                textBox.Text = shaderExtract.ToVFX();
            }
            catch (Exception ex)
            {
                textBox.Text = ex.ToString();
            }
        }

        private void DisplayStaticCombo(VfxStaticComboData combo)
        {
            using var output = new IndentedTextWriter();
            _ = new PrintStaticComboSummary(combo, output);
            textBox.Text = output.ToString();
        }

        private async void OnExportBytecodeClick(object? sender, RoutedEventArgs e)
        {
            if (fileListView.SelectedItem is not TreeViewItem { Tag: VfxShaderFile shaderFile })
            {
                return;
            }

            var combo = shaderFile.ParentCombo;
            Debug.Assert(combo.ParentProgramData != null);

            var extension = shaderFile.SourceType == "VULKAN" ? "spv" : shaderFile.SourceType.ToLowerInvariant();

            var (fileName, _) = await AppFileDialogs.SaveFileAsync(
                "Export bytecode",
                $"{combo.ParentProgramData.ShaderName}_{combo.StaticComboId:x08}_{shaderFile.ShaderFileId:x02}",
                extension,
                $"{shaderFile.SourceType} bytecode (*.{extension})|*.{extension}|All files (*.*)|*.*").ConfigureAwait(true);

            if (fileName == null)
            {
                return;
            }

            await File.WriteAllBytesAsync(fileName, shaderFile.Bytecode).ConfigureAwait(true);
        }
    }
}
