namespace Oire.PlanCake.Notes;

/// <summary>
/// A change to the file was refused and nothing was written: the file changed on disk
/// (<see cref="StaleFileException"/>), is open read-only (<see cref="ReadOnlyFileException"/>),
/// holds a note without a closing marker (<see cref="UnterminatedNoteException"/>), or the note
/// would land inside code (<see cref="NoteInCodeException"/>). An I/O failure is an
/// <see cref="IOException"/> instead.
/// </summary>
internal abstract class NoteWriteException: InvalidOperationException {
    protected NoteWriteException() { }

    protected NoteWriteException(string message) : base(message) { }

    protected NoteWriteException(string message, Exception innerException) : base(message, innerException) { }
}
