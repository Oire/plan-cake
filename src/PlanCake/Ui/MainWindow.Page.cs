using Oire.PlanCake.Rendering;
using Oire.PlanCake.Utils;
using Serilog;
using static Oire.PlanCake.Utils.Localization;

namespace Oire.PlanCake.Ui;

internal sealed partial class MainWindow {
    /// <summary>How the open file is rendered for the window, in <paramref name="documentLanguage"/>.</summary>
    private RenderOptions RenderOptionsFor(string documentLanguage) => new(
        _markers,
        RenderMode.Interactive,
        LocalizedText.RenderStrings(),
        documentLanguage
    );

    /// <summary>
    /// Renders the open file and sends it to the page. Without an explicit
    /// <paramref name="focus"/>, the page goes to the note starting on
    /// <paramref name="focusNoteLine"/>, or to the check box of the task-list item starting on
    /// <paramref name="focusTaskLine"/>, if given and found, else returns to the block the user was on.
    /// A document just <paramref name="opened"/> without a saved position starts at its first block.
    /// </summary>
    /// <param name="restorePosition">
    /// False for a render the user did not ask for by acting on a block (a change outside
    /// PlanCake, Reload, a language switch): the page is updated in place and nothing is focused,
    /// so the JAWS virtual cursor stays where the user is reading. The last block the page
    /// reported may be far from there, since the page never sees the virtual cursor move; sending
    /// the focus to it (at first the opening block) threw the reader back
    /// (<c>docs/jaws-spike.md</c>, "An outside edit threw the reader to the top").
    /// </param>
    /// <param name="prepared">
    /// The render made in the background while the file opened; used when it was made from the
    /// file's text with the options this render would use, else the file is rendered again.
    /// </param>
    private void RenderDocument(
        PageFocus? focus = null,
        int? focusNoteLine = null,
        int? focusTaskLine = null,
        bool opened = false,
        bool restorePosition = true,
        PreparedRender? prepared = null
    ) {
        if (_file is null) {
            return;
        }

        var options = RenderOptionsFor(_documentLanguage);

        // Another file starts with nothing selected in the notes list.
        var listSelection = opened ? null : SelectedListNote();
        _renderedText = _file.Text;
        _watcher?.Acknowledge(_renderedText);
        _render = prepared is not null && ReferenceEquals(prepared.Text, _renderedText) && prepared.Options == options
            ? prepared.Result
            : MarkdownRenderer.Render(_renderedText, options);
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
            focus = restorePosition ? new PageFocus(Lines: target.Lines) : null;
            _position = target;
        } else if (focus is null) {
            _position = null;
        }

        _pendingFocus = focus;

