using Oire.PlanCake.Notes;
using Oire.PlanCake.Rendering;
using static Oire.PlanCake.Utils.Localization;

namespace Oire.PlanCake.Utils;

/// <summary>
/// Texts in the interface language that the window and the command line both show, kept in one
/// place so the two cannot drift apart. <c>Notes/</c> and <c>Rendering/</c> stay free of the
/// gettext catalog; their callers translate through here.
/// </summary>
internal static class LocalizedText {
    /// <summary>Why the markers cannot delimit a note; <see langword="null"/> for <see cref="NoteMarkersError.None"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">An error this method does not know.</exception>
    public static string? MarkersError(NoteMarkersError error) => error switch {
        NoteMarkersError.None => null,
        NoteMarkersError.EmptyOpening => _("The opening marker cannot be empty."),
        NoteMarkersError.SurroundingWhitespace => _("A marker cannot start or end with a space."),
        NoteMarkersError.LineBreak => _("A marker cannot contain a line break."),
        NoteMarkersError.ClosingSameAsOpening => _("The closing marker must differ from the opening marker."),
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, null),
    };

    /// <summary>The strings the renderer writes into the document: the name of a note's region.</summary>
    public static RenderStrings RenderStrings() => new(_("User note"));
}
