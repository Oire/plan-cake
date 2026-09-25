using AwesomeAssertions;
using Oire.PlanCake.Ui;
using Oire.WinForms.NativeControls;
using Xunit;

namespace Oire.PlanCake.Tests;

/// <summary>
/// Menu items of later tasks are simply not added, so a separator may end up with nothing to
/// separate; <see cref="MenuBuilder"/> leaves such separators out.
/// </summary>
public class MenuBuilderTests {
    private static readonly Action _nothing = () => { };

    /// <summary>The level as text: item texts, separators as <c>-</c>.</summary>
    private static string[] Layout(NativeMenuSpec spec) =>
        spec.Items.Select(item => item.IsSeparator ? "-" : item.Text).ToArray();

    [Fact]
    public void Separator_BetweenItems_IsKept() {
        var menu = new MenuBuilder(new NativeMenuSpec());

        menu.Add("One", null, _nothing);
        menu.AddSeparator();
        menu.Add("Two", null, _nothing);

        Layout(menu.Spec).Should().Equal("One", "-", "Two");
    }

    [Fact]
    public void Separators_LeadingDoubledAndTrailing_AreDropped() {
        var menu = new MenuBuilder(new NativeMenuSpec());

        menu.AddSeparator();
        menu.Add("Open", null, _nothing);
        menu.AddSeparator();
        menu.AddSeparator();
        menu.Add("Exit", null, _nothing);
        menu.AddSeparator();

        Layout(menu.Spec).Should().Equal("Open", "-", "Exit");
    }

    [Fact]
    public void Submenu_FollowsTheSameRules() {
        var menu = new MenuBuilder(new NativeMenuSpec());

        var submenu = menu.AddMenu("&View", view => {
            view.AddSeparator();
            view.Add("Zoom in", "Ctrl+Plus", _nothing);
            view.AddSeparator();
        });

        submenu.Children.Should().ContainSingle().Which.Text.Should().Be("Zoom in");
    }

    [Fact]
    public void Items_AreReturnedForTheirStateAndShowTheirShortcutWithoutRegisteringIt() {
        var menu = new MenuBuilder(new NativeMenuSpec());

        var item = menu.Add("&Open...", "Ctrl+O", _nothing);
        var toggle = menu.AddCheckable("&Notes list", true, null, _nothing);
        var radio = menu.AddRadio("English", "language", false, _nothing);

        item.Should().BeSameAs(menu.Spec.Items[0]);
        item.Shortcut.Should().Be("Ctrl+O");
        item.ShortcutKeys.Should().BeNull();
        toggle.IsCheckable.Should().BeTrue();
        toggle.IsChecked.Should().BeTrue();
        radio.RadioGroup.Should().Be("language");
    }
}
