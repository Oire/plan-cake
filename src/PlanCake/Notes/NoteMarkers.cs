namespace Oire.PlanCake.Notes;

/// <summary>What is wrong with a <see cref="NoteMarkers"/> configuration, if anything.</summary>
internal enum NoteMarkersError {
    None,
    EmptyOpening,
    SurroundingWhitespace,
    LineBreak,
    ClosingSameAsOpening,
}

/// <summary>
/// The markers that wrap a note in the Markdown source. With a <see cref="Closing"/> marker a
/// note runs from <see cref="Opening"/> to the next <see cref="Closing"/> (paired mode); with an
/// empty one it runs from <see cref="Opening"/> to the end of that line (single-token mode).
/// Matching is ordinal and case-sensitive.
/// </summary>
internal sealed record NoteMarkers(string Opening, string Closing = "") {
    /// <summary>The pair Debussy's manual review understands: <c>[usernote]…[/usernote]</c>.</summary>
    public static NoteMarkers Default { get; } = new("[usernote]", "[/usernote]");

    /// <summary>True when a note runs from the opening marker to the end of its line.</summary>
    public bool IsSingleToken => Closing.Length == 0;

    /// <summary>
    /// Checks the markers can delimit a note: the opening marker is required, neither marker has
    /// whitespace around it or a line break in it, and the closing marker differs from the
    /// opening one. Returns <see cref="NoteMarkersError.None"/> when they can; the caller turns
    /// anything else into a message in the interface language.
    /// </summary>
    public NoteMarkersError Validate() {
        if (String.IsNullOrEmpty(Opening)) {
            return NoteMarkersError.EmptyOpening;
        }

        if (HasLineBreak(Opening) || HasLineBreak(Closing)) {
            return NoteMarkersError.LineBreak;
        }

        if (HasSurroundingWhitespace(Opening) || HasSurroundingWhitespace(Closing)) {
            return NoteMarkersError.SurroundingWhitespace;
        }

        return String.Equals(Opening, Closing, StringComparison.Ordinal)
            ? NoteMarkersError.ClosingSameAsOpening
            : NoteMarkersError.None;
    }

    private static bool HasLineBreak(string marker) => marker.AsSpan().IndexOfAny('\r', '\n') >= 0;

    private static bool HasSurroundingWhitespace(string marker) =>
        marker.Length > 0 && (Char.IsWhiteSpace(marker[0]) || Char.IsWhiteSpace(marker[^1]));
}
