using System.Reflection;
using FileListPageCounter.Core.Diagnostics;
using FileListPageCounter.Core.Models;
using FileListPageCounter.Core.Reporting;
using FileListPageCounter.Core.Scanning;
using FileListPageCounter.Tests.Helpers;
using Xunit;

namespace FileListPageCounter.Tests;

/// <summary>
/// Requirements 21 and 22: nothing leaves the machine, and no working file is ever placed
/// beside the archive.
/// </summary>
public class OfflineAndPrivacyTests
{
    [Fact]
    public void The_core_library_does_not_reference_any_networking_assembly()
    {
        // A single HttpClient, socket or WebRequest anywhere in the core would add one of these
        // references, so this fails the moment the offline promise is broken.
        string[] networking =
        {
            "System.Net",
            "System.Net.Http",
            "System.Net.Sockets",
            "System.Net.Requests",
            "System.Net.WebClient",
            "System.Net.Primitives",
            "System.Net.NetworkInformation",
            "System.Net.Mail",
            "System.Net.WebSockets"
        };

        string[] referenced = typeof(ScanService).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(referenced, name => networking.Contains(name, StringComparer.OrdinalIgnoreCase));
        Assert.DoesNotContain(referenced, name => name.StartsWith("System.Net", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void No_public_core_api_accepts_or_returns_a_uri()
    {
        // Nothing in the core can be handed a remote address, so no caller can make it upload.
        MethodInfo[] methods = typeof(ScanService).Assembly
            .GetExportedTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .ToArray();

        Assert.DoesNotContain(methods, m =>
            m.ReturnType == typeof(Uri) ||
            m.GetParameters().Any(p => p.ParameterType == typeof(Uri)));
    }

    [Fact]
    public async Task A_full_run_writes_nothing_next_to_the_source_files()
    {
        using var source = new TempFolder();
        using var output = new TempFolder();

        TestPdfFactory.Write(source.File("a.pdf"), 2);
        TestImageFactory.WriteTiff(source.File("b.tiff"), frames: 3);
        TestPdfFactory.WriteCorrupt(source.File("c.pdf"));

        string[] before = Directory.GetFiles(source.Path, "*", SearchOption.AllDirectories).OrderBy(x => x).ToArray();

        ScanResult result = await new ScanService().ScanFolderAsync(source.Path, new ScanOptions());
        WordReportBuilder.Build(output.File("report.docx"), result.Entries, new ReportOptions());

        string[] after = Directory.GetFiles(source.Path, "*", SearchOption.AllDirectories).OrderBy(x => x).ToArray();

        Assert.Equal(before, after);
        Assert.Empty(Directory.GetDirectories(source.Path, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void The_processing_log_is_stored_under_local_application_data()
    {
        string logPath = ProcessingLog.LogFileLocation();
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(localAppData, logPath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FileListPageCounter", logPath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Errors_are_recorded_with_their_reason_for_the_optional_log()
    {
        using var source = new TempFolder();
        TestPdfFactory.WriteCorrupt(source.File("broken.pdf"));

        ScanResult result = await new ScanService().ScanFolderAsync(source.Path, new ScanOptions());

        Assert.Equal(1, result.UnknownCount);
        Assert.Contains(result.Log.Entries, e => e.Path.EndsWith("broken.pdf", StringComparison.OrdinalIgnoreCase));
        Assert.False(string.IsNullOrWhiteSpace(result.Log.Render()));
    }
}
