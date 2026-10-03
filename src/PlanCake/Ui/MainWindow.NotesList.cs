using Oire.PlanCake.Rendering;
using Oire.PlanCake.Utils;
using Oire.WinForms.NativeControls;
using static Oire.PlanCake.Utils.Localization;

namespace Oire.PlanCake.Ui;

internal sealed partial class MainWindow {
    /// <summary>True while the notes list is shown.</summary>
    private bool IsNotesListVisible => !splitContainer.Panel2Collapsed;

    /// <summary>True while the notes list is shown and has the keyboard focus.</summary>
    private bool IsNotesListFocused => IsNotesListVisible && notesList.ContainsFocus;

    /// <summary>
    /// The columns Note, Lines and Block: the note first, filling the width the other two leave
    /// (<see cref="NotesListView.FitFirstColumn"/>), with the whole note as the row's info tip.
    /// </summary>
    private void SetUpNotesList() {
        notesList.Columns.Add(new NativeListViewColumn(String.Empty, LogicalToDeviceUnits(200)));
        notesList.Columns.Add(new NativeListViewColumn(String.Empty, LogicalToDeviceUnits(60)));
        notesList.Columns.Add(new NativeListViewColumn(String.Empty, LogicalToDeviceUnits(120)));
        notesList.InfoTip = item => item.Tag is RenderedNote note ? TextDirection.Embed(NotesListRow.TipText(note)) : null;
        LocalizeNotesList();

        notesList.ItemActivate += OnNotesListItemActivate;
        notesList.KeyDown += OnNotesListKeyDown;
        notesList.GotFocus += OnNotesListGotFocus;

        _notesListMenu = new NativeContextMenu(NotesListMenuSpec(EditSelectedNote, DeleteSelectedNote)) {
            Resolver = ResolveNotesListMenu,
        };
        _notesListMenu.AttachTo(notesList);

        splitContainer.Panel2Collapsed = !_showNotesList;
    }

    /// <summary>
    /// The list's column headers and name, which the catalog walk of <c>Localizer</c> does not
    /// reach: the columns are not controls. Screen readers do not read a preceding label for a
    /// list view, so the list is named itself. Call this again after any walk of the controls
    /// (a live language switch): <c>AccessibleName</c> is forwarded only when it is set through
    /// a <see cref="NativeListView"/>-typed reference.
    /// </summary>
    private void LocalizeNotesList() {
        notesList.Columns[0].Text = _("Note");
        notesList.Columns[1].Text = _("Lines");
        notesList.Columns[2].Text = _("Block");
        notesList.AccessibleName = _("Notes");
        _notesListName = null;
        NameNotesList();
    }

    /// <summary>
    /// Names the list window itself for MSAA, which is what JAWS reads: the system proxy of a
    /// list view ignores the window text <c>AccessibleName</c> sets (see
    /// <see cref="WindowAccessibleName"/>). The list window exists only once the control has a
    /// handle, and a right-to-left switch recreates it, so this runs again on the way in.
    /// </summary>
    private void NameNotesList() {
        var handle = notesList.ListHandle;
        var name = notesList.AccessibleName ?? String.Empty;

        if (handle == IntPtr.Zero || _notesListName == (handle, name)) {
            return;
        }

        if (WindowAccessibleName.Set(handle, name)) {
            _notesListName = (handle, name);
        }
    }

    // The container gets the focus first and hands it to the list window right after this.
    private void OnNotesListGotFocus(object? sender, EventArgs e) => NameNotesList();

    /// <summary>
    /// The context menu of the list; its items act on the selected note. Internal for the tests,
    /// which check its mnemonics in every catalog.
    /// </summary>
    internal static NativeMenuSpec NotesListMenuSpec(Action editNote, Action deleteNote) => new NativeMenuSpec()
        .Add(_("&Edit note..."), editNote)
        .Add(_("&Delete note"), deleteNote);

