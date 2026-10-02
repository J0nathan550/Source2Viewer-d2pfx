using NAudio.Wave;
using NLayer.NAudioSupport;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Audio
{
    internal static class AudioPlayer
    {
        /// <summary>
        /// Creates a decoded <see cref="WaveStream"/> and loop markers for the streaming sound
        /// data of a compiled sound resource, or null when the resource has no sound data.
        /// The caller takes ownership of the returned stream.
        /// </summary>
        public static (WaveStream Stream, (int Start, int End) LoopMarkers)? CreateWaveStream(Resource resource)
        {
            var soundData = (Sound?)resource.DataBlock;

            if (soundData == null || soundData.StreamingDataSize == 0)
            {
                return null;
            }

            var stream = soundData.GetSoundStream();

            try
            {
                WaveStream waveStream = soundData.SoundType switch
                {
                    Sound.AudioFileType.WAV => ToPcm(new WaveFileReader(stream)),
                    Sound.AudioFileType.MP3 => new Mp3FileReaderBase(stream, wf => new Mp3FrameDecompressor(wf)),
                    // The WinForms GUI decodes these through Media Foundation, which only exists on Windows
                    Sound.AudioFileType.AAC => throw new NotSupportedException("AAC sounds can not be played in this version, export the sound to listen to it."),
                    _ => throw new UnexpectedMagicException("Don't know how to play", (int)soundData.SoundType, nameof(soundData.SoundType)),
                };

                return (waveStream, (soundData.LoopStart, soundData.LoopEnd));
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        /// <summary>Wraps compressed wav data in a decoder, so playback only ever sees PCM or float samples.</summary>
        public static WaveStream ToPcm(WaveStream waveStream)
        {
            return waveStream.WaveFormat.Encoding switch
            {
                WaveFormatEncoding.Pcm or WaveFormatEncoding.IeeeFloat => waveStream,
                WaveFormatEncoding.Adpcm => new MsAdpcmWaveStream(waveStream),
                var encoding => throw new NotSupportedException($"Wave encoding {encoding} is not supported"),
            };
        }
    }
}
