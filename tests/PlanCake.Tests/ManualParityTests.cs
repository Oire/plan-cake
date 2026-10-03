using System.Net;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Oire.PlanCake.Ui;
using Oire.PlanCake.Utils;
using Xunit;

namespace Oire.PlanCake.Tests;

/// <summary>
/// The six user manuals (help/&lt;code&gt;/manual.html) are written by hand. These tests keep them
/// in step with each other and with the shortcut table: the same sections and anchors as the
/// English one, every in-page link landing somewhere, the same tables and keys, and every
/// shortcut of <see cref="HostCommands"/> in the keyboard reference, spelled as the menus spell
/// it in that language.
/// </summary>
[Collection(LocalizationCollection.Name)]
public sealed partial class ManualParityTests: IDisposable {
    public ManualParityTests() => Localization.SetLanguage("en-US");

    public void Dispose() {
        Localization.SetLanguage("en-US");
        GC.SuppressFinalize(this);
    }

    public static TheoryData<string> Translations() =>
        new(LanguageList.SupportedCodes.Where(code => code != LanguageList.English));

    public static TheoryData<string> Languages() => new(LanguageList.SupportedCodes);

    private static string Manual(string language) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "help", language, "manual.html"));

    [GeneratedRegex("\\sid=\"([^\"]+)\"")]
    private static partial Regex IdRegex();

    [GeneratedRegex("href=\"#([^\"]*)\"")]
    private static partial Regex FragmentRegex();

    [GeneratedRegex("<kbd[^>]*>(.*?)</kbd>", RegexOptions.Singleline)]
    private static partial Regex KbdRegex();

    [GeneratedRegex("<table[\\s>]")]
    private static partial Regex TableRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();

    private static HashSet<string> Ids(string html) =>
        IdRegex().Matches(html).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    [Theory]
    [MemberData(nameof(Translations))]
    public void Translation_HasTheSameIdsAsEnglish(string language) {
        var english = Ids(Manual(LanguageList.English));
        var translated = Ids(Manual(language));

        english.Except(translated).Should().BeEmpty("the {0} manual needs every section and anchor of the English one", language);
        translated.Except(english).Should().BeEmpty("the {0} manual should have no anchor the English one lacks", language);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryInPageLink_LandsOnAnId(string language) {
        var html = Manual(language);
        var ids = Ids(html);

        FragmentRegex().Matches(html)
            .Select(match => match.Groups[1].Value)
            .Where(fragment => !ids.Contains(fragment))
            .Should().BeEmpty("every #link in the {0} manual must name an id in it", language);
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void Translation_HasTheSameTablesAndKeysAsEnglish(string language) {
        var english = Manual(LanguageList.English);
        var translated = Manual(language);

        TableRegex().Count(translated).Should().Be(TableRegex().Count(english), "the {0} manual has the tables of the English one", language);
        KbdRegex().Count(translated).Should().Be(KbdRegex().Count(english), "the {0} manual names the keys of the English one", language);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void KeyboardReference_ListsEveryShortcut(string language) {
        Localization.SetLanguage(language);
        Localization.GetCurrentCulture().TwoLetterISOLanguageName.Should()
            .Be(language, "the {0} catalog must be compiled into the output (Compile-Translations.ps1)", language);

        var html = Manual(language);
        var start = html.IndexOf("<article aria-labelledby=\"keyboard\">", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the {0} manual has a keyboard reference", language);
        var reference = html[start..html.IndexOf("</article>", start, StringComparison.Ordinal)];
        var keys = KbdRegex().Matches(reference)
            .Select(match => Normalize(TagRegex().Replace(match.Groups[1].Value, String.Empty)))
            .ToHashSet(StringComparer.Ordinal);

        HostCommands.Shortcuts()
            .SelectMany(shortcut => shortcut.Keys)
            .Select(key => Normalize(HostCommands.KeyText(key)))
            .Where(text => !keys.Contains(text))
            .Should().BeEmpty("the keyboard reference of the {0} manual lists every shortcut as the menus write it", language);
    }

    private static string Normalize(string text) =>
        String.Join(' ', WebUtility.HtmlDecode(text).Replace(' ', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
