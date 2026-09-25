using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using GetText.WindowsForms;
using Oire.PlanCake.Notes;
using Oire.PlanCake.Rendering;
using Oire.PlanCake.Utils;
using Oire.PlanCake.Utils.Enums;
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
    private const NoteStyle DocumentNoteStyle = NoteStyle.Note;

    private readonly StatusAnnouncer _announcer;
    private readonly string? _initialFile;

    /// <summary>The open file, or <see langword="null"/> before the first one is opened.</summary>
    private MarkdownFile? _file;

    /// <summary>The render the page shows (or is about to show).</summary>
    private RenderResult? _render;

    /// <summary>The number of <see cref="_render"/>; a page message about an older one is ignored.</summary>
    private int _generation;

    /// <summary>The block the user last interacted with, from <see cref="_render"/>.</summary>
    private BlockInfo? _position;

    /// <summary>Where the page puts the virtual cursor when it shows <see cref="_render"/>.</summary>
    private PageFocus? _pendingFocus;

    /// <summary>True once the page has loaded and can take messages.</summary>
    private bool _pageReady;

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
    /// through here. On failure the user is told why and the current document stays.
    /// </summary>
    /// <returns>True when the file was opened.</returns>
    internal bool OpenFile(string path) {
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
        _file = file;

        if (!reopened) {
            _position = null;
        }

        RenderDocument();
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
    /// <paramref name="focus"/>, the page returns to the block the user was on.
    /// </summary>
    private void RenderDocument(PageFocus? focus = null) {
        if (_file is null) {
            return;
        }

        var options = new RenderOptions(
            NoteMarkers.Default,
            RenderMode.Interactive,
            DocumentNoteStyle,
            CurrentRenderStrings(),
            DocumentLanguage
        );
        _render = MarkdownRenderer.Render(_file.Text, options);
        _generation++;

        if (focus is null && PositionRestorer.FindTarget(_position, _render.Blocks) is { } target) {
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
        new(_("Note:"), _("user note"), _("unote"));

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
        if (TryRunShortcut(e.KeyData)) {
            e.Handled = true;
        }
    }

    private bool TryRunShortcut(Keys keyData) {
        if (!HostCommands.TryGetCommand(keyData, out var command)) {
            return false;
        }

        // The key may have come from inside a WebView2 event: run the command later, so that a
        // dialog it opens does not start a nested message loop inside that event.
        BeginInvoke(() => RunCommand(command, keyData));
        return true;
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
            case HostCommand.NextNote:
                MoveToNote(forward: true);
                break;
            case HostCommand.PreviousNote:
                MoveToNote(forward: false);
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
            case PageMessages.ActivateNote:
            case PageMessages.ContextMenu:
                // The note dialog and the context menu arrive in Task 7; the page has already
                // reported the position.
                Log.Debug("Page {Type}: {Json}", e.Type, e.Message.GetRawText());
                break;
            case PageMessages.OpenLink:
                var href = PageMessages.GetString(e.Message, "href") ?? "";
                BeginInvoke(() => OpenLink(href));
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
