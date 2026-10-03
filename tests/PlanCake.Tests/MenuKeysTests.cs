using AwesomeAssertions;
using Oire.PlanCake.Ui;
using Xunit;

namespace Oire.PlanCake.Tests;

/// <summary>
/// The keys that enter the menu bar from the document, where Windows never sees them: Alt+letter,
/// Alt pressed and released alone, and F10. Everything else, the host commands with Alt among
/// them, stays out of the menu's way.
/// </summary>
public class MenuKeysTests {
    [Theory]
    [InlineData(Keys.Alt | Keys.F)]
    [InlineData(Keys.Alt | Keys.H)]
    [InlineData(Keys.Alt | Keys.D1)]
    [InlineData(Keys.Alt | Keys.OemOpenBrackets)] // Russian х, Ukrainian х.
    [InlineData(Keys.Alt | Keys.OemQuotes)] // Russian э, Ukrainian є.
    public void AltAndALetter_OpensAMenu(Keys keys) =>
        new MenuKeys().KeyDown(keys).Should().Be(MenuKeyAction.OpenMenu);

    [Theory]
    [InlineData(Keys.Control | Keys.Alt | Keys.F)] // AltGr typing a character.
    [InlineData(Keys.Alt | Keys.Shift | Keys.F)]
    [InlineData(Keys.Control | Keys.F)]
    [InlineData(Keys.F)]
    [InlineData(Keys.Alt | Keys.Shift | Keys.Down)] // Next block.
    [InlineData(Keys.Alt | Keys.Left)] // Back.
    [InlineData(Keys.Alt | Keys.Down)] // The screen reader's.
    [InlineData(Keys.Alt | Keys.F4)]
    [InlineData(Keys.Shift | Keys.F10)] // The context menu.
    [InlineData(Keys.Control | Keys.F10)]
    public void OtherKeys_LeaveTheMenuAlone(Keys keys) =>
        new MenuKeys().KeyDown(keys).Should().Be(MenuKeyAction.None);

    [Fact]
    public void F10_EntersTheMenuBar() =>
        new MenuKeys().KeyDown(Keys.F10).Should().Be(MenuKeyAction.EnterMenuBar);

    [Theory]
    [InlineData(Keys.Menu)]
    [InlineData(Keys.LMenu)]
    [InlineData(Keys.RMenu)]
    public void AltPressedAndReleasedAlone_EntersTheMenuBar(Keys alt) {
        var keys = new MenuKeys();

        keys.KeyDown(alt | Keys.Alt).Should().Be(MenuKeyAction.None);
        keys.KeyUp(alt).Should().Be(MenuKeyAction.EnterMenuBar);
    }

    [Fact]
    public void AltHeldDown_RepeatsWithoutEnteringTheMenuBarUntilReleased() {
        var keys = new MenuKeys();

        keys.KeyDown(Keys.Menu | Keys.Alt).Should().Be(MenuKeyAction.None);
        keys.KeyDown(Keys.Menu | Keys.Alt).Should().Be(MenuKeyAction.None);
        keys.KeyUp(Keys.Menu).Should().Be(MenuKeyAction.EnterMenuBar);
    }

    [Fact]
    public void AltReleasedAfterAnotherKey_LeavesTheMenuBarAlone() {
        var keys = new MenuKeys();

        keys.KeyDown(Keys.Menu | Keys.Alt);
        keys.KeyDown(Keys.Down | Keys.Shift | Keys.Alt);
        keys.KeyUp(Keys.Down | Keys.Shift | Keys.Alt).Should().Be(MenuKeyAction.None);
        keys.KeyUp(Keys.Menu).Should().Be(MenuKeyAction.None);
    }

    [Fact]
    public void AltAfterAMenuLetter_LeavesTheMenuBarAlone() {
        var keys = new MenuKeys();

        keys.KeyDown(Keys.Menu | Keys.Alt);
        keys.KeyDown(Keys.F | Keys.Alt).Should().Be(MenuKeyAction.OpenMenu);
        keys.KeyUp(Keys.Menu).Should().Be(MenuKeyAction.None);
    }

    [Theory]
    [InlineData(Keys.Control)] // AltGr is Ctrl+Alt.
    [InlineData(Keys.Shift)] // Shift+Alt switches the layout.
    public void AltWithAnotherModifier_LeavesTheMenuBarAlone(Keys modifier) {
        var keys = new MenuKeys();

        keys.KeyDown(Keys.Menu | Keys.Alt | modifier);
        keys.KeyUp(Keys.Menu | modifier).Should().Be(MenuKeyAction.None);
    }

    [Fact]
    public void AltReleasedWithoutBeingPressedHere_LeavesTheMenuBarAlone() =>
        new MenuKeys().KeyUp(Keys.Menu).Should().Be(MenuKeyAction.None);

    [Fact]
    public void AltPressedBeforeTheWindowLostTheActivation_LeavesTheMenuBarAlone() {
        var keys = new MenuKeys();

        keys.KeyDown(Keys.Menu | Keys.Alt);
        keys.Reset();
        keys.KeyUp(Keys.Menu).Should().Be(MenuKeyAction.None);
    }

    [Fact]
    public void AltReleasedTwice_EntersTheMenuBarOnce() {
        var keys = new MenuKeys();

        keys.KeyDown(Keys.Menu | Keys.Alt);
        keys.KeyUp(Keys.Menu).Should().Be(MenuKeyAction.EnterMenuBar);
        keys.KeyUp(Keys.Menu).Should().Be(MenuKeyAction.None);
    }

    [Fact]
    public void NoHostCommandWithAltAlone_IsAMenuLetter() {
        // A host command on Alt+letter would never reach the menu bar: MainWindow runs commands first.
        foreach (var (_, keys) in HostCommands.Shortcuts()) {
            foreach (var key in keys) {
                var isAltLetter = (key & Keys.Modifiers) == Keys.Alt && MenuKeys.IsCharacterKey(key & Keys.KeyCode);
                isAltLetter.Should().BeFalse("{0} would hide a menu's mnemonic", key);
            }
        }
    }
}
