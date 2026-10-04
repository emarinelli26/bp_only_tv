using System.Text;
using BigPictureTV.Core.Display;

namespace BigPictureTV.Core.Tests;

public sealed class LayoutStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bptv-tests-" + Guid.NewGuid().ToString("N"));
    string File_ => Path.Combine(_dir, "saved-layout.json");

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    static Layout Sample()
    {
        var path = new PATH_INFO { flags = 1 };
        path.targetInfo.id = 7;
        path.targetInfo.adapterId = new LUID { LowPart = 0x1234, HighPart = -1 };
        var mode = new MODE_INFO { infoType = 1, sourceWidth = 3840, sourceHeight = 2160, sourcePositionX = -1920, raw5 = 0xDEADBEEF };
        return new Layout { Paths = new[] { path }, Modes = new[] { mode } };
    }

    [Fact]
    public void SaveThenLoadKeepsEveryByte()
    {
        var store = new LayoutStore(File_);
        Assert.False(store.HasSaved);
        var original = Sample();
        store.Save(original);
        Assert.True(store.HasSaved);

        var loaded = store.Load();
        Assert.Equal(LayoutStore.ToBase64(original.Paths), LayoutStore.ToBase64(loaded.Paths));
        Assert.Equal(LayoutStore.ToBase64(original.Modes), LayoutStore.ToBase64(loaded.Modes));
        Assert.Equal(-1920, loaded.Modes[0].sourcePositionX);

        store.Delete();
        Assert.False(store.HasSaved);
    }

    [Fact]
    public void ReadsTheFileThePowerShellScriptWrites()
    {
        // Windows PowerShell 5.1 writes UTF-8 with a BOM and an ISO 8601 date.
        var sample = Sample();
        string json = "{\r\n    \"Saved\":  \"2026-10-04T00:31:50.1234567-03:00\",\r\n" +
                      $"    \"Paths\":  \"{LayoutStore.ToBase64(sample.Paths)}\",\r\n" +
                      $"    \"Modes\":  \"{LayoutStore.ToBase64(sample.Modes)}\"\r\n}}";
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var loaded = new LayoutStore(File_).Load();
        Assert.Single(loaded.Paths);
        Assert.Equal(7u, loaded.Paths[0].targetInfo.id);
        Assert.Equal(3840u, loaded.Modes[0].sourceWidth);
    }

    [Fact]
    public void RejectsBytesThatAreNotWholeStructs()
    {
        Assert.Throws<FormatException>(() => LayoutStore.FromBase64<MODE_INFO>(Convert.ToBase64String(new byte[10])));
    }
}
