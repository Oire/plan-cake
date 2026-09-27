using Oire.WinForms.NativeControls;

namespace Oire.PlanCake.Ui;

/// <summary>
/// Adds an item to a <see cref="NativeMenuSpec"/> and returns that item, whose enabled or checked
/// state the owner changes later (<see cref="NativeMenuSpec"/>'s own methods return the spec, for
/// chaining).
/// </summary>
/// <remarks>
/// A shortcut is shown, never registered, because <see cref="HostCommands"/> runs the keys, from
/// the document too.
/// </remarks>
internal static class MenuSpecExtensions {
    /// <summary>Adds a command item.</summary>
    public static NativeMenuItemSpec AddItem(this NativeMenuSpec spec, string text, string? shortcut, Action onClick) {
        ArgumentNullException.ThrowIfNull(spec);
        spec.Add(text, shortcut, shortcutKeys: null, onClick);

        return spec.Items[^1];
    }

    /// <summary>Adds an on/off item.</summary>
    public static NativeMenuItemSpec AddCheckableItem(
        this NativeMenuSpec spec,
        string text,
        bool isChecked,
        string? shortcut,
        Action onClick
    ) {
        ArgumentNullException.ThrowIfNull(spec);
        spec.AddCheckable(text, isChecked, shortcut, shortcutKeys: null, onClick);

        return spec.Items[^1];
    }

    /// <summary>Adds one choice of a group of which exactly one is checked.</summary>
    public static NativeMenuItemSpec AddRadioItem(
        this NativeMenuSpec spec,
        string text,
        string group,
        bool isChecked,
        Action onClick
    ) {
        ArgumentNullException.ThrowIfNull(spec);
        spec.AddRadio(text, group, isChecked, onClick);

        return spec.Items[^1];
    }

    /// <summary>Adds a submenu, filled by <paramref name="build"/>.</summary>
    public static NativeMenuItemSpec AddSubmenu(this NativeMenuSpec spec, string text, Action<NativeMenuSpec> build) {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(build);
        spec.AddMenu(text, build);

        return spec.Items[^1];
    }
}
