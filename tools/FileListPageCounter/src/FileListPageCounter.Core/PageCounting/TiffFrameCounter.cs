using FileListPageCounter.Core.Common;

namespace FileListPageCounter.Core.PageCounting;

/// <summary>
/// Counts the frames (pages) of a TIFF by walking the IFD chain. Only a few bytes per frame are
/// read — no image data is ever decoded — which keeps multi-hundred-megabyte scans instant.
/// Classic TIFF and BigTIFF, little and big endian, are all supported.
/// </summary>
internal static class TiffFrameCounter
{
    private const int MaxFrames = 1_000_000; // guards against a corrupt, self-referencing IFD chain

    public static int? TryCount(string path, CancellationToken cancellationToken)
    {
        try
        {
            using FileStream stream = ReadOnlyFile.OpenRandomAccess(path);

            Span<byte> header = stackalloc byte[16];
            if (!ReadExactly(stream, header[..8])) return null;

            bool littleEndian;
            if (header[0] == 0x49 && header[1] == 0x49) littleEndian = true;       // "II"
            else if (header[0] == 0x4D && header[1] == 0x4D) littleEndian = false; // "MM"
            else return null;

            ushort magic = ReadUInt16(header[2..4], littleEndian);

            long nextIfd;
            bool bigTiff;

            if (magic == 42)
            {
                bigTiff = false;
                nextIfd = ReadUInt32(header[4..8], littleEndian);
            }
            else if (magic == 43)
            {
                bigTiff = true;
                ushort offsetSize = ReadUInt16(header[4..6], littleEndian);
                if (offsetSize != 8) return null;
                if (!ReadExactly(stream, header[8..16])) return null;
                nextIfd = (long)ReadUInt64(header[8..16], littleEndian);
            }
            else
            {
                return null;
            }

            int frames = 0;
            var visited = new HashSet<long>();

            while (nextIfd > 0 && nextIfd < stream.Length && frames < MaxFrames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!visited.Add(nextIfd)) break; // circular chain in a damaged file

                stream.Position = nextIfd;
                frames++;

                if (bigTiff)
                {
                    Span<byte> countBytes = stackalloc byte[8];
                    if (!ReadExactly(stream, countBytes)) break;
                    ulong entries = ReadUInt64(countBytes, littleEndian);

                    long afterEntries = nextIfd + 8 + (long)entries * 20;
                    if (afterEntries + 8 > stream.Length) break;

                    stream.Position = afterEntries;
                    Span<byte> nextBytes = stackalloc byte[8];
                    if (!ReadExactly(stream, nextBytes)) break;
                    nextIfd = (long)ReadUInt64(nextBytes, littleEndian);
                }
                else
                {
                    Span<byte> countBytes = stackalloc byte[2];
                    if (!ReadExactly(stream, countBytes)) break;
                    ushort entries = ReadUInt16(countBytes, littleEndian);

                    long afterEntries = nextIfd + 2 + (long)entries * 12;
                    if (afterEntries + 4 > stream.Length) break;

                    stream.Position = afterEntries;
                    Span<byte> nextBytes = stackalloc byte[4];
                    if (!ReadExactly(stream, nextBytes)) break;
                    nextIfd = ReadUInt32(nextBytes, littleEndian);
                }
            }

            return frames > 0 ? frames : null;
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

    private static bool ReadExactly(Stream stream, Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = stream.Read(buffer[total..]);
            if (read <= 0) return false;
            total += read;
        }

        return true;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> b, bool littleEndian) =>
        littleEndian
            ? (ushort)(b[0] | (b[1] << 8))
            : (ushort)((b[0] << 8) | b[1]);

    private static uint ReadUInt32(ReadOnlySpan<byte> b, bool littleEndian) =>
        littleEndian
            ? (uint)(b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24))
            : (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);

    private static ulong ReadUInt64(ReadOnlySpan<byte> b, bool littleEndian)
    {
        ulong value = 0;
        if (littleEndian)
        {
            for (int i = 7; i >= 0; i--) value = (value << 8) | b[i];
        }
        else
        {
            for (int i = 0; i < 8; i++) value = (value << 8) | b[i];
        }

        return value;
    }
}
