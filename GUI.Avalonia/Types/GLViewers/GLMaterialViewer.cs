using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GUI.Controls;
using GUI.Types.Viewers;
using GUI.Utils;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Materials;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using Resource = ValveResourceFormat.Resource;

namespace GUI.Types.GLViewers
{
    /// <summary>
    /// GL Render control with material controls and render mode selection.
    /// Port of the WinForms GLMaterialViewer, the scene logic is kept in step with it and only the UI differs.
    /// </summary>
    class GLMaterialViewer : GLSingleNodeViewer
    {
        private enum ParamType
        {
            Float,
            Int,
            Vector,
            Bool,
            Color,
        }

        private enum ParameterPresence
        {
            MaterialOnly,
            ShaderOnly,
            Both
        }

        private readonly Resource Resource;
        private TabControl? Tabs;
        private Button? openShaderButton;
        private StackPanel? ParamsTable;
        private RenderMaterial? renderMat;
        private ComboBox? previewObjectComboBox;

        private enum PreviewObjectType
        {
            Quad,
            Sphere,
            CustomModel
        }

        private PreviewObjectType currentPreviewObject = PreviewObjectType.Quad;
        private readonly Dictionary<PreviewObjectType, MeshCollectionNode> previewObjects = [];
        private MeshCollectionNode previewNode => previewObjects[currentPreviewObject];
        private ShaderCollection? vcsShader;

        public GLMaterialViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, Resource resource) : base(vrfGuiContext, rendererContext)
        {
            Resource = resource;
        }

        public void SetTabControl(TabControl tabs)
        {
            Tabs = tabs;
        }

        public override void Dispose()
        {
            vcsShader?.Dispose();

            base.Dispose();
        }

        protected override void LoadScene()
        {
            base.LoadScene();

            Scene.ShowToolsMaterials = true;
            renderMat = Renderer.RendererContext.MaterialLoader.LoadMaterial(Resource, Scene.RenderAttributes);
            renderMat.Shader.EnsureLoaded();
            renderMat.IsOverlay = false; // render without trying to overlay on empty space

            {
                var planeMesh = MeshSceneNode.CreateMaterialPreviewQuad(Scene, renderMat, new Vector2(32));
                planeMesh.Transform = Matrix4x4.CreateRotationZ(float.DegreesToRadians(90f));

                var isHorizontalPlaneMaterial = renderMat.IsCs2Water;
                if (!isHorizontalPlaneMaterial)
                {
                    planeMesh.Transform *= Matrix4x4.CreateRotationY(float.DegreesToRadians(90f));
                }

                Scene.Add(planeMesh, false);
                previewObjects[PreviewObjectType.Quad] = planeMesh;
            }

            {
                var sphereMesh = ShapeSceneNode.CreateEnvCubemapSphere(Scene);
                foreach (var renderable in sphereMesh.RenderableMeshes)
                {
                    renderable.SetMaterialForMaterialViewer(Resource);
                }

                Scene.Add(sphereMesh, false);
                previewObjects[PreviewObjectType.Sphere] = sphereMesh;
            }

            if (Resource.DataBlock is Material material && material.StringAttributes.TryGetValue("PreviewModel", out var previewModel))
            {
                var previewModelResource = GuiContext.LoadFileCompiled(previewModel);

                if (previewModelResource != null && previewModelResource.DataBlock is Model modelData)
                {
                    var customModel = new ModelSceneNode(Scene, modelData);

                    foreach (var renderable in customModel.RenderableMeshes)
                    {
                        renderable.SetMaterialForMaterialViewer(Resource);
                    }

                    Scene.Add(customModel, false);
                    previewObjects[PreviewObjectType.CustomModel] = customModel;
                }
            }

            vcsShader = GuiContext.LoadShader(renderMat.Material.ShaderName);
        }

