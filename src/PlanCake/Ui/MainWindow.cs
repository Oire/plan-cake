using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using GetText.WindowsForms;
using Oire.PlanCake.Notes;
using Oire.PlanCake.Rendering;
using Oire.PlanCake.Utils;
using Oire.PlanCake.Utils.Enums;
using Oire.WinForms.NativeControls;
using Serilog;
using static Oire.PlanCake.Utils.Localization;
using App = Oire.PlanCake.Utils.Constants.App;

namespace Oire.PlanCake.Ui;

public partial class MainWindow: Form {
    /// <summary>The page the document view shows, from the <c>web</c> folder.</summary>
    private const string PageFile = "index.html";

    // Settings that arrive with Task 12, hard-coded to their defaults until then. The document
    // language is English by default and never follows the interface language.
    private const string DocumentLanguage = "en";
    private const NoteEnterAction DocumentNoteEnterAction = NoteEnterAction.Save;
    private const bool ConfirmNoteDelete = true;
    private const bool ConfirmTaskToggle = true;

    /// <summary>The note markers, <c>[usernote]</c> … <c>[/usernote]</c> until Task 12 makes them a setting.</summary>
    private static readonly NoteMarkers _markers = NoteMarkers.Default;

    private readonly StatusAnnouncer _announcer;
    private readonly string? _initialFile;

    /// <summary>The files visited in this window, for Back and Forward.</summary>
    private readonly NavigationHistory _history = new();

    /// <summary>The open file, or <see langword="null"/> before the first one is opened.</summary>
    private MarkdownFile? _file;

    /// <summary>Adds, edits and deletes the notes of <see cref="_file"/>, with undo and redo.</summary>
    private NoteActionRunner? _notes;

    /// <summary>The render the page shows (or is about to show).</summary>
    private RenderResult? _render;

    /// <summary>The file text <see cref="_render"/> was made from.</summary>
    private string _renderedText = String.Empty;

    /// <summary>A note text that could not be written because the file changed, kept for the retry.</summary>
    private NoteDraft? _draft;

    /// <summary>The number of <see cref="_render"/>; a page message about an older one is ignored.</summary>
    private int _generation;

    /// <summary>The block the user last interacted with, from <see cref="_render"/>.</summary>
    private BlockInfo? _position;

    /// <summary>Where the page puts the virtual cursor when it shows <see cref="_render"/>.</summary>
    private PageFocus? _pendingFocus;

    /// <summary>True once the page has loaded and can take messages.</summary>
    private bool _pageReady;

    /// <summary>
    /// True while a task toggle is asking or writing: a second toggle (a double-click) is ignored,
    /// and the render that follows the first one resets the check box anyway.
    /// </summary>
    private bool _togglingTask;

    public MainWindow() : this(null) { }

    /// <param name="initialFile">The file to open once the window is up, from the command line.</param>
    internal MainWindow(string? initialFile) {
        InitializeComponent();

        // Walks the control tree and translates every text property through the gettext
        // catalog. Designer-set strings are therefore written in English and translated here;
        // strings built at run time go through _() instead.
        Localizer.Localize(this, Utils.Localization.Catalog);
        TextDirection.Apply(this);

        _initialFile = initialFile;
        _announcer = new StatusAnnouncer(statusStrip, statusLabel);
        documentView.MessageReceived += OnPageMessage;
        documentView.AcceleratorKeyDown += OnDocumentAcceleratorKeyDown;

        // Drops on the document itself arrive as a page message (the browser handles them);
        // these cover the rest of the window.
        AllowDrop = true;
        DragEnter += OnWindowDragEnter;
        DragDrop += OnWindowDragDrop;
    }

    /// <summary>
    /// True when the document view could not be started. The window closes itself in that case;
    /// <c>Program</c> reads this afterwards to choose the exit code.
    /// </summary>
    internal bool StartupFailed { get; private set; }

