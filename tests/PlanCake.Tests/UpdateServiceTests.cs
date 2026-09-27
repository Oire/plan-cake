using AwesomeAssertions;
using NetSparkleUpdater.Enums;
using Oire.PlanCake.Services;
using Oire.PlanCake.Utils;
using Oire.PlanCake.Utils.Constants;
using Oire.PlanCake.Utils.Enums;
using Xunit;

namespace Oire.PlanCake.Tests;

/// <summary>
/// The parts of <see cref="UpdateService"/> that decide without the network: the public key, the
/// background check intervals, the outcomes and what the user is told, and the claim that keeps
/// the background checks to one PlanCake window.
/// </summary>
[Collection(LocalizationCollection.Name)]
public class UpdateServiceTests {
    public UpdateServiceTests() {
        // The messages are asserted in English, the language of the source strings.
        Localization.SetLanguage("en-US");
    }

    [Fact]
    public void UpdatePublicKey_IsBase64OfA32ByteEd25519Key() {
        var bytes = new byte[64];

        Convert.TryFromBase64String(App.UpdatePublicKey, bytes, out var written).Should().BeTrue();
        written.Should().Be(32);
    }

    [Theory]
    [InlineData(UpdateCheckInterval.Daily, 1)]
    [InlineData(UpdateCheckInterval.EveryThreeDays, 3)]
    [InlineData(UpdateCheckInterval.Weekly, 7)]
    [InlineData(UpdateCheckInterval.Monthly, 30)]
    public void ToFrequency_GivesThePeriodOfEachInterval(UpdateCheckInterval interval, int days) =>
        UpdateService.ToFrequency(interval).Should().Be(TimeSpan.FromDays(days));

    [Fact]
    public void ToFrequency_OfNever_IsNone() =>
        UpdateService.ToFrequency(UpdateCheckInterval.Never).Should().BeNull();

    [Theory]
    [InlineData(UpdateStatus.UpdateAvailable, UpdateCheckOutcome.UpdateAvailable)]
    [InlineData(UpdateStatus.UpdateNotAvailable, UpdateCheckOutcome.UpToDate)]
    [InlineData(UpdateStatus.UserSkipped, UpdateCheckOutcome.Skipped)]
    [InlineData(UpdateStatus.CouldNotDetermine, UpdateCheckOutcome.Failed)]
    public void ToOutcome_MapsEveryStatus(UpdateStatus status, UpdateCheckOutcome expected) =>
        UpdateService.ToOutcome(status).Should().Be(expected);

    [Fact]
    public void Describe_AnAvailableUpdate_SaysNothing() =>
        UpdateService.Describe(UpdateCheckOutcome.UpdateAvailable).Should().BeNull();

    [Theory]
    [InlineData(UpdateCheckOutcome.UpToDate, "PlanCake is up to date.")]
    [InlineData(UpdateCheckOutcome.Skipped, "The latest version of PlanCake is one you chose to skip.")]
    [InlineData(UpdateCheckOutcome.Failed, "Unable to check for updates. Please try again later.")]
    [InlineData(UpdateCheckOutcome.Unavailable, "Update checks could not be started. The log has the details.")]
    public void Describe_TellsTheUserTheOutcome(UpdateCheckOutcome outcome, string expected) =>
        UpdateService.Describe(outcome).Should().Be(expected);

    [Fact]
    public void TryClaimBackgroundChecks_GoesToOneClaimantAtATime() {
        var name = $@"Local\Oire.PlanCake.Tests.{Guid.NewGuid():N}";

        var first = UpdateService.TryClaimBackgroundChecks(name);
        first.Should().NotBeNull();

        using (var second = UpdateService.TryClaimBackgroundChecks(name)) {
            second.Should().BeNull();
        }

        first!.Dispose();

        using var third = UpdateService.TryClaimBackgroundChecks(name);
        third.Should().NotBeNull();
    }
}