        private void CreateMaterialEditControls()
        {
            Debug.Assert(ParamsTable != null);

            var mesh = previewNode.RenderableMeshes[0];
            var drawCall = mesh.DrawCallsOpaque.Concat(mesh.DrawCallsBlended).First();

            // Collect all parameters with their types and sort them together
            var allParams = new List<(string name, object value, ParamType type, VfxVariableDescription? vfx)>();

            var materialParams = drawCall.Material;
            var shaderParams = drawCall.Material.Shader.Default;

            var allParameterNames = new HashSet<string>(materialParams.FloatParams.Keys);
            allParameterNames.UnionWith(materialParams.IntParams.Keys);
            allParameterNames.UnionWith(materialParams.VectorParams.Keys);
            allParameterNames.UnionWith(shaderParams.FloatParams.Keys);
            allParameterNames.UnionWith(shaderParams.IntParams.Keys);
            allParameterNames.UnionWith(shaderParams.VectorParams.Keys);

            var vcsDescriptionByName = new Dictionary<string, VfxVariableDescription>();
            if (vcsShader?.Features != null)
            {
                foreach (var varDesc in vcsShader.Features.VariableDescriptions)
                {
                    vcsDescriptionByName[varDesc.Name] = varDesc;
                }
            }

            foreach (var paramName in allParameterNames)
            {
                var inMaterial = materialParams.FloatParams.ContainsKey(paramName) ||
                    materialParams.IntParams.ContainsKey(paramName) ||
                    materialParams.VectorParams.ContainsKey(paramName);
                var inShader = shaderParams.FloatParams.ContainsKey(paramName) ||
                    shaderParams.IntParams.ContainsKey(paramName) ||
                    shaderParams.VectorParams.ContainsKey(paramName);
                var parameterPresence = (inShader, inMaterial) switch
                {
                    (false, true) => ParameterPresence.MaterialOnly,
                    (true, false) => ParameterPresence.ShaderOnly,
                    _ => ParameterPresence.Both,
                };

                var vfxDescription = vcsDescriptionByName.GetValueOrDefault(paramName);

                if (parameterPresence == ParameterPresence.ShaderOnly && vcsDescriptionByName.Count > 0 && vfxDescription == null)
                {
                    continue;
                }

                // Handle float parameters
                if (materialParams.FloatParams.ContainsKey(paramName) || shaderParams.FloatParams.ContainsKey(paramName))
                {
                    var value = materialParams.FloatParams.GetValueOrDefault(paramName,
                        shaderParams.FloatParams.GetValueOrDefault(paramName));
                    allParams.Add((paramName, (value, parameterPresence), ParamType.Float, vfxDescription));
                    continue;
                }

                // Handle int/bool parameters
                if (materialParams.IntParams.ContainsKey(paramName) || shaderParams.IntParams.ContainsKey(paramName))
                {
                    var value = materialParams.IntParams.GetValueOrDefault(paramName,
                        shaderParams.IntParams.GetValueOrDefault(paramName));

                    if (drawCall.Material.Shader.IsBooleanParameter(paramName)
                        || paramName.StartsWith("F_", StringComparison.OrdinalIgnoreCase) && value is 0 or 1)
                    {
                        var boolValue = Convert.ToBoolean(value);
                        allParams.Add((paramName, (boolValue, parameterPresence), ParamType.Bool, vfxDescription));
                    }
                    else
                    {
                        var int32Value = Convert.ToInt32(value);
                        allParams.Add((paramName, (int32Value, parameterPresence), ParamType.Int, vfxDescription));
                    }
                    continue;
                }

                // Handle vector parameters
                if (materialParams.VectorParams.ContainsKey(paramName) || shaderParams.VectorParams.ContainsKey(paramName))
                {
                    var value = materialParams.VectorParams.GetValueOrDefault(paramName,
                        shaderParams.VectorParams.GetValueOrDefault(paramName));
                    var componentCount = drawCall.Material.Shader.GetRegisterSize(paramName);

                    if (vfxDescription?.UiType == UiType.Color)
                    {
                        value.W = 1f;
                        allParams.Add((paramName, (value, parameterPresence), ParamType.Color, vfxDescription));
                    }
                    else
                    {
                        allParams.Add((paramName, (value, componentCount, parameterPresence), ParamType.Vector, vfxDescription));
                    }
                }
            }

            // Sort and group parameters
            var sortedParams = allParams
                .OrderBy(p => p.vfx?.UiGroup.Heading)
                .ThenBy(p => p.vfx?.UiGroup.HeadingOrder)
                .ThenBy(p => p.vfx?.UiGroup.Group)
                .ThenBy(p => p.vfx?.UiGroup.GroupOrder)
                .ThenBy(p => p.vfx?.UiGroup.VariableOrder)
                .ThenBy(p => p.name)
                .ToList();

            var currentHeading = string.Empty;

            // Add parameters to UI with layer headers
            foreach (var (paramName, value, type, vfxDescription) in sortedParams)
            {
                if (vfxDescription != null && vfxDescription.UiGroup.Heading != currentHeading)
                {
                    currentHeading = vfxDescription.UiGroup.Heading;

                    ParamsTable.Children.Add(new Border
                    {
                        Background = new SolidColorBrush(Colors.Gray, 0.25),
                        Padding = new(10, 5, 0, 5),
                        Margin = new(0, 6, 0, 2),
                        Child = new TextBlock { Text = currentHeading, FontWeight = FontWeight.Bold },
                    });
                }

                switch (type)
                {
                    case ParamType.Float:
                        var (floatVal, floatPresence) = ((float, ParameterPresence))value;
                        Vector2 range = vfxDescription != null
                            ? new(vfxDescription.FloatMins[0], vfxDescription.FloatMaxs[0])
                            : new(Math.Min(floatVal, 0), Math.Max(floatVal, 1));
                        AddNumericParameter(
                            paramName,
                            floatVal,
                            range,
                            ParamType.Float,
                            v => drawCall.Material.FloatParams[paramName] = (float)v,
                            floatPresence != ParameterPresence.MaterialOnly);
                        break;
                    case ParamType.Int:
                        var (intVal, intPresence) = ((int, ParameterPresence))value;
                        Vector2 rangeInt = vfxDescription != null
                            ? new(vfxDescription.IntMins[0], vfxDescription.IntMaxs[0])
                            : new(Math.Min(intVal, 0), Math.Max(intVal, 1));
                        AddNumericParameter(
                            paramName,
                            intVal,
                            rangeInt,
                            ParamType.Int,
                            v =>
                            {
                                drawCall.Material.IntParams[paramName] = (int)v;
                                drawCall.Material.LoadRenderState();
                            },
                            intPresence != ParameterPresence.MaterialOnly);
                        break;
                    case ParamType.Bool:
                        var (boolVal, boolPresence) = ((bool, ParameterPresence))value;
                        AddBooleanParameter(
                            paramName,
                            boolVal,
                            v =>
                            {
                                drawCall.Material.IntParams[paramName] = v ? 1 : 0;
                                drawCall.Material.LoadRenderState();
                            },
                            boolPresence != ParameterPresence.MaterialOnly);
                        break;
                    case ParamType.Vector:
                        var (vector, count, vectorPresence) = ((Vector4, int, ParameterPresence))value;
                        AddVectorParameter(
                            paramName,
                            count,
                            vector,
                            v => drawCall.Material.VectorParams[paramName] = v,
                            vectorPresence != ParameterPresence.MaterialOnly);
                        break;

                    case ParamType.Color:
                        var (colorVec, colorPresence) = ((Vector4, ParameterPresence))value;
                        AddColorParameter(
                            paramName,
                            Vector4ToColor(colorVec),
                            c => drawCall.Material.VectorParams[paramName] = ColorToVector4(c),
                            colorPresence != ParameterPresence.MaterialOnly);
                        break;
                }
            }
        }

