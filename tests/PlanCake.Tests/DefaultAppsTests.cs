using AwesomeAssertions;
using Oire.PlanCake.Utils;
using Xunit;

namespace Oire.PlanCake.Tests;

public class DefaultAppsTests {
    [Fact]
    public void SettingsUri_OpensPlanCakesDefaultAppsPage() {
        DefaultApps.SettingsUri.Should().Be("ms-settings:defaultapps?registeredAppMachine=PlanCake");
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    public void ShowsSettingsButton_OnlyWhenInstalledAndRegistered(bool isPortable, bool isRegistered, bool expected) {
        DefaultApps.ShowsSettingsButton(isPortable, isRegistered).Should().Be(expected);
    }

    [Fact]
    public void IsRegistered_DoesNotThrow() {
        var act = () => DefaultApps.IsRegistered();

        act.Should().NotThrow();
    }
}
