using System.Text;

namespace Oire.PlanCake.Notes;

/// <summary>
/// The notes found in a Markdown source, and the source with them taken out, which has the same
/// Markdown structure as the file would have without any notes.
/// </summary>
internal sealed class NoteParseResult {
    private readonly int[] _lineMap;

    public NoteParseResult(IReadOnlyList<Note> notes, string strippedSource, int[] lineMap) {
        Notes = notes;
        StrippedSource = strippedSource;
        _lineMap = lineMap;
    }

    /// <summary>The notes in source order.</summary>
    public IReadOnlyList<Note> Notes { get; }

    /// <summary>The source with every note removed, per Technical details → "Note parsing".</summary>
    public string StrippedSource { get; }

    /// <summary>
    /// For every line of <see cref="StrippedSource"/>, in order, the 1-based original line it
    /// came from: <c>LineMap[0]</c> is the original line of stripped line 1.
    /// </summary>
    public IReadOnlyList<int> LineMap => _lineMap;

    /// <summary>True when a paired-mode note has no closing marker.</summary>
    public bool HasUnterminated => Notes.Any(note => note.Unterminated);

    /// <summary>Maps a 1-based line of <see cref="StrippedSource"/> to its 1-based original line.</summary>
    public int ToOriginalLine(int strippedLine) {
        ArgumentOutOfRangeException.ThrowIfLessThan(strippedLine, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(strippedLine, _lineMap.Length);

        return _lineMap[strippedLine - 1];
    }
}

/// <summary>
/// Finds notes in a Markdown source and strips them out before the source is rendered, per
/// Technical details → "Note parsing" in the PlanCake plan.
/// </summary>
internal static class NoteParser {
    /// <summary>A source line: content from Start to ContentEnd, then its line break up to End.</summary>
    internal readonly record struct Line(int Start, int ContentEnd, int End);

    public static NoteParseResult Parse(string source, NoteMarkers markers) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(markers);

        var error = markers.Validate();

        if (error != NoteMarkersError.None) {
            throw new ArgumentException($"Invalid note markers: {error}.", nameof(markers));
        }

        var lines = SplitLines(source);
        var notes = FindNotes(source, markers, lines);
        var (stripped, lineMap) = Strip(source, lines, notes);

        return new NoteParseResult(notes, stripped, lineMap);
    }

    /// <summary>
    /// Reads a list marker, a bullet (<c>-</c>, <c>*</c>, <c>+</c>) or an ordered marker of up to
    /// nine digits (<c>1.</c>, <c>1)</c>), at <paramref name="start"/>, followed by whitespace or
    /// the end of the line (<paramref name="end"/>): <c>-foo</c> is no list item.
    /// </summary>
    /// <param name="markerEnd">Where the marker ends: the start of the whitespace after it.</param>
    /// <param name="contentStart">Where that whitespace ends: <paramref name="end"/> for an empty item.</param>
    /// <returns>False when no list marker starts there.</returns>
    internal static bool TryParseListMarker(string source, int start, int end, out int markerEnd, out int contentStart) {
        ArgumentNullException.ThrowIfNull(source);

        var i = start;
        markerEnd = contentStart = start;

        if (i < end && source[i] is '-' or '*' or '+') {
            i++;
        } else {
            while (i < end && i - start < 9 && Char.IsAsciiDigit(source[i])) {
                i++;
            }

            if (i == start || i >= end || source[i] is not ('.' or ')')) {
                return false;
            }

            i++;
        }

        if (i < end && source[i] is not (' ' or '\t')) {
            return false;
        }

        markerEnd = i;

        while (i < end && source[i] is ' ' or '\t') {
            i++;
        }

        contentStart = i;

        return true;
    }

    /// <summary>Splits a source into lines; a line break at the very end starts no further line.</summary>
    internal static List<Line> SplitLines(string source) {
        var lines = new List<Line>();
        var start = 0;
        var i = 0;

        while (i < source.Length) {
            var c = source[i];

            if (c is not ('\n' or '\r')) {
                i++;
                continue;
            }

            var contentEnd = i;
            i += c == '\r' && i + 1 < source.Length && source[i + 1] == '\n' ? 2 : 1;
            lines.Add(new Line(start, contentEnd, i));
            start = i;
        }

        // A line break at the very end of the source does not start another line.
        if (start < source.Length) {
            lines.Add(new Line(start, source.Length, source.Length));
        }

        return lines;
    }

    /// <summary>The index of the line holding the character at <paramref name="position"/>.</summary>
    private static int LineIndexOf(List<Line> lines, int position) {
        var low = 0;
        var high = lines.Count - 1;

        while (low < high) {
            var middle = (low + high + 1) / 2;

            if (lines[middle].Start <= position) {
                low = middle;
            } else {
                high = middle - 1;
            }
        }

        return low;
    }

