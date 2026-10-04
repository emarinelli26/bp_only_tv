using System.Runtime.InteropServices;
using System.Text;

namespace BigPictureTV.Core.Display;

// Windows CCD API (QueryDisplayConfig / SetDisplayConfig) declarations, ported
// from BigPictureTV.ps1. Struct layouts must match the Win32 headers exactly.

[StructLayout(LayoutKind.Sequential)]
public struct LUID
{
    public uint LowPart;
    public int HighPart;
}

[StructLayout(LayoutKind.Sequential)]
public struct RATIONAL
{
    public uint Numerator;
    public uint Denominator;
}

[StructLayout(LayoutKind.Sequential)]
public struct PATH_SOURCE_INFO
{
    public LUID adapterId;
    public uint id;
    public uint modeInfoIdx;
    public uint statusFlags;
}

[StructLayout(LayoutKind.Sequential)]
public struct PATH_TARGET_INFO
{
    public LUID adapterId;
    public uint id;
    public uint modeInfoIdx;
    public uint outputTechnology;
    public uint rotation;
    public uint scaling;
    public RATIONAL refreshRate;
    public uint scanLineOrdering;
    public int targetAvailable;
    public uint statusFlags;
}

[StructLayout(LayoutKind.Sequential)]
public struct PATH_INFO
{
    public PATH_SOURCE_INFO sourceInfo;
    public PATH_TARGET_INFO targetInfo;
    public uint flags;
}

// DISPLAYCONFIG_MODE_INFO is 64 bytes: a 16-byte header and a 48-byte union.
// The raw words keep every byte of the union intact; the named fields
// overlay the source-mode variant, which is the only one we edit.
[StructLayout(LayoutKind.Explicit, Size = 64)]
public struct MODE_INFO
{
    [FieldOffset(0)] public uint infoType;
    [FieldOffset(4)] public uint id;
    [FieldOffset(8)] public LUID adapterId;
    [FieldOffset(16)] public ulong raw0;
    [FieldOffset(24)] public ulong raw1;
    [FieldOffset(32)] public ulong raw2;
    [FieldOffset(40)] public ulong raw3;
    [FieldOffset(48)] public ulong raw4;
    [FieldOffset(56)] public ulong raw5;
    [FieldOffset(16)] public uint sourceWidth;
    [FieldOffset(20)] public uint sourceHeight;
    [FieldOffset(24)] public uint sourcePixelFormat;
    [FieldOffset(28)] public int sourcePositionX;
    [FieldOffset(32)] public int sourcePositionY;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct TARGET_DEVICE_NAME
{
    public uint type;
    public uint size;
    public LUID adapterId;
    public uint id;
    public uint flags;
    public uint outputTechnology;
    public ushort edidManufactureId;
    public ushort edidProductCodeId;
    public uint connectorInstance;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string monitorFriendlyDeviceName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string monitorDevicePath;
}

public sealed class Layout
{
    public PATH_INFO[] Paths = Array.Empty<PATH_INFO>();
    public MODE_INFO[] Modes = Array.Empty<MODE_INFO>();
}

internal static class NativeMethods
{
    public const uint QDC_ALL_PATHS = 0x1;
    public const uint QDC_ONLY_ACTIVE_PATHS = 0x2;
    public const uint SDC_TOPOLOGY_EXTEND = 0x4;
    public const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x20;
    public const uint SDC_APPLY = 0x80;
    public const uint SDC_SAVE_TO_DATABASE = 0x200;
    public const uint SDC_ALLOW_CHANGES = 0x400;
    public const uint PATH_ACTIVE = 0x1;
    public const uint INVALID_IDX = 0xFFFFFFFF;
    public const uint GET_TARGET_NAME = 2;

    public const uint ApplyFlags = SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES;

    [DllImport("user32.dll")]
    public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);

    [DllImport("user32.dll")]
    public static extern int QueryDisplayConfig(uint flags, ref uint numPaths, [Out] PATH_INFO[] paths,
        ref uint numModes, [Out] MODE_INFO[] modes, IntPtr topologyId);

    [DllImport("user32.dll")]
    public static extern int SetDisplayConfig(uint numPaths, [In] PATH_INFO[]? paths,
        uint numModes, [In] MODE_INFO[]? modes, uint flags);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref TARGET_DEVICE_NAME request);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
}
