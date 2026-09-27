using AwesomeAssertions;
using Oire.PlanCake.Utils;
using Xunit;

namespace Oire.PlanCake.Tests;

public class ClipboardClassifierTests {
    private static ClipboardContent Classify(string[]? dropList, string? text) =>
        ClipboardClassifier.Classify(dropList, text);

    [Fact]
    public void DropList_OpensItsFirstMarkdownFile() {
        var content = Classify([@"C:\pictures\cake.png", @"C:\plans\plan.md", @"C:\plans\other.markdown"], null);

        content.Should().Be(new ClipboardContent(ClipboardContentKind.MarkdownFile, @"C:\plans\plan.md"));
    }

    [Fact]
    public void DropList_FindsTheMarkdownExtensionInAnyCase() {
        Classify([@"C:\plans\PLAN.MARKDOWN"], null)
            .Should().Be(new ClipboardContent(ClipboardContentKind.MarkdownFile, @"C:\plans\PLAN.MARKDOWN"));
    }

    [Fact]
    public void DropList_WithoutMarkdownFile_SaysSo() {
        Classify([@"C:\pictures\cake.png", @"C:\plans\notes.txt"], null).Kind
            .Should().Be(ClipboardContentKind.NoMarkdownFile);
    }

    [Fact]
    public void DropList_WithoutMarkdownFile_LetsUsableTextWin() {
        Classify([@"C:\pictures\cake.png"], "https://example.com/plan.md")
            .Should().Be(new ClipboardContent(ClipboardContentKind.Link, "https://example.com/plan.md"));
    }

    [Theory]
    [InlineData(@"C:\plans\plan.md", @"C:\plans\plan.md")]
    [InlineData("  C:\\plans\\plan.md\r\n", @"C:\plans\plan.md")]
    [InlineData(@"C:\plans\sub\..\my plan.markdown", @"C:\plans\my plan.markdown")]
    [InlineData(@"\\server\share\plan.md", @"\\server\share\plan.md")]
    [InlineData("file:///C:/plans/my%20plan.md", @"C:\plans\my plan.md")]
    public void UnquotedPath_OpensTheFile(string text, string expected) {
        Classify(null, text).Should().Be(new ClipboardContent(ClipboardContentKind.MarkdownFile, expected));
    }

    [Theory]
    [InlineData("\"C:\\plans\\plan.md\"", @"C:\plans\plan.md")]
    [InlineData("\"C:\\plans\\my plan.md\"\r\n", @"C:\plans\my plan.md")]
    public void QuotedPath_FromCopyAsPath_OpensTheFile(string text, string expected) {
        Classify(null, text).Should().Be(new ClipboardContent(ClipboardContentKind.MarkdownFile, expected));
    }

    [Theory]
    [InlineData("https://github.com/Oire/plan-cake/blob/master/README.md")]
    [InlineData("http://example.com/plan")]
    public void Link_IsDownloaded(string text) {
        Classify(null, $" {text} ").Should().Be(new ClipboardContent(ClipboardContentKind.Link, text));
    }

    [Theory]
    [InlineData("Some text someone copied")]
    [InlineData(@"C:\plans\notes.txt")]
    [InlineData(@"plans\plan.md")]
    [InlineData("plan.md")]
    [InlineData("C:\\plans\\a.md\r\nC:\\plans\\b.md")]
    [InlineData("ftp://example.com/plan.md")]
    [InlineData("\"\"")]
    public void OtherText_IsNothing(string text) {
        Classify(null, text).Should().Be(ClipboardContent.Nothing);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyClipboard_IsNothing(string? text) {
        Classify(null, text).Should().Be(ClipboardContent.Nothing);
        Classify([], text).Should().Be(ClipboardContent.Nothing);
    }
}
