using AwesomeAssertions;
using Oire.PlanCake.Notes;
using Oire.PlanCake.Rendering;
using Xunit;

namespace Oire.PlanCake.Tests;

public class MarkdownRendererTests {
    private static readonly RenderStrings _strings = new("User note");

    private static RenderResult Render(
        string source,
        RenderMode mode = RenderMode.Interactive,
        RenderStrings? strings = null,
        string documentLanguage = "en"
    ) => MarkdownRenderer.Render(
        source,
        new RenderOptions(NoteMarkers.Default, mode, strings ?? _strings, documentLanguage)
    );

    private static IEnumerable<string> Ranges(RenderResult result, BlockKind kind) =>
        result.Blocks.Where(block => block.Kind == kind).Select(block => block.Lines);

    private const string NoteRoles = "role=\"region\" aria-label=\"User note\"";

    /// <summary>A user note whose text renders as one paragraph.</summary>
    private static string NoteDiv(int index, string text) => NoteDivHtml(index, $"""<p dir="auto">{text}</p>""");

    /// <summary>A user note with the given inner HTML.</summary>
    private static string NoteDivHtml(int index, string html) =>
        $"""<div class="note" {NoteRoles} data-note="{index}" dir="auto">{html}</div>""";

    // Line ranges

    [Fact]
    public void Render_Paragraph_CoversAllItsLines() {
        var result = Render("Intro.\n\nFirst line\nsecond line\nthird line\n");

        Ranges(result, BlockKind.Paragraph).Should().Equal("1-1", "3-5");
        result.Html.Should().Contain("""<p dir="auto" data-lines="3-5">First line""");
    }

    [Fact]
    public void Render_Headings_AtxAndSetext() {
        var result = Render("# Title\n\nText\n\nSub\n---\n");

        Ranges(result, BlockKind.Heading).Should().Equal("1-1", "5-6");
        result.Html.Should().Contain("""<h1 id="title" dir="auto" data-lines="1-1">Title</h1>""");
        result.Title.Should().Be("Title");
    }

    [Fact]
    public void Render_TightList_ParentItemExcludesItsNestedList() {
        var result = Render("- parent\n  - child one\n  - child two\n- last\n");

        Ranges(result, BlockKind.ListItem).Should().Equal("1-1", "2-2", "3-3", "4-4");
        result.Blocks[0].Text.Should().Be("parent");
        result.Html.Should().Contain("""<li dir="auto" data-lines="1-1">parent""");
        result.Html.Should().Contain("""<li dir="auto" data-lines="2-2">child one</li>""");
    }

    [Fact]
    public void Render_LooseList_StampsTheItemAndItsParagraphAlike() {
        var result = Render("1. first\n\n2. second\n\n   more of second\n");

        Ranges(result, BlockKind.ListItem).Should().Equal("1-1", "3-3");
        Ranges(result, BlockKind.Paragraph).Should().Equal("5-5");
        result.Html.Should().Contain("""<li dir="auto" data-lines="1-1"><p dir="auto" data-lines="1-1">first</p>""");
    }

    [Fact]
    public void Render_ListItemStartingWithCode_StampsTheCodeNotTheItem() {
        var result = Render("- ```\n  code\n  ```\n");

        result.Blocks.Should().ContainSingle()
            .Which.Should().Be(new BlockInfo(BlockKind.Code, 1, 3, "code", "code"));
        result.Html.Should().Contain("<li dir=\"auto\">\n<pre dir=\"auto\" data-lines=\"1-3\">");
    }

    [Fact]
    public void Render_PipeTable_StampsEveryRow() {
        var result = Render("Before\n\n| A | B |\n|---|---|\n| 1 | 2 |\n| 3 | 4 |\n");

        Ranges(result, BlockKind.TableRow).Should().Equal("3-3", "5-5", "6-6");
        result.Blocks.Where(block => block.Kind == BlockKind.TableRow).Select(block => block.Text)
            .Should().Equal("A | B", "1 | 2", "3 | 4");
        result.Html.Should().Contain("""<tr dir="auto" data-lines="5-5">""");
    }