        /// <summary>A label and an editor side by side, the editor dimmed when it has no effect on this material.</summary>
        private void AddParameterRow(string paramName, Control editor, bool isEnabled)
        {
            Debug.Assert(ParamsTable != null);

            var label = new TextBlock
            {
                Text = NormalizeParameterName(paramName),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new(0, 0, 8, 0),
                Opacity = isEnabled ? 1 : 0.5,
            };
            ToolTip.SetTip(label, paramName);

            editor.IsEnabled = isEnabled;

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), MinHeight = 30 };
            Grid.SetColumn(editor, 1);
            row.Children.Add(label);
            row.Children.Add(editor);

            ParamsTable.Children.Add(row);
        }

        private void AddBooleanParameter(string paramName, bool initialValue, Action<bool> onValueChanged, bool isEnabled = true)
        {
            var checkbox = new CheckBox
            {
                IsChecked = initialValue,
                Margin = new(10, 0, 0, 0),
            };

            checkbox.IsCheckedChanged += (sender, e) =>
            {
                onValueChanged(checkbox.IsChecked == true);
            };

            AddParameterRow(paramName, checkbox, isEnabled);
        }

        private static NumericUpDown CreateNumeric(double value, double min, double max, bool isInteger) => new()
        {
            Minimum = (decimal)Math.Max(min, (double)decimal.MinValue / 2),
            Maximum = (decimal)Math.Min(max, (double)decimal.MaxValue / 2),
            Value = (decimal)value,
            Increment = isInteger ? 1 : 0.01m,
            FormatString = isInteger ? "0" : "0.###",
            ShowButtonSpinner = false,
            MinWidth = 0,
            Padding = new(4, 0),
        };

        private void AddVectorParameter(string paramName, int componentCount, Vector4 value, Action<Vector4> onValueChanged, bool isEnabled = true)
        {
            var inputRow = new Avalonia.Controls.Primitives.UniformGrid { Columns = componentCount, Rows = 1 };

            // Add Numeric controls for vector components
            var inputs = new NumericUpDown[componentCount];
            for (var i = 0; i < componentCount; i++)
            {
                var index = i; // Capture for lambda
                var input = CreateNumeric(index == 0 ? value.X : index == 1 ? value.Y : index == 2 ? value.Z : value.W, float.MinValue, float.MaxValue, isInteger: false);

                input.ValueChanged += (sender, e) =>
                {
                    float Component(int c) => (float)(inputs[c].Value ?? 0);

                    // Keep existing W value for vec2/vec3, or use 1.0 as default
                    var w = componentCount < 4 ? (componentCount == 3 ? 1.0f : value.W) : Component(3);
                    var newVector = new Vector4(
                        Component(0),
                        componentCount > 1 ? Component(1) : value.Y,
                        componentCount > 2 ? Component(2) : value.Z,
                        w
                    );
                    onValueChanged(newVector);
                };

                inputs[i] = input;
                inputRow.Children.Add(input);
            }

            AddParameterRow(paramName, inputRow, isEnabled);
        }

        private static string NormalizeParameterName(string paramNameString)
        {
            // Handle feature flags (F_ prefix) - all uppercase, split by underscores
            if (paramNameString.StartsWith("F_", StringComparison.Ordinal))
            {
                return paramNameString[2..].Replace('_', ' ');
            }

            var paramName = paramNameString.AsSpan();

            foreach (var prefix in VfxVariableDescription.TypePrefixes)
            {
                if (paramName.StartsWith(prefix))
                {
                    paramName = paramName[prefix.Length..];
                    break;
                }
            }

            var result = new System.Text.StringBuilder();

            for (var i = 0; i < paramName.Length; i++)
            {
                var c = paramName[i];

                // Replace underscores with spaces
                if (c == '_')
                {
                    result.Append(' ');
                }
                // Add space before capital letters only if preceded by lowercase
                else if (char.IsUpper(c) && i > 0 && result.Length > 0 && char.IsLower(paramName[i - 1]))
                {
                    result.Append(' ');
                    result.Append(c);
                }
                else
                {
                    result.Append(c);
                }
            }

            return result.ToString().Trim();
        }

        private void AddColorParameter(string paramName, Color initialColor, Action<Color> onValueChanged, bool isEnabled = true)
        {
            AddParameterRow(paramName, CreateColorPicker(initialColor, onValueChanged), isEnabled);
        }

        private static ColorPicker CreateColorPicker(Color initialColor, Action<Color> onValueChanged)
        {
            var picker = new ColorPicker
            {
                Color = initialColor,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };

            picker.ColorChanged += (_, e) => onValueChanged(e.NewColor);
            return picker;
        }

        private void AddNumericParameter(string paramName, float initialValue, Vector2 range, ParamType paramType, Action<float> onValueChanged, bool isEnabled = true)
        {
            var min = range.X;
            var max = range.Y;
            if (float.IsNaN(min) || float.IsNaN(max) || min > max)
            {
                min = Math.Min(initialValue, 0);
                max = Math.Max(initialValue, 1);
            }

            if (paramType != ParamType.Float)
            {
                var intMin = (int)Math.Floor(min);
                var intMax = (int)Math.Ceiling(max);
                if (intMax < intMin)
                {
                    intMax = intMin;
                }

                var intInput = CreateNumeric(Math.Clamp((int)initialValue, intMin, intMax), intMin, intMax, isInteger: true);
                intInput.ValueChanged += (sender, e) => onValueChanged((float)(intInput.Value ?? 0));

                AddParameterRow(paramName, intInput, isEnabled);
                return;
            }

            var clampedInitial = Math.Clamp(initialValue, min, max);

            var slider = new Avalonia.Controls.Slider
            {
                Minimum = min,
                Maximum = max > min ? max : min + 0.001,
                Value = clampedInitial,
                VerticalAlignment = VerticalAlignment.Center,
                // If the range is degenerate, disable the slider UI
                IsEnabled = Math.Abs(max - min) >= 1e-6f,
            };

            var input = CreateNumeric(clampedInitial, min, max, isInteger: false);

            var updating = false;

            slider.ValueChanged += (sender, e) =>
            {
                if (!updating)
                {
                    updating = true;
                    input.Value = (decimal)e.NewValue;
                    updating = false;
                }
            };

            input.ValueChanged += (sender, e) =>
            {
                var v = Math.Clamp((float)(input.Value ?? 0), min, max);
                onValueChanged(v);

                if (!updating)
                {
                    updating = true;
                    slider.Value = v;
                    updating = false;
                }
            };

            var inputRow = new Grid { ColumnDefinitions = new ColumnDefinitions("7*,3*") };
            Grid.SetColumn(input, 1);
            inputRow.Children.Add(slider);
            inputRow.Children.Add(input);

            AddParameterRow(paramName, inputRow, isEnabled);
        }

        public override void PostSceneLoad()
        {
            base.PostSceneLoad();
            sunAngles = new Vector2(19, 196);

            Input.OrbitModeAlways = true;
            Input.OrbitTarget = Vector3.Zero;

            UpdateSunAngles();
            Scene.UpdateBuffers();
        }

        protected override void OnFirstPaint()
        {
            Input.Camera.FrameObjectFromAngle(Vector3.Zero, 0, 32, 32, float.DegreesToRadians(180f), 0);
            if (renderMat != null && renderMat.IsCs2Water)
            {
                Input.Camera.FrameObjectFromAngle(Vector3.Zero, 32, 32, 0, 0, float.DegreesToRadians(90f));
            }

            if (previewNode != null)
            {
                Input.OrbitTarget = previewNode.BoundingBox.Center;
            }

            Input.ForceUpdate = true;
        }

        private void OnShadersButtonClick(object? s, EventArgs e)
        {
            if (Resource.DataBlock is not Material material)
            {
                return;
            }

            var featureState = ShaderDataProvider.GetMaterialFeatureState(material);

            if (Tabs == null || vcsShader == null)
            {
                // todo: open in new tab when we're in preview
                return;
            }

            var viewer = new CompiledShader(GuiContext);

            try
            {
                var tabPage = new TabItem
                {
                    Header = material.ShaderName,
                    Content = viewer.Create(
                        vcsShader,
                        Path.GetFileNameWithoutExtension(material.ShaderName.AsSpan()),
                        ValveResourceFormat.CompiledShader.VcsProgramType.Features,
                        featureState
                    ),
                };
                viewer = null;

                Tabs.Items.Add(tabPage);
                Tabs.SelectedItem = tabPage;
            }
            finally
            {
                viewer?.Dispose();
            }
        }

        private void AddShaderButton()
        {
            Debug.Assert(UiControl != null);

            openShaderButton = new Button
            {
                Content = "Open Shader",
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };

            openShaderButton.Click += OnShadersButtonClick;

            UiControl.AddControl(openShaderButton);
        }

        static Color Vector4ToColor(Vector4 v)
        {
            return Color.FromArgb(
                (byte)Math.Clamp(v.W * 255, 0, 255),
                (byte)Math.Clamp(v.X * 255, 0, 255),
                (byte)Math.Clamp(v.Y * 255, 0, 255),
                (byte)Math.Clamp(v.Z * 255, 0, 255));
        }

        private void RenderMeshPreview_SelectionChanged(object? sender, EventArgs e)
        {
            Debug.Assert(previewObjectComboBox != null);

            foreach (var node in previewObjects.Values)
            {
                node.LayerEnabled = false;
            }

            if (Scene != null && previewObjectComboBox.SelectedIndex >= 0)
            {
                currentPreviewObject = Enum.Parse<PreviewObjectType>(previewObjectComboBox.SelectedItem?.ToString() ?? string.Empty);
                previewNode.LayerEnabled = true;
                OnFirstPaint();
            }
        }

        static Vector4 ColorToVector4(Color c)
        {
            return Vector4.Create(c.R, c.G, c.B, c.A) / 255f;
        }

        protected override void AddUiControls()
        {
            Debug.Assert(UiControl != null);

            // Make controls panel wider for material parameters
            UiControl.UseWideSplitter();

            AddRenderModeSelectionControl();
            UiControl.AddDivider();

            AddShaderButton();
            UiControl.AddDivider();

            previewObjectComboBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            previewObjectComboBox.SelectionChanged += RenderMeshPreview_SelectionChanged;

            var previewControls = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*"),
                RowDefinitions = new RowDefinitions("Auto,Auto"),
                RowSpacing = 4,
            };

            static TextBlock RightLabel(string text) => new()
            {
                Text = text,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new(0, 0, 8, 0),
            };

            var colorLabel = RightLabel("Render Color");
            var colorPicker = CreateColorPicker(Colors.White, pickedColor =>
            {
                if (previewNode != null)
                {
                    previewNode.Tint = ColorToVector4(pickedColor);
                }
            });
            var meshLabel = RightLabel("Render Mesh");

            Grid.SetColumn(colorPicker, 1);
            Grid.SetRow(meshLabel, 1);
            Grid.SetRow(previewObjectComboBox, 1);
            Grid.SetColumn(previewObjectComboBox, 1);

            previewControls.Children.Add(colorLabel);
            previewControls.Children.Add(colorPicker);
            previewControls.Children.Add(meshLabel);
            previewControls.Children.Add(previewObjectComboBox);

            UiControl.AddControl(previewControls);

            ParamsTable = new StackPanel();

            UiControl.AddDivider();
            UiControl.AddControl(ParamsTable);
            UiControl.AddDivider();

            // Populate UI controls with scene data
            if (renderMat != null)
            {
                if (openShaderButton != null)
                {
                    openShaderButton.Content = renderMat.Material.ShaderName;
                }

                var selectedIndex = (int)PreviewObjectType.Quad;
                if (Resource.DataBlock is Material material && material.StringAttributes.ContainsKey("PreviewModel") && previewObjects.ContainsKey(PreviewObjectType.CustomModel))
                {
                    selectedIndex = (int)PreviewObjectType.CustomModel;
                }

                previewObjectComboBox.ItemsSource = Enum.GetNames<PreviewObjectType>().Where(n => previewObjects.ContainsKey(Enum.Parse<PreviewObjectType>(n))).ToList();
                previewObjectComboBox.SelectedIndex = selectedIndex;

                CreateMaterialEditControls();
            }

            base.AddUiControls();
        }
    }
}
