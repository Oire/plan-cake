using AwesomeAssertions;
using Oire.PlanCake.Utils;
using Xunit;

namespace Oire.PlanCake.Tests;

[Collection(LocalizationCollection.Name)]
public class DialogHelperTests: IDisposable {
    public DialogHelperTests() => Localization.SetLanguage("en-US");

    public void Dispose() {
        Localization.SetLanguage("en-US");
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Confirmation_OffersYesAndNo_WithYesAsTheDefault() {
        var page = DialogHelper.ConfirmationPage("Delete this note?", "Delete note", MessageBoxIcon.Question);

        page.Buttons.Should().Equal(TaskDialogButton.Yes, TaskDialogButton.No);
        page.DefaultButton.Should().Be(TaskDialogButton.Yes);
        page.Text.Should().Be("Delete this note?");
        page.Caption.Should().Be("Delete note");
        page.Icon.Should().NotBeNull();
    }

    [Fact]
    public void Confirmation_ClosesWithEscape() =>
        DialogHelper.ConfirmationPage("Question?", "Title", MessageBoxIcon.Question).AllowCancel.Should().BeTrue();

    [Fact]
    public void Answer_IsYesOnlyForTheYesButton() {
        DialogHelper.IsYes(TaskDialogButton.Yes).Should().BeTrue();
        DialogHelper.IsYes(TaskDialogButton.No).Should().BeFalse();

        // Escape and the close button end the dialog with Cancel.
        DialogHelper.IsYes(TaskDialogButton.Cancel).Should().BeFalse();
        DialogHelper.IsYes(null).Should().BeFalse();
    }

    [Fact]
    public void Confirmation_InALeftToRightLanguage_IsNotMirrored() =>
        DialogHelper.ConfirmationPage("Question?", "Title", MessageBoxIcon.Question).RightToLeftLayout.Should().BeFalse();

    [SkippableFact]
    public void Confirmation_InHebrew_IsMirrored() {
        // Needs the Hebrew catalog, which Task 15 adds: without it the language falls back to English.
        Localization.SetLanguage("he-IL");
        Skip.IfNot(TextDirection.IsRightToLeft, "No Hebrew catalog yet (Task 15).");

        DialogHelper.ConfirmationPage("Question?", "Title", MessageBoxIcon.Question).RightToLeftLayout.Should().BeTrue();
    }
}
