using System.Runtime.InteropServices;
using Oire.PlanCake.Notes;
using Oire.PlanCake.Rendering;
using Oire.PlanCake.Utils;
using Oire.WinForms.NativeControls;
using Serilog;
using static Oire.PlanCake.Utils.Localization;

namespace Oire.PlanCake.Ui;

internal sealed partial class MainWindow {
    /// <summary>
    /// Enter or a click on a block (or Add note in its menu): asks for a note and writes it after
    /// the block.
    /// </summary>
    private void AddNote(NoteTarget target, BlockInfo block) {
        if (_notes is not { } notes) {
            return;
        }

        if ((CannotChangeReason(notes) ?? notes.UnterminatedAddReason(target.RenderedText, block)) is { } reason) {
            _announcer.Announce(reason);
            return;
        }

        var text = TakeDraft(NoteDialogMode.Add, block.Text) ?? String.Empty;
        RunNoteDialog(
            NoteDialogMode.Add, block.Excerpt, block.Text, text,
            noteText => notes.Add(target.RenderedText, block, noteText)
        );
    }

    /// <summary>Enter or a click on a note (or Edit note in its menu): edits its text.</summary>
    private void EditNote(NoteTarget target, RenderedNote note) {
        if (_notes is not { } notes) {
            return;
        }

        if ((CannotChangeReason(notes) ?? NoteActionRunner.UnterminatedReason(note.Note)) is { } reason) {
            _announcer.Announce(reason);
            return;
        }

        var text = TakeDraft(NoteDialogMode.Edit, note.Note.Text) ?? note.Note.Text;
        RunNoteDialog(
            NoteDialogMode.Edit, BlockExcerpt(note), note.Note.Text, text,
            noteText => notes.Edit(target.RenderedText, note.Note, noteText)
        );
    }

    /// <summary>
    /// Shows the note dialog until the note is written or the user cancels. A failed write
    /// reopens the dialog with the text; when the file changed on disk, the document is shown
    /// again and the text kept in <see cref="_draft"/> for the next attempt on the same block or note.
    /// </summary>
    private void RunNoteDialog(
        NoteDialogMode mode,
        string excerpt,
        string draftKey,
        string text,
        Func<string, NoteActionResult> save
    ) {
        while (_notes is { } notes) {
            using (var dialog = new NoteDialog(mode, excerpt, text, notes.DescribeTextError, Config.Notes.NoteEnterAction)) {
                if (dialog.ShowDialog(this) != DialogResult.OK) {
                    ReturnFocus();
                    return;
                }

                text = dialog.NoteText;
            }

            var result = save(text);
            ShowNoteResult(result);

            if (result.Status is NoteActionStatus.Failed or NoteActionStatus.InvalidText) {
                continue;
            }

            if (result.KeepsText) {
                _draft = new NoteDraft(mode, draftKey, text);
            }

            return;
        }
    }

    /// <summary>The kept draft for this block or note, taken out; <see langword="null"/> when there is none.</summary>
    private string? TakeDraft(NoteDialogMode mode, string key) {
        if (_draft is not { } draft || draft.Mode != mode || !String.Equals(draft.Key, key, StringComparison.Ordinal)) {
            return null;
        }

        _draft = null;

        return draft.Text;
    }

    /// <summary>Delete note: asks first (unless the setting says not to), then removes the note.</summary>
    private void DeleteNote(NoteTarget target, RenderedNote note) {
        if (_notes is not { } notes) {
            return;
        }

        if ((CannotChangeReason(notes) ?? NoteActionRunner.UnterminatedReason(note.Note)) is { } reason) {
            _announcer.Announce(reason);
            return;
        }

        if (Config.General.ConfirmNoteDelete) {
            var excerpt = TextDirection.Embed(MarkdownRenderer.Excerpt(MarkdownRenderer.NotePlainText(note.Note.Text)));
            var confirmed = DialogHelper.Confirm(_("Delete this note?\n\n{0}", excerpt), _("Delete note"));

            if (!confirmed) {
                ReturnFocus();
                return;
            }
        }

        _position = note.Block ?? _position;
        ShowNoteResult(notes.Delete(target.RenderedText, note.Note));
    }

