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

    /// <summary>
    /// Why <paramref name="file"/> is open read-only and never gets a note, or
    /// <see langword="null"/> when it can be written: a Unicode file with an invalid byte, a file
    /// that could not be converted, a file whose encoding was not recognized, or a file in a
    /// legacy encoding, which the Convert setting would convert.
    /// </summary>
    public static string? ReadOnlyReason(MarkdownFile file) {
        ArgumentNullException.ThrowIfNull(file);

        if (file.IsUnrecognized && file.InvalidByteLine is { } invalidLine) {
            return _(
                "This file is in {0} but has an invalid byte on line {1}, so it was opened read-only and is never changed. Notes cannot be added to it.",
                LegacyEncoding.DisplayName(file.Encoding), invalidLine
            );
        }

        if (file.ConversionFailed) {
            return _(
                "This file is not in UTF-8 and could not be converted, so it was opened read-only as {0}. Notes cannot be added to it.",
                LegacyEncoding.DisplayName(file.Encoding)
            );
        }

        if (file.IsUnrecognized) {
            return _(
                "The encoding of this file could not be recognized, so it was opened read-only and is never changed. Notes cannot be added to it."
            );
        }

        return file.IsReadOnly
            ? _(
                "This file is not in UTF-8, so it was opened read-only as {0}. Notes cannot be added to it. To convert it to UTF-8, turn on converting files that are not UTF-8 in the settings.",
                LegacyEncoding.DisplayName(file.Encoding)
            )
            : null;
    }

    /// <summary>The strings the renderer writes into the document: the name of a note's region.</summary>
    public static RenderStrings RenderStrings() => new(_("User note"));
}
