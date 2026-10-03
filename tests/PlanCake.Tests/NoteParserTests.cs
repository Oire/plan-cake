using AwesomeAssertions;
using Oire.PlanCake.Notes;
using Xunit;

namespace Oire.PlanCake.Tests;

public class NoteParserTests {
    private static readonly NoteMarkers _singleToken = new("!USERNOTE!");

    private static NoteParseResult Parse(string source) => NoteParser.Parse(source, NoteMarkers.Default);

    [Fact]
    public void Parse_PairedNoteOnItsOwnLine_IsFoundAndItsLineRemoved() {
        const string source = "Para one.\n[usernote]Hi there[/usernote]\nPara two.\n";

        var result = Parse(source);

        var note = result.Notes.Should().ContainSingle().Subject;
        note.Text.Should().Be("Hi there");
        note.StartLine.Should().Be(2);
        note.EndLine.Should().Be(2);
        note.Unterminated.Should().BeFalse();
        source.Substring(note.Start, note.Length).Should().Be("[usernote]Hi there[/usernote]");
        result.StrippedSource.Should().Be("Para one.\nPara two.\n");
        result.LineMap.Should().Equal(1, 3);
    }

    [Fact]
    public void Parse_NoteSpanningSeveralLines_KeepsItsLineBreaks() {
        const string source = "Intro\n[usernote]first\nsecond\nthird[/usernote]\nAfter\n";

        var result = Parse(source);

        var note = result.Notes.Should().ContainSingle().Subject;
        note.Text.Should().Be("first\nsecond\nthird");
        note.StartLine.Should().Be(2);
        note.EndLine.Should().Be(4);
        result.StrippedSource.Should().Be("Intro\nAfter\n");
        result.LineMap.Should().Equal(1, 5);
    }

    [Fact]
    public void Parse_NoteMidLine_CutsOnlyTheNoteOut() {
        var result = Parse("Before [usernote] mid [/usernote] after.\nNext\n");

        result.Notes.Should().ContainSingle().Which.Text.Should().Be("mid");
        result.StrippedSource.Should().Be("Before  after.\nNext\n");
        result.LineMap.Should().Equal(1, 2);
    }

    [Fact]
    public void Parse_NoteAtTheEndOfALine_LeavesNoTrailingSpaceBehind() {
        // Two trailing spaces would turn the line into a Markdown hard line break.
        var result = Parse("Some text  [usernote]note[/usernote]\nmore text\n");

        result.StrippedSource.Should().Be("Some text\nmore text\n");
    }

    [Fact]
    public void Parse_SeveralNotesOnOneLine_AreAllFoundInOrder() {
        var result = Parse("A [usernote]one[/usernote] B [usernote]two[/usernote]\n");

        result.Notes.Select(note => note.Text).Should().Equal("one", "two");
        result.Notes.Should().AllSatisfy(note => note.StartLine.Should().Be(1));
        result.StrippedSource.Should().Be("A  B\n");
    }

    [Fact]
    public void Parse_SingleTokenMode_RunsToTheEndOfTheLine() {
        const string source = "Text\n!USERNOTE! a note [/usernote] stays\nMore !USERNOTE! cut here\nNext\n";

        var result = NoteParser.Parse(source, _singleToken);

        result.Notes.Select(note => note.Text).Should().Equal("a note [/usernote] stays", "cut here");
        result.Notes[0].StartLine.Should().Be(2);
        result.Notes[0].EndLine.Should().Be(2);
        result.Notes[0].Unterminated.Should().BeFalse();
        source.Substring(result.Notes[1].Start, result.Notes[1].Length).Should().Be("!USERNOTE! cut here");
        result.StrippedSource.Should().Be("Text\nMore\nNext\n");
        result.LineMap.Should().Equal(1, 3, 4);
    }

