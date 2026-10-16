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
    /// <summary>
    /// Runs <paramref name="callback"/> once after <paramref name="delay"/>, canceling any pending
    /// run.
    /// </summary>
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
/// Watches one file for changes made outside PlanCake. It watches the file's folder, not the
/// file, because editors often save by writing a temporary file and renaming it over the
/// original. Events are debounced: the file is looked at once, 300 ms after
/// the last event about it, so a burst of events (and a save by rename) is one change.
/// </summary>
/// <remarks>
/// A change whose text equals the text last acknowledged (<see cref="Acknowledge"/>: what the
/// window shows, which after a note action is what PlanCake itself wrote) is ignored.
/// <para>
/// A symbolic link is watched at both ends: PlanCake writes the file it leads to (see
/// <see cref="MarkdownFile"/>), and an editor that opened that file saves it under its own name and
/// folder, so events about either path count, and the target's folder is watched too when it is
/// another one. The file is still read through the link.
/// </para>
/// <para>
/// A file on a network path (a UNC path such as <c>\\wsl.localhost\Ubuntu\…</c>, or a mapped
/// network drive) is also polled: <see cref="FileSystemWatcher"/> starts there without an error
/// and then reports nothing at all about a WSL distribution's files, so every
/// <see cref="PollInterval"/> the file's size and time of last write are compared with the last
/// ones seen, and it is read when they differ.
/// </para>
/// </remarks>
internal sealed class FileWatcher: IDisposable {
    /// <summary>How long after the last event the file is looked at.</summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// How many times in a row a file that cannot be read (locked, its permissions changed) is
    /// looked at again, each time twice as late (up to <see cref="MaxRetryDelay"/>), before the
    /// watcher waits for the next event about it.
    /// </summary>
    public const int MaxReadRetries = 8;

    /// <summary>The longest wait before a file that cannot be read is looked at again.</summary>
    public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(10);

    /// <summary>How often a file on a network path is looked at.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly IDebounceTimer _timer;
    private readonly Func<string, string?> _readText;
    private readonly List<FileSystemWatcher> _watchers = [];
    private string? _known;
    private bool _missing;
    private bool _disposed;
    private bool _pollingStarted;

    /// <summary>What the last poll saw; <see langword="null"/> before the first one.</summary>
    private FileStamp? _stamp;

    /// <summary>How many times in a row the file could not be read.</summary>
    private int _failedReads;

    /// <param name="path">The file to watch.</param>
    /// <param name="timer">
    /// The debounce timer; its callback must run on the thread that handles
    /// <see cref="FileChanged"/>.
    /// </param>
    /// <param name="readText">
    /// Reads the file's text, or returns <see langword="null"/> when the file does not exist; an
    /// <see cref="IOException"/> (a locked file) means "look again later". Defaults to
    /// <see cref="ReadText(String, MarkdownFileOptions?)"/> with the default options; the window
    /// passes one that decodes the file exactly as it does (its document language), or the two
    /// texts of a legacy file could differ and every event would look like a change.
    /// </param>
    /// <param name="polling">
    /// Whether to poll the file; by default, when it or the file a symbolic link leads to is on a
    /// network path (<see cref="IsOnNetwork"/>).
    /// </param>
    public FileWatcher(
        string path,
        IDebounceTimer timer,
        Func<string, string?>? readText = null,
        bool? polling = null
    ) {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(timer);

        Path = System.IO.Path.GetFullPath(path);
        TargetPath = FinalTarget(Path);
        _timer = timer;
        _readText = readText ?? (file => ReadText(file));
        Polling = polling ?? (IsOnNetwork(Path) || (TargetPath is not null && IsOnNetwork(TargetPath)));
    }

    /// <summary>The watched file's full path.</summary>
    public string Path { get; }

    /// <summary>
    /// The file a symbolic link at <see cref="Path"/> leads to, resolved once when the watcher is
    /// made; <see langword="null"/> when <see cref="Path"/> is no link.
    /// </summary>
    public string? TargetPath { get; }

    /// <summary>
    /// True when the file is also looked at every <see cref="PollInterval"/>, besides the events
    /// about it.
    /// </summary>
    public bool Polling { get; }

