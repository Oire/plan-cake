namespace Oire.PlanCake.Notes;

/// <summary>A note found in a Markdown source.</summary>
/// <param name="Text">
/// The note's text, trimmed, with the indentation PlanCake writes in front of continuation lines
/// removed and line breaks normalized to <c>\n</c>.
/// </param>
/// <param name="StartLine">The 1-based original line the opening marker is on.</param>
/// <param name="EndLine">The 1-based original line the note ends on.</param>
/// <param name="Start">Offset in the source of the opening marker's first character.</param>
/// <param name="End">
/// Offset in the source just past the note: past the closing marker in paired mode, at the end
/// of the line (before its line break) in single-token mode, or at the end of the source for an
/// unterminated note.
/// </param>
/// <param name="Unterminated">
/// True for a paired-mode note whose closing marker is missing: it runs to the end of the source.
/// </param>
internal sealed record Note(string Text, int StartLine, int EndLine, int Start, int End, bool Unterminated) {
    /// <summary>Length in characters of the note in the source, markers included.</summary>
    public int Length => End - Start;
}
