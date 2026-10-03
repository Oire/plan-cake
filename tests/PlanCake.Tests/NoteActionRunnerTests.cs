using System.Text;
using AwesomeAssertions;
using Oire.PlanCake.Notes;
using Oire.PlanCake.Rendering;
using Oire.PlanCake.Utils;
using Xunit;

namespace Oire.PlanCake.Tests;

[Collection(LocalizationCollection.Name)]
public class NoteActionRunnerTests: IDisposable {
    private static readonly RenderStrings _strings = new("User note");
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
    public void CannotWriteReason_LegacyFile_NamesItsEncodingAndTheConvertSetting() {
        File.WriteAllBytes(_path, _windows1251.GetBytes("Абзац.\n"));
        var runner = RunnerForFile(new MarkdownFileOptions(AnsiEncoding: _windows1251));

        runner.CannotWriteReason.Should().Be(
            "This file is not in UTF-8, so it was opened read-only as Windows-1251. Notes cannot be added to it. To convert it to UTF-8, turn on converting files that are not UTF-8 in the settings."
        );
    }

    [Fact]
    public void CannotWriteReason_DamagedUtf8_SaysItIsUtf8WithAnInvalidByte() {
        File.WriteAllBytes(_path, [.. new UTF8Encoding(false).GetBytes("Абзац.\nВторой абзац.\n"), 0xFF, (byte)'\n']);
        var runner = RunnerForFile(new MarkdownFileOptions(AnsiEncoding: _windows1251));

        runner.CannotWriteReason.Should().Be(
            "This file is in UTF-8 but has an invalid byte on line 3, so it was opened read-only and is never changed. Notes cannot be added to it."
        );
    }

    [Fact]
    public void CannotWriteReason_Utf8File_IsNull() =>
        Runner("Para.\n").CannotWriteReason.Should().BeNull();

