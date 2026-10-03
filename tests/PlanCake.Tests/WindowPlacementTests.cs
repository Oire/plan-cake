using AwesomeAssertions;
using Oire.PlanCake.Ui;
using Xunit;

namespace Oire.PlanCake.Tests;

/// <summary>
/// The main window opens where the last one closed, but always whole on a screen that is there,
/// and a first window fits the screen it opens on.
/// </summary>
public class WindowPlacementTests {
    private static readonly Size _minimum = new(500, 360);

    // A 1920 by 1080 screen with a taskbar at the bottom, and a second one to its right.
    private static readonly Rectangle _primary = new(0, 0, 1920, 1032);
    private static readonly Rectangle _secondary = new(1920, 0, 1280, 984);
    private static readonly Rectangle[] _screens = [_primary, _secondary];

    [Fact]
    public void FirstRunSize_TallerThanTheWorkArea_IsCutTo90Percent() {
        // 1000 by 700 at 150%.
        var size = WindowPlacement.FirstRunSize(new Size(1500, 1050), _primary);

        size.Should().Be(new Size(1500, 928));
    }

    [Fact]
    public void FirstRunSize_ThatFits_IsKept() =>
        WindowPlacement.FirstRunSize(new Size(1000, 700), _primary).Should().Be(new Size(1000, 700));

    [Fact]
    public void Restore_NothingSaved_IsNull() =>
        WindowPlacement.Restore(Rectangle.Empty, _screens, _minimum).Should().BeNull();

    [Fact]
    public void Restore_NoScreens_IsNull() =>
        WindowPlacement.Restore(new Rectangle(10, 10, 800, 600), [], _minimum).Should().BeNull();

    [Fact]
    public void Restore_OnAScreen_IsKeptAsItWas() {
        var saved = new Rectangle(100, 80, 1200, 800);

        WindowPlacement.Restore(saved, _screens, _minimum).Should().Be(saved);
    }

    [Fact]
    public void Restore_OnTheSecondScreen_StaysThere() {
        var saved = new Rectangle(2000, 50, 1000, 700);

        WindowPlacement.Restore(saved, _screens, _minimum).Should().Be(saved);
    }

    [Fact]
    public void Restore_PartlyOffTheScreen_IsMovedOntoIt() {
        // Its bottom was behind the taskbar.
        var saved = new Rectangle(300, 600, 1000, 700);

        WindowPlacement.Restore(saved, _screens, _minimum).Should().Be(new Rectangle(300, 332, 1000, 700));
    }

    [Fact]
    public void Restore_LargerThanTheScreen_IsShrunkToIt() {
        var saved = new Rectangle(-50, -20, 2500, 1400);

        WindowPlacement.Restore(saved, _screens, _minimum).Should().Be(_primary);
    }

    [Fact]
    public void Restore_AcrossTwoScreens_GoesWholeOntoTheOneItOverlapsMost() {
        // Most of it is on the second screen.
        var saved = new Rectangle(1700, 100, 1000, 700);

        WindowPlacement.Restore(saved, _screens, _minimum).Should().Be(new Rectangle(1920, 100, 1000, 700));
    }

    [Fact]
    public void Restore_OnAScreenThatIsGone_IsCenteredOnThePrimaryScreen() {
        var saved = new Rectangle(3500, 200, 1000, 700);

        WindowPlacement.Restore(saved, _screens, _minimum).Should().Be(new Rectangle(460, 166, 1000, 700));
    }

    [Fact]
    public void Restore_SmallerThanTheMinimum_GrowsToIt() {
        var saved = new Rectangle(100, 100, 200, 100);

        WindowPlacement.Restore(saved, _screens, _minimum).Should().Be(new Rectangle(100, 100, 500, 360));
    }

    [Fact]
    public void SplitterDistance_LeavesTheListItsSavedWidth() =>
        WindowPlacement.SplitterDistance(available: 1000, listWidth: 300, listMinimum: 150, documentMinimum: 200)
            .Should().Be(700);

    [Theory]
    [InlineData(100, 850)] // Narrower than the list's minimum.
    [InlineData(900, 200)] // Wider than the document's minimum leaves.
    public void SplitterDistance_KeepsBothPanesAtTheirMinimum(int listWidth, int distance) =>
        WindowPlacement.SplitterDistance(available: 1000, listWidth, listMinimum: 150, documentMinimum: 200)
            .Should().Be(distance);

    [Theory]
    [InlineData(1000, 0)] // Nothing saved.
    [InlineData(300, 300)] // No room for both minimums.
    public void SplitterDistance_WithoutAWidthOrRoom_IsNull(int available, int listWidth) =>
        WindowPlacement.SplitterDistance(available, listWidth, listMinimum: 150, documentMinimum: 200)
            .Should().BeNull();
}