    [Fact]
    public void Render_FencedCode_RangeIncludesBothFences() {
        var result = Render("Text\n\n```csharp\nvar x = 1;\nvar y = 2;\n```\n\nAfter\n");

        result.Blocks[1].Should()
            .Be(new BlockInfo(BlockKind.Code, 3, 6, "var x = 1;\nvar y = 2;", "var x = 1; var y = 2;"));
        result.Html.Should().Contain("""<pre class="language-csharp" dir="auto" data-lines="3-6"><code>""");
    }

    [Fact]
    public void Render_IndentedCode_CoversItsLines() {
        var result = Render("Text\n\n    one\n    two\n\nAfter\n");

        Ranges(result, BlockKind.Code).Should().Equal("3-4");
    }

    [Fact]
    public void Render_ParagraphInBlockquote_IsStampedAndTheQuoteIsNot() {
        var result = Render("> quoted\n> text\n");

        Ranges(result, BlockKind.Paragraph).Should().Equal("1-2");
        result.Html.Should().Contain("<blockquote dir=\"auto\">\n<p dir=\"auto\" data-lines=\"1-2\">");
    }

    [Fact]
    public void Render_NotesAboveInsideAndBelow_RangesStayInOriginalLines() {
        const string source =
            "[usernote]above[/usernote]\n" +     // 1
            "# Heading\n" +                      // 2
            "\n" +                               // 3
            "First line\n" +                     // 4
            "[usernote]inside[/usernote]\n" +    // 5
            "second line\n" +                    // 6
            "[usernote]below\n" +                // 7
            "two lines[/usernote]\n" +           // 8
            "\n" +                               // 9
            "- item\n";                          // 10

        var result = Render(source);

        result.Blocks.Select(block => $"{block.Kind} {block.Lines}")
            .Should().Equal("Heading 2-2", "Paragraph 4-6", "ListItem 10-10");
    }

    [Fact]
    public void Render_CrLfSource_GivesTheSameRanges() {
        var result = Render("# Title\r\n\r\nOne\r\ntwo\r\n[usernote]n[/usernote]\r\n\r\n- item\r\n");

        result.Blocks.Select(block => block.Lines).Should().Equal("1-1", "3-4", "7-7");
    }

    [Fact]
    public void Render_Blocks_GetDirAutoAndNoLang() {
        var result = Render("# T\n\nText\n\n- a\n\n> q\n\n| A |\n|---|\n| 1 |\n");

        result.Html.Should().NotContain("lang=");
        result.Html.Should().Contain("<ul dir=\"auto\">")
            .And.Contain("<table dir=\"auto\">")
            .And.Contain("<td dir=\"auto\">");
    }

    [Fact]
    public void Render_TaskListInteractive_CheckboxesAreEnabled() {
        var result = Render("- [x] done\n- [ ] todo\n");

        result.Html.Should().Contain(
            """<li class="task-list-item" dir="auto" data-lines="1-1"><input data-plancake-task="true" type="checkbox" aria-label="done" checked="checked" /> done</li>"""
        ).And.Contain(
            """<li class="task-list-item" dir="auto" data-lines="2-2"><input data-plancake-task="true" type="checkbox" aria-label="todo" /> todo</li>"""
        );
        result.Html.Should().NotContain("disabled");
        Ranges(result, BlockKind.ListItem).Should().Equal("1-1", "2-2");
    }

    [Fact]
    public void Render_TaskListLooseInteractive_CheckboxSitsInTheStampedParagraph() {
        var result = Render("- [ ] one\n\n- [x] two\n");

        result.Html.Should().Contain(
            """<p dir="auto" data-lines="1-1"><input data-plancake-task="true" type="checkbox" aria-label="one" /> one</p>"""
        );
        Ranges(result, BlockKind.ListItem).Should().Equal("1-1", "3-3");
    }

