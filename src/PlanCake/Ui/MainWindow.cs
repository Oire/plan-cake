using System.Runtime.InteropServices;
using GetText.WindowsForms;
using Oire.PlanCake.Notes;
using Oire.PlanCake.Rendering;
using Oire.PlanCake.Services;
using Oire.PlanCake.Utils;
using Oire.WinForms.NativeControls;
using Serilog;
using static Oire.PlanCake.Utils.Localization;
using App = Oire.PlanCake.Utils.Constants.App;

namespace Oire.PlanCake.Ui;

internal sealed partial class MainWindow: Form {
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

    /// <summary>
    /// Whether the notes list beside the document is shown here: View → Notes list and Settings
    /// save it, and it follows another window's save when this one is activated.
    /// </summary>
    private bool _showNotesList = Config.General.ShowNotesList;

    /// <summary>The window's menu bar, attached once the window has a handle.</summary>
    private NativeMenuBar? _menuBar;

    /// <summary>Picks the menu bar's keys (Alt+letter, Alt alone, F10) out of those pressed in the document.</summary>
    private readonly MenuKeys _menuKeys = new();

    /// <summary>The context menu of the notes list: Edit note, Delete note.</summary>
    private NativeContextMenu? _notesListMenu;

    /// <summary>The list window and the name last given to it for MSAA.</summary>
    private (IntPtr Handle, string Name)? _notesListName;

    /// <summary>Watches <see cref="_file"/> for changes made outside PlanCake.</summary>
    private FileWatcher? _watcher;

    /// <summary>
    /// This window's claim on <see cref="_file"/>: a second attempt to open it activates this
    /// window.
    /// </summary>
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

    /// <summary>
    /// True while a file is read and rendered in the background (<see cref="WaitForOpening"/>): the
    /// window pumps messages then, and nothing may open another file or apply settings meanwhile.
    /// </summary>
    private bool _opening;

    /// <summary>A file larger than this shows <see cref="OpeningDialog"/> at once while it opens.</summary>
    private const long LargeFileSize = 1024 * 1024;

    /// <summary>How long opening a smaller file may take before <see cref="OpeningDialog"/> shows.</summary>
    private static readonly TimeSpan OpeningDialogDelay = TimeSpan.FromMilliseconds(500);

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

        // The .ico holds every size from 16 to 256 pixels, so the title bar and Alt+Tab each get
        // a sharp one; the exe's own icon (ApplicationIcon) is not what a form shows.
        using (var iconStream = typeof(MainWindow).Assembly.GetManifestResourceStream("PlanCake.ico")) {
            if (iconStream is not null) {
                Icon = new Icon(iconStream);
            }
        }

        _initialFile = initialFile;
        _announcer = new StatusAnnouncer(statusStrip, statusLabel);
        documentView.MessageReceived += OnPageMessage;
        documentView.AcceleratorKeyDown += OnDocumentAcceleratorKeyDown;
        documentView.AcceleratorKeyUp += OnDocumentAcceleratorKeyUp;
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

    protected override async void OnLoad(EventArgs e) {
        // The window is not shown yet: it opens where and as large as this says.
        PlaceWindow();
        base.OnLoad(e);
        NameNotesList();

        try {
            await documentView.InitializeAsync();
            documentView.ZoomFactor = Config.Window.Zoom / 100.0;
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

    /// <summary>
    /// Opens the window where the last one closed (<see cref="Config.SectionWindow"/>), moved onto a
    /// screen that is still there, maximized if it was, with the notes list as wide as it was. A
    /// first window fits the screen it opens on instead: the designer's size is taller than a
    /// 1080p screen at 150%, which hid the status bar behind the taskbar.
    /// </summary>
    private void PlaceWindow() {
        var saved = Config.Window;
        var primary = Screen.PrimaryScreen;
        var workAreas = Screen.AllScreens
            .OrderBy(screen => screen.Equals(primary) ? 0 : 1)
            .Select(screen => screen.WorkingArea)
            .ToList();
        var bounds = WindowPlacement.Restore(
            new Rectangle(saved.Left, saved.Top, saved.Width, saved.Height), workAreas, MinimumSize
        );

        if (bounds is { } restored) {
            StartPosition = FormStartPosition.Manual;
            Bounds = restored;
        } else {
            // The designer's size was centered when the handle was made.
            Size = WindowPlacement.FirstRunSize(Size, Screen.FromPoint(Cursor.Position).WorkingArea);
            CenterToScreen();
        }

        var available = splitContainer.Width - splitContainer.SplitterWidth;

        if (WindowPlacement.SplitterDistance(
                available, saved.NotesListWidth, splitContainer.Panel2MinSize, splitContainer.Panel1MinSize
            ) is { } distance) {
            splitContainer.SplitterDistance = distance;
        }

        if (bounds is not null && saved.Maximized) {
            WindowState = FormWindowState.Maximized;
        }
    }

    /// <summary>
    /// Saves where the window is for the next one (<see cref="PlaceWindow"/>): its normal bounds
    /// even while maximized or minimized, the notes list's width (the last one known while it is
    /// hidden) and the zoom.
    /// </summary>
    private void SaveWindowPlacement() {
        var normal = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        var listWidth = IsNotesListVisible
            ? splitContainer.Width - splitContainer.SplitterWidth - splitContainer.SplitterDistance
            : Config.Window.NotesListWidth;
        var zoom = documentView.IsInitialized
            ? (int)Math.Round(documentView.ZoomFactor * 100)
            : Config.Window.Zoom;

        var window = new Config.SectionWindow {
            Left = normal.Left,
            Top = normal.Top,
            Width = normal.Width,
            Height = normal.Height,
            Maximized = WindowState == FormWindowState.Maximized,
            NotesListWidth = listWidth,
            Zoom = zoom,
        };

        if (!Config.SaveWindow(window)) {
            Log.Warning("The window's size and place could not be saved");
        }
    }

    private static void ShowError(string message) =>
        DialogHelper.Show(message, _("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);

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

    /// <summary>The window title: the open file's name, then the program's.</summary>
    private void UpdateTitle() =>
        Text = _file is null ? App.Name : _("{0} - {1}", Path.GetFileName(_file.Path), App.Name);

    protected override void OnFormClosed(FormClosedEventArgs e) {
        if (!StartupFailed) {
            SaveWindowPlacement();
        }

        _download?.Cancel();
        documentView.MessageReceived -= OnPageMessage;
        documentView.AcceleratorKeyDown -= OnDocumentAcceleratorKeyDown;
        documentView.AcceleratorKeyUp -= OnDocumentAcceleratorKeyUp;
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

        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    }
}
