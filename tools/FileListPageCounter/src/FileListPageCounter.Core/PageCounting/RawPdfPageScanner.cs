using System.Text;
using FileListPageCounter.Core.Common;

namespace FileListPageCounter.Core.PageCounting;

/// <summary>
/// Last-resort page count for PDFs whose cross-reference table is damaged: streams the raw bytes
/// and counts "/Type /Page" dictionaries (excluding "/Pages"). Read-only and allocation-light;
/// it returns null rather than a guess when it finds nothing it trusts.
/// </summary>
internal static class RawPdfPageScanner
{
    private const int ChunkSize = 1 << 20;   // 1 MB
    private const int Overlap = 64;          // longest token we look for, with whitespace slack

    public static int? TryCount(string path, CancellationToken cancellationToken)
    {
        try
        {
            using FileStream stream = ReadOnlyFile.OpenSequential(path);

            byte[] buffer = new byte[ChunkSize + Overlap];
            int carried = 0;
            int count = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int read = stream.Read(buffer, carried, ChunkSize);
                if (read <= 0)
                {
                    // End of file: the carried tail has not been examined yet, so scan all of it.
                    if (carried > 0)
                    {
                        count += CountPageObjects(Encoding.Latin1.GetString(buffer, 0, carried), carried);
                    }

                    break;
                }

                int available = carried + read;

                // Latin1 maps every byte to the same code point, so string offsets stay byte-exact.
                string text = Encoding.Latin1.GetString(buffer, 0, available);

                // Matches that start inside the last Overlap bytes may be cut in half; leave them
                // for the next iteration, where they reappear at the front of the buffer.
                int limit = Math.Max(0, available - Overlap);
                count += CountPageObjects(text, limit);

                carried = available - limit;
                Buffer.BlockCopy(buffer, limit, buffer, 0, carried);
            }

            return count > 0 ? count : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Counts "/Type /Page" dictionaries whose "/Type" token starts before <paramref name="limit"/>.</summary>
    private static int CountPageObjects(string text, int limit)
    {
        int count = 0;
        int i = 0;

        while (i < limit)
        {
            int at = text.IndexOf("/Type", i, StringComparison.Ordinal);
            if (at < 0 || at >= limit) break;

            int j = at + "/Type".Length;
            while (j < text.Length && (text[j] == ' ' || text[j] == '\r' || text[j] == '\n' || text[j] == '\t')) j++;

            if (j < text.Length && text[j] == '/' && IsToken(text, j + 1, "Page"))
            {
                int after = j + 1 + "Page".Length;
                // "/Pages" is the page-tree node, not a page.
                char next = after < text.Length ? text[after] : ' ';
                if (!char.IsLetterOrDigit(next))
                {
                    count++;
                }
            }

            i = at + 1;
        }

        return count;
    }

    private static bool IsToken(string text, int start, string token) =>
        start + token.Length <= text.Length &&
        string.CompareOrdinal(text, start, token, 0, token.Length) == 0;
}
