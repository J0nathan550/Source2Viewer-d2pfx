using System.Threading.Tasks;
using Avalonia.Threading;

namespace GUI.Utils;

static class DispatcherHelpers
{
    /// <summary>
    /// Waits for an asynchronous UI operation (a dialog, the clipboard) while keeping the UI thread pumping,
    /// for shared code that expects these APIs to be synchronous like they are in WinForms.
    /// </summary>
    public static T WaitOnUIThread<T>(Func<Task<T>> operation)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return Dispatcher.UIThread.InvokeAsync(operation).GetAwaiter().GetResult();
        }

        var task = operation();

        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
            Dispatcher.UIThread.PushFrame(frame);
        }

        return task.GetAwaiter().GetResult();
    }

    public static void WaitOnUIThread(Func<Task> operation)
    {
        WaitOnUIThread(async () =>
        {
            await operation().ConfigureAwait(true);
            return true;
        });
    }
}
