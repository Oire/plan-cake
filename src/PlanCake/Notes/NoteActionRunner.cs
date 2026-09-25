using Oire.PlanCake.Rendering;
using Serilog;
using static Oire.PlanCake.Utils.Localization;

namespace Oire.PlanCake.Notes;

/// <summary>How a note action ended.</summary>
internal enum NoteActionStatus {
    /// <summary>The file was written (or undo or redo went through).</summary>
    Done,

    /// <summary>There was nothing to undo or redo; nothing was written.</summary>
    NothingToDo,

    /// <summary>The note text cannot be written as it is; nothing was written.</summary>
    InvalidText,

    /// <summary>
    /// The file changed on disk since it was rendered; nothing was written, and the view must be
    /// rendered again from the file's current text.
    /// </summary>
    Stale,

    /// <summary>The file could not be read or written; nothing was written.</summary>
    Failed,

    /// <summary>The file is open read-only and is never written.</summary>
    ReadOnly,
}

/// <summary>The outcome of a note action, for the window to show.</summary>
/// <param name="Status">How the action ended.</param>
/// <param name="Message">What to tell the user: an announcement, or the reason for a failure.</param>
/// <param name="FocusNoteLine">
/// After <see cref="NoteActionStatus.Done"/>, the 1-based line of the file's new text the note to
/// focus starts on; <see langword="null"/> when no note is left to focus (the view then returns to
/// the block the user was on).
/// </param>
/// <param name="FocusTaskLine">
/// After <see cref="NoteActionStatus.Done"/> on a task-list toggle (or its undo or redo), the
/// 1-based line the task-list item starts on, whose check box keeps the focus.
/// </param>
internal sealed record NoteActionResult(
    NoteActionStatus Status,
    string Message,
    int? FocusNoteLine = null,
    int? FocusTaskLine = null
) {
    /// <summary>True when the view must be rendered again from the file's current text.</summary>
    public bool NeedsRender => Status is NoteActionStatus.Done or NoteActionStatus.Stale;

    /// <summary>
    /// True when the text the user typed must be kept for a retry, because it was not written
    /// through no fault of its own.
    /// </summary>
    public bool KeepsText => Status is NoteActionStatus.Stale or NoteActionStatus.Failed or NoteActionStatus.InvalidText;
}

/// <summary>
/// Runs the note actions of the window through a <see cref="NoteStore"/> and turns each outcome
/// into what the user is told and where the view goes next, so that the window only shows it.
/// </summary>
internal sealed class NoteActionRunner {
    public NoteActionRunner(NoteStore store) {
        ArgumentNullException.ThrowIfNull(store);
        Store = store;
    }

    public NoteStore Store { get; }

    /// <summary>Why no note can be written to the file, or <see langword="null"/> when one can.</summary>
    public string? CannotWriteReason => Store.File.IsReadOnly ? ReadOnlyMessage : null;

    private static string ReadOnlyMessage =>
        _("This file is not in UTF-8, so it is open read-only. Notes cannot be written to it.");

    /// <summary>Why <paramref name="text"/> cannot be written as a note, or <see langword="null"/> when it can.</summary>
    public string? DescribeTextError(string text) {
        ArgumentNullException.ThrowIfNull(text);

        return Store.Validate(Store.NormalizeText(text)) switch {
            NoteTextError.None => null,
            NoteTextError.Empty => _("The note is empty."),
            NoteTextError.ContainsClosingMarker =>
                _("The note cannot contain {0}, which marks the end of a note.", Store.Markers.Closing),
            NoteTextError.ContainsOpeningMarker =>
                _("The note cannot contain {0}, which marks the start of a note.", Store.Markers.Opening),
            NoteTextError.ContainsLineBreak => _("The note cannot contain a line break."),
            var error => throw new InvalidOperationException($"Unknown note text error {error}."),
        };
    }

    /// <summary>Adds a note after <paramref name="block"/>.</summary>
    public NoteActionResult Add(string renderedText, BlockInfo block, string text) {
        ArgumentNullException.ThrowIfNull(block);

        return WriteNote(text, () => Store.Add(renderedText, block, text), _("Note added"));
    }

    /// <summary>Replaces the text of <paramref name="note"/>.</summary>
    public NoteActionResult Edit(string renderedText, Note note, string text) {
        ArgumentNullException.ThrowIfNull(note);

        return WriteNote(text, () => Store.Edit(renderedText, note, text), _("Note edited"));
    }

    /// <summary>Removes <paramref name="note"/>; the view returns to the block it was on.</summary>
    public NoteActionResult Delete(string renderedText, Note note) {
        ArgumentNullException.ThrowIfNull(note);

        return Run(
            () => Store.Delete(renderedText, note),
            change => new NoteActionResult(NoteActionStatus.Done, _("Note deleted")),
            _("The file changed. Please try again.")
        );
    }