    protected override async void OnLoad(EventArgs e) {
        base.OnLoad(e);

        try {
            await documentView.InitializeAsync();
            documentView.Navigate(PageFile);
        } catch (Exception ex) {
            Log.Error(ex, "Unable to start the document view");
            StartupFailed = true;
            DialogHelper.Show(
                _("Unable to start the document view: {0}", ex.Message),
                _("Error"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
            Close();

            return;
        }

        if (!String.IsNullOrWhiteSpace(_initialFile)) {
            OpenFile(_initialFile);
        }
    }

    /// <summary>
    /// Opens a Markdown file in the window. Every way of opening one (the command line, drag and
    /// drop, a link in the document, and later File → Open, the clipboard and a web link) goes
    /// through here, and the file being left goes into the history for Back. On failure the
    /// user is told why and the current document stays.
    /// </summary>
    /// <returns>True when the file was opened.</returns>
    internal bool OpenFile(string path) => LoadFile(path, position: null, recordHistory: true);

    /// <param name="path">The file to open.</param>
    /// <param name="position">The block to return to in it (from the history), if any.</param>
    /// <param name="recordHistory">
    /// Push the file being left onto the history; false when moving through the history itself.
    /// </param>
    private bool LoadFile(string path, BlockInfo? position, bool recordHistory) {
        MarkdownFile file;

        try {
            // ConvertToUtf8 stays off (the default) until Task 12 wires the setting.
            file = MarkdownFile.Open(Path.GetFullPath(path), MarkdownFileOptions.Default);
        } catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) {
            Log.Warning(ex, "Unable to open {Path}: not found", path);
            ShowError(_("The file {0} does not exist.", path));

            return false;
        } catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException) {
            Log.Error(ex, "Unable to open {Path}", path);
            ShowError(_("Unable to open {0}: {1}", path, ex.Message));

            return false;
        }

        // Opening the same file again keeps the reading position; another file starts at the top.
        var reopened = _file is not null && String.Equals(_file.Path, file.Path, StringComparison.OrdinalIgnoreCase);

        if (!reopened) {
            if (recordHistory && _file is not null) {
                _history.Push(new HistoryEntry(_file.Path, _position));
            }

            _position = position;
        }

        _file = file;
        _notes = new NoteActionRunner(new NoteStore(file, _markers));
        _draft = null;

        RenderDocument(opened: !reopened);
        Text = _("{0} - {1}", Path.GetFileName(file.Path), App.Name);

        Log.Information(
            "Opened {Path}: encoding={Encoding} bom={Bom} readOnly={ReadOnly} convertedFrom={ConvertedFrom} notes={Notes}",
            file.Path, file.Encoding.WebName, file.HasBom, file.IsReadOnly, file.ConvertedFrom?.WebName, _render?.Notes.Count
        );

        AnnounceFileState(file);

        if (documentView.IsInitialized) {
            documentView.FocusDocument();
        }

        return true;
    }

    /// <summary>
    /// Renders the open file and sends it to the page. Without an explicit
    /// <paramref name="focus"/>, the page goes to the note starting on
    /// <paramref name="focusNoteLine"/>, or to the check box of the task-list item starting on
    /// <paramref name="focusTaskLine"/>, if given and found, else returns to the block the user was on.
    /// A document just <paramref name="opened"/> without a saved position starts at its first block.
    /// </summary>
    private void RenderDocument(
        PageFocus? focus = null,
        int? focusNoteLine = null,
        int? focusTaskLine = null,
        bool opened = false
    ) {
        if (_file is null) {
            return;
        }

        var options = new RenderOptions(
            _markers,
            RenderMode.Interactive,
            CurrentRenderStrings(),
            DocumentLanguage
        );
        _renderedText = _file.Text;
        _render = MarkdownRenderer.Render(_renderedText, options);
        _generation++;

        if (focus is null && focusNoteLine is { } line
            && _render.Notes.FirstOrDefault(note => note.Note.StartLine == line) is { } focusedNote) {
            focus = new PageFocus(Note: focusedNote.Index);
            _position = focusedNote.Block ?? _position;
        }

        if (focus is null && focusTaskLine is { } taskLine
            && _render.Blocks.FirstOrDefault(block => block.Kind == BlockKind.ListItem && block.StartLine == taskLine)
                is { } taskItem) {
            focus = new PageFocus(Lines: taskItem.Lines, Task: true);
            _position = taskItem;
        }

        var restored = opened
            ? PositionRestorer.FindOpeningTarget(_position, _render.Blocks)
            : PositionRestorer.FindTarget(_position, _render.Blocks);

        if (focus is null && restored is { } target) {
            focus = new PageFocus(Lines: target.Lines);
            _position = target;
        } else if (focus is null) {
            _position = null;
        }

        _pendingFocus = focus;
        PostRender();
    }

    private void PostRender() {
        if (!_pageReady || _render is null || _file is null) {
            return;
        }

        documentView.PostMessage(new RenderMessage(
            _render.Html,
            _generation,
            DocumentLanguage,
            _render.Title ?? Path.GetFileName(_file.Path),
            _pendingFocus
        ));
        _pendingFocus = null;
    }

