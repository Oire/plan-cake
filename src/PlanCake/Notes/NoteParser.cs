using System.Text;
using Markdig;
using Markdig.Syntax;
using Oire.PlanCake.Rendering;

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
        var inCode = new HashSet<int>();

        // A marker in an indented code block is only known once the notes are stripped (see
        // FindInIndentedCode); each round sets more markers aside, so this ends.
        while (true) {
            var notes = FindNotes(source, markers, lines, inCode);
            var (stripped, lineMap) = Strip(source, lines, notes);

            if (!FindInIndentedCode(source, lines, notes, stripped, lineMap, inCode)) {
                return new NoteParseResult(notes, stripped, lineMap);
            }
        }
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

    /// <summary>
    /// Finds the notes in source order, skipping every opening marker that is code: inside an
    /// inline code span on its line, inside a fenced code block, or in <paramref name="inCode"/>
    /// (the markers <see cref="FindInIndentedCode"/> found in indented code blocks). A document
    /// that shows the markers as code, such as PlanCake's own README, has no notes; PlanCake
    /// itself never writes a note inside code, only on the lines after a block.
    /// </summary>
    /// <remarks>
    /// The text of a note is never read as Markdown here: a fence or a backtick in a note does not
    /// change what counts as code after it, just as the note is not there once stripped. Code
    /// spans are matched within one line, and a fence opens at any indentation (a fence in a
    /// nested list item is indented too), so a line of a Markdown example in an indented code
    /// block can be taken for a fence.
    /// </remarks>
    private static List<Note> FindNotes(string source, NoteMarkers markers, List<Line> lines, HashSet<int> inCode) {
        var notes = new List<Note>();
        Fence? fence = null;

        // Everything before it belongs to a note already found.
        var position = 0;

        for (var index = 0; index < lines.Count; index++) {
            var line = lines[index];

            if (position >= line.End) {
                continue;
            }

            var from = Math.Max(position, line.Start);

            // Only a line that does not start inside a note can open or close a fence; a note
            // never starts in a fence, so none is open when a note ends halfway through a line.
            if (from == line.Start) {
                if (fence is { } open) {
                    if (IsClosingFence(source, line, open)) {
                        fence = null;
                    }

                    continue;
                }

                if (OpeningFence(source, line) is { } opened) {
                    fence = opened;

                    continue;
                }
            }

            while (FindMarker(source, markers.Opening, from, line.ContentEnd, inCode) is var start and >= 0) {
                var note = ReadNote(source, markers, lines, index, start);
                notes.Add(note);
                position = note.End;

                if (note.End > line.ContentEnd) {
                    break;
                }

                from = note.End;
            }
        }

        return notes;
    }

    /// <summary>The note whose opening marker starts at <paramref name="start"/>, on line <paramref name="lineIndex"/>.</summary>
    private static Note ReadNote(string source, NoteMarkers markers, List<Line> lines, int lineIndex, int start) {
        var textStart = start + markers.Opening.Length;
        int textEnd;
        int end;
        var unterminated = false;

        if (markers.IsSingleToken) {
            textEnd = end = lines[lineIndex].ContentEnd;
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
        var indentation = IndentationOf(source, lines[lineIndex]);
        var text = CleanText(source.AsSpan(textStart, textEnd - textStart), indentation);

        return new Note(text, lineIndex + 1, endLine + 1, start, end, unterminated);
    }

    /// <summary>
    /// The first opening marker in <c>[from, end)</c> that is not inside an inline code span
    /// closed on the same line, nor in <paramref name="inCode"/>; -1 when there is none.
    /// </summary>
    private static int FindMarker(string source, string opening, int from, int end, HashSet<int> inCode) {
        var i = from;

        while (i < end) {
            var marker = source.IndexOf(opening, i, end - i, StringComparison.Ordinal);

            if (marker < 0) {
                return -1;
            }

            var tick = source.IndexOf('`', i, marker - i);

            if (tick < 0) {
                if (!inCode.Contains(marker)) {
                    return marker;
                }

                i = marker + opening.Length;

                continue;
            }

            // A backtick before the marker: skip the code span it opens, if any.
            if (IsEscaped(source, tick)) {
                i = tick + 1;

                continue;
            }

            var runEnd = tick;

            while (runEnd < end && source[runEnd] == '`') {
                runEnd++;
            }

            var closing = ClosingRun(source, runEnd, end, runEnd - tick);
            i = closing < 0 ? runEnd : closing + (runEnd - tick);
        }

        return -1;
    }

    /// <summary>True when the character at <paramref name="index"/> follows an odd number of backslashes.</summary>
    private static bool IsEscaped(string source, int index) {
        var backslashes = 0;

        while (index - backslashes > 0 && source[index - backslashes - 1] == '\\') {
            backslashes++;
        }

        return backslashes % 2 == 1;
    }

    /// <summary>
    /// The start of the first run of exactly <paramref name="length"/> backticks in
    /// <c>[from, end)</c>, which closes a code span opened by a run of that length; -1 when none.
    /// </summary>
    private static int ClosingRun(string source, int from, int end, int length) {
        var i = from;

        while (i < end) {
            var tick = source.IndexOf('`', i, end - i);

            if (tick < 0) {
                return -1;
            }

            var runEnd = tick;

            while (runEnd < end && source[runEnd] == '`') {
                runEnd++;
            }

            if (runEnd - tick == length) {
                return tick;
            }

            i = runEnd;
        }

        return -1;
    }

    /// <summary>An open fenced code block: its fence character, how long its fence is, and the column it starts in.</summary>
    private readonly record struct Fence(char Character, int Length, int Column);

    /// <summary>
    /// The fence <paramref name="line"/> opens, or <see langword="null"/>: three or more backticks
    /// or tildes after any quote markers, list markers and indentation, and for backticks no
    /// backtick in the info string after them.
    /// </summary>
    private static Fence? OpeningFence(string source, Line line) {
        var i = line.Start;
        var column = 0;

        while (true) {
            (i, column) = SkipWhitespace(source, i, line.ContentEnd, column);

            if (i < line.ContentEnd && source[i] == '>') {
                i++;
                column++;
            } else if (TryParseListMarker(source, i, line.ContentEnd, out var markerEnd, out _) && markerEnd < line.ContentEnd) {
                column += markerEnd - i;
                i = markerEnd;
            } else {
                break;
            }
        }

        if (i >= line.ContentEnd || source[i] is not ('`' or '~')) {
            return null;
        }

        var character = source[i];
        var runEnd = i;

        while (runEnd < line.ContentEnd && source[runEnd] == character) {
            runEnd++;
        }

        if (runEnd - i < 3) {
            return null;
        }

        if (character == '`' && source.IndexOf('`', runEnd, line.ContentEnd - runEnd) >= 0) {
            return null;
        }

        return new Fence(character, runEnd - i, column);
    }

    /// <summary>
    /// True when <paramref name="line"/> closes <paramref name="fence"/>: after any quote markers,
    /// at most three columns further in than the opening fence, a run of its character at least
    /// as long, then nothing but whitespace.
    /// </summary>
    private static bool IsClosingFence(string source, Line line, Fence fence) {
        var i = line.Start;
        var column = 0;

        while (true) {
            (i, column) = SkipWhitespace(source, i, line.ContentEnd, column);

            if (i < line.ContentEnd && source[i] == '>') {
                i++;
                column++;
            } else {
                break;
            }
        }

        if (column > fence.Column + 3) {
            return false;
        }

        var runEnd = i;

        while (runEnd < line.ContentEnd && source[runEnd] == fence.Character) {
            runEnd++;
        }

        return runEnd - i >= fence.Length && source.AsSpan(runEnd, line.ContentEnd - runEnd).IsWhiteSpace();
    }

    /// <summary>Skips spaces and tabs, counting columns with tab stops every four.</summary>
    private static (int Index, int Column) SkipWhitespace(string source, int i, int end, int column) {
        while (i < end && source[i] is ' ' or '\t') {
            column = source[i] == '\t' ? column + 4 - (column % 4) : column + 1;
            i++;
        }

        return (i, column);
    }

    /// <summary>
    /// The second look at the notes found: the source is parsed as Markdown without its notes, and
    /// every note that starts on a line of an indented code block (between its first and last
    /// line) is code after all, its marker added to <paramref name="inCode"/>. A note on the lines
    /// right after a code block is not: that is where PlanCake writes a note on the block, at the
    /// block's indentation. A note on a line of its own after a blank line, indented enough to
    /// start a code block, stays in the parsed text, so a code block made of nothing but a marker
    /// line is code too; PlanCake never writes a note there. Parses only when a note starts on a
    /// line indented four columns or more, the least an indented code block needs.
    /// </summary>
    /// <returns>True when a marker was added, so the notes must be found again.</returns>
    private static bool FindInIndentedCode(
        string source,
        List<Line> lines,
        List<Note> notes,
        string stripped,
        int[] lineMap,
        HashSet<int> inCode
    ) {
        var indented = notes.Where(note => IsIndentedForCode(source, lines[note.StartLine - 1])).ToList();

        if (indented.Count == 0) {
            return false;
        }

        var kept = indented.Where(note => IsAfterBlankLine(source, lines, note)).ToHashSet();

        if (kept.Count > 0) {
            (stripped, lineMap) = Strip(source, lines, notes.Where(note => !kept.Contains(note)).ToList());
        }

        var document = Markdown.Parse(stripped, MarkdownRenderer.Pipeline);
        var lineStarts = SplitLines(stripped).Select(line => line.Start).ToArray();
        var added = false;

        foreach (var code in document.Descendants<CodeBlock>()) {
            if (code.Span.IsEmpty) {
                continue;
            }

            var first = lineMap[LineIndexAt(lineStarts, code.Span.Start)];
            var last = lineMap[LineIndexAt(lineStarts, Math.Max(code.Span.Start, code.Span.End))];

            foreach (var note in indented) {
                if (first <= note.StartLine && note.StartLine <= last && inCode.Add(note.Start)) {
                    added = true;
                }
            }
        }

        return added;
    }

    /// <summary>
    /// True when <paramref name="note"/> starts its line (after indentation and quote markers) and
    /// the line before it is blank, or there is none.
    /// </summary>
    private static bool IsAfterBlankLine(string source, List<Line> lines, Note note) {
        var line = lines[note.StartLine - 1];

        if (!IsBlankOrQuoteOnly(source.AsSpan(line.Start, note.Start - line.Start))) {
            return false;
        }

        if (note.StartLine == 1) {
            return true;
        }

        var previous = lines[note.StartLine - 2];

        return IsBlankOrQuoteOnly(source.AsSpan(previous.Start, previous.ContentEnd - previous.Start));
    }

    private static bool IsBlankOrQuoteOnly(ReadOnlySpan<char> text) {
        foreach (var c in text) {
            if (c != '>' && !Char.IsWhiteSpace(c)) {
                return false;
            }
        }

        return true;
    }

    /// <summary>The index of the line, by its start offsets, holding the character at <paramref name="offset"/>.</summary>
    private static int LineIndexAt(int[] lineStarts, int offset) {
        var index = Array.BinarySearch(lineStarts, offset);

        return index >= 0 ? index : ~index - 1;
    }

    /// <summary>True when <paramref name="line"/>, after any quote markers, is indented four columns or more.</summary>
    private static bool IsIndentedForCode(string source, Line line) {
        var i = line.Start;

        while (true) {
            var (next, column) = SkipWhitespace(source, i, line.ContentEnd, 0);

            if (next < line.ContentEnd && source[next] == '>') {
                i = next + 1;

                continue;
            }

            return column >= 4;
        }
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
