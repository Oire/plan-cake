using System.Globalization;

namespace Oire.PlanCake.Utils;

/// <summary>
/// Finds the user manual for the interface language: <c>help\&lt;culture&gt;\manual.html</c>,
/// then <c>help\&lt;two-letter&gt;\manual.html</c>, then the English one.
/// </summary>
internal static class HelpLocator {
    public const string ManualFileName = "manual.html";

    /// <summary>The language whose manual every other one falls back to.</summary>
    public const string FallbackLanguage = "en";

    /// <summary>
    /// The paths the manual is looked for at, in order and without repeats: for <c>fr-FR</c>,
    /// <c>help\fr-FR</c>, <c>help\fr</c>, <c>help\en</c>; for <c>en-US</c>, <c>help\en-US</c> and
    /// <c>help\en</c> only.
    /// </summary>
    public static IReadOnlyList<string> Candidates(string helpFolder, CultureInfo culture) {
        string[] languages = [culture.Name, culture.TwoLetterISOLanguageName, FallbackLanguage];

        return languages
            .Where(language => !String.IsNullOrEmpty(language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(language => Path.Combine(helpFolder, language, ManualFileName))
            .ToList();
    }

    /// <summary>
    /// The first of <see cref="Candidates"/> that <paramref name="exists"/> says is there
    /// (<see cref="File.Exists"/> when not given), or <see langword="null"/> when none is.
    /// </summary>
    public static string? FindManual(string helpFolder, CultureInfo culture, Func<string, bool>? exists = null) {
        exists ??= File.Exists;

        return Candidates(helpFolder, culture).FirstOrDefault(exists);
    }
}
