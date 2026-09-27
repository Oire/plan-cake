using AwesomeAssertions;
using Oire.PlanCake.Notes;
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
        var strings = new RenderStrings("user note", "unote");
        var options = new RenderOptions(
            Notes.NoteMarkers.Default, RenderMode.Interactive, strings
        );
        var before = MarkdownRenderer.Render("# Plan\n\nFirst.\n\nSecond.\n", options);
        var after = MarkdownRenderer.Render("# Plan\n\nNew.\n\nFirst.\n\nSecond.\n", options);
        var previous = before.Blocks.Single(block => block.Text == "Second.");

        var target = PositionRestorer.FindTarget(previous, after.Blocks);

        target.Should().NotBeNull();
        target!.Lines.Should().Be("7-7");
    }

    // Opening a document

    [Fact]
    public void FindOpeningTarget_NoSavedPosition_StartsAtTheFirstBlock() {
        // A file opened fresh (a followed .md link, the command line, a drop) has no saved
        // position: the view starts at the top, never where the previous file was left.
        var blocks = new[] {
            Block(BlockKind.Heading, 1, 1, "Other plan"),
            Block(BlockKind.Paragraph, 3, 3, "Short."),
        };

        PositionRestorer.FindOpeningTarget(null, blocks).Should().BeSameAs(blocks[0]);
    }

    [Fact]
    public void FindOpeningTarget_SavedPositionFromTheHistory_ReturnsToIt() {
        var saved = Block(BlockKind.Paragraph, 3, 3, "Short.");
        var blocks = new[] {
            Block(BlockKind.Heading, 1, 1, "Other plan"),
            Block(BlockKind.Paragraph, 3, 3, "Short."),
        };

        PositionRestorer.FindOpeningTarget(saved, blocks).Should().BeSameAs(blocks[1]);
    }

    [Fact]
    public void FindOpeningTarget_EmptyDocument_ReturnsNull() =>
        PositionRestorer.FindOpeningTarget(null, []).Should().BeNull();

    // Notes list selection

    private static RenderedNote NoteOn(int index, int line, string text, BlockInfo? block) =>
        new(index, new Note(text, line, line, 0, 0, Unterminated: false), block);

    [Fact]
    public void FindNote_SameNoteAfterLinesShifted_FindsIt() {
        var block = Block(BlockKind.Paragraph, 5, 5, "Second.");
        var previous = NoteOn(1, 6, "Check this.", block);
        var shifted = Block(BlockKind.Paragraph, 9, 9, "Second.");
        var notes = new[] {
            NoteOn(0, 3, "A new note above.", Block(BlockKind.Heading, 1, 1, "Plan")),
            NoteOn(1, 5, "Another new one.", Block(BlockKind.Paragraph, 4, 4, "First.")),
            NoteOn(2, 10, "Check this.", shifted),
        };

        PositionRestorer.FindNote(previous, notes).Should().BeSameAs(notes[2]);
    }

    [Fact]
    public void FindNote_SameTextOnAnotherBlock_IsNotTheSameNote() {
        var previous = NoteOn(0, 20, "Why?", Block(BlockKind.Paragraph, 19, 19, "Second."));
        var notes = new[] {
            NoteOn(0, 4, "Why?", Block(BlockKind.Paragraph, 3, 3, "First.")),
            NoteOn(1, 21, "Why not?", Block(BlockKind.Paragraph, 19, 19, "Second.")),
        };

        PositionRestorer.FindNote(previous, notes).Should().BeSameAs(notes[1]);
    }

    [Fact]
    public void FindNote_SameNoteSeveralTimes_TakesTheNearest() {
        var block = Block(BlockKind.ListItem, 10, 10, "Done");
        var previous = NoteOn(1, 21, "Same", block);
        var notes = new[] {
            NoteOn(0, 11, "Same", block),
            NoteOn(1, 23, "Same", block),
            NoteOn(2, 41, "Same", block),
        };

        PositionRestorer.FindNote(previous, notes).Should().BeSameAs(notes[1]);
    }

    [Fact]
    public void FindNote_NoteDeleted_TakesTheNoteWithTheNearestStartLine() {
        var previous = NoteOn(1, 12, "Deleted", Block(BlockKind.Paragraph, 11, 11, "Middle."));
        var notes = new[] {
            NoteOn(0, 4, "First", Block(BlockKind.Paragraph, 3, 3, "Top.")),
            NoteOn(1, 14, "Last", Block(BlockKind.Paragraph, 13, 13, "Bottom.")),
        };

        PositionRestorer.FindNote(previous, notes).Should().BeSameAs(notes[1]);
    }

    [Fact]
    public void FindNote_NoteAtTheTop_MatchesWithoutABlock() {
        // The note with the block is nearer to the previous line: only matching on the missing
        // block (both have none) picks the other one.
        var previous = NoteOn(0, 1, "Top note", null);
        var notes = new[] {
            NoteOn(0, 3, "Top note", null),
            NoteOn(1, 2, "Top note", Block(BlockKind.Paragraph, 1, 1, "Text.")),
        };

        PositionRestorer.FindNote(previous, notes).Should().BeSameAs(notes[0]);
    }

    [Fact]
    public void FindNote_NoPreviousNote_ReturnsNull() =>
        PositionRestorer.FindNote(null, [NoteOn(0, 1, "Note", null)]).Should().BeNull();

    [Fact]
    public void FindNote_NoNotesLeft_ReturnsNull() =>
        PositionRestorer.FindNote(NoteOn(0, 1, "Note", null), []).Should().BeNull();
}
