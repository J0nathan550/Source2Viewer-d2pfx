using System.Buffers.Binary;
using NAudio.Wave;

namespace GUI.Types.Audio;

/// <summary>
/// Decodes Microsoft ADPCM to 16 bit PCM. NAudio only decodes it through the Windows audio compression manager,
/// this works everywhere.
/// </summary>
internal sealed class MsAdpcmWaveStream : WaveStream
{
    private static readonly int[] AdaptationTable = [230, 230, 230, 230, 307, 409, 512, 614, 768, 614, 512, 409, 307, 230, 230, 230];
    private static readonly int[] DefaultCoefficient1 = [256, 512, 0, 192, 240, 460, 392];
    private static readonly int[] DefaultCoefficient2 = [0, -256, 0, 64, 0, -208, -232];

    private readonly WaveStream source;
    private readonly WaveFormat outputFormat;
    private readonly int channels;
    private readonly int blockAlign;
    private readonly int samplesPerBlock;
    private readonly long blockCount;
    private readonly byte[] blockData;
    private readonly short[] decodedBlock;
    private long decodedBlockIndex = -1;
    private long position;

    public MsAdpcmWaveStream(WaveStream source)
    {
        if (source.WaveFormat.Encoding != WaveFormatEncoding.Adpcm)
        {
            throw new ArgumentException("Source is not Microsoft ADPCM", nameof(source));
        }

        this.source = source;
        channels = source.WaveFormat.Channels;
        blockAlign = source.WaveFormat.BlockAlign;
        samplesPerBlock = (blockAlign - 7 * channels) * 8 / (4 * channels) + 2;
        blockCount = source.Length / blockAlign;
        blockData = new byte[blockAlign];
        decodedBlock = new short[samplesPerBlock * channels];
        outputFormat = new WaveFormat(source.WaveFormat.SampleRate, 16, channels);
    }

    public override WaveFormat WaveFormat => outputFormat;

    public override long Length => blockCount * samplesPerBlock * channels * sizeof(short);

    public override long Position
    {
        get => position;
        set => position = Math.Clamp(value - value % outputFormat.BlockAlign, 0, Length);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var bytesPerBlock = samplesPerBlock * channels * sizeof(short);
        var written = 0;

        while (written < count && position < Length)
        {
            var blockIndex = position / bytesPerBlock;
            var offsetInBlock = (int)(position % bytesPerBlock);

            if (blockIndex != decodedBlockIndex && !DecodeBlock(blockIndex))
            {
                break;
            }

            var available = Math.Min(bytesPerBlock - offsetInBlock, count - written);
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(decodedBlock.AsSpan()).Slice(offsetInBlock, available).CopyTo(buffer.AsSpan(offset + written));

            written += available;
            position += available;
        }

        return written;
    }

    private bool DecodeBlock(long blockIndex)
    {
        source.Position = blockIndex * blockAlign;

        if (source.Read(blockData, 0, blockAlign) < 7 * channels)
        {
            return false;
        }

        Span<int> coefficient1 = stackalloc int[2];
        Span<int> coefficient2 = stackalloc int[2];
        Span<int> delta = stackalloc int[2];
        Span<int> sample1 = stackalloc int[2];
        Span<int> sample2 = stackalloc int[2];

        var data = blockData.AsSpan();
        var offset = 0;

        for (var c = 0; c < channels; c++)
        {
            var predictor = Math.Min((int)data[offset++], DefaultCoefficient1.Length - 1);
            coefficient1[c] = DefaultCoefficient1[predictor];
            coefficient2[c] = DefaultCoefficient2[predictor];
        }

        for (var c = 0; c < channels; c++, offset += 2)
        {
            delta[c] = BinaryPrimitives.ReadInt16LittleEndian(data[offset..]);
        }

        for (var c = 0; c < channels; c++, offset += 2)
        {
            sample1[c] = BinaryPrimitives.ReadInt16LittleEndian(data[offset..]);
        }

        for (var c = 0; c < channels; c++, offset += 2)
        {
            sample2[c] = BinaryPrimitives.ReadInt16LittleEndian(data[offset..]);
        }

        // The header samples come out first, oldest first
        var outIndex = 0;

        for (var c = 0; c < channels; c++)
        {
            decodedBlock[outIndex++] = (short)sample2[c];
        }

        for (var c = 0; c < channels; c++)
        {
            decodedBlock[outIndex++] = (short)sample1[c];
        }

        var channel = 0;

        while (outIndex < decodedBlock.Length && offset < data.Length)
        {
            var value = data[offset++];

            foreach (var nibble in (ReadOnlySpan<int>)[value >> 4, value & 0x0F])
            {
                if (outIndex >= decodedBlock.Length)
                {
                    break;
                }

                var signed = nibble >= 8 ? nibble - 16 : nibble;
                var predicted = (sample1[channel] * coefficient1[channel] + sample2[channel] * coefficient2[channel]) / 256 + signed * delta[channel];
                predicted = Math.Clamp(predicted, short.MinValue, short.MaxValue);

                sample2[channel] = sample1[channel];
                sample1[channel] = predicted;
                delta[channel] = Math.Max(16, AdaptationTable[nibble] * delta[channel] / 256);

                decodedBlock[outIndex++] = (short)predicted;
                channel = (channel + 1) % channels;
            }
        }

        // A short final block leaves the rest silent
        decodedBlock.AsSpan(outIndex).Clear();
        decodedBlockIndex = blockIndex;
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            source.Dispose();
        }

        base.Dispose(disposing);
    }
}
