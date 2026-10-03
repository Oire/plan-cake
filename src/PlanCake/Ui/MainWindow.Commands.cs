using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Oire.PlanCake.Notes;
using Oire.PlanCake.Services;
using Oire.PlanCake.Utils;
using Serilog;
using static Oire.PlanCake.Utils.Localization;
using App = Oire.PlanCake.Utils.Constants.App;

namespace Oire.PlanCake.Ui;

internal sealed partial class MainWindow {
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
            _announcer.Announce(_("Unable to read the clipboard"));

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
            case ClipboardContentKind.Nothing:
                _announcer.Announce(_("The clipboard holds no Markdown file or link."));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(content), content.Kind, null);
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
        // content is accepted misleads when the link turns out to be a web page (a JAWS check in
        // plan 001, Task 11). The name comes with "Downloaded and saved to".
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
            Log.Error(ex, "Download of {Url} failed", UrlHelper.ForLog(url));

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
    /// File → Open in editor: a Markdown file in the program Windows opens Markdown files with,
    /// unless that is PlanCake itself (which would only bring this window to the front) or there
    /// is none; any other file (opened through All files), and a Markdown file then, with its
    /// "edit" verb, else in Notepad. Never a non-Markdown file with its default verb, which for a
    /// script or a program runs it.
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
            var program = LinkResolver.DefaultProgramFor(path);

            if (!LinkResolver.IsNoEditor(program, Environment.ProcessPath)) {
                ShellOpen(path);
                return;
            }

            Log.Information(
                "Markdown files open in {Program}; opening {Path} for editing otherwise",
                program ?? "nothing", path
            );
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
            "Exported {0} note to {1}", "Exported {0} notes to {1}", count, count, Path.GetFileName(dialog.FileName)
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
}
