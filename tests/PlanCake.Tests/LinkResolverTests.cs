using AwesomeAssertions;
using Oire.PlanCake.Utils;
using Xunit;

namespace Oire.PlanCake.Tests;

public class LinkResolverTests {
    private const string Folder = @"C:\plans";

    private static readonly HashSet<string> _existing = new(StringComparer.OrdinalIgnoreCase) {
        @"C:\plans\other.md",
        @"C:\plans\sub\nested.markdown",
        @"C:\plans\my plan.md",
        @"C:\plans\diagram.png",
        @"C:\plans\assets",
        @"C:\notes.md",
        @"D:\elsewhere\UPPER.MD",
    };

    private static LinkTarget Resolve(string? href, string? folder = Folder) =>
        LinkResolver.Resolve(href, folder, _existing.Contains);

    [Theory]
    [InlineData("https://github.com/Oire/plan-cake", "https://github.com/Oire/plan-cake")]
    [InlineData("http://example.com/a?b=c#d", "http://example.com/a?b=c#d")]
    [InlineData("HTTPS://Example.com/", "https://example.com/")]
    [InlineData("mailto:ap@oire.me", "mailto:ap@oire.me")]
    public void Resolve_WebAndMailLinks_AreExternal(string href, string expected) {
        Resolve(href).Should().Be(new LinkTarget(LinkKind.External, expected));
    }

    [Theory]
    [InlineData("#task-6-show-the-document", "task-6-show-the-document")]
    [InlineData("#", "")]
    public void Resolve_Anchor_IsInPage(string href, string expected) {
        Resolve(href).Should().Be(new LinkTarget(LinkKind.InPage, expected));
    }

    [Theory]
    [InlineData("other.md", @"C:\plans\other.md")]
    [InlineData("./other.md", @"C:\plans\other.md")]
    [InlineData("other.md#some-section", @"C:\plans\other.md")]
    [InlineData("sub/nested.markdown", @"C:\plans\sub\nested.markdown")]
    [InlineData(@"sub\nested.markdown", @"C:\plans\sub\nested.markdown")]
    [InlineData("../notes.md", @"C:\notes.md")]
    [InlineData("my%20plan.md", @"C:\plans\my plan.md")]
    public void Resolve_RelativeMarkdownFile_OpensInPlanCake(string href, string expected) {
        Resolve(href).Should().Be(new LinkTarget(LinkKind.Markdown, expected));
    }

    [Theory]
    [InlineData(@"D:\elsewhere\UPPER.MD", @"D:\elsewhere\UPPER.MD")]
    [InlineData("file:///D:/elsewhere/UPPER.MD", @"D:\elsewhere\UPPER.MD")]
    public void Resolve_AbsoluteMarkdownPath_OpensInPlanCake(string href, string expected) {
        Resolve(href).Should().Be(new LinkTarget(LinkKind.Markdown, expected));
    }

    [Theory]
    [InlineData("diagram.png", @"C:\plans\diagram.png")]
    [InlineData("assets", @"C:\plans\assets")]
    public void Resolve_RelativeOtherTarget_OpensWithTheSystem(string href, string expected) {
        Resolve(href).Should().Be(new LinkTarget(LinkKind.OtherFile, expected));
    }

    [Theory]
    [InlineData("setup.exe", @"C:\plans\setup.exe")]
    [InlineData("run.bat", @"C:\plans\run.bat")]
    [InlineData("RUN.CMD", @"C:\plans\RUN.CMD")]
    [InlineData("tool.hta", @"C:\plans\tool.hta")]
    [InlineData("script.js", @"C:\plans\script.js")]
    [InlineData("script.vbs", @"C:\plans\script.vbs")]
    [InlineData("script.ps1", @"C:\plans\script.ps1")]
    [InlineData("shortcut.lnk", @"C:\plans\shortcut.lnk")]
    [InlineData("site.url", @"C:\plans\site.url")]
    [InlineData("install.msi", @"C:\plans\install.msi")]
    [InlineData("file:///C:/plans/setup.exe", @"C:\plans\setup.exe")]
    public void Resolve_ProgramOrScript_IsNeverOpenedByTheSystem(string href, string expected) {
        var programs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { expected };

        LinkResolver.Resolve(href, Folder, programs.Contains).Should().Be(new LinkTarget(LinkKind.Program, expected));
    }

