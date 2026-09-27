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
    public void Parse_NoteInsideAFencedCodeBlock_IsFoundAndTheFenceKept() {
        var result = Parse("```\ncode\n[usernote]in code[/usernote]\nmore\n```\n");

        result.Notes.Should().ContainSingle().Which.StartLine.Should().Be(3);
        result.StrippedSource.Should().Be("```\ncode\nmore\n```\n");
        result.LineMap.Should().Equal(1, 2, 4, 5);
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
        var result = Parse("    [usernote]first\n  second\nthird[/usernote]\n");

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
