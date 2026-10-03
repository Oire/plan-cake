using System.Text.Json;
using AwesomeAssertions;
using Oire.PlanCake.Notes;
using Oire.PlanCake.Rendering;
using Oire.PlanCake.Ui;
using Xunit;

namespace Oire.PlanCake.Tests;

/// <summary>
/// app.js reads these exact property names, so a renamed property silently breaks the page.
/// </summary>
public class PageMessagesTests {
    private static JsonElement Parse(string json) {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.Clone();
    }

    [Fact]
    public void Serialize_Render_UsesTheProtocolNames() {
        var json = PageMessages.Serialize(new RenderMessage("<p>Hi</p>", 3, "en", "Plan", new PageFocus(Lines: "4-6")));
        var message = Parse(json);

        message.GetProperty("type").GetString().Should().Be("render");
        message.GetProperty("html").GetString().Should().Be("<p>Hi</p>");
        message.GetProperty("generation").GetInt32().Should().Be(3);
        message.GetProperty("documentLang").GetString().Should().Be("en");
        message.GetProperty("title").GetString().Should().Be("Plan");
        message.GetProperty("focus").GetProperty("lines").GetString().Should().Be("4-6");
        message.GetProperty("focus").TryGetProperty("note", out _).Should().BeFalse();
    }

    [Fact]
    public void Serialize_RenderWithoutFocus_LeavesFocusOut() {
        var message = Parse(PageMessages.Serialize(new RenderMessage("", 1, "ru", "Plan", null)));

        message.TryGetProperty("focus", out _).Should().BeFalse();
    }

    [Fact]
    public void Serialize_Strings_CarriesLanguageAndDirection() {
        var message = Parse(PageMessages.Serialize(new StringsMessage("he-IL", "rtl", "אין קובץ פתוח.", ["F1"])));

        message.GetProperty("type").GetString().Should().Be("strings");
        message.GetProperty("uiLang").GetString().Should().Be("he-IL");
        message.GetProperty("uiDir").GetString().Should().Be("rtl");
        message.GetProperty("noDocument").GetString().Should().Be("אין קובץ פתוח.");
        message.GetProperty("noDocumentHints").EnumerateArray().Select(hint => hint.GetString())
            .Should().Equal("F1");
    }

    [Fact]
    public void Serialize_FocusAndNavigationMessages_HaveTheirTypes() {
        Parse(PageMessages.Serialize(new FocusNoteMessage(2))).GetProperty("note").GetInt32().Should().Be(2);
        Parse(PageMessages.Serialize(new NextNoteMessage())).GetProperty("type").GetString().Should().Be("nextNote");
        Parse(PageMessages.Serialize(new PreviousNoteMessage())).GetProperty("type").GetString()
            .Should().Be("previousNote");
        Parse(PageMessages.Serialize(new NextBlockMessage())).GetProperty("type").GetString().Should().Be("nextBlock");
        Parse(PageMessages.Serialize(new PreviousBlockMessage())).GetProperty("type").GetString()
            .Should().Be("previousBlock");
    }

    [Fact]
    public void GetString_And_GetInt_ReadOnlyTheRightKinds() {
        var message = Parse("""{ "type": "position", "lines": "5-7", "note": 1, "generation": "2", "big": 1e20 }""");

        PageMessages.GetString(message, "lines").Should().Be("5-7");
        PageMessages.GetString(message, "note").Should().BeNull();
        PageMessages.GetString(message, "missing").Should().BeNull();
        PageMessages.GetInt(message, "note").Should().Be(1);
        PageMessages.GetInt(message, "generation").Should().BeNull();
        PageMessages.GetInt(message, "big").Should().BeNull();
        PageMessages.GetInt(Parse("[1]"), "note").Should().BeNull();
    }

    [Fact]
    public void GetBool_ReadsOnlyBooleans() {
        var message = Parse("""{ "type": "toggleTask", "lines": "3-3", "checked": true, "off": false, "text": "true" }""");

        PageMessages.GetBool(message, "checked").Should().BeTrue();
        PageMessages.GetBool(message, "off").Should().BeFalse();
        PageMessages.GetBool(message, "text").Should().BeNull();
        PageMessages.GetBool(message, "missing").Should().BeNull();
        PageMessages.GetBool(Parse("[true]"), "checked").Should().BeNull();
    }

