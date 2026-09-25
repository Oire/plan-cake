using System.Text;
using AwesomeAssertions;
using Oire.PlanCake.Notes;
using Oire.PlanCake.Rendering;
using Oire.PlanCake.Utils;
using Xunit;

namespace Oire.PlanCake.Tests;

[Collection(LocalizationCollection.Name)]
public class NoteActionRunnerTests: IDisposable {
    private static readonly RenderStrings _strings = new("user note", "unote");
    private static readonly Encoding _windows1251 = CodePagesEncodingProvider.Instance.GetEncoding(1251)!;

    private readonly string _folder;
    private readonly string _path;

    public NoteActionRunnerTests() {
        // The messages are asserted in English, the language of the source strings.
        Localization.SetLanguage("en-US");

        _folder = Path.Combine(Path.GetTempPath(), $"PlanCake.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_folder);
        _path = Path.Combine(_folder, "plan.md");
    }

    public void Dispose() {
        if (Directory.Exists(_folder)) {
            Directory.Delete(_folder, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private NoteActionRunner Runner(string text, MarkdownFileOptions? options = null) {
        File.WriteAllText(_path, text, new UTF8Encoding(false));

        return RunnerForFile(options);
    }

    private NoteActionRunner RunnerForFile(MarkdownFileOptions? options = null) => new(new NoteStore(
        MarkdownFile.Open(_path, options ?? new MarkdownFileOptions(RetryDelay: TimeSpan.FromMilliseconds(10))),
        NoteMarkers.Default
    ));

    private static RenderResult Render(string text) => MarkdownRenderer.Render(
        text,
        new RenderOptions(NoteMarkers.Default, RenderMode.Interactive, _strings)
    );

    private string OnDisk => File.ReadAllText(_path);

    // Add

    [Fact]
    public void Add_Success_AnnouncesAndFocusesTheNewNote() {
        var runner = Runner("# Title\n\nPara.\n");
        var rendered = runner.Store.File.Text;
        var block = Render(rendered).Blocks[1];

        var result = runner.Add(rendered, block, "Check this");

        result.Status.Should().Be(NoteActionStatus.Done);
        result.Message.Should().Be("Note added");
        result.FocusNoteLine.Should().Be(4);
        result.NeedsRender.Should().BeTrue();
        result.KeepsText.Should().BeFalse();
        OnDisk.Should().Be("# Title\n\nPara.\n[usernote]Check this[/usernote]\n");

        var note = Render(OnDisk).Notes.Should().ContainSingle().Subject;
        note.Note.StartLine.Should().Be(result.FocusNoteLine);
    }

    [Fact]
    public void Add_FileChangedOnDisk_WritesNothingAndKeepsTheText() {
        var runner = Runner("Para.\n");
        var rendered = runner.Store.File.Text;
        var block = Render(rendered).Blocks[0];
        File.WriteAllText(_path, "Para, changed elsewhere.\n");

        var result = runner.Add(rendered, block, "Check this");

        result.Status.Should().Be(NoteActionStatus.Stale);
        result.Message.Should().Be("The file changed. Please try again.");
        result.NeedsRender.Should().BeTrue();
        result.KeepsText.Should().BeTrue();
        OnDisk.Should().Be("Para, changed elsewhere.\n");
        runner.Store.File.Text.Should().Be("Para, changed elsewhere.\n", "the view renders the file's current text next");
    }

    [Fact]
    public void Add_LockedFile_FailsWithTheReasonAndKeepsTheText() {
        var runner = Runner("Para.\n");
        var rendered = runner.Store.File.Text;
        var block = Render(rendered).Blocks[0];
        NoteActionResult result;

        using (new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
            result = runner.Add(rendered, block, "Check this");
        }

        result.Status.Should().Be(NoteActionStatus.Failed);
        result.Message.Should().StartWith("Unable to write the file: ");
        result.NeedsRender.Should().BeFalse();
        result.KeepsText.Should().BeTrue();
        OnDisk.Should().Be("Para.\n");
    }

    [Fact]
    public void Add_ReadOnlyFile_IsRefusedWithTheReason() {
        File.WriteAllBytes(_path, _windows1251.GetBytes("Абзац.\n"));
        var runner = RunnerForFile(new MarkdownFileOptions(AnsiEncoding: _windows1251));
        var rendered = runner.Store.File.Text;
        var block = Render(rendered).Blocks[0];

        var result = runner.Add(rendered, block, "заметка");

        runner.CannotWriteReason.Should().NotBeNull();
        result.Status.Should().Be(NoteActionStatus.ReadOnly);
        result.Message.Should().Be(runner.CannotWriteReason);
        result.Message.Should().Contain("read-only");
        result.NeedsRender.Should().BeFalse();
        File.ReadAllBytes(_path).Should().Equal(_windows1251.GetBytes("Абзац.\n"));
    }

    [Fact]
    public void Delete_ReadOnlyFile_IsRefusedWithTheReason() {
        const string text = "Абзац.\n[usernote]заметка[/usernote]\n";
        File.WriteAllBytes(_path, _windows1251.GetBytes(text));
        var runner = RunnerForFile(new MarkdownFileOptions(AnsiEncoding: _windows1251));
        var note = Render(text).Notes[0].Note;

        var result = runner.Delete(text, note);

        result.Status.Should().Be(NoteActionStatus.ReadOnly);
        File.ReadAllBytes(_path).Should().Equal(_windows1251.GetBytes(text));
    }

    [Fact]
    public void Add_TextWithClosingMarker_IsRejectedWithTheReason() {
        var runner = Runner("Para.\n");
        var rendered = runner.Store.File.Text;
        var block = Render(rendered).Blocks[0];

        var result = runner.Add(rendered, block, "see [/usernote] here");

        result.Status.Should().Be(NoteActionStatus.InvalidText);
        result.Message.Should().Contain("[/usernote]");
        result.KeepsText.Should().BeTrue();
        OnDisk.Should().Be("Para.\n");
    }

    [Theory]
    [InlineData("fine", null)]
    [InlineData("  line one\r\nline two  ", null)]
    [InlineData("   ", "The note is empty.")]
    [InlineData("a [/usernote] b", "The note cannot contain [/usernote], which marks the end of a note.")]
    public void DescribeTextError_ExplainsWhyATextCannotBeWritten(string text, string? expected) {
        var runner = Runner("Para.\n");

        runner.DescribeTextError(text).Should().Be(expected);
    }

    [Fact]
    public void DescribeTextError_SingleTokenMode_RejectsTheOpeningToken() {
        File.WriteAllText(_path, "Para.\n");
        var runner = new NoteActionRunner(new NoteStore(MarkdownFile.Open(_path), new NoteMarkers("!USERNOTE!")));

        runner.DescribeTextError("a !USERNOTE! b")
            .Should().Be("The note cannot contain !USERNOTE!, which marks the start of a note.");
        runner.DescribeTextError("line one\nline two").Should().BeNull("line breaks become spaces");
    }

    // Edit and delete

    [Fact]
    public void Edit_Success_AnnouncesAndFocusesTheNote() {
        var runner = Runner("Para.\n[usernote]old[/usernote]\n");
        var rendered = runner.Store.File.Text;
        var note = Render(rendered).Notes[0].Note;

        var result = runner.Edit(rendered, note, "new");

        result.Status.Should().Be(NoteActionStatus.Done);
        result.Message.Should().Be("Note edited");
        result.FocusNoteLine.Should().Be(2);
        OnDisk.Should().Be("Para.\n[usernote]new[/usernote]\n");
    }

    [Fact]
    public void Edit_FileChangedOnDisk_WritesNothingAndKeepsTheText() {
        var runner = Runner("Para.\n[usernote]old[/usernote]\n");
        var rendered = runner.Store.File.Text;
        var note = Render(rendered).Notes[0].Note;
        File.WriteAllText(_path, "Para.\n[usernote]changed[/usernote]\n");

        var result = runner.Edit(rendered, note, "new");

        result.Status.Should().Be(NoteActionStatus.Stale);
        result.KeepsText.Should().BeTrue();
        OnDisk.Should().Be("Para.\n[usernote]changed[/usernote]\n");
    }

    [Fact]
    public void Delete_Success_AnnouncesWithoutANoteToFocus() {
        var runner = Runner("Para.\n[usernote]old[/usernote]\n");
        var rendered = runner.Store.File.Text;
        var note = Render(rendered).Notes[0].Note;

        var result = runner.Delete(rendered, note);

        result.Status.Should().Be(NoteActionStatus.Done);
        result.Message.Should().Be("Note deleted");
        result.FocusNoteLine.Should().BeNull();
        OnDisk.Should().Be("Para.\n");
    }

    // Undo and redo

    [Fact]
    public void UndoAndRedo_WithNothingToDo_WriteNothing() {
        var runner = Runner("Para.\n");

        var undo = runner.Undo();
        var redo = runner.Redo();

        undo.Status.Should().Be(NoteActionStatus.NothingToDo);
        undo.Message.Should().Be("Nothing to undo");
        undo.NeedsRender.Should().BeFalse();
        redo.Status.Should().Be(NoteActionStatus.NothingToDo);
        redo.Message.Should().Be("Nothing to redo");
    }

    [Fact]
    public void UndoAndRedo_OfAnAdd_AnnounceTheChangeAndFocusTheNoteWhenItIsBack() {
        var runner = Runner("Para.\n");
        var rendered = runner.Store.File.Text;
        runner.Add(rendered, Render(rendered).Blocks[0], "Check this");

        var undo = runner.Undo();

        undo.Status.Should().Be(NoteActionStatus.Done);
        undo.Message.Should().Be("Note added undone");
        undo.FocusNoteLine.Should().BeNull();
        OnDisk.Should().Be("Para.\n");

        var redo = runner.Redo();

        redo.Message.Should().Be("Note added redone");
        redo.FocusNoteLine.Should().Be(2);
        OnDisk.Should().Be("Para.\n[usernote]Check this[/usernote]\n");
    }

    [Fact]
    public void UndoAndRedo_OfAnEdit_FocusTheNoteBothWays() {
        var runner = Runner("Para.\n[usernote]old[/usernote]\n");
        var rendered = runner.Store.File.Text;
        runner.Edit(rendered, Render(rendered).Notes[0].Note, "new");

        var undo = runner.Undo();
        var redo = runner.Redo();

        undo.Message.Should().Be("Note edited undone");
        undo.FocusNoteLine.Should().Be(2);
        redo.Message.Should().Be("Note edited redone");
        redo.FocusNoteLine.Should().Be(2);
    }

    [Fact]
    public void UndoAndRedo_OfADelete_FocusTheNoteWhenItIsBack() {
        var runner = Runner("Para.\n[usernote]old[/usernote]\n");
        var rendered = runner.Store.File.Text;
        runner.Delete(rendered, Render(rendered).Notes[0].Note);

        var undo = runner.Undo();

        undo.Message.Should().Be("Note deleted undone");
        undo.FocusNoteLine.Should().Be(2);
        OnDisk.Should().Be("Para.\n[usernote]old[/usernote]\n");

        var redo = runner.Redo();

        redo.Message.Should().Be("Note deleted redone");
        redo.FocusNoteLine.Should().BeNull();
    }

    [Fact]
    public void Undo_AfterAnExternalChange_IsRefusedAndTheHistoryIsGone() {
        var runner = Runner("Para.\n");
        var rendered = runner.Store.File.Text;
        runner.Add(rendered, Render(rendered).Blocks[0], "Check this");
        File.WriteAllText(_path, "Changed elsewhere.\n");

        var undo = runner.Undo();

        undo.Status.Should().Be(NoteActionStatus.Stale);
        undo.Message.Should().Be("The file changed, so there is nothing more to undo or redo.");
        undo.NeedsRender.Should().BeTrue();
        OnDisk.Should().Be("Changed elsewhere.\n");
        runner.Undo().Status.Should().Be(NoteActionStatus.NothingToDo);
    }
}
