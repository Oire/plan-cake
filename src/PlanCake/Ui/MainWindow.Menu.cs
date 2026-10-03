using Oire.PlanCake.Utils;
using Oire.WinForms.NativeControls;
using static Oire.PlanCake.Utils.Localization;

namespace Oire.PlanCake.Ui;

internal sealed partial class MainWindow {
    /// <summary>
    /// The menu bar, as data (Technical details → "Menus" in the plan). The shortcuts are shown
    /// only: <see cref="HostCommands"/> runs them, from the document too. Enabled and checked
    /// states are set again from the window's state whenever the menu bar opens (<see cref="WndProc"/>).
    /// Internal for the tests, which check the mnemonics of every menu level in every catalog.
    /// </summary>
    internal NativeMenuSpec BuildMenuSpec() {
        _menuEnabledWhen.Clear();
        _menuCheckedWhen.Clear();

        var spec = new NativeMenuSpec();

        spec.AddSubmenu(_("&File"), file => {
            MenuCommand(file, _("&Open..."), HostCommand.Open);
            MenuCommand(file, _("Open from &clipboard"), HostCommand.OpenFromClipboard);
            MenuCommand(file, _("Open from &link..."), HostCommand.OpenFromLink);
            file.AddSeparator();
            EnabledWhen(MenuCommand(file, _("Open in &editor"), HostCommand.OpenInEditor), FileIsThere);
            EnabledWhen(file.AddItem(_("Export &notes..."), null, ExportNotes), HasFile);
            file.AddSeparator();
            MenuCommand(file, _("&Settings..."), HostCommand.Settings);
            file.AddSeparator();
            file.AddItem(_("E&xit"), HostCommands.KeyText(Keys.Alt | Keys.F4), Close);
        });

        spec.AddSubmenu(_("&Edit"), edit => {
            EnabledWhen(MenuCommand(edit, _("&Undo"), HostCommand.Undo), () => FileIsThere() && _notes?.Store.CanUndo == true);
            EnabledWhen(MenuCommand(edit, _("&Redo"), HostCommand.Redo), () => FileIsThere() && _notes?.Store.CanRedo == true);
            edit.AddSeparator();
            EnabledWhen(edit.AddItem(_("&Delete all notes"), null, DeleteAllNotes), () => FileIsThere() && HasNotes());
        });

        spec.AddSubmenu(_("&View"), view => {
            CheckedWhen(view.AddCheckableItem(_("&Notes list"), _showNotesList, null, ToggleNotesList), () => _showNotesList);
            MenuCommand(view, _("&Switch pane"), HostCommand.SwitchPane);
            EnabledWhen(view.AddItem(_("&Wider notes list"), null, () => ResizeNotesList(larger: true)), () => IsNotesListVisible);
            EnabledWhen(view.AddItem(_("N&arrower notes list"), null, () => ResizeNotesList(larger: false)), () => IsNotesListVisible);
            view.AddSeparator();

            // Language names carry no mnemonics: each is written in its own language.
            view.AddSubmenu(_("&Interface language"), languages => {
                var options = LanguageList.InterfaceLanguages(_("System default"));

                foreach (var option in options) {
                    var code = option.Code;
                    CheckedWhen(
                        languages.AddRadioItem(option.Name, "interfaceLanguage", false, () => SetInterfaceLanguage(code)),
                        () => LanguageList.Find(options, Config.General.Language).Code == code
                    );
                }
            });

            EnabledWhen(view.AddSubmenu(_("&Document language"), languages => {
                foreach (var option in LanguageList.DocumentLanguages()) {
                    var code = option.Code;
                    CheckedWhen(
                        languages.AddRadioItem(option.Name, "documentLanguage", false, () => SetDocumentLanguage(code)),
                        () => _documentLanguage == code
                    );
                }
            }), HasFile);

            view.AddSeparator();
            MenuCommand(view, _("&Zoom in"), HostCommand.ZoomIn);
            MenuCommand(view, _("Zoom &out"), HostCommand.ZoomOut);
            MenuCommand(view, _("R&eset zoom"), HostCommand.ResetZoom);
            view.AddSeparator();
            EnabledWhen(MenuCommand(view, _("&Back"), HostCommand.Back), () => _history.CanGoBack);
            EnabledWhen(MenuCommand(view, _("&Forward"), HostCommand.Forward), () => _history.CanGoForward);
            EnabledWhen(MenuCommand(view, _("&Reload"), HostCommand.Reload), FileIsThere);
        });

        spec.AddSubmenu(_("&Notes"), notes => {
            EnabledWhen(notes.AddItem(_("&Edit note..."), null, EditCurrentNote), () => FileIsThere() && CurrentNote() is not null);
            EnabledWhen(notes.AddItem(_("&Delete note"), null, DeleteCurrentNote), () => FileIsThere() && CurrentNote() is not null);
            notes.AddSeparator();
            EnabledWhen(MenuCommand(notes, _("&Next note"), HostCommand.NextNote), HasNotes);
            EnabledWhen(MenuCommand(notes, _("&Previous note"), HostCommand.PreviousNote), HasNotes);
            notes.AddSeparator();
            EnabledWhen(MenuCommand(notes, _("Next &block"), HostCommand.NextBlock), HasFile);
            EnabledWhen(MenuCommand(notes, _("Previous b&lock"), HostCommand.PreviousBlock), HasFile);
        });

        spec.AddSubmenu(_("&Help"), help => {
            MenuCommand(help, _("&User manual"), HostCommand.UserManual);
            help.AddItem(_("&Keyboard shortcuts"), null, ShowShortcuts);
            help.AddSeparator();
            help.AddItem(_("&Check for updates"), null, CheckForUpdates);
            MenuCommand(help, _("&About PlanCake"), HostCommand.About);
        });

        RefreshMenuState();

        return spec;
    }

    /// <summary>A menu item that runs a host command and shows the command's first key.</summary>
    private NativeMenuItemSpec MenuCommand(NativeMenuSpec menu, string text, HostCommand command) =>
        menu.AddItem(text, HostCommands.MenuShortcut(command), () => RunCommand(command, Keys.None));

    private NativeMenuItemSpec EnabledWhen(NativeMenuItemSpec item, Func<bool> isEnabled) {
        _menuEnabledWhen.Add((item, isEnabled));
        return item;
    }

    private NativeMenuItemSpec CheckedWhen(NativeMenuItemSpec item, Func<bool> isChecked) {
        _menuCheckedWhen.Add((item, isChecked));
        return item;
    }

    /// <summary>Sets every menu item's enabled and checked state from the window's state.</summary>
    private void RefreshMenuState() {
        foreach (var (item, isEnabled) in _menuEnabledWhen) {
            item.IsEnabled = isEnabled();
        }

        foreach (var (item, isChecked) in _menuCheckedWhen) {
            item.IsChecked = isChecked();
        }
    }

    private bool HasFile() => _file is not null;

    /// <summary>
    /// True when a file is open and still on disk, so it can be written, reloaded and opened
    /// elsewhere.
    /// </summary>
    private bool FileIsThere() => _file is not null && !_fileMissing;

    private bool HasNotes() => _render is { Notes.Count: > 0 };
}