    /// <summary>
    /// A right-click selects the row under the pointer first; without a selected note there is
    /// no menu.
    /// </summary>
    private NativeContextMenu? ResolveNotesListMenu(NativeContextMenuRequest request) {
        if (!request.FromKeyboard
            && notesList.GetItemAt(notesList.PointToClient(request.ScreenLocation)) is { } item) {
            SelectListItem(item);
        }

        return SelectedListNote() is null ? null : _notesListMenu;
    }

    /// <summary>
    /// Shows the notes of <paramref name="render"/> in the list. The selection stays on the same
    /// note (<see cref="PositionRestorer.FindNote"/>), or goes to <paramref name="focused"/> when
    /// given. Rows that did not change are left alone, so a screen reader in the list hears nothing.
    /// </summary>
    private void FillNotesList(RenderResult render, RenderedNote? previous, RenderedNote? focused) {
        var startOfDocument = _("The start of the document");
        // Every cell keeps its own direction: a right-to-left interface would move the punctuation
        // of a note or an excerpt.
        var rows = render.Notes
            .Select(note => NotesListRow.From(note, startOfDocument).ToCells().Select(TextDirection.Embed).ToArray())
            .ToList();
        var unchanged = rows.Count == notesList.Items.Count
            && rows.Select((cells, index) => cells.SequenceEqual(notesList.Items[index].Cells)).All(same => same);

        RenderedNote? selected;

        if (unchanged) {
            for (var index = 0; index < rows.Count; index++) {
                notesList.Items[index].Tag = render.Notes[index];
            }

            selected = focused;
        } else {
            selected = focused ?? PositionRestorer.FindNote(previous, render.Notes);
            notesList.BeginUpdate();

            try {
                notesList.Items.Clear();

                for (var index = 0; index < rows.Count; index++) {
                    notesList.Items.Add(new NativeListViewItem(rows[index]) { Tag = render.Notes[index] });
                }
            } finally {
                notesList.EndUpdate();
            }

            // Rows added or removed may show or hide the scroll bar.
            notesList.FitFirstColumn();
        }

        if (selected is not null && selected.Index < notesList.Items.Count) {
            SelectListItem(notesList.Items[selected.Index]);
        }
    }

    /// <summary>Selects <paramref name="item"/> alone, gives it the list's focus rectangle and scrolls to it.</summary>
    private void SelectListItem(NativeListViewItem item) {
        if (item.Selected && item.Focused) {
            return;
        }

        notesList.ClearSelection();
        item.Selected = true;
        item.Focused = true;
        item.EnsureVisible();
    }

    /// <summary>
    /// Selects <paramref name="note"/> in the list without moving the focus, so the list follows
    /// the note the user acts on or moves to in the document. The page cannot see the JAWS
    /// virtual cursor, so merely reading past a note does not move the selection.
    /// </summary>
    private void SelectNoteInList(RenderedNote note) {
        if (note.Index < notesList.Items.Count && notesList.Items[note.Index].Tag is RenderedNote listed && listed == note) {
            SelectListItem(notesList.Items[note.Index]);
        }
    }

    /// <summary>The note selected in the list, from the current render, or <see langword="null"/>.</summary>
    private RenderedNote? SelectedListNote() =>
        notesList.SelectedItems.Count > 0 && notesList.SelectedItems[0].Tag is RenderedNote note ? note : null;

    /// <summary>
    /// View → Notes list: shows or hides the list. A hidden list is skipped by F6. This is the
    /// setting Settings → Show the notes list holds, and is saved as such
    /// (<see cref="Config.SaveShowNotesList"/>, which reads the file again first, as View →
    /// Interface language does); other windows follow when they are activated. A failed save
    /// says so, and the list is shown or hidden here all the same.
    /// </summary>
    private void ToggleNotesList() {
        var show = !_showNotesList;
        var before = CurrentSettings();

        if (!Config.SaveShowNotesList(show)) {
            ShowError(_("The settings could not be saved. They apply until PlanCake is closed."));
        }

        ShowNotesList(show);

        // Whatever else the file read again changed applies too; the focus stays where it is.
        ApplySettings(before, returnFocus: false);
    }

