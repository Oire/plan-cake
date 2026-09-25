namespace Oire.PlanCake.Rendering;

/// <summary>
/// The localized strings the renderer writes into the document. The caller translates them, so
/// the renderer itself never touches the gettext catalog.
/// </summary>
/// <param name="NoteRoleDescription">The role description a screen reader speaks: "user note".</param>
/// <param name="NoteBrailleRoleDescription">The role description on a braille display: "unote".</param>
internal sealed record RenderStrings(string NoteRoleDescription, string NoteBrailleRoleDescription);
