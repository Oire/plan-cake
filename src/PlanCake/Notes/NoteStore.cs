using Oire.PlanCake.Rendering;

namespace Oire.PlanCake.Notes;

/// <summary>What a <see cref="NoteChange"/> did to the file.</summary>
internal enum NoteOperation {
    Add,
    Edit,
    Delete,
    Clear,

    /// <summary>A task-list item was checked.</summary>
    CheckTask,

    /// <summary>A task-list item was unchecked.</summary>
    UncheckTask,
}

/// <summary>What is wrong with a note's text, if anything.</summary>
internal enum NoteTextError {
    None,

    /// <summary>The text is blank.</summary>
    Empty,

    /// <summary>Paired mode: the closing marker would end the note early.</summary>
    ContainsClosingMarker,

    /// <summary>Single-token mode: the opening token would split the note in two when read back.</summary>
    ContainsOpeningMarker,

    /// <summary>Single-token mode: a note ends at the end of its line.</summary>
    ContainsLineBreak,
}

/// <summary>One change <see cref="NoteStore"/> made to the file, as undo and redo replay it.</summary>
/// <param name="Operation">What the change did.</param>
/// <param name="Before">The file's text before the change.</param>
/// <param name="After">The file's text after the change.</param>
/// <param name="Line">
/// The 1-based line of <see cref="After"/> the added or edited note starts on, or the line the
/// deleted note started on, or the line the toggled task-list item starts on;
/// <see langword="null"/> for <see cref="NoteOperation.Clear"/>.
/// </param>
/// <param name="Count">How many notes (or tasks) the change touched.</param>
internal sealed record NoteChange(NoteOperation Operation, string Before, string After, int? Line, int Count);

/// <summary>
/// Adds, edits and deletes notes in a Markdown file (and checks and unchecks its task-list
/// items), never over a change the caller has not seen, and undoes and redoes those changes.
/// Where a note goes is set out in plan 001, Technical details → "Note placement in the file".
/// </summary>
/// <remarks>
/// Every operation takes the text the caller last rendered and throws
/// <see cref="StaleFileException"/> without writing when the file on disk differs from it.
/// </remarks>
internal sealed class NoteStore {
    /// <summary>
    /// How many changes can be undone. Each one holds the file's text twice, so an unbounded
    /// history of a large file would grow without limit; the oldest change is forgotten first.
    /// </summary>
    public const int HistoryLimit = 100;

    private readonly LinkedList<NoteChange> _undo = new();
    private readonly Stack<NoteChange> _redo = new();

    public NoteStore(MarkdownFile file, NoteMarkers markers) {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(markers);

        File = file;
        Markers = markers;
    }

    public MarkdownFile File { get; }

