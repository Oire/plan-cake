using System.Text.RegularExpressions;
using AwesomeAssertions;
using Oire.PlanCake.Ui;
using Oire.PlanCake.Utils.Constants;
using Xunit;

namespace Oire.PlanCake.Tests;

/// <summary>
/// The content security policy of the page the document view shows (<c>web/index.html</c>, as
/// copied to the output). The page puts a plan's raw HTML into the document as it is, so the
/// policy is all that keeps an <c>&lt;img onerror&gt;</c> or a <c>&lt;script&gt;</c> in a downloaded or
/// AI-written plan from running script, which could post messages to the host.
/// </summary>
public partial class PageSecurityPolicyTests {
    [GeneratedRegex("""<meta\s+http-equiv="Content-Security-Policy"\s+content="([^"]*)"\s*>""", RegexOptions.IgnoreCase)]
    private static partial Regex PolicyMeta();

    /// <summary>The page's policy, directive by directive.</summary>
    private static Dictionary<string, string[]> Policy() {
        var html = File.ReadAllText(Path.Combine(App.WebFolder, "index.html"));
        var matches = PolicyMeta().Matches(html);
        matches.Should().ContainSingle("the page must declare exactly one content security policy");

        return matches[0].Groups[1].Value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directive => directive.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToDictionary(parts => parts[0], parts => parts[1..], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Policy_DefaultsToNothing() =>
        Policy()["default-src"].Should().Equal("'none'");

    [Fact]
    public void Policy_RunsOnlyTheAppsOwnScriptFiles() =>
        Policy()["script-src"].Should().Equal(DocumentView.BaseUri.GetLeftPart(UriPartial.Authority));

    [Fact]
    public void Policy_AllowsNoInlineScriptNoEvalAndNoWildcardAnywhere() {
        foreach (var (directive, sources) in Policy()) {
            if (directive.Equals("style-src", StringComparison.OrdinalIgnoreCase)) {
                // Inline style cannot run script.
                sources.Should().NotContain(["'unsafe-eval'", "*", "https:", "http:", "data:"]);
                continue;
            }

            sources.Should().NotContain(["'unsafe-inline'", "'unsafe-eval'", "'unsafe-hashes'", "*", "https:", "http:"], directive);
        }
    }

    [Fact]
    public void Policy_KeepsFormsAndTheBaseUrlLockedDown() {
        var policy = Policy();

        policy["base-uri"].Should().Equal("'none'");
        policy["form-action"].Should().Equal("'none'");
    }
}
