using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using GUI.Controls;
using GUI.Types.Audio;
using GUI.Utils;
using NAudio.Wave;
using NLayer.NAudioSupport;

namespace GUI.Types.Viewers
{
    class Audio(VrfGuiContext vrfGuiContext, bool isPreview) : IViewer, IDisposable
    {
        private WaveStream? waveStream;
        private AudioPlaybackPanel? playbackPanel;

        public static bool IsAccepted(uint magic, string fileName)
        {
            return (magic == 0x46464952 /* RIFF */ && fileName.EndsWith(".wav", StringComparison.InvariantCultureIgnoreCase)) ||
                    fileName.EndsWith(".mp3", StringComparison.InvariantCultureIgnoreCase);
        }

        public async Task LoadAsync(Stream? stream)
        {
            if (stream == null)
            {
                if (vrfGuiContext.FileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                {
                    waveStream = AudioPlayer.ToPcm(new WaveFileReader(vrfGuiContext.FileName));
                }
                else if (vrfGuiContext.FileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                {
                    waveStream = new Mp3FileReaderBase(vrfGuiContext.FileName, wf => new Mp3FrameDecompressor(wf));
                }
                else
                {
                    throw new NotImplementedException($"Unknown audio file extension: {Path.GetExtension(vrfGuiContext.FileName)}");
                }
            }
            else if (vrfGuiContext.FileName!.EndsWith(".mp3", StringComparison.InvariantCultureIgnoreCase))
            {
                waveStream = new Mp3FileReaderBase(stream, wf => new Mp3FrameDecompressor(wf));
            }
            else
            {
                waveStream = AudioPlayer.ToPcm(new WaveFileReader(stream));
            }
        }

        public Control Create()
        {
            Debug.Assert(waveStream is not null);

            var autoPlay = ((Settings.QuickPreviewFlags)Settings.Config.QuickFilePreview & Settings.QuickPreviewFlags.AutoPlaySounds) != 0;
            playbackPanel = new AudioPlaybackPanel(waveStream, isPreview && autoPlay, (0, 0));

            // The panel owns the stream now
            waveStream = null;

            return playbackPanel;
        }

        public void Dispose()
        {
            playbackPanel?.Dispose();
            playbackPanel = null;
            waveStream?.Dispose();
            waveStream = null;
        }
    }
}
