using Oire.PlanCake.Rendering;

namespace Oire.PlanCake.Ui;

/// <summary>
/// The cells of one row of the notes list beside the document: where the note is in the file,
/// the block it follows, and the note itself, each on one line of plain text.
/// </summary>
/// <param name="Lines">The note's original lines: <c>12</c>, or <c>12-14</c> for a note over several.</param>
/// <param name="Block">The excerpt of the block the note follows.</param>
/// <param name="Text">
/// The note's Markdown as plain text (<see cref="MarkdownRenderer.NotePlainText"/>) on one line,
/// so a screen reader reads no backticks or asterisks.
/// </param>
internal sealed record NotesListRow(string Lines, string Block, string Text) {
    /// <summary>The cells in column order.</summary>
    public string[] ToCells() => [Lines, Block, Text];

    /// <summary>The row for <paramref name="note"/>.</summary>
    /// <param name="note">A note of the current render.</param>
    /// <param name="startOfDocument">What the block cell says for a note before the first block.</param>
    public static NotesListRow From(RenderedNote note, string startOfDocument) {
        ArgumentNullException.ThrowIfNull(note);

        var start = note.Note.StartLine;
        var end = note.Note.EndLine;
        var lines = end > start ? $"{start}-{end}" : $"{start}";
        var text = string.Join(
            ' ',
            MarkdownRenderer.NotePlainText(note.Note.Text).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
        );

        return new NotesListRow(lines, note.Block?.Excerpt ?? startOfDocument, text);
    }
}
