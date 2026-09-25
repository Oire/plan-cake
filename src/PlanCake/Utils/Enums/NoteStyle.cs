namespace Oire.PlanCake.Utils.Enums;

/// <summary>How a note appears in the rendered document.</summary>
internal enum NoteStyle {
    /// <summary>A <c>role="note"</c> element with the "user note" role description.</summary>
    Note,

    /// <summary>A button reading "Note: …", which JAWS's B key finds.</summary>
    Button,
}