        // A note the view goes to (one just added, edited or restored, in the document or from
        // the list) is selected in the list too; otherwise the list keeps its own selection.
        var listFocus = focus?.Note is { } focusedIndex ? _render.Notes[focusedIndex] : null;
        _currentNote = listFocus;
        FillNotesList(_render, listSelection, listFocus);
        PostRender();
    }

    private void PostRender() {
        if (!_pageReady || _render is null || _file is null) {
            return;
        }

        Log.Debug("Render {Generation} posted, focus {Focus}", _generation, _pendingFocus);
        documentView.PostMessage(new RenderMessage(
            _render.Html,
            _generation,
            _documentLanguage,
            _render.Title ?? Path.GetFileName(_file.Path),
            _pendingFocus
        ));
        _pendingFocus = null;
    }

    private void PostStrings() => documentView.PostMessage(PageStrings());

    /// <summary>
    /// The interface language and direction of the page chrome, and the page's own strings. The
    /// rendered blocks carry <c>dir="auto"</c> of their own, so a right-to-left interface does not
    /// turn a left-to-right plan around.
    /// </summary>
    internal static StringsMessage PageStrings() => new(
        Utils.Localization.GetCurrentCulture().Name,
        TextDirection.IsRightToLeft ? "rtl" : "ltr",
        _("No file is open."),
        [
            _("To open a Markdown file, press {0}.", ShortcutOf(HostCommand.Open)),
            _("To open one from a link, press {0}.", ShortcutOf(HostCommand.OpenFromLink)),
            _("You can also drag a Markdown file here."),
            _("For the user manual, press {0}.", ShortcutOf(HostCommand.UserManual)),
        ]
    );

    /// <summary>The key a command's menu item shows, for text that names it.</summary>
    private static string ShortcutOf(HostCommand command) => HostCommands.MenuShortcut(command) ?? String.Empty;

    /// <summary>
    /// F9 / Shift+F9: in the notes list, moves the selection; in the document, the page moves to
    /// the next or previous note from the current position.
    /// </summary>
    private void MoveToNote(bool forward) {
        if (IsNotesListFocused) {
            MoveListSelection(forward);
            return;
        }

        if (!_pageReady || _render is null) {
            _announcer.Announce(_("No more notes"));
            return;
        }

        documentView.PostMessage(forward ? new NextNoteMessage() : new PreviousNoteMessage());
    }

    /// <summary>
    /// Alt+Shift+Down / Alt+Shift+Up: the page moves the focus to the next or previous block (or note) from
    /// the current position, so a keyboard user who does not use a screen reader can reach any
    /// block and press Enter on it. From the notes list the move happens in the document, which
    /// gets the focus first: a page without the keyboard focus does not show its focused
    /// element, and the page marks the block it moves to only while it has the focus.
    /// </summary>
    private void MoveToBlock(bool forward) {
        if (!_pageReady || _render is null) {
            _announcer.Announce(_("No more blocks"));
            return;
        }

        FocusDocument();
        documentView.PostMessage(forward ? new NextBlockMessage() : new PreviousBlockMessage());
    }

    /// <summary>
    /// Every message the page sends, read by <see cref="PageMessageRouter"/>. This runs inside a
    /// WebView2 event: anything that may open a dialog or a menu is deferred with <c>BeginInvoke</c>.
    /// </summary>
    private void OnPageMessage(object? sender, PageMessageEventArgs e) {
        Log.Debug("Page message {Json}", e.Message.GetRawText());

        var state = new PageState(_render, _generation, _renderedText, Config.Notes.BlockEnterAction);
        var intent = PageMessageRouter.Route(e.Type, e.Message, e.Files, state);

        // The block or note a message is about becomes the current one, whatever comes of it.
        if (PageMessageRouter.TargetOf(intent) is { } target) {
            MakeCurrent(target);
        }

        switch (intent) {
            case PageIntent.Ready:
                Log.Information("Page ready");
                _pageReady = true;

                // The document first: a page that already shows it has no "No file is open" to
                // flash while the strings arrive.
                PostRender();
                PostStrings();
                documentView.FocusDocument();

                if (_announceWhenReady is { } announcement) {
                    _announceWhenReady = null;
                    _announcer.Announce(announcement);
                }

                break;
            case PageIntent.Position position:
                UpdatePosition(position);
                break;
            case PageIntent.AddNote add:
                BeginInvoke(WhileCurrent(add.Target, () => AddNote(add.Target, add.Block)));
                break;
            case PageIntent.EditNote edit:
                BeginInvoke(WhileCurrent(edit.Target, () => EditNote(edit.Target, edit.Note)));
                break;
            case PageIntent.ShowContextMenu menu:
                BeginInvoke(WhileCurrent(menu.Target, () => ShowContextMenu(menu.Target, menu.Rect, menu.Scale)));
                break;
            case PageIntent.ToggleTask toggle:
                BeginInvoke(() => {
                    if (toggle.Target.Generation == _generation) {
                        ToggleTask(toggle.Target, toggle.Item, toggle.Checked);
                    } else {
                        TaskToggleDropped();
                    }
                });
                break;
            case PageIntent.TaskToggleDropped:
                TaskToggleDropped();
                break;
            case PageIntent.OpenLink link:
                BeginInvoke(() => OpenLink(link.Href));
                break;
            case PageIntent.GoBack:
                BeginInvoke(() => RunCommand(HostCommand.Back, Keys.Back));
                break;
            case PageIntent.NoMoreNotes:
                _announcer.Announce(_("No more notes"));
                break;
            case PageIntent.NoMoreBlocks:
                _announcer.Announce(_("No more blocks"));
                break;
            case PageIntent.DropFiles drop:
                BeginInvoke(() => OpenDroppedFiles(drop.Files));
                break;
            case PageIntent.Unknown unknown:
                Log.Warning("Unknown page message {Type}", unknown.Type);
                break;
            case PageIntent.Ignored:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(e), intent, null);
        }
    }

    /// <summary>
    /// Makes the block or note a page message is about the current one: where a re-render
    /// returns to, and (a note) what Notes → Edit note acts on and the list selects.
    /// </summary>
    private void MakeCurrent(NoteTarget target) {
        if (target.Note is { } note) {
            _position = note.Block ?? _position;
            _currentNote = note;
            SelectNoteInList(note);
        } else {
            _position = target.Block;
            _currentNote = null;
        }
    }

    /// <summary>Remembers the block or note the page reports, so a re-render can return to it.</summary>
    private void UpdatePosition(PageIntent.Position position) {
        if (position.Note is { } note) {
            _position = note.Block;
            _currentNote = note;
            SelectNoteInList(note);
        } else {
            _position = position.Block ?? _position;
            _currentNote = null;
        }
    }

    /// <inheritdoc cref="PageMessageRouter.WhileCurrent"/>
    private Action WhileCurrent(NoteTarget target, Action action) =>
        PageMessageRouter.WhileCurrent(target, () => _generation, action);
}
