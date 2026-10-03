using Avalonia.Controls;

namespace GUI.Controls;

/// <summary>
/// Shows a control and disposes what it depends on when its tab closes, for tabs that have no file to export.
/// </summary>
sealed class OwningDecorator(params IDisposable[] owned) : Decorator, IDisposable
{
    public void Dispose()
    {
        foreach (var disposable in owned)
        {
            disposable.Dispose();
        }
    }
}
