using Oire.PlanCake.Notes;
using Oire.PlanCake.Rendering;
using Oire.PlanCake.Utils;
using Serilog;
using static Oire.PlanCake.Utils.Localization;

namespace Oire.PlanCake.Ui;

internal sealed partial class MainWindow {
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
        if (_opening) {
            Log.Information("{Path} not opened: another file is opening", path);

            return OpenOutcome.Failed;
        }

        string fullPath;

        try {
            fullPath = Path.GetFullPath(path);
        } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) {
            Log.Error(ex, "Unable to open {Path}", path);
            ShowError(_("Unable to open {0}: {1}", path, ex.Message));

            return OpenOutcome.Failed;
        }

        if (Directory.Exists(fullPath)) {
            Log.Warning("Unable to open {Path}: it is a folder", fullPath);
            ShowError(_("{0} is a folder, not a file.", path));

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

        // The document language helps recognize the encoding of a file that is not UTF-8.
        var documentLanguage = reopened ? _documentLanguage : Config.General.DefaultDocumentLanguage;
        var fileOptions = new MarkdownFileOptions(
            ConvertToUtf8: Config.Advanced.ConvertToUtf8,
            DocumentLanguage: documentLanguage
        );
        var renderOptions = RenderOptionsFor(documentLanguage);

        // Read, decoded, parsed and rendered off the UI thread, so a large file does not freeze
        // the window; nothing of the window's state changes until it is done.
        var work = Task.Run(() => {
            var opened = MarkdownFile.Open(fullPath, fileOptions);
            var render = MarkdownRenderer.Render(opened.Text, renderOptions);

            return new PreparedRender(opened.Text, renderOptions, render, opened);
        });

        if (!WaitForOpening(work, fullPath)) {
            claim?.Dispose();
            Log.Information("Opening {Path} canceled", fullPath);

            // Whatever the work still throws is of no interest any more.
            work.ContinueWith(
                task => Log.Debug(task.Exception, "Canceled opening of {Path} failed", fullPath),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default
            );
            ReturnFocus();

            return OpenOutcome.Failed;
        }

        MarkdownFile file;
        PreparedRender prepared;

        try {
            prepared = work.GetAwaiter().GetResult();
            file = prepared.File;
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
        } catch {
            claim?.Dispose();
            throw;
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
        // Replacing the content in place left JAWS at its old offset in the virtual buffer, which
        // in a shorter file is the end (docs/jaws-spike.md, "A followed link landed at the end of
        // the new file"). The same file again, and every re-render after a note action, stays in
        // place so the reading position survives.
        if (!reopened && _pageReady) {
            _pageReady = false;
            documentView.Navigate(PageFile);
        }

        RenderDocument(opened: !reopened, restorePosition: !reopened, prepared: prepared);
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
    /// Waits for a file being opened in the background. A large file, or one that takes longer
    /// than <see cref="OpeningDialogDelay"/>, shows <see cref="OpeningDialog"/> meanwhile.
    /// </summary>
    /// <returns>False when the user canceled: the window stays as it was.</returns>
    private bool WaitForOpening(Task work, string path) {
        var delay = IsLargeFile(path) ? TimeSpan.Zero : OpeningDialogDelay;
        _opening = true;

        try {
            // WhenAny never throws: the caller reads how the work ended.
            if (Task.WhenAny(work).Wait(delay)) {
                return true;
            }

            Log.Information("Opening {Path} takes a while; showing the progress dialog", path);

            using var dialog = new OpeningDialog(Path.GetFileName(path), work);

            return dialog.ShowDialog(this) == DialogResult.OK;
        } finally {
            _opening = false;
        }
    }

    private static bool IsLargeFile(string path) {
        try {
            return new FileInfo(path).Length > LargeFileSize;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                         or NotSupportedException) {
            return false;
        }
    }

    /// <summary>Tells the user what is special about the file just opened, if anything.</summary>
    private void AnnounceFileState(MarkdownFile file) {
        var messages = new List<string>();

        if (file.ConvertedFrom is { } convertedFrom) {
            messages.Add(_("The file was converted from {0} to UTF-8.", LegacyEncoding.DisplayName(convertedFrom)));
        } else if (LocalizedText.ReadOnlyReason(file) is { } readOnly) {
            messages.Add(readOnly);
        }

        if (_render?.Parse.HasUnterminated == true) {
            messages.Add(_("A note has no closing marker, so it runs to the end of the file."));
        }

        if (messages.Count > 0) {
            _announcer.Announce(String.Join(" ", messages));
        }
    }
}

/// <summary>A file opened and rendered in the background, for the window to show.</summary>
/// <param name="Text">The file text the render was made from.</param>
/// <param name="Options">The options it was rendered with.</param>
/// <param name="Result">The render.</param>
/// <param name="File">The file.</param>
internal sealed record PreparedRender(string Text, RenderOptions Options, RenderResult Result, MarkdownFile File);
