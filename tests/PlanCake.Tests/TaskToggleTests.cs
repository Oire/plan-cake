using AwesomeAssertions;
using Oire.PlanCake.Notes;
using Xunit;

namespace Oire.PlanCake.Tests;

public class TaskToggleTests {
    [Fact]
    public void SetChecked_UncheckedItem_BecomesChecked() =>
        TaskToggle.SetChecked("- [ ] todo\n- [ ] other\n", 1, isChecked: true)
            .Should().Be("- [x] todo\n- [ ] other\n");

    [Fact]
    public void SetChecked_CheckedItem_BecomesUnchecked() =>
        TaskToggle.SetChecked("- [x] done\n", 1, isChecked: false).Should().Be("- [ ] done\n");

    [Fact]
    public void SetChecked_CapitalX_BecomesUnchecked() =>
        TaskToggle.SetChecked("- [X] done\n", 1, isChecked: false).Should().Be("- [ ] done\n");

    [Fact]
    public void SetChecked_AlreadyInThatState_LeavesTheLineAlone() {
        TaskToggle.SetChecked("- [X] done\n", 1, isChecked: true).Should().Be("- [X] done\n");
        TaskToggle.SetChecked("- [ ] todo\n", 1, isChecked: false).Should().Be("- [ ] todo\n");
    }

    [Fact]
    public void SetChecked_NestedItem_ChangesOnlyThatLine() =>
        TaskToggle.SetChecked("- [ ] parent\n  - [ ] child\n  - [ ] sibling\n", 2, isChecked: true)
            .Should().Be("- [ ] parent\n  - [x] child\n  - [ ] sibling\n");

    [Fact]
    public void SetChecked_ItemFollowedByANote_LeavesTheNoteUntouched() {
        const string source = "- [ ] todo [x]\n  [usernote]- [ ] not a task[/usernote]\n- [ ] next\n";

        TaskToggle.SetChecked(source, 1, isChecked: true)
            .Should().Be("- [x] todo [x]\n  [usernote]- [ ] not a task[/usernote]\n- [ ] next\n");
    }

    [Theory]
    [InlineData("* [ ] star\n", "* [x] star\n")]
    [InlineData("+ [ ] plus\n", "+ [x] plus\n")]
    [InlineData("1. [ ] first\n", "1. [x] first\n")]
    [InlineData("12) [ ] paren\n", "12) [x] paren\n")]
    [InlineData("> - [ ] quoted\n", "> - [x] quoted\n")]
    [InlineData("-\t[ ] tab\n", "-\t[x] tab\n")]
    public void SetChecked_EveryListMarker(string source, string expected) =>
        TaskToggle.SetChecked(source, 1, isChecked: true).Should().Be(expected);

    [Fact]
    public void SetChecked_CrLf_KeepsEveryLineEnding() =>
        TaskToggle.SetChecked("# T\r\n\r\n- [ ] a\r\n- [ ] b\r\n", 4, isChecked: true)
            .Should().Be("# T\r\n\r\n- [ ] a\r\n- [x] b\r\n");

    [Fact]
    public void SetChecked_LastLineWithoutLineBreak() =>
        TaskToggle.SetChecked("- [x] a", 1, isChecked: false).Should().Be("- [ ] a");

    [Theory]
    [InlineData("- plain item\n")]
    [InlineData("Paragraph with [x] inside.\n")]
    [InlineData("-[ ] no space after the marker\n")]
    [InlineData("- [y] other letter\n")]
    [InlineData("- [\n")]
    [InlineData("\n")]
    public void SetChecked_LineWithoutATaskMarker_Throws(string source) {
        var toggle = () => TaskToggle.SetChecked(source, 1, isChecked: true);

        toggle.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("line");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void SetChecked_LineOutOfRange_Throws(int line) {
        var toggle = () => TaskToggle.SetChecked("- [ ] a\n- [ ] b", line, isChecked: true);

        toggle.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("- [ ] todo\n", false)]
    [InlineData("- [x] done\n", true)]
    [InlineData("- [X] done\n", true)]
    public void IsChecked_ReadsTheMarker(string source, bool expected) =>
        TaskToggle.IsChecked(source, 1).Should().Be(expected);

    [Theory]
    [InlineData("[x] done", "done")]
    [InlineData("[X] done", "done")]
    [InlineData("[ ] todo", "todo")]
    [InlineData("plain item", "plain item")]
    [InlineData("[y] other", "[y] other")]
    [InlineData("[x]", "")]
    public void WithoutMarker_DropsTheLeadingMarker(string text, string expected) =>
        TaskToggle.WithoutMarker(text).Should().Be(expected);

    [Fact]
    public void IsChecked_LineWithoutATaskMarker_IsNull() =>
        TaskToggle.IsChecked("- plain\n", 1).Should().BeNull();
}
