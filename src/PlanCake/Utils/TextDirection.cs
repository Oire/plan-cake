namespace Oire.PlanCake.Utils;

/// <summary>
/// Right-to-left support for the UI. Hebrew needs the whole window mirrored, and WinForms
/// never derives that from the culture on its own — every form asks for it explicitly.
/// </summary>
internal static class TextDirection {
    /// <summary>True when the active UI language is written right to left.</summary>
    public static bool IsRightToLeft => Localization.GetCurrentCulture().TextInfo.IsRightToLeft;

    /// <summary>
    /// Mirrors <paramref name="form"/> to match the active language, and un-mirrors it when the
    /// user switches back to a left-to-right one. Call it right after <c>Localizer.Localize</c>.
    /// <see cref="Control.RightToLeft"/> is ambient and reaches every child on its own, but
    /// <c>RightToLeftLayout</c> is not inherited, so the controls that carry their own copy of it
    /// are walked here.
    /// </summary>
    public static void Apply(Form form) {
        var rightToLeft = IsRightToLeft;
        form.RightToLeft = rightToLeft ? RightToLeft.Yes : RightToLeft.No;
        ApplyLayout(form, rightToLeft);
    }

    /// <summary>
    /// Keeps <paramref name="controls"/> left to right in a mirrored window. For boxes that hold
    /// code or a link (the note markers, a URL): read right to left, <c>[/usernote]</c> showed as
    /// <c>[usernote/]</c>. Call it after <see cref="Apply"/>.
    /// </summary>
    public static void KeepLeftToRight(params Control[] controls) {
        ArgumentNullException.ThrowIfNull(controls);

        foreach (var control in controls) {
            control.RightToLeft = RightToLeft.No;
        }
    }

    /// <summary>
    /// Text from the document, written by the user or kept in English (a block's excerpt, a note,
    /// the copyright line), to be shown in the interface. In a right-to-left interface, text that starts left to right (an English
    /// excerpt) is wrapped in LEFT-TO-RIGHT EMBEDDING (U+202A) and POP DIRECTIONAL FORMATTING
    /// (U+202C), so it keeps its own direction and its punctuation stays where it was written: its
    /// final period or ellipsis no longer jumps to its start. Text that starts right to left already
    /// runs with the interface, and a left-to-right interface reorders nothing, so they are left
    /// alone. Not the isolates of Unicode 6.3 (U+2068, U+2069): GDI, which draws labels, list
    /// views and message boxes, shows them as boxes.
    /// </summary>
    public static string Embed(string text) {
        ArgumentNullException.ThrowIfNull(text);

        return IsRightToLeft && StartsLeftToRight(text)
            ? LeftToRightEmbedding + text + PopDirectionalFormatting
            : text;
    }

    /// <summary>U+202A LEFT-TO-RIGHT EMBEDDING: what follows runs left to right.</summary>
    internal const char LeftToRightEmbedding = '\u202A';

    /// <summary>U+202C POP DIRECTIONAL FORMATTING: ends <see cref="LeftToRightEmbedding"/>.</summary>
    internal const char PopDirectionalFormatting = '\u202C';

    /// <summary>
    /// True when the first letter of <paramref name="text"/> is written left to right; false when
    /// it is Hebrew, Arabic or another right-to-left script, or when there is no letter at all.
    /// </summary>
    internal static bool StartsLeftToRight(string text) {
        foreach (var character in text) {
            if (Char.IsLetter(character)) {
                return !IsRightToLeftLetter(character);
            }
        }

        return false;
    }

    /// <summary>A letter of Hebrew, Arabic, Syriac, Thaana, N'Ko and the other right-to-left blocks.</summary>
    private static bool IsRightToLeftLetter(char letter) =>
        letter is (>= '\u0590' and <= '\u08FF') or (>= '\uFB1D' and <= '\uFDFF') or (>= '\uFE70' and <= '\uFEFF');

    private static void ApplyLayout(Control control, bool rightToLeft) {
        switch (control) {
            case Form form:
                form.RightToLeftLayout = rightToLeft;
                break;
            case ListView listView:
                listView.RightToLeftLayout = rightToLeft;
                break;
            case TabControl tabControl:
                tabControl.RightToLeftLayout = rightToLeft;
                break;
            case ProgressBar progressBar:
                progressBar.RightToLeftLayout = rightToLeft;
                break;
        }

        foreach (Control child in control.Controls) {
            ApplyLayout(child, rightToLeft);
        }
    }
}