    // Partially checked parents

    // A check box named after its item, then the item's text.
    private static string Unchecked(string text) =>
        $"""<input data-plancake-task="true" type="checkbox" aria-label="{text}" /> {text}""";

    private static string Mixed(string text) =>
        $"""<input data-plancake-task="true" type="checkbox" data-mixed="true" aria-label="{text}" /> {text}""";

    private static string Checked(string text) =>
        $"""<input data-plancake-task="true" type="checkbox" aria-label="{text}" checked="checked" /> {text}""";

    [Fact]
    public void Render_TaskParentWithOneOfTwoChildrenChecked_IsMixed() {
        var result = Render("- [ ] parent\n  - [x] one\n  - [ ] two\n");

        result.Html.Should().Contain(Mixed("parent"));
        result.Html.Should().Contain(Checked("one")).And.Contain(Unchecked("two"));
        result.Html.Split("data-mixed").Should().HaveCount(2);
    }

    [Theory]
    [InlineData("- [ ] parent\n  - [ ] one\n  - [ ] two\n")]
    [InlineData("- [ ] parent\n  - [x] one\n  - [X] two\n")]
    [InlineData("- [ ] parent\n  - plain child\n  - [x] one\n")]
    [InlineData("- [ ] parent without task children\n  - one\n  - two\n")]
    public void Render_TaskParentWithNoneOrAllChildrenChecked_IsNotMixed(string source) =>
        Render(source).Html.Should().NotContain("data-mixed");

    [Fact]
    public void Render_CheckedTaskParent_StaysCheckedWhateverItsChildren() {
        var result = Render("- [x] parent\n  - [x] one\n  - [ ] two\n");

        result.Html.Should().Contain(Checked("parent")).And.NotContain("data-mixed");
    }

    [Fact]
    public void Render_Grandchild_CountsAsADescendant() {
        var result = Render("- [ ] parent\n  - [ ] child\n    - [x] grandchild\n");

        result.Html.Should().Contain(Mixed("parent"));
        result.Html.Should().Contain(Unchecked("child"));
        result.Html.Split("data-mixed").Should().HaveCount(2);
    }

    [Fact]
    public void Render_MixedParentInExport_IsAriaCheckedMixed() {
        var result = Render("- [ ] parent\n  - [x] one\n  - [ ] two\n", RenderMode.Export);

        result.Html.Should().Contain(
            """<input disabled="disabled" type="checkbox" aria-checked="mixed" aria-label="parent" /> parent"""
        );
        result.Html.Should().NotContain("data-mixed");
    }

    [Fact]
    public void Render_TaskListExport_CheckboxesStayDisabled() {
        var result = Render("- [x] done\n- [ ] todo\n", RenderMode.Export);

        result.Html.Should().Contain("""<input disabled="disabled" type="checkbox" aria-label="done" checked="checked" />""")
            .And.Contain("""<input disabled="disabled" type="checkbox" aria-label="todo" />""");
        result.Html.Should().NotContain("data-plancake-task");
    }

    [Fact]
    public void Render_TaskCheckbox_IsNamedAfterItsItemWithoutTheMarker() {
        var result = Render("- [ ] Fix \"quotes\" & **bold** text\n");

        result.Html.Should().Contain(
            """<input data-plancake-task="true" type="checkbox" aria-label="Fix &quot;quotes&quot; &amp; bold text" />"""
        );
    }

    [Fact]
    public void Render_LongTaskItem_NamesItsCheckboxWithTheExcerpt() {
        var words = String.Join(' ', Enumerable.Repeat("word", 30));

        var result = Render($"- [x] {words}\n");

        var label = TaskToggle.WithoutMarker(result.Blocks[0].Excerpt);
        label.Should().EndWith("…");
        result.Html.Should().Contain($"""aria-label="{label}" checked="checked" />""");
    }