    [Theory]
    [InlineData("setup.exe", true)]
    [InlineData("Setup.EXE", true)]
    [InlineData("run.bat", true)]
    [InlineData("payload.hta", true)]
    [InlineData("x.wsf", true)]
    [InlineData("x.scr", true)]
    [InlineData("diagram.png", false)]
    [InlineData("notes.txt", false)]
    [InlineData("report.pdf", false)]
    [InlineData("plan.md", false)]
    [InlineData("README", false)]
    [InlineData(null, false)]
    public void IsRunnable_ChecksTheExtension(string? path, bool expected) {
        LinkResolver.IsRunnable(path).Should().Be(expected);
    }

    [Theory]
    [InlineData(@"\\host\share\x.exe")]
    [InlineData(@"\\host\share\plan.md")]
    [InlineData("//host/share/plan.md")]
    [InlineData("file://host/share/plan.md")]
    [InlineData(@"\\?\UNC\host\share\plan.md")]
    [InlineData(@"\\.\pipe\plancake")]
    public void Resolve_AnotherComputersShare_IsUnsupportedWithoutTouchingIt(string href) {
        var touched = new List<string>();

        var target = LinkResolver.Resolve(href, Folder, path => {
            touched.Add(path);
            return true;
        });

        target.Kind.Should().Be(LinkKind.Unsupported);
        touched.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_ShareOfTheDocumentItself_IsFollowed() {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            @"\\server\plans\next.md",
            @"\\server\other\diagram.png",
        };

        LinkResolver.Resolve("next.md", @"\\server\plans", existing.Contains)
            .Should().Be(new LinkTarget(LinkKind.Markdown, @"\\server\plans\next.md"));
        LinkResolver.Resolve(@"\\server\other\diagram.png", @"\\SERVER\plans", existing.Contains)
            .Should().Be(new LinkTarget(LinkKind.OtherFile, @"\\server\other\diagram.png"));
        LinkResolver.Resolve(@"\\elsewhere\plans\next.md", @"\\server\plans", existing.Contains)
            .Kind.Should().Be(LinkKind.Unsupported);
    }

    [Theory]
    [InlineData("missing.md", @"C:\plans\missing.md")]
    [InlineData("sub/missing.png", @"C:\plans\sub\missing.png")]
    [InlineData(@"C:\nowhere\plan.md", @"C:\nowhere\plan.md")]
    public void Resolve_MissingTarget_IsReportedWithItsPath(string href, string expected) {
        Resolve(href).Should().Be(new LinkTarget(LinkKind.Missing, expected));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.com/plan.md")]
    [InlineData("data:text/html,hi")]
    [InlineData("?only-a-query")]
    public void Resolve_OtherSchemesAndJunk_AreUnsupported(string href) {
        Resolve(href).Kind.Should().Be(LinkKind.Unsupported);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_EmptyHref_IsUnsupported(string? href) {
        Resolve(href).Kind.Should().Be(LinkKind.Unsupported);
    }

    [Fact]
    public void Resolve_RelativeTargetWithoutADocument_IsUnsupported() {
        Resolve("other.md", folder: null).Kind.Should().Be(LinkKind.Unsupported);
    }

    [Fact]
    public void Resolve_RealDisk_FindsAnExistingFile() {
        var folder = Directory.CreateTempSubdirectory("PlanCakeLinkTests").FullName;

        try {
            File.WriteAllText(Path.Combine(folder, "real.md"), "# Real\n");

            LinkResolver.Resolve("real.md", folder).Should()
                .Be(new LinkTarget(LinkKind.Markdown, Path.Combine(folder, "real.md")));
            LinkResolver.Resolve("absent.md", folder).Kind.Should().Be(LinkKind.Missing);
        } finally {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("plan.md", true)]
    [InlineData("PLAN.MD", true)]
    [InlineData("plan.markdown", true)]
    [InlineData("plan.md.txt", false)]
    [InlineData("plan", false)]
    [InlineData(null, false)]
    public void IsMarkdownPath_ChecksTheExtension(string? path, bool expected) {
        LinkResolver.IsMarkdownPath(path).Should().Be(expected);
    }
}
