using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using GetText.WindowsForms;
using Oire.PlanCake.Notes;
using Oire.PlanCake.Rendering;
using Oire.PlanCake.Services;
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

    /// <summary>The note markers from Settings, <c>[usernote]</c> … <c>[/usernote]</c> by default.</summary>
    private NoteMarkers _markers = Config.Notes.ToMarkers();

    /// <summary>Downloads the files File → Open from link and a link on the clipboard open.</summary>
    private static readonly MarkdownDownloader _downloader = new();

    private readonly StatusAnnouncer _announcer;
    private readonly string? _initialFile;

    /// <summary>The designer's texts, kept so a language switch translates from English again.</summary>
    private readonly ObjectPropertiesStore _localizationStore = new();

    /// <summary>The menu items whose enabled state depends on the window's state, and when they are enabled.</summary>
    private readonly List<(NativeMenuItemSpec Item, Func<bool> IsEnabled)> _menuEnabledWhen = [];

    /// <summary>The menu items with a check mark, and when they are checked.</summary>
    private readonly List<(NativeMenuItemSpec Item, Func<bool> IsChecked)> _menuCheckedWhen = [];

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

    /// <summary>
    /// The note the document is on (the page reported it, or the view went to it), from
    /// <see cref="_render"/>; <see langword="null"/> when the document is on a block. Notes → Edit
    /// note and Delete note act on it while the document has the focus.
    /// </summary>
    private RenderedNote? _currentNote;

    /// <summary>
    /// The <c>lang</c> of the open document (View → Document language); another file starts with
    /// the default from Settings again. English by default, and it never follows the interface language.
    /// </summary>
    private string _documentLanguage = Config.General.DefaultDocumentLanguage;

    /// <summary>Where the page puts the virtual cursor when it shows <see cref="_render"/>.</summary>
    private PageFocus? _pendingFocus;

    /// <summary>True once the page has loaded and can take messages.</summary>
    private bool _pageReady;

    /// <summary>
    /// True while a task toggle is asking or writing: a second toggle (a double-click) is ignored,
    /// and the render that follows the first one resets the check box anyway.
    /// </summary>
    private bool _togglingTask;

    /// <summary>Whether the notes list beside the document is shown (View → Notes list).</summary>
    private bool _showNotesList = Config.General.ShowNotesList;

    /// <summary>The window's menu bar, attached once the window has a handle.</summary>
    private NativeMenuBar? _menuBar;

    /// <summary>The context menu of the notes list: Edit note, Delete note.</summary>
    private NativeContextMenu? _notesListMenu;

    /// <summary>The list window and the name last given to it for MSAA.</summary>
    private (IntPtr Handle, string Name)? _notesListName;

    /// <summary>Watches <see cref="_file"/> for changes made outside PlanCake.</summary>
    private FileWatcher? _watcher;

    /// <summary>This window's claim on <see cref="_file"/>: a second attempt to open it activates this window.</summary>
    private SingleInstance? _instance;

    /// <summary>
    /// True while <see cref="_file"/> is gone from disk (deleted, or renamed or moved away): the
    /// view stays, but nothing can be written to the file and Reload is off until it is back.
    /// </summary>
    private bool _fileMissing;

    /// <summary>True while the window asks whether to reload a file changed outside PlanCake.</summary>
    private bool _askingReload;

    /// <summary>Cancels the download in progress, if any; one at a time.</summary>
    private CancellationTokenSource? _download;

    /// <summary>
    /// A message to announce once the page has loaded: said while a new page loads, it would be
    /// cut off by the screen reader starting on the new document.
    /// </summary>
    private string? _announceWhenReady;

    /// <summary>
    /// The update checks, created once the window is shown (NetSparkle's windows need the UI
    /// thread); <see langword="null"/> before that, and when they could not be set up.
    /// </summary>
    private UpdateService? _updateService;

    /// <summary>True once <see cref="_updateService"/> has been set up (or found impossible).</summary>
    private bool _updatesInitialized;

    /// <summary>True while Help → Check for updates is checking: a second request is ignored.</summary>
    private bool _checkingUpdates;

    public MainWindow() : this(null) { }

    /// <param name="initialFile">The file to open once the window is up, from the command line.</param>
    internal MainWindow(string? initialFile) {
        InitializeComponent();

        // Walks the control tree and translates every text property through the gettext
        // catalog. Designer-set strings are therefore written in English and translated here;
        // strings built at run time go through _() instead.
        Localizer.Localize(this, Utils.Localization.Catalog, _localizationStore);
        TextDirection.Apply(this);

        _initialFile = initialFile;
        _announcer = new StatusAnnouncer(statusStrip, statusLabel);
        documentView.MessageReceived += OnPageMessage;
        documentView.AcceleratorKeyDown += OnDocumentAcceleratorKeyDown;
        SetUpNotesList();

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

    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);

        // The menu needs the window's handle; a recreated handle (a right-to-left switch) gets
        // the menu attached again.
        _menuBar ??= new NativeMenuBar(this);
        _menuBar.Attach(BuildMenuSpec());
    }

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
            EnabledWhen(edit.AddItem(_("&Delete all notes..."), null, DeleteAllNotes), () => FileIsThere() && HasNotes());
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

    /// <summary>True when a file is open and still on disk, so it can be written, reloaded and opened elsewhere.</summary>
    private bool FileIsThere() => _file is not null && !_fileMissing;

    private bool HasNotes() => _render is { Notes.Count: > 0 };

    /// <summary>
    /// The native menu reads each item's state from its spec when a menu opens; <c>WM_INITMENU</c>
    /// comes once before any of them, when the menu bar is entered by keyboard or mouse.
    /// </summary>
    protected override void WndProc(ref Message m) {
        const int WM_INITMENU = 0x0116;

        if (m.Msg == WM_INITMENU) {
            RefreshMenuState();
        }

        base.WndProc(ref m);
    }

    /// <summary>
    /// Sets up the update checks once the window is visible: the silent startup check and the
    /// background checks, each as Settings says, in the first PlanCake window only
    /// (<see cref="UpdateService.DoesBackgroundChecks"/>); another window takes the background
    /// checks over when that one closes.
    /// </summary>
    protected override void OnShown(EventArgs e) {
        base.OnShown(e);
        InitializeUpdates();
    }

    private void InitializeUpdates() {
        if (_updatesInitialized || StartupFailed || IsDisposed) {
            return;
        }

        _updatesInitialized = true;
        _updateService = UpdateService.Create(CurrentUpdateCheckInterval);

        // A window that does not do the background checks may take them over later, reading the
        // interval again then (CurrentUpdateCheckInterval).
        if (_updateService is not { DoesBackgroundChecks: true } updates) {
            return;
        }

        updates.ConfigurePeriodicChecks(Config.General.UpdateCheckInterval);

        if (Config.General.CheckForUpdatesOnStartup) {
            // Fire and forget: the check never throws, says nothing unless there is an update,
            // and never takes the focus otherwise. A named local, since _ is the gettext method here.
            var startupCheck = updates.CheckForUpdatesAsync();
            GC.KeepAlive(startupCheck);
        }
    }

    /// <summary>
    /// The update check interval for this window taking the background checks over from one that
    /// closed: the settings file is read again first, as on activation, since another window may
    /// have changed it; a file that cannot be read keeps the settings in memory.
    /// </summary>
    /// <returns>
    /// The interval, or <see langword="null"/> while a dialog of this window is open: the action
    /// that opened it must finish with the settings the user saw (see <see cref="OnActivated"/>).
    /// </returns>
    private UpdateCheckInterval? CurrentUpdateCheckInterval() {
        if (StartupFailed || IsDisposed || !IsHandleCreated || !NativeMethods.IsWindowEnabled(Handle)) {
            return null;
        }

        ReloadSettingsIfChanged();

        return Config.General.UpdateCheckInterval;
    }

    /// <summary>
    /// Help → Check for updates: an available update shows NetSparkle's window; every other
    /// outcome (up to date, skipped, no network, checks that could not be set up) is said in a
    /// message box.
    /// </summary>
    private async void CheckForUpdates() {
        if (_checkingUpdates) {
            return;
        }

        _checkingUpdates = true;

        try {
            var outcome = _updateService is { } updates
                ? await updates.CheckForUpdatesAsync()
                : UpdateCheckOutcome.Unavailable;

            if (IsDisposed) {
                return;
            }

            if (UpdateService.Describe(outcome) is { } message) {
                var icon = outcome is UpdateCheckOutcome.UpToDate or UpdateCheckOutcome.Skipped
                    ? MessageBoxIcon.Information
                    : MessageBoxIcon.Warning;
                DialogHelper.Show(message, _("Check for updates"), MessageBoxButtons.OK, icon);
                ReturnFocus();
            }
        } finally {
            _checkingUpdates = false;
        }
    }

    protected override async void OnLoad(EventArgs e) {
        base.OnLoad(e);
        NameNotesList();

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

        // Another window took the file between Program's check and this one: that window is in
        // front now, and this one, which has nothing to show, goes.
        if (!String.IsNullOrWhiteSpace(_initialFile)
            && LoadFileAs(_initialFile, position: null, recordHistory: true) == OpenOutcome.OpenElsewhere) {
            Close();
        }
    }

    /// <summary>How an attempt to open a file ended.</summary>
    private enum OpenOutcome {
        Opened,
        Failed,

        /// <summary>Another window shows the file and was brought to the front; this one kept its file.</summary>
        OpenElsewhere,
    }

    /// <summary>
    /// Opens a Markdown file in the window. Every way of opening one (the command line, drag and
    /// drop, a link in the document, File → Open, the clipboard and a web link) goes
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
    private bool LoadFile(string path, BlockInfo? position, bool recordHistory) =>
        LoadFileAs(path, position, recordHistory) == OpenOutcome.Opened;

    /// <inheritdoc cref="LoadFile"/>
    private OpenOutcome LoadFileAs(string path, BlockInfo? position, bool recordHistory) {
        string fullPath;

        try {
            fullPath = Path.GetFullPath(path);
        } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) {
            Log.Error(ex, "Unable to open {Path}", path);
            ShowError(_("Unable to open {0}: {1}", path, ex.Message));

            return OpenOutcome.Failed;
        }

        // Opening the same file again keeps the reading position; another file starts at the top.
        var reopened = _file is not null && String.Equals(_file.Path, fullPath, StringComparison.OrdinalIgnoreCase);

        // One window per file, claimed before the file is opened: two windows that open the same
        // file at once cannot both claim it, so only one of them ever writes it. A file another
        // window shows brings that window to the front, and this one stays as it is (its history too).
        SingleInstance? claim = null;

        if (!reopened) {
            switch (SingleInstance.TryClaim(fullPath, OnActivationRequested, out claim)) {
                case ClaimOutcome.ActivatedOther:
                    Log.Information("{Path} is open in another window, which was activated", fullPath);

                    return OpenOutcome.OpenElsewhere;
                case ClaimOutcome.Unavailable:
                    ShowError(_("The file {0} is open in another PlanCake window, which does not respond.", path));

                    return OpenOutcome.Failed;
            }
        }

        MarkdownFile file;

        // The document language helps recognize the encoding of a file that is not UTF-8.
        var documentLanguage = reopened ? _documentLanguage : Config.General.DefaultDocumentLanguage;

        try {
            file = MarkdownFile.Open(fullPath, new MarkdownFileOptions(
                ConvertToUtf8: Config.Advanced.ConvertToUtf8,
                DocumentLanguage: documentLanguage
            ));
        } catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) {
            claim?.Dispose();
            Log.Warning(ex, "Unable to open {Path}: not found", path);
            ShowError(_("The file {0} does not exist.", path));

            return OpenOutcome.Failed;
        } catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException) {
            claim?.Dispose();
            Log.Error(ex, "Unable to open {Path}", path);
            ShowError(_("Unable to open {0}: {1}", path, ex.Message));

            return OpenOutcome.Failed;
        }

        if (!reopened) {
            if (recordHistory && _file is not null) {
                _history.Push(new HistoryEntry(_file.Path, _position));
            }

            _position = position;
            _documentLanguage = documentLanguage;
        }

        _file = file;
        _notes = new NoteActionRunner(new NoteStore(file, _markers));
        _draft = null;
        _fileMissing = false;

        if (!reopened || _watcher is null || _watcher.IsMissing) {
            WatchFile(file.Path);
        }

        // The claim on the new file replaces the one on the file being left.
        if (claim is not null) {
            _instance?.Dispose();
            _instance = claim;
        }

        // Another file gets a fresh page, so a screen reader starts it as a new document, at the
        // top (or at the block Back returns to); the render waits for the new page's "ready".
        // Replacing the content in place left JAWS at its old offset in the virtual buffer,
        // which in a shorter file is the end (Task 8 JAWS check). The same file again, and every
        // re-render after a note action, stays in place so the reading position survives.
        if (!reopened && _pageReady) {
            _pageReady = false;
            documentView.Navigate(PageFile);
        }

        RenderDocument(opened: !reopened, restorePosition: !reopened);
        UpdateTitle();

        Log.Information(
            "Opened {Path}: encoding={Encoding} bom={Bom} readOnly={ReadOnly} unrecognized={Unrecognized} convertedFrom={ConvertedFrom} notes={Notes}",
            file.Path, file.Encoding.WebName, file.HasBom, file.IsReadOnly, file.IsUnrecognized, file.ConvertedFrom?.WebName,
            _render?.Notes.Count
        );

        AnnounceFileState(file);

        if (documentView.IsInitialized) {
            documentView.FocusDocument();
        }

        return OpenOutcome.Opened;
    }

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
    /// the focus to it (at first the opening block) threw the reader back (Task 10 JAWS check).
    /// </param>
    private void RenderDocument(
        PageFocus? focus = null,
        int? focusNoteLine = null,
        int? focusTaskLine = null,
        bool opened = false,
        bool restorePosition = true
    ) {
        if (_file is null) {
            return;
        }

        var options = new RenderOptions(
            _markers,
            RenderMode.Interactive,
            LocalizedText.RenderStrings(),
            _documentLanguage
        );

        // Another file starts with nothing selected in the notes list.
        var listSelection = opened ? null : SelectedListNote();
        _renderedText = _file.Text;
        _watcher?.Acknowledge(_renderedText);
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
        _("No file is open.")
    );

    /// <summary>Tells the user what is special about the file just opened, if anything.</summary>
    private void AnnounceFileState(MarkdownFile file) {
        var messages = new List<string>();

        if (file.ConvertedFrom is { } convertedFrom) {
            messages.Add(_("Converted from {0} to UTF-8.", LegacyEncoding.DisplayName(convertedFrom)));
        } else if (file.IsUnrecognized && file.InvalidByteLine is { } invalidLine) {
            messages.Add(_(
                "This file is in {0} but has an invalid byte on line {1}, so it was opened read-only and is never changed. Notes cannot be added to it.",
                LegacyEncoding.DisplayName(file.Encoding), invalidLine
            ));
        } else if (file.ConversionFailed) {
            messages.Add(_(
                "This file is not in UTF-8 and could not be converted, so it was opened read-only as {0}. Notes cannot be added to it.",
                LegacyEncoding.DisplayName(file.Encoding)
            ));
        } else if (file.IsUnrecognized) {
            messages.Add(_(
                "The encoding of this file could not be recognized, so it was opened read-only and is never changed. Notes cannot be added to it."
            ));
        } else if (file.IsReadOnly) {
            messages.Add(_(
                "This file is not in UTF-8, so it was opened read-only as {0}. Notes cannot be added to it.",
                LegacyEncoding.DisplayName(file.Encoding)
            ));
        }

        if (_render?.Parse.HasUnterminated == true) {
            messages.Add(_("A note has no closing marker, so it runs to the end of the file."));
        }

        if (messages.Count > 0) {
            _announcer.Announce(String.Join(" ", messages));
        }
    }

    private static void ShowError(string message) =>
        DialogHelper.Show(message, _("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);

    /// <summary>
    /// Host shortcuts pressed anywhere but in the document. Keys pressed in the document never
    /// get here (see <see cref="OnDocumentAcceleratorKeyDown"/>); both paths end in
    /// <see cref="TryRunShortcut"/>, so the one table in <see cref="HostCommands"/> decides.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) =>
        TryLeaveNotesListByTab(keyData) || TryRunShortcut(keyData) || base.ProcessCmdKey(ref msg, keyData);

    /// <summary>
    /// Tab and Shift+Tab in the notes list go to the document: Tab to its first focusable
    /// element, Shift+Tab to its last. The list passes Tab on only among the controls of its own
    /// panel, where it is the only stop, so without this the focus never left it. The other half
    /// of the cycle is the WebView2's own: Tab from the page's last element and Shift+Tab from its
    /// first go to the next tab stop of the window, which is the list while it is shown, and back
    /// into the page while it is hidden.
    /// </summary>
    private bool TryLeaveNotesListByTab(Keys keyData) {
        if (keyData is not (Keys.Tab or (Keys.Shift | Keys.Tab)) || !IsNotesListFocused || !documentView.IsInitialized) {
            return false;
        }

        documentView.EnterByTab(forward: keyData == Keys.Tab);
        return true;
    }

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
            case HostCommand.Open:
                ShowOpenDialog();
                break;
            case HostCommand.OpenFromClipboard:
                OpenFromClipboard();
                break;
            case HostCommand.OpenFromLink:
                ShowOpenLinkDialog();
                break;
            case HostCommand.OpenInEditor:
                OpenInEditor();
                break;
            case HostCommand.Settings:
                ShowSettings();
                break;
            case HostCommand.Reload:
                ReloadFile();
                break;
            case HostCommand.UserManual:
                ShowUserManual();
                break;
            case HostCommand.About:
                ShowAbout();
                break;
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
            case HostCommand.SwitchPane:
                SwitchPane();
                break;
            case HostCommand.NextNote:
                MoveToNote(forward: true);
                break;
            case HostCommand.PreviousNote:
                MoveToNote(forward: false);
                break;
            case HostCommand.NextBlock:
                MoveToBlock(forward: true);
                break;
            case HostCommand.PreviousBlock:
                MoveToBlock(forward: false);
                break;
            case HostCommand.Undo:
                UndoOrRedo(redo: false);
                break;
            case HostCommand.Redo:
                UndoOrRedo(redo: true);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command, null);
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
    /// Every message the page sends. This runs inside a WebView2 event: anything that may open a
    /// dialog or a menu is deferred with <c>BeginInvoke</c>.
    /// </summary>
    private void OnPageMessage(object? sender, PageMessageEventArgs e) {
        Log.Debug("Page message {Json}", e.Message.GetRawText());

        switch (e.Type) {
            case PageMessages.Ready:
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
            case PageMessages.Position:
                if (IsCurrentRender(e.Message)) {
                    UpdatePosition(e.Message);
                }

                break;
            case PageMessages.Activate:
                if (FindTarget(e.Message) is { Block: { } block } target) {
                    if (Config.Notes.BlockEnterAction == BlockEnterAction.ContextMenu) {
                        // The setting makes Enter (and a click) on a block open its menu instead.
                        var blockRect = PageMessages.GetRect(e.Message, "rect");
                        var blockScale = PageMessages.GetDouble(e.Message, "scale") ?? 1;
                        BeginInvoke(WhileCurrent(target, () => ShowContextMenu(target, blockRect, blockScale)));
                    } else {
                        BeginInvoke(WhileCurrent(target, () => AddNote(target, block)));
                    }
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
                if (PageMessages.GetBool(e.Message, "checked") is not { } isChecked) {
                    break;
                }

                if (FindTarget(e.Message) is { Block: { } taskBlock, Note: null } taskTarget) {
                    BeginInvoke(() => {
                        if (taskTarget.Generation == _generation) {
                            ToggleTask(taskTarget, taskBlock, isChecked);
                        } else {
                            TaskToggleDropped();
                        }
                    });
                } else if (_render is not null && !IsCurrentRender(e.Message)) {
                    TaskToggleDropped();
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
            case PageMessages.NoMoreBlocks:
                _announcer.Announce(_("No more blocks"));
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

        if (PageMessages.GetInt(message, "note") is not null) {
            if (PageMessages.FindNote(message, _render.Notes) is { } note) {
                _position = note.Block;
                _currentNote = note;
                SelectNoteInList(note);
            }

            return;
        }

        if (PageMessages.GetString(message, "lines") is { } lines) {
            _position = _render.Blocks.FirstOrDefault(block => block.Lines == lines) ?? _position;
            _currentNote = null;
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

        if (PageMessages.GetInt(message, "note") is not null) {
            if (PageMessages.FindNote(message, _render.Notes) is not { } note) {
                return null;
            }

            _position = note.Block ?? _position;
            _currentNote = note;
            SelectNoteInList(note);

            return new NoteTarget(_generation, _renderedText, note.Block, note);
        }

        if (PageMessages.GetString(message, "lines") is { } lines
            && _render.Blocks.FirstOrDefault(block => block.Lines == lines) is { } found) {
            _position = found;
            _currentNote = null;

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

        if ((CannotChangeReason(notes) ?? notes.UnterminatedReason(target.RenderedText, block)) is { } reason) {
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
            var confirmed = DialogHelper.Confirm(
                _("Delete this note?\n\n{0}", MarkdownRenderer.Excerpt(MarkdownRenderer.NotePlainText(note.Note.Text))),
                _("Delete note")
            );

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
        var text = TaskToggle.WithoutMarker(item.Excerpt);
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
        switch (result.Status) {
            case NoteActionStatus.Done:
                RenderDocument(focusNoteLine: result.FocusNoteLine, focusTaskLine: result.FocusTaskLine);
                ReturnFocus();
                _announcer.Announce(result.Message);
                break;
            case NoteActionStatus.Stale:
                RenderDocument();
                ReturnFocus();
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
    /// The items of the context menu of a block or a note: Add note, Edit note, Delete note and
    /// Copy block text, each only when its action is given. Internal for the tests, which check
    /// the mnemonics of the full menu in every catalog.
    /// </summary>
    internal static NativeMenuSpec BlockMenuSpec(Action? addNote, Action? editNote, Action? deleteNote, Action? copyText) {
        var spec = new NativeMenuSpec();

        if (addNote is not null) {
            spec.Add(_("&Add note"), addNote);
        }

        if (editNote is not null) {
            spec.Add(_("&Edit note"), editNote);
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

    /// <summary>
    /// After a note action: back to the document, unless the user is working in the notes list,
    /// where the focus stays (a closed dialog has already given it back to the list).
    /// </summary>
    private void ReturnFocus() {
        if (!IsNotesListFocused) {
            FocusDocument();
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
            case LinkKind.Program:
                OfferToShowProgram(target.Target);
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

    /// <summary>
    /// A link to a program, a script or any other file that is not a passive document: it is
    /// never opened from a link, whatever the link text says, since its default action might run
    /// something. Yes shows it selected in File Explorer, where the user can decide.
    /// </summary>
    private void OfferToShowProgram(string path) {
        var confirmed = DialogHelper.Confirm(
            _("PlanCake does not open this file from a link, since opening it could run a program:\n\n{0}\n\nShow it in File Explorer?", path),
            _("Open link"),
            MessageBoxIcon.Warning
        );

        if (confirmed) {
            StartProcess(new ProcessStartInfo(ExplorerPath) {
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = false,
            }, path);
        }

        ReturnFocus();
    }

    private static string ExplorerPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    /// <summary>
    /// Opens <paramref name="target"/> with the system's default verb: a web link, a folder, a
    /// passive document (<see cref="LinkResolver.IsPassiveDocument"/>). Never for a program, a
    /// script or any other local file from a link, which the default verb might run.
    /// </summary>
    private void ShellOpen(string target) =>
        StartProcess(new ProcessStartInfo(target) { UseShellExecute = true }, target);

    private void StartProcess(ProcessStartInfo startInfo, string target) {
        try {
            Process.Start(startInfo)?.Dispose();
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

    // --- Menu commands -----------------------------------------------------------------

    /// <summary>The window title: the open file's name, then the program's.</summary>
    private void UpdateTitle() =>
        Text = _file is null ? App.Name : _("{0} - {1}", Path.GetFileName(_file.Path), App.Name);

    /// <summary>File → Open: Markdown files first, then any file.</summary>
    private void ShowOpenDialog() {
        using var dialog = new OpenFileDialog {
            Title = _("Open"),
            Filter = $"{_("Markdown files")} (*.md;*.markdown)|*.md;*.markdown|{_("All files")} (*.*)|*.*",
            CheckFileExists = true,
            RestoreDirectory = true,
        };

        if (_file is not null && Path.GetDirectoryName(_file.Path) is { } folder) {
            dialog.InitialDirectory = folder;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK) {
            OpenFile(dialog.FileName);
        } else {
            ReturnFocus();
        }
    }

    /// <summary>
    /// File → Open from clipboard: files copied in Explorer, a path or a link copied as text (see
    /// <see cref="ClipboardClassifier"/>).
    /// </summary>
    private void OpenFromClipboard() {
        ClipboardContent content;

        try {
            content = ClipboardClassifier.Classify(ClipboardDropList(), Clipboard.ContainsText() ? Clipboard.GetText() : null);
        } catch (ExternalException ex) {
            Log.Warning(ex, "Unable to read the clipboard");
            _announcer.Announce(_("Unable to read the clipboard."));

            return;
        }

        Log.Information("Clipboard holds {Kind} {Target}", content.Kind, content.Target);

        switch (content.Kind) {
            case ClipboardContentKind.MarkdownFile:
                OpenFile(content.Target);
                break;
            case ClipboardContentKind.Link:
                DownloadAndOpen(content.Target);
                break;
            case ClipboardContentKind.NoMarkdownFile:
                _announcer.Announce(_("None of the copied files is a Markdown file (.md, .markdown)."));
                break;
            default:
                _announcer.Announce(_("The clipboard holds no Markdown file or link."));
                break;
        }
    }

    private static List<string>? ClipboardDropList() {
        if (!Clipboard.ContainsFileDropList()) {
            return null;
        }

        return Clipboard.GetFileDropList().Cast<string?>().OfType<string>().ToList();
    }

    /// <summary>File → Open from link: asks for a link, starting with the one on the clipboard, if any.</summary>
    private void ShowOpenLinkDialog() {
        if (_download is not null) {
            _announcer.Announce(_("A download is already in progress."));

            return;
        }

        string? initialUrl = null;

        try {
            if (Clipboard.ContainsText() && UrlHelper.IsValidHttpUrl(Clipboard.GetText(), out var url)) {
                initialUrl = url;
            }
        } catch (ExternalException ex) {
            Log.Debug(ex, "Unable to read the clipboard for a link");
        }

        using var dialog = new OpenLinkDialog(initialUrl);

        if (dialog.ShowDialog(this) == DialogResult.OK) {
            DownloadAndOpen(dialog.Url);
        } else {
            ReturnFocus();
        }
    }

    /// <summary>
    /// Downloads a Markdown file into the Downloads folder (see <see cref="MarkdownDownloader"/>)
    /// and opens that local copy, which is where notes then go. Failures are announced.
    /// </summary>
    private async void DownloadAndOpen(string url) {
        if (_download is not null) {
            _announcer.Announce(_("A download is already in progress."));

            return;
        }

        using var cancellation = new CancellationTokenSource();
        _download = cancellation;

        // The host only: a whole link is long to listen to, and a file name guessed before the
        // content is accepted misleads when the link turns out to be a web page (Task 11 JAWS check).
        // The name comes with "Downloaded and saved to".
        var source = UrlHelper.IsValidHttpUrl(url, out var link) ? new Uri(link).Host : url;
        _announcer.Announce(_("Downloading from {0}...", source));
        DownloadResult result;

        try {
            result = await _downloader.DownloadAsync(url, cancellation.Token);
        } catch (OperationCanceledException) {
            // The window closed.
            return;
        } catch (Exception ex) {
            // Nothing may leave an async void: it would end up in the unhandled-exception handler.
            Log.Error(ex, "Download of {Url} failed", url);

            if (!IsDisposed && !Disposing) {
                _announcer.Announce(_("The download failed: {0}", ex.Message));
            }

            return;
        } finally {
            _download = null;
        }

        if (IsDisposed || Disposing) {
            return;
        }

        if (result.FilePath is not { } path) {
            _announcer.Announce(result.Error);

            return;
        }

        var saved = _("Downloaded and saved to {0}", path);

        // Opening another file loads a new page, and the page's "ready" says the message; the
        // same file again (or a failure) leaves the page as it is, so it is said now.
        _announceWhenReady = null;
        var opened = OpenFile(path);

        if (opened && !_pageReady) {
            _announceWhenReady = saved;
        } else {
            _announcer.Announce(saved);
        }
    }

    /// <summary>
    /// File → Open in editor: a Markdown file in the program Windows opens Markdown files with;
    /// any other file (opened through All files) with its "edit" verb, else in Notepad, never
    /// with its default verb, which for a script or a program runs it.
    /// </summary>
    private void OpenInEditor() {
        if (_file is null) {
            _announcer.Announce(_("No file is open."));
            return;
        }

        if (_fileMissing) {
            _announcer.Announce(MissingFileMessage());
            return;
        }

        var path = _file.Path;

        if (LinkResolver.IsMarkdownPath(path)) {
            ShellOpen(path);
            return;
        }

        if (!LinkResolver.IsRunnable(path)) {
            try {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "edit" })?.Dispose();
                return;
            } catch (Win32Exception ex) {
                Log.Information(ex, "{Path} has no edit verb; opening it in Notepad", path);
            }
        }

        var notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
        StartProcess(new ProcessStartInfo(notepad) { ArgumentList = { path }, UseShellExecute = false }, path);
    }

    /// <summary>
    /// File → Export notes: the notes as JSON with their references, exactly what
    /// <c>plancake list --json</c> prints (<see cref="NotesJson"/>). A copy for other tools;
    /// PlanCake never reads it back.
    /// </summary>
    private void ExportNotes() {
        if (_file is null || _render is null) {
            _announcer.Announce(_("No file is open."));
            return;
        }

        using var dialog = new SaveFileDialog {
            Title = _("Export notes"),
            Filter = $"{_("JSON files")} (*.json)|*.json|{_("All files")} (*.*)|*.*",
            FileName = $"{Path.GetFileNameWithoutExtension(_file.Path)}.notes.json",
            OverwritePrompt = true,
            RestoreDirectory = true,
        };

        if (Path.GetDirectoryName(_file.Path) is { } folder) {
            dialog.InitialDirectory = folder;
        }

        if (dialog.ShowDialog(this) != DialogResult.OK) {
            ReturnFocus();
            return;
        }

        var count = _render.Notes.Count;

        try {
            File.WriteAllBytes(dialog.FileName, NotesJson.SerializeToUtf8(_render.Notes));
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                         or NotSupportedException) {
            Log.Error(ex, "Unable to export the notes to {Path}", dialog.FileName);
            ShowError(_("Unable to write {0}: {1}", dialog.FileName, ex.Message));
            ReturnFocus();
            return;
        }

        Log.Information("Exported {Count} notes of {Path} to {Output}", count, _file.Path, dialog.FileName);
        ReturnFocus();
        _announcer.Announce(_n(
            "Exported {0} note to {1}.", "Exported {0} notes to {1}.", count, count, Path.GetFileName(dialog.FileName)
        ));
    }

    /// <summary>
    /// View → Reload: reads the file again and shows it at the same place. Undo and redo go on
    /// working: each checks the file still holds the text it expects before it writes.
    /// </summary>
    private void ReloadFile() {
        if (_file is null) {
            _announcer.Announce(_("No file is open."));
            return;
        }

        if (_fileMissing) {
            _announcer.Announce(MissingFileMessage());
            return;
        }

        if (Reload(fromOutside: false)) {
            ReturnFocus();
            _announcer.Announce(_("File reloaded"));
        }
    }

    /// <summary>
    /// Reads the open file again and renders it at the block the user was on. A reload the user
    /// asked for reports a failure in a message box; one caused by a change outside PlanCake only
    /// announces it, as the user may be busy elsewhere.
    /// </summary>
    /// <returns>True when the file was read and rendered.</returns>
    private bool Reload(bool fromOutside) {
        if (_file is not { } file) {
            return false;
        }

        try {
            file.Reload();
        } catch (IOException ex) {
            Log.Error(ex, "Unable to reload {Path}", file.Path);

            if (fromOutside) {
                _announcer.Announce(_("Unable to reload {0}: {1}", Path.GetFileName(file.Path), ex.Message));
            } else {
                ShowError(_("Unable to reload {0}: {1}", file.Path, ex.Message));
            }

            return false;
        }

        RenderDocument(restorePosition: false);

        return true;
    }

    // --- Following the file on disk ---------------------------------------------------

    /// <summary>Starts watching <paramref name="path"/> for changes made outside PlanCake, instead of the previous file.</summary>
    private void WatchFile(string path) {
        if (_watcher is { } previous) {
            previous.FileChanged -= OnWatchedFileChanged;
            previous.Dispose();
        }

        // Decoded as the window decodes it: a legacy file depends on the document language.
        _watcher = new FileWatcher(
            path,
            new UiDebounceTimer(),
            file => FileWatcher.ReadText(file, new MarkdownFileOptions(DocumentLanguage: _documentLanguage))
        );
        _watcher.FileChanged += OnWatchedFileChanged;
        _watcher.Start(this);
    }

    /// <summary>Another attempt to open this window's file; runs on the pipe's thread.</summary>
    private void OnActivationRequested() {
        try {
            if (!IsDisposed && IsHandleCreated) {
                BeginInvoke(ActivateFromOutside);
            }
        } catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException) {
            Log.Debug(ex, "The window closed before it could be activated");
        }
    }

    /// <summary>Restores the window if it is minimized and brings it to the front.</summary>
    private void ActivateFromOutside() {
        if (IsDisposed) {
            return;
        }

        if (WindowState == FormWindowState.Minimized) {
            WindowState = FormWindowState.Normal;
        }

        Activate();
    }

    /// <summary>The open file changed outside PlanCake, went missing, or came back.</summary>
    private void OnWatchedFileChanged(object? sender, FileChangeEventArgs e) {
        if (sender != _watcher || _file is null) {
            return;
        }

        var name = Path.GetFileName(_file.Path);

        switch (e.Kind) {
            case FileChangeKind.Missing:
                _fileMissing = true;
                _announcer.Announce(MissingFileMessage());
                break;
            case FileChangeKind.Restored:
                _fileMissing = false;

                if (Reload(fromOutside: true)) {
                    _announcer.Announce(_("{0} is back. File reloaded", name));
                }

                break;
            case FileChangeKind.Changed when Config.General.ExternalChangeAction == ExternalChangeAction.Ask:
                // Out of the watcher's event first: the question is a message box.
                BeginInvoke(AskToReload);
                break;
            case FileChangeKind.Changed:
                if (Reload(fromOutside: true)) {
                    _announcer.Announce(_("File reloaded"));
                }

                break;
        }
    }

    /// <summary>
    /// The "ask first" setting: Yes reloads; No keeps the view as it is, and a note action on it
    /// then fails as stale, since the file no longer holds what it shows.
    /// </summary>
    private void AskToReload() {
        if (_askingReload || _file is null || _fileMissing) {
            return;
        }

        _askingReload = true;

        try {
            var confirmed = DialogHelper.Confirm(
                _("{0} was changed outside PlanCake. Reload it?", Path.GetFileName(_file.Path)),
                _("File changed")
            );

            if (confirmed) {
                if (Reload(fromOutside: false)) {
                    ReturnFocus();
                    _announcer.Announce(_("File reloaded"));
                }
            } else {
                ReturnFocus();
                _announcer.Announce(_("The file was not reloaded. Press F5 to reload it."));
            }
        } finally {
            _askingReload = false;
        }
    }

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

    /// <summary>
    /// Help → User manual: the manual in the interface language (<see cref="HelpLocator"/>),
    /// opened in the default browser. A missing manual is said in a message box.
    /// </summary>
    private void ShowUserManual() {
        var culture = Localization.GetCurrentCulture();

        if (HelpLocator.FindManual(App.HelpFolder, culture) is { } manual) {
            ShellOpen(manual);
            return;
        }

        Log.Warning("No user manual for {Culture} under {Folder}", culture.Name, App.HelpFolder);
        DialogHelper.Show(
            _("The user manual could not be found."),
            _("User manual"),
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning
        );
        ReturnFocus();
    }

    /// <summary>Help → Keyboard shortcuts.</summary>
    private void ShowShortcuts() {
        using var dialog = new ShortcutsDialog(ShortcutsDialog.BuildRows());
        dialog.ShowDialog(this);
        ReturnFocus();
    }

    /// <summary>Help → About PlanCake.</summary>
    private void ShowAbout() {
        using var dialog = new AboutDialog();
        dialog.ShowDialog(this);
        ReturnFocus();
    }

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
        if (StartupFailed || IsDisposed) {
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
    /// notes carry localized role descriptions.
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

    // --- The notes list beside the document ---------------------------------------------

    /// <summary>True while the notes list is shown.</summary>
    private bool IsNotesListVisible => !splitContainer.Panel2Collapsed;

    /// <summary>True while the notes list is shown and has the keyboard focus.</summary>
    private bool IsNotesListFocused => IsNotesListVisible && notesList.ContainsFocus;

    private void SetUpNotesList() {
        notesList.Columns.Add(new NativeListViewColumn(String.Empty, LogicalToDeviceUnits(60)));
        notesList.Columns.Add(new NativeListViewColumn(String.Empty, LogicalToDeviceUnits(200)));
        notesList.Columns.Add(new NativeListViewColumn(String.Empty, LogicalToDeviceUnits(300)));
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
        notesList.Columns[0].Text = _("Lines");
        notesList.Columns[1].Text = _("Block");
        notesList.Columns[2].Text = _("Note");
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
        .Add(_("&Edit note"), editNote)
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
        var rows = render.Notes.Select(note => NotesListRow.From(note, startOfDocument).ToCells()).ToList();
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

    /// <summary>View → Notes list: shows or hides the list. A hidden list is skipped by F6.</summary>
    private void ToggleNotesList() => ShowNotesList(!_showNotesList);

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

    /// <summary>A note of the current render as the target of a note action.</summary>
    private NoteTarget CurrentTarget(RenderedNote note) => new(_generation, _renderedText, note.Block, note);

    protected override void OnFormClosed(FormClosedEventArgs e) {
        _download?.Cancel();
        documentView.MessageReceived -= OnPageMessage;
        documentView.AcceleratorKeyDown -= OnDocumentAcceleratorKeyDown;
        notesList.ItemActivate -= OnNotesListItemActivate;
        notesList.KeyDown -= OnNotesListKeyDown;
        notesList.GotFocus -= OnNotesListGotFocus;
        DragEnter -= OnWindowDragEnter;
        DragDrop -= OnWindowDragDrop;

        if (_watcher is { } watcher) {
            watcher.FileChanged -= OnWatchedFileChanged;
            watcher.Dispose();
            _watcher = null;
        }

        _instance?.Dispose();
        _instance = null;
        _updateService?.Dispose();
        _updateService = null;

        // Before the handles go: the menus need the windows they belong to while they are released.
        _menuBar?.Dispose();
        _menuBar = null;
        _notesListMenu?.Dispose();
        _notesListMenu = null;
        base.OnFormClosed(e);
    }

    private static class NativeMethods {
        /// <summary>False while a modal dialog this window owns is open.</summary>
        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowEnabled(IntPtr window);
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

/// <summary>The settings <c>MainWindow</c> applies itself, as they were before the Settings dialog.</summary>
internal sealed record AppliedSettings(
    string Language,
    string DefaultDocumentLanguage,
    bool ShowNotesList,
    NoteMarkers Markers,
    bool ConvertToUtf8
);

/// <summary>A note text kept after the file changed under it, for the next attempt on the same block or note.</summary>
/// <param name="Mode">Whether it was a new note or an edit.</param>
/// <param name="Key">The block's text (a new note) or the note's original text (an edit).</param>
/// <param name="Text">What the user typed.</param>
internal sealed record NoteDraft(NoteDialogMode Mode, string Key, string Text);
