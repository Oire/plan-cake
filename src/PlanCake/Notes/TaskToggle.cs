namespace Oire.PlanCake.Notes;

/// <summary>
/// Reads and rewrites the task marker (<c>[ ]</c>, <c>[x]</c> or <c>[X]</c>) of a task-list item
/// in a Markdown source. Pure text operations: nothing else on the line, no line ending and no
/// other line is touched.
/// </summary>
internal static class TaskToggle {
    /// <summary>
    /// Whether the task-list item starting on <paramref name="line"/> is checked, or
    /// <see langword="null"/> when that line holds no task marker.
    /// </summary>
    /// <param name="source">The file's text, notes included.</param>
    /// <param name="line">The 1-based original line the item starts on.</param>
    public static bool? IsChecked(string source, int line) {
        ArgumentNullException.ThrowIfNull(source);

        return FindMarker(source, line) is { } position ? source[position] != ' ' : null;
    }

    /// <summary>
    /// Sets the task marker of the item starting on <paramref name="line"/> to <c>[x]</c> or
    /// <c>[ ]</c>. An item already in that state is left exactly as it is (a checked <c>[X]</c>
    /// keeps its capital).
    /// </summary>
    /// <param name="source">The file's text, notes included.</param>
    /// <param name="line">The 1-based original line the item starts on.</param>
    /// <param name="isChecked">The state to write.</param>
    /// <returns>The source with that one character changed.</returns>
    /// <exception cref="ArgumentException">The line holds no task marker.</exception>
    public static string SetChecked(string source, int line, bool isChecked) {
        ArgumentNullException.ThrowIfNull(source);

        var position = FindMarker(source, line)
            ?? throw new ArgumentException($"Line {line} holds no task marker.", nameof(line));

        if ((source[position] != ' ') == isChecked) {
            return source;
        }

        return String.Create(source.Length, (source, position, isChecked), static (span, state) => {
            state.source.AsSpan().CopyTo(span);
            span[state.position] = state.isChecked ? 'x' : ' ';
        });
    }

    /// <summary>
    /// A task-list item's plain text without the <c>[ ]</c> or <c>[x]</c> it starts with, so that
    /// a screen reader does not read the brackets out (the confirmation shows the item's text).
    /// </summary>
    public static string WithoutMarker(string text) {
        ArgumentNullException.ThrowIfNull(text);

        return text.Length >= 3 && text[0] == '[' && text[2] == ']' && text[1] is ' ' or 'x' or 'X'
            ? text[3..].TrimStart()
            : text;
    }

    /// <summary>
    /// The offset of the character between the task marker's brackets on <paramref name="line"/>,
    /// or <see langword="null"/> when there is no marker: after the indentation, any quote
    /// markers and the list marker, the line must go on with <c>[ ]</c>, <c>[x]</c> or <c>[X]</c>.
    /// </summary>
    private static int? FindMarker(string source, int line) {
        var lines = NoteParser.SplitLines(source);
        ArgumentOutOfRangeException.ThrowIfLessThan(line, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(line, lines.Count);

        var (start, end, _) = lines[line - 1];
        var i = SkipIndentationAndQuotes(source, start, end);
        i = SkipListMarker(source, i, end);

        if (end - i < 3 || source[i] != '[' || source[i + 2] != ']' || source[i + 1] is not (' ' or 'x' or 'X')) {
            return null;
        }

        return i + 1;
    }

    private static int SkipIndentationAndQuotes(string source, int i, int end) {
        while (i < end && source[i] is ' ' or '\t' or '>') {
            i++;
        }

        return i;
    }

    /// <summary>
    /// Skips a list marker and the whitespace after it; returns <paramref name="i"/> unchanged
    /// when there is none, or nothing follows it on the line (an item whose paragraph starts on
    /// the line after its marker).
    /// </summary>
    private static int SkipListMarker(string source, int i, int end) =>
        NoteParser.TryParseListMarker(source, i, end, out var markerEnd, out var contentStart) && contentStart > markerEnd
            ? contentStart
            : i;
}
