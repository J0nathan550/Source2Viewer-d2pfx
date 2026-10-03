using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using Avalonia.Threading;

namespace GUI.Utils;

/// <summary>
/// Collects log lines from any thread and hands them to the console view in batches on the UI thread.
/// </summary>
internal sealed class ConsoleTab
{
    public readonly record struct LogLine(DateTime Time, Log.Category Category, string Component, string Message);

    private const int MaxLines = 20_000;

    // The process's own output rather than Console.Out, which exporters redirect into the log while they run
    private static readonly StreamWriter StandardOutput = new(Console.OpenStandardOutput(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
    private static readonly Lock StandardOutputLock = new();

    private readonly ConcurrentQueue<LogLine> pending = new();
    private readonly List<LogLine> lines = [];
    private int flushQueued;

    /// <summary>Raised on the UI thread with the lines appended since the last call.</summary>
    public event Action<IReadOnlyList<LogLine>>? LinesAdded;

    /// <summary>Raised on the UI thread when the buffer is cleared.</summary>
    public event Action? Cleared;

    public IReadOnlyList<LogLine> Lines => lines;

    public void WriteLine(Log.Category category, string component, string message)
    {
        var line = new LogLine(DateTime.Now, category, component, message);

        // Still useful on Linux where the app is commonly started from a terminal
        lock (StandardOutputLock)
        {
            StandardOutput.WriteLine(Format(line));
        }

        pending.Enqueue(line);

        if (Interlocked.Exchange(ref flushQueued, 1) == 0)
        {
            Dispatcher.UIThread.Post(Flush, DispatcherPriority.Background);
        }
    }

    public void ClearBuffer()
    {
        Dispatcher.UIThread.Post(() =>
        {
            lines.Clear();
            Cleared?.Invoke();
        });
    }

    public static string Format(LogLine line)
    {
        var builder = new StringBuilder(line.Message.Length + 48);
        builder.Append('[').Append(line.Time.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture)).Append("] ");

        if (line.Category != Log.Category.INFO)
        {
            builder.Append(line.Category).Append(' ');
        }

        builder.Append('[').Append(line.Component).Append("] ").Append(line.Message);
        return builder.ToString();
    }

    private void Flush()
    {
        Interlocked.Exchange(ref flushQueued, 0);

        var added = new List<LogLine>();

        while (pending.TryDequeue(out var line))
        {
            added.Add(line);
        }

        if (added.Count == 0)
        {
            return;
        }

        lines.AddRange(added);

        if (lines.Count > MaxLines)
        {
            lines.RemoveRange(0, lines.Count - MaxLines);
        }

        LinesAdded?.Invoke(added);
    }
}
