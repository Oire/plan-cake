using AwesomeAssertions;
using Oire.PlanCake.Ui;
using Xunit;

namespace Oire.PlanCake.Tests;

public class WrappingLabelTests {
    private const string Text = "Закривальний маркер (залиште порожнім, щоб використовувати один маркер, який діє до кінця рядка):";

    [Fact]
    public void PreferredSize_WithoutAWidth_IsOneLine() =>
        Sta.Run(() => {
            using var label = new WrappingLabel { Text = Text };

            label.GetPreferredSize(Size.Empty).Height.Should().Be(label.GetPreferredSize(new Size(Int32.MaxValue, 0)).Height);
            label.GetPreferredSize(Size.Empty).Height.Should().BeLessThan(2 * label.Font.Height);
        });

    [Fact]
    public void PreferredSize_InANarrowColumn_WrapsWithinIt() =>
        Sta.Run(() => {
            using var label = new WrappingLabel { Text = Text, Padding = new Padding(0, 0, 0, 8) };
            var size = label.GetPreferredSize(new Size(200, 0));

            size.Width.Should().BeLessThanOrEqualTo(200);
            size.Height.Should().BeGreaterThanOrEqualTo((3 * label.Font.Height) + 8, "the text takes several lines, plus the padding");
        });

    [Fact]
    public void PreferredSize_WhileEmpty_KeepsOneLine() =>
        Sta.Run(() => {
            using var label = new WrappingLabel();

            label.GetPreferredSize(new Size(200, 0)).Height.Should().Be(label.Font.Height);
        });
}