    [Fact]
    public void Serialize_TaskMessages_UseTheProtocolNames() {
        var state = Parse(PageMessages.Serialize(new TaskStateMessage("3-3", Checked: false)));

        state.GetProperty("type").GetString().Should().Be("taskState");
        state.GetProperty("lines").GetString().Should().Be("3-3");
        state.GetProperty("checked").GetBoolean().Should().BeFalse();

        var focus = Parse(PageMessages.Serialize(new RenderMessage("", 1, "en", "Plan", new PageFocus("3-3", Task: true))))
            .GetProperty("focus");

        focus.GetProperty("lines").GetString().Should().Be("3-3");
        focus.GetProperty("task").GetBoolean().Should().BeTrue();
        PageMessages.ToggleTask.Should().Be("toggleTask");
    }

    [Theory]
    [InlineData(1.0, 1, 1.1)]
    [InlineData(1.0, -1, 0.9)]
    [InlineData(1.04, 1, 1.1)]
    [InlineData(2.9, 1, 3.0)]
    [InlineData(3.0, 1, 3.0)]
    [InlineData(0.5, -1, 0.5)]
    [InlineData(0.6, -1, 0.5)]
    [InlineData(2.5, 0, 1.0)]
    public void StepZoom_MovesInTenPercentStepsWithinLimits(double current, int direction, double expected) {
        DocumentView.StepZoom(current, direction).Should().BeApproximately(expected, 1e-9);
    }

    [Fact]
    public void GetDouble_And_GetRect_ReadTheContextMenuGeometry() {
        var message = Parse(
            """{ "type": "contextMenu", "rect": { "x": 10.5, "y": 20, "width": 300, "height": 40 }, "scale": 1.5 }"""
        );

        PageMessages.GetDouble(message, "scale").Should().Be(1.5);
        PageMessages.GetDouble(message, "type").Should().BeNull();
        PageMessages.GetRect(message, "rect").Should().Be(new RectangleF(10.5f, 20, 300, 40));
        PageMessages.GetRect(Parse("""{ "rect": { "x": 1, "y": 2, "width": 3 } }"""), "rect").Should().BeNull();
        PageMessages.GetRect(Parse("""{ "rect": "0,0,1,1" }"""), "rect").Should().BeNull();
        PageMessages.GetRect(message, "missing").Should().BeNull();
    }

    [Fact]
    public void MenuAnchor_ScalesCssPixelsToTheBottomLeftOfTheElement() {
        var anchor = DocumentView.MenuAnchor(new RectangleF(10, 20, 300, 40), 1.5, new Size(1000, 700));

        anchor.Should().Be(new Point(15, 90));
    }

    [Fact]
    public void MenuAnchor_ElementTallerThanTheView_UsesItsTop() {
        var anchor = DocumentView.MenuAnchor(new RectangleF(8, 100, 500, 2000), 1, new Size(1000, 700));

        anchor.Should().Be(new Point(8, 100));
    }

    [Fact]
    public void MenuAnchor_ElementScrolledPartlyOut_StaysInsideTheView() {
        DocumentView.MenuAnchor(new RectangleF(-20, -500, 100, 1500), 1, new Size(1000, 700))
            .Should().Be(new Point(0, 0));
        DocumentView.MenuAnchor(new RectangleF(10, 690, 100, 30), 1, new Size(1000, 700))
            .Should().Be(new Point(10, 690));
        DocumentView.MenuAnchor(new RectangleF(10, 20, 100, 30), Double.NaN, new Size(1000, 700))
            .Should().Be(new Point(10, 50));
    }

    // The note a page message is about: the notes list selects it

    private static readonly RenderedNote[] _notes = [
        new(0, new Note("Top", 1, 1, 0, 10, Unterminated: false), null),
        new(1, new Note("Second", 5, 5, 40, 60, Unterminated: false),
            new BlockInfo(BlockKind.Paragraph, 3, 4, "Text.", "Text.")),
    ];

    [Theory]
    [InlineData("""{ "type": "position", "note": 1, "generation": 2 }""")]
    [InlineData("""{ "type": "activateNote", "note": 1, "generation": 2 }""")]
    [InlineData("""{ "type": "contextMenu", "lines": "3-4", "note": 1, "generation": 2 }""")]
    public void FindNote_MessageAboutANote_ReturnsIt(string json) =>
        PageMessages.FindNote(Parse(json), _notes).Should().BeSameAs(_notes[1]);

    [Theory]
    [InlineData("""{ "type": "position", "lines": "3-4", "generation": 2 }""")]
    [InlineData("""{ "type": "contextMenu", "lines": "3-4", "generation": 2 }""")]
    [InlineData("""{ "type": "position", "note": 2, "generation": 2 }""")]
    [InlineData("""{ "type": "position", "note": -1, "generation": 2 }""")]
    [InlineData("""{ "type": "position", "note": "1", "generation": 2 }""")]
    public void FindNote_NoKnownNote_ReturnsNull(string json) =>
        PageMessages.FindNote(Parse(json), _notes).Should().BeNull();
}