    private static List<Note> FindNotes(string source, NoteMarkers markers, List<Line> lines) {
        var notes = new List<Note>();
        var position = 0;

        while (position < source.Length) {
            var start = source.IndexOf(markers.Opening, position, StringComparison.Ordinal);

            if (start < 0) {
                break;
            }

            var startLine = LineIndexOf(lines, start);
            var textStart = start + markers.Opening.Length;
            int textEnd;
            int end;
            var unterminated = false;

            if (markers.IsSingleToken) {
                textEnd = end = lines[startLine].ContentEnd;
            } else {
                var closing = source.IndexOf(markers.Closing, textStart, StringComparison.Ordinal);

                if (closing < 0) {
                    textEnd = end = source.Length;
                    unterminated = true;
                } else {
                    textEnd = closing;
                    end = closing + markers.Closing.Length;
                }
            }

            var endLine = LineIndexOf(lines, Math.Max(start, end - 1));
            var indentation = IndentationOf(source, lines[startLine]);
            var text = CleanText(source.AsSpan(textStart, textEnd - textStart), indentation);

            notes.Add(new Note(text, startLine + 1, endLine + 1, start, end, unterminated));
            position = end;
        }

        return notes;
    }

    private static int IndentationOf(string source, Line line) {
        var i = line.Start;

        while (i < line.ContentEnd && source[i] is ' ' or '\t') {
            i++;
        }

        return i - line.Start;
    }

    /// <summary>
    /// Normalizes line breaks to <c>\n</c>, takes up to <paramref name="indentation"/> leading
    /// whitespace characters off every continuation line (the prefix PlanCake writes in front of
    /// each line of a note), and trims the result.
    /// </summary>
    private static string CleanText(ReadOnlySpan<char> raw, int indentation) {
        var text = new StringBuilder(raw.Length);
        var first = true;

        while (true) {
            var lineBreak = raw.IndexOfAny('\r', '\n');
            var line = lineBreak < 0 ? raw : raw[..lineBreak];

            if (!first) {
                text.Append('\n');
                var removable = 0;

                while (removable < indentation && removable < line.Length && line[removable] is ' ' or '\t') {
                    removable++;
                }

                line = line[removable..];
            }

            text.Append(line);
            first = false;

            if (lineBreak < 0) {
                break;
            }

            var isCrLf = raw[lineBreak] == '\r' && lineBreak + 1 < raw.Length && raw[lineBreak + 1] == '\n';
            raw = raw[(lineBreak + (isCrLf ? 2 : 1))..];
        }

        return text.ToString().Trim();
    }

    /// <summary>
    /// Cuts every note out of the source line by line. A line a note touched that is left empty
    /// (or holding only whitespace and <c>&gt;</c> quote markers) is removed with its line break;
    /// any other line stays, with the note cut out and its own line break kept, so every
    /// stripped line maps to exactly one original line.
    /// </summary>
    private static (string Stripped, int[] LineMap) Strip(string source, List<Line> lines, List<Note> notes) {
        if (notes.Count == 0) {
            return (source, Enumerable.Range(1, lines.Count).ToArray());
        }

        var stripped = new StringBuilder(source.Length);
        var lineMap = new List<int>(lines.Count);
        var kept = new StringBuilder();
        var firstNote = 0;

        for (var index = 0; index < lines.Count; index++) {
            var line = lines[index];

            while (firstNote < notes.Count && notes[firstNote].End <= line.Start) {
                firstNote++;
            }

            kept.Clear();
            var touched = false;
            var cutAtEnd = false;
            var cursor = line.Start;

            for (var n = firstNote; n < notes.Count && notes[n].Start < line.End; n++) {
                var note = notes[n];
                touched = true;
                var cutStart = Math.Max(note.Start, line.Start);
                var cutEnd = Math.Min(note.End, line.ContentEnd);

                if (cutStart > cursor) {
                    kept.Append(source, cursor, cutStart - cursor);
                }

                cursor = Math.Max(cursor, cutEnd);
            }

            if (!touched) {
                stripped.Append(source, line.Start, line.End - line.Start);
                lineMap.Add(index + 1);
                continue;
            }

            if (cursor < line.ContentEnd) {
                kept.Append(source, cursor, line.ContentEnd - cursor);
            } else {
                cutAtEnd = true;
            }

            if (IsBlankOrQuoteOnly(kept)) {
                continue;
            }

            // "text [usernote]…[/usernote]" without the note is "text": a space left in front of
            // the cut would otherwise count towards a Markdown hard line break.
            if (cutAtEnd) {
                TrimEnd(kept);
            }

            stripped.Append(kept).Append(source, line.ContentEnd, line.End - line.ContentEnd);
            lineMap.Add(index + 1);
        }

        return (stripped.ToString(), lineMap.ToArray());
    }

    private static bool IsBlankOrQuoteOnly(StringBuilder text) {
        foreach (var chunk in text.GetChunks()) {
            foreach (var c in chunk.Span) {
                if (c != '>' && !Char.IsWhiteSpace(c)) {
                    return false;
                }
            }
        }

        return true;
    }

    private static void TrimEnd(StringBuilder text) {
        var length = text.Length;

        while (length > 0 && text[length - 1] is ' ' or '\t') {
            length--;
        }

        text.Length = length;
    }
}