    [Fact]
    public void Add_AfterAFenceThatIsNeverClosed_FailsWithTheReasonAndKeepsTheText() {
        var runner = Runner("Para.\n\n```\ncode\n");
        var rendered = runner.Store.File.Text;
        var block = Render(rendered).Blocks[^1];

        var result = runner.Add(rendered, block, "Lost");

        result.Status.Should().Be(NoteActionStatus.Failed);
        result.Message.Should().Contain("code block");
        result.KeepsText.Should().BeTrue();
        OnDisk.Should().Be("Para.\n\n```\ncode\n");
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

    [Fact]
    public void Add_FileWithTheReadOnlyAttribute_FailsWithTheReasonAndKeepsTheText() {
        var runner = Runner("Para.\n", new MarkdownFileOptions(Retries: 0));
        var rendered = runner.Store.File.Text;
        var block = Render(rendered).Blocks[0];
        File.SetAttributes(_path, FileAttributes.ReadOnly);
        NoteActionResult result;

        try {
            result = runner.Add(rendered, block, "Check this");
        } finally {
            File.SetAttributes(_path, FileAttributes.Normal);
        }

        result.Status.Should().Be(NoteActionStatus.Failed);
        result.Message.Should().StartWith("Unable to write the file: ");
        result.KeepsText.Should().BeTrue();
        OnDisk.Should().Be("Para.\n");
    }

    // A note without a closing marker

    [Fact]
    public void EditDeleteAndClear_NoteWithoutClosingMarker_AreRefusedWithTheReason() {
        const string Text = "Para.\n[usernote]Never closed\n\nMore of the plan\n";
        var runner = Runner(Text);
        var note = Render(Text).Notes[0].Note;

        NoteActionRunner.UnterminatedReason(note).Should().Contain("line 2").And.Contain("no closing marker");

        foreach (var result in new[] { runner.Edit(Text, note, "Never closed"), runner.Delete(Text, note), runner.Clear(Text) }) {
            result.Status.Should().Be(NoteActionStatus.Unterminated);
            result.Message.Should().Be(NoteActionRunner.UnterminatedReason(note));
            result.NeedsRender.Should().BeFalse();
            result.KeepsText.Should().BeFalse();
        }

        OnDisk.Should().Be(Text);
    }

    [Fact]
    public void Add_AfterANoteWithoutClosingMarker_IsRefusedWithTheReason() {
        const string Text = "# Title\n\nPara.\n[usernote]Never closed\n\nMore of the plan\n";
        var runner = Runner(Text);
        var render = Render(Text);
        var paragraph = render.Blocks[^1];

        var reason = runner.UnterminatedReason(Text, paragraph);
        var result = runner.Add(Text, paragraph, "Looks good");

        reason.Should().Contain("line 4").And.Contain("a note added here would become part of it");
        result.Status.Should().Be(NoteActionStatus.Unterminated);
        result.Message.Should().Be(reason);
        result.NeedsRender.Should().BeFalse();
        runner.UnterminatedReason(Text, render.Blocks[0]).Should().BeNull();
        OnDisk.Should().Be(Text);
    }

    [Fact]
    public void UnterminatedReason_OfAClosedNote_IsNull() =>
        NoteActionRunner.UnterminatedReason(Render("Para.\n[usernote]closed[/usernote]\n").Notes[0].Note).Should().BeNull();

    // Delete all notes

    [Fact]
    public void Clear_Success_RemovesEveryNoteAndAnnouncesTheCount() {
        var runner = Runner("# Title\n[usernote]one[/usernote]\n\nPara.\n[usernote]two[/usernote]\n");
        var rendered = runner.Store.File.Text;

        var result = runner.Clear(rendered);

        result.Status.Should().Be(NoteActionStatus.Done);
        result.Message.Should().Be("All 2 notes deleted");
        result.FocusNoteLine.Should().BeNull();
        result.NeedsRender.Should().BeTrue();
        OnDisk.Should().Be("# Title\n\nPara.\n");
    }

    [Fact]
    public void Clear_OneNote_SaysSo() {
        var runner = Runner("Para.\n[usernote]one[/usernote]\n");

        runner.Clear(runner.Store.File.Text).Message.Should().Be("1 note deleted");
    }

    [Fact]
    public void Clear_WithoutNotes_WritesNothing() {
        var runner = Runner("Para.\n");

        var result = runner.Clear(runner.Store.File.Text);

        result.Status.Should().Be(NoteActionStatus.NothingToDo);
        result.Message.Should().Be("There are no notes to delete.");
        runner.Store.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void Clear_FileChangedOnDisk_WritesNothing() {
        var runner = Runner("Para.\n[usernote]one[/usernote]\n");
        var rendered = runner.Store.File.Text;
        File.WriteAllText(_path, "Para.\n[usernote]one[/usernote]\nMore.\n");

        var result = runner.Clear(rendered);

        result.Status.Should().Be(NoteActionStatus.Stale);
        result.NeedsRender.Should().BeTrue();
        OnDisk.Should().Be("Para.\n[usernote]one[/usernote]\nMore.\n");
    }

    [Fact]
    public void Clear_ReadOnlyFile_IsRefusedWithTheReason() {
        const string text = "Абзац.\n[usernote]заметка[/usernote]\n";
        File.WriteAllBytes(_path, _windows1251.GetBytes(text));
        var runner = RunnerForFile(new MarkdownFileOptions(AnsiEncoding: _windows1251));

        var result = runner.Clear(text);

        result.Status.Should().Be(NoteActionStatus.ReadOnly);
        File.ReadAllBytes(_path).Should().Equal(_windows1251.GetBytes(text));
    }

    [Fact]
    public void UndoAndRedo_OfAClear_PutTheNotesBackAndTakeThemAway() {
        const string text = "Para.\n[usernote]one[/usernote]\n[usernote]two[/usernote]\n";
        var runner = Runner(text);
        runner.Clear(runner.Store.File.Text);

        var undo = runner.Undo();

        undo.Message.Should().Be("All notes deleted undone");
        undo.FocusNoteLine.Should().BeNull();
        OnDisk.Should().Be(text);

        runner.Redo().Message.Should().Be("All notes deleted redone");
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

    // Task-list toggles

    [Fact]
    public void ToggleTask_Success_AnnouncesAndKeepsTheFocusOnTheItem() {
        var runner = Runner("# Plan\n\n- [ ] one\n- [x] two\n");

        var check = runner.ToggleTask(runner.Store.File.Text, 3, isChecked: true);

        check.Status.Should().Be(NoteActionStatus.Done);
        check.Message.Should().Be("Task checked");
        check.FocusTaskLine.Should().Be(3);
        check.FocusNoteLine.Should().BeNull();
        check.NeedsRender.Should().BeTrue();

        var uncheck = runner.ToggleTask(runner.Store.File.Text, 4, isChecked: false);

        uncheck.Message.Should().Be("Task unchecked");
        uncheck.FocusTaskLine.Should().Be(4);
        OnDisk.Should().Be("# Plan\n\n- [x] one\n- [ ] two\n");
    }

    [Fact]
    public void ToggleTask_FileChangedOnDisk_WritesNothing() {
        var runner = Runner("- [ ] one\n");
        var rendered = runner.Store.File.Text;
        File.WriteAllText(_path, "- [ ] one, changed elsewhere\n");

        var result = runner.ToggleTask(rendered, 1, isChecked: true);

        result.Status.Should().Be(NoteActionStatus.Stale);
        result.Message.Should().Be("The file changed. Please try again.");
        result.NeedsRender.Should().BeTrue();
        OnDisk.Should().Be("- [ ] one, changed elsewhere\n");
    }

    [Fact]
    public void ToggleTask_LockedFile_FailsWithTheReason() {
        var runner = Runner("- [ ] one\n");
        var rendered = runner.Store.File.Text;
        NoteActionResult result;

        using (new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
            result = runner.ToggleTask(rendered, 1, isChecked: true);
        }

        result.Status.Should().Be(NoteActionStatus.Failed);
        result.Message.Should().StartWith("Unable to write the file: ");
        result.NeedsRender.Should().BeFalse();
        OnDisk.Should().Be("- [ ] one\n");
    }

    [Fact]
    public void ToggleTask_ReadOnlyFile_IsRefusedWithTheReason() {
        var bytes = _windows1251.GetBytes("- [ ] задача\n");
        File.WriteAllBytes(_path, bytes);
        var runner = RunnerForFile(new MarkdownFileOptions(AnsiEncoding: _windows1251));

        var result = runner.ToggleTask(runner.Store.File.Text, 1, isChecked: true);

        result.Status.Should().Be(NoteActionStatus.ReadOnly);
        result.Message.Should().Be(runner.CannotWriteReason);
        result.NeedsRender.Should().BeFalse();
        File.ReadAllBytes(_path).Should().Equal(bytes);
    }

    [Theory]
    [InlineData(true, "Task checked undone", "Task checked redone")]
    [InlineData(false, "Task unchecked undone", "Task unchecked redone")]
    public void UndoAndRedo_OfAToggle_AnnounceItAndFocusTheItem(bool isChecked, string undone, string redone) {
        var before = isChecked ? "- [ ] one\n" : "- [x] one\n";
        var runner = Runner(before);
        runner.ToggleTask(runner.Store.File.Text, 1, isChecked);
        var after = OnDisk;

        var undo = runner.Undo();

        undo.Message.Should().Be(undone);
        undo.FocusTaskLine.Should().Be(1);
        undo.FocusNoteLine.Should().BeNull();
        OnDisk.Should().Be(before);

        var redo = runner.Redo();

        redo.Message.Should().Be(redone);
        redo.FocusTaskLine.Should().Be(1);
        OnDisk.Should().Be(after);
    }

    [Fact]
    public void UndoAndRedo_OfANoteAction_FocusNoTask() {
        var runner = Runner("Para.\n");
        var rendered = runner.Store.File.Text;
        runner.Add(rendered, Render(rendered).Blocks[0], "Check this");

        runner.Undo().FocusTaskLine.Should().BeNull();
        runner.Redo().FocusTaskLine.Should().BeNull();
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
