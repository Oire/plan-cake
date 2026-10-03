using System.Text.Json;
using AwesomeAssertions;
using Oire.PlanCake.Notes;
using Oire.PlanCake.Rendering;
using Oire.PlanCake.Ui;
using Oire.PlanCake.Utils.Enums;
using Xunit;

namespace Oire.PlanCake.Tests;

/// <summary>
/// The page protocol decides what may end in a write to the user's file: a message about an
/// older render, or about a block or note the render does not have, must never lead to one.
/// </summary>
public class PageMessageRouterTests {
    private const int Generation = 5;

    private const string Text =
        "[usernote]At the top[/usernote]\n\n# Title\n\nPara.\n[usernote]A note[/usernote]\n\n- [ ] Task\n";

    private static readonly RenderResult _render = MarkdownRenderer.Render(
        Text,
        new RenderOptions(NoteMarkers.Default, RenderMode.Interactive, new RenderStrings("User note"))
    );

    private static PageState State(BlockEnterAction enterAction = BlockEnterAction.AddNote) =>
        new(_render, Generation, Text, enterAction);

    private static readonly PageState _noRender = new(null, 0, String.Empty, BlockEnterAction.AddNote);

    private static BlockInfo Paragraph => _render.Blocks.Single(block => block.Kind == BlockKind.Paragraph);

    private static BlockInfo TaskItem => _render.Blocks.Single(block => block.Kind == BlockKind.ListItem);

    /// <summary>The note after the paragraph.</summary>
    private static RenderedNote BlockNote => _render.Notes.Single(note => note.Block is not null);

    /// <summary>The note before the first block, which follows no block.</summary>
    private static RenderedNote TopNote => _render.Notes.Single(note => note.Block is null);

    private static JsonElement Message(string json) {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.Clone();
    }

    private static PageIntent Route(
        string type,
        string json,
        PageState? state = null,
        IReadOnlyList<string>? files = null
    ) => PageMessageRouter.Route(type, Message(json), files ?? [], state ?? State());

    [Fact]
    public void TheTestDocument_HasTheBlocksAndNotesTheTestsUse() {
        Paragraph.Should().NotBeNull();
        TaskItem.Should().NotBeNull();
        BlockNote.Block.Should().Be(Paragraph);
        TopNote.Block.Should().BeNull();
    }

    // Activate

    [Fact]
    public void Activate_OnABlockOfTheCurrentRender_AddsANoteToIt() {
        var intent = Route(PageMessages.Activate, $$"""{ "lines": "{{Paragraph.Lines}}", "generation": 5 }""");

        var add = intent.Should().BeOfType<PageIntent.AddNote>().Subject;
        add.Block.Should().Be(Paragraph);
        add.Target.Should().Be(new NoteTarget(Generation, Text, Paragraph, null));
    }

