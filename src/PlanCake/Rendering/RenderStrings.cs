namespace Oire.PlanCake.Rendering;

/// <summary>
/// The localized strings the renderer writes into the document. The caller translates them, so
/// the renderer itself never touches the gettext catalog.
/// </summary>
/// <param name="NoteLabel">The name of every note's region: "User note".</param>
/// <param name="TaskLabel">The name of every task-list check box: "Done".</param>
internal sealed record RenderStrings(string NoteLabel, string TaskLabel);
