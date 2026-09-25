using AwesomeAssertions;
using Oire.PlanCake.Notes;
using Oire.PlanCake.Rendering;
using Oire.PlanCake.Utils.Enums;
using Xunit;

namespace Oire.PlanCake.Tests;

public class MarkdownRendererTests {
    private static readonly RenderStrings _strings = new("Note:", "user note", "unote");

    private static RenderResult Render(
        string source,
        RenderMode mode = RenderMode.Interactive,
        NoteStyle style = NoteStyle.Note,
        RenderStrings? strings = null,
        string documentLanguage = "en"
    ) => MarkdownRenderer.Render(
        source,
        new RenderOptions(NoteMarkers.Default, mode, style, strings ?? _strings, documentLanguage)
    );

    private static IEnumerable<string> Ranges(RenderResult result, BlockKind kind) =>
        result.Blocks.Where(block => block.Kind == kind).Select(block => block.Lines);

    private const string NoteRoles =
        "role=\"note\" aria-roledescription=\"user note\" aria-brailleroledescription=\"unote\"";

    private static string NoteDiv(int index, string text) =>
        $"""<div class="note" {NoteRoles} data-note="{index}" dir="auto">{text}</div>""";

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
    public void Render_TaskList_CheckboxesStayDisabled() {
        var result = Render("- [x] done\n- [ ] todo\n");

        result.Html.Should().Contain("""<input disabled="disabled" type="checkbox" checked="checked" />""")
            .And.Contain("""<input disabled="disabled" type="checkbox" />""");
        Ranges(result, BlockKind.ListItem).Should().Equal("1-1", "2-2");
    }

    [Fact]
    public void Render_RawHtml_IsKept() {
        var result = Render("Press <kbd>F9</kbd>.\n\n<details><summary>More</summary>Hidden</details>\n");

        result.Html.Should().Contain("<kbd>F9</kbd>")
            .And.Contain("<details><summary>More</summary>Hidden</details>");
        result.Blocks.Should().ContainSingle().Which.Text.Should().Be("Press F9.");
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
        result.Html.Should().EndWith($"Two</p>\n{NoteDiv(0, "runs on<br><br>and on")}\n");
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
    public void Render_NoteText_IsEncodedAndLineBreaksBecomeBr() {
        var result = Render("Para\n[usernote]a <b> & \"q\" 'r'\nnext[/usernote]\n");

        result.Html.Should().Contain(NoteDiv(0, "a &lt;b&gt; &amp; &quot;q&quot; &#39;r&#39;<br>next"));
        result.Html.Should().NotContain("<b>");
    }

    [Fact]
    public void Render_ButtonStyle_WritesALabelledButton() {
        var result = Render("Para\n[usernote]x < y[/usernote]\n", style: NoteStyle.Button);

        result.Html.Should()
            .Contain("""<button type="button" class="note" data-note="0" dir="auto">Note: x &lt; y</button>""");
    }

    [Fact]
    public void Render_LocalizedStrings_AreWrittenEncoded() {
        var strings = new RenderStrings("Заметка:", "заметка \"пользователя\"", "зам");

        var result = Render("Para\n[usernote]n[/usernote]\n", strings: strings);

        result.Html.Should()
            .Contain("""aria-roledescription="заметка &quot;пользователя&quot;" """)
            .And.Contain("""aria-brailleroledescription="зам" """);
        Render("Para\n[usernote]n[/usernote]\n", style: NoteStyle.Button, strings: strings)
            .Html.Should().Contain(">Заметка: n</button>");
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
        var words = string.Join(' ', Enumerable.Repeat("word", 30));

        var excerpt = Render(words + "\n").Blocks[0].Excerpt;

        excerpt.Should().HaveLength(MarkdownRenderer.ExcerptLength).And.EndWith("…");
        words.Should().StartWith(excerpt[..^1].TrimEnd());
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
            + """script-src 'none'; style-src 'unsafe-inline'; img-src * data:">""";
        html.Should().StartWith("<!DOCTYPE html>\n<html lang=\"fr\">");
        html.Should().Contain(meta).And.Contain("<title>Plan</title>");
        html.IndexOf("<script>alert(1)</script>", StringComparison.Ordinal)
            .Should().BeGreaterThan(html.IndexOf(meta, StringComparison.Ordinal));
        html.Should().Contain($"""<div class="note" {NoteRoles} dir="auto">a note</div>""");
        html.Should().NotContain("data-note").And.Contain("disabled=\"disabled\"");
    }
}
