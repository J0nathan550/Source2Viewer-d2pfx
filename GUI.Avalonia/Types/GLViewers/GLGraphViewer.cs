using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.Graphs.Core;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using ValveResourceFormat.Graphs;
using ValveResourceFormat.Renderer;
using AvaloniaControls = Avalonia.Controls;

namespace GUI.Types.GLViewers
{
    /// <summary>
    /// OpenGL host for <see cref="GraphView"/>: provides a GL-backed Skia surface, pan/zoom,
    /// raster copy/save and double click to open node resource references.
    /// Port of the WinForms GLGraphViewer, the graph logic is kept in step with it and only the sidebar differs.
    /// </summary>
    class GLGraphViewer : GLTextureViewer
    {
        protected readonly GraphView View;
        private SKRect graphBounds;
        private bool needsFit = true;
        private ThemedContextMenuStrip? contextMenu;

        // Skia/OpenGL context
        private GRGlInterface? glInterface;
        private GRContext? grContext;
        private GRBackendRenderTarget? renderTarget;
        private SKSurface? surface;
        private SKSizeI lastSize;

        public GLGraphViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, GraphView view)
            : base(vrfGuiContext, rendererContext, (SKBitmap?)null)
        {
            View = view;
            View.GraphChanged += OnGraphChanged;
            graphBounds = View.GetGraphBounds();
        }

        private void OnGraphChanged(object? sender, System.EventArgs e)
        {
            InvalidateRender();
        }

        protected override bool ShowResetZoomButton => false;

        // The graph is drawn through Skia; there are no scene shaders to hot-reload.
        protected override bool ShowReloadShadersButton => false;

        /// <summary>Frontends that build state machines expose a checkbox to draw them as a statechart.</summary>
        protected virtual bool HasStateMachineToggle => false;

        /// <summary>Rebuilds the graph with state machines drawn as a statechart (true) or flattened (false).</summary>
        protected virtual void SetDrawStateMachines(bool draw)
        {
        }

        /// <summary>Frontends whose parameters fan out to many consumers expose a checkbox to draw those wires.</summary>
        protected virtual bool HasParameterWireToggle => false;

        /// <summary>Rebuilds the graph with parameter feeds drawn as wires (true) or listed as card text (false).</summary>
        protected virtual void SetDrawParameterWires(bool draw)
        {
        }

        private AvaloniaControls.TextBlock? statsLabel;
        private AvaloniaControls.TextBox? searchBox;
        private GraphNode? lastSearchResult;
        private CheckedListBox? subtitleFilter;
        private ComboBox? wireCombo;
        private CheckBox? stateMachinesCheckBox;
        private CheckBox? parameterWiresCheckBox;
        private bool suppressWireChange;
        private bool openingStraightWires;

        protected override void AddUiControls()
        {
            Debug.Assert(UiControl != null);

            base.AddUiControls();

            statsLabel = new AvaloniaControls.TextBlock
            {
                Margin = new(3, 8, 3, 0),
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
            };
            UiControl.AddControl(statsLabel);
            RefreshStatsLabel();

            AddSearchSection();
            AddViewSection();

            var subtitles = View.GetDistinctSubtitles();

            if (subtitles.Count > 1)
            {
                // The list scrolls on its own past a fixed height instead of pushing everything below it out of the sidebar
                subtitleFilter = UiControl.AddMultiSelection("Filter", listBox =>
                {
                    foreach (var subtitle in subtitles)
                    {
                        listBox.Items.Add(subtitle, true);
                    }
                }, visible =>
                {
                    View.SetSubtitleFilter(visible);
                    RefitToGraph();
                });
            }

            AddLegendPanel();

            var resetButton = new AvaloniaControls.Button
            {
                Content = "Reset view",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Margin = new(4, 8),
            };
            resetButton.Click += (_, _) => ResetGraph();
            UiControl.AddControl(resetButton);

            // Saving is the least used control here, so it goes under everything else.
            if (SaveSection?.Parent is AvaloniaControls.Panel saveParent)
            {
                saveParent.Children.Remove(SaveSection);
                saveParent.Children.Add(SaveSection);
            }
        }

