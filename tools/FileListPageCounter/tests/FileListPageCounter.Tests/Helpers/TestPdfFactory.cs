using System.Text;

namespace FileListPageCounter.Tests.Helpers;

/// <summary>
/// Builds real, structurally valid PDF files (classic cross-reference table) so the page counter
/// is exercised against actual documents rather than mocks.
/// </summary>
internal static class TestPdfFactory
{
    public static void Write(string path, int pageCount)
    {
        File.WriteAllBytes(path, Build(pageCount));
    }

    public static byte[] Build(int pageCount)
    {
        if (pageCount < 1) throw new ArgumentOutOfRangeException(nameof(pageCount));

        // Object 1 = catalog, object 2 = page tree, objects 3.. = pages.
        var objects = new List<string>();

        objects.Add("<< /Type /Catalog /Pages 2 0 R >>");

        var kids = new StringBuilder();
        for (int i = 0; i < pageCount; i++)
        {
            if (i > 0) kids.Append(' ');
            kids.Append(i + 3).Append(" 0 R");
        }

        objects.Add($"<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>");

        for (int i = 0; i < pageCount; i++)
        {
            objects.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>");
        }

        var body = new StringBuilder();
        body.Append("%PDF-1.4\n");

        var offsets = new int[objects.Count + 1];

        for (int i = 0; i < objects.Count; i++)
        {
            offsets[i + 1] = ByteLength(body);
            body.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
        }

        int xrefOffset = ByteLength(body);

        body.Append("xref\n0 ").Append(objects.Count + 1).Append('\n');
        body.Append("0000000000 65535 f \n");
        for (int i = 1; i <= objects.Count; i++)
        {
            body.Append(offsets[i].ToString("D10")).Append(" 00000 n \n");
        }

        body.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R >>\n");
        body.Append("startxref\n").Append(xrefOffset).Append("\n%%EOF\n");

        return Encoding.Latin1.GetBytes(body.ToString());
    }

    /// <summary>A file that starts like a PDF but whose structure is unusable.</summary>
    public static void WriteCorrupt(string path)
    {
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.Latin1.GetBytes("%PDF-1.4\n"));
        bytes.AddRange(Encoding.Latin1.GetBytes("this is not a pdf body at all, no objects, no xref, no trailer\n"));
        bytes.AddRange(new byte[] { 0x00, 0xFF, 0x13, 0x37, 0x00, 0xAB });
        File.WriteAllBytes(path, bytes.ToArray());
    }

    // Latin1 keeps one byte per character, so the character count is the byte offset.
    private static int ByteLength(StringBuilder sb) => sb.Length;
}
