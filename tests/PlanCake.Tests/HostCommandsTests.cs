using AwesomeAssertions;
using Oire.PlanCake.Ui;
using Oire.PlanCake.Utils;
using Xunit;

namespace Oire.PlanCake.Tests;

/// <summary>
/// The host shortcut table is the only way a key pressed inside the WebView2 reaches the host,
/// so a shortcut missing from it simply does nothing in the document. The menu and the shortcuts
/// dialog show their keys from it too.
/// </summary>
[Collection(LocalizationCollection.Name)]
public class HostCommandsTests {
    public HostCommandsTests() {
        // The key names are asserted in English, the language of the source strings.
        Localization.SetLanguage("en-US");
    }

    public static TheoryData<Keys, string> PlannedShortcuts => new() {
        { Keys.Control | Keys.O, nameof(HostCommand.Open) },
        { Keys.Control | Keys.V, nameof(HostCommand.OpenFromClipboard) },
        { Keys.Control | Keys.L, nameof(HostCommand.OpenFromLink) },
        { Keys.Control | Keys.E, nameof(HostCommand.OpenInEditor) },
        { Keys.Control | Keys.Oemcomma, nameof(HostCommand.Settings) },
        { Keys.F5, nameof(HostCommand.Reload) },
        { Keys.Alt | Keys.Left, nameof(HostCommand.Back) },
        { Keys.Back, nameof(HostCommand.Back) },
        { Keys.Alt | Keys.Right, nameof(HostCommand.Forward) },
        { Keys.F6, nameof(HostCommand.SwitchPane) },
        { Keys.Shift | Keys.F6, nameof(HostCommand.SwitchPane) },
        { Keys.F9, nameof(HostCommand.NextNote) },
        { Keys.Shift | Keys.F9, nameof(HostCommand.PreviousNote) },
        { Keys.Alt | Keys.Shift | Keys.Down, nameof(HostCommand.NextBlock) },
        { Keys.Alt | Keys.Shift | Keys.Up, nameof(HostCommand.PreviousBlock) },
        { Keys.Control | Keys.Z, nameof(HostCommand.Undo) },
        { Keys.Control | Keys.Y, nameof(HostCommand.Redo) },
        { Keys.Control | Keys.Oemplus, nameof(HostCommand.ZoomIn) },
        { Keys.Control | Keys.Shift | Keys.Oemplus, nameof(HostCommand.ZoomIn) }, // Ctrl+Plus where + is Shift+=.
        { Keys.Control | Keys.Add, nameof(HostCommand.ZoomIn) },
        { Keys.Control | Keys.OemMinus, nameof(HostCommand.ZoomOut) },
        { Keys.Control | Keys.Subtract, nameof(HostCommand.ZoomOut) },
        { Keys.Control | Keys.D0, nameof(HostCommand.ResetZoom) },
        { Keys.F1, nameof(HostCommand.UserManual) },
        { Keys.Shift | Keys.F1, nameof(HostCommand.About) },
    };

