using System.ComponentModel;
using Oire.PlanCake.Notes;
using Serilog;

namespace Oire.PlanCake.Utils;

/// <summary>What happened to a watched file.</summary>
internal enum FileChangeKind {
    /// <summary>The file's text changed, and not to the text PlanCake last showed or wrote.</summary>
    Changed,

    /// <summary>The file was deleted, or renamed or moved away.</summary>
    Missing,

    /// <summary>The file is back after <see cref="Missing"/>.</summary>
    Restored,
}

/// <summary>A change to a watched file.</summary>
/// <param name="kind">What happened.</param>
/// <param name="text">The file's text now; <see langword="null"/> for <see cref="FileChangeKind.Missing"/>.</param>
internal sealed class FileChangeEventArgs(FileChangeKind kind, string? text): EventArgs {
    public FileChangeKind Kind { get; } = kind;
    public string? Text { get; } = text;
}

/// <summary>
/// A timer that runs a callback once, some time after it was last (re)started. Injected into
/// <see cref="FileWatcher"/> so that its tests can fire it without waiting.
/// </summary>
internal interface IDebounceTimer: IDisposable {
    /// <summary>Runs <paramref name="callback"/> once after <paramref name="delay"/>, canceling any pending run.</summary>
    void Restart(TimeSpan delay, Action callback);

    /// <summary>Cancels the pending run, if any.</summary>
    void Stop();
}

/// <summary>A <see cref="IDebounceTimer"/> on the WinForms timer: the callback runs on the UI thread.</summary>
internal sealed class UiDebounceTimer: IDebounceTimer {
    private readonly System.Windows.Forms.Timer _timer = new();
    private Action? _callback;

    public UiDebounceTimer() => _timer.Tick += OnTick;

    public void Restart(TimeSpan delay, Action callback) {
        ArgumentNullException.ThrowIfNull(callback);
        _timer.Stop();
        _callback = callback;
        _timer.Interval = Math.Max(1, (int)delay.TotalMilliseconds);
        _timer.Start();
    }

    public void Stop() {
        _timer.Stop();
        _callback = null;
    }

    public void Dispose() {
        _timer.Tick -= OnTick;
        _timer.Dispose();
        _callback = null;
    }

    private void OnTick(object? sender, EventArgs e) {
        _timer.Stop();
        var callback = _callback;
        _callback = null;
        callback?.Invoke();
    }
}

/// <summary>
/// Watches one file for changes made outside PlanCake (Task 10 of the plan). It watches the
/// file's folder, not the file, because editors often save by writing a temporary file and
/// renaming it over the original. Events are debounced: the file is looked at once, 300 ms after
/// the last event about it, so a burst of events (and a save by rename) is one change.
/// </summary>
/// <remarks>
/// A change whose text equals the text last acknowledged (<see cref="Acknowledge"/>: what the
/// window shows, which after a note action is what PlanCake itself wrote) is ignored.
/// </remarks>
internal sealed class FileWatcher: IDisposable {
    /// <summary>How long after the last event the file is looked at.</summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

    private readonly IDebounceTimer _timer;
    private readonly Func<string, string?> _readText;
    private FileSystemWatcher? _watcher;
    private string? _known;
    private bool _missing;
    private bool _disposed;

    /// <param name="path">The file to watch.</param>
    /// <param name="timer">The debounce timer; its callback must run on the thread that handles <see cref="FileChanged"/>.</param>
    /// <param name="readText">
    /// Reads the file's text, or returns <see langword="null"/> when the file does not exist; an
    /// <see cref="IOException"/> (a locked file) means "look again later". Defaults to reading it
    /// the way <see cref="MarkdownFile"/> does.
    /// </param>
    public FileWatcher(string path, IDebounceTimer timer, Func<string, string?>? readText = null) {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(timer);

        Path = System.IO.Path.GetFullPath(path);
        _timer = timer;
        _readText = readText ?? ReadText;
    }

    /// <summary>The watched file's full path.</summary>
    public string Path { get; }

    /// <summary>True while the file is missing (deleted, or renamed or moved away).</summary>
    public bool IsMissing => _missing;

    /// <summary>Raised on the timer's thread when the file changed, went missing or came back.</summary>
    public event EventHandler<FileChangeEventArgs>? FileChanged;

