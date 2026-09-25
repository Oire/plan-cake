using AwesomeAssertions;
using Oire.PlanCake.Rendering;
using Xunit;

namespace Oire.PlanCake.Tests;

public class PositionRestorerTests {
    private static BlockInfo Block(BlockKind kind, int start, int end, string text) =>
        new(kind, start, end, text, MarkdownRenderer.Excerpt(text));

    [Fact]
    public void FindTarget_SameTextAfterLinesShifted_FindsTheBlockByItsText() {
        var previous = Block(BlockKind.Paragraph, 10, 11, "Second paragraph.");
        var blocks = new[] {
            Block(BlockKind.Heading, 1, 1, "Title"),
            Block(BlockKind.Paragraph, 3, 3, "First paragraph."),
            Block(BlockKind.Paragraph, 5, 5, "A paragraph added above."),
            Block(BlockKind.Paragraph, 14, 15, "Second paragraph."),
        };

        PositionRestorer.FindTarget(previous, blocks).Should().BeSameAs(blocks[3]);
    }

    [Fact]
    public void FindTarget_SameTextSeveralTimes_TakesTheNearest() {
        var previous = Block(BlockKind.ListItem, 20, 20, "Done");
        var blocks = new[] {
            Block(BlockKind.ListItem, 4, 4, "Done"),
            Block(BlockKind.ListItem, 22, 22, "Done"),
            Block(BlockKind.ListItem, 40, 40, "Done"),
        };

        PositionRestorer.FindTarget(previous, blocks).Should().BeSameAs(blocks[1]);
    }

    [Fact]
    public void FindTarget_SameTextOfAnotherKind_IsNotAMatch() {
        var previous = Block(BlockKind.Paragraph, 10, 10, "Setup");
        var blocks = new[] {
            Block(BlockKind.Heading, 2, 2, "Setup"),
            Block(BlockKind.Paragraph, 11, 11, "Setup, rewritten."),
        };

        PositionRestorer.FindTarget(previous, blocks).Should().BeSameAs(blocks[1]);
    }

    [Fact]
    public void FindTarget_TextChanged_TakesTheBlockWithTheNearestStartLine() {
        var previous = Block(BlockKind.Paragraph, 12, 13, "The old wording.");
        var blocks = new[] {
            Block(BlockKind.Paragraph, 1, 1, "Intro."),
            Block(BlockKind.Paragraph, 9, 10, "Something before."),
            Block(BlockKind.Paragraph, 12, 14, "The new wording, a little longer."),
            Block(BlockKind.Paragraph, 16, 16, "Something after."),
        };

        PositionRestorer.FindTarget(previous, blocks).Should().BeSameAs(blocks[2]);
    }

    [Fact]
    public void FindTarget_TieOnDistance_TakesTheEarlierBlock() {
        var previous = Block(BlockKind.Paragraph, 10, 10, "Gone.");
        var blocks = new[] {
            Block(BlockKind.Paragraph, 8, 8, "Before."),
            Block(BlockKind.Paragraph, 12, 12, "After."),
        };

        PositionRestorer.FindTarget(previous, blocks).Should().BeSameAs(blocks[0]);
    }

    [Fact]
    public void FindTarget_BlockDeletedAtTheEndOfTheFile_TakesTheNewLastBlock() {
        var previous = Block(BlockKind.Paragraph, 30, 30, "The last paragraph, now deleted.");
        var blocks = new[] {
            Block(BlockKind.Heading, 1, 1, "Title"),
            Block(BlockKind.Paragraph, 3, 5, "Body."),
            Block(BlockKind.Code, 7, 10, "code"),
        };

        PositionRestorer.FindTarget(previous, blocks).Should().BeSameAs(blocks[2]);
    }

    [Fact]
    public void FindTarget_EmptyDocument_ReturnsNull() {
        var previous = Block(BlockKind.Paragraph, 3, 3, "Text.");

        PositionRestorer.FindTarget(previous, []).Should().BeNull();
    }

    [Fact]
    public void FindTarget_NoPreviousPosition_ReturnsNull() {
        var blocks = new[] { Block(BlockKind.Paragraph, 1, 1, "Text.") };

        PositionRestorer.FindTarget(null, blocks).Should().BeNull();
    }

    [Fact]
    public void FindTarget_AfterARealReRender_ReturnsToTheSameParagraph() {
        var strings = new RenderStrings("Note:", "user note", "unote");
        var options = new RenderOptions(
            Notes.NoteMarkers.Default, RenderMode.Interactive, Utils.Enums.NoteStyle.Note, strings
        );
        var before = MarkdownRenderer.Render("# Plan\n\nFirst.\n\nSecond.\n", options);
        var after = MarkdownRenderer.Render("# Plan\n\nNew.\n\nFirst.\n\nSecond.\n", options);
        var previous = before.Blocks.Single(block => block.Text == "Second.");

        var target = PositionRestorer.FindTarget(previous, after.Blocks);

        target.Should().NotBeNull();
        target!.Lines.Should().Be("7-7");
    }
}
