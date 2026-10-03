using AwesomeAssertions;
using Oire.PlanCake.Ui;
using Oire.PlanCake.Utils;
using Oire.PlanCake.Utils.Enums;
using Xunit;

namespace Oire.PlanCake.Tests;

/// <summary>
/// The reason the Settings dialog shows next to markers that cannot be used, and the choices it
/// offers.
/// </summary>
[Collection(LocalizationCollection.Name)]
public class SettingsDialogTests {
    public SettingsDialogTests() {
        // The reasons are asserted in English, the language of the source strings.
        Localization.SetLanguage("en-US");
    }

    [Theory]
    [InlineData("[usernote]", "[/usernote]")]
    [InlineData("!USERNOTE!", "")]
    [InlineData("#note", "/note#")]
    [InlineData("a\"b", "c\"d")]
    public void DescribeMarkersError_AcceptsUsableMarkers(string opening, string closing) =>
        SettingsDialog.DescribeMarkersError(opening, closing).Should().BeNull();

    [Theory]
    [InlineData("", "[/usernote]", "The opening marker cannot be empty.")]
    [InlineData(" [note]", "[/note]", "A marker cannot start or end with a space.")]
    [InlineData("[note]", "[/note] ", "A marker cannot start or end with a space.")]
    [InlineData("[no\nte]", "[/note]", "A marker cannot contain a line break.")]
    [InlineData("[note]", "[note]", "The closing marker must differ from the opening marker.")]
    [InlineData("\"note", "note\"", "A marker cannot start or end with a double quote.")]
    public void DescribeMarkersError_GivesTheReason(string opening, string closing, string expected) =>
        SettingsDialog.DescribeMarkersError(opening, closing).Should().Be(expected);

    [Fact]
    public void MarkersErrorText_SaysItIsAnErrorInWords() {
        SettingsDialog.MarkersErrorText(null).Should().BeEmpty();
        SettingsDialog.MarkersErrorText("A marker cannot contain a line break.")
            .Should().Be("Error: A marker cannot contain a line break.");
    }

    [Fact]
    public void ErrorColor_StandsOutAgainstTheDialogAndItsTabs() {
        ContrastRatio(SettingsDialog.ErrorColor, SystemColors.Control).Should().BeGreaterThanOrEqualTo(4.5);
        ContrastRatio(SettingsDialog.ErrorColor, Color.White).Should().BeGreaterThanOrEqualTo(4.5);
    }

    [Fact]
    public void NoteEnterChoices_AreShortAndTheLabelNamesCtrlEnter() =>
        Sta.Run(() => {
            using var dialog = new SettingsDialog();
            var comboBox = (ComboBox)Find(dialog, "noteEnterComboBox");

            comboBox.Items.Cast<object>().Select(item => item.ToString()).Should().Equal("Saves the note", "Starts a new line");
            Find(dialog, "noteEnterLabel").Text.Should().Be("Enter in the note dialo&g (Ctrl+Enter does the other):");
        });

    [Theory]
    [MemberData(nameof(Languages))]
    public void ChoicesAndCheckBoxes_FitTheirTabInEveryLanguage(string language) {
        Localization.SetLanguage(language);

        Sta.Run(() => {
            using var dialog = new SettingsDialog();
            dialog.PerformLayout();

            // A tab's content: the tab control less its border and the page's padding. Tab pages
            // get their size only with a handle, which a test does not create.
            var content = Find(dialog, "tabControl").Width - TabBorder - (2 * 8);

            foreach (var checkBox in AllControls(dialog).OfType<CheckBox>()) {
                (checkBox.PreferredSize.Width + checkBox.Margin.Horizontal).Should()
                    .BeLessThanOrEqualTo(content, "\"{0}\" must show whole in {1}", checkBox.Text, language);
            }

            // General: the labels' column takes what its widest label needs, the boxes the rest.
            var general = (TableLayoutPanel)Find(dialog, "generalLayout");
            var labels = general.Controls.Cast<Control>()
                .Where(control => control is Label && general.GetColumn(control) == 0)
                .Max(label => label.PreferredSize.Width + label.Margin.Horizontal);
            AssertChoicesFit(general, content - labels, language);

            // Notes: two columns of half the width each.
            AssertChoicesFit((TableLayoutPanel)Find(dialog, "notesLayout"), content / 2, language);
        });
    }

    public static TheoryData<string> Languages() => new(LanguageList.SupportedCodes);

    /// <summary>What the tab control takes of its width for its own border, at most.</summary>
    private const int TabBorder = 8;

    private static void AssertChoicesFit(TableLayoutPanel layout, int column, string language) {
        foreach (var comboBox in layout.Controls.OfType<ComboBox>()) {
            // The closed box shows the item beside its arrow, inside its border.
            var room = column - comboBox.Margin.Horizontal - SystemInformation.VerticalScrollBarWidth - 8;

            foreach (var item in comboBox.Items.Cast<object>().Select(item => item.ToString()!)) {
                TextRenderer.MeasureText(item, comboBox.Font).Width.Should()
                    .BeLessThanOrEqualTo(room, "\"{0}\" in {1} must show whole while the box is closed", item, language);
            }
        }
    }

    private static Control Find(Form form, string name) => form.Controls.Find(name, searchAllChildren: true).Single();

    private static IEnumerable<Control> AllControls(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(child => AllControls(child).Prepend(child));

    /// <summary>The WCAG contrast ratio of two opaque colors.</summary>
    private static double ContrastRatio(Color first, Color second) {
        var one = Luminance(first);
        var other = Luminance(second);

        return (Math.Max(one, other) + 0.05) / (Math.Min(one, other) + 0.05);
    }

    private static double Luminance(Color color) =>
        (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));

    private static double Linear(byte channel) {
        var value = channel / 255.0;

        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    [Fact]
    public void UpdateIntervalOptions_OfferEveryIntervalOnceMostFrequentFirst() {
        var options = SettingsDialog.UpdateIntervalOptions();

        options.Select(option => option.Interval).Should().Equal(Enum.GetValues<UpdateCheckInterval>());
        options.Select(option => option.Text).Should()
            .Equal("Once a day", "Every 3 days", "Once a week", "Once a month", "Never");
    }
}