    /// <summary>The markers notes are read and written with.</summary>
    public NoteMarkers Markers { get; set; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>
    /// Trims the text and normalizes its line breaks to <c>\n</c>; in single-token mode, where a
    /// note ends at the end of its line, turns each line break into a space.
    /// </summary>
    public string NormalizeText(string text) {
        ArgumentNullException.ThrowIfNull(text);

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();

        return Markers.IsSingleToken ? normalized.Replace('\n', ' ') : normalized;
    }

    /// <summary>Checks that <paramref name="text"/> reads back as exactly one note once written.</summary>
    public NoteTextError Validate(string text) {
        ArgumentNullException.ThrowIfNull(text);

        if (String.IsNullOrWhiteSpace(text)) {
            return NoteTextError.Empty;
        }

        if (Markers.IsSingleToken) {
            if (text.AsSpan().IndexOfAny('\r', '\n') >= 0) {
                return NoteTextError.ContainsLineBreak;
            }

            return text.Contains(Markers.Opening, StringComparison.Ordinal)
                ? NoteTextError.ContainsOpeningMarker
                : NoteTextError.None;
        }

        // Also catches a text ending in the start of the closing marker, which the marker PlanCake
        // appends would complete early.
        var withClosing = String.Concat(text, Markers.Closing);

        return withClosing.IndexOf(Markers.Closing, StringComparison.Ordinal) < text.Length
            ? NoteTextError.ContainsClosingMarker
            : NoteTextError.None;
    }

    /// <summary>
    /// Writes a note after <paramref name="afterBlock"/>'s last line, after any notes already
    /// anchored there.
    /// </summary>
    /// <exception cref="UnterminatedNoteException">
    /// A note without a closing marker starts before where the note would go, so the new note's
    /// closing marker would end it (<see cref="UnterminatedBefore"/>); nothing is written.
    /// </exception>
    /// <exception cref="NoteInCodeException">
    /// The note would land inside code (a fenced code block that is never closed), where it would
    /// not read back as a note; nothing is written.
    /// </exception>
    public NoteChange Add(string renderedText, BlockInfo afterBlock, string text) {
        ArgumentNullException.ThrowIfNull(afterBlock);

        var noteText = PrepareText(text);
        var current = ReadCurrent(renderedText);
        var parse = NoteParser.Parse(current, Markers);

        if (UnterminatedBefore(current, parse, afterBlock) is { } unterminated) {
            throw new UnterminatedNoteException(unterminated.StartLine);
        }

        var (after, line) = InsertNote(current, parse, afterBlock, noteText, Markers, File.LineEnding);

        // Markers inside code are not notes, so a note written into code would vanish into it.
        if (!NoteParser.Parse(after, Markers).Notes.Any(note => note.StartLine == line && note.Text == noteText)) {
            throw new NoteInCodeException();
        }

        return Apply(new NoteChange(NoteOperation.Add, current, after, line, 1));
    }

    /// <summary>
    /// The note without a closing marker that a note added after <paramref name="block"/> would
    /// land in, or <see langword="null"/> when there is none. Such a note runs to the end of the
    /// file; a note written after its start would give it a closing marker, so the two would read
    /// back as one note taking in everything between them, which deleting it would then remove.
    /// </summary>
    public Note? UnterminatedBefore(string source, BlockInfo block) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(block);

        return UnterminatedBefore(source, NoteParser.Parse(source, Markers), block);
    }

    /// <summary>Replaces the text of <paramref name="note"/>, found in the rendered text.</summary>
    /// <exception cref="UnterminatedNoteException">The note has no closing marker; nothing is written.</exception>
    public NoteChange Edit(string renderedText, Note note, string text) {
        ArgumentNullException.ThrowIfNull(note);

        var noteText = PrepareText(text);
        var current = ReadCurrent(renderedText);
        EnsureNoteIn(current, note);
        EnsureTerminated(note);
        var after = ReplaceNote(current, note, noteText, Markers, File.LineEnding);

        return Apply(new NoteChange(NoteOperation.Edit, current, after, note.StartLine, 1));
    }

    /// <summary>Removes <paramref name="note"/>, found in the rendered text.</summary>
    /// <exception cref="UnterminatedNoteException">The note has no closing marker; nothing is written.</exception>
    public NoteChange Delete(string renderedText, Note note) {
        ArgumentNullException.ThrowIfNull(note);

        var current = ReadCurrent(renderedText);
        EnsureNoteIn(current, note);
        EnsureTerminated(note);
        var after = RemoveNote(current, note);

        return Apply(new NoteChange(NoteOperation.Delete, current, after, note.StartLine, 1));
    }

    /// <summary>Removes every note; nothing is written when there is none.</summary>
    /// <exception cref="UnterminatedNoteException">A note has no closing marker; nothing is written.</exception>
    public NoteChange Clear(string renderedText) {
        var current = ReadCurrent(renderedText);
        var notes = NoteParser.Parse(current, Markers).Notes;

        foreach (var note in notes) {
            EnsureTerminated(note);
        }

        var after = RemoveAll(current, notes);

        return Apply(new NoteChange(NoteOperation.Clear, current, after, null, notes.Count));
    }

    /// <summary>
    /// Checks or unchecks the task-list item starting on <paramref name="line"/> (1-based,
    /// original), through the same stale-safe write and undo history as the note operations.
    /// </summary>
    /// <exception cref="ArgumentException">The line holds no task marker.</exception>
    public NoteChange ToggleTask(string renderedText, int line, bool isChecked) {
        var current = ReadCurrent(renderedText);
        var after = TaskToggle.SetChecked(current, line, isChecked);
        var operation = isChecked ? NoteOperation.CheckTask : NoteOperation.UncheckTask;

        return Apply(new NoteChange(operation, current, after, line, 1));
    }

