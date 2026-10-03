namespace Oire.PlanCake.Notes;

/// <summary>
/// The file is open read-only because it is not valid in its detected encoding, and PlanCake
/// never writes it (see <see cref="MarkdownFile.IsReadOnly"/>).
/// </summary>
internal sealed class ReadOnlyFileException: NoteWriteException {
    public ReadOnlyFileException() : base("The file is open read-only.") { }

    public ReadOnlyFileException(string message) : base(message) { }

    public ReadOnlyFileException(string message, Exception innerException) : base(message, innerException) { }
}
