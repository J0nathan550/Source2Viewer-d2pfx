using System.Diagnostics;
using System.Linq;
using System.Text;

namespace GUI.Utils;

/// <summary>
/// Finds the item whose name starts with what is typed, like Explorer: letters typed in quick succession make up the
/// name, and typing the same letter again goes on to the next item that starts with it.
/// </summary>
sealed class TypeAheadSearch
{
    private static readonly TimeSpan ResetDelay = TimeSpan.FromSeconds(1);

    private readonly StringBuilder typed = new();
    private long lastInputTimestamp;

    /// <summary>
    /// Adds the typed text and finds the item to go to.
    /// </summary>
    /// <param name="input">The text just typed.</param>
    /// <param name="count">How many items there are.</param>
    /// <param name="getName">The name of the item at an index.</param>
    /// <param name="currentIndex">The index of the item that has the focus, or -1.</param>
    /// <returns>The index of the matching item, or -1 when none matches.</returns>
    public int Find(string? input, int count, Func<int, string> getName, int currentIndex)
    {
        if (string.IsNullOrEmpty(input) || input.Any(char.IsControl))
        {
            return -1;
        }

        if (Stopwatch.GetElapsedTime(lastInputTimestamp) > ResetDelay)
        {
            typed.Clear();
        }

        lastInputTimestamp = Stopwatch.GetTimestamp();
        typed.Append(input);

        if (count == 0)
        {
            return -1;
        }

        var text = typed.ToString();
        var repeated = text.All(c => char.ToUpperInvariant(c) == char.ToUpperInvariant(text[0]));
        var prefix = repeated ? text[..1] : text;

        // A new letter moves on from the current item, a longer name may still be the current one
        var start = repeated ? currentIndex + 1 : Math.Max(currentIndex, 0);

        for (var i = 0; i < count; i++)
        {
            var index = (start + i) % count;

            if (getName(index).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }
}
