using System.Runtime.InteropServices;
using BigPictureTV.Core.Display;

namespace BigPictureTV.Core.Tests;

public class NativeLayoutTests
{
    // Sizes from the Windows SDK headers; a mismatch would corrupt SetDisplayConfig calls.
    [Fact] public void PathInfoIs72Bytes() => Assert.Equal(72, Marshal.SizeOf<PATH_INFO>());
    [Fact] public void ModeInfoIs64Bytes() => Assert.Equal(64, Marshal.SizeOf<MODE_INFO>());
    [Fact] public void TargetDeviceNameIs420Bytes() => Assert.Equal(420, Marshal.SizeOf<TARGET_DEVICE_NAME>());

    [Fact]
    public void SourcePositionOverlaysTheUnion()
    {
        var m = new MODE_INFO { sourcePositionX = -1920, sourcePositionY = 5 };
        Assert.Equal(unchecked((uint)-1920), (uint)(m.raw1 >> 32));
        Assert.Equal(5u, (uint)m.raw2);
    }
}
