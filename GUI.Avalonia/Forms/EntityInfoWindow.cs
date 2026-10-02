using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using GUI.Controls;
using GUI.Utils;

namespace GUI.Forms
{
    /// <summary>
    /// Properties and I/O connections of an entity or scene node, opened from the world viewer.
    /// </summary>
    sealed class EntityInfoWindow : Window
    {
        private static PixelRect? savedBounds;

        private readonly DockPanel root;

        public EntityInfoControl EntityInfoControl { get; }

        public Button? ShowInGraphButton { get; private set; }

        public EntityInfoWindow(VrfGuiContext vrfGuiContext)
        {
            Width = 800;
            Height = 450;
            Title = "Entity info";
            Icon = Program.MainForm.Icon;

            if (savedBounds is { } bounds)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Position = bounds.Position;
                Width = bounds.Width / Program.MainForm.RenderScaling;
                Height = bounds.Height / Program.MainForm.RenderScaling;
            }

            EntityInfoControl = new EntityInfoControl(vrfGuiContext);
            EntityInfoControl.ExternalReferenceOpened += Close;

            root = new DockPanel { Children = { EntityInfoControl } };
            Content = root;

            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
                {
                    Close();
                    e.Handled = true;
                }
            };

            Closing += (_, _) => savedBounds = new PixelRect(Position, PixelSize.FromSize(ClientSize, RenderScaling));
        }

        public void AddShowInGraphButton(EventHandler onClick)
        {
            ShowInGraphButton = new Button
            {
                Content = "Show in I/O graph",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Margin = new(4),
            };
            ShowInGraphButton.Click += (s, e) => onClick(s, e);
            DockPanel.SetDock(ShowInGraphButton, Dock.Bottom);
            root.Children.Insert(0, ShowInGraphButton);
        }
    }
}