    /// <summary>
    /// Puts back the text from before the last change, provided the file still holds the text
    /// that change wrote; otherwise throws <see cref="StaleFileException"/> and forgets the history.
    /// </summary>
    /// <returns>The change that was undone.</returns>
    public NoteChange Undo() {
        if (!CanUndo) {
            throw new InvalidOperationException("There is nothing to undo.");
        }

        var change = _undo.Last!.Value;
        WriteReplacing(change.After, change.Before);
        _undo.RemoveLast();
        _redo.Push(change);

        return change;
    }

    /// <summary>
    /// Makes the last undone change again, provided the file still holds the text the undo
    /// wrote; otherwise throws <see cref="StaleFileException"/> and forgets the history.
    /// </summary>
    /// <returns>The change that was redone.</returns>
    public NoteChange Redo() {
        if (!CanRedo) {
            throw new InvalidOperationException("There is nothing to redo.");
        }

        var change = _redo.Peek();
        WriteReplacing(change.Before, change.After);
        _redo.Pop();
        PushUndo(change);

        return change;
    }

    /// <summary>Forgets every change, so nothing can be undone or redone.</summary>
    public void ClearHistory() {
        _undo.Clear();
        _redo.Clear();
    }

    private string PrepareText(string text) {
        var normalized = NormalizeText(text);
        var error = Validate(normalized);

        return error == NoteTextError.None
            ? normalized
            : throw new ArgumentException($"The note text cannot be written: {error}.", nameof(text));
    }

    /// <summary>Reads the file afresh and checks it still holds what the caller rendered.</summary>
    private string ReadCurrent(string renderedText) {
        ArgumentNullException.ThrowIfNull(renderedText);

        var current = File.Reload();

        if (!String.Equals(current, renderedText, StringComparison.Ordinal)) {
            throw new StaleFileException();
        }

        File.EnsureWritable();

        return current;
    }

    /// <summary>
    /// Refuses a note without a closing marker: it runs to the end of the file, so rewriting or
    /// removing it would take the rest of the document with it.
    /// </summary>
    private static void EnsureTerminated(Note note) {
        if (note.Unterminated) {
            throw new UnterminatedNoteException(note.StartLine);
        }
    }

    private void EnsureNoteIn(string source, Note note) {
        var found = NoteParser.Parse(source, Markers).Notes
            .Any(candidate => candidate.Start == note.Start && candidate.End == note.End);

        if (!found) {
            throw new ArgumentException("The note is not in the file's current text.", nameof(note));
        }
    }

    private NoteChange Apply(NoteChange change) {
        if (String.Equals(change.Before, change.After, StringComparison.Ordinal)) {
            return change;
        }

        File.Write(change.After);
        PushUndo(change);
        _redo.Clear();

        return change;
    }

    private void PushUndo(NoteChange change) {
        _undo.AddLast(change);

        if (_undo.Count > HistoryLimit) {
            _undo.RemoveFirst();
        }
    }

    private void WriteReplacing(string expected, string replacement) {
        var current = File.Reload();

        if (!String.Equals(current, expected, StringComparison.Ordinal)) {
            ClearHistory();

            throw new StaleFileException();
        }

        File.EnsureWritable();
        File.Write(replacement);
    }

    // Pure text operations, internal for the tests.

    private static Note? UnterminatedBefore(string source, NoteParseResult parse, BlockInfo block) {
        // Only the last note can lack its closing marker: it runs to the end of the file.
        if (parse.Notes.FirstOrDefault(note => note.Unterminated) is not { } unterminated) {
            return null;
        }

        // A note goes in at the start of its line, so one inserted on the unterminated note's own
        // line or before it stays out of it.
        return InsertionLine(source, parse, block) > unterminated.StartLine ? unterminated : null;
    }

    /// <summary>
    /// The 1-based line a note added after <paramref name="block"/> goes in before (one past the
    /// last line at the end of the file): after the block's last original line and any notes
    /// already anchored there (skipping blank lines only when a note follows them).
    /// </summary>
    private static int InsertionLine(string source, NoteParseResult parse, BlockInfo block) {
        var lines = NoteParser.SplitLines(source);
        ArgumentOutOfRangeException.ThrowIfLessThan(block.StartLine, 1, nameof(block));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(block.StartLine, block.EndLine, nameof(block));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(block.EndLine, lines.Count, nameof(block));

        // A line the parser removed held nothing but notes; a note line right after the block
        // (blank lines between allowed) is anchored to it.
        var surviving = new HashSet<int>(parse.LineMap);
        var insertAt = block.EndLine + 1;

        for (var probe = insertAt; probe <= lines.Count; probe++) {
            if (!surviving.Contains(probe)) {
                insertAt = probe + 1;
            } else if (!IsBlank(source, lines[probe - 1])) {
                break;
            }
        }

        return insertAt;
    }

