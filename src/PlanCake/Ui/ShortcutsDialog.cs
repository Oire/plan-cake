using GetText.WindowsForms;
using Oire.PlanCake.Utils;
using Oire.WinForms.NativeControls;
using static Oire.PlanCake.Utils.Localization;

namespace Oire.PlanCake.Ui;

/// <summary>One row of the keyboard shortcuts dialog.</summary>
/// <param name="Command">What the keys do.</param>
/// <param name="Shortcut">The keys, several separated by commas.</param>
internal sealed record ShortcutRow(string Command, string Shortcut);

/// <summary>
/// Help → Keyboard shortcuts: every shortcut PlanCake has, in one list. The host shortcuts come
/// from <see cref="HostCommands"/>, the same table that runs them, so the list cannot drift from
/// what the keys do; the keys that belong to the document and the notes list, which no menu
/// shows, are listed after them.
/// </summary>
internal sealed partial class ShortcutsDialog: Form {
    /// <param name="rows">The rows to show, from <see cref="BuildRows"/>.</param>
    public ShortcutsDialog(IReadOnlyList<ShortcutRow> rows) {
        ArgumentNullException.ThrowIfNull(rows);

        InitializeComponent();
        Localizer.Localize(this, Utils.Localization.Catalog);
        TextDirection.Apply(this);
        Text = _("Keyboard shortcuts");

        shortcutsList.Columns.Add(new NativeListViewColumn(_("Command"), LogicalToDeviceUnits(420)));
        shortcutsList.Columns.Add(new NativeListViewColumn(_("Shortcut"), LogicalToDeviceUnits(180)));

        // A list view is not named by the label before it: it is named itself (see CLAUDE.md).
        shortcutsList.AccessibleName = _("Keyboard shortcuts");
        shortcutsList.GotFocus += OnListGotFocus;

        foreach (var row in rows) {
            shortcutsList.Items.Add(new NativeListViewItem(row.Command, row.Shortcut));
        }

        ActiveControl = shortcutsList;
    }

    /// <summary>
    /// Every shortcut: the host commands with all their keys, then the keys of the
    /// document and of the notes list, then Exit.
    /// </summary>
    internal static IReadOnlyList<ShortcutRow> BuildRows() {
        var rows = HostCommands.Shortcuts()
            .Select(entry => new ShortcutRow(
                HostCommands.DisplayName(entry.Command),
                String.Join(", ", entry.Keys.Select(HostCommands.KeyText))
            ))
            .ToList();

        var enter = HostCommands.KeyText(Keys.Enter);
        var menuKeys = String.Join(", ", HostCommands.KeyText(Keys.Apps), HostCommands.KeyText(Keys.Shift | Keys.F10));

        rows.Add(new ShortcutRow(_("In the document: add a note after a block, or edit a note"), enter));
        rows.Add(new ShortcutRow(_("In the document: menu of a block or a note"), menuKeys));
        rows.Add(new ShortcutRow(
            _("In the document: check or uncheck a task"),
            String.Join(", ", HostCommands.KeyText(Keys.Space), enter)
        ));
        rows.Add(new ShortcutRow(_("In the notes list: go to the note in the document"), enter));
        rows.Add(new ShortcutRow(_("In the notes list: delete the note"), HostCommands.KeyText(Keys.Delete)));
        rows.Add(new ShortcutRow(_("In the notes list: menu of the note"), menuKeys));
        rows.Add(new ShortcutRow(_("Exit"), HostCommands.KeyText(Keys.Alt | Keys.F4)));

        return rows;
    }

    protected override void OnShown(EventArgs e) {
        base.OnShown(e);
        NameList();

        // A list with nothing selected says nothing when it gets the focus.
        if (shortcutsList.Items.Count > 0) {
            var first = shortcutsList.Items[0];
            first.Selected = true;
            first.Focused = true;
        }
    }

    private void OnListGotFocus(object? sender, EventArgs e) => NameList();

    /// <summary>
    /// Names the list window for MSAA, which is what JAWS reads (see
    /// <see cref="WindowAccessibleName"/>).
    /// </summary>
    private void NameList() => WindowAccessibleName.Set(shortcutsList.ListHandle, shortcutsList.AccessibleName ?? String.Empty);

    protected override void OnFormClosed(FormClosedEventArgs e) {
        shortcutsList.GotFocus -= OnListGotFocus;
        base.OnFormClosed(e);
    }
}