    [Fact]
    public void Render_RawHtml_IsKept() {
        var result = Render("Press <kbd>F9</kbd>.\n\n<details><summary>More</summary>Hidden</details>\n");

        result.Html.Should().Contain("<kbd>F9</kbd>")
            .And.Contain("<details><summary>More</summary>Hidden</details>");
        result.Blocks.Should().ContainSingle().Which.Text.Should().Be("Press F9.");
    }

    // The plan's own markup cannot imitate the page's markers

    [Fact]
    public void Render_RawHtmlBlock_ProtocolAttributesAreRenamed() {
        var result = Render(
            "Text\n\n<div data-lines=\"40-40\" style=\"position:fixed;inset:0;opacity:0\">\n"
            + "<span DATA-NOTE='0' data-mixed data-plancake-current>x</span>\n</div>\n"
        );

        result.Html.Should().Contain("""<div x-data-lines="40-40" style="position:fixed;inset:0;opacity:0">""")
            .And.Contain("<span x-DATA-NOTE='0' x-data-mixed x-data-plancake-current>x</span>");
        result.Html.Should().NotContain(" data-lines=\"40-40\"").And.NotContain(" DATA-NOTE");
        Ranges(result, BlockKind.Paragraph).Should().Equal("1-1");
    }

    [Fact]
    public void Render_RawInlineHtml_ProtocolAttributesAreRenamed() {
        var result = Render("Press <span\tData-Lines=\"9-9\" data-note=1>F9</span>.\n");

        result.Html.Should().Contain("<span\tx-Data-Lines=\"9-9\" x-data-note=1>F9</span>");
        result.Html.Split("data-lines=").Should().HaveCount(2, "only the paragraph's own range is left");
    }

    [Theory]
    [InlineData("<div/data-lines=1>", "<div/x-data-lines=1>")]
    [InlineData("<div title=\"a\"data-note='0'>", "<div title=\"a\"x-data-note='0'>")]
    [InlineData("<div\ndata-mixed\n>", "<div\nx-data-mixed\n>")]
    [InlineData("<div data-PLANCAKE-current/>", "<div x-data-PLANCAKE-current/>")]
    [InlineData("<div data-lines>", "<div x-data-lines>")]
    public void NeutralizeRawHtml_RenamesEveryWayToWriteTheName(string html, string expected) =>
        MarkdownRenderer.NeutralizeRawHtml(html).Should().Be(expected);

    [Fact]
    public void Render_RawCheckbox_IsNotATaskCheckbox() {
        var result = Render(
            "- [ ] real\n\n<input type=\"checkbox\" class=\"task-list-item-checkbox\" Data-PlanCake-Task=\"true\">\n"
        );

        result.Html.Split("data-plancake-task=").Should().HaveCount(2, "only the real task has the marker");
        result.Html.Should().Contain("""<input type="checkbox" class="task-list-item-checkbox" x-Data-PlanCake-Task="true">""");
    }

    [Fact]
    public void Render_RawHtmlTextAndOtherAttributes_AreKept() {
        const string html = """<div class="data-lines" data-linesx="1" title="data-note">no data-lines-like name</div>""";

        Render(html + "\n").Html.Should().Contain(html);
    }

    [Fact]
    public void Render_GenericAttributes_ProtocolNamesAreDropped() {
        var result = Render("# Title {data-lines=40-40 data-plancake-task=true}\n\nText *em*{Data-Note=0} [l](u){data-mixed=true}\n");

        result.Html.Should().Contain("""<h1 id="title" dir="auto" data-lines="1-1">Title</h1>""")
            .And.Contain("<em>em</em>")
            .And.Contain("""<a href="u">l</a>""");
        result.Html.Should().NotContain("40-40").And.NotContain("Data-Note").And.NotContain("data-mixed")
            .And.NotContain("data-plancake-task");
    }

    [Fact]
    public void Render_NoteText_GenericAttributesCannotFakeABlock() {
        var result = Render("Text\n[usernote][link](u){data-lines=1-1} *em*{data-note=0}[/usernote]\n");

        result.Html.Should().Contain(NoteDiv(0, """<a href="u">link</a> <em>em</em>"""));
    }

