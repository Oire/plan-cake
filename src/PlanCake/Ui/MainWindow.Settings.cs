using GetText.WindowsForms;
using Oire.PlanCake.Notes;
using Oire.PlanCake.Utils;
using Serilog;
using static Oire.PlanCake.Utils.Localization;

namespace Oire.PlanCake.Ui;

internal sealed partial class MainWindow {
    /// <summary>
    /// File → Settings: on OK the dialog has saved the settings; they then apply at once, without
    /// a restart. The confirmations, the "ask first" reload and the Enter keys are read from
    /// <see cref="Config"/> each time they are needed; the rest is applied here.
    /// </summary>
    private void ShowSettings() {
        var before = CurrentSettings();
        var saveFailed = false;

        // Every window is a process of its own with its own copy of the settings: read the file
        // again, so the dialog shows (and OK keeps) what another window saved since. A file that
        // cannot be read keeps the settings in memory, never the defaults, for the dialog to show.
        Config.Reload();

        using (var dialog = new SettingsDialog()) {
            if (dialog.ShowDialog(this) == DialogResult.OK) {
                saveFailed = dialog.SaveFailed;
            }
        }

        // After Cancel too: what another window saved applies here as well.
        ApplySettings(before);

        if (saveFailed) {
            ShowError(_("The settings could not be saved. They apply until PlanCake is closed."));
        }
    }

    /// <summary>What <see cref="ApplySettings"/> compares with to find what changed.</summary>
    private static AppliedSettings CurrentSettings() => new(
        Config.General.Language,
        Config.General.DefaultDocumentLanguage,
        Config.General.ShowNotesList,
        Config.Notes.ToMarkers(),
        Config.Advanced.ConvertToUtf8
    );

    /// <summary>
    /// Every window is a process of its own with its own copy of the settings: what another window
    /// saved (the note markers above all, which the notes written here must use) applies here as
    /// soon as this window is active again, before anything can be written with the old settings.
    /// </summary>
    /// <remarks>
    /// Posted, not run at once: the window is activated again while a dialog it opened closes,
    /// before the action that opened it (a note written, notes deleted after a question) has run,
    /// and that action must finish with the settings, and the render, the user saw.
    /// </remarks>
    protected override void OnActivated(EventArgs e) {
        base.OnActivated(e);

        if (!StartupFailed && !IsDisposed && IsHandleCreated) {
            BeginInvoke(ReloadSettingsIfChanged);
        }
    }

    private void ReloadSettingsIfChanged() {
        // While a file opens, the next activation applies them.
        if (StartupFailed || IsDisposed || _opening) {
            return;
        }

        var before = CurrentSettings();

        if (Config.ReloadIfChanged()) {
            Log.Information("Settings file changed; applying it");

            // The focus stays where activation puts it back.
            ApplySettings(before, returnFocus: false);
        }
    }

    /// <summary>Applies what Settings changed, compared with <paramref name="before"/>.</summary>
    private void ApplySettings(AppliedSettings before, bool returnFocus = true) {
        var general = Config.General;
        var needsRender = false;

        if (Config.Notes.ToMarkers() is var markers && markers != before.Markers) {
            _markers = markers;

            if (_notes is not null) {
                _notes.Store.Markers = markers;
            }

            Log.Information("Note markers set to {Opening} … {Closing}", markers.Opening, markers.Closing);
            needsRender = true;
        }

        // The open document follows a new default unless the user chose its language from the View menu.
        if (general.DefaultDocumentLanguage != before.DefaultDocumentLanguage
            && _documentLanguage == before.DefaultDocumentLanguage) {
            _documentLanguage = general.DefaultDocumentLanguage;
            needsRender = true;
        }

        if (general.ShowNotesList != before.ShowNotesList && general.ShowNotesList != _showNotesList) {
            ShowNotesList(general.ShowNotesList);
        }

        // A file opened read-only because it is not UTF-8 is opened again, which converts it.
        if (Config.Advanced.ConvertToUtf8 && !before.ConvertToUtf8
            && _file is { IsReadOnly: true, IsUnrecognized: false, ConvertedFrom: null } file && !_fileMissing) {
            LoadFile(file.Path, _position, recordHistory: false);
            needsRender = false;
        }

        if (!String.Equals(general.Language, before.Language, StringComparison.OrdinalIgnoreCase)) {
            Utils.Localization.SetLanguage(general.Language);
            Log.Information("Interface language set to {Language}", general.Language);

            // Out of the menu command first: a switch of direction recreates the window's handle.
            // The new render that comes with it shows the other changes too.
            BeginInvoke(ApplyLocalization);
            needsRender = false;
        }

        // Does nothing when the interval has not changed, or in a window that does not do the
        // background checks.
        _updateService?.ConfigurePeriodicChecks(general.UpdateCheckInterval);

        if (needsRender) {
            RenderDocument(restorePosition: false);
        }

        if (returnFocus) {
            ReturnFocus();
        }
    }

    /// <summary>
    /// View → Interface language: saves the choice and switches the menus, the window and the
    /// page chrome to it at once. The document's own language does not change. Like Settings,
    /// it reads the file again first (<see cref="Config.SaveLanguage"/>), so what another window
    /// saved since is kept, and applies here too.
    /// </summary>
    private void SetInterfaceLanguage(string code) {
        if (String.Equals(Config.General.Language, code, StringComparison.OrdinalIgnoreCase)) {
            return;
        }

        var before = CurrentSettings();

        if (!Config.SaveLanguage(code)) {
            ShowError(_("The settings could not be saved. They apply until PlanCake is closed."));
        }

        // Switches the language (out of the menu command: a switch of direction recreates the
        // window's handle) and applies whatever else changed in the file.
        ApplySettings(before);
    }

    /// <summary>
    /// Translates the window into the current interface language, as SIC does: the designer's
    /// texts from English again, the direction, what the walk of the controls does not reach (the
    /// list's columns and name, the menu, the title), the page chrome, and a new render, whose
    /// notes carry the localized "User note" label.
    /// </summary>
    private void ApplyLocalization() {
        Localizer.Revert(this, _localizationStore);
        Localizer.Localize(this, Utils.Localization.Catalog, _localizationStore);
        TextDirection.Apply(this);
        LocalizeNotesList();
        _menuBar?.Attach(BuildMenuSpec());
        _notesListMenu?.Rebuild(NotesListMenuSpec(EditSelectedNote, DeleteSelectedNote));
        UpdateTitle();

        if (_pageReady) {
            PostStrings();
        }

        RenderDocument(restorePosition: false);
    }

    /// <summary>
    /// View → Document language: renders the open document again with this <c>lang</c>, which
    /// picks the screen reader's voice. For this document only: another file starts with the default.
    /// </summary>
    private void SetDocumentLanguage(string code) {
        if (_file is null || _documentLanguage == code) {
            return;
        }

        _documentLanguage = code;

        // A file open read-only in a legacy encoding is read again: the new language may pick
        // the right code page for it.
        if (_file is { IsReadOnly: true } file && !_fileMissing) {
            LoadFile(file.Path, _position, recordHistory: false);
        } else {
            RenderDocument(restorePosition: false);
        }

        ReturnFocus();
        _announcer.Announce(_("Document language: {0}", LanguageList.NativeName(code)));
    }
}

/// <summary>The settings <c>MainWindow</c> applies itself, as they were before the Settings dialog.</summary>
internal sealed record AppliedSettings(
    string Language,
    string DefaultDocumentLanguage,
    bool ShowNotesList,
    NoteMarkers Markers,
    bool ConvertToUtf8
);