    /// <summary>Shows or hides the notes list, and says so.</summary>
    private void ShowNotesList(bool show) {
        var hadFocus = IsNotesListFocused;
        _showNotesList = show;
        splitContainer.Panel2Collapsed = !_showNotesList;

        if (hadFocus) {
            FocusDocument();
        }

        _announcer.Announce(_showNotesList ? _("Notes list shown") : _("Notes list hidden"));
    }

    /// <summary>
    /// View → Wider notes list / Narrower notes list: the keyboard's way to move the splitter,
    /// which is not a tab stop (Tab goes straight between the document and the list). Moves it a
    /// step (<see cref="PaneSplit"/>) and says the list's new share.
    /// </summary>
    private void ResizeNotesList(bool larger) {
        if (!IsNotesListVisible) {
            return;
        }

        var total = splitContainer.Orientation == Orientation.Vertical ? splitContainer.Width : splitContainer.Height;
        var available = total - splitContainer.SplitterWidth;
        var list = available - splitContainer.SplitterDistance;
        var size = PaneSplit.Resize(available, list, splitContainer.Panel2MinSize, splitContainer.Panel1MinSize, larger);

        if (size != list) {
            splitContainer.SplitterDistance = available - size;
        }

        _announcer.Announce(_("Notes list {0}%", PaneSplit.Percent(available, size)));
    }

    /// <summary>F6: from the document to the notes list and back; to the document while the list is hidden.</summary>
    private void SwitchPane() {
        if (!IsNotesListVisible || IsNotesListFocused) {
            FocusDocument();
            return;
        }

        // A list with nothing selected says nothing when it gets the focus.
        if (notesList.Items.Count > 0 && notesList.SelectedItems.Count == 0) {
            SelectListItem(notesList.Items[0]);
        }

        notesList.Focus();

        if (notesList.Items.Count == 0) {
            _announcer.Announce(_("No notes"));
        }
    }

    /// <summary>F9 / Shift+F9 in the list: the next or previous row.</summary>
    private void MoveListSelection(bool forward) {
        var count = notesList.Items.Count;
        var current = notesList.SelectedItems.Count > 0 ? notesList.SelectedItems[0].Index : -1;
        var next = current < 0
            ? (forward ? 0 : count - 1)
            : current + (forward ? 1 : -1);

        if (next < 0 || next >= count) {
            _announcer.Announce(_("No more notes"));
            return;
        }

        SelectListItem(notesList.Items[next]);
    }

    /// <summary>Enter (or a double-click) on a row: the note in the document, with the focus.</summary>
    private void OnNotesListItemActivate(object? sender, NativeListViewItemEventArgs e) {
        if (e.Item.Tag is RenderedNote note) {
            BeginInvoke(() => JumpToNote(note));
        }
    }

    private void JumpToNote(RenderedNote note) {
        if (!_pageReady || _render is null || !_render.Notes.Contains(note)) {
            return;
        }

        _position = note.Block ?? _position;
        _currentNote = note;
        documentView.PostMessage(new FocusNoteMessage(note.Index));
        FocusDocument();
    }

    private void OnNotesListKeyDown(object? sender, KeyEventArgs e) {
        if (e.KeyData == Keys.Delete) {
            e.Handled = true;
            BeginInvoke(DeleteSelectedNote);
        }
    }

    private void EditSelectedNote() {
        if (SelectedListNote() is { } note) {
            EditNote(CurrentTarget(note), note);
        }
    }

    private void DeleteSelectedNote() {
        if (SelectedListNote() is { } note) {
            DeleteNote(CurrentTarget(note), note);
        }
    }
}
