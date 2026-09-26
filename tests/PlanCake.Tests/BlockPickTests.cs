using System.Globalization;
using AwesomeAssertions;
using Jint;
using Xunit;

namespace Oire.PlanCake.Tests;

/// <summary>
/// The decision part of <c>web/blocks.js</c>, run in Jint: which block or note Alt+Down Arrow and
/// Alt+Up Arrow move to (-1: none in that direction, so the host announces "No more blocks").
/// Finding the blocks and focusing them needs a browser and is checked by hand.
/// </summary>
public class BlockPickTests {
    private static readonly Lazy<string> _script = new(() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "web", "blocks.js")));

    private const int ViewportHeight = 600;

    /// <summary>
    /// An item: its top and bottom in the view, or "hidden" for one that is not rendered.
    /// </summary>
    private static int Pick(string[] items, int current, bool forward) {
        var engine = new Engine();
        engine.Execute(_script.Value);

        var array = String.Join(", ", items.Select(item => {
            if (item == "hidden") {
                return "{ top: 0, bottom: 0, shown: false }";
            }

            var parts = item.Split(' ');
            return $"{{ top: {parts[0]}, bottom: {parts[1]}, shown: true }}";
        }));

        var result = engine.Evaluate(
            $"PlanCakeBlocks.pick([{array}], {current}, {(forward ? "true" : "false")}, {ViewportHeight})"
        ).AsNumber();

        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    // Four blocks, the view scrolled so that the second one is at its top.
    private static readonly string[] _document = ["-300 -100", "0 200", "250 400", "900 1000"];

    [Fact]
    public void Next_FromTheCurrentBlock_IsTheOneAfterIt() =>
        Pick(_document, 1, forward: true).Should().Be(2);

    [Fact]
    public void Previous_FromTheCurrentBlock_IsTheOneBeforeIt() =>
        Pick(_document, 2, forward: false).Should().Be(1);

    [Fact]
    public void Next_FromTheCurrentBlock_IgnoresTheView() =>
        Pick(_document, 0, forward: true).Should().Be(1);

    [Fact]
    public void Next_PastTheLastBlock_FindsNone() =>
        Pick(_document, 3, forward: true).Should().Be(-1);

    [Fact]
    public void Previous_BeforeTheFirstBlock_FindsNone() =>
        Pick(_document, 0, forward: false).Should().Be(-1);

    [Fact]
    public void HiddenBlocks_AreSkipped() {
        string[] items = ["0 100", "hidden", "hidden", "200 300"];

        Pick(items, 0, forward: true).Should().Be(3);
        Pick(items, 3, forward: false).Should().Be(0);
    }

    [Fact]
    public void OnlyHiddenBlocksAhead_FindsNone() =>
        Pick(["0 100", "hidden"], 0, forward: true).Should().Be(-1);

    [Fact]
    public void Next_WithNoPosition_IsTheFirstBlockInView() =>
        Pick(_document, -1, forward: true).Should().Be(1);

    [Fact]
    public void Previous_WithNoPosition_IsTheLastBlockInView() =>
        Pick(_document, -1, forward: false).Should().Be(2);

    [Fact]
    public void Next_WithNoPosition_TakesABlockReachingIntoTheViewFromAbove() =>
        Pick(["-500 50", "100 200"], -1, forward: true).Should().Be(0);

    [Fact]
    public void Next_WithNoPosition_AndNothingInView_IsTheFirstBlockBelow() =>
        Pick(["-300 -100", "700 800"], -1, forward: true).Should().Be(1);

    [Fact]
    public void Previous_WithNoPosition_AndNothingInView_IsTheLastBlockAbove() =>
        Pick(["-300 -100", "700 800"], -1, forward: false).Should().Be(0);

    [Fact]
    public void WithNoPosition_HiddenBlocksAreSkipped() {
        string[] items = ["hidden", "10 20", "hidden"];

        Pick(items, -1, forward: true).Should().Be(1);
        Pick(items, -1, forward: false).Should().Be(1);
    }

    [Fact]
    public void AnEmptyDocument_HasNoBlockToMoveTo() {
        Pick([], -1, forward: true).Should().Be(-1);
        Pick([], -1, forward: false).Should().Be(-1);
    }
}
