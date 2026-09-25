using System.Globalization;
using Serilog;
using App = Oire.PlanCake.Utils.Constants.App;

namespace Oire.PlanCake.Utils;

/// <summary>A language to choose from: its code and the name to show for it.</summary>
/// <param name="Code">
/// A culture name such as <c>ru</c>, or <see cref="App.SystemLanguageName"/> for the Windows language.
/// </param>
/// <param name="Name">The language's own name for itself (<c>Русский</c>), or the text for "System default".</param>
internal sealed record LanguageOption(string Code, string Name);

/// <summary>
/// The languages PlanCake offers: for the interface, the ones a catalog ships for (found in
/// <c>locale\&lt;code&gt;\</c>); for the document, the six PlanCake supports. Every language is
/// named in its own language, so a user who cannot read the current one still finds theirs.
/// </summary>
internal static class LanguageList {
    /// <summary>The source language of the catalogs, always available.</summary>
    public const string English = "en";

    /// <summary>The languages PlanCake supports, in the order the menus list them.</summary>
    public static readonly IReadOnlyList<string> SupportedCodes = [English, "ru", "uk", "fr", "he", "de"];

    /// <summary>
    /// The languages a document can be read in (the <c>lang</c> of the rendered plan, which picks
    /// the screen reader's voice): the supported six, whatever catalogs are installed.
    /// </summary>
    public static IReadOnlyList<LanguageOption> DocumentLanguages() =>
        SupportedCodes.Select(code => new LanguageOption(code, NativeName(code))).ToList();

    /// <summary>
    /// The interface languages: <paramref name="systemDefaultName"/> first (the Windows language),
    /// English, then every folder of <paramref name="localesFolder"/> that holds
    /// <c>&lt;catalogName&gt;.mo</c> and names a culture, in the order of
    /// <see cref="SupportedCodes"/> and then by name.
    /// </summary>
    public static IReadOnlyList<LanguageOption> InterfaceLanguages(
        string systemDefaultName,
        string? localesFolder = null,
        string catalogName = App.Name
    ) {
        ArgumentNullException.ThrowIfNull(systemDefaultName);

        var found = new List<LanguageOption>();
        localesFolder ??= App.LocalesFolder;

        if (Directory.Exists(localesFolder)) {
            foreach (var folder in Directory.GetDirectories(localesFolder)) {
                var code = Path.GetFileName(folder);

                if (IsEnglish(code) || !File.Exists(Path.Combine(folder, $"{catalogName}.mo"))) {
                    continue;
                }

                try {
                    found.Add(new LanguageOption(code, NativeName(code)));
                } catch (CultureNotFoundException) {
                    Log.Debug("Locale folder {Folder} does not name a culture; skipped", code);
                }
            }
        }

        var ordered = found
            .OrderBy(option => Rank(option.Code))
            .ThenBy(option => option.Name, StringComparer.CurrentCultureIgnoreCase);

        return [
            new LanguageOption(App.SystemLanguageName, systemDefaultName),
            new LanguageOption(English, NativeName(English)),
            .. ordered,
        ];
    }

    /// <summary>
    /// The option <paramref name="configured"/> (a setting's value) stands for: the same code, else
    /// the same language (<c>ru-RU</c> → <c>ru</c>); the first option (the system default) when
    /// the setting is empty or matches none.
    /// </summary>
    public static LanguageOption Find(IReadOnlyList<LanguageOption> options, string? configured) {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Count == 0) {
            throw new ArgumentException("There is no language to choose from.", nameof(options));
        }

        if (String.IsNullOrWhiteSpace(configured)) {
            return options[0];
        }

        var exact = options.FirstOrDefault(option => String.Equals(option.Code, configured, StringComparison.OrdinalIgnoreCase));

        if (exact is not null) {
            return exact;
        }

        try {
            var language = CultureInfo.GetCultureInfo(configured).TwoLetterISOLanguageName;

            return options.FirstOrDefault(option => String.Equals(option.Code, language, StringComparison.OrdinalIgnoreCase))
                ?? options[0];
        } catch (CultureNotFoundException) {
            return options[0];
        }
    }

    /// <summary>A language's name in that language, capitalized: <c>Français</c>, <c>Українська</c>.</summary>
    /// <exception cref="CultureNotFoundException"><paramref name="code"/> names no culture.</exception>
    public static string NativeName(string code) {
        var culture = CultureInfo.GetCultureInfo(code);

        return culture.TextInfo.ToTitleCase(culture.NativeName);
    }

    private static bool IsEnglish(string code) =>
        code.Equals(English, StringComparison.OrdinalIgnoreCase)
        || code.StartsWith("en-", StringComparison.OrdinalIgnoreCase);

    private static int Rank(string code) {
        for (var index = 0; index < SupportedCodes.Count; index++) {
            if (String.Equals(SupportedCodes[index], code, StringComparison.OrdinalIgnoreCase)) {
                return index;
            }
        }

        return SupportedCodes.Count;
    }
}
