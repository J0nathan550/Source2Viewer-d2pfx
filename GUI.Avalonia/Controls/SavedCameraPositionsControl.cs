using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using GUI.Utils;

namespace GUI.Controls
{
    sealed class SavedCameraPositionsControl : StackPanel, IDisposable
    {
        public class RestoreCameraRequestEvent : EventArgs
        {
            public required string Camera { get; init; }
        }

        public event EventHandler? SaveCameraRequest;
        public event EventHandler<RestoreCameraRequestEvent>? RestoreCameraRequest;
        public event EventHandler<bool>? GetOrSetPositionFromClipboardRequest;

        private readonly ComboBox cmbPositions;
        private readonly Button btnRestore;
        private readonly Button btnDelete;

        public SavedCameraPositionsControl()
        {
            Spacing = 4;

            cmbPositions = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };

            var btnSave = new Button { Content = "Save" };
            btnSave.Click += (_, _) => SaveCameraRequest?.Invoke(this, EventArgs.Empty);

            btnRestore = new Button { Content = "Restore" };
            btnRestore.Click += (_, _) =>
            {
                if (cmbPositions.SelectedItem is string camera)
                {
                    RestoreCameraRequest?.Invoke(this, new RestoreCameraRequestEvent { Camera = camera });
                }
            };

            btnDelete = new Button { Content = "Delete" };
            btnDelete.Click += (_, _) =>
            {
                if (cmbPositions.SelectedItem is string camera)
                {
                    Settings.Config.SavedCameras.Remove(camera);
                    Settings.InvokeRefreshCamerasOnSave();
                }
            };

            var btnGetPos = new Button { Content = "Copy setpos" };
            ToolTip.SetTip(btnGetPos, "Copy the camera position as setpos and setang console commands");
            btnGetPos.Click += (_, _) => GetOrSetPositionFromClipboardRequest?.Invoke(this, false);

            var btnSetPos = new Button { Content = "Paste setpos" };
            ToolTip.SetTip(btnSetPos, "Move the camera to the setpos and setang console commands in the clipboard");
            btnSetPos.Click += (_, _) => GetOrSetPositionFromClipboardRequest?.Invoke(this, true);

            Children.Add(cmbPositions);
            Children.Add(new WrapPanel { Children = { btnSave, btnRestore, btnDelete } });
            Children.Add(new WrapPanel { Children = { btnGetPos, btnSetPos } });

            foreach (var button in Children.OfType<WrapPanel>().SelectMany(static p => p.Children).OfType<Button>())
            {
                button.Margin = new(0, 0, 4, 4);
            }

            Settings.RefreshCamerasOnSave += RefreshSavedPositions;
        }

        public void Dispose()
        {
            Settings.RefreshCamerasOnSave -= RefreshSavedPositions;
        }

        private void RefreshSavedPositions(object? sender, EventArgs e) => RefreshSavedPositions();

        public void RefreshSavedPositions()
        {
            var previousCamera = cmbPositions.SelectedItem as string;

            if (Settings.Config.SavedCameras.Count == 0)
            {
                btnRestore.IsEnabled = false;
                btnDelete.IsEnabled = false;
                cmbPositions.IsEnabled = false;
                cmbPositions.ItemsSource = new[] { "(no saved cameras)" };
                cmbPositions.SelectedIndex = 0;
                return;
            }

            btnRestore.IsEnabled = true;
            btnDelete.IsEnabled = true;
            cmbPositions.IsEnabled = true;

            var names = Settings.Config.SavedCameras.Keys.ToList();
            cmbPositions.ItemsSource = names;
            cmbPositions.SelectedIndex = Math.Max(0, previousCamera == null ? 0 : names.IndexOf(previousCamera));
        }
    }
}
