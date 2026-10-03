using Avalonia;
using Avalonia.Controls;
using GUI.Utils;

namespace GUI.Controls;

/// <summary>
/// Builds its child the first time it is shown. Tab controls only attach the selected page, so text
/// for unselected tabs (block dumps, decompiled output) is not produced or kept in memory until opened.
/// </summary>
sealed class DeferredContent(Func<Control> factory, Func<Exception, Control>? onError = null) : Decorator
{
    private Func<Control>? factory = factory;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        var create = factory;

        if (create == null)
        {
            return;
        }

        factory = null;

        try
        {
            Child = create();
        }
        catch (Exception ex)
        {
            Log.Error(nameof(DeferredContent), ex.ToString());
            Child = onError?.Invoke(ex) ?? CodeTextBox.CreateFromException(ex);
        }
    }
}