    [Fact]
    public void Parse_MarkerInsideAFencedCodeBlock_IsCodeNotANote() {
        const string source = "```\ncode\n[usernote]in code[/usernote]\nmore\n```\n";

        var result = Parse(source);

        result.Notes.Should().BeEmpty();
        result.StrippedSource.Should().Be(source);
        result.LineMap.Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public void Parse_NotesInsideATable_LeaveTheTableIntact() {
        const string source =
            "| a | b |\n|---|---|\n| 1 [usernote]cell[/usernote] | 2 |\n[usernote]row note[/usernote]\n| 3 | 4 |\n";

        var result = Parse(source);

        result.Notes.Select(note => note.Text).Should().Equal("cell", "row note");
        result.StrippedSource.Should().Be("| a | b |\n|---|---|\n| 1  | 2 |\n| 3 | 4 |\n");
        result.LineMap.Should().Equal(1, 2, 3, 5);
    }

    [Fact]
    public void Parse_LineMap_MapsStrippedLinesToOriginalOnes() {
        var result = Parse("L1\nL2\n[usernote]a\nb[/usernote]\nL5\n[usernote]c[/usernote]\nL7");

        result.StrippedSource.Should().Be("L1\nL2\nL5\nL7");
        result.LineMap.Should().Equal(1, 2, 5, 7);
        result.ToOriginalLine(3).Should().Be(5);
        result.ToOriginalLine(4).Should().Be(7);
    }

    [Fact]
    public void ToOriginalLine_OutsideTheStrippedSource_Throws() {
        var result = Parse("One\n[usernote]x[/usernote]\n");

        FluentActions.Invoking(() => result.ToOriginalLine(0)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => result.ToOriginalLine(2)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Parse_WithoutNotes_ReturnsTheSourceUnchanged() {
        const string source = "# Title\n\nText\n";

        var result = Parse(source);

        result.Notes.Should().BeEmpty();
        result.StrippedSource.Should().Be(source);
        result.LineMap.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Parse_ContinuationLines_LoseTheIndentationPlanCakeWrites() {
        const string source =
            "- item\n  - nested\n    [usernote]first line\n    second line\n      indented more[/usernote]\n";

        var result = Parse(source);

        result.Notes.Should().ContainSingle().Which.Text.Should().Be("first line\nsecond line\n  indented more");
        result.StrippedSource.Should().Be("- item\n  - nested\n");
    }

    [Fact]
    public void Parse_ContinuationLineIndentedLessThanTheFirst_LosesOnlyWhatItHas() {
        // After a paragraph: on a line of its own at the top, indented four, it would be code.
        var result = Parse("Para.\n    [usernote]first\n  second\nthird[/usernote]\n");

        result.Notes.Should().ContainSingle().Which.Text.Should().Be("first\nsecond\nthird");
    }

    [Fact]
    public void Parse_UnterminatedNote_RunsToTheEndOfTheSourceAndIsReported() {
        const string source = "Para\n[usernote]runs on\nto the end\n";

        var result = Parse(source);

        var note = result.Notes.Should().ContainSingle().Subject;
        note.Unterminated.Should().BeTrue();
        note.Text.Should().Be("runs on\nto the end");
        note.StartLine.Should().Be(2);
        note.EndLine.Should().Be(3);
        note.End.Should().Be(source.Length);
        result.HasUnterminated.Should().BeTrue();
        result.StrippedSource.Should().Be("Para\n");
        result.LineMap.Should().Equal(1);
    }

    [Fact]
    public void Parse_ClosingMarkerWithoutAnOpeningOne_IsLeftAsText() {
        const string source = "Text [/usernote] here\n";

        var result = Parse(source);

        result.Notes.Should().BeEmpty();
        result.HasUnterminated.Should().BeFalse();
        result.StrippedSource.Should().Be(source);
    }

    [Fact]
    public void Parse_EmptyNote_IsStillANote() {
        var result = Parse("[usernote][/usernote]\nText\n");

        result.Notes.Should().ContainSingle().Which.Text.Should().BeEmpty();
        result.StrippedSource.Should().Be("Text\n");
        result.LineMap.Should().Equal(2);
    }

    [Fact]
    public void Parse_CrLfSource_KeepsCrLfAndNormalizesNoteLineBreaks() {
        const string source = "One\r\n[usernote]a\r\nb[/usernote]\r\nTwo\r\n";

        var result = Parse(source);

        var note = result.Notes.Should().ContainSingle().Subject;
        note.Text.Should().Be("a\nb");
        note.StartLine.Should().Be(2);
        note.EndLine.Should().Be(3);
        result.StrippedSource.Should().Be("One\r\nTwo\r\n");
        result.LineMap.Should().Equal(1, 4);
    }

    [Fact]
    public void Parse_HandWrittenNoteInsideAQuote_RemovesTheWholeLine() {
        var result = Parse("> Quoted para.\n> [usernote]hand note[/usernote]\n> More.\n");

        result.Notes.Should().ContainSingle().Which.Text.Should().Be("hand note");
        result.StrippedSource.Should().Be("> Quoted para.\n> More.\n");
        result.LineMap.Should().Equal(1, 3);
    }

    // Markers shown as code are not notes.

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Parse_ReadmeThatShowsTheMarkers_HasNoNotes(string lineEnding) {
        var source = String.Join(lineEnding,
            "# PlanCake",
            "",
            "Notes go between a pair of markers",
            "(`[usernote]` … `[/usernote]` by default).",
            "",
            "```markdown",
            "Back up the database.",
            "[usernote]Also back up the uploads folder.[/usernote]",
            "```",
            "",
            "Write ``[usernote]`` to start one, then `[/usernote]`.",
            "",
            "~~~json",
            "{ \"noteMarkers\": [\"[usernote]...[/usernote]\"] }",
            "~~~",
            ""
        );

        var result = Parse(source);

        result.Notes.Should().BeEmpty();
        result.StrippedSource.Should().Be(source);
    }

    [Fact]
    public void Parse_OpeningMarkerInCodeOnly_DoesNotSwallowTheRealNoteAfterIt() {
        const string source = "Start a note with `[usernote]`.\n\nPara.\n[usernote]real[/usernote]\n";

        var result = Parse(source);

        var note = result.Notes.Should().ContainSingle().Subject;
        note.Text.Should().Be("real");
        note.StartLine.Should().Be(4);
        note.Unterminated.Should().BeFalse();
    }

    [Theory]
    [InlineData("A ` stray backtick [usernote]note[/usernote]\n")]
    [InlineData("Escaped \\`[usernote]note[/usernote]` here\n")]
    [InlineData("`code` then [usernote]note[/usernote]\n")]
    [InlineData("``a`b`` then [usernote]note[/usernote]\n")]
    public void Parse_MarkerOutsideAnyCodeSpan_IsANote(string source) {
        Parse(source).Notes.Should().ContainSingle().Which.Text.Should().Be("note");
    }

    [Fact]
    public void Parse_BacktickInANote_DoesNotHideTheNextNote() {
        const string source = "Para.\n[usernote]the ` key[/usernote]\n[usernote]use `x` here[/usernote]\n";

        Parse(source).Notes.Select(note => note.Text).Should().Equal("the ` key", "use `x` here");
    }

    [Fact]
    public void Parse_FenceInsideANote_DoesNotTurnTheRestIntoCode() {
        const string source = "Para.\n[usernote]Try:\n```\nrun()\n```[/usernote]\n\nNext.\n[usernote]second[/usernote]\n";

        Parse(source).Notes.Select(note => note.Text).Should().Equal("Try:\n```\nrun()\n```", "second");
    }

    [Fact]
    public void Parse_NoteAfterAFencedCodeBlock_IsFound() {
        const string source = "```js\n[usernote]shown[/usernote]\n```\n[usernote]real[/usernote]\n";

        var result = Parse(source);

        result.Notes.Should().ContainSingle().Which.StartLine.Should().Be(4);
        result.StrippedSource.Should().Be("```js\n[usernote]shown[/usernote]\n```\n");
    }

    [Theory]
    [InlineData("- item\n  ```\n  [usernote]x[/usernote]\n  ```\n")]
    [InlineData("1. ```\n   [usernote]x[/usernote]\n   ```\n")]
    [InlineData("> ```\n> [usernote]x[/usernote]\n> ```\n")]
    [InlineData("- a\n  - b\n\n        ```\n        [usernote]x[/usernote]\n        ```\n")]
    [InlineData("````\n```\n[usernote]x[/usernote]\n````\n")]
    [InlineData("```\n```js\n[usernote]x[/usernote]\n")]
    public void Parse_MarkerInAFenceInsideAContainer_IsCode(string source) {
        Parse(source).Notes.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ShortOrIndentedClosingFence_DoesNotCloseTheFence() {
        const string source = "````\n```\n        ````\n[usernote]x[/usernote]\n````\n[usernote]real[/usernote]\n";

        Parse(source).Notes.Should().ContainSingle().Which.Text.Should().Be("real");
    }

    [Fact]
    public void Parse_MarkerInsideAnIndentedCodeBlock_IsCode() {
        const string source = "Example:\n\n    first\n    [usernote]shown[/usernote]\n    last\n\nAfter.\n";

        var result = Parse(source);

        result.Notes.Should().BeEmpty();
        result.StrippedSource.Should().Be(source);
    }

    [Fact]
    public void Parse_InlineMarkerOnAnIndentedCodeLine_IsCode() {
        Parse("Example:\n\n    var x = \"[usernote]\";\n    var y = \"[/usernote]\";\n").Notes.Should().BeEmpty();
    }

    // PlanCake writes a note on an indented code block right after it, at its indentation.
    [Fact]
    public void Parse_NoteRightAfterAnIndentedCodeBlock_IsANote() {
        const string source = "Example:\n\n    code\n    more\n    [usernote]on the code[/usernote]\n    [usernote]second[/usernote]\n\nAfter.\n";

        var result = Parse(source);

        result.Notes.Select(note => note.Text).Should().Equal("on the code", "second");
        result.StrippedSource.Should().Be("Example:\n\n    code\n    more\n\nAfter.\n");
    }

    [Fact]
    public void Parse_NoteInANestedListItem_IsNotTakenForCode() {
        const string source = "- a\n  - b\n    [usernote]on b[/usernote]\n";

        Parse(source).Notes.Should().ContainSingle().Which.Text.Should().Be("on b");
    }

    // Raw HTML blocks of CommonMark types 1 (pre, script, style, textarea) and 2 (comments).

    [Theory]
    [InlineData("<pre>\n[usernote]x[/usernote]\n</pre>\n")]
    [InlineData("<script>\nvar a = \"[usernote]\";\nvar b = \"[/usernote]\";\n</script>\n")]
    [InlineData("<style>\n/* [usernote]x[/usernote] */\n</style>\n")]
    [InlineData("<TEXTAREA rows=\"3\">\n[usernote]x[/usernote]\n</textarea>\n")]
    [InlineData("<pre>[usernote]x[/usernote]\n</pre>\n")]
    [InlineData("<!--\n[usernote]x[/usernote]\n-->\n")]
    [InlineData("<!-- [usernote]x[/usernote] -->\n")]
    [InlineData("Para.\n<pre>\n\n[usernote]x[/usernote]\n</pre>\n")]
    [InlineData("- item\n\n  <pre>\n  [usernote]x[/usernote]\n  </pre>\n")]
    [InlineData("<!-- never closed\n[usernote]x[/usernote]\n")]
    public void Parse_MarkerInARawHtmlBlock_IsNotANote(string source) {
        var result = Parse(source);

        result.Notes.Should().BeEmpty();
        result.StrippedSource.Should().Be(source);
    }

    [Theory]
    [InlineData("<pre>\ncode\n</pre>\n")]
    [InlineData("<script>\nrun();\n</script>\n")]
    [InlineData("<style>\np {}\n</style>\n")]
    [InlineData("<textarea>\ntext\n</textarea>\n")]
    [InlineData("<!--\ncomment\n-->\n")]
    [InlineData("<!-- [usernote]shown[/usernote] -->\n")]
    public void Parse_NoteRightAfterARawHtmlBlock_IsANote(string block) {
        var source = block + "[usernote]after[/usernote]\nNext.\n";

        var result = Parse(source);

        var note = result.Notes.Should().ContainSingle().Subject;
        note.Text.Should().Be("after");
        note.StartLine.Should().Be(block.Count(c => c == '\n') + 1);
        result.StrippedSource.Should().Be(block + "Next.\n");
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Parse_RawHtmlBlocks_KeepTheLineMap(string lineEnding) {
        var source = String.Join(lineEnding,
            "Para.",
            "[usernote]before[/usernote]",
            "<pre>",
            "[usernote]shown[/usernote]",
            "</pre>",
            "[usernote]after[/usernote]",
            "<!-- [usernote]shown[/usernote]",
            "-->",
            "Last.",
            ""
        );

        var result = Parse(source);

        result.Notes.Select(note => note.Text).Should().Equal("before", "after");
        result.Notes.Select(note => note.StartLine).Should().Equal(2, 6);
        result.LineMap.Should().Equal(1, 3, 4, 5, 7, 8, 9);
    }

    [Fact]
    public void Parse_NoteStartingTheLineOfAnHtmlTag_IsANote() {
        // Without the note the line opens a <pre> block; in the file it does not.
        var result = Parse("[usernote]n[/usernote]<pre>\nx\n</pre>\n");

        result.Notes.Should().ContainSingle().Which.Text.Should().Be("n");
    }

    [Theory]
    [InlineData("Use <code>[usernote]x[/usernote]</code> here.\n")]
    [InlineData("A <!-- c --> [usernote]x[/usernote] inline.\n")]
    [InlineData("<div>\n[usernote]x[/usernote]\n</div>\n")]
    [InlineData("<details>\n<summary>More</summary>\n[usernote]x[/usernote]\n</details>\n")]
    public void Parse_MarkerInInlineHtmlOrOtherHtmlBlocks_IsANote(string source) {
        Parse(source).Notes.Should().ContainSingle().Which.Text.Should().Be("x");
    }

    [Fact]
    public void Parse_CommentMarkers_AreNotHiddenByComments() {
        var markers = new NoteMarkers("<!--note", "-->");
        const string source = "Para.\n<!-- plain comment\n<!--note inside-->\n-->\n<!--note own line-->\n<pre>\n<!--note shown-->\n</pre>\n";

        var result = NoteParser.Parse(source, markers);

        result.Notes.Select(note => note.Text).Should().Equal("inside", "own line");
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Parse_SingleTokenMarkersInRawHtml_AreNotNotes(string lineEnding) {
        var source = String.Join(lineEnding,
            "<pre>",
            "!USERNOTE! example",
            "</pre>",
            "!USERNOTE! real",
            "<!--",
            "!USERNOTE! hidden",
            "-->",
            ""
        );

        var note = NoteParser.Parse(source, _singleToken).Notes.Should().ContainSingle().Subject;
        note.Text.Should().Be("real");
        note.StartLine.Should().Be(4);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Parse_SingleTokenMarkersInCode_AreNotNotes(string lineEnding) {
        var source = String.Join(lineEnding,
            "Mark a note with `!USERNOTE!` at the start of a line.",
            "",
            "```",
            "!USERNOTE! example",
            "```",
            "",
            "    !USERNOTE! indented example",
            "",
            "Para.",
            "!USERNOTE! real",
            ""
        );

        var result = NoteParser.Parse(source, _singleToken);

        var note = result.Notes.Should().ContainSingle().Subject;
        note.Text.Should().Be("real");
        note.StartLine.Should().Be(10);
        result.StrippedSource.Should().Be(source.Replace("!USERNOTE! real" + lineEnding, "", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_WithInvalidMarkers_Throws() {
        FluentActions.Invoking(() => NoteParser.Parse("text", new NoteMarkers("")))
            .Should().Throw<ArgumentException>();
    }
}

public class NoteMarkersTests {
    [Fact]
    public void Default_IsTheUsernotePair() {
        NoteMarkers.Default.Opening.Should().Be("[usernote]");
        NoteMarkers.Default.Closing.Should().Be("[/usernote]");
        NoteMarkers.Default.IsSingleToken.Should().BeFalse();
        NoteMarkers.Default.Validate().Should().Be(NoteMarkersError.None);
    }

    [Fact]
    public void Validate_SingleToken_IsValid() {
        var markers = new NoteMarkers("!USERNOTE!");

        markers.IsSingleToken.Should().BeTrue();
        markers.Validate().Should().Be(NoteMarkersError.None);
    }

    [Theory]
    [InlineData("", "", "EmptyOpening")]
    [InlineData("", "[/n]", "EmptyOpening")]
    [InlineData(" [n]", "[/n]", "SurroundingWhitespace")]
    [InlineData("[n]\t", "[/n]", "SurroundingWhitespace")]
    [InlineData("[n]", "[/n] ", "SurroundingWhitespace")]
    [InlineData("[n\n]", "[/n]", "LineBreak")]
    [InlineData("[n]", "[/n\r]", "LineBreak")]
    [InlineData("[n]", "[n]", "ClosingSameAsOpening")]
    public void Validate_RejectsUnusableMarkers(string opening, string closing, string expected) {
        // NoteMarkersError is internal, and a public test method cannot take it as a parameter.
        new NoteMarkers(opening, closing).Validate().ToString().Should().Be(expected);
    }
}