    /// <summary>True while the file is missing (deleted, or renamed or moved away).</summary>
    public bool IsMissing => _missing;

    /// <summary>Raised on the timer's thread when the file changed, went missing or came back.</summary>
    public event EventHandler<FileChangeEventArgs>? FileChanged;

    /// <summary>
    /// Starts watching the file's folder, and the folder of the file a symbolic link leads to
    /// when that is another one. The file system's events are marshaled through
    /// <paramref name="synchronizingObject"/> (the window), so everything runs on the UI thread.
    /// When <see cref="Polling"/>, polling starts as well, whether or not the folder can be watched.
    /// </summary>
    /// <returns>
    /// False when the file's folder cannot be watched and the file is not polled; the failure is
    /// logged.
    /// </returns>
    public bool Start(ISynchronizeInvoke? synchronizingObject) {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_watchers.Count > 0 || _pollingStarted) {
            return true;
        }

        if (Polling) {
            _pollingStarted = true;
            Log.Information("{Path} is on a network path; looking at it every {Interval}", Path, PollInterval);
            _timer.Restart(PollInterval, Poll);
        }

        var folder = System.IO.Path.GetDirectoryName(Path);

        if (String.IsNullOrEmpty(folder) || !Watch(folder, Path, synchronizingObject)) {
            return Polling;
        }

        // Without it outside changes to the target are missed, but the link's folder still tells.
        var targetFolder = TargetPath is null ? null : System.IO.Path.GetDirectoryName(TargetPath);

        if (!String.IsNullOrEmpty(targetFolder) && !String.Equals(targetFolder, folder, StringComparison.OrdinalIgnoreCase)) {
            Watch(targetFolder, TargetPath!, synchronizingObject);
        }

