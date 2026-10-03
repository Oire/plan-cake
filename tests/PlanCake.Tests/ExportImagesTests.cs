using AwesomeAssertions;
using Oire.PlanCake.Notes;
using Oire.PlanCake.Rendering;
using Xunit;

namespace Oire.PlanCake.Tests;

/// <summary>
/// An export carries its local pictures inside it as data: URIs (its content security policy
/// allows no file:), leaves remote ones as links, and never reads another host's share.
/// </summary>
public sealed class ExportImagesTests: IDisposable {
    // The smallest valid PNG: one transparent pixel.
    private static readonly byte[] _png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNgYGBgAAAABQABpfZFQAAAAABJRU5ErkJggg=="
    );

    private static readonly string _pngDataUri = "data:image/png;base64," + Convert.ToBase64String(_png);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "plancake-export-" + Guid.NewGuid().ToString("N"));

    public ExportImagesTests() => Directory.CreateDirectory(_folder);

    public void Dispose() {
        Directory.Delete(_folder, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string Export(string source) => MarkdownRenderer.Render(
        source,
        new RenderOptions(NoteMarkers.Default, RenderMode.Export, new RenderStrings("User note"), DocumentFolder: _folder)
    ).Html;

    [Fact]
    public void Export_EmbedsALocalMarkdownPicture() {
        File.WriteAllBytes(Path.Combine(_folder, "diagram.png"), _png);

        var html = Export("![The diagram](diagram.png)\n");

        html.Should().Contain($"src=\"{_pngDataUri}\"").And.NotContain("src=\"diagram.png\"");
    }

    [Fact]
    public void Export_EmbedsARawHtmlPictureInASubfolder() {
        Directory.CreateDirectory(Path.Combine(_folder, "my pictures"));
        File.WriteAllBytes(Path.Combine(_folder, "my pictures", "a.png"), _png);

        var html = Export("<p><img alt='a' src='my%20pictures/a.png'></p>\n");

        html.Should().Contain($"src=\"{_pngDataUri}\"");
    }

    [Fact]
    public void Export_LeavesARemotePictureAsALink() {
        var html = Export("![Remote](https://example.com/remote.png)\n");

        html.Should().Contain("src=\"https://example.com/remote.png\"").And.NotContain("data:image");
    }

    [Fact]
    public void Export_LeavesAMissingOrUnknownFileAsALink() {
        File.WriteAllText(Path.Combine(_folder, "notes.txt"), "not a picture");

        var html = Export("![Missing](missing.png)\n\n![Text](notes.txt)\n");

        html.Should().Contain("src=\"missing.png\"").And.Contain("src=\"notes.txt\"").And.NotContain("data:image");
    }

    [Fact]
    public void Export_LeavesAPictureOverTheLimitAsALink() {
        using (var stream = File.Create(Path.Combine(_folder, "huge.png"))) {
            stream.SetLength(ExportImages.MaxImageBytes + 1);
        }

        var html = Export("![Huge](huge.png)\n");

        html.Should().Contain("src=\"huge.png\"").And.NotContain("data:image");
    }

    [Fact]
    public void Export_LeavesCodeAlone() {
        File.WriteAllBytes(Path.Combine(_folder, "diagram.png"), _png);

        var html = Export("`<img src=\"diagram.png\">`\n");

        html.Should().NotContain("data:image");
    }

    [Theory]
    [InlineData(@"\\otherhost\share\x.png")]
    [InlineData("file://otherhost/share/x.png")]
    [InlineData("//otherhost/share/x.png")]
    public void LocalPath_NeverNamesAnotherHostsShare(string source) {
        ExportImages.LocalPath(source, @"C:\plans").Should().BeNull();
    }

    [Fact]
    public void LocalPath_KeepsAShareOnTheDocumentsOwnHost() {
        ExportImages.LocalPath("x.png", @"\\server\plans").Should().Be(@"\\server\plans\x.png");
    }

    [Theory]
    [InlineData("https://example.com/x.png")]
    [InlineData("http://example.com/x.png")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("#top")]
    [InlineData("")]
    public void LocalPath_IsNullForWhatIsNotALocalFile(string source) {
        ExportImages.LocalPath(source, @"C:\plans").Should().BeNull();
    }

    [Fact]
    public void LocalPath_ResolvesRelativeAndAbsolutePaths() {
        ExportImages.LocalPath("img/a.png?v=2#x", @"C:\plans").Should().Be(@"C:\plans\img\a.png");
        ExportImages.LocalPath(@"D:\pictures\a.png", @"C:\plans").Should().Be(@"D:\pictures\a.png");
        ExportImages.LocalPath("a.png", null).Should().BeNull();
    }
}
