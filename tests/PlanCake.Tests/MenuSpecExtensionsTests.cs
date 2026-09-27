using AwesomeAssertions;
using Oire.PlanCake.Ui;
using Oire.WinForms.NativeControls;
using Xunit;

namespace Oire.PlanCake.Tests;

/// <summary>The menu items are returned for their state, and show their shortcut without registering it.</summary>
public class MenuSpecExtensionsTests {
    private static readonly Action _nothing = () => { };

    [Fact]
    public void Items_AreReturnedForTheirStateAndShowTheirShortcutWithoutRegisteringIt() {
        var spec = new NativeMenuSpec();

        var item = spec.AddItem("&Open...", "Ctrl+O", _nothing);
        var toggle = spec.AddCheckableItem("&Notes list", true, null, _nothing);
        var radio = spec.AddRadioItem("English", "language", false, _nothing);
        var submenu = spec.AddSubmenu("&View", view => view.AddItem("Zoom in", "Ctrl+Plus", _nothing));

        item.Should().BeSameAs(spec.Items[0]);
        item.Shortcut.Should().Be("Ctrl+O");
        item.ShortcutKeys.Should().BeNull();
        toggle.IsCheckable.Should().BeTrue();
        toggle.IsChecked.Should().BeTrue();
        radio.RadioGroup.Should().Be("language");
        submenu.Should().BeSameAs(spec.Items[3]);
        submenu.Children.Should().ContainSingle().Which.Text.Should().Be("Zoom in");
    }
}
