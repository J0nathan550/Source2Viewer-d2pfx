using System.IO;
using System.Threading.Tasks;
using GUI.Utils;

namespace GUI.Types.Viewers
{
    class ByteViewer(VrfGuiContext vrfGuiContext) : IViewer, IDisposable
    {
        private byte[] input = [];

        public static bool IsAccepted() => true;

        public async Task LoadAsync(Stream? stream)
        {
            if (stream == null)
            {
                input = await File.ReadAllBytesAsync(vrfGuiContext.FileName!).ConfigureAwait(false);
            }
            else
            {
                input = new byte[stream.Length];
                stream.ReadExactly(input);
            }
        }

        public ViewerContent GetContent()
        {
            var bytes = input;
            List<ViewerTab> tabs = [new("Hex", new ViewerContent.HexDump(bytes))];

            // Decoded only when the text tab is shown, the hex view already holds the whole file
            if (IsText(bytes))
            {
                tabs.Add(new("Text", new ViewerContent.LazyText(() => GetTextFromBytes(bytes) ?? string.Empty), Select: true));
            }

            input = [];

            return new ViewerContent.Tabs(tabs);
        }

        /// <summary>Whether <see cref="GetTextFromBytes"/> would return non-empty text, without decoding it.</summary>
        public static bool IsText(ReadOnlySpan<byte> span)
        {
            if (span.Length == 0)
            {
                return false;
            }

            // Byte order marks, see GetTextFromBytes
            if (span.Length >= 2 && ((span[0] == 0xFF && span[1] == 0xFE) || (span[0] == 0xFE && span[1] == 0xFF)))
            {
                return true;
            }

            if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
            {
                return true;
            }

            if (span.Length >= 4 && span[0] == 0x00 && span[1] == 0x00 && span[2] == 0xFE && span[3] == 0xFF)
            {
                return true;
            }

            var firstNullByte = span.IndexOf((byte)0);

            if (firstNullByte < 0)
            {
                return true;
            }

            return firstNullByte > 0 && !span[(firstNullByte + 1)..].ContainsAnyExcept((byte)0);
        }

        public static string? GetTextFromBytes(ReadOnlySpan<byte> span)
        {
            if (span.Length >= 4 && span[0] == 0xFF && span[1] == 0xFE && span[2] == 0x00 && span[3] == 0x00)  // UTF-32 LE BOM
            {
                var enc = new System.Text.UTF32Encoding(bigEndian: false, byteOrderMark: true);
                return enc.GetString(span[4..]);
            }

            if (span.Length >= 4 && span[0] == 0x00 && span[1] == 0x00 && span[2] == 0xFE && span[3] == 0xFF) // UTF-32 BE BOM
            {
                var enc = new System.Text.UTF32Encoding(bigEndian: true, byteOrderMark: true);
                return enc.GetString(span[4..]);
            }

            if (span.Length >= 2 && span[0] == 0xFF && span[1] == 0xFE) // UTF-16 LE BOM
            {
                return System.Text.Encoding.Unicode.GetString(span[2..]);
            }

            if (span.Length >= 2 && span[0] == 0xFE && span[1] == 0xFF) // UTF-16 BE BOM
            {
                var enc = new System.Text.UnicodeEncoding(bigEndian: true, byteOrderMark: true);
                return enc.GetString(span[2..]);
            }

            if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF) // UTF-8 BOM
            {
                return System.Text.Encoding.UTF8.GetString(span[3..]);
            }

            var firstNullByte = span.IndexOf((byte)0);
            if (firstNullByte < 0)
            {
                return System.Text.Encoding.UTF8.GetString(span); // No null bytes found
            }

            if (firstNullByte == 0)
            {
                return null; // Starts with null byte
            }

            // Check if everything after first null byte is also null
            var remainingBytes = span[(firstNullByte + 1)..];
            foreach (var b in remainingBytes)
            {
                if (b != 0)
                {
                    return null; // Has embedded null bytes
                }
            }

            // Only trailing nulls, trim them and decode as UTF-8
            return System.Text.Encoding.UTF8.GetString(span[..firstNullByte]);
        }

        public void Dispose()
        {
            //
        }
    }
}
