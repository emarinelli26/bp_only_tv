using BigPictureTV.Core;
using BigPictureTV.Core.Display;

namespace BigPictureTV.Core.Tests;

sealed class ListLog : ILog
{
    public List<string> Lines { get; } = new();
    public void Write(string message) => Lines.Add(message);
}

sealed class FakeSwitcher : IDisplaySwitcher
{
    public bool HasSavedLayout { get; set; }
    public bool SwitchSucceeds { get; set; } = true;
    public int Switches { get; private set; }
    public int Restores { get; private set; }

    public bool SwitchToTv()
    {
        Switches++;
        if (SwitchSucceeds) HasSavedLayout = true;
        return SwitchSucceeds;
    }

    public void RestoreDesktop()
    {
        Restores++;
        HasSavedLayout = false;
    }
}

sealed class FakeDisplayConfig : IDisplayConfig
{
    public List<DisplayInfo> Displays { get; } = new();
    public Layout Active { get; set; } = new() { Paths = new PATH_INFO[2] };
    public int ApplyResult { get; set; }
    public int ExtendResult { get; set; }
    public List<(Layout Layout, bool Save)> Applied { get; } = new();
    public int Extends { get; private set; }

    public IReadOnlyList<DisplayInfo> ListDisplays() => Displays;
    public Layout QueryActive() => Active;

    public Layout? BuildSingleDisplay(string devicePath)
    {
        if (!Displays.Any(d => d.DevicePath == devicePath)) return null;
        var path = new PATH_INFO();
        path.targetInfo.id = 42;
        return new Layout { Paths = new[] { path }, Modes = new MODE_INFO[2] };
    }

    public int Apply(Layout layout, bool saveToDatabase)
    {
        Applied.Add((layout, saveToDatabase));
        return ApplyResult;
    }

    public int ApplyExtend()
    {
        Extends++;
        return ExtendResult;
    }
}

static class Displays
{
    // Emanuel's real setup, from BigPictureTV.ps1 -ListDisplays.
    public static DisplayInfo Monitor => new()
    {
        Name = "IQ24H",
        DevicePath = @"\\?\DISPLAY#HKN2380#5&61349ff&0&UID16640#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}",
        Active = true,
        Primary = true,
        Connection = OutputTechnology.DisplayPortExternal,
        Width = 1920,
        Height = 1080,
    };

    public static DisplayInfo LgTv => new()
    {
        Name = "LG TV",
        DevicePath = @"\\?\DISPLAY#GSM0001#5&61349ff&0&UID16642#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}",
        Active = true,
        Connection = OutputTechnology.Hdmi,
        Width = 3840,
        Height = 2160,
    };
}
