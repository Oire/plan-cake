using AwesomeAssertions;
using Oire.PlanCake.Ui;
using Xunit;

namespace Oire.PlanCake.Tests;

public class PaneSplitTests {
    private const int ListMinimum = 150;
    private const int DocumentMinimum = 200;

    private static int Larger(int available, int list) =>
        PaneSplit.Resize(available, list, ListMinimum, DocumentMinimum, larger: true);

    private static int Smaller(int available, int list) =>
        PaneSplit.Resize(available, list, ListMinimum, DocumentMinimum, larger: false);

    [Theory]
    [InlineData(300, 400)]
    [InlineData(400, 500)]
    [InlineData(320, 400)]
    [InlineData(399, 500)]
    public void Larger_MovesToTheNextWholeStep(int list, int expected) =>
        Larger(1000, list).Should().Be(expected);

    [Theory]
    [InlineData(400, 300)]
    [InlineData(320, 300)]
    [InlineData(401, 300)]
    public void Smaller_MovesToThePreviousWholeStep(int list, int expected) =>
        Smaller(1000, list).Should().Be(expected);

    [Fact]
    public void Steps_AreATenthOfTheRoom() {
        var list = 310;

        list = Larger(993, list);
        PaneSplit.Percent(993, list).Should().Be(40);

        list = Larger(993, list);
        PaneSplit.Percent(993, list).Should().Be(50);

        list = Smaller(993, list);
        PaneSplit.Percent(993, list).Should().Be(40);
    }

    [Fact]
    public void Larger_StopsWhereTheDocumentWouldGetTooSmall() {
        Larger(1000, 750).Should().Be(800);
        Larger(1000, 800).Should().Be(800);
        PaneSplit.Percent(1000, 800).Should().Be(80);
    }

    [Fact]
    public void Smaller_StopsAtTheListMinimum() {
        Smaller(1000, 200).Should().Be(ListMinimum);
        Smaller(1000, ListMinimum).Should().Be(ListMinimum);
        PaneSplit.Percent(1000, ListMinimum).Should().Be(15);
    }

    [Fact]
    public void NoRoomForBothMinimums_LeavesTheSizeAlone() {
        Larger(300, 150).Should().Be(150);
        Smaller(300, 150).Should().Be(150);
        Larger(0, 0).Should().Be(0);
    }

    [Fact]
    public void Percent_OfNoRoom_IsZero() => PaneSplit.Percent(0, 100).Should().Be(0);
}