    private static RenderStrings CurrentRenderStrings() =>
        new(_("user note"), _("unote"));

    private void PostStrings() => documentView.PostMessage(new StringsMessage(
        Utils.Localization.GetCurrentCulture().Name,
        TextDirection.IsRightToLeft ? "rtl" : "ltr",
        _("No file is open.")
    ));

    /// <summary>Tells the user what is special about the file just opened, if anything.</summary>
    private void AnnounceFileState(MarkdownFile file) {
        var messages = new List<string>();

        if (file.ConvertedFrom is { } convertedFrom) {
            messages.Add(_("Converted from {0} to UTF-8.", EncodingName(convertedFrom)));
        } else if (file.IsReadOnly) {
            messages.Add(_(
                "This file is not in UTF-8, so it was opened read-only as {0}. Notes cannot be added to it.",
                EncodingName(file.Encoding)
            ));
        }

        if (_render?.Parse.HasUnterminated == true) {
            messages.Add(_("A note has no closing marker, so it runs to the end of the file."));
        }

        if (messages.Count > 0) {
            _announcer.Announce(String.Join(" ", messages));
        }
    }

    /// <summary>An encoding's name as people write it: <c>Windows-1251</c>, <c>UTF-8</c>.</summary>
    private static string EncodingName(Encoding encoding) =>
        encoding.WebName.StartsWith("windows-", StringComparison.OrdinalIgnoreCase)
            ? $"Windows-{encoding.CodePage}"
            : encoding.WebName.ToUpperInvariant();

