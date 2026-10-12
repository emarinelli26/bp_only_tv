using BigPictureTV.Core.TvMenu;

namespace BigPictureTV.Core.Tests;

public class IconLinksTests
{
    static readonly Uri Page = new("https://www.example.com/tv");

    [Fact]
    public void LargestDeclaredIconComesFirstThenTheFixedPlaces()
    {
        const string html = """
            <head>
            <link rel="shortcut icon" href="/favicon.ico" type="image/x-icon">
            <link rel="icon" href="https://cdn.example.com/img/icon_32.png" sizes="32x32">
            <link rel='icon' href='//cdn.example.com/img/icon_144.png' sizes='144x144'>
            <link rel="apple-touch-icon" href="img/touch.png">
            <link rel="icon" href="/logo.svg" type="image/svg+xml">
            <link rel="stylesheet" href="/site.css">
            </head>
            """;
        var found = IconLinks.Candidates(html, Page).Select(u => u.ToString()).ToList();
        Assert.Equal(new[]
        {
            "https://www.example.com/img/touch.png",        // apple-touch-icon: 180 when not said
            "https://cdn.example.com/img/icon_144.png",
            "https://www.example.com/favicon.ico",          // a plain icon: 32
            "https://cdn.example.com/img/icon_32.png",
            "https://www.example.com/apple-touch-icon.png",
        }, found);
    }

    [Fact]
    public void WithNoLinksTheFixedPlacesAreTried()
    {
        var found = IconLinks.Candidates("<html></html>", Page).Select(u => u.ToString());
        Assert.Equal(new[] { "https://www.example.com/apple-touch-icon.png", "https://www.example.com/favicon.ico" }, found);
    }

    [Fact]
    public void EntitiesInAddressesAreDecoded()
    {
        var found = IconLinks.Candidates("""<link href="/i.png?a=1&amp;b=2" rel="icon" sizes="16x16 64x64">""", Page);
        Assert.Equal("https://www.example.com/i.png?a=1&b=2", found[0].ToString());
    }

    [Fact]
    public void ReadsTheSizeOfPngAndTheLargestPictureOfAnIco()
    {
        var png = new byte[24];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 13, 10, 26, 10 }.CopyTo(png, 0);
        png[18] = 0; png[19] = 144; // width 144
        Assert.Equal(144, IconLinks.PixelSize(png));

        var ico = new byte[6 + 3 * 16];
        ico[2] = 1; ico[4] = 3;
        ico[6] = 16; ico[22] = 0; ico[38] = 48; // 16, 256 (0), 48
        Assert.Equal(256, IconLinks.PixelSize(ico));

        Assert.Equal(0, IconLinks.PixelSize(new byte[] { 0xFF, 0xD8, 0xFF, 0, 0, 0, 0, 0, 0 })); // JPEG: unknown
    }

    [Fact]
    public void OnlyImagesTheMenuCanDrawAreAccepted()
    {
        Assert.True(IconLinks.IsImage(new byte[] { 0x89, 0x50, 0x4E, 0x47, 13, 10, 26, 10, 0 }));
        Assert.True(IconLinks.IsImage(new byte[] { 0, 0, 1, 0, 1, 0, 32, 32, 0 }));
        Assert.False(IconLinks.IsImage("<!DOCTYPE html><html>"u8));
        Assert.False(IconLinks.IsImage(new byte[] { 0x89, 0x50 }));
    }
}
