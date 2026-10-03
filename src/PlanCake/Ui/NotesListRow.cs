using Oire.PlanCake.Rendering;

namespace Oire.PlanCake.Ui;

/// <summary>
/// The cells of one row of the notes list beside the document: the note, where it is in the
/// file, and the block it follows, each on one line of plain text. The note comes first, since it
/// is what the row is about, and a screen reader reads the cells in this order.
/// </summary>
/// <param name="Text">
/// The first sentence of the note (<see cref="MarkdownRenderer.FirstSentence"/>) as plain text
/// (<see cref="MarkdownRenderer.NotePlainText"/>), so a screen reader reads no backticks or
/// asterisks; the whole note is the row's info tip (<see cref="TipText"/>).
/// </param>
/// <param name="Lines">The note's original lines: <c>12</c>, or <c>12-14</c> for a note over several.</param>
/// <param name="Block">The excerpt of the block the note follows.</param>
internal sealed record NotesListRow(string Text, string Lines, string Block) {
    /// <summary>The cells in column order.</summary>
    public string[] ToCells() => [Text, Lines, Block];

    /// <summary>The row for <paramref name="note"/>.</summary>
    /// <param name="note">A note of the current render.</param>
    /// <param name="startOfDocument">What the block cell says for a note before the first block.</param>
    public static NotesListRow From(RenderedNote note, string startOfDocument) {
        ArgumentNullException.ThrowIfNull(note);

        var start = note.Note.StartLine;
        var end = note.Note.EndLine;
        var lines = end > start ? $"{start}-{end}" : $"{start}";
        var text = MarkdownRenderer.FirstSentence(MarkdownRenderer.NotePlainText(note.Note.Text));

        return new NotesListRow(text, lines, note.Block?.Excerpt ?? startOfDocument);
    }

    /// <summary>The whole note as plain text on one line, which the row shows when the pointer rests on it.</summary>
    public static string TipText(RenderedNote note) {
        ArgumentNullException.ThrowIfNull(note);

        return MarkdownRenderer.OneLine(MarkdownRenderer.NotePlainText(note.Note.Text));
    }
}