        private void AddSearchSection()
        {
            Debug.Assert(UiControl != null);

            searchBox = new AvaloniaControls.TextBox
            {
                PlaceholderText = "Name search (Enter = next)",
                Margin = new(0, 4),
            };
            searchBox.KeyDown += OnSearchKeyDown;
            searchBox.TextChanged += (_, _) => View.SetSearchHighlight(searchBox.Text);

            var section = new GLViewerGroupedSectionControl("Search");
            section.AddRow(searchBox);
            UiControl.AddControl(section);
        }

        /// <summary>
        /// The framed section with everything that changes how the graph is presented: layout
        /// engine, wire style, the rebuild toggles and the unbudgeted crossing repair. The
        /// repair button exists because the automatic pass is capped so opening a graph stays
        /// quick, which means a large graph keeps crossings the full pass would have removed.
        /// </summary>
        private void AddViewSection()
        {
            Debug.Assert(UiControl != null);

            openingStraightWires = View.StraightWires;

            var section = new GLViewerGroupedSectionControl("View");

            suppressWireChange = true;
            var wireSelection = CreateSelection("Wires", index =>
            {
                if (suppressWireChange || index < 0)
                {
                    return;
                }

                View.StraightWires = index == 1;
                View.MarkVisualDirty();
                InvalidateRender();
            });
            wireCombo = wireSelection.ComboBox;
            wireCombo.Items.AddRange(["Curved", "Straight"]);
            wireCombo.SelectedIndex = View.StraightWires ? 1 : 0;
            suppressWireChange = false;
            section.AddRow(wireSelection);

            if (HasStateMachineToggle)
            {
                var checkbox = RendererControl.CreateCheckBox("Draw state machines", false, draw =>
                {
                    SetDrawStateMachines(draw);
                    RefreshLegend();
                });
                stateMachinesCheckBox = checkbox.CheckBox;
                section.AddRow(checkbox);
            }

            if (HasParameterWireToggle)
            {
                var checkbox = RendererControl.CreateCheckBox("Draw parameter wires", false, draw =>
                {
                    SetDrawParameterWires(draw);
                    RefreshLegend();
                });
                parameterWiresCheckBox = checkbox.CheckBox;
                section.AddRow(checkbox);
            }

            var reduceCaption = new AvaloniaControls.TextBlock
            {
                Text = "Wires are untangled for up to four seconds when the graph opens.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Margin = new(0, 6, 0, 2),
            };
            section.AddRow(reduceCaption);

            var reduceButton = new AvaloniaControls.Button
            {
                Content = "Reduce Visual Graph Complexity",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                MinHeight = 30,
            };

            reduceButton.Click += async (_, _) =>
            {
                if (disposed)
                {
                    return;
                }

                reduceButton.IsEnabled = false;
                var previous = reduceButton.Content;
                reduceButton.Content = "Working...";

                // Let the caption repaint before the work blocks the UI thread
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);

                try
                {
                    View.ReduceVisualComplexity();
                    InvalidateRender();
                }
                finally
                {
                    reduceButton.Content = previous;
                    reduceButton.IsEnabled = true;
                }
            };
            section.AddRow(reduceButton);

