using AwesomeAssertions;
using Oire.PlanCake.Utils;
using Oire.PlanCake.Utils.Constants;
using Xunit;

namespace Oire.PlanCake.Tests;

public class LanguageListTests: IDisposable {
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"PlanCake.Tests-{Guid.NewGuid():N}");

    public LanguageListTests() => Directory.CreateDirectory(_folder);

    public void Dispose() {
        if (Directory.Exists(_folder)) {
            Directory.Delete(_folder, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private void AddCatalog(string code, string catalogName = App.Name) {
        var folder = Path.Combine(_folder, code);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, $"{catalogName}.mo"), []);
    }

    [Fact]
    public void DocumentLanguages_AreTheSixInTheirOwnNames() {
        LanguageList.DocumentLanguages().Select(option => (option.Code, option.Name)).Should().Equal(
            ("en", "English"),
            ("ru", "Русский"),
            ("uk", "Українська"),
            ("fr", "Français"),
            ("he", "עברית"),
            ("de", "Deutsch")
        );
    }

    [Fact]
    public void InterfaceLanguages_WithoutCatalogs_AreTheSystemDefaultAndEnglish() {
        var options = LanguageList.InterfaceLanguages("System default", _folder);

        options.Should().Equal(
            new LanguageOption(App.SystemLanguageName, "System default"),
            new LanguageOption("en", "English")
        );
    }

    [Fact]
    public void InterfaceLanguages_ListEveryFolderWithACatalog_InTheSupportedOrder() {
        AddCatalog("de");
        AddCatalog("ru");
        AddCatalog("he");

        var options = LanguageList.InterfaceLanguages("System default", _folder);

        options.Select(option => option.Code).Should().Equal(App.SystemLanguageName, "en", "ru", "he", "de");
        options[2].Name.Should().Be("Русский");
    }

    [Fact]
    public void InterfaceLanguages_SkipFoldersWithoutTheCatalogAndEnglishFolders() {
        AddCatalog("fr", catalogName: "SomethingElse");
        Directory.CreateDirectory(Path.Combine(_folder, "uk"));
        Directory.CreateDirectory(Path.Combine(_folder, "scripts"));
        AddCatalog("en-US");

        var options = LanguageList.InterfaceLanguages("System default", _folder);

        options.Select(option => option.Code).Should().Equal(App.SystemLanguageName, "en");
    }

    [Fact]
    public void InterfaceLanguages_WithoutTheLocaleFolder_StillOfferEnglish() {
        var options = LanguageList.InterfaceLanguages("System default", Path.Combine(_folder, "missing"));

        options.Select(option => option.Code).Should().Equal(App.SystemLanguageName, "en");
    }

    [Theory]
    [InlineData(null, "System")]
    [InlineData("", "System")]
    [InlineData("System", "System")]
    [InlineData("ru", "ru")]
    [InlineData("RU", "ru")]
    [InlineData("ru-RU", "ru")]
    [InlineData("en-US", "en")]
    [InlineData("ja", "System")] // No Japanese catalog: the Windows language, which falls back to English.
    public void Find_MatchesTheSettingToAnOption(string? configured, string expected) {
        AddCatalog("ru");
        var options = LanguageList.InterfaceLanguages("System default", _folder);

        LanguageList.Find(options, configured).Code.Should().Be(expected);
    }
}