        return true;
    }

    /// <summary>Watches <paramref name="folder"/> for events about <paramref name="file"/>.</summary>
    /// <returns>False when the folder cannot be watched; the failure is logged.</returns>
    private bool Watch(string folder, string file, ISynchronizeInvoke? synchronizingObject) {
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
            _watchers.Add(watcher);
            Log.Debug("Watching {Path} for changes", file);

            return true;
        } catch (Exception ex) when (ex is ArgumentException or IOException or PlatformNotSupportedException) {
            Log.Warning(ex, "Unable to watch {Path} for changes", file);

            return false;
        }
    }

    /// <summary>
    /// The file a symbolic link at <paramref name="path"/> finally leads to, or
    /// <see langword="null"/> when it is no link or cannot be resolved.
    /// </summary>
    private static string? FinalTarget(string path) {
        try {
            var target = new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName;

            return target is null || String.Equals(target, path, StringComparison.OrdinalIgnoreCase) ? null : target;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                         or NotSupportedException) {
            Log.Debug(ex, "Unable to resolve where {Path} leads", path);

            return null;
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
            // A new event: a file that could not be read is worth trying again from the start.
            _failedReads = 0;
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
        !String.IsNullOrEmpty(path)
        && (String.Equals(path, Path, StringComparison.OrdinalIgnoreCase)
            || String.Equals(path, TargetPath, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether <paramref name="path"/> is on a network path: a UNC path, or a drive that Windows
    /// reports as a network drive.
    /// </summary>
    internal static bool IsOnNetwork(string path) {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        // A device path to a drive (\\?\C:\…, \\.\C:\…) is on that drive.
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)
            || path.StartsWith(@"\\.\", StringComparison.Ordinal)) {
            path = path[4..];
        } else if (path.StartsWith(@"\\", StringComparison.Ordinal)) {
            return true;
        }

        var root = System.IO.Path.GetPathRoot(path);

        if (String.IsNullOrEmpty(root) || root.StartsWith('\\')) {
            return false;
        }

        try {
            return new DriveInfo(root).DriveType == DriveType.Network;
        } catch (ArgumentException) {
            return false;
        }
    }

    /// <summary>
    /// Compares the size and time of last write of the file (or of the file a symbolic link leads
    /// to) with what the last poll saw, and looks at the file when they changed.
    /// </summary>
    private void Poll() {
        if (_disposed) {
            return;
        }

        var stamp = FileStamp.Of(TargetPath ?? Path);

        if (_stamp == stamp) {
            _timer.Restart(PollInterval, Poll);

            return;
        }

        _stamp = stamp;
        // A change: a file that could not be read is worth trying again from the start.
        _failedReads = 0;
        Check();
    }

    /// <summary>
    /// Looks at the file once the events have settled, and reports what changed; when
    /// <see cref="Polling"/>, the next poll follows.
    /// </summary>
    private void Check() {
        if (_disposed) {
            return;
        }

        var retrying = Look();

        // The handler of FileChanged may have disposed the watcher.
        if (Polling && !retrying && !_disposed) {
            _timer.Restart(PollInterval, Poll);
        }
    }

    /// <summary>Reads the file and reports what changed.</summary>
    /// <returns>True when the file could not be read and is looked at again later.</returns>
    private bool Look() {
        string? text;

        try {
            text = _readText(Path);
        } catch (IOException ex) {
            _failedReads++;

            // Still being written (locked): look again a little later, each time later still.
            // A file that stays unreadable (its permissions changed, another program holds it
            // without sharing) is left alone until the next event about it.
            if (_failedReads > MaxReadRetries) {
                Log.Warning(ex, "{Path} cannot be read; waiting for the next change to it", Path);

                return false;
            }

            Log.Debug(ex, "{Path} cannot be read yet; checking again", Path);
            _timer.Restart(RetryDelay(_failedReads), Check);

            return true;
        }

        _failedReads = 0;

        if (text is null) {
            if (!_missing) {
                _missing = true;
                Log.Information("{Path} is missing", Path);
                FileChanged?.Invoke(this, new FileChangeEventArgs(FileChangeKind.Missing, null));
            }

            return false;
        }

        if (_missing) {
            _missing = false;
            _known = text;
            Log.Information("{Path} is back", Path);
            FileChanged?.Invoke(this, new FileChangeEventArgs(FileChangeKind.Restored, text));

            return false;
        }

        if (_known is not null && String.Equals(text, _known, StringComparison.Ordinal)) {
            return false;
        }

        // Reported once: the same text again is not a new change, whether or not the window reloads.
        _known = text;
        Log.Information("{Path} changed on disk", Path);
        FileChanged?.Invoke(this, new FileChangeEventArgs(FileChangeKind.Changed, text));

        return false;
    }

    /// <summary>
    /// The wait before the next look at a file that could not be read <paramref name="failures"/>
    /// times.
    /// </summary>
    internal static TimeSpan RetryDelay(int failures) {
        var delay = Debounce * Math.Pow(2, Math.Clamp(failures - 1, 0, 16));

        return delay < MaxRetryDelay ? delay : MaxRetryDelay;
    }

    /// <summary>
    /// The file's text decoded as <see cref="MarkdownFile"/> does with <paramref name="options"/>
    /// (never retried, and never converted), or <see langword="null"/> when it is missing.
    /// </summary>
    public static string? ReadText(string path, MarkdownFileOptions? options = null) {
        if (!File.Exists(path)) {
            return null;
        }

        var readOptions = (options ?? MarkdownFileOptions.Default) with { ConvertToUtf8 = false, Retries = 0 };

        try {
            return MarkdownFile.Open(path, readOptions).Text;
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

        foreach (var watcher in _watchers) {
            watcher.EnableRaisingEvents = false;
            watcher.Changed -= OnFileSystemEvent;
            watcher.Created -= OnFileSystemEvent;
            watcher.Deleted -= OnFileSystemEvent;
            watcher.Renamed -= OnFileSystemEvent;
            watcher.Error -= OnError;
            watcher.Dispose();
        }

        _watchers.Clear();
    }

    /// <summary>
    /// A file's size and time of last write, as a poll sees them; the default when the file is
    /// missing or cannot be looked at.
    /// </summary>
    private readonly record struct FileStamp(long Length, DateTime WrittenUtc) {
        public static FileStamp Of(string path) {
            try {
                var info = new FileInfo(path);

                return info.Exists ? new FileStamp(info.Length, info.LastWriteTimeUtc) : default;
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                return default;
            }
        }
    }
}
