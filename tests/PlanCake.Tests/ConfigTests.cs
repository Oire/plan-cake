using AwesomeAssertions;
using Oire.PlanCake.Notes;
using Oire.PlanCake.Utils;
using Oire.PlanCake.Utils.Constants;
using Oire.PlanCake.Utils.Enums;
using Xunit;

namespace Oire.PlanCake.Tests;

/// <summary>
/// Round-trips the configuration file through a temp directory. <c>Config.OverrideFilePath</c>
/// is an <c>InternalsVisibleTo</c> hook that exists for exactly this: without it these tests
/// would read and overwrite the developer's real settings under <c>%APPDATA%</c>.
/// </summary>
/// <remarks>
/// <c>Config</c> is static, so these tests must not run in parallel with each other. xUnit
/// serializes tests within a single class by default, which is enough here; a second class
/// touching <c>Config</c> would need a shared collection fixture.
/// </remarks>
public class ConfigTests: IDisposable {
    private readonly string _tempFolder;

    public ConfigTests() {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"PlanCake.Tests-{Guid.NewGuid():N}");
        Config.OverrideFilePath = Path.Combine(_tempFolder, $"{App.Name}.{App.ConfigFileExtension}");
    }

    public void Dispose() {
        Config.OverrideFilePath = null;

        if (Directory.Exists(_tempFolder)) {
            Directory.Delete(_tempFolder, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private static string FilePath => Config.OverrideFilePath!;

    private void WriteFile(string text) {
        Directory.CreateDirectory(_tempFolder);
        File.WriteAllText(FilePath, text);
    }

    [Fact]
    public void Load_OnFirstRun_WritesTheDefaultsToDisk() {
        Config.Load();

        File.Exists(FilePath).Should().BeTrue();
        ShouldHaveTheDefaults();

        var text = File.ReadAllText(FilePath);
        text.Should().Contain("[General]").And.Contain("[Notes]").And.Contain("[Advanced]");
    }

    [Fact]
    public void Load_ThenLoadAgain_KeepsTheDefaults() {
        Config.Load();
        Config.Load();

        ShouldHaveTheDefaults();
    }

    [Fact]
    public void Defaults_MatchThePlan() {
        var general = new Config.SectionGeneral();
        var notes = new Config.SectionNotes();
        var advanced = new Config.SectionAdvanced();

        general.Language.Should().Be(App.SystemLanguageName);
        general.DefaultDocumentLanguage.Should().Be("en");
        general.ConfirmNoteDelete.Should().BeTrue();
        general.ConfirmTaskToggle.Should().BeTrue();
        general.ExternalChangeAction.Should().Be(ExternalChangeAction.AutoReload);
        general.ShowNotesList.Should().BeTrue();
        general.CheckForUpdatesOnStartup.Should().BeTrue();
        general.UpdateCheckInterval.Should().Be(UpdateCheckInterval.Weekly);
        notes.OpeningMarker.Should().Be("[usernote]");
        notes.ClosingMarker.Should().Be("[/usernote]");
        notes.BlockEnterAction.Should().Be(BlockEnterAction.AddNote);
        notes.NoteEnterAction.Should().Be(NoteEnterAction.Save);
        notes.ToMarkers().Should().Be(NoteMarkers.Default);
        advanced.ConvertToUtf8.Should().BeFalse();
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsEveryChangedValue() {
        Config.Load();
        Config.General.Language = "fr";
        Config.General.DefaultDocumentLanguage = "he";
        Config.General.ConfirmNoteDelete = false;
        Config.General.ConfirmTaskToggle = false;
        Config.General.ExternalChangeAction = ExternalChangeAction.Ask;
        Config.General.ShowNotesList = false;
        Config.General.CheckForUpdatesOnStartup = false;
        Config.General.UpdateCheckInterval = UpdateCheckInterval.Never;
        Config.Notes.OpeningMarker = "<<note>>";
        Config.Notes.ClosingMarker = "<</note>>";
        Config.Notes.BlockEnterAction = BlockEnterAction.ContextMenu;
        Config.Notes.NoteEnterAction = NoteEnterAction.NewLine;
        Config.Advanced.ConvertToUtf8 = true;
        Config.Save().Should().BeTrue();

        Config.Load();

        Config.General.Language.Should().Be("fr");
        Config.General.DefaultDocumentLanguage.Should().Be("he");
        Config.General.ConfirmNoteDelete.Should().BeFalse();
        Config.General.ConfirmTaskToggle.Should().BeFalse();
        Config.General.ExternalChangeAction.Should().Be(ExternalChangeAction.Ask);
        Config.General.ShowNotesList.Should().BeFalse();
        Config.General.CheckForUpdatesOnStartup.Should().BeFalse();
        Config.General.UpdateCheckInterval.Should().Be(UpdateCheckInterval.Never);
        Config.Notes.OpeningMarker.Should().Be("<<note>>");
        Config.Notes.ClosingMarker.Should().Be("<</note>>");
        Config.Notes.BlockEnterAction.Should().Be(BlockEnterAction.ContextMenu);
        Config.Notes.NoteEnterAction.Should().Be(NoteEnterAction.NewLine);
        Config.Advanced.ConvertToUtf8.Should().BeTrue();
    }

    [Theory]
    [InlineData("!USERNOTE!", "")]
    [InlineData("#note#", "#/note#")]
    [InlineData(";note;", ";/note;")]
    [InlineData("{note}", "{/note}")]
    [InlineData("a\"note", "b\"note")] // A double quote inside a marker; around one, see CanStore.
    [InlineData("[заметка]", "[/заметка]")]
    [InlineData("a = b", "c = d")]
    public void Save_ThenLoad_RoundTripsMarkersWithIniSpecialCharacters(string opening, string closing) {
        Config.Load();
        Config.Notes.OpeningMarker = opening;
        Config.Notes.ClosingMarker = closing;
        Config.Save().Should().BeTrue();

        Config.Load();

        Config.Notes.OpeningMarker.Should().Be(opening);
        Config.Notes.ClosingMarker.Should().Be(closing);
    }

    [Theory]
    [InlineData("[usernote]", true)]
    [InlineData("a\"b", true)]
    [InlineData("\"note", false)]
    [InlineData("note\"", false)]
    public void CanStore_RefusesValuesThatStartOrEndWithADoubleQuote(string value, bool expected) =>
        Config.CanStore(value).Should().Be(expected);

    [Fact]
    public void Load_OnAnUnreadableFile_FallsBackToTheDefaultsWithoutThrowing() {
        WriteFile("[General]\nLanguage = fr-FR\n");

        // Hold the file open with no sharing: Load must survive an IO failure rather than
        // taking the whole startup path down with it.
        using var locked = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None);

        Config.Load();

        Config.General.Language.Should().Be(App.SystemLanguageName);
    }

    [Fact]
    public void Load_WithAnInvalidEnum_FallsBackToItsDefaultAndKeepsTheOtherSettings() {
        WriteFile(
            "[General]\nLanguage = ru\nExternalChangeAction = Sometimes\nConfirmNoteDelete = False\n"
            + "[Notes]\nBlockEnterAction = 42\nNoteEnterAction = NewLine\n"
        );

        Config.Load();

        Config.General.ExternalChangeAction.Should().Be(ExternalChangeAction.AutoReload);
        Config.Notes.BlockEnterAction.Should().Be(BlockEnterAction.AddNote);
        Config.General.Language.Should().Be("ru");
        Config.General.ConfirmNoteDelete.Should().BeFalse();
        Config.Notes.NoteEnterAction.Should().Be(NoteEnterAction.NewLine);
    }

    [Theory]
    [InlineData("Hourly")]
    [InlineData("99")]
    [InlineData("")]
    public void Load_WithAnInvalidUpdateCheckInterval_FallsBackToWeekly(string interval) {
        WriteFile($"[General]\nUpdateCheckInterval = {interval}\nCheckForUpdatesOnStartup = False\n");

        Config.Load();

        Config.General.UpdateCheckInterval.Should().Be(UpdateCheckInterval.Weekly);
        Config.General.CheckForUpdatesOnStartup.Should().BeFalse();
    }

    [Fact]
    public void Load_WithAnUnreadableBoolean_FallsBackToItsDefault() {
        WriteFile("[General]\nConfirmTaskToggle = perhaps\nShowNotesList = False\n");

        Config.Load();

        Config.General.ConfirmTaskToggle.Should().BeTrue();
        Config.General.ShowNotesList.Should().BeFalse();
    }

    [Theory]
    [InlineData("", "[/usernote]")] // No opening marker.
    [InlineData("[same]", "[same]")] // Closing same as opening.
    public void Load_WithUnusableMarkers_FallsBackToTheDefaultPair(string opening, string closing) {
        WriteFile($"[Notes]\nOpeningMarker = {opening}\nClosingMarker = {closing}\nNoteEnterAction = NewLine\n");

        Config.Load();

        Config.Notes.ToMarkers().Should().Be(NoteMarkers.Default);
        Config.Notes.NoteEnterAction.Should().Be(NoteEnterAction.NewLine);
    }

    [Fact]
    public void Load_WithOnlyAClosingMarkerMissing_KeepsSingleTokenMode() {
        WriteFile("[Notes]\nOpeningMarker = !USERNOTE!\nClosingMarker =\n");

        Config.Load();

        Config.Notes.ToMarkers().Should().Be(new NoteMarkers("!USERNOTE!"));
        Config.Notes.ToMarkers().IsSingleToken.Should().BeTrue();
    }

    [Theory]
    [InlineData("xx")]
    [InlineData("Klingon")]
    [InlineData("")]
    [InlineData("ja")] // A real language, but not one of the six a document can be read in.
    public void Load_WithAnUnknownDocumentLanguage_FallsBackToEnglish(string language) {
        WriteFile($"[General]\nDefaultDocumentLanguage = {language}\nConfirmNoteDelete = False\n");

        Config.Load();

        Config.General.DefaultDocumentLanguage.Should().Be("en");
        Config.General.ConfirmNoteDelete.Should().BeFalse();
    }

    [Fact]
    public void Load_NormalizesTheCaseOfADocumentLanguage() {
        WriteFile("[General]\nDefaultDocumentLanguage = UK\n");

        Config.Load();

        Config.General.DefaultDocumentLanguage.Should().Be("uk");
    }

    [Theory]
    [InlineData("not-a-culture-at-all", "System")]
    [InlineData("system", "System")]
    [InlineData("de", "de")]
    public void Load_WithAnUnknownInterfaceLanguage_FallsBackToTheSystemLanguage(string language, string expected) {
        WriteFile($"[General]\nLanguage = {language}\n");

        Config.Load();

        Config.General.Language.Should().Be(expected);
    }

    private static void ShouldHaveTheDefaults() {
        Config.General.Language.Should().Be(App.SystemLanguageName);
        Config.General.DefaultDocumentLanguage.Should().Be("en");
        Config.General.ConfirmNoteDelete.Should().BeTrue();
        Config.General.ConfirmTaskToggle.Should().BeTrue();
        Config.General.ExternalChangeAction.Should().Be(ExternalChangeAction.AutoReload);
        Config.General.ShowNotesList.Should().BeTrue();
        Config.General.CheckForUpdatesOnStartup.Should().BeTrue();
        Config.General.UpdateCheckInterval.Should().Be(UpdateCheckInterval.Weekly);
        Config.Notes.ToMarkers().Should().Be(NoteMarkers.Default);
        Config.Notes.BlockEnterAction.Should().Be(BlockEnterAction.AddNote);
        Config.Notes.NoteEnterAction.Should().Be(NoteEnterAction.Save);
        Config.Advanced.ConvertToUtf8.Should().BeFalse();
    }
}
