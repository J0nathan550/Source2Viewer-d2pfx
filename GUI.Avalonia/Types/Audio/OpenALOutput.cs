using System.Threading;
using GUI.Utils;
using Silk.NET.OpenAL;

namespace GUI.Types.Audio;

/// <summary>
/// The process wide OpenAL device and context. OpenAL has a single current context per process, so every
/// stream shares this one and gets its own source. All AL calls must hold <see cref="Lock"/>.
/// </summary>
internal static unsafe class OpenALOutput
{
    public static readonly Lock Lock = new();

    private static AL? al;
    private static ALContext? alc;
    private static Device* device;
    private static Context* context;
    private static int users;
    private static bool failed;

    public static AL Api => al ?? throw new InvalidOperationException("OpenAL is not initialized");

    public static int SampleRate { get; private set; } = 48000;

    /// <summary>Opens the default output device on first use. Returns false when there is no usable device.</summary>
    public static bool Acquire()
    {
        using var _ = Lock.EnterScope();

        if (failed)
        {
            return false;
        }

        if (users++ > 0)
        {
            return true;
        }

        try
        {
            alc = ALContext.GetApi(soft: true);
            al = AL.GetApi(soft: true);

            device = alc.OpenDevice(string.Empty);

            if (device == null)
            {
                throw new InvalidOperationException("No audio output device");
            }

            context = alc.CreateContext(device, null);
            alc.MakeContextCurrent(context);

            var frequency = 0;
            alc.GetContextProperty(device, (GetContextInteger)0x1007 /* ALC_FREQUENCY */, 1, &frequency);

            if (frequency > 0)
            {
                SampleRate = frequency;
            }

            return true;
        }
        catch (Exception e)
        {
            Log.Warn(nameof(OpenALOutput), $"Audio output is unavailable, sound playback disabled: {e.Message}");

            failed = true;
            users = 0;
            ReleaseNative();
            return false;
        }
    }

    public static void Release()
    {
        using var _ = Lock.EnterScope();

        if (users == 0 || --users > 0)
        {
            return;
        }

        ReleaseNative();
    }

    private static void ReleaseNative()
    {
        if (alc != null)
        {
            alc.MakeContextCurrent(null);

            if (context != null)
            {
                alc.DestroyContext(context);
            }

            if (device != null)
            {
                alc.CloseDevice(device);
            }
        }

        context = null;
        device = null;
        al?.Dispose();
        alc?.Dispose();
        al = null;
        alc = null;
    }
}

/// <summary>
/// A streaming OpenAL source fed with interleaved float samples, converted to 16 bit since float buffers are an extension.
/// </summary>
internal sealed class OpenALStream : IDisposable
{
    private const int BufferCount = 4;

    private readonly uint source;
    private readonly uint[] buffers;
    private readonly Queue<uint> freeBuffers = new();
    private short[] conversion = [];
    private bool disposed;

    public int SampleRate { get; }
    public int Channels { get; }

    public OpenALStream(int sampleRate, int channels)
    {
        if (channels is not (1 or 2))
        {
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "Only mono and stereo are supported");
        }

        SampleRate = sampleRate;
        Channels = channels;

        using var _ = OpenALOutput.Lock.EnterScope();
        var al = OpenALOutput.Api;

        source = al.GenSource();
        buffers = al.GenBuffers(BufferCount);

        foreach (var buffer in buffers)
        {
            freeBuffers.Enqueue(buffer);
        }
    }

    public float Volume
    {
        set
        {
            using var _ = OpenALOutput.Lock.EnterScope();
            OpenALOutput.Api.SetSourceProperty(source, SourceFloat.Gain, Math.Clamp(value, 0f, 1f));
        }
    }

    /// <summary>Number of buffers queued and not yet played.</summary>
    public int QueuedBuffers
    {
        get
        {
            using var _ = OpenALOutput.Lock.EnterScope();
            Reclaim();
            return BufferCount - freeBuffers.Count;
        }
    }

    public bool IsPlaying
    {
        get
        {
            using var _ = OpenALOutput.Lock.EnterScope();
            OpenALOutput.Api.GetSourceProperty(source, GetSourceInteger.SourceState, out var state);
            return (SourceState)state == SourceState.Playing;
        }
    }

    /// <summary>Queues samples, waiting while every buffer is still in use. Returns false once disposed.</summary>
    public bool Submit(ReadOnlySpan<float> samples)
    {
        uint buffer;

        while (true)
        {
            if (disposed)
            {
                return false;
            }

            using (OpenALOutput.Lock.EnterScope())
            {
                Reclaim();

                if (freeBuffers.TryDequeue(out buffer))
                {
                    break;
                }
            }

            Thread.Sleep(2);
        }

        if (conversion.Length < samples.Length)
        {
            conversion = new short[samples.Length];
        }

        for (var i = 0; i < samples.Length; i++)
        {
            conversion[i] = (short)(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
        }

        using (OpenALOutput.Lock.EnterScope())
        {
            if (disposed)
            {
                return false;
            }

            var al = OpenALOutput.Api;
            var format = Channels == 2 ? BufferFormat.Stereo16 : BufferFormat.Mono16;

            unsafe
            {
                fixed (short* data = conversion)
                {
                    al.BufferData(buffer, format, data, samples.Length * sizeof(short), SampleRate);
                }
            }

            al.SourceQueueBuffers(source, [buffer]);

            // Restart after an underrun, or for the first buffer
            al.GetSourceProperty(source, GetSourceInteger.SourceState, out var state);

            if ((SourceState)state != SourceState.Playing && !Paused)
            {
                al.SourcePlay(source);
            }
        }

        return true;
    }

    public bool Paused
    {
        get;
        set
        {
            field = value;

            using var _ = OpenALOutput.Lock.EnterScope();

            if (value)
            {
                OpenALOutput.Api.SourcePause(source);
            }
            else
            {
                OpenALOutput.Api.SourcePlay(source);
            }
        }
    }

    /// <summary>Drops queued audio, for seeking.</summary>
    public void Flush()
    {
        using var _ = OpenALOutput.Lock.EnterScope();
        var al = OpenALOutput.Api;

        al.SourceStop(source);
        al.SetSourceProperty(source, SourceInteger.Buffer, 0);

        freeBuffers.Clear();

        foreach (var buffer in buffers)
        {
            freeBuffers.Enqueue(buffer);
        }
    }

    private void Reclaim()
    {
        var al = OpenALOutput.Api;
        al.GetSourceProperty(source, GetSourceInteger.BuffersProcessed, out var processed);

        if (processed <= 0)
        {
            return;
        }

        var done = new uint[processed];
        al.SourceUnqueueBuffers(source, done);

        foreach (var buffer in done)
        {
            freeBuffers.Enqueue(buffer);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        using var _ = OpenALOutput.Lock.EnterScope();
        disposed = true;

        var al = OpenALOutput.Api;
        al.SourceStop(source);
        al.SetSourceProperty(source, SourceInteger.Buffer, 0);
        al.DeleteSource(source);
        al.DeleteBuffers(buffers);
    }
}