    [Fact]
    public void Activate_WithTheContextMenuSetting_OpensTheBlocksMenuAtItsPlace() {
        var intent = Route(
            PageMessages.Activate,
            $$"""{ "lines": "{{Paragraph.Lines}}", "generation": 5, "rect": { "x": 1, "y": 2, "width": 3, "height": 4 }, "scale": 1.5 }""",
            State(BlockEnterAction.ContextMenu)
        );

        var menu = intent.Should().BeOfType<PageIntent.ShowContextMenu>().Subject;
        menu.Target.Block.Should().Be(Paragraph);
        menu.Rect.Should().Be(new RectangleF(1, 2, 3, 4));
        menu.Scale.Should().Be(1.5);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    public void Activate_FromAnotherRender_IsIgnoredWithoutATarget(int generation) {
        var intent = Route(PageMessages.Activate, $$"""{ "lines": "{{Paragraph.Lines}}", "generation": {{generation}} }""");

        intent.Should().Be(new PageIntent.Ignored());
    }

    [Theory]
    [InlineData("""{ "lines": "99-99", "generation": 5 }""")]
    [InlineData("""{ "lines": 7, "generation": 5 }""")]
    [InlineData("""{ "generation": 5 }""")]
    [InlineData("""{ "lines": "5-5", "generation": "5" }""")]
    [InlineData("""{ "lines": "5-5" }""")]
    public void Activate_WithoutAKnownBlockOfTheCurrentRender_IsIgnored(string json) =>
        Route(PageMessages.Activate, json).Should().Be(new PageIntent.Ignored());

    [Fact]
    public void Activate_BeforeAnyRender_IsIgnored() =>
        Route(PageMessages.Activate, """{ "lines": "5-5", "generation": 0 }""", _noRender)
            .Should().Be(new PageIntent.Ignored());

    [Fact]
    public void Activate_OnANoteThatFollowsNoBlock_AddsNothingButMakesTheNoteCurrent() {
        var intent = Route(PageMessages.Activate, $$"""{ "note": {{TopNote.Index}}, "generation": 5 }""");

        var ignored = intent.Should().BeOfType<PageIntent.Ignored>().Subject;
        ignored.Target!.Note.Should().Be(TopNote);
        PageMessageRouter.TargetOf(intent).Should().Be(ignored.Target);
    }

    // ActivateNote

    [Fact]
    public void ActivateNote_OnANoteOfTheCurrentRender_EditsIt() {
        var intent = Route(PageMessages.ActivateNote, $$"""{ "note": {{BlockNote.Index}}, "generation": 5 }""");

        var edit = intent.Should().BeOfType<PageIntent.EditNote>().Subject;
        edit.Note.Should().Be(BlockNote);
        edit.Target.Should().Be(new NoteTarget(Generation, Text, Paragraph, BlockNote));
    }

    [Theory]
    [InlineData("""{ "note": 2, "generation": 5 }""")]
    [InlineData("""{ "note": -1, "generation": 5 }""")]
    [InlineData("""{ "note": 0, "generation": 4 }""")]
    public void ActivateNote_WithoutAKnownNoteOfTheCurrentRender_IsIgnored(string json) =>
        Route(PageMessages.ActivateNote, json).Should().Be(new PageIntent.Ignored());

    [Fact]
    public void ActivateNote_NamingANoteAndLines_TheNoteWins() {
        var intent = Route(
            PageMessages.ActivateNote,
            $$"""{ "note": {{BlockNote.Index}}, "lines": "{{TaskItem.Lines}}", "generation": 5 }"""
        );

        intent.Should().BeOfType<PageIntent.EditNote>().Which.Note.Should().Be(BlockNote);
    }

    [Fact]
    public void ActivateNote_OnABlock_EditsNothing() {
        var intent = Route(PageMessages.ActivateNote, $$"""{ "lines": "{{Paragraph.Lines}}", "generation": 5 }""");

        intent.Should().BeOfType<PageIntent.Ignored>().Which.Target!.Block.Should().Be(Paragraph);
    }

    // ContextMenu

    [Fact]
    public void ContextMenu_OnANote_OpensItsMenuAtScaleOneWhenNoneIsGiven() {
        var intent = Route(PageMessages.ContextMenu, $$"""{ "note": {{TopNote.Index}}, "generation": 5 }""");

        var menu = intent.Should().BeOfType<PageIntent.ShowContextMenu>().Subject;
        menu.Target.Note.Should().Be(TopNote);
        menu.Target.Block.Should().BeNull();
        menu.Rect.Should().BeNull();
        menu.Scale.Should().Be(1);
    }

    [Fact]
    public void ContextMenu_FromAnOlderRender_IsIgnored() =>
        Route(PageMessages.ContextMenu, $$"""{ "lines": "{{Paragraph.Lines}}", "generation": 1 }""")
            .Should().Be(new PageIntent.Ignored());

    // ToggleTask

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ToggleTask_OnATaskOfTheCurrentRender_WritesTheNewState(bool isChecked) {
        var json = $$"""{ "lines": "{{TaskItem.Lines}}", "checked": {{(isChecked ? "true" : "false")}}, "generation": 5 }""";

        var toggle = Route(PageMessages.ToggleTask, json).Should().BeOfType<PageIntent.ToggleTask>().Subject;

        toggle.Item.Should().Be(TaskItem);
        toggle.Checked.Should().Be(isChecked);
        toggle.Target.Should().Be(new NoteTarget(Generation, Text, TaskItem, null));
    }

    [Fact]
    public void ToggleTask_FromAnOlderRender_IsDroppedAndSaidSo() =>
        Route(PageMessages.ToggleTask, $$"""{ "lines": "{{TaskItem.Lines}}", "checked": true, "generation": 4 }""")
            .Should().Be(new PageIntent.TaskToggleDropped());

    [Theory]
    [InlineData("""{ "lines": "8-8", "generation": 5 }""")]
    [InlineData("""{ "lines": "8-8", "checked": "true", "generation": 5 }""")]
    [InlineData("""{ "lines": "8-8", "checked": true, "generation": 4 }""")]
    public void ToggleTask_BeforeAnyRender_IsIgnored(string json) =>
        Route(PageMessages.ToggleTask, json, _noRender).Should().Be(new PageIntent.Ignored());

    [Fact]
    public void ToggleTask_WithoutTheNewState_IsIgnoredEvenFromAnOlderRender() =>
        Route(PageMessages.ToggleTask, $$"""{ "lines": "{{TaskItem.Lines}}", "generation": 4 }""")
            .Should().Be(new PageIntent.Ignored());

    [Fact]
    public void ToggleTask_UnknownLinesOfTheCurrentRender_IsIgnored() =>
        Route(PageMessages.ToggleTask, """{ "lines": "99-99", "checked": true, "generation": 5 }""")
            .Should().Be(new PageIntent.Ignored());

    [Fact]
    public void ToggleTask_OnANote_WritesNothing() {
        var intent = Route(
            PageMessages.ToggleTask,
            $$"""{ "note": {{BlockNote.Index}}, "checked": true, "generation": 5 }"""
        );

        intent.Should().BeOfType<PageIntent.Ignored>().Which.Target!.Note.Should().Be(BlockNote);
    }

    // Position

    [Fact]
    public void Position_OnANote_ReportsTheNote() =>
        Route(PageMessages.Position, $$"""{ "note": {{BlockNote.Index}}, "generation": 5 }""")
            .Should().Be(new PageIntent.Position(BlockNote, null));

    [Fact]
    public void Position_OnABlock_ReportsTheBlock() =>
        Route(PageMessages.Position, $$"""{ "lines": "{{Paragraph.Lines}}", "generation": 5 }""")
            .Should().Be(new PageIntent.Position(null, Paragraph));

    [Fact]
    public void Position_OnLinesTheRenderDoesNotKnow_ReportsNoBlock() =>
        Route(PageMessages.Position, """{ "lines": "99-99", "generation": 5 }""")
            .Should().Be(new PageIntent.Position(null, null));

    [Theory]
    [InlineData("""{ "lines": "5-5", "generation": 4 }""")]
    [InlineData("""{ "note": 9, "generation": 5 }""")]
    [InlineData("""{ "generation": 5 }""")]
    public void Position_FromAnOlderRenderOrAboutNothingKnown_IsIgnored(string json) =>
        Route(PageMessages.Position, json).Should().Be(new PageIntent.Ignored());

    [Fact]
    public void Position_BeforeAnyRender_IsIgnored() =>
        Route(PageMessages.Position, """{ "lines": "5-5", "generation": 0 }""", _noRender)
            .Should().Be(new PageIntent.Ignored());

    // Messages about no block

    [Fact]
    public void Ready_IsReady() =>
        Route(PageMessages.Ready, """{ "type": "ready" }""", _noRender).Should().Be(new PageIntent.Ready());

    [Theory]
    [InlineData("""{ "href": "other.md" }""", "other.md")]
    [InlineData("""{ "href": 3 }""", "")]
    [InlineData("""{ }""", "")]
    public void OpenLink_CarriesTheRawHref(string json, string href) =>
        Route(PageMessages.OpenLink, json).Should().Be(new PageIntent.OpenLink(href));

    [Fact]
    public void GoBack_NoMoreNotes_NoMoreBlocks_HaveTheirIntents() {
        Route(PageMessages.GoBack, "{}").Should().Be(new PageIntent.GoBack());
        Route(PageMessages.NoMoreNotes, "{}").Should().Be(new PageIntent.NoMoreNotes());
        Route(PageMessages.NoMoreBlocks, "{}").Should().Be(new PageIntent.NoMoreBlocks());
    }

    [Fact]
    public void DropFiles_CarriesTheFilesThePagePassed() {
        string[] files = [@"C:\plans\a.md", @"C:\plans\b.txt"];

        Route(PageMessages.DropFiles, "{}", files: files)
            .Should().BeOfType<PageIntent.DropFiles>().Which.Files.Should().Equal(files);
    }

    [Fact]
    public void AnUnknownType_IsReportedAsUnknown() =>
        Route("announce", "{}").Should().Be(new PageIntent.Unknown("announce"));

    // TargetOf and WhileCurrent

    [Fact]
    public void TargetOf_IntentsAboutNoBlock_HaveNone() {
        PageMessageRouter.TargetOf(new PageIntent.Ready()).Should().BeNull();
        PageMessageRouter.TargetOf(new PageIntent.Position(BlockNote, null)).Should().BeNull();
        PageMessageRouter.TargetOf(new PageIntent.TaskToggleDropped()).Should().BeNull();
        PageMessageRouter.TargetOf(new PageIntent.Ignored()).Should().BeNull();
    }

    [Fact]
    public void TargetOf_AnAction_IsItsTarget() {
        var intent = Route(PageMessages.Activate, $$"""{ "lines": "{{Paragraph.Lines}}", "generation": 5 }""");

        PageMessageRouter.TargetOf(intent).Should().Be(((PageIntent.AddNote)intent).Target);
    }

    [Fact]
    public void WhileCurrent_RunsTheActionOnlyWhileItsRenderIsShown() {
        var target = new NoteTarget(Generation, Text, Paragraph, null);
        var current = Generation;
        var runs = 0;
        var action = PageMessageRouter.WhileCurrent(target, () => current, () => runs++);

        action();
        current = Generation + 1;
        action();

        runs.Should().Be(1);
    }
}
