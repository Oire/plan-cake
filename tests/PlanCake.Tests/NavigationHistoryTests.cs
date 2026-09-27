using AwesomeAssertions;
using Oire.PlanCake.Rendering;
using Oire.PlanCake.Utils;
using Xunit;

namespace Oire.PlanCake.Tests;

public class NavigationHistoryTests {
    private readonly HashSet<string> _existing = new(StringComparer.OrdinalIgnoreCase) {
        @"C:\plans\a.md", @"C:\plans\b.md", @"C:\plans\c.md", @"C:\plans\d.md",
    };

    private readonly List<HistoryEntry> _opened = [];

    private static HistoryEntry Entry(string name, BlockInfo? position = null) =>
        new($@"C:\plans\{name}.md", position);

    private bool Open(HistoryEntry entry) {
        _opened.Add(entry);
        return true;
    }

    private HistoryMove Back(NavigationHistory history, HistoryEntry? current) =>
        history.GoBack(current, _existing.Contains, Open);

    private HistoryMove Forward(NavigationHistory history, HistoryEntry? current) =>
        history.GoForward(current, _existing.Contains, Open);

    /// <summary>a → b → c, as when following two links: the window now shows c.</summary>
    private static NavigationHistory VisitedAThenBThenC() {
        var history = new NavigationHistory();
        history.Push(Entry("a"));
        history.Push(Entry("b"));

        return history;
    }

    [Fact]
    public void GoBack_ReturnsThroughTheFilesInReverseOrder() {
        var history = VisitedAThenBThenC();

        Back(history, Entry("c")).Outcome.Should().Be(HistoryOutcome.Moved);
        Back(history, Entry("b")).Outcome.Should().Be(HistoryOutcome.Moved);

        _opened.Select(entry => entry.Path).Should().Equal(@"C:\plans\b.md", @"C:\plans\a.md");
        history.CanGoBack.Should().BeFalse();
    }

    [Fact]
    public void GoForward_RetracesTheFilesBackLeft() {
        var history = VisitedAThenBThenC();
        Back(history, Entry("c"));
        Back(history, Entry("b"));
        _opened.Clear();

        Forward(history, Entry("a")).Outcome.Should().Be(HistoryOutcome.Moved);
        Forward(history, Entry("b")).Outcome.Should().Be(HistoryOutcome.Moved);

        _opened.Select(entry => entry.Path).Should().Equal(@"C:\plans\b.md", @"C:\plans\c.md");
        history.CanGoForward.Should().BeFalse();
        history.CanGoBack.Should().BeTrue();
    }

    [Fact]
    public void Push_AfterGoingBack_ClearsTheForwardList() {
        var history = VisitedAThenBThenC();
        Back(history, Entry("c"));
        history.CanGoForward.Should().BeTrue();

        history.Push(Entry("b"));

        history.CanGoForward.Should().BeFalse();
        Forward(history, Entry("d")).Outcome.Should().Be(HistoryOutcome.AtEnd);
    }

    [Fact]
    public void GoBackAndForward_AtTheEnds_OpenNothing() {
        var history = new NavigationHistory();

        var back = Back(history, Entry("a"));
        back.Outcome.Should().Be(HistoryOutcome.AtEnd);
        back.Missing.Should().BeEmpty();
        Forward(history, Entry("a")).Outcome.Should().Be(HistoryOutcome.AtEnd);

        _opened.Should().BeEmpty();
        history.CanGoBack.Should().BeFalse();
        history.CanGoForward.Should().BeFalse();
    }

    [Fact]
    public void GoBack_ReturnsTheStoredPosition_AndStoresTheCurrentOneForForward() {
        var inA = new BlockInfo(BlockKind.Paragraph, 40, 42, "Where I was in a.", "Where I was in a.");
        var inB = new BlockInfo(BlockKind.Heading, 7, 7, "Where I was in b", "Where I was in b");
        var history = new NavigationHistory();
        history.Push(Entry("a", inA));

        Back(history, Entry("b", inB));
        Forward(history, Entry("a", inA));

        _opened[0].Position.Should().BeSameAs(inA);
        _opened[1].Should().Be(Entry("b", inB));
    }

    [Fact]
    public void GoBack_SkipsAndDropsAFileThatNoLongerExists() {
        var history = VisitedAThenBThenC();
        _existing.Remove(@"C:\plans\b.md");

        var move = Back(history, Entry("c"));

        move.Outcome.Should().Be(HistoryOutcome.Moved);
        move.Missing.Should().Equal(@"C:\plans\b.md");
        _opened.Single().Path.Should().Be(@"C:\plans\a.md");

        // b is gone for good: forward from a goes straight back to c.
        _existing.Add(@"C:\plans\b.md");
        _opened.Clear();
        Forward(history, Entry("a"));
        _opened.Single().Path.Should().Be(@"C:\plans\c.md");
    }

    [Fact]
    public void GoBack_EveryPreviousFileMissing_ReportsThemAndTheEnd() {
        var history = VisitedAThenBThenC();
        _existing.Clear();

        var move = Back(history, Entry("c"));

        move.Outcome.Should().Be(HistoryOutcome.AtEnd);
        move.Missing.Should().Equal(@"C:\plans\b.md", @"C:\plans\a.md");
        history.CanGoBack.Should().BeFalse();
        history.CanGoForward.Should().BeFalse();
    }

    [Fact]
    public void GoBack_OpenFails_LeavesTheHistoryUnchanged() {
        var history = VisitedAThenBThenC();

        history.GoBack(Entry("c"), _existing.Contains, _ => false).Outcome.Should().Be(HistoryOutcome.OpenFailed);

        history.CanGoForward.Should().BeFalse();
        Back(history, Entry("c"));
        _opened.Single().Path.Should().Be(@"C:\plans\b.md");
    }

    [Fact]
    public void GoBack_WithoutACurrentFile_AddsNothingToForward() {
        var history = new NavigationHistory();
        history.Push(Entry("a"));

        Back(history, current: null).Outcome.Should().Be(HistoryOutcome.Moved);

        history.CanGoForward.Should().BeFalse();
    }
}