    /// <summary>
    /// Checks or unchecks the task-list item starting on <paramref name="line"/>; the focus stays
    /// on its check box.
    /// </summary>
    public NoteActionResult ToggleTask(string renderedText, int line, bool isChecked) {
        if (CannotWriteReason is { } reason) {
            return new NoteActionResult(NoteActionStatus.ReadOnly, reason);
        }

        return Run(
            () => Store.ToggleTask(renderedText, line, isChecked),
            change => new NoteActionResult(
                NoteActionStatus.Done,
                isChecked ? _("Task checked") : _("Task unchecked"),
                FocusTaskLine: change.Line
            ),
            _("The file changed. Please try again.")
        );
    }

    /// <summary>Undoes the last change, announcing which one.</summary>
    public NoteActionResult Undo() {
        if (!Store.CanUndo) {
            return new NoteActionResult(NoteActionStatus.NothingToDo, _("Nothing to undo"));
        }

        return Run(
            Store.Undo,
            change => new NoteActionResult(
                NoteActionStatus.Done,
                UndoneMessage(change.Operation),
                change.Operation is NoteOperation.Edit or NoteOperation.Delete ? change.Line : null,
                TaskLine(change)
            ),
            _("The file changed, so there is nothing more to undo or redo.")
        );
    }

    /// <summary>Makes the last undone change again, announcing which one.</summary>
    public NoteActionResult Redo() {
        if (!Store.CanRedo) {
            return new NoteActionResult(NoteActionStatus.NothingToDo, _("Nothing to redo"));
        }

        return Run(
            Store.Redo,
            change => new NoteActionResult(
                NoteActionStatus.Done,
                RedoneMessage(change.Operation),
                change.Operation is NoteOperation.Add or NoteOperation.Edit ? change.Line : null,
                TaskLine(change)
            ),
            _("The file changed, so there is nothing more to undo or redo.")
        );
    }

    private NoteActionResult WriteNote(string text, Func<NoteChange> write, string doneMessage) {
        ArgumentNullException.ThrowIfNull(text);

        if (CannotWriteReason is { } reason) {
            return new NoteActionResult(NoteActionStatus.ReadOnly, reason);
        }

        if (DescribeTextError(text) is { } error) {
            return new NoteActionResult(NoteActionStatus.InvalidText, error);
        }

        return Run(
            write,
            change => new NoteActionResult(NoteActionStatus.Done, doneMessage, change.Line),
            _("The file changed. Please try again.")
        );
    }

    private NoteActionResult Run(Func<NoteChange> action, Func<NoteChange, NoteActionResult> done, string staleMessage) {
        try {
            var change = action();
            Log.Information(
                "{Operation} in {Path}: line={Line} count={Count}",
                change.Operation, Store.File.Path, change.Line, change.Count
            );

            return done(change);
        } catch (StaleFileException ex) {
            Log.Warning(ex, "Action refused: {Path} changed on disk", Store.File.Path);

            return new NoteActionResult(NoteActionStatus.Stale, staleMessage);
        } catch (ReadOnlyFileException ex) {
            Log.Warning(ex, "Action refused: {Path} is read-only", Store.File.Path);

            return new NoteActionResult(NoteActionStatus.ReadOnly, ReadOnlyMessage);
        } catch (IOException ex) {
            Log.Error(ex, "Action failed on {Path}", Store.File.Path);

            return new NoteActionResult(NoteActionStatus.Failed, _("Unable to write the file: {0}", ex.Message));
        }
    }

    /// <summary>The line of a toggled task-list item, whose check box gets the focus back.</summary>
    private static int? TaskLine(NoteChange change) =>
        change.Operation is NoteOperation.CheckTask or NoteOperation.UncheckTask ? change.Line : null;

    private static string UndoneMessage(NoteOperation operation) => operation switch {
        NoteOperation.Add => _("Note added undone"),
        NoteOperation.Edit => _("Note edited undone"),
        NoteOperation.Delete => _("Note deleted undone"),
        NoteOperation.Clear => _("All notes deleted undone"),
        NoteOperation.CheckTask => _("Task checked undone"),
        NoteOperation.UncheckTask => _("Task unchecked undone"),
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
    };

    private static string RedoneMessage(NoteOperation operation) => operation switch {
        NoteOperation.Add => _("Note added redone"),
        NoteOperation.Edit => _("Note edited redone"),
        NoteOperation.Delete => _("Note deleted redone"),
        NoteOperation.Clear => _("All notes deleted redone"),
        NoteOperation.CheckTask => _("Task checked redone"),
        NoteOperation.UncheckTask => _("Task unchecked redone"),
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
    };
}