    [Fact]
    public void Render_NoHeading_HasNoTitle() => Render("Just text.\n").Title.Should().BeNull();

    // Note placement

    [Fact]
    public void Render_NoteAfterParagraph_FollowsIt() {
        var result = Render("Para\n[usernote]hello[/usernote]\n\nNext\n");

        result.Html.Should().Contain($"<p dir=\"auto\" data-lines=\"1-1\">Para</p>\n{NoteDiv(0, "hello")}\n<p");
        result.Notes.Should().ContainSingle().Which.Block!.Lines.Should().Be("1-1");
    }

    [Fact]
    public void Render_NoteAfterHeading_FollowsIt() {
        var result = Render("# Title\n[usernote]n[/usernote]\n\nText\n");

        result.Html.Should().Contain($"Title</h1>\n{NoteDiv(0, "n")}");
        result.Notes[0].Block!.Kind.Should().Be(BlockKind.Heading);
    }

    [Fact]
    public void Render_NoteAfterCodeBlock_FollowsItsClosingFence() {
        var result = Render("```\ncode\n```\n[usernote]n[/usernote]\n");

        result.Html.Should().Contain($"</code></pre>\n{NoteDiv(0, "n")}");
        result.Notes[0].Block!.Lines.Should().Be("1-3");
    }

    [Fact]
    public void Render_NoteAfterTableRow_GoesIntoTheRowsLastCell() {
        var result = Render("| A | B |\n|---|---|\n| 1 | 2 |\n[usernote]n[/usernote]\n| 3 | 4 |\n");

        result.Html.Should().Contain($"<td dir=\"auto\">1</td>\n<td dir=\"auto\"><p>2</p>\n{NoteDiv(0, "n")}\n</td>");
        result.Notes[0].Block!.Should().Be(new BlockInfo(BlockKind.TableRow, 3, 3, "1 | 2", "1 | 2"));
        Ranges(result, BlockKind.TableRow).Should().Equal("1-1", "3-3", "5-5");
    }

    [Fact]
    public void Render_NoteAfterNestedListItem_GoesInsideThatItem() {
        var result = Render("- parent\n  - child\n    [usernote]n[/usernote]\n- next\n");

        result.Html.Should().Contain($"<li dir=\"auto\" data-lines=\"2-2\">child{NoteDiv(0, "n")}\n</li>");
        result.Notes[0].Block!.Lines.Should().Be("2-2");
    }

    [Fact]
    public void Render_NoteAfterParentItem_GoesBeforeItsNestedList() {
        var result = Render("- parent\n  [usernote]n[/usernote]\n  - child\n");

        result.Html.Should().Contain($"<li dir=\"auto\" data-lines=\"1-1\">parent{NoteDiv(0, "n")}\n<ul");
    }

    [Fact]
    public void Render_NoteAfterQuotedParagraph_StaysInTheQuote() {
        var result = Render("> quoted\n> [usernote]n[/usernote]\n\nAfter\n");

        result.Html.Should().Contain($"quoted</p>\n{NoteDiv(0, "n")}\n</blockquote>");
    }

    [Fact]
    public void Render_NoteBeforeTheFirstBlock_IsAtTheTopWithoutAnchor() {
        var result = Render("[usernote]first[/usernote]\n[usernote]second[/usernote]\n# Title\n");

        result.Html.Should().StartWith($"{NoteDiv(0, "first")}\n{NoteDiv(1, "second")}\n<h1");
        result.Notes.Should().AllSatisfy(note => note.Block.Should().BeNull());
    }