    private static void ShowError(string message) =>
        DialogHelper.Show(message, _("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);

    /// <summary>
    /// Host shortcuts pressed anywhere but in the document. Keys pressed in the document never
    /// get here (see <see cref="OnDocumentAcceleratorKeyDown"/>); both paths end in
    /// <see cref="TryRunShortcut"/>, so the one table in <see cref="HostCommands"/> decides.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) =>
        TryRunShortcut(keyData) || base.ProcessCmdKey(ref msg, keyData);

    /// <summary>
    /// Host shortcuts pressed in the document. The WebView2 control reports them as a
    /// <c>KeyDown</c> from inside a browser event, bypassing the message loop, the native menu's
    /// accelerator table and <see cref="ProcessCmdKey"/>.
    /// </summary>
    private void OnDocumentAcceleratorKeyDown(object? sender, KeyEventArgs e) {
        if (TryRunShortcut(e.KeyData, fromDocument: true)) {
            e.Handled = true;
        }
    }

    private bool TryRunShortcut(Keys keyData, bool fromDocument = false) {
        if (!HostCommands.TryGetCommand(keyData, out var command)) {
            return false;
        }

        // Backspace means Back only in the document and the window's lists, never in a box the
        // user types in.
        if (keyData == Keys.Back && !fromDocument
            && FocusedControl() is TextBoxBase or ComboBox or UpDownBase) {
            return false;
        }

        // The key may have come from inside a WebView2 event: run the command later, so that a
        // dialog it opens does not start a nested message loop inside that event.
        BeginInvoke(() => RunCommand(command, keyData));
        return true;
    }

    /// <summary>The innermost control of this window that has focus.</summary>
    private Control? FocusedControl() {
        Control? control = ActiveControl;

        while (control is ContainerControl { ActiveControl: { } inner }) {
            control = inner;
        }

        return control;
    }

    private void RunCommand(HostCommand command, Keys keyData) {
        Log.Debug("Host command {Command} from {Keys}", command, keyData);

        switch (command) {
            case HostCommand.ZoomIn:
                SetZoom(DocumentView.StepZoom(documentView.ZoomFactor, 1));
                break;
            case HostCommand.ZoomOut:
                SetZoom(DocumentView.StepZoom(documentView.ZoomFactor, -1));
                break;
            case HostCommand.ResetZoom:
                SetZoom(DocumentView.StepZoom(documentView.ZoomFactor, 0));
                break;
            case HostCommand.Back:
                MoveThroughHistory(back: true);
                break;
            case HostCommand.Forward:
                MoveThroughHistory(back: false);
                break;
            case HostCommand.NextNote:
                MoveToNote(forward: true);
                break;
            case HostCommand.PreviousNote:
                MoveToNote(forward: false);
                break;
            case HostCommand.Undo:
                UndoOrRedo(redo: false);
                break;
            case HostCommand.Redo:
                UndoOrRedo(redo: true);
                break;
            default:
                // The other commands arrive with their own tasks (menus, settings, notes list…).
                Log.Debug("Host command {Command} is not available yet", command);
                break;
        }
    }

    private void SetZoom(double zoom) {
        if (!documentView.IsInitialized) {
            return;
        }

        documentView.ZoomFactor = zoom;
        _announcer.Announce(_("Zoom {0}%", (int)Math.Round(documentView.ZoomFactor * 100)));
    }

    /// <summary>
    /// Back or Forward: opens the previous or next file at the block the user was on, skipping
    /// (and dropping) files that no longer exist.
    /// </summary>
    private void MoveThroughHistory(bool back) {
        var current = _file is null ? null : new HistoryEntry(_file.Path, _position);
        Func<HistoryEntry, bool> open = entry => LoadFile(entry.Path, entry.Position, recordHistory: false);
        var move = back
            ? _history.GoBack(current, File.Exists, open)
            : _history.GoForward(current, File.Exists, open);

        var messages = move.Missing
            .Select(path => _("{0} no longer exists and was removed from the history.", Path.GetFileName(path)))
            .ToList();

        if (move.Outcome == HistoryOutcome.AtEnd) {
            messages.Add(back ? _("No previous file") : _("No next file"));
        }

        if (messages.Count > 0) {
            Log.Information(
                "History {Direction}: {Outcome}, missing={Missing}",
                back ? "back" : "forward", move.Outcome, move.Missing
            );
            _announcer.Announce(String.Join(" ", messages));
        }
    }

    private void MoveToNote(bool forward) {
        if (!_pageReady || _render is null) {
            _announcer.Announce(_("No more notes"));
            return;
        }

        documentView.PostMessage(forward ? new NextNoteMessage() : new PreviousNoteMessage());
    }

    /// <summary>
    /// Every message the page sends. This runs inside a WebView2 event: anything that may open a
    /// dialog or a menu is deferred with <c>BeginInvoke</c>.
    /// </summary>
    private void OnPageMessage(object? sender, PageMessageEventArgs e) {
        switch (e.Type) {
            case PageMessages.Ready:
                Log.Information("Page ready");
                _pageReady = true;
                PostStrings();
                PostRender();
                documentView.FocusDocument();
                break;
            case PageMessages.Position:
                if (IsCurrentRender(e.Message)) {
                    UpdatePosition(e.Message);
                }

                break;
            case PageMessages.Activate:
                if (FindTarget(e.Message) is { Block: { } block } target) {
                    BeginInvoke(WhileCurrent(target, () => AddNote(target, block)));
                }

                break;
            case PageMessages.ActivateNote:
                if (FindTarget(e.Message) is { Note: { } note } noteTarget) {
                    BeginInvoke(WhileCurrent(noteTarget, () => EditNote(noteTarget, note)));
                }

                break;
            case PageMessages.ContextMenu:
                if (FindTarget(e.Message) is { } menuTarget) {
                    var rect = PageMessages.GetRect(e.Message, "rect");
                    var scale = PageMessages.GetDouble(e.Message, "scale") ?? 1;
                    BeginInvoke(WhileCurrent(menuTarget, () => ShowContextMenu(menuTarget, rect, scale)));
                }

                break;
            case PageMessages.ToggleTask:
                if (FindTarget(e.Message) is { Block: { } taskBlock, Note: null } taskTarget
                    && PageMessages.GetBool(e.Message, "checked") is { } isChecked) {
                    BeginInvoke(WhileCurrent(taskTarget, () => ToggleTask(taskTarget, taskBlock, isChecked)));
                }

                break;
            case PageMessages.OpenLink:
                var href = PageMessages.GetString(e.Message, "href") ?? "";
                BeginInvoke(() => OpenLink(href));
                break;
            case PageMessages.GoBack:
                BeginInvoke(() => RunCommand(HostCommand.Back, Keys.Back));
                break;
            case PageMessages.NoMoreNotes:
                _announcer.Announce(_("No more notes"));
                break;
            case PageMessages.DropFiles:
                var files = e.Files;
                BeginInvoke(() => OpenDroppedFiles(files));
                break;
            default:
                Log.Warning("Unknown page message {Type}", e.Type);
                break;
        }
    }

    private bool IsCurrentRender(JsonElement message) =>
        _render is not null && PageMessages.GetInt(message, "generation") == _generation;

    /// <summary>Remembers the block the page reports, so a re-render can return to it.</summary>
    private void UpdatePosition(JsonElement message) {
        if (_render is null) {
            return;
        }

        if (PageMessages.GetInt(message, "note") is { } note) {
            if (note >= 0 && note < _render.Notes.Count) {
                _position = _render.Notes[note].Block;
            }

            return;
        }

        if (PageMessages.GetString(message, "lines") is { } lines) {
            _position = _render.Blocks.FirstOrDefault(block => block.Lines == lines) ?? _position;
        }
    }

    /// <summary>
    /// The block or note a page message is about (<c>note</c> wins over <c>lines</c>), from the
    /// current render; <see langword="null"/> for an older render or an unknown element.
    /// </summary>
    private NoteTarget? FindTarget(JsonElement message) {
        if (_render is null || !IsCurrentRender(message)) {
            return null;
        }

        if (PageMessages.GetInt(message, "note") is { } index) {
            if (index < 0 || index >= _render.Notes.Count) {
                return null;
            }

            var note = _render.Notes[index];
            _position = note.Block ?? _position;

            return new NoteTarget(_generation, _renderedText, note.Block, note);
        }

        if (PageMessages.GetString(message, "lines") is { } lines
            && _render.Blocks.FirstOrDefault(block => block.Lines == lines) is { } found) {
            _position = found;

            return new NoteTarget(_generation, _renderedText, found, null);
        }

        return null;
    }

    /// <summary>
    /// Wraps a deferred action about <paramref name="target"/> so that it does nothing once the
    /// render the target came from has been replaced.
    /// </summary>
    private Action WhileCurrent(NoteTarget target, Action action) => () => {
        if (target.Generation == _generation) {
            action();
        }
    };

    /// <summary>Enter or a click on a block (or Add note in its menu): asks for a note and writes it after the block.</summary>
    private void AddNote(NoteTarget target, BlockInfo block) {
        if (_notes is not { } notes) {
            return;
        }

        if (notes.CannotWriteReason is { } reason) {
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

        if (notes.CannotWriteReason is { } reason) {
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
            using (var dialog = new NoteDialog(mode, excerpt, text, notes.DescribeTextError, DocumentNoteEnterAction)) {
                if (dialog.ShowDialog(this) != DialogResult.OK) {
                    FocusDocument();
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

        if (notes.CannotWriteReason is { } reason) {
            _announcer.Announce(reason);
            return;
        }

        if (ConfirmNoteDelete) {
            var answer = DialogHelper.Show(
                _("Delete this note?\n\n{0}", MarkdownRenderer.Excerpt(MarkdownRenderer.NotePlainText(note.Note.Text))),
                _("Delete note"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );

            if (answer != DialogResult.Yes) {
                FocusDocument();
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

            if (notes.CannotWriteReason is { } reason) {
                RevertTask(item, fileState);
                _announcer.Announce(reason);
                return;
            }

            if (ConfirmTaskToggle && !ConfirmToggle(item, isChecked)) {
                RevertTask(item, fileState);
                FocusDocument();
                return;
            }

            var result = notes.ToggleTask(target.RenderedText, item.StartLine, isChecked);

            if (!result.NeedsRender) {
                RevertTask(item, fileState);
            }

            ShowNoteResult(result);
        } finally {
            _togglingTask = false;
        }
    }

    /// <summary>Asks before a check box rewrites the file on disk.</summary>
    private static bool ConfirmToggle(BlockInfo item, bool isChecked) {
        var text = TaskToggle.WithoutMarker(item.Excerpt);
        var question = isChecked
            ? _("Mark this task as done? The file on disk will be changed.\n\n{0}", text)
            : _("Mark this task as not done? The file on disk will be changed.\n\n{0}", text);

        return DialogHelper.Show(
            question,
            isChecked ? _("Check task") : _("Uncheck task"),
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question
        ) == DialogResult.Yes;
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

        ShowNoteResult(redo ? notes.Redo() : notes.Undo());
    }

    /// <summary>Shows the outcome of a note action: re-renders when the file changed, and tells the user.</summary>
    private void ShowNoteResult(NoteActionResult result) {
        switch (result.Status) {
            case NoteActionStatus.Done:
                RenderDocument(focusNoteLine: result.FocusNoteLine, focusTaskLine: result.FocusTaskLine);
                FocusDocument();
                _announcer.Announce(result.Message);
                break;
            case NoteActionStatus.Stale:
                RenderDocument();
                FocusDocument();
                _announcer.Announce(result.Message);
                break;
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
    /// The context menu of a block or a note, at the element's position: Add note, then Edit and
    /// Delete note on a note, then Copy block text.
    /// </summary>
    private void ShowContextMenu(NoteTarget target, RectangleF? rect, double scale) {
        var spec = new NativeMenuSpec();

        if (target.Block is { } block) {
            spec.Add(_("&Add note"), WhileCurrent(target, () => AddNote(target, block)));
        }

        if (target.Note is { } note) {
            spec.Add(_("&Edit note"), WhileCurrent(target, () => EditNote(target, note)));
            spec.Add(_("&Delete note"), WhileCurrent(target, () => DeleteNote(target, note)));
        }

        if (target.Block is { } copied) {
            spec.Add(_("&Copy block text"), () => CopyBlockText(copied));
        }

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
            _announcer.Announce(_("Unable to copy to the clipboard."));
        }
    }

    /// <summary>The excerpt of the block a note is on, for the note dialog.</summary>
    private static string BlockExcerpt(RenderedNote note) =>
        note.Block?.Excerpt ?? _("The start of the document");

    private void FocusDocument() {
        if (documentView.IsInitialized) {
            documentView.FocusDocument();
        }
    }

    /// <summary>Follows a link from the document: see <see cref="LinkResolver"/>.</summary>
    private void OpenLink(string href) {
        var folder = _file is null ? null : Path.GetDirectoryName(_file.Path);
        var target = LinkResolver.Resolve(href, folder);
        Log.Information("Link {Href} resolved to {Kind} {Target}", href, target.Kind, target.Target);

        switch (target.Kind) {
            case LinkKind.External:
            case LinkKind.OtherFile:
                ShellOpen(target.Target);
                break;
            case LinkKind.Markdown:
                OpenFile(target.Target);
                break;
            case LinkKind.InPage:
                // The page scrolls to its own anchors.
                break;
            case LinkKind.Missing:
                _announcer.Announce(_("The link target does not exist: {0}", target.Target));
                break;
            case LinkKind.Unsupported:
                _announcer.Announce(_("This link cannot be opened: {0}", target.Target));
                break;
        }
    }

    private void ShellOpen(string target) {
        try {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        } catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException) {
            Log.Error(ex, "Unable to open {Target}", target);
            _announcer.Announce(_("Unable to open {0}", target));
        }
    }

    /// <summary>Opens the first Markdown file of a drop.</summary>
    private void OpenDroppedFiles(IEnumerable<string> files) {
        if (FirstMarkdownFile(files) is { } path) {
            OpenFile(path);
        } else {
            _announcer.Announce(_("Only Markdown files (.md, .markdown) can be opened."));
        }
    }

    private static string? FirstMarkdownFile(IEnumerable<string> files) =>
        files.FirstOrDefault(LinkResolver.IsMarkdownPath);

    private static string[] DroppedFiles(IDataObject? data) =>
        data?.GetData(DataFormats.FileDrop) as string[] ?? [];

    private void OnWindowDragEnter(object? sender, DragEventArgs e) =>
        e.Effect = FirstMarkdownFile(DroppedFiles(e.Data)) is null ? DragDropEffects.None : DragDropEffects.Copy;

    private void OnWindowDragDrop(object? sender, DragEventArgs e) {
        var files = DroppedFiles(e.Data);

        // Explorer waits until the drop returns: open the file afterwards.
        BeginInvoke(() => OpenDroppedFiles(files));
    }

    protected override void OnFormClosed(FormClosedEventArgs e) {
        documentView.MessageReceived -= OnPageMessage;
        documentView.AcceleratorKeyDown -= OnDocumentAcceleratorKeyDown;
        DragEnter -= OnWindowDragEnter;
        DragDrop -= OnWindowDragDrop;
        base.OnFormClosed(e);
    }
}

/// <summary>
/// The block or note a page action is about, with the render it came from: the text that render
/// was made from is what the note store checks the file against.
/// </summary>
/// <param name="Generation">The render's number.</param>
/// <param name="RenderedText">The file text the render was made from.</param>
/// <param name="Block">The block, or the block the note is on; <see langword="null"/> for a note at the top.</param>
/// <param name="Note">The note, when the action is about one.</param>
internal sealed record NoteTarget(int Generation, string RenderedText, BlockInfo? Block, RenderedNote? Note);

/// <summary>A note text kept after the file changed under it, for the next attempt on the same block or note.</summary>
/// <param name="Mode">Whether it was a new note or an edit.</param>
/// <param name="Key">The block's text (a new note) or the note's original text (an edit).</param>
/// <param name="Text">What the user typed.</param>
internal sealed record NoteDraft(NoteDialogMode Mode, string Key, string Text);
