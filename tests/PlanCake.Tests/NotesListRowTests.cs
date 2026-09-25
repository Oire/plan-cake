using AwesomeAssertions;
using Oire.PlanCake.Notes;
using Oire.PlanCake.Rendering;
using Oire.PlanCake.Ui;
using Xunit;

namespace Oire.PlanCake.Tests;

public class NotesListRowTests {
    private const string StartOfDocument = "The start of the document";

    private static readonly BlockInfo _block = new(BlockKind.Paragraph, 3, 4, "Some text.", "Some text.");

    private static RenderedNote NoteOf(string text, int start, int end, BlockInfo? block) =>
        new(0, new Note(text, start, end, 0, 0, Unterminated: false), block);

    [Fact]
    public void From_OneLineNote_ShowsItsLineTheBlockAndTheText() {
        var row = NotesListRow.From(NoteOf("Looks good.", 5, 5, _block), StartOfDocument);

        row.ToCells().Should().Equal("5", "Some text.", "Looks good.");
    }

    [Fact]
    public void From_NoteOverSeveralLines_ShowsTheRangeAndOneLineOfText() {
        var row = NotesListRow.From(NoteOf("First line\nsecond line", 5, 6, _block), StartOfDocument);

        row.Lines.Should().Be("5-6");
        row.Text.Should().Be("First line second line");
    }

    [Fact]
    public void From_NoteWithMarkdown_ShowsPlainText() {
        var row = NotesListRow.From(NoteOf("Use `NoteStore` and **not** the file", 5, 5, _block), StartOfDocument);

        row.Text.Should().Be("Use NoteStore and not the file");
    }

    [Fact]
    public void From_NoteBeforeTheFirstBlock_SaysItIsAtTheStart() {
        var row = NotesListRow.From(NoteOf("Top note", 1, 1, null), StartOfDocument);

        row.Block.Should().Be(StartOfDocument);
    }
}