    /// <summary>
    /// Starts watching the file's folder. The file system's events are marshaled through
    /// <paramref name="synchronizingObject"/> (the window), so everything runs on the UI thread.
    /// </summary>
    /// <returns>False when the folder cannot be watched; the failure is logged.</returns>
    public bool Start(ISynchronizeInvoke? synchronizingObject) {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_watcher is not null) {
            return true;
        }

        var folder = System.IO.Path.GetDirectoryName(Path);

        if (String.IsNullOrEmpty(folder)) {
            return false;
        }

        try {
            var watcher = new FileSystemWatcher(folder) {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
                    | NotifyFilters.CreationTime,
                SynchronizingObject = synchronizingObject,
            };
            watcher.Changed += OnFileSystemEvent;
            watcher.Created += OnFileSystemEvent;
            watcher.Deleted += OnFileSystemEvent;
            watcher.Renamed += OnFileSystemEvent;
            watcher.Error += OnError;
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
            Log.Debug("Watching {Path} for changes", Path);

            return true;
        } catch (Exception ex) when (ex is ArgumentException or IOException or PlatformNotSupportedException) {
            Log.Warning(ex, "Unable to watch {Path} for changes", Path);

            return false;
        }
    }

    /// <summary>
    /// Records <paramref name="text"/> as the file's text the window knows about (it shows it, or
    /// PlanCake has just written it): a change to exactly this text is not reported.
    /// </summary>
    public void Acknowledge(string text) {
        ArgumentNullException.ThrowIfNull(text);
        _known = text;
    }

    /// <summary>An event of the file system about the watched folder; internal for the tests.</summary>
    internal void OnFileSystemEvent(object? sender, FileSystemEventArgs e) {
        ArgumentNullException.ThrowIfNull(e);

        if (_disposed) {
            return;
        }

        var concerned = IsWatchedPath(e.FullPath)
            || (e is RenamedEventArgs renamed && IsWatchedPath(renamed.OldFullPath));

        if (concerned) {
            _timer.Restart(Debounce, Check);
        }
    }

    private void OnError(object? sender, ErrorEventArgs e) {
        // Events may have been lost (an overflowing buffer, the folder gone): look at the file.
        Log.Warning(e.GetException(), "File watcher error on {Path}", Path);

        if (!_disposed) {
            _timer.Restart(Debounce, Check);
        }
    }

    private bool IsWatchedPath(string? path) =>
        !String.IsNullOrEmpty(path) && String.Equals(path, Path, StringComparison.OrdinalIgnoreCase);

    /// <summary>Looks at the file once the events have settled, and reports what changed.</summary>
    private void Check() {
        if (_disposed) {
            return;
        }

        string? text;

        try {
            text = _readText(Path);
        } catch (IOException ex) {
            // Still being written (locked): look again a little later.
            Log.Debug(ex, "{Path} cannot be read yet; checking again", Path);
            _timer.Restart(Debounce, Check);

            return;
        }

        if (text is null) {
            if (!_missing) {
                _missing = true;
                Log.Information("{Path} is missing", Path);
                FileChanged?.Invoke(this, new FileChangeEventArgs(FileChangeKind.Missing, null));
            }

            return;
        }

        if (_missing) {
            _missing = false;
            _known = text;
            Log.Information("{Path} is back", Path);
            FileChanged?.Invoke(this, new FileChangeEventArgs(FileChangeKind.Restored, text));

            return;
        }

        if (_known is not null && String.Equals(text, _known, StringComparison.Ordinal)) {
            return;
        }

        // Reported once: the same text again is not a new change, whether or not the window reloads.
        _known = text;
        Log.Information("{Path} changed on disk", Path);
        FileChanged?.Invoke(this, new FileChangeEventArgs(FileChangeKind.Changed, text));
    }

    /// <summary>The file's text decoded as <see cref="MarkdownFile"/> does, or <see langword="null"/> when it is missing.</summary>
    private static string? ReadText(string path) {
        if (!File.Exists(path)) {
            return null;
        }

        try {
            return MarkdownFile.Open(path, new MarkdownFileOptions(Retries: 0)).Text;
        } catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) {
            return null;
        }
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        _timer.Dispose();

        if (_watcher is { } watcher) {
            watcher.EnableRaisingEvents = false;
            watcher.Changed -= OnFileSystemEvent;
            watcher.Created -= OnFileSystemEvent;
            watcher.Deleted -= OnFileSystemEvent;
            watcher.Renamed -= OnFileSystemEvent;
            watcher.Error -= OnError;
            watcher.Dispose();
            _watcher = null;
        }
    }
}
