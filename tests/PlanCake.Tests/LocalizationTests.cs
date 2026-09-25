using AwesomeAssertions;
using Oire.PlanCake.Utils;
using Oire.PlanCake.Utils.Constants;
using Xunit;

namespace Oire.PlanCake.Tests;

public class LocalizationTests {
    [Fact]
    public void GetCurrentCulture_WithNoCatalogForTheLanguage_FallsBackToEnglish() {
        // A clean checkout ships no compiled catalogs, so the resolver must land on en-US
        // rather than throwing or returning a culture we cannot serve.
        Localization.SetLanguage("fr-FR");

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
}
