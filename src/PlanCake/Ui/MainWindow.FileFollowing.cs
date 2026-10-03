using Oire.PlanCake.Notes;
using Oire.PlanCake.Utils;
using Oire.PlanCake.Utils.Enums;
using Serilog;
using static Oire.PlanCake.Utils.Localization;

namespace Oire.PlanCake.Ui;

internal sealed partial class MainWindow {
    /// <summary>
    /// Starts watching <paramref name="path"/> for changes made outside PlanCake, instead of the
    /// previous file.
    /// </summary>
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
                    _announcer.Announce(_("{0} is back and was reloaded.", name));
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
            default:
                throw new ArgumentOutOfRangeException(nameof(e), e.Kind, null);
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
}
