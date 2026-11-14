using System.Text;

namespace ForceBond.Launcher;

internal static class RuntimeLogReader
{
    public static string ReadTail(string path, int maxBytes = 32768, int maxLines = 70)
    {
        if (maxBytes < 4 || maxLines < 1) throw new ArgumentOutOfRangeException();
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        byte[] probe = new byte[(int)Math.Min(file.Length, 1024)];
        int probed = file.Read(probe);
        Encoding encoding = Encoding.UTF8;
        int bom = 0;
        if (probed >= 2 && probe[0] == 0xff && probe[1] == 0xfe) { encoding = Encoding.Unicode; bom = 2; }
        else if (probed >= 2 && probe[0] == 0xfe && probe[1] == 0xff) { encoding = Encoding.BigEndianUnicode; bom = 2; }
        else if (probed >= 3 && probe[0] == 0xef && probe[1] == 0xbb && probe[2] == 0xbf) { bom = 3; }
        else if (probed >= 8)
        {
            // The injected runtime used BOM-less UTF-16; native OdysseyMP
            // writes UTF-8. ASCII log prefixes expose UTF-16's zero byte lane.
            int even = 0, odd = 0;
            for (int i = 0; i < probed; i++) if (probe[i] == 0) { if ((i & 1) == 0) even++; else odd++; }
            if (odd > probed / 8 && odd > even * 3) encoding = Encoding.Unicode;
            else if (even > probed / 8 && even > odd * 3) encoding = Encoding.BigEndianUnicode;
        }
        long start = Math.Max(bom, file.Length - maxBytes);
        if (encoding.CodePage is 1200 or 1201) start -= start % 2;
        file.Position = start;
        byte[] bytes = new byte[(int)Math.Min(maxBytes + 2L, file.Length - start)];
        int count = 0, read;
        while (count < bytes.Length && (read = file.Read(bytes, count, bytes.Length - count)) > 0) count += read;
        int offset = 0;
        if (encoding.CodePage == Encoding.UTF8.CodePage && start > bom)
            while (offset < count && (bytes[offset] & 0xc0) == 0x80) offset++;
        string text = encoding.GetString(bytes, offset, count - offset);
        if (start > bom && text.IndexOf('\n') is int newline && newline >= 0) text = text[(newline + 1)..];
        return string.Join(Environment.NewLine, text.Replace("\r\n", "\n").Split('\n').TakeLast(maxLines));
    }
}
