using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using GUI.Utils;

namespace GUI.Forms
{
    /// <summary>
    /// Modal progress dialog that runs <see cref="OnProcess"/> on a worker thread, with the same API as the WinForms one
    /// so the exporters are shared.
    /// </summary>
    sealed class GenericProgressForm : IProgress<string>, IDisposable
    {
        private readonly CancellationTokenSource cancellationTokenSource = new();
        private readonly DispatcherTimer updateTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
        private readonly Window window;
        private readonly TextBlock statusLabel;
        private readonly ProgressBar progressBar;
        private readonly Button cancelButton;
        private string? pendingText;
        private int pendingBarValue = -1;
        private int pendingBarMax = -1;
        private long startTimestamp;
        private bool completed;
        private bool closed;
        private Task? workCompletion;

        public Func<CancellationToken, Task>? OnProcess { get; set; }

        /// <summary>
        /// When set, the dialog stays open after the work completes and shows a completed state instead of closing itself.
        /// </summary>
        public bool StayOpenOnCompletion { get; set; }

        public string Text { get; set; } = "Extracting files...";

        /// <summary>
        /// Time elapsed since the dialog was shown.
        /// </summary>
        internal TimeSpan Elapsed => Stopwatch.GetElapsedTime(startTimestamp);

        /// <summary>
        /// Completes once the work has actually stopped, which on cancellation is after the dialog has already closed.
        /// Await this before disposing anything the work reads from. Never faults, failures are reported to the user.
        /// </summary>
        internal Task WorkCompletion => workCompletion ?? Task.CompletedTask;

        public GenericProgressForm()
        {
            statusLabel = new TextBlock { TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
            progressBar = new ProgressBar { IsIndeterminate = true, Minimum = 0, Maximum = 1, Height = 18 };
            cancelButton = new Button { Content = "Cancel", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };

            window = new Window
            {
                Width = 560,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                Content = new StackPanel
                {
                    Margin = new(16),
                    Spacing = 10,
                    Children = { statusLabel, progressBar, cancelButton },
                },
            };

            cancelButton.Click += (_, _) => window.Close();
            window.Opened += OnShown;
            window.Closing += (_, _) =>
            {
                updateTimer.Stop();
                cancellationTokenSource.Cancel();
            };
            window.Closed += (_, _) => closed = true;

            updateTimer.Tick += (_, _) => ApplyPendingUpdate();
        }

        public void Report(string value) => SetProgress(value);

        /// <summary>
        /// Stores the latest status text. Pending values are applied on a timer and only the most recent text is shown,
        /// so this is safe to call from any thread at any rate.
        /// </summary>
        public void SetProgress(string text)
        {
            Volatile.Write(ref pendingText, text);
        }

        /// <summary>
        /// Stores the latest progress bar value, applied together with the status text.
        /// </summary>
        public void SetBarValue(int value)
        {
            Volatile.Write(ref pendingBarValue, value);
        }

        /// <summary>
        /// Switches the bar from marquee to a determinate bar with the given maximum.
        /// </summary>
        public void SetBarMax(int count)
        {
            Volatile.Write(ref pendingBarMax, count);
        }

        /// <summary>Shows the dialog modally and returns once it closed, pumping the UI meanwhile.</summary>
        public System.Windows.Forms.DialogResult ShowDialog()
        {
            if (AppMessageDialogs.GetOwner() is not { } owner)
            {
                throw new InvalidOperationException("Progress dialog needs an owner window");
            }

            DispatcherHelpers.WaitOnUIThread(() => window.ShowDialog(owner));
            return System.Windows.Forms.DialogResult.OK;
        }

        private void ApplyPendingUpdate()
        {
            if (closed)
            {
                return;
            }

            var text = Interlocked.Exchange(ref pendingText, null);

            if (text != null)
            {
                statusLabel.Text = text;
            }

            var barMax = Interlocked.Exchange(ref pendingBarMax, -1);

            // A single unit of work has no meaningful progression, keep the marquee
            if (barMax > 1)
            {
                progressBar.IsIndeterminate = false;
                progressBar.Maximum = barMax;
            }

            var barValue = Interlocked.Exchange(ref pendingBarValue, -1);

            if (barValue >= 0)
            {
                progressBar.Value = Math.Min(barValue, progressBar.Maximum);
            }

            UpdateTitle();
        }

        private void UpdateTitle()
        {
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);

            if (completed)
            {
                window.Title = $"{Text} (completed in {FormatTime(elapsed)})";
                return;
            }

            var value = progressBar.Value;
            var max = progressBar.Maximum;

            if (progressBar.IsIndeterminate || value <= 0 || max <= 0)
            {
                window.Title = $"{Text} ({FormatTime(elapsed)} elapsed)";
                return;
            }

            var remaining = elapsed * ((max - value) / value);
            window.Title = $"{Text} {(int)(value * 100 / max)}% ({FormatTime(elapsed)} elapsed, ~{FormatTime(remaining)} left)";
        }

        internal static string FormatTime(TimeSpan time)
        {
            return time.TotalHours >= 1
                ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
                : time.ToString(@"m\:ss", CultureInfo.InvariantCulture);
        }

        private void OnShown(object? sender, EventArgs e)
        {
            startTimestamp = Stopwatch.GetTimestamp();

            // Show anything that was set before the window existed
            ApplyPendingUpdate();
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

            // Flush the last reported text and render the completed title
            ApplyPendingUpdate();

            progressBar.IsIndeterminate = false;
            progressBar.Value = progressBar.Maximum;
            cancelButton.Content = "Close";
        }

        public void Dispose()
        {
            updateTimer.Stop();
            cancellationTokenSource.Dispose();
        }
    }
}
