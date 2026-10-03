using AwesomeAssertions;
using Oire.PlanCake.Ui;
using Oire.PlanCake.Utils;
using Xunit;

namespace Oire.PlanCake.Tests;

public class UrlHelperTests {
    [Theory]
    [InlineData("https://github.com/Oire/plan-cake/blob/master/README.md", "https://github.com/Oire/plan-cake/blob/master/README.md")]
    [InlineData("http://example.com/plan.md", "http://example.com/plan.md")]
    [InlineData("  https://example.com/plan.md\r\n", "https://example.com/plan.md")]
    [InlineData("HTTPS://EXAMPLE.COM/plan.md", "HTTPS://EXAMPLE.COM/plan.md")]
    public void IsValidHttpUrl_AcceptsHttpAndHttps_Trimmed(string text, string expected) {
        UrlHelper.IsValidHttpUrl(text, out var url).Should().BeTrue();
        url.Should().Be(expected);
    }

    [Theory]
    [InlineData("ftp://example.com/plan.md")]
    [InlineData("file:///C:/plans/plan.md")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:ap@oire.me")]
    [InlineData(@"C:\plans\plan.md")]
    [InlineData("example.com/plan.md")]
    [InlineData("not a link at all")]
    [InlineData("https://")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsValidHttpUrl_RejectsOtherSchemesAndJunk(string? text) {
        UrlHelper.IsValidHttpUrl(text, out var url).Should().BeFalse();
        url.Should().Be(text?.Trim() ?? "");
    }

    [Theory]
    [InlineData("https://example.com/plan.md", true)]
    [InlineData("  http://example.com/plan.md  ", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("example.com/plan.md", false)]
    [InlineData("ftp://example.com/plan.md", false)]
    public void OpenLinkDialog_AcceptsOnlyHttpLinks(string text, bool accepted) =>
        (OpenLinkDialog.Validate(text) is null).Should().Be(accepted);

    // ForLog: what a log may hold of a link

    [Theory]
    [InlineData(
        "https://user:secret@raw.githubusercontent.com/o/r/main/plan.md?token=SECRET&ref=main#SECRET",
        "https://raw.githubusercontent.com/o/r/main/plan.md?token&ref"
    )]
    [InlineData("https://example.com/plan.md", "https://example.com/plan.md")]
    [InlineData("http://example.com:8080/a/b.md?x=1", "http://example.com:8080/a/b.md?x")]
    [InlineData("other.md?token=SECRET#part", "other.md?token#part")]
    [InlineData("#section", "#section")]
    [InlineData(@"C:\plans\plan.md", @"C:\plans\plan.md")]
    [InlineData("mailto:ap@oire.me?subject=SECRET", "mailto:ap@oire.me?subject")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void ForLog_DropsCredentialsQueryValuesAndFragments(string? text, string expected) {
        var logged = UrlHelper.ForLog(text);

        logged.Should().Be(expected);
        logged.Should().NotContain("SECRET").And.NotContain("?token=");
    }

    [Fact]
    public void ForLog_OfAUri_NeverHoldsTheToken() {
        var uri = new Uri("https://someone:secret@raw.githubusercontent.com/o/r/main/plan.md?token=GHSAT0AAAA");

        var logged = UrlHelper.ForLog(uri);

        logged.Should().Be("https://raw.githubusercontent.com/o/r/main/plan.md?token");
        logged.Should().NotContain("?token=").And.NotContain("GHSAT").And.NotContain("secret").And.NotContain("someone");
    }
}
