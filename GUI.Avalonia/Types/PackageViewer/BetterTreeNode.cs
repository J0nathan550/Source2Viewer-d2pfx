using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using GUI.Utils;
using ValvePak;

namespace GUI.Types.PackageViewer
{
    /// <summary>
    /// A folder or file in the package browser tree. Children are created when a folder is first
    /// expanded, packages can have hundreds of thousands of files.
    /// </summary>
    public sealed class BetterTreeNode : TreeViewItem, IBetterBaseItem
    {
        /// <summary>Horizontal distance between the icons of a folder and its children, like the WinForms tree.</summary>
        internal const double LevelIndent = 28;

        private const double LeftPadding = 2;

        private static readonly object Placeholder = new();

        private Control? headerRow;
        private TreeExpander? expander;

        private BetterTreeNode(string text, string iconName)
        {
            Text = text;
            Header = AppIcons.CreateHeader(iconName, text);
            Template = new FuncControlTemplate(static (control, scope) => ((BetterTreeNode)control).BuildTemplate(scope));
        }

        /// <summary>Creates a folder node.</summary>
        public BetterTreeNode(string text, VirtualPackageNode node, string iconName = "Folder") : this(text, iconName)
        {
            PkgNode = node;

            if (node.Folders.Count > 0 || node.Files.Count > 0)
            {
                Items.Add(Placeholder);
            }
        }

        /// <summary>Creates a file node.</summary>
        public BetterTreeNode(PackageEntry entry) : this(entry.GetFileName(), AppIcons.GetExtensionIconName(entry.TypeName))
        {
            PackageEntry = entry;
        }

        protected override Type StyleKeyOverride => typeof(TreeViewItem);

        /// <summary>The name shown for the node.</summary>
        public string Text { get; }

        public PackageEntry? PackageEntry { get; }

        public VirtualPackageNode? PkgNode { get; }

        public bool IsFolder => PackageEntry == null;

        public BetterTreeNode? ParentNode => Parent as BetterTreeNode;

        public IEnumerable<BetterTreeNode> ChildNodes => Items.OfType<BetterTreeNode>();

        /// <summary>
        /// Laid out like the WinForms tree: the top level has no expander, the levels below it have one in front of
        /// their icon, and only the icon and name are highlighted when selected.
        /// </summary>
        private StackPanel BuildTemplate(INameScope scope)
        {
            var headerPresenter = new ContentPresenter
            {
                Name = "PART_HeaderPresenter",
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new(0, 0, 4, 0),
            };
            headerPresenter.Bind(ContentPresenter.ContentProperty, this.GetObservable(HeaderProperty));
            scope.Register(headerPresenter.Name, headerPresenter);

            var layoutRoot = new Border
            {
                Name = "PART_LayoutRoot",
                Child = headerPresenter,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            TemplatedControl.SetIsTemplateFocusTarget(layoutRoot, true);
            scope.Register(layoutRoot.Name, layoutRoot);

            expander = new TreeExpander(this);

            var header = new DockPanel
            {
                Name = "PART_Header",
                MinHeight = 26,
                Background = Brushes.Transparent,
            };
            DockPanel.SetDock(expander, Dock.Left);
            header.Children.Add(expander);
            header.Children.Add(layoutRoot);
            scope.Register(header.Name, header);
            headerRow = header;

            var itemsPresenter = new ItemsPresenter { Name = "PART_ItemsPresenter" };
            itemsPresenter.Bind(IsVisibleProperty, this.GetObservable(IsExpandedProperty));
            scope.Register(itemsPresenter.Name, itemsPresenter);

            UpdateIndent();

            return new StackPanel { Children = { header, itemsPresenter } };
        }

        private void UpdateIndent()
        {
            if (headerRow == null || expander == null)
            {
                return;
            }

            // The top level is not indented and has no expander, the levels below it start where their parent's icon does
            expander.IsVisible = Level > 0;
            headerRow.Margin = new(LeftPadding + (Math.Max(0, Level - 1) * LevelIndent), 0, 0, 0);
            expander.InvalidateVisual();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == LevelProperty)
            {
                UpdateIndent();
            }
            else if (change.Property == IsExpandedProperty)
            {
                expander?.InvalidateVisual();

                if (IsExpanded)
                {
                    EnsureChildren();

                    // A folder that only holds one folder is expanded through, like the WinForms tree
                    if (Items is [BetterTreeNode { IsFolder: true } only])
                    {
                        only.IsExpanded = true;
                    }
                }
            }
        }

        public void EnsureChildren()
        {
            if (PkgNode == null || Items.Count != 1 || Items[0] != Placeholder)
            {
                return;
            }

            Items.Clear();
            PkgNode.CreatedNode = this;

            var folders = new List<VirtualPackageNode>(PkgNode.Folders.Values);
            folders.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            foreach (var folder in folders)
            {
                Items.Add(new BetterTreeNode(folder.Name, folder));
            }

            var files = new List<PackageEntry>(PkgNode.Files);
            files.Sort(static (a, b) => string.Compare(a.GetFileName(), b.GetFileName(), StringComparison.OrdinalIgnoreCase));

            foreach (var file in files)
            {
                Items.Add(new BetterTreeNode(file));
            }

            expander?.InvalidateVisual();
        }

        /// <summary>Drops created children so they are rebuilt from the virtual tree on next expand.</summary>
        public void Invalidate()
        {
            Items.Clear();

            if (PkgNode != null && (PkgNode.Folders.Count > 0 || PkgNode.Files.Count > 0))
            {
                Items.Add(Placeholder);
            }

            if (IsExpanded)
            {
                EnsureChildren();
            }

            expander?.InvalidateVisual();
        }
    }

    /// <summary>
    /// The arrow in front of a folder in the package tree, pointing right while collapsed and down while expanded,
    /// like the Explorer themed WinForms tree. Clicking it expands or collapses the folder without selecting it.
    /// </summary>
    sealed class TreeExpander : Control
    {
        private const double ArrowSize = 4;

        private readonly BetterTreeNode node;

        static TreeExpander()
        {
            AffectsRender<TreeExpander>(IsPointerOverProperty);
        }

        public TreeExpander(BetterTreeNode node)
        {
            this.node = node;
            Width = BetterTreeNode.LevelIndent;
            Focusable = false;
        }

        public override void Render(DrawingContext context)
        {
            // Hit testable over its whole slot, not only where the arrow is drawn
            context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

            if (node.ItemCount == 0)
            {
                return;
            }

            var colors = Themer.CurrentThemeColors;
            var pen = new ImmutablePen(Themer.GetBrush(IsPointerOver ? colors.Contrast : colors.ContrastSoft), 1.5, lineCap: PenLineCap.Round);
            var center = new Point(12, Math.Round(Bounds.Height / 2));

            if (node.IsExpanded)
            {
                context.DrawLine(pen, new Point(center.X - ArrowSize, center.Y - (ArrowSize / 2)), new Point(center.X, center.Y + (ArrowSize / 2)));
                context.DrawLine(pen, new Point(center.X, center.Y + (ArrowSize / 2)), new Point(center.X + ArrowSize, center.Y - (ArrowSize / 2)));
            }
            else
            {
                context.DrawLine(pen, new Point(center.X - (ArrowSize / 2), center.Y - ArrowSize), new Point(center.X + (ArrowSize / 2), center.Y));
                context.DrawLine(pen, new Point(center.X + (ArrowSize / 2), center.Y), new Point(center.X - (ArrowSize / 2), center.Y + ArrowSize));
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            if (node.ItemCount > 0 && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                node.IsExpanded = !node.IsExpanded;
                e.Handled = true;
            }
        }
    }
}
