using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using GUI.Utils;
using WinForms = System.Windows.Forms;

namespace GUI.Forms
{
    /// <summary>
    /// Progress dialog for the custom exporters that keeps a scrolling log of every line they report, with the same
    /// API as the WinForms one so the exporters are shared.
    /// </summary>
    sealed class CustomVmdlExtractProgressForm : IProgress<string>, IDisposable
    {
        private readonly CancellationTokenSource cancellationTokenSource = new();
        private readonly DispatcherTimer updateTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
        private readonly Lock pendingLinesLock = new();
        private readonly StringBuilder pendingLines = new();
        private readonly Window window;
        private readonly ProgressBar progressBar;
        private readonly TextBox logTextBox;
        private readonly Button cancelButton;
        private long startTimestamp;
        private bool completed;
        private bool closed;
        private Task? workCompletion;

        public Func<CancellationToken, Task>? OnProcess { get; set; }

        /// <summary>
        /// When set, the dialog stays open after the work completes and shows a completed state instead of closing itself.
        /// </summary>
        public bool StayOpenOnCompletion { get; set; }

        public string Text { get; set; } = "Extracting files…";

        /// <summary>
        /// Time elapsed since the dialog was shown.
        /// </summary>
        internal TimeSpan Elapsed => Stopwatch.GetElapsedTime(startTimestamp);

        /// <summary>
        /// Completes once the work has actually stopped, which on cancellation is after the dialog has already closed.
        /// Await this before disposing anything the work reads from. Never faults, failures are reported to the user.
        /// </summary>
        internal Task WorkCompletion => workCompletion ?? Task.CompletedTask;

        public CustomVmdlExtractProgressForm()
        {
            progressBar = new ProgressBar { IsIndeterminate = true, Minimum = 0, Maximum = 1, Height = 22, Margin = new(0, 0, 0, 8) };

            logTextBox = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new FontFamily("Consolas, DejaVu Sans Mono, Liberation Mono, monospace"),
                FontSize = 12,
                VerticalContentAlignment = VerticalAlignment.Top,
                Margin = new(0, 0, 0, 8),
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(logTextBox, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(logTextBox, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);

            cancelButton = new Button { Content = "Cancel", Width = 88, Height = 26, HorizontalAlignment = HorizontalAlignment.Right };

            var root = new DockPanel { Margin = new(12) };
            DockPanel.SetDock(progressBar, Dock.Top);
            DockPanel.SetDock(cancelButton, Dock.Bottom);
            root.Children.Add(progressBar);
            root.Children.Add(cancelButton);
            root.Children.Add(logTextBox);

            window = new Window
            {
                Width = 760,
                Height = 480,
                MinWidth = 420,
                MinHeight = 260,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                Content = root,
            };

            cancelButton.Click += (_, _) => window.Close();
            window.KeyDown += (_, e) =>
            {
                // Escape only closes the dialog once the work is done, like the WinForms cancel button
                if (e.Key == Key.Escape && completed)
                {
                    window.Close();
                }
            };
            window.Opened += OnShown;
            window.Closing += (_, _) =>
            {
                updateTimer.Stop();
                cancellationTokenSource.Cancel();
            };
            window.Closed += (_, _) => closed = true;

            updateTimer.Tick += (_, _) => FlushPendingLines();
        }

        public void Report(string value) => AppendLine(value);

        /// <summary>
        /// Queues a line to be appended to the log. Safe to call from any thread at any rate: lines
        /// are batched and flushed to the text box together on a timer.
        /// </summary>
        public void AppendLine(string text)
        {
            lock (pendingLinesLock)
            {
                pendingLines.AppendLine(text);
            }
        }

        /// <summary>Shows the dialog modally and returns once it closed, pumping the UI meanwhile.</summary>
        public WinForms.DialogResult ShowDialog()
        {
            if (AppMessageDialogs.GetOwner() is not { } owner)
            {
                throw new InvalidOperationException("Progress dialog needs an owner window");
            }

            DispatcherHelpers.WaitOnUIThread(() => window.ShowDialog(owner));
            return WinForms.DialogResult.OK;
        }

        /// <summary>Shows the dialog modally, completing once it closed.</summary>
        public async Task<WinForms.DialogResult> ShowDialogAsync()
        {
            if (AppMessageDialogs.GetOwner() is not { } owner)
            {
                throw new InvalidOperationException("Progress dialog needs an owner window");
            }

            await window.ShowDialog(owner).ConfigureAwait(true);
            return WinForms.DialogResult.OK;
        }

        private void FlushPendingLines()
        {
            if (closed)
            {
                return;
            }

            string? toAppend = null;

            lock (pendingLinesLock)
            {
                if (pendingLines.Length > 0)
                {
                    toAppend = pendingLines.ToString();
                    pendingLines.Clear();
                }
            }

            if (toAppend != null)
            {
                logTextBox.Text += toAppend;
                logTextBox.CaretIndex = logTextBox.Text?.Length ?? 0;
            }

            UpdateTitle();
        }

        private void UpdateTitle()
        {
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);

            window.Title = completed
                ? $"{Text} (completed in {GenericProgressForm.FormatTime(elapsed)})"
                : $"{Text} ({GenericProgressForm.FormatTime(elapsed)} elapsed)";
        }

        private void OnShown(object? sender, EventArgs e)
        {
            startTimestamp = Stopwatch.GetTimestamp();

            FlushPendingLines();
            updateTimer.Start();

            workCompletion = Task.Run(
                () => OnProcess?.Invoke(cancellationTokenSource.Token) ?? Task.CompletedTask,
                cancellationTokenSource.Token)
                .ContinueWith(t =>
                {
                    if (t.Exception != null)
                    {
                        foreach (var exception in t.Exception.Flatten().InnerExceptions)
                        {
                            if (exception is not OperationCanceledException)
                            {
                                Program.ShowError(exception);
                            }
                        }
                    }

                    Dispatcher.UIThread.Post(OnWorkFinished);
                }, TaskScheduler.Default);
        }

        private void OnWorkFinished()
        {
            if (closed)
            {
                return;
            }

            // Cancelling already closed the dialog, there is no completed state to show for work that did not finish
            if (cancellationTokenSource.IsCancellationRequested || !StayOpenOnCompletion)
            {
                window.Close();
                return;
            }

            updateTimer.Stop();
            completed = true;

            // Flush the last reported lines and render the completed title
            FlushPendingLines();

            progressBar.IsIndeterminate = false;
            progressBar.Value = progressBar.Maximum;
            cancelButton.Content = "Close";
            cancelButton.IsDefault = true;
        }

        public void Dispose()
        {
            updateTimer.Stop();
            cancellationTokenSource.Dispose();
        }
    }
}
