using AwesomeAssertions;
using Oire.PlanCake.Ui;
using Xunit;

namespace Oire.PlanCake.Tests;

/// <summary>
/// The lockdown of the document view: only the app's own pages may talk to the host, and the
/// only navigation is the page the host asked for, once.
/// </summary>
public class DocumentViewTests {
    private const string Page = "https://app.plancake/index.html";

    // NavigationGate

    [Fact]
    public void NavigationGate_LetsTheAllowedPageThroughOnce() {
        var gate = new NavigationGate();
        gate.Allow(Page);

        gate.TryPass(Page).Should().BeTrue();
        gate.TryPass(Page).Should().BeFalse();
    }

    [Fact]
    public void NavigationGate_NothingAllowed_RefusesEverything() {
        var gate = new NavigationGate();

        gate.TryPass(Page).Should().BeFalse();
        gate.TryPass(null).Should().BeFalse();
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("https://app.plancake/other.html")]
    [InlineData("https://app.plancake/index.html#top")]
    [InlineData("https://app.plancake/index.html?x=1")]
    [InlineData("HTTPS://APP.PLANCAKE/INDEX.HTML")]
    [InlineData("file:///C:/Windows/notepad.exe")]
    [InlineData("about:blank")]
    [InlineData("")]
    public void NavigationGate_RefusesAnyOtherNavigation_AndKeepsTheAllowance(string uri) {
        var gate = new NavigationGate();
        gate.Allow(Page);

        gate.TryPass(uri).Should().BeFalse();
        gate.TryPass(Page).Should().BeTrue();
    }

    [Fact]
    public void NavigationGate_ANewAllowance_ReplacesTheOldOne() {
        var gate = new NavigationGate();
        gate.Allow(Page);
        gate.Allow("https://app.plancake/second.html");

        gate.TryPass(Page).Should().BeFalse();
        gate.TryPass("https://app.plancake/second.html").Should().BeTrue();
    }

    // Message source and shape

    [Theory]
    [InlineData("https://app.plancake/index.html")]
    [InlineData("https://app.plancake/")]
    [InlineData("HTTPS://App.PlanCake/index.html")]
    public void IsTrustedSource_TheAppsOwnPages_AreTrusted(string source) =>
        DocumentView.IsTrustedSource(source).Should().BeTrue();

    [Theory]
    [InlineData("https://app.plancake.example.com/index.html")]
    [InlineData("https://example.com/https://app.plancake/")]
    [InlineData("http://app.plancake/index.html")]
    [InlineData("https://app.plancake")]
    [InlineData("about:blank")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData(null)]
    public void IsTrustedSource_AnythingElse_IsNotTrusted(string? source) =>
        DocumentView.IsTrustedSource(source).Should().BeFalse();

    [Fact]
    public void ParsePageMessage_AnObjectWithAType_GivesTheTypeAndTheWholeMessage() {
        var parsed = DocumentView.ParsePageMessage(Page, """{ "type": "activate", "lines": "3-4" }""");

        parsed.Should().NotBeNull();
        parsed!.Value.Type.Should().Be("activate");
        parsed.Value.Message.GetProperty("lines").GetString().Should().Be("3-4");
    }

    [Theory]
    [InlineData("https://example.com/", """{ "type": "activate" }""")]
    [InlineData(Page, "not json")]
    [InlineData(Page, "")]
    [InlineData(Page, null)]
    [InlineData(Page, """["activate"]""")]
    [InlineData(Page, "\"activate\"")]
    [InlineData(Page, """{ "lines": "3-4" }""")]
    [InlineData(Page, """{ "type": 3 }""")]
    [InlineData(Page, """{ "type": null }""")]
    public void ParsePageMessage_AnythingElse_IsDropped(string source, string? json) =>
        DocumentView.ParsePageMessage(source, json).Should().BeNull();
}