    [Theory]
    [MemberData(nameof(PlannedShortcuts))]
    public void TryGetCommand_FindsEveryPlannedShortcut(Keys keys, string expected) {
        // HostCommand is internal, and a public test method cannot take it as a parameter.
        HostCommands.TryGetCommand(keys, out var command).Should().BeTrue();
        command.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData(Keys.Alt | Keys.F4)] // Closes the window: the system handles it, not the host.
    [InlineData(Keys.Enter)] // Enter on a block belongs to the page.
    [InlineData(Keys.Apps)] // So does the Applications key...
    [InlineData(Keys.Shift | Keys.F10)] // ...and its Shift+F10 equivalent.
    [InlineData(Keys.Control | Keys.A)] // JAWS handles Select all in the document itself.
    [InlineData(Keys.F8)] // JAWS takes F8 for extended select (docs/jaws-spike.md, item 8): notes use F9.
    [InlineData(Keys.Shift | Keys.F8)]
    [InlineData(Keys.Left)] // Plain arrows move the virtual cursor.
    [InlineData(Keys.Down)] // So do Down and Up: only Alt+Shift+Down and Alt+Shift+Up move from block to block.
    [InlineData(Keys.Up)]
    [InlineData(Keys.Alt | Keys.Down)] // Screen readers take Alt+Down and Alt+Up (JAWS: open and close a list box).
    [InlineData(Keys.Alt | Keys.Up)]
    [InlineData(Keys.Control | Keys.Back)] // Deletes a word in a text box.
    public void TryGetCommand_LeavesOtherKeysAlone(Keys keys) {
        HostCommands.TryGetCommand(keys, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(Keys.Control | Keys.O, "Ctrl+O")]
    [InlineData(Keys.Shift | Keys.F9, "Shift+F9")]
    [InlineData(Keys.Alt | Keys.Left, "Alt+Left Arrow")]
    [InlineData(Keys.Back, "Backspace")]
    [InlineData(Keys.Control | Keys.Oemcomma, "Ctrl+Comma")]
    [InlineData(Keys.Control | Keys.Oemplus, "Ctrl+Plus")]
    [InlineData(Keys.Control | Keys.Subtract, "Ctrl+Numpad Minus")]
    [InlineData(Keys.Control | Keys.D0, "Ctrl+0")]
    [InlineData(Keys.Alt | Keys.F4, "Alt+F4")]
    [InlineData(Keys.Control | Keys.Shift | Keys.Z, "Ctrl+Shift+Z")]
    [InlineData(Keys.Shift | Keys.Alt | Keys.Down, "Alt+Shift+Down Arrow")] // Ctrl, Alt, Shift, as Windows orders them.
    [InlineData(Keys.Control | Keys.Shift | Keys.Alt | Keys.S, "Ctrl+Alt+Shift+S")]
    public void KeyText_WritesTheKeysAsAMenuShowsThem(Keys keys, string expected) =>
        HostCommands.KeyText(keys).Should().Be(expected);

    [Theory]
    [InlineData(nameof(HostCommand.Open), "Ctrl+O")]
    [InlineData(nameof(HostCommand.Back), "Alt+Left Arrow")] // Backspace is never shown on a menu.
    [InlineData(nameof(HostCommand.ZoomIn), "Ctrl+Plus")] // The numpad key is listed in the dialog only.
    [InlineData(nameof(HostCommand.PreviousNote), "Shift+F9")]
    [InlineData(nameof(HostCommand.NextBlock), "Alt+Shift+Down Arrow")]
    [InlineData(nameof(HostCommand.PreviousBlock), "Alt+Shift+Up Arrow")]
    public void MenuShortcut_ShowsTheCommandsFirstKey(string command, string expected) =>
        HostCommands.MenuShortcut(Enum.Parse<HostCommand>(command)).Should().Be(expected);

    [Fact]
    public void EveryCommand_HasKeysAndAName() {
        foreach (var command in Enum.GetValues<HostCommand>()) {
            HostCommands.KeysOf(command).Should().NotBeEmpty(command.ToString());
            HostCommands.DisplayName(command).Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void Shortcuts_ListEveryCommand() {
        var commands = HostCommands.Shortcuts().Select(entry => entry.Command).ToList();

        commands.Should().BeEquivalentTo(Enum.GetValues<HostCommand>());
        commands.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void KeysOf_LeavesOutTheLayoutVariantOfCtrlPlus() =>
        HostCommands.KeysOf(HostCommand.ZoomIn).Should().Equal(Keys.Control | Keys.Oemplus, Keys.Control | Keys.Add);

    [Fact]
    public void Shortcuts_GiveEveryKeyOfACommand() {
        var back = HostCommands.Shortcuts().Single(entry => entry.Command == HostCommand.Back);

        back.Keys.Should().Equal(Keys.Alt | Keys.Left, Keys.Back);
    }
}