    /// <summary>
    /// A task-list check box was toggled in the page: asks first (unless the setting says not to),
    /// then rewrites the item's marker in the file. When nothing is written, the check box is
    /// set back to the file's state.
    /// </summary>
    private void ToggleTask(NoteTarget target, BlockInfo item, bool isChecked) {
        if (_notes is not { } notes || _togglingTask) {
            return;
        }

        if (TaskToggle.IsChecked(target.RenderedText, item.StartLine) is not { } fileState) {
            Log.Warning("Task toggle on {Lines}, which holds no task marker", item.Lines);
            return;
        }

        _togglingTask = true;

        try {
            _position = item;

            if (CannotChangeReason(notes) is { } reason) {
                RevertTask(item, fileState);
                _announcer.Announce(reason);
                return;
            }

            if (Config.General.ConfirmTaskToggle && !ConfirmToggle(item, isChecked)) {
                RevertTask(item, fileState);
                FocusDocument();
                return;
            }

            var result = notes.ToggleTask(target.RenderedText, item.StartLine, isChecked);

            // Stale re-renders from the file, and the page sets every check box from the new
            // render; the check box is set back first all the same.
            if (result.Status != NoteActionStatus.Done) {
                RevertTask(item, fileState);
            }

            ShowNoteResult(result);
        } finally {
            _togglingTask = false;
        }
    }

    /// <summary>Asks before a check box rewrites the file on disk.</summary>
    private static bool ConfirmToggle(BlockInfo item, bool isChecked) {
        var text = TextDirection.Embed(TaskToggle.WithoutMarker(item.Excerpt));
        var question = isChecked
            ? _("Mark this task as done? The file on disk will be changed.\n\n{0}", text)
            : _("Mark this task as not done? The file on disk will be changed.\n\n{0}", text);

        return DialogHelper.Confirm(
            question,
            isChecked ? _("Check task") : _("Uncheck task")
        );
    }

    /// <summary>
    /// Why nothing can be written to the open file now, or <see langword="null"/> when it can:
    /// it is gone from disk, or it is open read-only.
    /// </summary>
    private string? CannotChangeReason(NoteActionRunner notes) =>
        _fileMissing ? MissingFileMessage() : notes.CannotWriteReason;

    private string MissingFileMessage() => _(
        "{0} is no longer there: it was deleted, renamed or moved. Notes cannot be changed until it is back.",
        _file is null ? String.Empty : Path.GetFileName(_file.Path)
    );

    /// <summary>
    /// A check box was toggled in a render that has since been replaced (the file changed): the
    /// toggle is not written, and the new render, on its way to the page, shows the file's state.
    /// </summary>
    private void TaskToggleDropped() {
        Log.Information("Task toggle from an older render dropped");
        _announcer.Announce(_("The file changed. Please try again."));
    }

    /// <summary>Sets the item's check box in the page back to the file's state.</summary>
    private void RevertTask(BlockInfo item, bool fileState) {
        if (_pageReady) {
            documentView.PostMessage(new TaskStateMessage(item.Lines, fileState));
        }
    }

    private void UndoOrRedo(bool redo) {
        if (_notes is not { } notes) {
            _announcer.Announce(redo ? _("Nothing to redo") : _("Nothing to undo"));
            return;
        }

        if (_fileMissing) {
            _announcer.Announce(MissingFileMessage());
            return;
        }

        ShowNoteResult(redo ? notes.Redo() : notes.Undo());
    }

    /// <summary>Shows the outcome of a note action: re-renders when the file changed, and tells the user.</summary>
    private void ShowNoteResult(NoteActionResult result) {
        // Written, or changed on disk: the file's text is shown again. Only a write has a note
        // or a check box to focus.
        if (result.NeedsRender) {
            RenderDocument(focusNoteLine: result.FocusNoteLine, focusTaskLine: result.FocusTaskLine);
            ReturnFocus();
            _announcer.Announce(result.Message);
            return;
        }

        switch (result.Status) {
            case NoteActionStatus.Failed:
            case NoteActionStatus.InvalidText:
                ShowError(result.Message);
                break;
            default:
                _announcer.Announce(result.Message);
                break;
        }
    }

    /// <summary>
    /// The items of the context menu of a block or a note: Add note, Edit note, Delete note and
    /// Copy block text, each only when its action is given. Internal for the tests, which check
    /// the mnemonics of the full menu in every catalog.
    /// </summary>
    internal static NativeMenuSpec BlockMenuSpec(Action? addNote, Action? editNote, Action? deleteNote, Action? copyText) {
        var spec = new NativeMenuSpec();

        if (addNote is not null) {
            spec.Add(_("&Add note..."), addNote);
        }

        if (editNote is not null) {
            spec.Add(_("&Edit note..."), editNote);
        }

        if (deleteNote is not null) {
            spec.Add(_("&Delete note"), deleteNote);
        }

        if (copyText is not null) {
            spec.Add(_("&Copy block text"), copyText);
        }

        return spec;
    }

