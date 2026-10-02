using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using GUI.Controls;

namespace GUI.Types.Viewers
{
    interface IViewer : IDisposable
    {
        public Task LoadAsync(Stream? stream);

        /// <summary>
        /// UI agnostic description of the loaded content. Viewers that implement this instead
        /// of overriding <see cref="Create"/> are shared with the WinForms GUI unmodified.
        /// </summary>
        public ViewerContent? GetContent() => null;

        /// <summary>
        /// Builds the control shown in the viewer's tab. Called on the UI thread after <see cref="LoadAsync"/>.
        /// </summary>
        public Control Create()
        {
            var content = GetContent()
                ?? throw new NotImplementedException($"{GetType().Name} must implement either GetContent or Create");

            return ViewerContentPresenter.CreateControl(content);
        }

        /// <summary>
        /// Called after the viewer has been made visible (the loading panel was removed). Viewers that render
        /// lazily (e.g. GL viewers) use this to force their first draw. No-op by default.
        /// </summary>
        public void NotifyVisible() { }
    }
}
