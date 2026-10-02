using System.Diagnostics;
using System.Threading;
using ValveResourceFormat.Renderer.Audio;

namespace GUI.Types.Audio;

/// <summary>
/// <see cref="IAudioDevice"/> for scene audio on OpenAL. Without an output device it keeps accepting
/// samples at the playback rate and discards them, so the sound system still runs and the viewer still loads.
/// The shared viewers know this type by the name of the WinForms GUI's device, see GlobalUsings.cs.
/// </summary>
internal sealed class OpenALAudioDevice : IAudioDevice
{
    private readonly OpenALStream? stream;
    private readonly bool acquired;
    private long silentStart;
    private long silentSamples;
    private volatile bool disposed;

    public int SampleRate { get; }
    public int Channels => 2;

    public OpenALAudioDevice()
    {
        acquired = OpenALOutput.Acquire();
        SampleRate = OpenALOutput.SampleRate;

        if (acquired)
        {
            stream = new OpenALStream(SampleRate, Channels);
        }
    }

    public void SubmitSamples(ReadOnlySpan<float> samples)
    {
        if (disposed)
        {
            return;
        }

        if (stream != null)
        {
            stream.Submit(samples);
            return;
        }

        // Silent device: block like a real one would, so the mixing thread does not spin
        if (silentStart == 0)
        {
            silentStart = Stopwatch.GetTimestamp();
        }

        silentSamples += samples.Length / Channels;

        var due = TimeSpan.FromSeconds((double)silentSamples / SampleRate);
        var elapsed = Stopwatch.GetElapsedTime(silentStart);

        if (due > elapsed)
        {
            Thread.Sleep(due - elapsed);
        }
    }

    public void Dispose()
    {
        disposed = true;
        stream?.Dispose();

        if (acquired)
        {
            OpenALOutput.Release();
        }
    }
}
