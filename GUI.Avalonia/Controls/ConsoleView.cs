using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using GUI.Utils;

namespace GUI.Controls;

sealed class ConsoleView : DockPanel
{
    private readonly TextEditor editor;

    public ConsoleView(ConsoleTab console)
    {
        editor = new TextEditor
        {
            IsReadOnly = true,
            FontFamily = CodeTextBox.MonospaceFont,
            FontSize = 12,
            ShowLineNumbers = false,
            WordWrap = false,
            Document = new TextDocument(),
        };
        editor.TextArea.TextView.LineTransformers.Add(new CategoryColorizer());

        var clearButton = new Button { Content = "Clear console", Margin = new(4) };
        clearButton.Click += (_, _) => Log.ClearConsole();

        SetDock(clearButton, Dock.Top);
        clearButton.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        Children.Add(clearButton);
        Children.Add(editor);

        Append(console.Lines);
        console.LinesAdded += Append;
        console.Cleared += () => editor.Document.Text = string.Empty;
    }

    private void Append(IReadOnlyList<ConsoleTab.LogLine> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        var builder = new System.Text.StringBuilder();

        foreach (var line in lines)
        {
            builder.AppendLine(ConsoleTab.Format(line));
        }

        var wasAtEnd = editor.TextArea.Caret.Offset >= editor.Document.TextLength;

        editor.Document.Insert(editor.Document.TextLength, builder.ToString());

        if (wasAtEnd)
        {
            editor.TextArea.Caret.Offset = editor.Document.TextLength;
            editor.ScrollToEnd();
        }
    }

    private sealed class CategoryColorizer : DocumentColorizingTransformer
    {
        private static readonly IBrush Warning = new ImmutableSolidColorBrush(Colors.Orange);
        private static readonly IBrush Error = new ImmutableSolidColorBrush(Color.FromRgb(240, 80, 80));
        private static readonly IBrush Debug = new ImmutableSolidColorBrush(Colors.Gray);

        protected override void ColorizeLine(DocumentLine line)
        {
            // Lines look like "[12:34:56.789] WARN [Component] message", the category sits after the timestamp
            const int CategoryStart = 15;

            if (line.Length <= CategoryStart)
            {
                return;
            }

            var text = CurrentContext.Document.GetText(line.Offset + CategoryStart, Math.Min(5, line.Length - CategoryStart));

            IBrush? brush = text switch
            {
                _ when text.StartsWith("WARN", StringComparison.Ordinal) => Warning,
                _ when text.StartsWith("ERROR", StringComparison.Ordinal) => Error,
                _ when text.StartsWith("DEBUG", StringComparison.Ordinal) => Debug,
                _ => null,
            };

            if (brush != null)
            {
                ChangeLinePart(line.Offset, line.EndOffset, e => e.TextRunProperties.SetForegroundBrush(brush));
            }
        }
    }
}
