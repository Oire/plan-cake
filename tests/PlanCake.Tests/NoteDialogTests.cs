using AwesomeAssertions;
using Oire.PlanCake.Ui;
using Oire.PlanCake.Utils.Enums;
using Xunit;

namespace Oire.PlanCake.Tests;

public class NoteDialogTests {
    // The enums are internal and a test method is public, so the cases pass their names.
    [Theory]
    [InlineData(Keys.Enter, nameof(NoteEnterAction.Save), nameof(NoteKeyAction.Save))]
    [InlineData(Keys.Control | Keys.Enter, nameof(NoteEnterAction.Save), nameof(NoteKeyAction.NewLine))]
    [InlineData(Keys.Enter, nameof(NoteEnterAction.NewLine), nameof(NoteKeyAction.NewLine))]
    [InlineData(Keys.Control | Keys.Enter, nameof(NoteEnterAction.NewLine), nameof(NoteKeyAction.Save))]
    [InlineData(Keys.Shift | Keys.Enter, nameof(NoteEnterAction.Save), nameof(NoteKeyAction.None))]
    [InlineData(Keys.A, nameof(NoteEnterAction.Save), nameof(NoteKeyAction.None))]
    public void KeyAction_EnterAndCtrlEnterFollowTheSetting(Keys keys, string setting, string expected) =>
        NoteDialog.KeyAction(keys, Enum.Parse<NoteEnterAction>(setting))
            .Should().Be(Enum.Parse<NoteKeyAction>(expected));
}
