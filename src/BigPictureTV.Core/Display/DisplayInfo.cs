namespace BigPictureTV.Core.Display;

/// <summary>How a display is connected (DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY).</summary>
public enum OutputTechnology : uint
{
    Other = 0xFFFFFFFF,
    Hd15 = 0,
    SVideo = 1,
    CompositeVideo = 2,
    ComponentVideo = 3,
    Dvi = 4,
    Hdmi = 5,
    Lvds = 6,
    DJpn = 8,
    Sdi = 9,
    DisplayPortExternal = 10,
    DisplayPortEmbedded = 11,
    UdiExternal = 12,
    UdiEmbedded = 13,
    Sdtvdongle = 14,
    Miracast = 15,
    IndirectWired = 16,
    IndirectVirtual = 17,
    DisplayPortUsbTunnel = 18,
    Internal = 0x80000000,
}

/// <summary>One physical display that Windows can currently see.</summary>
public sealed record DisplayInfo
{
    /// <summary>Friendly name from the EDID, e.g. "LG TV". Can be empty.</summary>
    public string Name { get; init; } = "";

    /// <summary>Stable device path, e.g. \\?\DISPLAY#GSM0001#...; survives reboots.</summary>
    public string DevicePath { get; init; } = "";

    public bool Active { get; init; }
    public bool Primary { get; init; }
    public OutputTechnology Connection { get; init; } = OutputTechnology.Other;
    public uint Width { get; init; }
    public uint Height { get; init; }

    /// <summary>Desktop position of an active display; the primary one is at (0,0).</summary>
    public int X { get; init; }
    public int Y { get; init; }

    /// <summary>GDI name of an active display (\\.\DISPLAY1), to match it with a screen.</summary>
    public string GdiName { get; init; } = "";

    /// <summary>Three-letter PnP manufacturer id taken from the device path (GSM = LG, SAM = Samsung...).</summary>
    public string Manufacturer => ManufacturerFromDevicePath(DevicePath);

    public static string ManufacturerFromDevicePath(string devicePath)
    {
        const string marker = "DISPLAY#";
        int i = devicePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (i < 0 || devicePath.Length < i + marker.Length + 3) return "";
        string id = devicePath.Substring(i + marker.Length, 3);
        return id.All(char.IsLetter) ? id.ToUpperInvariant() : "";
    }

    public override string ToString() => string.IsNullOrEmpty(Name) ? DevicePath : Name;
}
