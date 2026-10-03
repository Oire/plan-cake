namespace Oire.PlanCake.Notes;

/// <summary>
/// A note added after the block would not read back as a note, because it would land inside code
/// (a fenced code block that is never closed runs to the end of the file): nothing was written.
/// </summary>
internal sealed class NoteInCodeException: NoteWriteException {
    public NoteInCodeException() : base("A note added after this block would be inside a code block.") { }

    public NoteInCodeException(string message) : base(message) { }

    public NoteInCodeException(string message, Exception innerException) : base(message, innerException) { }
}
