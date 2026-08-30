namespace FileListPageCounter.Tests.Helpers;

/// <summary>Builds small but structurally real image files, including multi-frame TIFFs.</summary>
internal static class TestImageFactory
{
    /// <summary>A minimal JPEG: SOI, an APP0/JFIF segment and EOI.</summary>
    public static void WriteJpeg(string path)
    {
        byte[] bytes =
        {
            0xFF, 0xD8,                                     // SOI
            0xFF, 0xE0, 0x00, 0x10,                         // APP0, length 16
            0x4A, 0x46, 0x49, 0x46, 0x00,                   // "JFIF\0"
            0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01,       // version, units, density
            0x00, 0x00,                                     // no thumbnail
            0xFF, 0xD9                                      // EOI
        };

        File.WriteAllBytes(path, bytes);
    }

    /// <summary>A 1x1 transparent PNG.</summary>
    public static void WritePng(string path)
    {
        byte[] bytes =
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
            0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
            0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
            0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
            0x42, 0x60, 0x82
        };

        File.WriteAllBytes(path, bytes);
    }

    /// <summary>
    /// A little-endian classic TIFF holding <paramref name="frames"/> IFDs chained together.
    /// Each IFD carries one minimal entry; no pixel data is needed to count pages.
    /// </summary>
    public static void WriteTiff(string path, int frames)
    {
        if (frames < 1) throw new ArgumentOutOfRangeException(nameof(frames));

        const int HeaderSize = 8;
        const int EntryCount = 1;
        const int IfdSize = 2 + (EntryCount * 12) + 4; // entry count + entries + next-IFD pointer

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((byte)0x49);        // "II" — little endian
        writer.Write((byte)0x49);
        writer.Write((ushort)42);        // classic TIFF
        writer.Write((uint)HeaderSize);  // offset of the first IFD

        for (int i = 0; i < frames; i++)
        {
            writer.Write((ushort)EntryCount);

            writer.Write((ushort)0x0100); // ImageWidth
            writer.Write((ushort)3);      // SHORT
            writer.Write((uint)1);        // one value
            writer.Write((ushort)1);      // value = 1
            writer.Write((ushort)0);      // padding to 4 bytes

            bool isLast = i == frames - 1;
            uint next = isLast ? 0u : (uint)(HeaderSize + ((i + 1) * IfdSize));
            writer.Write(next);
        }

        writer.Flush();
        File.WriteAllBytes(path, stream.ToArray());
    }
}
