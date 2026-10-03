namespace Oire.PlanCake.Notes;

/// <summary>
/// The note to edit or delete (or one of the notes to clear) has no closing marker, so it runs
/// to the end of the file: nothing was written, since rewriting or removing it would take the
/// rest of the document with it.
/// </summary>
internal sealed class UnterminatedNoteException: NoteWriteException {
    public UnterminatedNoteException() : base("The note has no closing marker, so it runs to the end of the file.") { }

    public UnterminatedNoteException(string message) : base(message) { }

    public UnterminatedNoteException(string message, Exception innerException) : base(message, innerException) { }

    /// <param name="line">The 1-based line the note starts on.</param>
    public UnterminatedNoteException(int line)
        : base($"The note on line {line} has no closing marker, so it runs to the end of the file.") {
        Line = line;
    }

    /// <summary>The 1-based line the note starts on; 0 when not given.</summary>
    public int Line { get; }
}
