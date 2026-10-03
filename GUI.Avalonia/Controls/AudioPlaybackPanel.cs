using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using GUI.Types.Audio;
using GUI.Utils;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GUI.Controls
{
    /// <summary>
    /// Plays a decoded sound with a seekable waveform, loop toggle and volume. Output goes through OpenAL.
    /// </summary>
    internal sealed class AudioPlaybackPanel : DockPanel, IDisposable
    {
        private const int ChunkMilliseconds = 40;

        private readonly WaveStream WaveStream;
        private readonly MemoryStream audioData;
        private readonly ISampleProvider? SampleProvider;
        private readonly LoopingSampleProvider? LoopingProvider;
        private readonly VolumeSampleProvider? VolumeProvider;
        private readonly OpenALStream? output;
        private readonly bool acquiredOutput;
        private readonly Lock streamLock = new();
        private readonly ManualResetEventSlim playSignal = new(false);
        private readonly Thread? pumpThread;
        private readonly DispatcherTimer playbackTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };

        private readonly WaveformView waveform;
        private readonly Button playPauseButton;
        private readonly ThemedIcon playPauseIcon = AppIcons.Create("AudioPlay", 20);
        private readonly ThemedIcon loopIcon = AppIcons.Create("AudioRepeat", 20);
        private readonly ToggleButton loopButton;
        private readonly Avalonia.Controls.Slider volumeSlider;
        private readonly TextBlock labelCurrentTime;

        private readonly (int Start, int End) LoopMarkers;
        private readonly bool AutoPlay;
        private readonly Stopwatch playbackStopwatch = new();
        private TimeSpan playbackStartPosition;
        private volatile bool playing;
        private volatile bool disposed;
        private bool Looping;

        public AudioPlaybackPanel(WaveStream inputStream, bool autoPlay, (int start, int end) loopMarkers)
        {
            AutoPlay = autoPlay;
            WaveStream = AudioPlayer.ToPcm(inputStream);

            // some files have stupid values;
            loopMarkers = (Math.Max(0, loopMarkers.start), Math.Max(0, loopMarkers.end));

            if (loopMarkers.end > loopMarkers.start)
            {
                LoopMarkers = loopMarkers;
                Looping = true;
            }
            else
            {
                LoopMarkers = (0, (int)(WaveStream.Length / WaveStream.BlockAlign));
                Looping = false;
            }

            WaveStream.Position = 0;
            audioData = new MemoryStream((int)WaveStream.Length);
            WaveStream.CopyTo(audioData);
            WaveStream.Position = 0;

            waveform = new WaveformView(audioData, WaveStream.WaveFormat) { Height = 160, Margin = new(8) };
            waveform.Seek += progression => UpdatePlaybackProgression(progression);

            playPauseButton = new Button { Content = playPauseIcon, MinWidth = 44, HorizontalContentAlignment = HorizontalAlignment.Center };
            ToolTip.SetTip(playPauseButton, "Play / pause (Space)");
            playPauseButton.Click += (_, _) => TogglePlayback();

            var rewindButton = new Button { Content = AppIcons.Create("AudioRewindLeft", 20), MinWidth = 44, HorizontalContentAlignment = HorizontalAlignment.Center };
            ToolTip.SetTip(rewindButton, "Restart (Home)");
            rewindButton.Click += (_, _) =>
            {
                UpdatePlaybackProgression(0);
                Play();
            };

            loopButton = new ToggleButton { Content = loopIcon, MinWidth = 44, HorizontalContentAlignment = HorizontalAlignment.Center, IsChecked = Looping };
            ToolTip.SetTip(loopButton, "Loop (L)");
            loopButton.IsCheckedChanged += (_, _) =>
            {
                loopIcon.IconName = loopButton.IsChecked == true ? "AudioRepeatPressed" : "AudioRepeat";
                SetLooping(loopButton.IsChecked == true);
            };
            loopIcon.IconName = Looping ? "AudioRepeatPressed" : "AudioRepeat";

            labelCurrentTime = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0), FontFamily = CodeTextBox.MonospaceFont };

            volumeSlider = new Avalonia.Controls.Slider { Minimum = 0, Maximum = 1, Value = Settings.Config.Volume, Width = 140, VerticalAlignment = VerticalAlignment.Center };
            volumeSlider.ValueChanged += (_, e) => SetVolume((float)e.NewValue);

            var controls = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Margin = new(8, 0, 8, 8),
                Children =
                {
                    playPauseButton,
                    rewindButton,
                    loopButton,
                    labelCurrentTime,
                    new TextBlock { Text = "Volume", VerticalAlignment = VerticalAlignment.Center, Margin = new(16, 0, 4, 0) },
                    volumeSlider,
                },
            };

            SetDock(controls, Dock.Bottom);
            Children.Add(controls);
            Children.Add(waveform);

            Focusable = true;
            KeyDown += OnKeyDown;
            PointerPressed += (_, _) => Focus();
            playbackTimer.Tick += (_, _) => UpdateTime();

            labelCurrentTime.Text = GetCurrentTimeString(TimeSpan.Zero);

            try
            {
                var samples = WaveStream.ToSampleProvider();

                // OpenAL takes mono or stereo
                if (samples.WaveFormat.Channels > 2)
                {
                    samples = new MultiplexingSampleProvider([samples], 2);
                }

                SampleProvider = samples;
                LoopingProvider = new LoopingSampleProvider(SampleProvider, WaveStream, LoopMarkers.Start, LoopMarkers.End)
                {
                    EnableLooping = Looping,
                };
                LoopingProvider.LoopOccurred += OnLoopOccurred;
                VolumeProvider = new VolumeSampleProvider(LoopingProvider) { Volume = (float)volumeSlider.Value };

                acquiredOutput = OpenALOutput.Acquire();

                if (acquiredOutput)
                {
                    output = new OpenALStream(VolumeProvider.WaveFormat.SampleRate, VolumeProvider.WaveFormat.Channels);
                    pumpThread = new Thread(PumpAudio) { IsBackground = true, Name = nameof(AudioPlaybackPanel) };
                    pumpThread.Start();
                }
                else
                {
                    playPauseButton.IsEnabled = false;
                    labelCurrentTime.Text = "No audio output device";
                }
            }
            catch (Exception e)
            {
                Program.ShowError(e);
            }
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            if (AutoPlay)
            {
                AutoPlayOnce();
            }
        }

        private bool autoPlayed;

        private void AutoPlayOnce()
        {
            if (!autoPlayed)
            {
                autoPlayed = true;
                Play();
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);

            // Switching away from the tab stops the sound, like closing the WinForms panel did
            Pause();
        }

        private void SetVolume(float value)
        {
            VolumeProvider?.Volume = value;
            Settings.Config.Volume = value;
        }

        private void SetLooping(bool looping)
        {
            Looping = looping;
            LoopingProvider?.EnableLooping = looping;
            loopButton.IsChecked = looping;
        }

        private string GetCurrentTimeString(TimeSpan currentTime)
        {
            return $"{currentTime.ToString("mm\\:ss\\.ff", CultureInfo.InvariantCulture)} / {WaveStream.TotalTime.ToString("mm\\:ss\\.ff", CultureInfo.InvariantCulture)}";
        }

        private void UpdatePlaybackProgression(float progression)
        {
            progression = MathUtils.Saturate(progression);

            using (streamLock.EnterScope())
            {
                WaveStream.CurrentTime = TimeSpan.FromSeconds(WaveStream.TotalTime.TotalSeconds * progression);
                output?.Flush();
            }

            playbackStopwatch.Restart();
            playbackStartPosition = WaveStream.CurrentTime;

            UpdateTime();
        }

        private void TogglePlayback()
        {
            if (playing)
            {
                Pause();
            }
            else
            {
                Play();
            }
        }

        public void Play()
        {
            if (playing || output == null)
            {
                return;
            }

            using (streamLock.EnterScope())
            {
                if (WaveStream.CurrentTime >= WaveStream.TotalTime)
                {
                    WaveStream.Position = 0;
                }

                playbackStartPosition = WaveStream.CurrentTime;
            }

            playbackStopwatch.Restart();
            playing = true;
            output.Paused = false;
            playSignal.Set();
            playbackTimer.Start();
            playPauseIcon.IconName = "AudioPause";
            UpdateTime();
        }

        private void Pause()
        {
            if (!playing || output == null)
            {
                return;
            }

            playing = false;
            playSignal.Reset();
            output.Paused = true;

            // The stream read ahead of what was heard, rewind it to what was actually played
            using (streamLock.EnterScope())
            {
                var heard = CurrentPlaybackTime();
                output.Flush();
                WaveStream.CurrentTime = heard;
                playbackStartPosition = heard;
            }

            playbackTimer.Stop();
            playPauseIcon.IconName = "AudioPlay";
            UpdateTime();
        }

        private void OnPlaybackFinished()
        {
            playing = false;
            playSignal.Reset();
            playbackTimer.Stop();
            playPauseIcon.IconName = "AudioPlay";
            playbackStartPosition = WaveStream.TotalTime;
            UpdateTime();
        }

        private void PumpAudio()
        {
            Debug.Assert(VolumeProvider != null && output != null);

            var channels = VolumeProvider.WaveFormat.Channels;
            var buffer = new float[VolumeProvider.WaveFormat.SampleRate * ChunkMilliseconds / 1000 * channels];

            while (!disposed)
            {
                playSignal.Wait(200);

                if (!playing || disposed)
                {
                    continue;
                }

                int read;

                using (streamLock.EnterScope())
                {
                    read = VolumeProvider.Read(buffer.AsSpan());
                }

                if (read > 0)
                {
                    output.Submit(buffer.AsSpan(0, read));
                    continue;
                }

                // End of the sound: let the queued audio finish, then report it
                while (!disposed && playing && output.IsPlaying)
                {
                    Thread.Sleep(10);
                }

                if (!disposed && playing)
                {
                    Dispatcher.UIThread.Post(OnPlaybackFinished);
                    playing = false;
                }
            }
        }

        private void OnLoopOccurred(object? sender, EventArgs e)
        {
            // Called on the pump thread with the stream lock held, the next timer tick picks it up
            Dispatcher.UIThread.Post(() =>
            {
                playbackStopwatch.Restart();
                playbackStartPosition = TimeSpan.FromSeconds((double)LoopMarkers.Start / WaveStream.WaveFormat.SampleRate);
            });
        }

        private TimeSpan CurrentPlaybackTime()
        {
            if (!playing)
            {
                return WaveStream.CurrentTime;
            }

            var currentTime = playbackStartPosition + playbackStopwatch.Elapsed;
            return currentTime > WaveStream.TotalTime ? WaveStream.TotalTime : currentTime;
        }

        private void UpdateTime()
        {
            if (disposed)
            {
                return;
            }

            var currentTime = CurrentPlaybackTime();
            var progression = WaveStream.TotalTime.TotalSeconds > 0
                ? (float)Math.Min(1, currentTime.TotalSeconds / WaveStream.TotalTime.TotalSeconds)
                : 0f;

            waveform.Progression = progression;

            if (output != null)
            {
                labelCurrentTime.Text = GetCurrentTimeString(currentTime);
            }
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            const double SeekIncrementSeconds = 5.0;
            const double VolumeIncrement = 0.05;

            switch (e.Key)
            {
                case Key.Space:
                case Key.Enter:
                    TogglePlayback();
                    break;

                case Key.Left:
                    SeekRelative(-SeekIncrementSeconds);
                    break;

                case Key.Right:
                    SeekRelative(SeekIncrementSeconds);
                    break;

                case Key.Up:
                    volumeSlider.Value = Math.Min(1, volumeSlider.Value + VolumeIncrement);
                    break;

                case Key.Down:
                    volumeSlider.Value = Math.Max(0, volumeSlider.Value - VolumeIncrement);
                    break;

                case Key.Home:
                    UpdatePlaybackProgression(0f);
                    break;

                case Key.End:
                    UpdatePlaybackProgression(1f);
                    break;

                case Key.L:
                    SetLooping(!Looping);
                    break;

                default:
                    return;
            }

            e.Handled = true;
        }

        private void SeekRelative(double seconds)
        {
            var newSeconds = Math.Clamp(CurrentPlaybackTime().TotalSeconds + seconds, 0, WaveStream.TotalTime.TotalSeconds);
            UpdatePlaybackProgression((float)(newSeconds / WaveStream.TotalTime.TotalSeconds));
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            playing = false;
            playSignal.Set();
            playbackTimer.Stop();
            pumpThread?.Join(500);

            LoopingProvider?.LoopOccurred -= OnLoopOccurred;
            output?.Dispose();

            if (acquiredOutput)
            {
                OpenALOutput.Release();
            }

            playSignal.Dispose();
            WaveStream.Dispose();
            audioData.Dispose();
        }

        /// <summary>
        /// Sample provider that handles looping at specified frame positions.
        /// </summary>
        private sealed class LoopingSampleProvider(ISampleProvider source, WaveStream waveStream, int loopStart, int loopEnd) : ISampleProvider
        {
            private readonly long loopStartFrame = loopStart;
            private readonly long loopEndFrame = loopEnd;

            public WaveFormat WaveFormat => source.WaveFormat;

            public bool EnableLooping { get; set; }

            public event EventHandler? LoopOccurred;

            public int Read(Span<float> buffer)
            {
                var totalRead = 0;

                while (totalRead < buffer.Length)
                {
                    var samplesToRead = buffer.Length - totalRead;

                    if (EnableLooping && loopEndFrame > loopStartFrame)
                    {
                        var currentFrame = waveStream.Position / waveStream.BlockAlign;
                        var framesUntilLoop = loopEndFrame - currentFrame;

                        if (framesUntilLoop <= 0)
                        {
                            waveStream.Position = loopStartFrame * waveStream.BlockAlign;
                            LoopOccurred?.Invoke(this, EventArgs.Empty);
                            continue;
                        }

                        var samplesUntilLoop = (int)framesUntilLoop * WaveFormat.Channels;
                        samplesToRead = Math.Min(samplesToRead, samplesUntilLoop);
                    }

                    var samplesRead = source.Read(buffer.Slice(totalRead, samplesToRead));

                    if (samplesRead == 0)
                    {
                        break;
                    }

                    totalRead += samplesRead;
                }

                return totalRead;
            }
        }

        /// <summary>Min/max peaks of the sound, with the played part highlighted. Click or drag to seek.</summary>
        private sealed class WaveformView : Control
        {
            private const int PixelsPerPeak = 4;

            private readonly MemoryStream audioData;
            private readonly WaveFormat waveFormat;
            private (float Min, float Max)[] peaks = [];
            private float maxAmplitude = 1f;
            private int peaksWidth = -1;
            private bool dragging;

            public event Action<float>? Seek;

            public WaveformView(MemoryStream audioData, WaveFormat waveFormat)
            {
                this.audioData = audioData;
                this.waveFormat = waveFormat;
                Cursor = new Cursor(StandardCursorType.Hand);
            }

            public float Progression
            {
                get;
                set
                {
                    if (field != value)
                    {
                        field = value;
                        InvalidateVisual();
                    }
                }
            }

            protected override void OnPointerPressed(PointerPressedEventArgs e)
            {
                base.OnPointerPressed(e);
                dragging = true;
                e.Pointer.Capture(this);
                SeekTo(e.GetPosition(this).X);
            }

            protected override void OnPointerMoved(PointerEventArgs e)
            {
                base.OnPointerMoved(e);

                if (dragging)
                {
                    SeekTo(e.GetPosition(this).X);
                }
            }

            protected override void OnPointerReleased(PointerReleasedEventArgs e)
            {
                base.OnPointerReleased(e);
                dragging = false;
                e.Pointer.Capture(null);
            }

            private void SeekTo(double x)
            {
                if (Bounds.Width > 0)
                {
                    Seek?.Invoke((float)Math.Clamp(x / Bounds.Width, 0, 1));
                }
            }

            private void CalculatePeaks(int width)
            {
                peaksWidth = width;
                var peakCount = Math.Max(1, width / PixelsPerPeak);

                audioData.Position = 0;
                using var waveStream = new RawSourceWaveStream(audioData, waveFormat);

                ISampleProvider provider;

                try
                {
                    provider = waveStream.ToSampleProvider();
                }
                catch (ArgumentException)
                {
                    // Compressed formats have no direct sample conversion, there is just no waveform then
                    peaks = [];
                    return;
                }

                var frames = waveStream.Length / waveStream.BlockAlign;
                var framesPerPeak = Math.Max(1, frames / peakCount);
                var readBuffer = new float[framesPerPeak * waveFormat.Channels];

                var result = new (float Min, float Max)[peakCount];

                for (var i = 0; i < peakCount; i++)
                {
                    var read = provider.Read(readBuffer.AsSpan());

                    if (read == 0)
                    {
                        break;
                    }

                    var min = float.MaxValue;
                    var max = float.MinValue;

                    foreach (var sample in readBuffer.AsSpan(0, read))
                    {
                        min = Math.Min(min, sample);
                        max = Math.Max(max, sample);
                    }

                    result[i] = (min, max);
                }

                maxAmplitude = Math.Max(result.Max(static p => Math.Max(Math.Abs(p.Min), Math.Abs(p.Max))), 0.0001f);
                peaks = result;
            }

            public override void Render(DrawingContext context)
            {
                var width = (int)Bounds.Width;
                var height = Bounds.Height;

                if (width <= 0)
                {
                    return;
                }

                if (width != peaksWidth)
                {
                    CalculatePeaks(width);
                }

                context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

                var accent = Color.FromRgb(99, 161, 255);
                var played = new ImmutableSolidColorBrush(accent);
                var unplayed = new ImmutableSolidColorBrush(accent, 0.35);
                var mid = height / 2;
                var progressX = Progression * width;

                for (var i = 0; i < peaks.Length; i++)
                {
                    var (min, max) = peaks[i];
                    var x = i * PixelsPerPeak;
                    var top = mid - max / maxAmplitude * mid;
                    var bottom = mid - min / maxAmplitude * mid;
                    var barHeight = Math.Max(1, bottom - top);

                    context.FillRectangle(x <= progressX ? played : unplayed, new Rect(x, top, PixelsPerPeak - 1, barHeight));
                }

                context.DrawLine(new Pen(Brushes.White, 1), new Point(progressX, 0), new Point(progressX, height));
            }
        }
    }
}
