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
    public void UpdateIntervalOptions_OfferEveryIntervalOnceMostFrequentFirst() {
        var options = SettingsDialog.UpdateIntervalOptions();

        options.Select(option => option.Interval).Should().Equal(Enum.GetValues<UpdateCheckInterval>());
        options.Select(option => option.Text).Should()
            .Equal("Once a day", "Every 3 days", "Once a week", "Once a month", "Never");
    }
}
