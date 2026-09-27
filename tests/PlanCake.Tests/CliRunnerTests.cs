using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Oire.PlanCake.Cli;
using Oire.PlanCake.Utils;
using Oire.PlanCake.Utils.Constants;
using Xunit;

namespace Oire.PlanCake.Tests;

/// <summary>
/// The headless subcommands, run through <see cref="CliRunner"/> with writers in place of the
/// console. <c>Config</c> is loaded from a temp folder, never the developer's <c>%APPDATA%</c>.
/// </summary>
[Collection(LocalizationCollection.Name)]
public class CliRunnerTests: IDisposable {
    private static readonly UTF8Encoding _utf8 = new(false);

    /// <summary>
    /// The ANSI code page every run decodes with as a last resort, so that no test depends on the
    /// machine's (a CJK code page decodes bytes that Windows-1252 leaves undefined).
    /// </summary>
    private static readonly Encoding _ansi = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;

    /// <summary>A note of every kind: before the first block, then after each kind of block.</summary>
    private const string EveryKind = """
        [usernote]At the top[/usernote]
        # Title
        [usernote]On the heading[/usernote]

        Para one.
        [usernote]On the paragraph
        second line[/usernote]

        - item
          [usernote]On the item[/usernote]

        ```
        x
        ```
        [usernote]On the code[/usernote]

        | a | b |
        |---|---|
        | 1 | 2 |
        [usernote]On the row[/usernote]

        """;

    private readonly string _folder;

