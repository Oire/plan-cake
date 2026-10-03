namespace Oire.PlanCake.Notes;

/// <summary>
/// The file on disk no longer holds the text the caller last rendered (or, for undo and redo,
/// the text PlanCake last wrote), so nothing was written: writing now would overwrite a change
/// the user has not seen.
/// </summary>
internal sealed class StaleFileException: NoteWriteException {
    public StaleFileException() : base("The file changed on disk since it was last read.") { }

    public StaleFileException(string message) : base(message) { }

    public StaleFileException(string message, Exception innerException) : base(message, innerException) { }
}
