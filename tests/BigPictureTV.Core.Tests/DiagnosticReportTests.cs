namespace BigPictureTV.Core.Tests;

public class DiagnosticReportTests
{
    [Fact]
    public void ListsDisplaysSettingsAndLog()
    {
        var text = DiagnosticReport.Build("0.7.0", "Windows 11", new[] { Displays.Monitor, Displays.LgTv },
            Displays.LgTv, "detected automatically", new AppSettings(), DisplayMode.Desktop, new[] { "line one" });
        Assert.Contains("BigPictureTV 0.7.0", text);
        Assert.Contains("LG TV", text);
        Assert.Contains("GSM", text);
        Assert.Contains("Shortcut: Ctrl+Alt+F12; controller combo: off", text);
        Assert.Contains("  line one", text);
    }

    [Fact]
    public void TailOfAMissingFileIsEmpty() =>
        Assert.Empty(DiagnosticReport.Tail(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".log"), 10));
}
