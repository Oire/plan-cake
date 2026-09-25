using AwesomeAssertions;
using Oire.PlanCake.Ui;
using Xunit;

namespace Oire.PlanCake.Tests;

/// <summary>
/// The host shortcut table is the only way a key pressed inside the WebView2 reaches the host,
/// so a shortcut missing from it simply does nothing in the document.
/// </summary>
public class HostCommandsTests {
    public static TheoryData<Keys, string> PlannedShortcuts => new() {
        { Keys.Control | Keys.O, nameof(HostCommand.Open) },
        { Keys.Control | Keys.V, nameof(HostCommand.OpenFromClipboard) },
        { Keys.Control | Keys.L, nameof(HostCommand.OpenFromLink) },
        { Keys.Control | Keys.E, nameof(HostCommand.OpenInEditor) },
        { Keys.Control | Keys.Oemcomma, nameof(HostCommand.Settings) },
        { Keys.F5, nameof(HostCommand.Reload) },
        { Keys.F6, nameof(HostCommand.SwitchPane) },
        { Keys.F8, nameof(HostCommand.NextNote) },
        { Keys.Shift | Keys.F8, nameof(HostCommand.PreviousNote) },
        { Keys.Control | Keys.Z, nameof(HostCommand.Undo) },
        { Keys.Control | Keys.Y, nameof(HostCommand.Redo) },
        { Keys.Control | Keys.Oemplus, nameof(HostCommand.ZoomIn) },
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
    public void TryGetCommand_LeavesOtherKeysAlone(Keys keys) {
        HostCommands.TryGetCommand(keys, out _).Should().BeFalse();
    }
}