            UiControl.AddControl(section);
        }

        private static GLViewerSelectionControl CreateSelection(string name, Action<int> changeCallback)
        {
            var selection = new GLViewerSelectionControl(name, horizontal: false, fill: false);

            selection.ComboBox.SelectedIndexChanged += (_, _) =>
            {
                changeCallback(selection.ComboBox.SelectedIndex);
            };

            return selection;
        }

        /// <summary>
        /// Resets every option to its default and lays the whole graph out fresh: search,
        /// selection and filters cleared, the wire combo back to its opening value,
        /// rebuild toggles off, then a full relayout and a refit.
        /// </summary>
        private void ResetGraph()
        {
            lastSearchResult = null;

            if (searchBox != null)
            {
                searchBox.Text = string.Empty;
            }

            View.SetSearchHighlight(null);
            View.ClearSelection();

            // The options go back first so a toggle rebuild below already lays out with them.
            if (wireCombo != null)
            {
                suppressWireChange = true;
                wireCombo.SelectedIndex = openingStraightWires ? 1 : 0;
                suppressWireChange = false;
                View.StraightWires = openingStraightWires;
            }

            if (stateMachinesCheckBox != null)
            {
                stateMachinesCheckBox.Checked = false;
            }

            if (parameterWiresCheckBox != null)
            {
                parameterWiresCheckBox.Checked = false;
            }

            if (subtitleFilter != null)
            {
                for (var i = 0; i < subtitleFilter.Items.Count; i++)
                {
                    subtitleFilter.SetItemChecked(i, true);
                }
            }

            pendingFullRelayout = false;
            View.ShowAllNodes();
            View.LayoutNodesPacked();
            RefitToGraph();
        }

        protected override void OnViewportLostFocus()
        {
            View.CancelDrag();
        }

        protected virtual string BuildStatsText(int islandCount)
        {
            var islandSuffix = islandCount == 1 ? "island" : "islands";
            var text = $"{View.NodeCount} nodes\n{View.WireCount} connections\n{islandCount} {islandSuffix}";
            var (selfLoops, orphans) = View.CountSelfLoopsAndOrphans();

            if (selfLoops > 0)
            {
                text += $"\n{selfLoops} self-loop{(selfLoops == 1 ? "" : "s")}";
            }

            if (orphans > 0)
            {
                text += $"\n{orphans} unconnected node{(orphans == 1 ? "" : "s")}";
            }

            return text;
        }

        /// <summary>Recomputes the stats label text and height.</summary>
        protected void RefreshStatsLabel()
        {
            if (statsLabel == null || UiControl == null)
            {
                return;
            }

            statsLabel.Text = BuildStatsText(View.GetComponents().Count);
        }

        private void OnSearchKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            if (e.Key != Avalonia.Input.Key.Enter || searchBox == null)
            {
                return;
            }

            e.Handled = true;

            var match = View.FindNextNode(searchBox.Text ?? string.Empty, lastSearchResult);
            lastSearchResult = match;

            if (match == null)
            {
                return;
            }

            if (match.Hidden)
            {
                FocusIslandOf(match);
            }

            FocusNode(match);
        }

        /// <summary>The declared rows whose colour something in the current graph draws.</summary>
        private List<GraphLegendEntry> PresentLegendEntries()
        {
            HashSet<GraphHue> categories = [];
            HashSet<GraphHue> tints = [];
            HashSet<GraphHue> lines = [];
            HashSet<GraphHue> dashed = [];

            foreach (var node in View.Nodes)
            {
                categories.Add(node.EffectiveCategory);

                if (node.BodyTint is { } tint)
                {
                    tints.Add(tint);
                }

                foreach (var socket in node.Inputs)
                {
                    lines.Add(socket.Hue);
                }

                foreach (var socket in node.Outputs)
                {
                    lines.Add(socket.Hue);
                }
            }

            foreach (var wire in View.Wires)
            {
                (wire.Dashed ? dashed : lines).Add(wire.From.Hue);
            }

            return View.Legend.FindAll(entry => entry.Kind switch
            {
                GraphLegendKind.Category => categories.Contains(entry.Hue),
                GraphLegendKind.BodyTint => tints.Contains(entry.Hue),
                GraphLegendKind.DashedWire => dashed.Contains(entry.Hue),
                _ => lines.Contains(entry.Hue),
            });
        }

        /// <summary>The legend split by sample kind: line samples first, then the node colour swatches.</summary>
        private List<(string Title, List<GraphLegendEntry> Entries)> LegendSections()
        {
            List<GraphLegendEntry> lineEntries = [];
            List<GraphLegendEntry> nodeEntries = [];

            foreach (var entry in PresentLegendEntries())
            {
                (entry.Kind is GraphLegendKind.Category or GraphLegendKind.BodyTint ? nodeEntries : lineEntries).Add(entry);
            }

            List<(string, List<GraphLegendEntry>)> sections = [];

            if (lineEntries.Count > 0)
            {
                sections.Add(("Line types", lineEntries));
            }

            if (nodeEntries.Count > 0)
            {
                sections.Add(("Node types", nodeEntries));
            }

            return sections;
        }

        private GLViewerGroupedSectionControl? legendSection;

        private void AddLegendPanel()
        {
            Debug.Assert(UiControl != null);

            if (View.Legend.Count == 0)
            {
                return;
            }

            legendSection = new GLViewerGroupedSectionControl("Legend");
            PopulateLegendRows();
            UiControl.AddControl(legendSection);
        }

        /// <summary>Re-reads which colours the graph draws and rebuilds the legend rows.</summary>
        private void RefreshLegend()
        {
            if (legendSection == null)
            {
                return;
            }

            legendSection.ClearRows();
            PopulateLegendRows();
        }

        private void PopulateLegendRows()
        {
            Debug.Assert(legendSection != null);

            var sections = LegendSections();
            legendSection.IsVisible = sections.Count > 0;

            var first = true;

            foreach (var (title, entries) in sections)
            {
                var heading = new AvaloniaControls.TextBlock
                {
                    Text = title,
                    Margin = new(0, first ? 2 : 8, 0, 2),
                };
                legendSection.AddRow(heading);
                first = false;

                foreach (var entry in entries)
                {
                    legendSection.AddRow(new AvaloniaControls.StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        Spacing = 4,
                        Children =
                        {
                            new LegendSwatch(View, entry),
                            new AvaloniaControls.TextBlock { Text = entry.Label, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                        },
                    });
                }
            }
        }

        /// <summary>The colour sample of one legend entry: a line, a diamond marker or a swatch.</summary>
        private sealed class LegendSwatch(GraphView view, GraphLegendEntry entry) : AvaloniaControls.Control
        {
            protected override Avalonia.Size MeasureOverride(Avalonia.Size availableSize) => new(22, 16);

            public override void Render(Avalonia.Media.DrawingContext context)
            {
                // Palette slots resolve at paint time so the legend follows the theme.
                var skColor = entry.Kind switch
                {
                    GraphLegendKind.Category => view.Palette.Category(entry.Hue),
                    GraphLegendKind.BodyTint => view.Palette.BodyTint(entry.Hue),
                    _ => view.Palette.Signal(entry.Hue),
                };
                var brush = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Avalonia.Media.Color.FromRgb(skColor.Red, skColor.Green, skColor.Blue));
                var mid = Bounds.Height / 2;

                if (entry.Kind is GraphLegendKind.Wire or GraphLegendKind.DashedWire)
                {
                    var dash = entry.Kind == GraphLegendKind.DashedWire ? Avalonia.Media.DashStyle.Dash : null;
                    context.DrawLine(new Avalonia.Media.Pen(brush, 3, dash), new Avalonia.Point(2, mid), new Avalonia.Point(20, mid));
                }
                else if (entry.Kind == GraphLegendKind.Marker)
                {
                    const double Radius = 5;
                    const double CenterX = 11;
                    var diamond = new Avalonia.Media.StreamGeometry();

                    using (var geometry = diamond.Open())
                    {
                        geometry.BeginFigure(new Avalonia.Point(CenterX, mid - Radius), true);
                        geometry.LineTo(new Avalonia.Point(CenterX + Radius, mid));
                        geometry.LineTo(new Avalonia.Point(CenterX, mid + Radius));
                        geometry.LineTo(new Avalonia.Point(CenterX - Radius, mid));
                        geometry.EndFigure(true);
                    }

                    context.DrawGeometry(brush, null, diamond);
                }
                else
                {
                    context.FillRectangle(brush, new Avalonia.Rect(5, mid - 6, 12, 12));
                }
            }
        }

        /// <summary>Refits the view to the current graph bounds on the next frame.</summary>
        public void RefitToGraph()
        {
            needsFit = true;
            InvalidateRender();
        }

        private void OnMouseDoubleClick(MouseEventArgs e)
        {
            var graphPoint = ScreenToGraph(e.Location);

            if (View.FindNodeAt(graphPoint) is { } node)
            {
                OnNodeDoubleClick(node);
            }
        }

        /// <summary>What double clicking a node does. Opens the asset the node references.</summary>
        protected virtual void OnNodeDoubleClick(GraphNode node)
        {
            if (node.ExternalResourceName != null)
            {
                OpenExternalResource(node);
            }
        }

        private void OpenExternalResource(GraphNode node)
        {
            Debug.Assert(node.ExternalResourceName != null);

            var name = node.ExternalResourceName;

            // A subgraph an uncompiled animation graph points at is itself an uncompiled loose
            // file that never ships compiled, so it resolves without the _c suffix and next to
            // the graph on disk rather than through the compiled-resource path.
            if (name.EndsWith(".vsubgrph", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".vanmgrph", StringComparison.OrdinalIgnoreCase))
            {
                OpenLooseGraphReference(name);
                return;
            }

            var foundFile = VrfGuiContext.FindFileWithContext(name + ValveResourceFormat.IO.GameFileLoader.CompiledFileSuffix);
            if (foundFile.Context != null)
            {
                Debug.Assert(foundFile.PackageEntry != null);
                Program.MainForm.OpenFile(foundFile.Context, foundFile.PackageEntry);
            }
        }

        // Resolves an uncompiled graph reference against the loaded content and then, when the
        // graph was opened straight from a folder, next to it and up its parent directories.
        // Opens the referenced file in a new tab if found, otherwise does nothing.
        private void OpenLooseGraphReference(string referencePath)
        {
            var found = VrfGuiContext.FindFileWithContext(referencePath);
            if (found.Context != null && found.PackageEntry != null)
            {
                Program.MainForm.OpenFile(found.Context, found.PackageEntry);
                return;
            }

            var openPath = VrfGuiContext.FileName;
            if (string.IsNullOrEmpty(openPath))
            {
                return;
            }

            var relative = referencePath.Replace('/', Path.DirectorySeparatorChar);
            var baseName = Path.GetFileName(relative);
            var directory = Path.GetDirectoryName(Path.GetFullPath(openPath));

            while (!string.IsNullOrEmpty(directory))
            {
                foreach (var candidate in new[] { Path.Combine(directory, relative), Path.Combine(directory, baseName) })
                {
                    if (File.Exists(candidate) && !string.Equals(Path.GetFullPath(candidate), Path.GetFullPath(openPath), StringComparison.OrdinalIgnoreCase))
                    {
                        Program.MainForm.OpenFile(candidate);
                        return;
                    }
                }

                directory = Path.GetDirectoryName(directory);
            }
        }

        /// <summary>Selects <paramref name="node"/> and centers the view on it.</summary>
        public void FocusNode(GraphNode node)
        {
            View.SelectNode(node);

            graphBounds = View.GetGraphBounds();
            OriginalWidth = (int)graphBounds.Width;
            OriginalHeight = (int)graphBounds.Height;
            needsFit = false;

            if (GLControl != null)
            {
                if (TextureScale < 0.4f)
                {
                    TextureScale = 1f;
                }

                var center = node.Position + View.MeasuredSizeOf(node) / 2f;
                Position = new Vector2(
                    (center.X - graphBounds.Left) * TextureScale - GLControl.PixelWidth / 2f,
                    (center.Y - graphBounds.Top) * TextureScale - GLControl.PixelHeight / 2f);
                TextureScaleChangeTime = 10f; // Skip animation

                UpdateZoomLabel();
            }

            InvalidateRender();
        }

        /// <summary>Lets subclasses prepend their own items to the node right-click menu.</summary>
        protected virtual void AddNodeContextMenuItems(ThemedContextMenuStrip menu, GraphNode node)
        {
        }

        /// <summary>Whether the graph is made of more than one connected component.</summary>
        protected virtual bool HasMultipleIslands => View.HasMultipleIslands();

        /// <summary>Hides every island except the one containing <paramref name="node"/>.</summary>
        protected virtual void FocusIslandOf(GraphNode node)
        {
            View.Isolate(node, GraphIsolateMode.Island);
            RefitToGraph();
        }

        private bool pendingFullRelayout;

        private void AddShowEverything(ThemedContextMenuStrip menu)
        {
            var showAllItem = new ToolStripMenuItem("Show everything");
            showAllItem.Click += (_, _) => ShowAllIslands();
            menu.Items.Add(showAllItem);
        }

        /// <summary>
        /// Adds a node filtering action twice: keeping the surviving nodes where they are,
        /// and a re-layout variant that lays out just the survivors.
        /// </summary>
        private void AddFilterAction(ThemedContextMenuStrip menu, string label, Action filter)
        {
            var keepItem = new ToolStripMenuItem(label);
            keepItem.Click += (_, _) =>
            {
                filter();
                RefitToGraph();
            };
            menu.Items.Add(keepItem);

            var relayoutItem = new ToolStripMenuItem($"{label} (re-layout)");
            relayoutItem.Click += (_, _) =>
            {
                filter();
                View.LayoutNodesPacked();
                pendingFullRelayout = true;
                RefitToGraph();
            };
            menu.Items.Add(relayoutItem);
        }

        protected virtual void ShowAllIslands()
        {
            View.ShowAllNodes();

            // A chain that was isolated with relayout moved; re-lay everything so the
            // restored nodes cannot overlap it.
            if (pendingFullRelayout)
            {
                pendingFullRelayout = false;
                View.LayoutNodesPacked();
            }

            RefitToGraph();
        }

        private void ShowContextMenu(System.Drawing.Point location)
        {
            Debug.Assert(GLControl != null);

            var graphPoint = ScreenToGraph(location);
            var node = View.FindNodeAt(graphPoint);

            // Selecting the node dims the rest of the graph, so it is unambiguous which
            // node the menu actions will apply to.
            if (node != null)
            {
                View.SelectNode(node);
            }

            contextMenu ??= new ThemedContextMenuStrip();

            while (contextMenu.Items.Count > 0)
            {
                var item = contextMenu.Items[0];
                contextMenu.Items.RemoveAt(0);
                item.Dispose();
            }

            if (node != null)
            {
                AddNodeContextMenuItems(contextMenu, node);

                if (node.ExternalResourceName != null)
                {
                    var openItem = new ToolStripMenuItem($"Open {node.ExternalResourceName}");
                    openItem.Click += (_, _) => OpenExternalResource(node);
                    contextMenu.Items.Add(openItem);
                }

                if (HasMultipleIslands)
                {
                    var focusItem = new ToolStripMenuItem("Focus on this island");
                    focusItem.Click += (_, _) => FocusIslandOf(node);
                    contextMenu.Items.Add(focusItem);
                }

                AddShowEverything(contextMenu);
                contextMenu.Items.Add(new ToolStripSeparator());

                AddFilterAction(contextMenu, "Isolate chain", () => View.Isolate(node, GraphIsolateMode.Chain));
                AddFilterAction(contextMenu, "Isolate upstream", () => View.Isolate(node, GraphIsolateMode.Upstream));
                AddFilterAction(contextMenu, "Isolate downstream", () => View.Isolate(node, GraphIsolateMode.Downstream));

                if (node.GroupPath != null)
                {
                    var groupName = node.GroupPath[(node.GroupPath.LastIndexOf('/') + 1)..];
                    AddFilterAction(contextMenu, $"Isolate group '{groupName}'", () => View.Isolate(node, GraphIsolateMode.Group));
                }
            }
            else
            {
                AddShowEverything(contextMenu);
            }

            if (contextMenu.Items.Count > 0)
            {
                contextMenu.Show(GLControl, location);
            }
        }

        protected override void OnGLLoad()
        {
            UseDefaultFramebuffer();

            // Set texture size to graph bounds for zoom calculations
            graphBounds = View.GetGraphBounds();
            OriginalWidth = (int)graphBounds.Width;
            OriginalHeight = (int)graphBounds.Height;
        }

        protected override void OnFirstPaint()
        {
            // The initial fit, and the zoom label that follows it, happen in OnPaint.
        }

        protected override int GetRenderHash()
        {
            Debug.Assert(MainFramebuffer != null);

            return HashCode.Combine(
                GetCurrentPositionAndScale(),
                View.VisualVersion,
                MainFramebuffer.Width,
                MainFramebuffer.Height,
                needsFit);
        }

        protected override void RenderToFramebuffer()
        {
            Debug.Assert(MainFramebuffer != null);

            MainFramebuffer.Bind(FramebufferTarget.Framebuffer);

            if (grContext == null)
            {
                glInterface = GRGlInterface.Create();
                grContext = GRContext.CreateGl(glInterface);
            }
            else
            {
                // The viewer loop issues its own GL calls (clears, framebuffer binds) between
                // frames; make Skia re-sync its cached GL state or its offscreen mask/layer
                // composites break at some zoom levels.
                grContext.ResetContext();
            }

            var newSize = new SKSizeI(MainFramebuffer.Width, MainFramebuffer.Height);

            if (renderTarget == null || lastSize != newSize || !renderTarget.IsValid)
            {
                lastSize = newSize;

                GL.GetInteger(GetPName.FramebufferBinding, out var framebuffer);
                GL.GetInteger(GetPName.Samples, out var samples);

                var maxSamples = grContext.GetMaxSurfaceSampleCount(SKColorType.Rgba8888);
                if (samples > maxSamples)
                {
                    samples = maxSamples;
                }

                var glInfo = new GRGlFramebufferInfo((uint)framebuffer, SKColorType.Rgba8888.ToGlSizedFormat());

                surface?.Dispose();
                surface = null;
                renderTarget?.Dispose();
                renderTarget = new GRBackendRenderTarget(newSize.Width, newSize.Height, samples, 0, glInfo);
            }

            surface ??= SKSurface.Create(grContext, renderTarget, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888);

            var canvas = surface.Canvas;

            if (needsFit)
            {
                graphBounds = View.GetGraphBounds();
                OriginalWidth = (int)graphBounds.Width;
                OriginalHeight = (int)graphBounds.Height;
                FitToViewport();
                needsFit = false;
            }
            else
            {
                // Update graphBounds and compensate Position for any origin shift
                var newGraphBounds = View.GetGraphBounds();

                var deltaLeft = newGraphBounds.Left - graphBounds.Left;
                var deltaTop = newGraphBounds.Top - graphBounds.Top;

                if (deltaLeft != 0 || deltaTop != 0)
                {
                    Position = new Vector2(
                        Position.X - deltaLeft * TextureScale,
                        Position.Y - deltaTop * TextureScale
                    );
                    TextureScaleChangeTime = 10f; // Skip interpolation for instant compensation
                }

                if ((int)newGraphBounds.Width != OriginalWidth || (int)newGraphBounds.Height != OriginalHeight)
                {
                    OriginalWidth = (int)newGraphBounds.Width;
                    OriginalHeight = (int)newGraphBounds.Height;
                }

                graphBounds = newGraphBounds;
            }

            var (scale, position) = GetCurrentPositionAndScale();

            canvas.Save();

            // Apply pan/zoom transform
            canvas.Translate(-position.X, -position.Y);
            canvas.Scale(scale, scale);
            canvas.Translate(-graphBounds.Left, -graphBounds.Top);

            var visibleRect = new SKRect(
                position.X / scale + graphBounds.Left,
                position.Y / scale + graphBounds.Top,
                (position.X + MainFramebuffer.Width) / scale + graphBounds.Left,
                (position.Y + MainFramebuffer.Height) / scale + graphBounds.Top
            );
            visibleRect.Inflate(50f / scale, 50f / scale);

            View.RenderToCanvas(canvas, visibleRect, scale);

            canvas.Restore();
            canvas.Flush();
            grContext.Flush();
        }

        /// <summary>
        /// Zoom-out floor: zooming stops once the graph's limiting dimension shrinks
        /// to 30% of the viewport (never above 1:1 for small graphs).
        /// </summary>
        internal float MinTextureScale()
        {
            if (GLControl == null || graphBounds.IsEmpty)
            {
                return 0.01f;
            }

            var fitScale = Math.Min(GLControl.PixelWidth / graphBounds.Width, GLControl.PixelHeight / graphBounds.Height);
            return Math.Min(1f, 0.3f * fitScale);
        }

        private void FitToViewport()
        {
            if (GLControl == null || graphBounds.IsEmpty)
            {
                return;
            }

            var scaleX = (GLControl.PixelWidth * 0.9f) / graphBounds.Width;
            var scaleY = (GLControl.PixelHeight * 0.9f) / graphBounds.Height;
            TextureScale = Math.Min(scaleX, scaleY);
            TextureScale = Math.Max(MinTextureScale(), Math.Min(TextureScale, 2f));

            Position = new Vector2(
                -(GLControl.PixelWidth - graphBounds.Width * TextureScale) / 2f,
                -(GLControl.PixelHeight - graphBounds.Height * TextureScale) / 2f
            );

            TextureScaleChangeTime = 10f; // Skip animation

            PostZoomLabelUpdate();
        }

        // Fits run on the render thread, so the label has to be set back on the UI thread.
        private void PostZoomLabelUpdate() => UpdateZoomLabel();

        protected override void OnMouseDown(object? sender, MouseEventArgs e)
        {
            // Keyboard shortcuts are routed to the focused control, and the sidebar search box
            // keeps focus until something takes it back.
            GLControl?.Focus();

            if (e.Button == MouseButtons.Left && e.Clicks == 2)
            {
                OnMouseDoubleClick(e);
            }

            // A live node drag owns the gesture; chorded buttons must not start panning
            // or reach the view mid-drag.
            if (View.IsMoving && e.Button != MouseButtons.Left)
            {
                return;
            }

            // Middle mouse = pan (let GLTextureViewer handle it)
            if (e.Button == MouseButtons.Middle)
            {
                base.OnMouseDown(sender, e);
                return;
            }

            var graphPoint = ScreenToGraph(e.Location);

            View.HandleMouseDown(graphPoint, e.Button, Control.ModifierKeys);

            if (e.Button == MouseButtons.Left && Control.ModifierKeys == Keys.None)
            {
                var clickedNothing = View.FindNodeAt(graphPoint) == null && View.FindWireAt(graphPoint) == null;

                if (clickedNothing)
                {
                    base.OnMouseDown(sender, e);
                }
            }
        }

        /// <summary>
        /// Ctrl+0 fits the graph to the viewport instead of the base viewer's literal 100% zoom,
        /// which on a graph is an arbitrary crop rather than a meaningful view.
        /// </summary>
        protected override void OnKeyDown(Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.D0) || keyData == (Keys.Control | Keys.NumPad0))
            {
                RefitToGraph();
                return;
            }

            base.OnKeyDown(keyData);
        }

        protected override void OnMouseMove(int x, int y)
        {
            var graphPoint = ScreenToGraph(new SKPoint(x, y));

            if (ClickPosition.HasValue)
            {
                base.OnMouseMove(x, y);
                return;
            }

            View.HandleMouseMove(graphPoint, Control.ModifierKeys);
        }

        protected override void OnMouseUp(object? sender, MouseEventArgs e)
        {
            // Always call base first to clear ClickPosition for panning
            base.OnMouseUp(sender, e);

            var wasDragging = View.IsMoving;
            var graphPoint = ScreenToGraph(e.Location);

            View.HandleMouseUp(graphPoint, e.Button);

            if (e.Button == MouseButtons.Right && !wasDragging)
            {
                ShowContextMenu(e.Location);
            }
        }

        protected SKPoint ScreenToGraph(System.Drawing.Point screenPoint)
            => ScreenToGraph(new SKPoint(screenPoint.X, screenPoint.Y));

        protected SKPoint ScreenToGraph(SKPoint screenPoint)
        {
            // Hit-test with the same animated transform the frame is drawn with.
            var (scale, position) = GetCurrentPositionAndScale();

            var canvasX = (screenPoint.X + position.X) / scale;
            var canvasY = (screenPoint.Y + position.Y) / scale;

            return new SKPoint(canvasX + graphBounds.Left, canvasY + graphBounds.Top);
        }

        /// <summary>There is no texture or resource behind a graph viewer; the saved image is the graph.</summary>
        protected override bool CanSaveVisual => true;

        /// <summary>
        /// The graph is drawn through Skia, not the texture/shader pipeline the base capture path
        /// assumes. Rasterizes the whole graph, independent of the viewport and its zoom, at native
        /// size unless that would put the long edge past 8192 px.
        /// </summary>
        protected override SKBitmap ReadPixelsToBitmap()
        {
            var bounds = View.GetGraphBounds();

            const float MaxCaptureDimension = 8192f;
            var scale = Math.Min(1f, MaxCaptureDimension / Math.Max(bounds.Width, bounds.Height));

            var width = Math.Max(1, (int)(bounds.Width * scale));
            var height = Math.Max(1, (int)(bounds.Height * scale));

            var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));

            try
            {
                using var canvas = new SKCanvas(bitmap);
                canvas.Scale(scale, scale);
                canvas.Translate(-bounds.Left, -bounds.Top);

                View.RenderToCanvas(canvas, bounds, scale);

                var bitmapToReturn = bitmap;
                bitmap = null;
                return bitmapToReturn;
            }
            finally
            {
                bitmap?.Dispose();
            }
        }

        private bool disposed;

        public override void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            contextMenu?.Dispose();
            subtitleFilter?.Dispose();
            View.GraphChanged -= OnGraphChanged;

            // Stops the render loop first; afterwards the GL context may no longer be
            // current, so abandon it to make Skia skip GL calls during disposal.
            base.Dispose();

            grContext?.AbandonContext();
            surface?.Dispose();
            renderTarget?.Dispose();
            grContext?.Dispose();
            glInterface?.Dispose();

            View.Dispose();
        }
    }
}
