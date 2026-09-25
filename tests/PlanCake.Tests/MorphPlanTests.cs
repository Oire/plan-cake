using System.Text.Json;
using AwesomeAssertions;
using Jint;
using Xunit;

namespace Oire.PlanCake.Tests;

/// <summary>
/// The decision part of <c>web/morph.js</c>, which keeps the document's unchanged nodes when the
/// same file is rendered again, run in Jint: which old node each new node becomes (-1: a new
/// node is inserted). The DOM part needs a browser and is checked with JAWS.
/// </summary>
public class MorphPlanTests {
    private static readonly Lazy<string> _script = new(() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "web", "morph.js")));

    /// <summary>An item: its content key, and its kind (the tag name) after a colon, "P" by default.</summary>
    private static int[] Plan(string[] oldItems, string[] newItems) {
        var engine = new Engine();
        engine.Execute(_script.Value);
        var json = engine.Evaluate(
            $"JSON.stringify(PlanCakeMorph.planMatches({Items(oldItems)}, {Items(newItems)}))"
        ).AsString();

        return JsonSerializer.Deserialize<int[]>(json)!;
    }

    private static string Items(string[] items) => JsonSerializer.Serialize(items.Select(item => {
        var parts = item.Split(':');
        return new { key = parts[0], kind = parts.Length > 1 ? parts[1] : "P" };
    }));

    [Fact]
    public void SameContent_KeepsEveryNode() =>
        Plan(["a", "b", "c"], ["a", "b", "c"]).Should().Equal(0, 1, 2);

    [Fact]
    public void Appended_KeepsTheOldNodesAndInsertsTheNewOne() =>
        Plan(["a", "b"], ["a", "b", "c"]).Should().Equal(0, 1, -1);

    [Fact]
    public void InsertedInTheMiddle_KeepsTheNodesAround() =>
        Plan(["a", "b", "c"], ["a", "x", "b", "c"]).Should().Equal(0, -1, 1, 2);

    [Fact]
    public void InsertedWithItsWhitespace_KeepsEveryOldNode() =>
        Plan(["p1", "ws:#3", "p2"], ["p1", "ws:#3", "new", "ws:#3", "p2"]).Should().Equal(0, 1, -1, -1, 2);

    [Fact]
    public void Deleted_KeepsTheOthers() =>
        Plan(["a", "b", "c"], ["a", "c"]).Should().Equal(0, 2);

    [Fact]
    public void ChangedBlock_OfTheSameKind_IsPatchedInPlace() =>
        Plan(["a", "b", "c"], ["a", "b2", "c"]).Should().Equal(0, 1, 2);

    [Fact]
    public void ChangedBlock_OfAnotherKind_IsReplaced() =>
        Plan(["a", "b", "c"], ["a", "b2:UL", "c"]).Should().Equal(0, -1, 2);

    [Fact]
    public void TwoChangesApart_KeepEverythingBetween() => Plan(
        ["h:H1", "ws:#3", "p1", "ws:#3", "p2", "ws:#3", "p3"],
        ["h:H1", "ws:#3", "p1b", "ws:#3", "p2", "ws:#3", "p3b"]
    ).Should().Equal(0, 1, 2, 3, 4, 5, 6);

    [Fact]
    public void NoteAddedAfterABlock_IsInsertedAndTheRestKept() => Plan(
        ["p1", "ws:#3", "p2", "ws:#3", "p3"],
        ["p1", "ws:#3", "note:DIV#note", "ws:#3", "p2", "ws:#3", "p3"]
    ).Should().Equal(0, 1, -1, -1, 2, 3, 4);

    [Fact]
    public void KeptNodes_NeverChangeOrder() {
        var plan = Plan(["a", "b", "c", "d"], ["d", "c", "b", "a"]);
        var kept = plan.Where(index => index >= 0).ToList();

        kept.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        kept.Should().HaveCount(1);
    }

    [Fact]
    public void EmptyOld_InsertsEverything() =>
        Plan([], ["a", "b"]).Should().Equal(-1, -1);

    [Fact]
    public void EmptyNew_KeepsNothing() =>
        Plan(["a", "b"], []).Should().BeEmpty();

    [Fact]
    public void LargeChangedStretch_IsPatchedPositionByPosition() {
        var oldItems = Enumerable.Range(0, 600).Select(i => $"old{i}").ToArray();
        var newItems = Enumerable.Range(0, 600).Select(i => $"new{i}").ToArray();

        Plan(oldItems, newItems).Should().Equal(Enumerable.Range(0, 600));
    }
}