    [Fact]
    public void Render_UnterminatedNote_IsAnchoredByItsStartLine() {
        var result = Render("One\n\nTwo\n[usernote]runs on\n\nand on\n");

        result.Notes[0].Note.Unterminated.Should().BeTrue();
        result.Notes[0].Block!.Lines.Should().Be("3-3");
        result.Html.Should().EndWith(
            $"Two</p>\n{NoteDivHtml(0, "<p dir=\"auto\">runs on</p>\n<p dir=\"auto\">and on</p>")}\n"
        );
    }

    [Fact]
    public void Render_NoteWrittenInsideAParagraph_IsAnchoredToThatParagraph() {
        var result = Render("Line one\n[usernote]n[/usernote]\nLine two\n\nNext\n");

        result.Notes[0].Block!.Lines.Should().Be("1-3");
    }

    [Fact]
    public void Render_TwoNotesOnOneBlock_StackInSourceOrder() {
        var result = Render("Para\n[usernote]one[/usernote]\n[usernote]two[/usernote]\n\nNext\n");

        result.Html.Should().Contain($"Para</p>\n{NoteDiv(0, "one")}\n{NoteDiv(1, "two")}\n<p");
    }

    [Fact]
    public void Render_ManyNotes_EachFollowsItsOwnBlock() {
        var source = String.Concat(Enumerable.Range(0, 500).Select(i => $"Para {i}.\n[usernote]note {i}[/usernote]\n\n"));

        var result = Render(source);

        result.Notes.Should().HaveCount(500);
        result.Notes.Should().AllSatisfy(note => note.Block!.Excerpt.Should().Be($"Para {note.Index}."));
        result.Html.Should().Contain($"Para 499.</p>\n{NoteDiv(499, "note 499")}\n");
        result.Html.IndexOf(NoteDiv(0, "note 0"), StringComparison.Ordinal)
            .Should().BeLessThan(result.Html.IndexOf("Para 1.", StringComparison.Ordinal));
    }

    [Fact]
    public void Render_NoteAfterAFootnote_IsAnchoredToTheFootnoteNotTheLastBlock() {
        // Footnotes are rendered at the end, out of line order.
        const string source = "Text[^1].\n\n[^1]: The footnote.\n[usernote]on the footnote[/usernote]\n\nLast para.\n[usernote]on the last[/usernote]\n";

        var result = Render(source);

        result.Notes[0].Block!.Excerpt.Should().StartWith("The footnote.");
        result.Notes[1].Block!.Excerpt.Should().Be("Last para.");
    }

    [Fact]
    public void Render_NoteText_IsEncodedAndLineBreaksBecomeBr() {
        var result = Render("Para\n[usernote]a <b> & \"q\" 'r'\nnext[/usernote]\n");

        result.Html.Should().Contain(NoteDiv(0, "a &lt;b&gt; &amp; &quot;q&quot; 'r'<br />\nnext"));
        result.Html.Should().NotContain("<b>");
    }

    // Markdown in notes

    [Fact]
    public void Render_NoteMarkdown_InlineCodeAndEmphasisRender() {
        var result = Render("Para\n[usernote]Use `dotnet test`, *not* **that**[/usernote]\n");

        result.Html.Should().Contain(
            NoteDiv(0, "Use <code>dotnet test</code>, <em>not</em> <strong>that</strong>")
        );
        result.Html.Should().NotContain("`");
    }

    [Fact]
    public void Render_NoteMarkdown_HeadingBecomesABoldParagraph() {
        var result = Render("# Title\n[usernote]## Why\nBecause.[/usernote]\n");

        result.Html.Should().Contain(
            NoteDivHtml(0, "<p dir=\"auto\"><strong>Why</strong></p>\n<p dir=\"auto\">Because.</p>")
        );
        result.Html.Should().NotContain("<h2");
        result.Blocks.Should().ContainSingle("a heading in a note is not a block of the document");
        result.Title.Should().Be("Title");
    }

    [Fact]
    public void Render_NoteMarkdown_ListAndCodeBlockRender() {
        var result = Render("Para\n[usernote]Steps:\n\n- one\n- two\n\n```\nx = 1\n```[/usernote]\n");

        var note = NoteHtml(result.Html, 0);
        note.Should().Contain("<ul dir=\"auto\">\n<li dir=\"auto\">one</li>\n<li dir=\"auto\">two</li>\n</ul>");
        note.Should().Contain("<pre dir=\"auto\"><code>x = 1\n</code></pre>");
    }

    [Fact]
    public void Render_NoteMarkdown_InnerBlocksAreNotAnnotatable() {
        var result = Render("Para\n[usernote]## Heading\n\n- item\n\n| a |\n|---|\n| b |\n\n```\ncode\n```[/usernote]\n");

        NoteHtml(result.Html, 0).Should().NotContain("data-lines");
        result.Blocks.Should().ContainSingle().Which.Kind.Should().Be(BlockKind.Paragraph);
    }

    [Fact]
    public void Render_NoteMarkdown_LinkStaysALinkInAUserNote() {
        var result = Render("Para\n[usernote]See [the docs](https://example.com).[/usernote]\n");

        NoteHtml(result.Html, 0).Should().Contain("<a href=\"https://example.com\">the docs</a>");
    }

    [Fact]
    public void NotePlainText_DropsTheMarkdownPunctuation() {
        MarkdownRenderer.NotePlainText("Use `dotnet test`, **not** [that](https://example.com)")
            .Should().Be("Use dotnet test, not that");
    }

    [Fact]
    public void Render_LocalizedStrings_AreWrittenEncoded() {
        var strings = new RenderStrings("Заметка \"пользователя\"");

        var result = Render("Para\n[usernote]n[/usernote]\n", strings: strings);

        result.Html.Should().Contain("""aria-label="Заметка &quot;пользователя&quot;" """);
    }

    /// <summary>The HTML of the user note with this <c>data-note</c> index.</summary>
    private static string NoteHtml(string html, int index) {
        var start = html.IndexOf($"data-note=\"{index}\"", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);

        return html[start..html.IndexOf("</div>", start, StringComparison.Ordinal)];
    }

    // Excerpts

    [Fact]
    public void Render_Excerpt_IsPlainTextOnOneLine() {
        var result = Render("Some **bold** and `code`\nwith a [link](https://example.com).\n");

        var block = result.Blocks.Should().ContainSingle().Subject;
        block.Text.Should().Be("Some bold and code\nwith a link.");
        block.Excerpt.Should().Be("Some bold and code with a link.");
    }

    [Fact]
    public void Render_LongExcerpt_IsTruncatedTo80CharactersWithAnEllipsis() {
        var words = String.Join(' ', Enumerable.Repeat("word", 30));

        var excerpt = Render(words + "\n").Blocks[0].Excerpt;

        excerpt.Should().HaveLength(MarkdownRenderer.ExcerptLength).And.EndWith("…");
        words.Should().StartWith(excerpt[..^1].TrimEnd());
    }

    [Fact]
    public void Excerpt_LongText_IsCutAtTheEndOfAWord() {
        // Words of eight letters: the 79 characters before the ellipsis end inside the ninth word.
        var text = String.Join(' ', Enumerable.Repeat("abcdefgh", 12));

        var excerpt = MarkdownRenderer.Excerpt(text);

        excerpt.Should().Be(String.Join(' ', Enumerable.Repeat("abcdefgh", 8)) + "…");
    }

    [Fact]
    public void Excerpt_WordLongerThanHalfTheExcerpt_IsCutWhereItMust() {
        var text = "See https://example.com/" + new string('a', 100);

        var excerpt = MarkdownRenderer.Excerpt(text);

        excerpt.Should().HaveLength(MarkdownRenderer.ExcerptLength).And.EndWith("a…");
    }

    [Theory]
    [InlineData("Check this. And that.", "Check this.")]
    [InlineData("Really? Yes.", "Really?")]
    [InlineData("Stop! Now.", "Stop!")]
    [InlineData("He said \"go.\" Then left.", "He said \"go.\"")]
    [InlineData("Use it (see the plan.) Then more.", "Use it (see the plan.)")]
    [InlineData("Wait… What?", "Wait…")]
    [InlineData("One sentence without an end", "One sentence without an end")]
    [InlineData("Ends with a stop.", "Ends with a stop.")]
    [InlineData("Use e.g. this one. Then more.", "Use e.g. this one.")]
    [InlineData("Version 1.2 is out. Upgrade.", "Version 1.2 is out.")]
    [InlineData("First line\nsecond line. Third.", "First line second line.")]
    [InlineData("これは文です。次の文。", "これは文です。")]
    [InlineData("זה משפט. ועוד אחד.", "זה משפט.")]
    public void FirstSentence_EndsAtTheFirstSentenceEnd(string text, string expected) =>
        MarkdownRenderer.FirstSentence(text).Should().Be(expected);

    [Fact]
    public void FirstSentence_TooLong_IsCutAtTheEndOfAWord() {
        var text = String.Join(' ', Enumerable.Repeat("abcdefghi", 20)) + ". Next.";

        var sentence = MarkdownRenderer.FirstSentence(text);

        sentence.Should().EndWith("abcdefghi…");
        sentence.Length.Should().BeLessThanOrEqualTo(MarkdownRenderer.SentenceLength);
    }

    // Footnotes in plain text

    [Fact]
    public void Render_Footnote_ExcerptAndTextLeaveTheBackLinkOut() {
        var result = Render("Text[^1].\n\n[^1]: The footnote.\n");

        var footnote = result.Blocks.Single(block => block.StartLine == 3);
        footnote.Text.Should().Be("The footnote.");
        footnote.Excerpt.Should().Be("The footnote.");
    }

    [Fact]
    public void Render_FootnoteReference_IsItsNumberInBracketsInPlainText() {
        var result = Render("Text[^note] here.\n\n[^note]: The footnote.\n");

        var paragraph = result.Blocks.Single(block => block.StartLine == 1);
        paragraph.Text.Should().Be("Text[1] here.");
        paragraph.Excerpt.Should().NotContain("<");
        result.Html.Should().Contain("footnote-back-ref", "the page itself still links back");
    }

    [Fact]
    public void Excerpt_ShortText_IsKeptWhole() {
        var text = new string('a', MarkdownRenderer.ExcerptLength);

        MarkdownRenderer.Excerpt(text).Should().Be(text);
        MarkdownRenderer.Excerpt(text + "b").Should().Be(new string('a', 79) + "…");
    }

    // Export

    [Fact]
    public void Render_Export_IsAStandaloneDocumentUnderAStrictPolicy() {
        const string source =
            "# Plan\n\nText\n[usernote]a note[/usernote]\n\n<script>alert(1)</script>\n\n- [ ] task\n";

        var html = Render(source, RenderMode.Export, documentLanguage: "fr").Html;

        const string meta = """<meta http-equiv="Content-Security-Policy" content="default-src 'none'; """
            + """script-src 'none'; style-src 'unsafe-inline'; img-src * data:; base-uri 'none'; form-action 'none'">""";
        html.Should().StartWith("<!DOCTYPE html>\n<html lang=\"fr\">");
        html.Should().Contain(meta).And.Contain("<title>Plan</title>");
        html.IndexOf("<script>alert(1)</script>", StringComparison.Ordinal)
            .Should().BeGreaterThan(html.IndexOf(meta, StringComparison.Ordinal));
        html.Should().Contain($"""<div class="note" {NoteRoles} dir="auto"><p dir="auto">a note</p></div>""");
        html.Should().NotContain("data-note").And.Contain("disabled=\"disabled\"");
    }

    [Fact]
    public void Render_Export_RendersTheNoteMarkdown() {
        var html = Render("Text\n[usernote]Use `code`, *really*[/usernote]\n", RenderMode.Export).Html;

        html.Should().Contain(
            $"""<div class="note" {NoteRoles} dir="auto"><p dir="auto">Use <code>code</code>, <em>really</em></p></div>"""
        );
    }
}