    public CliRunnerTests() {
        _folder = Path.Combine(Path.GetTempPath(), $"PlanCake.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_folder);

        // Loading from an empty temp folder gives the defaults, whatever another test left.
        Config.OverrideFilePath = Path.Combine(_folder, "config", $"{App.Name}.{App.ConfigFileExtension}");
        Config.Load();
        Localization.SetLanguage("en-US");
    }

    public void Dispose() {
        Config.OverrideFilePath = null;

        if (Directory.Exists(_folder)) {
            Directory.Delete(_folder, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private string Write(string text, string name = "plan.md") => WriteBytes(_utf8.GetBytes(text), name);

    private string WriteBytes(byte[] bytes, string name = "plan.md") {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, bytes);

        return path;
    }

    private static (int ExitCode, string Output, string Error) Run(params string[] args) {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = new CliRunner(output, error, _ansi).Run(args);

        return (exitCode, output.ToString(), error.ToString());
    }

    /// <summary>Runs with the output written as UTF-8 bytes, the way a redirected standard output gets it.</summary>
    private static (int ExitCode, byte[] Output) RunToBytes(params string[] args) {
        using var stream = new MemoryStream();
        using var output = new StreamWriter(stream, _utf8);
        using var error = new StringWriter();
        var exitCode = new CliRunner(output, error, _ansi).Run(args);
        output.Flush();

        return (exitCode, stream.ToArray());
    }

    // The window or the command line

    [Theory]
    [InlineData(new string[0], null)]
    [InlineData(new[] { "plan.md" }, "plan.md")]
    [InlineData(new[] { @"C:\plans\my plan.md" }, @"C:\plans\my plan.md")]
    [InlineData(new[] { "@plan.md" }, "@plan.md")]
    [InlineData(new[] { @".\-draft.md" }, @".\-draft.md")]
    public void OpensWindow_WithoutASubcommand_GivesTheFile(string[] args, string? expected) {
        CliRunner.OpensWindow(args, out var file).Should().BeTrue();
        file.Should().Be(expected);
    }

    [Theory]
    [InlineData("list", "plan.md")]
    [InlineData("check", "plan.md")]
    [InlineData("clear", "plan.md")]
    [InlineData("export", "plan.md", "-o", "plan.html")]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("--version")]
    [InlineData("--bogus")]
    [InlineData("-draft.md")]
    [InlineData("/?")]
    [InlineData("plan.md", "other.md")]
    [InlineData("list")]
    [InlineData("export", "plan.md")]
    public void OpensWindow_WithASubcommandHelpVersionOrAParseError_IsFalse(params string[] args) {
        CliRunner.OpensWindow(args, out var file).Should().BeFalse();
        file.Should().BeNull();
    }

    [Theory]
    [InlineData("--bogus")]
    [InlineData("list", "plan.md", "--bogus")]
    [InlineData("list")]
    [InlineData("export", "plan.md")]
    [InlineData("plan.md", "other.md")]
    public void Run_ParseError_IsReportedOnStandardErrorWithTheErrorCode(params string[] args) {
        var (exitCode, _, error) = Run(args);

        exitCode.Should().Be(ExitCode.Error);
        error.Should().NotBeEmpty();
    }

    [Fact]
    public void Run_Help_PrintsTheSubcommands() {
        var (exitCode, output, _) = Run("--help");

        exitCode.Should().Be(ExitCode.Success);
        output.Should().Contain("list").And.Contain("check").And.Contain("clear").And.Contain("export");
    }

    // list

    [Fact]
    public void List_Text_GivesEveryNoteWithItsBlock() {
        var path = Write(EveryKind);

        var (exitCode, output, error) = Run("list", path);

        exitCode.Should().Be(ExitCode.Success);
        error.Should().BeEmpty();
        output.Should().Be(
            """
            1-1 after 0-0 "": At the top
            3-3 after 2-2 "Title": On the heading
            6-7 after 5-5 "Para one.": On the paragraph / second line
            10-10 after 9-9 "item": On the item
            15-15 after 12-14 "x": On the code
            20-20 after 19-19 "1 | 2": On the row

            """
        );
    }

    [Fact]
    public void List_Json_GivesEveryNoteWithItsBlock() {
        var path = Write(EveryKind);

        var (exitCode, output, _) = Run("list", path, "--json");

        exitCode.Should().Be(ExitCode.Success);
        output.Should().EndWith("]\n").And.NotContain("\r");

        using var json = JsonDocument.Parse(output);
        var notes = json.RootElement.EnumerateArray().ToList();
        notes.Select(note => note.GetProperty("blockKind").GetString())
            .Should().Equal("start", "heading", "paragraph", "listItem", "code", "tableRow");

        notes[0].EnumerateObject().Select(property => property.Name).Should().Equal(
            "noteStartLine", "noteEndLine", "blockStartLine", "blockEndLine", "blockKind", "blockExcerpt", "text"
        );
        notes[0].GetProperty("blockStartLine").GetInt32().Should().Be(0);
        notes[0].GetProperty("blockExcerpt").GetString().Should().BeEmpty();

        var paragraph = notes[2];
        paragraph.GetProperty("noteStartLine").GetInt32().Should().Be(6);
        paragraph.GetProperty("noteEndLine").GetInt32().Should().Be(7);
        paragraph.GetProperty("blockStartLine").GetInt32().Should().Be(5);
        paragraph.GetProperty("blockEndLine").GetInt32().Should().Be(5);
        paragraph.GetProperty("blockExcerpt").GetString().Should().Be("Para one.");
        paragraph.GetProperty("text").GetString().Should().Be("On the paragraph\nsecond line");
    }

    [Fact]
    public void List_Json_KeepsCyrillicAndHebrewNotesAsUtf8() {
        const string Cyrillic = "Проверить «кавычки» и ё";
        const string Hebrew = "לבדוק את הכיוון";
        var path = Write($"Первый абзац.\n[usernote]{Cyrillic}[/usernote]\n\nפסקה שנייה.\n[usernote]{Hebrew}[/usernote]\n");

        var (exitCode, bytes) = RunToBytes("list", path, "--json");

        exitCode.Should().Be(ExitCode.Success);
        bytes.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
        ContainsBytes(bytes, _utf8.GetBytes(Cyrillic)).Should().BeTrue();
        ContainsBytes(bytes, _utf8.GetBytes(Hebrew)).Should().BeTrue();
        ContainsBytes(bytes, _utf8.GetBytes("Первый абзац.")).Should().BeTrue();

        var texts = JsonDocument.Parse(bytes).RootElement.EnumerateArray()
            .Select(note => note.GetProperty("text").GetString());
        texts.Should().Equal(Cyrillic, Hebrew);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void List_WithOutputFile_WritesTheSameBytesAsTheStandardOutput(bool json) {
        var path = Write("Абзац.\n[usernote]Заметка\nи вторая строка[/usernote]\n\nעוד.\n[usernote]הערה[/usernote]\n");
        var outPath = Path.Combine(_folder, json ? "notes.json" : "notes.txt");
        string[] format = json ? ["--json"] : [];

        var (_, printed) = RunToBytes(["list", path, .. format]);
        var (exitCode, output, error) = Run(["list", path, .. format, "-o", outPath]);

        exitCode.Should().Be(ExitCode.Success);
        output.Should().BeEmpty();
        error.Should().BeEmpty();
        File.ReadAllBytes(outPath).Should().Equal(printed);
    }

    [Fact]
    public void List_WithoutNotes_PrintsNothingOrAnEmptyArray() {
        var path = Write("# Plan\n\nNo notes here.\n");

        Run("list", path).Should().Be((ExitCode.Success, String.Empty, String.Empty));
        Run("list", path, "--json").Should().Be((ExitCode.Success, "[]\n", String.Empty));
    }

    [Fact]
    public void List_UnterminatedNote_IsListedWithAWarning() {
        var path = Write("Text\n[usernote]Never closed\n\nMore\n");

        var (exitCode, output, error) = Run("list", path);

        exitCode.Should().Be(ExitCode.Success);
        output.Should().StartWith("2-");
        error.Should().Contain("line 2");
    }

    [Fact]
    public void List_OutputFileIsTheFileItself_IsRefused() {
        var path = Write(EveryKind);

        var (exitCode, _, error) = Run("list", path, "-o", path);

        exitCode.Should().Be(ExitCode.Error);
        error.Should().NotBeEmpty();
        File.ReadAllText(path).Should().Be(EveryKind);
    }

    // --single-token and the markers

    [Fact]
    public void List_SingleToken_ReadsNotesToTheEndOfTheLine() {
        var path = Write("Para.\n!USERNOTE! one note\n\nOther.\n!USERNOTE! another\n");

        var (exitCode, output, _) = Run("list", path, "--open-marker", "!USERNOTE!", "--single-token");

        exitCode.Should().Be(ExitCode.Success);
        output.Should().Be("2-2 after 1-1 \"Para.\": one note\n5-5 after 4-4 \"Other.\": another\n");

        // In paired mode with the same opening marker, the first note runs to the end of the file.
        var (_, paired, _) = Run("list", path, "--open-marker", "!USERNOTE!");
        paired.Should().NotBe(output);
    }

    [Fact]
    public void List_MarkerOptions_OverrideTheSettings() {
        var path = Write("Para.\n<<one>>\n\n[usernote]not a note here[/usernote]\n");

        var (exitCode, output, _) = Run("list", path, "--open-marker", "<<", "--close-marker", ">>");

        exitCode.Should().Be(ExitCode.Success);
        output.Should().Be("2-2 after 1-1 \"Para.\": one\n");
    }

    [Fact]
    public void Check_MarkerOptions_OverrideTheSettings() {
        // Two notes with these markers, one with the default ones: only the options give 2.
        var path = Write("Para.\n<<one>>\n\nOther.\n<<two>>\n\n[usernote]not a note here[/usernote]\n");

        var (exitCode, output, _) = Run("check", path, "--open-marker", "<<", "--close-marker", ">>");

        exitCode.Should().Be(ExitCode.NotesRemain);
        output.Should().Be("2 notes\n");
    }

    [Fact]
    public void Commands_UseTheMarkersFromTheSettings() {
        Config.Notes.OpeningMarker = "%%";
        Config.Notes.ClosingMarker = String.Empty;
        var path = Write("Para.\n%% a note\n\n[usernote]not a note[/usernote]\n");

        var (_, output, _) = Run("list", path);

        output.Should().Be("2-2 after 1-1 \"Para.\": a note\n");
    }

    [Theory]
    [InlineData("--open-marker", "")]
    [InlineData("--open-marker", " [note]")]
    [InlineData("--close-marker", "[usernote]")]
    [InlineData("--close-marker", "[/note] ")]
    public void InvalidMarkerOptions_AreAnError(string option, string value) {
        var path = Write(EveryKind);

        foreach (var command in new[] { "list", "check", "clear" }) {
            var (exitCode, output, error) = Run(command, path, option, value);

            exitCode.Should().Be(ExitCode.Error);
            output.Should().BeEmpty();
            error.Should().NotBeEmpty();
        }

        File.ReadAllText(path).Should().Be(EveryKind);
    }

    [Fact]
    public void SingleTokenWithACloseMarker_IsAnError() {
        var path = Write(EveryKind);

        var (exitCode, _, error) = Run("list", path, "--single-token", "--close-marker", "[/x]");

        exitCode.Should().Be(ExitCode.Error);
        error.Should().Contain("--single-token");
    }

    // check

    [Fact]
    public void Check_WithoutNotes_ExitsWithSuccess() {
        var path = Write("# Plan\n\nNothing to see.\n");

        var (exitCode, output, _) = Run("check", path);

        exitCode.Should().Be(ExitCode.Success);
        output.Should().Contain("0");
    }

    [Fact]
    public void Check_WithNotes_ExitsWithNotesRemain() {
        var path = Write(EveryKind);

        var (exitCode, output, _) = Run("check", path);

        exitCode.Should().Be(ExitCode.NotesRemain).And.Be(3);
        output.Should().Contain("6");
    }

    // clear

    [Fact]
    public void Clear_RemovesEveryNoteAndLeavesTheRestByteIdentical() {
        const string Before = "# Title\r\n[usernote]one[/usernote]\r\n\r\nPara, keep it: [usernote]inline[/usernote] as is.\r\n  [usernote]multi\r\n  line[/usernote]\r\n\r\n- item\r\n";
        const string After = "# Title\r\n\r\nPara, keep it:  as is.\r\n\r\n- item\r\n";
        var path = WriteBytes([0xEF, 0xBB, 0xBF, .. _utf8.GetBytes(Before)]);

        var (exitCode, output, _) = Run("clear", path);

        exitCode.Should().Be(ExitCode.Success);
        output.Should().Contain("3");
        File.ReadAllBytes(path).Should().Equal([0xEF, 0xBB, 0xBF, .. _utf8.GetBytes(After)]);
        Run("check", path).ExitCode.Should().Be(ExitCode.Success);
    }

    [Fact]
    public void Clear_WithANoteWithoutClosingMarker_IsRefusedAndNeverWritten() {
        const string Text = "Text\n[usernote]Closed[/usernote]\n\n[usernote]Never closed\n\nMore of the plan\n";
        var path = Write(Text);

        var (exitCode, output, error) = Run("clear", path);

        exitCode.Should().Be(ExitCode.Error);
        output.Should().BeEmpty();
        error.Should().Contain("line 4").And.Contain("Nothing was removed");
        File.ReadAllText(path).Should().Be(Text);
    }

    [Fact]
    public void Clear_WithoutNotes_LeavesTheFileAlone() {
        var path = Write("# Plan\n");
        var written = File.GetLastWriteTimeUtc(path);

        var (exitCode, output, _) = Run("clear", path);

        exitCode.Should().Be(ExitCode.Success);
        output.Should().Contain("0");
        File.ReadAllText(path).Should().Be("# Plan\n");
        File.GetLastWriteTimeUtc(path).Should().Be(written);
    }

    [Fact]
    public void Clear_FileNotInUtf8_IsRefusedAndNeverWritten() {
        Config.General.DefaultDocumentLanguage = "ru";
        var windows1251 = CodePagesEncodingProvider.Instance.GetEncoding(1251)!;
        var bytes = windows1251.GetBytes("Привет, мир.\n[usernote]Заметка[/usernote]\n");
        var path = WriteBytes(bytes);

        var (exitCode, _, error) = Run("clear", path);

        exitCode.Should().Be(ExitCode.Error);
        error.Should().Contain("Windows-1251");
        File.ReadAllBytes(path).Should().Equal(bytes);
    }

    [Fact]
    public void Clear_UnrecognizedEncoding_IsRefusedAndNeverWritten() {
        Config.Advanced.ConvertToUtf8 = true;
        byte[] bytes = [.. _utf8.GetBytes("Text\n[usernote]note[/usernote]\n"), 0x81, 0x8D, 0x8F, 0x90, 0x9D, 0x98, (byte)'\n'];
        var path = WriteBytes(bytes);

        var (exitCode, _, error) = Run("clear", path);

        exitCode.Should().Be(ExitCode.Error);
        error.Should().NotBeEmpty();
        File.ReadAllBytes(path).Should().Equal(bytes);
    }

    // export

    [Fact]
    public void Output_ThatNamesTheFileItselfAnotherWay_IsRefusedAndNeverWritten() {
        var path = Write(EveryKind);

        foreach (var output in new[] { path, @"\\?\" + path, Path.Combine(_folder, ".", "PLAN.MD") }) {
            var (exportCode, _, exportError) = Run("export", path, "-o", output);
            var (listCode, _, listError) = Run("list", path, "-o", output);

            exportCode.Should().Be(ExitCode.Error, output);
            exportError.Should().Contain("cannot be the file itself");
            listCode.Should().Be(ExitCode.Error, output);
            listError.Should().Contain("cannot be the file itself");
        }

        File.ReadAllText(path).Should().Be(EveryKind);
    }

    [Fact]
    public void Export_WritesStandaloneHtmlWithNotesInTheDocumentLanguage() {
        Config.General.DefaultDocumentLanguage = "fr";
        var path = Write("# Le plan\n\nTexte.\n[usernote]Une *note*[/usernote]\n");
        var outPath = Path.Combine(_folder, "plan.html");

        var (exitCode, output, error) = Run("export", path, "-o", outPath);

        exitCode.Should().Be(ExitCode.Success);
        output.Should().BeEmpty();
        error.Should().BeEmpty();

        var bytes = File.ReadAllBytes(outPath);
        bytes.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
        var html = _utf8.GetString(bytes);
        html.Should().StartWith("<!DOCTYPE html>")
            .And.Contain("<html lang=\"fr\">")
            .And.Contain("<meta http-equiv=\"Content-Security-Policy\"")
            .And.Contain("<title>Le plan</title>")
            .And.Contain("role=\"region\"")
            .And.Contain("aria-roledescription=\"user note\"")
            .And.Contain("<em>note</em>")
            .And.NotContain("<script")
            .And.NotContain("[usernote]");
    }

    [Fact]
    public void Export_LangOption_OverridesTheDocumentLanguageNotTheInterfaceLanguage() {
        var path = Write("Text.\n");
        var outPath = Path.Combine(_folder, "plan.html");

        Run("export", path, "-o", outPath, "--lang", "he").ExitCode.Should().Be(ExitCode.Success);
        File.ReadAllText(outPath).Should().Contain("<html lang=\"he\">");

        Run("export", path, "-o", outPath).ExitCode.Should().Be(ExitCode.Success);
        File.ReadAllText(outPath).Should().Contain("<html lang=\"en\">").And.Contain("<title>plan.md</title>");
    }

    [Fact]
    public void Export_InvalidLang_IsAnError() {
        var path = Write("Text.\n");
        var outPath = Path.Combine(_folder, "plan.html");

        var (exitCode, _, error) = Run("export", path, "-o", outPath, "--lang", "no-such-language");

        exitCode.Should().Be(ExitCode.Error);
        error.Should().NotBeEmpty();
        File.Exists(outPath).Should().BeFalse();
    }

    [Fact]
    public void Export_OverTheFileItself_IsRefused() {
        var path = Write(EveryKind);

        var (exitCode, _, _) = Run("export", path, "-o", path);

        exitCode.Should().Be(ExitCode.Error);
        File.ReadAllText(path).Should().Be(EveryKind);
    }

    // Files that cannot be read

    [Theory]
    [InlineData("list")]
    [InlineData("check")]
    [InlineData("clear")]
    [InlineData("export")]
    public void MissingFile_IsAnError(string command) {
        var path = Path.Combine(_folder, "missing.md");
        string[] export = command == "export" ? ["-o", Path.Combine(_folder, "out.html")] : [];

        var (exitCode, output, error) = Run([command, path, .. export]);

        exitCode.Should().Be(ExitCode.Error);
        output.Should().BeEmpty();
        error.Should().Contain("missing.md");
    }

    [Fact]
    public void UnreadableFile_IsAnError() {
        var path = Write(EveryKind);

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
            var (exitCode, output, error) = Run("check", path);

            exitCode.Should().Be(ExitCode.Error);
            output.Should().BeEmpty();
            error.Should().Contain("plan.md");
        }
    }

    [Fact]
    public void UnwritableOutputFile_IsAnError() {
        var path = Write(EveryKind);
        var outPath = Path.Combine(_folder, "no-such-folder", "notes.json");

        var (exitCode, _, error) = Run("list", path, "--json", "-o", outPath);

        exitCode.Should().Be(ExitCode.Error);
        error.Should().Contain("notes.json");
    }

    private static bool ContainsBytes(byte[] haystack, byte[] needle) =>
        haystack.AsSpan().IndexOf(needle) >= 0;
}
