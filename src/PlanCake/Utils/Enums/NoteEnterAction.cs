namespace Oire.PlanCake.Utils.Enums;

/// <summary>What Enter does in the note dialog's text box; Ctrl+Enter does the other.</summary>
internal enum NoteEnterAction {
    /// <summary>Enter saves the note, Ctrl+Enter starts a new line.</summary>
    Save,

    /// <summary>Enter starts a new line, Ctrl+Enter saves the note.</summary>
    NewLine,
}
