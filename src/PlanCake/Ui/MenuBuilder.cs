using Oire.WinForms.NativeControls;

namespace Oire.PlanCake.Ui;

/// <summary>
/// Fills one level of a <see cref="NativeMenuSpec"/>, keeping separators where they separate
/// something: a separator asked for before the first item, after another separator or after the
/// last item is left out. An item whose feature comes with a later task is simply not added, and
/// the menu still reads cleanly.
/// </summary>
/// <remarks>
/// Every item is display-only as far as keys go: a shortcut is shown, never registered, because
/// <see cref="HostCommands"/> runs the keys, from the document too.
/// </remarks>
internal sealed class MenuBuilder(NativeMenuSpec spec) {
    private bool _separatorPending;

    /// <summary>The level being filled.</summary>
    public NativeMenuSpec Spec { get; } = spec ?? throw new ArgumentNullException(nameof(spec));

    /// <summary>Adds a command item.</summary>
    /// <returns>The item, whose enabled state the owner may change later.</returns>
    public NativeMenuItemSpec Add(string text, string? shortcut, Action onClick) =>
        Emit(() => Spec.Add(text, shortcut, shortcutKeys: null, onClick));

    /// <summary>Adds an on/off item.</summary>
    /// <returns>The item, whose check and enabled state the owner may change later.</returns>
    public NativeMenuItemSpec AddCheckable(string text, bool isChecked, string? shortcut, Action onClick) =>
        Emit(() => Spec.AddCheckable(text, isChecked, shortcut, shortcutKeys: null, onClick));

    /// <summary>Adds one choice of a group of which exactly one is checked.</summary>
    /// <returns>The item, whose check and enabled state the owner may change later.</returns>
    public NativeMenuItemSpec AddRadio(string text, string group, bool isChecked, Action onClick) =>
        Emit(() => Spec.AddRadio(text, group, isChecked, onClick));

    /// <summary>Adds a submenu, filled by <paramref name="build"/> with the same separator rules.</summary>
    /// <returns>The submenu item, whose enabled state the owner may change later.</returns>
    public NativeMenuItemSpec AddMenu(string text, Action<MenuBuilder> build) {
        ArgumentNullException.ThrowIfNull(build);

        return Emit(() => Spec.AddMenu(text, child => build(new MenuBuilder(child))));
    }

    /// <summary>
    /// Asks for a separator before the next item. Nothing is added unless an item follows and one
    /// came before it, and two requests in a row make one separator.
    /// </summary>
    public void AddSeparator() => _separatorPending = Spec.Items.Count > 0;

    private NativeMenuItemSpec Emit(Action add) {
        if (_separatorPending) {
            Spec.AddSeparator();
            _separatorPending = false;
        }

        add();

        return Spec.Items[^1];
    }
}
