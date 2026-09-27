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
/// <c>Config</c> is static, so these tests run in <see cref="LocalizationCollection"/>, the
/// collection for static app state, with every other class that changes <c>Config</c>
/// (<c>CliRunnerTests</c>) or the interface language, one at a time.
/// </remarks>
[Collection(LocalizationCollection.Name)]
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

        // Values that differ from the defaults, so a Load that kept them would be caught.
        Config.General.Language = "fr";
        Config.General.ShowNotesList = false;
        Config.Notes.OpeningMarker = "<<";
        Config.Notes.ClosingMarker = ">>";
        Config.Advanced.ConvertToUtf8 = true;

        // Hold the file open with no sharing: Load must survive an IO failure rather than
        // taking the whole startup path down with it.
        using var locked = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None);

        Config.Load();

        ShouldHaveTheDefaults();
    }

    [Fact]
    public void Save_ReplacesTheFileWholeAndLeavesNoTemporaryFileBehind() {
        Config.Load();
        Config.General.Language = "de";

        Config.Save().Should().BeTrue();

        Directory.GetFiles(_tempFolder).Should().Equal(FilePath);
        File.ReadAllText(FilePath).Should().Contain("Language=de");
    }

    [Fact]
    public void SaveLanguage_KeepsWhatAnotherWindowSavedSince() {
        // This window read the settings...
        Config.Load();
        var ownCopy = Config.Notes.OpeningMarker;

        // ...then another window (another process with its own copy) changed and saved others.
        WriteFile(
            "[General]\nLanguage = System\nConfirmNoteDelete = False\n"
            + "[Notes]\nOpeningMarker = <<\nClosingMarker = >>\n[Advanced]\nConvertToUtf8 = True\n"
        );
        Config.Notes.OpeningMarker.Should().Be(ownCopy);

        Config.SaveLanguage("uk").Should().BeTrue();

        Config.General.Language.Should().Be("uk");
        Config.Notes.OpeningMarker.Should().Be("<<");

        Config.Load();
        Config.General.Language.Should().Be("uk");
        Config.General.ConfirmNoteDelete.Should().BeFalse();
        Config.Notes.OpeningMarker.Should().Be("<<");
        Config.Notes.ClosingMarker.Should().Be(">>");
        Config.Advanced.ConvertToUtf8.Should().BeTrue();
    }

    [Fact]
    public void ReloadIfChanged_ReadsWhatAnotherWindowSavedOnce() {
        Config.Load();
        Config.ReloadIfChanged().Should().BeFalse();

        // Another window (another process with its own copy) saves new markers.
        WriteFile("[Notes]\nOpeningMarker = <<\nClosingMarker = >>\n");
        File.SetLastWriteTimeUtc(FilePath, DateTime.UtcNow.AddMinutes(1));

        Config.ReloadIfChanged().Should().BeTrue();
        Config.Notes.OpeningMarker.Should().Be("<<");
        Config.Notes.ClosingMarker.Should().Be(">>");
        Config.ReloadIfChanged().Should().BeFalse();
    }

    [Fact]
    public void ReloadIfChanged_AfterItsOwnSave_ReadsNothing() {
        Config.Load();
        Config.Notes.OpeningMarker = "<<";
        Config.Notes.ClosingMarker = ">>";

        Config.Save().Should().BeTrue();

        Config.ReloadIfChanged().Should().BeFalse();
        Config.Notes.OpeningMarker.Should().Be("<<");
    }

    [Fact]
    public void ReloadIfChanged_WhenTheFileCannotBeRead_KeepsTheSettingsAndTriesAgain() {
        WriteFile("[Notes]\nOpeningMarker = <<\nClosingMarker = >>\n[General]\nLanguage = fr\n");
        Config.Load();

        // Another window saves; the file is then held by another program (an antivirus scan).
        WriteFile("[Notes]\nOpeningMarker = {{\nClosingMarker = }}\n[General]\nLanguage = fr\n");
        File.SetLastWriteTimeUtc(FilePath, DateTime.UtcNow.AddMinutes(1));

        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None)) {
            Config.ReloadIfChanged().Should().BeFalse();
        }

        Config.Notes.OpeningMarker.Should().Be("<<");
        Config.Notes.ClosingMarker.Should().Be(">>");
        Config.General.Language.Should().Be("fr");

        // Released: the next check reads it.
        Config.ReloadIfChanged().Should().BeTrue();
        Config.Notes.OpeningMarker.Should().Be("{{");
        Config.Notes.ClosingMarker.Should().Be("}}");
    }

    [Fact]
    public void Reload_OnAMalformedFile_KeepsTheSettingsInMemory() {
        WriteFile("[Notes]\nOpeningMarker = <<\nClosingMarker = >>\n[Advanced]\nConvertToUtf8 = True\n");
        Config.Load();

        WriteFile(UnparsableFile);

        Config.Reload().Should().BeFalse();

        Config.Notes.OpeningMarker.Should().Be("<<");
        Config.Notes.ClosingMarker.Should().Be(">>");
        Config.Advanced.ConvertToUtf8.Should().BeTrue();
    }

    [Fact]
    public void Load_AtStartupOnAMalformedFile_UsesTheDefaults() {
        WriteFile(UnparsableFile);
        Config.Notes.OpeningMarker = "<<";
        Config.Notes.ClosingMarker = ">>";

        Config.Load();

        ShouldHaveTheDefaults();
        File.ReadAllText(FilePath).Should().Be(UnparsableFile);
    }

    [Fact]
    public void SaveLanguage_WhenTheFileCannotBeRead_SetsTheLanguageWithoutOverwritingTheFile() {
        WriteFile("[Notes]\nOpeningMarker = <<\nClosingMarker = >>\n");
        Config.Load();

        WriteFile(UnparsableFile);

        Config.SaveLanguage("uk").Should().BeFalse();

        Config.General.Language.Should().Be("uk");
        Config.Notes.OpeningMarker.Should().Be("<<");
        File.ReadAllText(FilePath).Should().Be(UnparsableFile);
    }

    [Fact]
    public void Reload_WhenTheFileWasDeleted_WritesTheDefaultsOut() {
        WriteFile("[Notes]\nOpeningMarker = <<\nClosingMarker = >>\n");
        Config.Load();

        File.Delete(FilePath);

        Config.Reload().Should().BeTrue();

        ShouldHaveTheDefaults();
        File.Exists(FilePath).Should().BeTrue();
    }

    /// <summary>A hand edit SharpConfig rejects: a section header with no closing bracket.</summary>
    private const string UnparsableFile = "[General\nLanguage = fr\n";

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
