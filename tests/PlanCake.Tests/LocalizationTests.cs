using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using AwesomeAssertions;
using GetText;
using Oire.PlanCake.Utils;
using Oire.PlanCake.Utils.Constants;
using Xunit;

namespace Oire.PlanCake.Tests;

[Collection(LocalizationCollection.Name)]
public class LocalizationTests: IDisposable {
    public void Dispose() {
        Localization.SetLanguage("en-US");
        GC.SuppressFinalize(this);
    }

    /// <summary>The languages PlanCake ships a catalog for: every supported one but English, the source.</summary>
    public static TheoryData<string> ShippedLanguages() =>
        new(LanguageList.SupportedCodes.Where(code => code != LanguageList.English));

    [Fact]
    public void GetCurrentCulture_WithNoCatalogForTheLanguage_FallsBackToEnglish() {
        // No catalog ships for Japanese, so the resolver must land on en-US rather than throwing
        // or returning a culture we cannot serve.
        Localization.SetLanguage("ja-JP");

        Localization.GetCurrentCulture().Name.Should().Be("en-US");
    }

    [Fact]
    public void SetLanguage_RaisesLanguageChanged() {
        var raised = false;
        EventHandler handler = (_, _) => raised = true;

        Localization.LanguageChanged += handler;

        try {
            Localization.SetLanguage(App.SystemLanguageName);
        } finally {
            Localization.LanguageChanged -= handler;
        }

        raised.Should().BeTrue();
    }

    [Fact]
    public void Underscore_WithNoCatalog_ReturnsTheSourceString() {
        Localization.SetLanguage("en-US");

        Localization._("Unable to start the program up. Please contact the developer.")
            .Should().Be("Unable to start the program up. Please contact the developer.");
    }

    [Theory]
    [MemberData(nameof(ShippedLanguages))]
    public void ShippedCatalog_LoadsAndTranslates(string language) {
        // A regional name finds the neutral catalog, as the Windows language does.
        Localization.SetLanguage($"{language}-{RegionOf(language)}");

        Localization.GetCurrentCulture().Name.Should().Be(language);
        Localization._("&File").Should().NotBe("&File");
        Localization._n("Removed {0} note.", "Removed {0} notes.", 5, 5).Should().Contain("5").And.NotBe("Removed 5 notes.");
    }

    [Theory]
    [MemberData(nameof(ShippedLanguages))]
    public void ShippedCatalog_TranslatesEveryMessageOfTheTemplate(string language) {
        var catalog = new Catalog(App.Name, App.LocalesFolder, new CultureInfo(language));

        // The empty ID is the catalog's header, not a message.
        var messages = catalog.Translations.Where(entry => entry.Key.Length > 0).ToList();

        messages.Select(entry => entry.Key).Should()
            .BeEquivalentTo(TemplateMessageIds(), "a message the {0} catalog lacks shows in English", language);
        messages.SelectMany(entry => entry.Value).Should().NotContain(String.Empty);
    }

    [Theory]
    [InlineData(1, "1 заметка")]
    [InlineData(3, "3 заметки")]
    [InlineData(5, "5 заметок")]
    [InlineData(21, "21 заметка")]
    [InlineData(112, "112 заметок")]
    public void ShippedCatalog_UsesTheLanguagesPluralRule(int count, string expected) {
        Localization.SetLanguage("ru");

        Localization._n("{0} note", "{0} notes", count, count).Should().Be(expected);
    }

    [Fact]
    public void InterfaceLanguages_OfferEveryShippedCatalog() =>
        LanguageList.InterfaceLanguages("System default").Select(option => option.Code).Should()
            .Equal([App.SystemLanguageName, .. LanguageList.SupportedCodes]);

    private static string RegionOf(string language) => language switch {
        "uk" => "UA",
        "he" => "IL",
        "en" => "US",
        _ => language.ToUpperInvariant(),
    };

    /// <summary>
    /// The message IDs of <c>locale/messages.pot</c> in the source tree (found from this file's
    /// path, so the tests find it whatever their output folder).
    /// </summary>
    private static HashSet<string> TemplateMessageIds([CallerFilePath] string testFile = "") {
        var root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(testFile)))!;
        var template = Path.Combine(root, "src", "PlanCake", "locale", "messages.pot");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        StringBuilder? current = null;

        foreach (var line in File.ReadLines(template).Append(String.Empty)) {
            if (line.StartsWith("msgid ", StringComparison.Ordinal)) {
                current = new StringBuilder(Unquote(line["msgid ".Length..]));
            } else if (current is not null && line.StartsWith('"')) {
                current.Append(Unquote(line));
            } else if (current is not null) {
                // The header's empty ID is not a message.
                if (current.Length > 0) {
                    ids.Add(current.ToString());
                }

                current = null;
            }
        }

        ids.Should().NotBeEmpty("{0} holds the messages", template);

        return ids;
    }

    private static string Unquote(string quoted) =>
        quoted.Trim()[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\n", "\n", StringComparison.Ordinal);
}