    /// <summary>
    /// The context menu of a block or a note, at the element's position: Add note, then Edit and
    /// Delete note on a note, then Copy block text.
    /// </summary>
    private void ShowContextMenu(NoteTarget target, RectangleF? rect, double scale) {
        var block = target.Block;
        var note = target.Note;
        var spec = BlockMenuSpec(
            block is null ? null : WhileCurrent(target, () => AddNote(target, block)),
            note is null ? null : WhileCurrent(target, () => EditNote(target, note)),
            note is null ? null : WhileCurrent(target, () => DeleteNote(target, note)),
            block is null ? null : () => CopyBlockText(block)
        );

        if (spec.Items.Count == 0) {
            return;
        }

        var anchor = rect is { } bounds
            ? DocumentView.MenuAnchor(bounds, scale, documentView.ClientSize)
            : Point.Empty;

        using var menu = new NativeContextMenu(spec);
        menu.Show(this, documentView.PointToScreen(anchor));
    }

    private void CopyBlockText(BlockInfo block) {
        if (String.IsNullOrEmpty(block.Text)) {
            _announcer.Announce(_("The block has no text to copy."));
            return;
        }

        try {
            Clipboard.SetText(block.Text);
            _announcer.Announce(_("Block text copied"));
        } catch (ExternalException ex) {
            Log.Error(ex, "Unable to copy the block text to the clipboard");
            _announcer.Announce(_("Unable to copy to the clipboard"));
        }
    }

    /// <summary>The excerpt of the block a note is on, for the note dialog.</summary>
    private static string BlockExcerpt(RenderedNote note) =>
        note.Block?.Excerpt ?? _("The start of the document");

    /// <summary>
    /// The note Notes → Edit note and Delete note act on: the one selected in the list while the
    /// list has the focus, else the one the document is on.
    /// </summary>
    private RenderedNote? CurrentNote() {
        var note = IsNotesListFocused ? SelectedListNote() : _currentNote;

        return note is not null && _render is not null && _render.Notes.Contains(note) ? note : null;
    }

    private void EditCurrentNote() {
        if (CurrentNote() is { } note) {
            EditNote(CurrentTarget(note), note);
        }
    }

    private void DeleteCurrentNote() {
        if (CurrentNote() is { } note) {
            DeleteNote(CurrentTarget(note), note);
        }
    }

    /// <summary>Edit → Delete all notes: always asks first, then removes every note of the file.</summary>
    private void DeleteAllNotes() {
        if (_notes is not { } notes || _render is not { Notes.Count: > 0 } render) {
            _announcer.Announce(_("There are no notes to delete."));
            return;
        }

        var unterminated = render.Notes.FirstOrDefault(note => note.Note.Unterminated)?.Note;

        if ((CannotChangeReason(notes) ?? (unterminated is null ? null : NoteActionRunner.UnterminatedReason(unterminated)))
            is { } reason) {
            _announcer.Announce(reason);
            return;
        }

        // Taken before the question: while it is open, an outside change can reload the file, and
        // the notes it brings must not be deleted without being asked about (Clear then refuses).
        var renderedText = _renderedText;
        var count = render.Notes.Count;
        var confirmed = DialogHelper.Confirm(
            _n("Delete the note in this file?", "Delete all {0} notes in this file?", count, count),
            _("Delete all notes")
        );

        if (!confirmed) {
            ReturnFocus();
            return;
        }

        ShowNoteResult(notes.Clear(renderedText));
    }

    /// <summary>A note of the current render as the target of a note action.</summary>
    private NoteTarget CurrentTarget(RenderedNote note) => new(_generation, _renderedText, note.Block, note);
}

/// <summary>A note text kept after the file changed under it, for the next attempt on the same block or note.</summary>
/// <param name="Mode">Whether it was a new note or an edit.</param>
/// <param name="Key">The block's text (a new note) or the note's original text (an edit).</param>
/// <param name="Text">What the user typed.</param>
internal sealed record NoteDraft(NoteDialogMode Mode, string Key, string Text);
