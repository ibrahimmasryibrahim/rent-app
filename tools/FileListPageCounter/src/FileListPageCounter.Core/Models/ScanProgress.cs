namespace FileListPageCounter.Core.Models;

public readonly record struct ScanProgress(int Processed, int Total)
{
    public double Percent => Total <= 0 ? 0d : Processed * 100d / Total;
}