    /// <summary>
    /// Inserts a note on new lines after <paramref name="block"/>'s last original line and after
    /// any notes already anchored there (skipping blank lines only when a note follows them).
    /// </summary>
    /// <returns>The new text and the 1-based line the note starts on.</returns>
    internal static (string Text, int Line) InsertNote(
        string source,
        NoteParseResult parse,
        BlockInfo block,
        string text,
        NoteMarkers markers,
        string lineEnding
    ) {
        var insertAt = InsertionLine(source, parse, block);
        var lines = NoteParser.SplitLines(source);
        var anchorLine = lines[block.StartLine - 1];
        var prefix = block.Kind == BlockKind.ListItem
            ? ListItemPrefix(source, anchorLine)
            : source[anchorLine.Start..(anchorLine.Start + IndentationLength(source, anchorLine))];
        var written = String.Join(lineEnding, FormatNote(text, markers, prefix, prefix));

        if (insertAt <= lines.Count) {
            return (source.Insert(lines[insertAt - 1].Start, written + lineEnding), insertAt);
        }

        var last = lines[^1];

        return last.End > last.ContentEnd
            ? (source + written + lineEnding, insertAt)
            : (source + lineEnding + written, insertAt);
    }

    /// <summary>Replaces the note's characters with the note written anew with <paramref name="text"/>.</summary>
    internal static string ReplaceNote(string source, Note note, string text, NoteMarkers markers, string lineEnding) {
        var lineStart = LineStartOf(source, note.Start);
        var indentation = source[lineStart..SkipIndentation(source, lineStart, note.Start)];
        var written = String.Join(lineEnding, FormatNote(text, markers, String.Empty, indentation));
        var end = EffectiveEnd(source, note);

        return String.Concat(source.AsSpan(0, note.Start), written, source.AsSpan(end));
    }

    /// <summary>
    /// Removes the note. Lines it leaves empty (or holding only whitespace and <c>&gt;</c>) go
    /// with it, as they do when the parser strips notes; otherwise the whitespace between the
    /// note and the line's start or end goes too.
    /// </summary>
    internal static string RemoveNote(string source, Note note) {
        var end = EffectiveEnd(source, note);
        var lineStart = LineStartOf(source, note.Start);
        var contentEnd = ContentEndFrom(source, end);
        var blankBefore = IsBlankOrQuoteOnly(source.AsSpan(lineStart, note.Start - lineStart));
        var blankAfter = IsBlankOrQuoteOnly(source.AsSpan(end, contentEnd - end));

        if (blankBefore && blankAfter) {
            var lineEnd = BreakEnd(source, contentEnd);

            // The last line has no line break of its own: take the one before it instead, so a
            // note added at the end of a file without a final line break deletes cleanly.
            if (lineEnd == contentEnd && lineStart > 0) {
                lineStart = BreakStartBefore(source, lineStart);
            }

            return source.Remove(lineStart, lineEnd - lineStart);
        }

        if (blankAfter) {
            var start = note.Start;

            while (start > lineStart && source[start - 1] is ' ' or '\t') {
                start--;
            }

            return source.Remove(start, contentEnd - start);
        }

        var removeEnd = end;

        if (blankBefore) {
            while (removeEnd < contentEnd && source[removeEnd] is ' ' or '\t') {
                removeEnd++;
            }
        }

        return source.Remove(note.Start, removeEnd - note.Start);
    }

    /// <summary>Removes every note, last first so the earlier notes' offsets stay valid.</summary>
    internal static string RemoveAll(string source, IReadOnlyList<Note> notes) {
        for (var i = notes.Count - 1; i >= 0; i--) {
            source = RemoveNote(source, notes[i]);
        }

        return source;
    }

