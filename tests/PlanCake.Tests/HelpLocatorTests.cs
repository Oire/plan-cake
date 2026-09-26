using System.Globalization;
using AwesomeAssertions;
using Oire.PlanCake.Utils;
using Xunit;
using App = Oire.PlanCake.Utils.Constants.App;

namespace Oire.PlanCake.Tests;

/// <summary>
/// F1 opens the manual in the interface language, falling back to the language without its
/// region and then to English.
/// </summary>
public class HelpLocatorTests {
    private const string Help = @"C:\PlanCake\help";

    private static readonly CultureInfo _frenchCanada = new("fr-CA");

    private static string Manual(string language) => Path.Combine(Help, language, HelpLocator.ManualFileName);

    private static string? Find(CultureInfo culture, params string[] languages) {
        var existing = languages.Select(Manual).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return HelpLocator.FindManual(Help, culture, existing.Contains);
    }

    [Fact]
    public void FindManual_PrefersTheExactCulture() =>
        Find(_frenchCanada, "fr-CA", "fr", "en").Should().Be(Manual("fr-CA"));

    [Fact]
    public void FindManual_FallsBackToTheTwoLetterLanguage() =>
        Find(_frenchCanada, "fr", "en").Should().Be(Manual("fr"));

    [Fact]
    public void FindManual_FallsBackToEnglish() =>
        Find(_frenchCanada, "en", "de").Should().Be(Manual("en"));

    [Fact]
    public void FindManual_WithNoManual_IsNull() =>
        Find(_frenchCanada, "de").Should().BeNull();

    [Fact]
    public void FindManual_WithNoHelpFolder_IsNull() =>
        HelpLocator.FindManual(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), _frenchCanada)
            .Should().BeNull();

    [Fact]
    public void TheEnglishManual_IsCopiedToTheOutput() =>
        File.Exists(Path.Combine(App.HelpFolder, HelpLocator.FallbackLanguage, HelpLocator.ManualFileName))
            .Should().BeTrue();

    [Fact]
    public void Candidates_ForANeutralCulture_LookInItsFolderThenEnglish() =>
        HelpLocator.Candidates(Help, new CultureInfo("he")).Should().Equal(Manual("he"), Manual("en"));

    [Fact]
    public void Candidates_ForEnglish_HaveNoRepeats() =>
        HelpLocator.Candidates(Help, new CultureInfo("en-US")).Should().Equal(Manual("en-US"), Manual("en"));
}
