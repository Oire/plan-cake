using System.Text;
using AwesomeAssertions;
using Oire.PlanCake.Notes;
using Oire.PlanCake.Rendering;
using Xunit;

namespace Oire.PlanCake.Tests;

public class NoteStoreTests: IDisposable {
    private static readonly RenderStrings _strings = new("user note", "unote");
    private static readonly NoteMarkers _singleToken = new("!USERNOTE!");
    private static readonly Encoding _windows1251 = CodePagesEncodingProvider.Instance.GetEncoding(1251)!;

    private readonly string _folder;
    private string _path = string.Empty;

    public NoteStoreTests() {
        _folder = Path.Combine(Path.GetTempPath(), $"PlanCake.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_folder);
    }

    public void Dispose() {
        if (Directory.Exists(_folder)) {
            Directory.Delete(_folder, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private NoteStore Store(string text, NoteMarkers? markers = null, bool bom = false) {
        _path = Path.Combine(_folder, "plan.md");
        File.WriteAllText(_path, text, new UTF8Encoding(bom));

        return Store(markers);
    }

    private NoteStore Store(NoteMarkers? markers = null, MarkdownFileOptions? options = null) => new(
        MarkdownFile.Open(_path, options ?? new MarkdownFileOptions(RetryDelay: TimeSpan.FromMilliseconds(10))),
        markers ?? NoteMarkers.Default
    );

    private static RenderResult Render(string text, NoteMarkers? markers = null) => MarkdownRenderer.Render(
        text,
        new RenderOptions(markers ?? NoteMarkers.Default, RenderMode.Interactive, _strings)
    );

    private static BlockInfo Block(RenderResult render, BlockKind kind, int index = 0) =>
        render.Blocks.Where(block => block.Kind == kind).ElementAt(index);

    private string OnDisk => File.ReadAllText(_path);

    /// <summary>Adds a note after the <paramref name="index"/>th block of the kind, as the view would.</summary>
    private static NoteChange AddAfter(NoteStore store, BlockKind kind, string text, int index = 0) {
        var rendered = store.File.Text;
        var render = Render(rendered, store.Markers);

        return store.Add(rendered, Block(render, kind, index), text);
    }

    // Placement

    [Fact]
    public void Add_AfterParagraph_GoesOnTheNextLine() {
        var store = Store("Intro line one\nline two\n\nNext.\n");

        var change = AddAfter(store, BlockKind.Paragraph, "Check this");

        OnDisk.Should().Be("Intro line one\nline two\n[usernote]Check this[/usernote]\n\nNext.\n");
        change.Line.Should().Be(3);
        change.Operation.Should().Be(NoteOperation.Add);
    }

    [Fact]
    public void Add_AfterHeading_GoesOnTheNextLine() {
        var store = Store("# Title\n\nText.\n");

        AddAfter(store, BlockKind.Heading, "Rename");

        OnDisk.Should().Be("# Title\n[usernote]Rename[/usernote]\n\nText.\n");
    }

    [Fact]
    public void Add_AfterNestedListItem_IndentsToTheItemsContent() {
        var store = Store("- parent\n  - child\n  - other\n- last\n");

        AddAfter(store, BlockKind.ListItem, "On the child", index: 1);

        OnDisk.Should().Be("- parent\n  - child\n    [usernote]On the child[/usernote]\n  - other\n- last\n");
        var render = Render(OnDisk);
        render.Notes.Should().ContainSingle().Which.Block!.Text.Should().Be("child");
    }

    [Fact]
    public void Add_AfterOrderedListItem_IndentsToTheItemsContent() {
        var store = Store("1.  first\n2.  second\n");

        AddAfter(store, BlockKind.ListItem, "Note");

        OnDisk.Should().Be("1.  first\n    [usernote]Note[/usernote]\n2.  second\n");
    }

    [Fact]
    public void Add_AfterTableRow_GoesAfterThatRow() {
        var store = Store("| A | B |\n|---|---|\n| 1 | 2 |\n| 3 | 4 |\n");

        AddAfter(store, BlockKind.TableRow, "Row one", index: 1);

        OnDisk.Should().Be("| A | B |\n|---|---|\n| 1 | 2 |\n[usernote]Row one[/usernote]\n| 3 | 4 |\n");
        Render(OnDisk).Notes.Should().ContainSingle().Which.Block!.Lines.Should().Be("3-3");
    }

    [Fact]
    public void Add_AfterFencedCode_GoesAfterTheClosingFence() {
        var store = Store("```cs\nvar x = 1;\n```\nAfter.\n");

        AddAfter(store, BlockKind.Code, "Why 1?");

        OnDisk.Should().Be("```cs\nvar x = 1;\n```\n[usernote]Why 1?[/usernote]\nAfter.\n");
    }

    [Fact]
    public void Add_AfterParagraphInBlockquote_WritesNoQuoteMarkerAndStillAnchorsThere() {
        var store = Store("> Quoted text\n> more\n\nAfter.\n");

        AddAfter(store, BlockKind.Paragraph, "About the quote");

        OnDisk.Should().Be("> Quoted text\n> more\n[usernote]About the quote[/usernote]\n\nAfter.\n");
        var render = Render(OnDisk);
        render.Notes.Should().ContainSingle().Which.Block!.Text.Should().StartWith("Quoted text");
        render.Html.Should().Contain("<blockquote");
    }

    [Fact]
    public void Add_TwoNotesOnOneBlock_StackInTheOrderAdded() {
        var store = Store("Para.\n\nNext.\n");

        AddAfter(store, BlockKind.Paragraph, "first");
        var second = AddAfter(store, BlockKind.Paragraph, "second");

        OnDisk.Should().Be("Para.\n[usernote]first[/usernote]\n[usernote]second[/usernote]\n\nNext.\n");
        second.Line.Should().Be(3);
        Render(OnDisk).Notes.Select(note => note.Note.Text).Should().Equal("first", "second");
    }

    [Fact]
    public void Add_HandWrittenNoteAfterABlankLine_NewNoteStacksAfterIt() {
        var store = Store("Para.\n\n[usernote]old[/usernote]\n\nNext.\n");

        AddAfter(store, BlockKind.Paragraph, "new");

        OnDisk.Should().Be("Para.\n\n[usernote]old[/usernote]\n[usernote]new[/usernote]\n\nNext.\n");
    }

    [Fact]
    public void Add_MultiLineNote_PrefixesEveryLine() {
        var store = Store("- item\n- other\n");

        AddAfter(store, BlockKind.ListItem, "line one\r\nline two\n\nline four");

        OnDisk.Should().Be(
            "- item\n  [usernote]line one\n  line two\n\n  line four[/usernote]\n- other\n"
        );
        Render(OnDisk).Notes.Should().ContainSingle().Which.Note.Text.Should().Be("line one\nline two\n\nline four");
    }

    [Fact]
    public void Add_AtTheEndOfAFileWithoutFinalLineBreak_AddsNoTrailingOne() {
        var store = Store("Only line");

        AddAfter(store, BlockKind.Paragraph, "note");

        OnDisk.Should().Be("Only line\n[usernote]note[/usernote]");
    }

    [Fact]
    public void Add_SingleTokenMode_WritesTheTokenAndTurnsLineBreaksIntoSpaces() {
        var store = Store("Para.\n", _singleToken);

        AddAfter(store, BlockKind.Paragraph, "one\ntwo");

        OnDisk.Should().Be("Para.\n!USERNOTE! one two\n");
    }

    // Edit, delete, clear

    [Fact]
    public void Edit_ReplacesTheNoteText() {
        var store = Store("Para.\n  [usernote]old[/usernote]\nNext.\n");
        var render = Render(store.File.Text);

        var change = store.Edit(store.File.Text, render.Notes[0].Note, "new\nsecond");

        OnDisk.Should().Be("Para.\n  [usernote]new\n  second[/usernote]\nNext.\n");
        change.Operation.Should().Be(NoteOperation.Edit);
        change.Line.Should().Be(2);
    }

    [Fact]
    public void Delete_NoteOnItsOwnLine_RemovesTheLine() {
        var store = Store("Para.\n[usernote]gone[/usernote]\n\nNext.\n");
        var render = Render(store.File.Text);

        store.Delete(store.File.Text, render.Notes[0].Note);

        OnDisk.Should().Be("Para.\n\nNext.\n");
    }

    [Fact]
    public void Delete_NoteMidLine_KeepsTheRestOfTheLine() {
        var store = Store("Text [usernote]a[/usernote]\nMore [usernote]b[/usernote] text\n");
        var render = Render(store.File.Text);

        store.Delete(store.File.Text, render.Notes[1].Note);
        store.Delete(store.File.Text, Render(store.File.Text).Notes[0].Note);

        OnDisk.Should().Be("Text\nMore  text\n");
    }

    [Fact]
    public void Delete_LastLineWithoutLineBreak_RestoresTheFileAsItWas() {
        var store = Store("Only line");
        AddAfter(store, BlockKind.Paragraph, "note");

        store.Delete(store.File.Text, Render(store.File.Text).Notes[0].Note);

        OnDisk.Should().Be("Only line");
    }

    [Fact]
    public void Clear_RemovesEveryNoteAndNothingElse() {
        const string clean = "# T\n\n- a\n  - b\n\n| x |\n|---|\n| 1 |\n\n> quote\n";
        const string withNotes = "# T\n[usernote]h[/usernote]\n\n- a\n  - b\n    [usernote]multi\n    line[/usernote]\n"
            + "\n| x |\n|---|\n| 1 |\n[usernote]r[/usernote] [usernote]r2[/usernote]\n\n> quote\n> [usernote]q[/usernote]\n";
        var store = Store(withNotes);

        var change = store.Clear(withNotes);

        OnDisk.Should().Be(clean);
        change.Count.Should().Be(5);
        change.Line.Should().BeNull();
    }

    [Fact]
    public void Clear_WithoutNotes_WritesNothingAndRecordsNoUndo() {
        var store = Store("Para.\n");
        var before = File.GetLastWriteTimeUtc(_path);

        var change = store.Clear("Para.\n");

        change.Count.Should().Be(0);
        store.CanUndo.Should().BeFalse();
        File.GetLastWriteTimeUtc(_path).Should().Be(before);
    }

    // Validation

    [Fact]
    public void Add_TextWithClosingMarker_IsRejectedAndNothingWritten() {
        var store = Store("Para.\n");

        var add = () => AddAfter(store, BlockKind.Paragraph, "a [/usernote] b");

        add.Should().Throw<ArgumentException>();
        OnDisk.Should().Be("Para.\n");
        store.Validate("a [/usernote] b").Should().Be(NoteTextError.ContainsClosingMarker);
    }

    [Theory]
    [InlineData("fine", nameof(NoteTextError.None))]
    [InlineData("   ", nameof(NoteTextError.Empty))]
    [InlineData("[usernote] nested opening is harmless", nameof(NoteTextError.None))]
    [InlineData("ends with [/usernote", nameof(NoteTextError.None))]
    [InlineData("x[/usernote]", nameof(NoteTextError.ContainsClosingMarker))]
    public void Validate_PairedMode(string text, string expected) =>
        // NoteTextError is internal, and a public test method cannot take it as a parameter.
        new NoteStore(MarkdownFileStub(), NoteMarkers.Default).Validate(text).ToString().Should().Be(expected);

    [Fact]
    public void Validate_ClosingMarkerCompletedByTheAppendedOne_IsRejected() {
        var store = new NoteStore(MarkdownFileStub(), new NoteMarkers("<<", "]]"));

        store.Validate("text ]").Should().Be(NoteTextError.ContainsClosingMarker);
    }

    [Theory]
    [InlineData("fine", nameof(NoteTextError.None))]
    [InlineData("two\nlines", nameof(NoteTextError.ContainsLineBreak))]
    [InlineData("has !USERNOTE! inside", nameof(NoteTextError.ContainsOpeningMarker))]
    public void Validate_SingleTokenMode(string text, string expected) =>
        new NoteStore(MarkdownFileStub(), _singleToken).Validate(text).ToString().Should().Be(expected);

    [Fact]
    public void NormalizeText_SingleTokenMode_TurnsLineBreaksIntoSpaces() =>
        new NoteStore(MarkdownFileStub(), _singleToken).NormalizeText(" a\r\nb\rc\n ").Should().Be("a b c");

    private MarkdownFile MarkdownFileStub() {
        _path = Path.Combine(_folder, "stub.md");
        File.WriteAllText(_path, string.Empty);

        return MarkdownFile.Open(_path);
    }

    // Safety

    [Fact]
    public void Add_FileChangedOnDisk_ThrowsStaleAndWritesNothing() {
        var store = Store("Para.\n");
        var rendered = store.File.Text;
        var render = Render(rendered);
        File.WriteAllText(_path, "Para.\nEdited elsewhere.\n");

        var add = () => store.Add(rendered, render.Blocks[0], "note");

        add.Should().Throw<StaleFileException>();
        OnDisk.Should().Be("Para.\nEdited elsewhere.\n");
        store.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void Add_CrLfFile_WritesCrLf() {
        var store = Store("Para.\r\n\r\nNext.\r\n");

        AddAfter(store, BlockKind.Paragraph, "one\ntwo");

        OnDisk.Should().Be("Para.\r\n[usernote]one\r\ntwo[/usernote]\r\n\r\nNext.\r\n");
    }

    [Fact]
    public void Add_LfFile_WritesLf() {
        var store = Store("Para.\n\nNext.\n");

        AddAfter(store, BlockKind.Paragraph, "one\r\ntwo");

        OnDisk.Should().Be("Para.\n[usernote]one\ntwo[/usernote]\n\nNext.\n");
    }

    [Fact]
    public void Add_BomFile_KeepsTheBom() {
        var store = Store("Para.\n", bom: true);

        AddAfter(store, BlockKind.Paragraph, "note");

        File.ReadAllBytes(_path).Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
    }

    [Fact]
    public void Add_FileWithoutBom_GetsNoBom() {
        var store = Store("Para.\n");

        AddAfter(store, BlockKind.Paragraph, "Привет");

        File.ReadAllBytes(_path).Should().Equal(Encoding.UTF8.GetBytes("Para.\n[usernote]Привет[/usernote]\n"));
    }

    [Fact]
    public void Windows1251File_EveryWriteIsRefused() {
        _path = Path.Combine(_folder, "ansi.md");
        const string text = "Абзац.\n[usernote]заметка[/usernote]\n";
        var bytes = _windows1251.GetBytes(text);
        File.WriteAllBytes(_path, bytes);
        var store = Store(options: new MarkdownFileOptions(AnsiEncoding: _windows1251));
        var render = Render(store.File.Text);

        store.File.IsReadOnly.Should().BeTrue();
        store.File.Text.Should().Be(text);

        ((Action)(() => store.Add(text, render.Blocks[0], "new"))).Should().Throw<ReadOnlyFileException>();
        ((Action)(() => store.Edit(text, render.Notes[0].Note, "new"))).Should().Throw<ReadOnlyFileException>();
        ((Action)(() => store.Delete(text, render.Notes[0].Note))).Should().Throw<ReadOnlyFileException>();
        ((Action)(() => store.Clear(text))).Should().Throw<ReadOnlyFileException>();
        File.ReadAllBytes(_path).Should().Equal(bytes);
    }

    [Fact]
    public void Windows1251FileWithConvertToUtf8_IsRewrittenAsUtf8AndNotesWork() {
        _path = Path.Combine(_folder, "ansi.md");
        File.WriteAllBytes(_path, _windows1251.GetBytes("Абзац.\r\n\r\nДальше.\r\n"));
        var store = Store(options: new MarkdownFileOptions(ConvertToUtf8: true, AnsiEncoding: _windows1251));

        File.ReadAllBytes(_path).Should().Equal(new UTF8Encoding(false).GetBytes("Абзац.\r\n\r\nДальше.\r\n"));

        AddAfter(store, BlockKind.Paragraph, "заметка");

        File.ReadAllBytes(_path).Should().Equal(
            new UTF8Encoding(false).GetBytes("Абзац.\r\n[usernote]заметка[/usernote]\r\n\r\nДальше.\r\n")
        );
    }

    [Fact]
    public void Add_LockedFile_RetriesThenThrowsIOException() {
        var store = Store("Para.\n");
        var rendered = store.File.Text;
        var block = Render(rendered).Blocks[0];

        using (new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
            var add = () => store.Add(rendered, block, "note");

            add.Should().Throw<IOException>();
        }

        OnDisk.Should().Be("Para.\n");
    }

    // Undo and redo

    [Fact]
    public void UndoRedo_RestoreEachState() {
        var store = Store("Para.\n");
        AddAfter(store, BlockKind.Paragraph, "one");
        var afterAdd = OnDisk;
        store.Edit(store.File.Text, Render(store.File.Text).Notes[0].Note, "two");

        store.Undo().Operation.Should().Be(NoteOperation.Edit);
        OnDisk.Should().Be(afterAdd);

        store.Undo().Operation.Should().Be(NoteOperation.Add);
        OnDisk.Should().Be("Para.\n");
        store.CanUndo.Should().BeFalse();

        store.Redo().Operation.Should().Be(NoteOperation.Add);
        OnDisk.Should().Be(afterAdd);

        store.Redo().Operation.Should().Be(NoteOperation.Edit);
        OnDisk.Should().Be("Para.\n[usernote]two[/usernote]\n");
        store.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void NewChange_ClearsTheRedoStack() {
        var store = Store("Para.\n");
        AddAfter(store, BlockKind.Paragraph, "one");
        store.Undo();

        AddAfter(store, BlockKind.Paragraph, "two");

        store.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void Undo_AfterAnExternalChange_IsRefusedAndForgetsTheHistory() {
        var store = Store("Para.\n");
        AddAfter(store, BlockKind.Paragraph, "one");
        File.WriteAllText(_path, "Rewritten.\n");

        var undo = () => store.Undo();

        undo.Should().Throw<StaleFileException>();
        OnDisk.Should().Be("Rewritten.\n");
        store.CanUndo.Should().BeFalse();
        store.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void Redo_AfterAnExternalChange_IsRefused() {
        var store = Store("Para.\n");
        AddAfter(store, BlockKind.Paragraph, "one");
        store.Undo();
        File.WriteAllText(_path, "Rewritten.\n");

        var redo = () => store.Redo();

        redo.Should().Throw<StaleFileException>();
        OnDisk.Should().Be("Rewritten.\n");
        store.CanRedo.Should().BeFalse();
    }

    // Round trip

    [Fact]
    public void RoundTrip_MultiLineNoteInNestedListItem_KeepsExactlyOneIndentation() {
        var store = Store("- parent\n  - child\n- last\n");

        AddAfter(store, BlockKind.ListItem, "first line\n  indented by me\nlast line", index: 1);
        var parsed = NoteParser.Parse(store.File.Text, NoteMarkers.Default).Notes.Should().ContainSingle().Subject;
        parsed.Text.Should().Be("first line\n  indented by me\nlast line");

        store.Edit(store.File.Text, parsed, parsed.Text + "\nadded");
        var reparsed = NoteParser.Parse(store.File.Text, NoteMarkers.Default).Notes.Should().ContainSingle().Subject;

        reparsed.Text.Should().Be("first line\n  indented by me\nlast line\nadded");
        OnDisk.Should().Be(
            "- parent\n  - child\n    [usernote]first line\n      indented by me\n    last line\n    added[/usernote]\n- last\n"
        );
    }
}
