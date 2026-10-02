using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using GUI.Controls;
using GUI.Utils;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Upgrade;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Particles;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.GLViewers
{
    /// <summary>
    /// Renders a single <see cref="ParticleSystem"/> via a <see cref="ParticleSceneNode"/>, with UI controls for playback and an operator/renderer tree.
    /// Port of the WinForms GLParticleViewer, the scene logic is kept in step with it and only the UI differs.
    /// </summary>
    class GLParticleViewer : GLSceneViewer
    {
        private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
        private static readonly IBrush UnsupportedColor = new ImmutableSolidColorBrush(Color.FromRgb(224, 80, 80));
        private static readonly IBrush RemovedColor = new ImmutableSolidColorBrush(Color.FromRgb(140, 140, 140));
        private static readonly IBrush LinkColorDark = new ImmutableSolidColorBrush(Color.FromRgb(99, 161, 255));
        private static readonly IBrush LinkColorLight = new ImmutableSolidColorBrush(Color.FromRgb(0, 102, 204));
        private static readonly IBrush DisabledLinkColorDark = new ImmutableSolidColorBrush(Color.FromRgb(122, 138, 160));
        private static readonly IBrush DisabledLinkColorLight = new ImmutableSolidColorBrush(Color.FromRgb(112, 128, 148));

        // Order matches the CS2 particle editor (PET): pre-emission first, then emit/init/operate,
        // forces, constraints, and renderers last.
        private static readonly (string Title, string ListName, Func<string, bool> IsSupported)[] FunctionGroups =
        [
            ("Pre-Emission Operators", "m_PreEmissionOperators", ParticleSupportInfo.IsPreEmissionOperatorSupported),
            ("Emitters", "m_Emitters", ParticleSupportInfo.IsEmitterSupported),
            ("Initializers", "m_Initializers", ParticleSupportInfo.IsInitializerSupported),
            ("Operators", "m_Operators", ParticleSupportInfo.IsOperatorSupported),
            ("Force Generators", "m_ForceGenerators", ParticleSupportInfo.IsForceGeneratorSupported),
            ("Constraints", "m_Constraints", ParticleSupportInfo.IsConstraintSupported),
            ("Renderers", "m_Renderers", ParticleRendererFactory.IsSupported),
        ];

        private readonly ParticleSystem particleSystem;
        private readonly ParticleSnapshot? particleSnapshot;
        private IReadOnlyDictionary<string, IReadOnlyList<ParticleUpgradeTrace.TracedFunction>>? functionLists;
        private ParticleSceneNode? particleSceneNode;
        private GLViewerSliderControl? slowmodeTrackBar;
        private Button? restartButton;
        private Button? pauseButton;
        private Button? endCapButton;
        private float screenSize = SnapshotParticleSystem.DefaultScreenSize;
        private bool ShowRenderBounds { get; set; }

        public GLParticleViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, ParticleSystem particleSystem, ParticleSnapshot? particleSnapshot = null)
            : base(vrfGuiContext, rendererContext, Frustum.CreateEmpty())
        {
            this.particleSystem = particleSystem;
            this.particleSnapshot = particleSnapshot;
        }

        public override void Dispose()
        {
            base.Dispose();

            slowmodeTrackBar?.Dispose();
        }

        protected override void LoadScene()
        {
            InitializeSoundPlayer();
            LoadDefaultLighting();
            Scene.LightingInfo.UseSceneBoundsForSunLightFrustum = false;

            // Taken before the scene node reads the upgraded tree, so the chain runs once and the
            // function list describes the tree that is being simulated.
            functionLists = particleSystem.GetUpgradeTrace();

            particleSceneNode = new ParticleSceneNode(Scene, particleSystem, particleSnapshot, true)
            {
                Transform = Matrix4x4.Identity
            };

            if (particleSnapshot != null)
            {
                particleSceneNode.SetTextureOverride(Scene.RendererContext.MaterialLoader.GetDefaultColor());
            }

            Scene.Add(particleSceneNode, true);
        }

        protected override void OnGLLoad()
        {
            base.OnGLLoad();

            if (particleSnapshot != null)
            {
                var bounds = SnapshotParticleSystem.GetBounds(particleSnapshot);
                var size = bounds.Size;

                Input.Camera.FrameObject(bounds.Center, size.X, size.Y, size.Z);
                Input.OrbitTargetProvider = () => bounds.Center;

                ApplyScreenSize();
                return;
            }

            Input.Camera.SetLocation(new Vector3(200, 200, 200));
            Input.Camera.LookAt(Vector3.Zero);
        }

        private void ApplyScreenSize()
        {
            if (particleSceneNode != null && particleSnapshot != null && SnapshotParticleSystem.UsesConstantScreenSize(particleSnapshot))
            {
                SnapshotParticleSystem.SetScreenSize(particleSceneNode.GetControlPoint(SnapshotParticleSystem.ScreenSizeControlPoint), screenSize, Input.Camera.GetFOV());
            }
        }

        protected override void AddUiControls()
        {
            Debug.Assert(UiControl != null);
            Debug.Assert(SelectedNodeRenderer != null);

            AddRenderModeSelectionControl();

            var detailLevelComboBox = UiControl.AddSelection("Detail Level", (_, i) =>
            {
                if (i < 0)
                {
                    return;
                }

                using var lockedGl = MakeCurrent();
                particleSceneNode?.SetDetailLevel((ParticleDetailLevel)i);
                particleSceneNode?.Restart();
            }, horizontal: true, fill: true);
            detailLevelComboBox.Items.AddRange(["Low", "Medium", "High", "Ultra"]);
            detailLevelComboBox.SelectedIndex = (int)ParticleDetailLevel.PARTICLEDETAIL_ULTRA;

            AddBaseGridControl();

            restartButton = new Button
            {
                Content = "Restart",
            };
            restartButton.Click += (_, _) =>
            {
                using var lockedGl = MakeCurrent();
                particleSceneNode?.Restart();
            };

            pauseButton = new Button
            {
                Content = "Pause",
            };
            pauseButton.Click += (_, _) =>
            {
                if (particleSceneNode == null)
                {
                    return;
                }

                particleSceneNode.IsPaused = !particleSceneNode.IsPaused;
                pauseButton.Content = particleSceneNode.IsPaused ? "Resume" : "Pause";
            };

            endCapButton = new Button
            {
                Content = "Play Endcap",
            };
            endCapButton.Click += (_, _) =>
            {
                using var lockedGl = MakeCurrent();
                particleSceneNode?.PlayEndCap();
            };

            using (UiControl.BeginGroup("Playback"))
            {
                var playbackModeComboBox = UiControl.AddSelection("Mode", (_, i) =>
                {
                    if (i < 0 || particleSceneNode == null)
                    {
                        return;
                    }

                    using var lockedGl = MakeCurrent();
                    particleSceneNode.PlaybackMode = (ParticlePlaybackMode)i;
                    particleSceneNode.Restart();
                }, horizontal: true, fill: true);
                playbackModeComboBox.Items.AddRange(["Normal + Endcap", "Normal", "Endcap Only"]);
                playbackModeComboBox.SelectedIndex = (int)ParticlePlaybackMode.NormalWithEndCap;

                UiControl.AddCheckBox("Loop", true, value =>
                {
                    if (particleSceneNode == null)
                    {
                        return;
                    }

                    using var lockedGl = MakeCurrent();
                    particleSceneNode.Loop = value;
                    particleSceneNode.Restart();
                });

                UiControl.AddControl(new WrapPanel
                {
                    Children = { restartButton, pauseButton, endCapButton },
                    ItemSpacing = 4,
                    LineSpacing = 4,
                });

                slowmodeTrackBar = UiControl.AddTrackBar(value =>
                {
                    particleSceneNode?.FrametimeMultiplier = value;
                }, particleSceneNode?.FrametimeMultiplier ?? 1f);
            }

            using (UiControl.BeginGroup("Display"))
            {
                UiControl.AddCheckBox("Show Render Bounds", ShowRenderBounds, value => SelectedNodeRenderer.SelectNode(value ? particleSceneNode : null));

                // Only when the snapshot stores no radius, in which case the preview invents a size.
                if (particleSnapshot != null && SnapshotParticleSystem.UsesConstantScreenSize(particleSnapshot))
                {
                    UiControl.AddControl(RendererControl.CreateFloatInput("Point Size", value =>
                    {
                        screenSize = value / 100f;
                        ApplyScreenSize();
                    }, SnapshotParticleSystem.DefaultScreenSize * 100f, 0.05f, 5f));
                }
            }

            AddOperatorTree();

            base.AddUiControls();
        }

        private void AddOperatorTree()
        {
            Debug.Assert(UiControl != null);

            var lists = functionLists ?? particleSystem.GetUpgradeTrace();

            foreach (var (title, listName, isSupported) in FunctionGroups)
            {
                AddFunctionGroup(title, lists[listName], isSupported);
            }

            AddChildList();
        }

        private void AddChildList()
        {
            Debug.Assert(UiControl != null);

            var upgradedData = particleSystem.GetUpgradedData();
            var children = new List<ChildSystemItem>();

            foreach (var childInfo in upgradedData.GetArray("m_Children") ?? [])
            {
                var childRef = childInfo.GetStringProperty("m_ChildRef");

                if (string.IsNullOrEmpty(childRef))
                {
                    continue;
                }

                var disabled = !particleSystem.IsChildEnabled(childInfo);
                var shortName = Path.GetFileNameWithoutExtension(childRef);
                children.Add(new ChildSystemItem(disabled ? $"{shortName} (disabled)" : shortName, childRef, disabled));
            }

            if (children.Count == 0)
            {
                return;
            }

            var darkMode = Themer.IsDarkModeEnabled;
            var linkColor = darkMode ? LinkColorDark : LinkColorLight;
            var disabledLinkColor = darkMode ? DisabledLinkColorDark : DisabledLinkColorLight;

            var listBox = new StackPanel();

            foreach (var item in children)
            {
                var link = new TextBlock
                {
                    Text = item.Text,
                    Foreground = item.Disabled ? disabledLinkColor : linkColor,
                    TextDecorations = TextDecorations.Underline,
                    Cursor = HandCursor,
                    Margin = new(0, 1),
                };
                ToolTip.SetTip(link, item.ChildRef);

                link.PointerReleased += (_, e) =>
                {
                    if (e.InitialPressMouseButton == MouseButton.Left)
                    {
                        Viewers.Resource.OpenExternalReference(GuiContext, item.ChildRef);
                    }
                };

                listBox.Children.Add(link);
            }

            using (UiControl.BeginGroup("Children"))
            {
                UiControl.AddControl(listBox);
            }
        }

        private void AddFunctionGroup(string groupName, IReadOnlyList<ParticleUpgradeTrace.TracedFunction> functions, Func<string, bool> isSupported)
        {
            Debug.Assert(UiControl != null);

            if (functions.Count == 0)
            {
                return;
            }

            using (UiControl.BeginGroup(groupName))
            {
                UiControl.AddControl(BuildFunctionList(functions, isSupported));
            }
        }

        private static StackPanel BuildFunctionList(IReadOnlyList<ParticleUpgradeTrace.TracedFunction> functions, Func<string, bool> isSupported)
        {
            var listBox = new StackPanel();

            foreach (var function in functions)
            {
                var displayName = StripClassPrefix(function.Class);
                ParticleFunctionItem item;

                if (function.RemovedByUpgrade)
                {
                    item = new ParticleFunctionItem($"{displayName} (removed by upgrade)", FunctionSupport.Removed);
                }
                else
                {
                    if (function.OriginalClass != null)
                    {
                        displayName = $"{displayName} (was {StripClassPrefix(function.OriginalClass)})";
                    }

                    var support = isSupported(function.Class) ? FunctionSupport.Supported : FunctionSupport.Unsupported;
                    item = new ParticleFunctionItem(displayName, support);
                }

                var text = new TextBlock
                {
                    Text = item.Text,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new(0, 1),
                };
                ToolTip.SetTip(text, function.Class);

                var color = item.Support switch
                {
                    FunctionSupport.Unsupported => UnsupportedColor,
                    FunctionSupport.Removed => RemovedColor,
                    _ => null,
                };

                if (color != null)
                {
                    text.Foreground = color;
                }

                listBox.Children.Add(text);
            }

            return listBox;
        }

        private static string StripClassPrefix(string className)
        {
            if (className.StartsWith("C_OP_", StringComparison.Ordinal))
            {
                return className[5..];
            }

            if (className.StartsWith("C_INIT_", StringComparison.Ordinal))
            {
                return className[7..];
            }

            return className;
        }

        private enum FunctionSupport
        {
            Supported,
            Unsupported,
            Removed,
        }

        private sealed record ParticleFunctionItem(string Text, FunctionSupport Support);

        private sealed record ChildSystemItem(string Text, string ChildRef, bool Disabled);

        protected override void OnPicked(object? sender, PickingTexture.PickingResponse pixelInfo)
        {
            //
        }
    }
}
