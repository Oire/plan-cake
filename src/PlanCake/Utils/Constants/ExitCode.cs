namespace Oire.PlanCake.Utils.Constants;

/// <summary>
/// Process exit codes returned from <c>Program.Main</c>.
/// </summary>
internal static class ExitCode {
    /// <summary>Normal exit with no errors.</summary>
    public const int Success = 0;

    /// <summary>Exit due to an unrecoverable error during startup or runtime.</summary>
    public const int Error = 1;

    /// <summary>
    /// <c>plancake check</c> found notes in the file. Distinct from <see cref="Error"/>, so a caller
    /// can tell "notes left" from "could not read".
    /// </summary>
    public const int NotesRemain = 3;
}