    /// <summary>
    /// The lines of a written note: <c>[usernote]text[/usernote]</c> with the markers around the
    /// whole text, or <c>!USERNOTE! text</c> in single-token mode. The first line gets
    /// <paramref name="firstPrefix"/>, every further non-empty line <paramref name="prefix"/>.
    /// </summary>
    private static List<string> FormatNote(string text, NoteMarkers markers, string firstPrefix, string prefix) {
        if (markers.IsSingleToken) {
            return [$"{firstPrefix}{markers.Opening} {text}"];
        }

        var lines = text.Split('\n');
        lines[0] = markers.Opening + lines[0];
        lines[^1] += markers.Closing;

        // An empty continuation line gets no prefix: the parser reads it back empty either way,
        // and the file gets no trailing whitespace.
        return lines
            .Select((line, index) => (index == 0 ? firstPrefix : line.Length == 0 ? String.Empty : prefix) + line)
            .ToList();
    }

    /// <summary>
    /// The indentation for a note on a list item: the item's content column, in spaces (tabs
    /// kept), quote markers included, so the note lines up under the item's text.
    /// </summary>
    private static string ListItemPrefix(string source, NoteParser.Line line) {
        var i = line.Start;

        while (i < line.ContentEnd && source[i] is ' ' or '\t' or '>') {
            i++;
        }

        var contentStart = ListContentStart(source, i, line.ContentEnd) ?? i;

        return String.Create(
            contentStart - line.Start,
            (source, line.Start),
            static (span, state) => {
                for (var k = 0; k < span.Length; k++) {
                    span[k] = state.source[state.Start + k] == '\t' ? '\t' : ' ';
                }
            }
        );
    }

    /// <summary>
    /// Where the content of a list item whose marker starts at <paramref name="markerStart"/>
    /// begins, or <see langword="null"/> when no list marker is there.
    /// </summary>
    private static int? ListContentStart(string source, int markerStart, int contentEnd) {
        if (!NoteParser.TryParseListMarker(source, markerStart, contentEnd, out var spacesStart, out var i)) {
            return null;
        }

        // An empty item, or content indented five or more (an indented code block): the content
        // column is one space after the marker.
        return i == contentEnd || i - spacesStart > 4 ? Math.Min(spacesStart + 1, contentEnd) : i;
    }

    private static int IndentationLength(string source, NoteParser.Line line) =>
        SkipIndentation(source, line.Start, line.ContentEnd) - line.Start;

    private static int SkipIndentation(string source, int start, int limit) {
        var i = start;

        while (i < limit && source[i] is ' ' or '\t') {
            i++;
        }

        return i;
    }

    private static bool IsBlank(string source, NoteParser.Line line) =>
        source.AsSpan(line.Start, line.ContentEnd - line.Start).IsWhiteSpace();

    private static bool IsBlankOrQuoteOnly(ReadOnlySpan<char> text) {
        foreach (var c in text) {
            if (c != '>' && !Char.IsWhiteSpace(c)) {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Where the note ends for rewriting: an unterminated note runs to the end of the file, but the
    /// line breaks at the very end stay the file's own.
    /// </summary>
    private static int EffectiveEnd(string source, Note note) {
        var end = note.End;

        if (note.Unterminated) {
            while (end > note.Start && source[end - 1] is '\r' or '\n') {
                end--;
            }
        }

        return end;
    }

    private static int LineStartOf(string source, int position) {
        while (position > 0 && source[position - 1] is not ('\r' or '\n')) {
            position--;
        }

        return position;
    }

    private static int ContentEndFrom(string source, int position) {
        while (position < source.Length && source[position] is not ('\r' or '\n')) {
            position++;
        }

        return position;
    }

    private static int BreakEnd(string source, int contentEnd) {
        if (contentEnd >= source.Length) {
            return contentEnd;
        }

        return source[contentEnd] == '\r' && contentEnd + 1 < source.Length && source[contentEnd + 1] == '\n'
            ? contentEnd + 2
            : contentEnd + 1;
    }

    /// <summary>The start of the line break that ends just before <paramref name="lineStart"/>.</summary>
    private static int BreakStartBefore(string source, int lineStart) =>
        lineStart >= 2 && source[lineStart - 2] == '\r' && source[lineStart - 1] == '\n'
            ? lineStart - 2
            : lineStart - 1;
}
