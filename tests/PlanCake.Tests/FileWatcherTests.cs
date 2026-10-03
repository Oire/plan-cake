using System.Text;
using AwesomeAssertions;
using Oire.PlanCake.Notes;
using Oire.PlanCake.Utils;
using Xunit;

namespace Oire.PlanCake.Tests;

public class FileWatcherTests: IDisposable {
    private static readonly UTF8Encoding _utf8 = new(false);

    private readonly string _folder;
    private readonly string _path;
    private readonly FakeTimer _timer = new();
    private readonly List<FileChangeEventArgs> _changes = [];

    public FileWatcherTests() {
        _folder = Path.Combine(Path.GetTempPath(), $"PlanCake.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_folder);
        _path = Path.Combine(_folder, "plan.md");
        File.WriteAllText(_path, "# Plan\n\nFirst.\n", _utf8);
    }

    public void Dispose() {
        if (Directory.Exists(_folder)) {
            Directory.Delete(_folder, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>A watcher that knows the file's current text, as the window does after rendering it.</summary>
    private FileWatcher Watcher(Func<string, string?>? readText = null) {
        var watcher = new FileWatcher(_path, _timer, readText);
        watcher.FileChanged += (_, e) => _changes.Add(e);
        watcher.Acknowledge(File.ReadAllText(_path));

        return watcher;
    }

    private void Changed(FileWatcher watcher, string? path = null) => watcher.OnFileSystemEvent(
        null,
        new FileSystemEventArgs(WatcherChangeTypes.Changed, _folder, Path.GetFileName(path ?? _path))
    );

    private void Deleted(FileWatcher watcher, string? path = null) => watcher.OnFileSystemEvent(
        null,
        new FileSystemEventArgs(WatcherChangeTypes.Deleted, _folder, Path.GetFileName(path ?? _path))
    );

    private void Created(FileWatcher watcher, string path) => watcher.OnFileSystemEvent(
        null,
        new FileSystemEventArgs(WatcherChangeTypes.Created, _folder, Path.GetFileName(path))
    );

    private void Renamed(FileWatcher watcher, string from, string to) => watcher.OnFileSystemEvent(
        null,
        new RenamedEventArgs(WatcherChangeTypes.Renamed, _folder, Path.GetFileName(to), Path.GetFileName(from))
    );

    [Fact]
    public void BurstOfEvents_WithinTheDebounce_ProducesOneReload() {
        using var watcher = Watcher();
        File.WriteAllText(_path, "# Plan\n\nSecond.\n", _utf8);

        for (var i = 0; i < 5; i++) {
            Changed(watcher);
        }

        _timer.Restarts.Should().Be(5);
        _timer.Delay.Should().Be(FileWatcher.Debounce);
        _timer.Fire();

        _changes.Should().ContainSingle();
        _changes[0].Kind.Should().Be(FileChangeKind.Changed);
        _changes[0].Text.Should().Be("# Plan\n\nSecond.\n");
        _timer.IsPending.Should().BeFalse();
    }

    [Fact]
    public void TwoChanges_ApartFromEachOther_ProduceTwoReloads() {
        using var watcher = Watcher();

        File.WriteAllText(_path, "# Plan\n\nSecond.\n", _utf8);
        Changed(watcher);
        _timer.Fire();
        File.WriteAllText(_path, "# Plan\n\nThird.\n", _utf8);
        Changed(watcher);
        _timer.Fire();

        _changes.Select(change => change.Text).Should().Equal("# Plan\n\nSecond.\n", "# Plan\n\nThird.\n");
    }

    [Fact]
    public void OwnWrite_ProducesNoReload() {
        using var watcher = Watcher();
        var file = MarkdownFile.Open(_path);

        // What the window does after a note action: write, then render (and acknowledge) the new text.
        file.Write("# Plan\n\nFirst.\n[usernote]Note[/usernote]\n");
        watcher.Acknowledge(file.Text);
        Changed(watcher);
        Renamed(watcher, Path.Combine(_folder, ".plan.md.tmp"), _path);
        _timer.Fire();

        _changes.Should().BeEmpty();
    }

    [Fact]
    public void SameText_AsAcknowledged_ProducesNoReload() {
        using var watcher = Watcher();

        // An editor saving without changes touches the file but leaves its text alone.
        File.WriteAllText(_path, File.ReadAllText(_path), _utf8);
        Changed(watcher);
        _timer.Fire();

        _changes.Should().BeEmpty();
    }

    [Fact]
    public void AChange_IsReportedOnce_EvenWhenTheWindowDoesNotReload() {
        using var watcher = Watcher();

        File.WriteAllText(_path, "# Plan\n\nSecond.\n", _utf8);
        Changed(watcher);
        _timer.Fire();

        // The user said No to the reload: the next event with the same text is not a new change.
        Changed(watcher);
        _timer.Fire();

        _changes.Should().ContainSingle();
    }

    [Fact]
    public void SaveByRename_ProducesOneReload_AndNoMissingState() {
        using var watcher = Watcher();
        var temp = Path.Combine(_folder, "plan.md~");

        File.WriteAllText(temp, "# Plan\n\nSaved by rename.\n", _utf8);
        Created(watcher, temp);
        File.Delete(_path);
        Deleted(watcher);
        File.Move(temp, _path);
        Renamed(watcher, temp, _path);
        _timer.Fire();

        _changes.Should().ContainSingle();
        _changes[0].Kind.Should().Be(FileChangeKind.Changed);
        _changes[0].Text.Should().Be("# Plan\n\nSaved by rename.\n");
        watcher.IsMissing.Should().BeFalse();
    }

    [Fact]
    public void RealDelete_ProducesTheMissingState() {
        using var watcher = Watcher();

        File.Delete(_path);
        Deleted(watcher);
        _timer.Fire();

        _changes.Should().ContainSingle();
        _changes[0].Kind.Should().Be(FileChangeKind.Missing);
        _changes[0].Text.Should().BeNull();
        watcher.IsMissing.Should().BeTrue();
    }

    [Fact]
    public void RenameAway_ProducesTheMissingState() {
        using var watcher = Watcher();
        var renamed = Path.Combine(_folder, "old-plan.md");

        File.Move(_path, renamed);
        Renamed(watcher, _path, renamed);
        _timer.Fire();

        _changes.Should().ContainSingle();
        _changes[0].Kind.Should().Be(FileChangeKind.Missing);
    }

    [Fact]
    public void MissingFile_IsReportedOnce() {
        using var watcher = Watcher();

        File.Delete(_path);
        Deleted(watcher);
        _timer.Fire();
        Deleted(watcher);
        _timer.Fire();

        _changes.Should().ContainSingle();
    }

    [Fact]
    public void FileComingBack_AfterMissing_IsRestored() {
        using var watcher = Watcher();

        File.Delete(_path);
        Deleted(watcher);
        _timer.Fire();
        File.WriteAllText(_path, "# Plan\n\nBack.\n", _utf8);
        Created(watcher, _path);
        _timer.Fire();

        _changes.Select(change => change.Kind).Should().Equal(FileChangeKind.Missing, FileChangeKind.Restored);
        _changes[1].Text.Should().Be("# Plan\n\nBack.\n");
        watcher.IsMissing.Should().BeFalse();
    }

    [Fact]
    public void FileComingBack_WithTheSameText_IsStillRestored() {
        using var watcher = Watcher();
        var original = File.ReadAllText(_path);
        var renamed = Path.Combine(_folder, "elsewhere.md");

        File.Move(_path, renamed);
        Renamed(watcher, _path, renamed);
        _timer.Fire();
        File.Move(renamed, _path);
        Renamed(watcher, renamed, _path);
        _timer.Fire();

        _changes.Select(change => change.Kind).Should().Equal(FileChangeKind.Missing, FileChangeKind.Restored);
        _changes[1].Text.Should().Be(original);
    }

    [Fact]
    public void EventsAboutOtherFiles_AreIgnored() {
        using var watcher = Watcher();
        var other = Path.Combine(_folder, "other.md");

        File.WriteAllText(other, "Other.\n", _utf8);
        Created(watcher, other);
        Changed(watcher, other);
        Renamed(watcher, other, Path.Combine(_folder, "another.md"));

        _timer.Restarts.Should().Be(0);
        _changes.Should().BeEmpty();
    }

    [Fact]
    public void EventPath_IsMatchedIgnoringCase() {
        using var watcher = Watcher();

        File.WriteAllText(_path, "# Plan\n\nSecond.\n", _utf8);
        Changed(watcher, Path.Combine(_folder, "PLAN.MD"));
        _timer.Fire();

        _changes.Should().ContainSingle();
    }

    [Fact]
    public void LockedFile_IsLookedAtAgainLater() {
        var attempts = 0;
        using var watcher = Watcher(path => ++attempts == 1 ? throw new IOException("Locked") : "Changed.\n");

        Changed(watcher);
        _timer.Fire();

        _changes.Should().BeEmpty();
        _timer.IsPending.Should().BeTrue();

        _timer.Fire();

        _changes.Should().ContainSingle();
        _changes[0].Text.Should().Be("Changed.\n");
    }

    [Fact]
    public void UnreadableFile_IsLookedAtLaterEachTime_ThenLeftUntilTheNextEvent() {
        var attempts = 0;
        using var watcher = Watcher(_ => {
            attempts++;
            throw new IOException("Access denied");
        });

        Changed(watcher);
        var delays = new List<TimeSpan>();

        while (_timer.IsPending) {
            _timer.Fire();

            if (_timer.IsPending) {
                delays.Add(_timer.Delay);
            }
        }

        attempts.Should().Be(FileWatcher.MaxReadRetries + 1);
        delays.Should().HaveCount(FileWatcher.MaxReadRetries);
        delays.Should().BeInAscendingOrder();
        delays[0].Should().Be(FileWatcher.Debounce);
        delays[^1].Should().Be(FileWatcher.MaxRetryDelay);
        _changes.Should().BeEmpty();

        // The next event about the file starts over.
        Changed(watcher);
        _timer.Delay.Should().Be(FileWatcher.Debounce);
        _timer.Fire();
        attempts.Should().Be(FileWatcher.MaxReadRetries + 2);
        _timer.Delay.Should().Be(FileWatcher.Debounce);
    }

    [Fact]
    public void LegacyFileSavedUnchanged_ReadWithTheDocumentLanguage_ProducesNoReload() {
        var windows1251 = CodePagesEncodingProvider.Instance.GetEncoding(1251)!;
        var windows1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;
        var bytes = windows1251.GetBytes("Абзац.\n");
        File.WriteAllBytes(_path, bytes);

        // How the window opens it: the document language's code page before the ANSI one.
        var asTheWindow = new MarkdownFileOptions(AnsiEncoding: windows1252, DocumentLanguage: "ru");
        var shown = MarkdownFile.Open(_path, asTheWindow).Text;
        shown.Should().Be("Абзац.\n");

        using var watcher = new FileWatcher(_path, _timer, path => FileWatcher.ReadText(path, asTheWindow));
        watcher.FileChanged += (_, e) => _changes.Add(e);
        watcher.Acknowledge(shown);

        // Saved again with the same bytes: only a timestamp changes.
        File.WriteAllBytes(_path, bytes);
        Changed(watcher);
        _timer.Fire();

        _changes.Should().BeEmpty();

        // Decoded as the ANSI code page alone, the same bytes read as another text: a false change.
        FileWatcher.ReadText(_path, asTheWindow with { DocumentLanguage = null }).Should().NotBe(shown);
    }

    [Fact]
    public void ReadText_NeverConvertsTheFile() {
        var bytes = CodePagesEncodingProvider.Instance.GetEncoding(1251)!.GetBytes("Абзац.\n");
        File.WriteAllBytes(_path, bytes);

        FileWatcher.ReadText(_path, new MarkdownFileOptions(ConvertToUtf8: true, DocumentLanguage: "ru"));

        File.ReadAllBytes(_path).Should().Equal(bytes);
    }

    [Fact]
    public void DisposedWatcher_ReportsNothing() {
        var watcher = Watcher();
        File.WriteAllText(_path, "# Plan\n\nSecond.\n", _utf8);
        Changed(watcher);
        watcher.Dispose();

        _timer.Fire();
        Changed(watcher);

        _changes.Should().BeEmpty();
        _timer.Disposed.Should().BeTrue();
    }

    [Fact]
    public void Start_WatchesAnExistingFolder_AndRefusesAMissingOne() {
        using var watcher = new FileWatcher(_path, new FakeTimer());
        using var nowhere = new FileWatcher(Path.Combine(_folder, "missing", "plan.md"), new FakeTimer());

        watcher.Start(null).Should().BeTrue();
        nowhere.Start(null).Should().BeFalse();
    }

    [Fact]
    public void PlainFile_HasNoTarget() {
        using var watcher = new FileWatcher(_path, new FakeTimer());

        watcher.TargetPath.Should().BeNull();
    }

    /// <summary>
    /// A symbolic link in the test folder to a file in a subfolder; <see langword="null"/> when
    /// links cannot be made here (that takes Developer Mode or elevation).
    /// </summary>
    private string? LinkToFileInAnotherFolder(out string target) {
        var targetFolder = Path.Combine(_folder, "real");
        Directory.CreateDirectory(targetFolder);
        target = Path.Combine(targetFolder, "plan.md");
        File.WriteAllText(target, "# Plan\n\nFirst.\n", _utf8);
        var link = Path.Combine(_folder, "link.md");

        try {
            File.CreateSymbolicLink(link, target);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return null;
        }

        return link;
    }

    [Fact]
    public void SymbolicLink_EventAboutItsTarget_IsAChangeReadThroughTheLink() {
        if (LinkToFileInAnotherFolder(out var target) is not { } link) {
            return;
        }

        var read = new List<string>();
        using var watcher = new FileWatcher(link, _timer, path => {
            read.Add(path);

            return FileWatcher.ReadText(path);
        });
        watcher.FileChanged += (_, e) => _changes.Add(e);
        watcher.Acknowledge("# Plan\n\nFirst.\n");
        File.WriteAllText(target, "# Plan\n\nEdited.\n", _utf8);

        watcher.TargetPath.Should().Be(target);
        watcher.OnFileSystemEvent(
            null,
            new FileSystemEventArgs(WatcherChangeTypes.Changed, Path.GetDirectoryName(target)!, "plan.md")
        );
        _timer.Fire();

        _changes.Should().ContainSingle().Which.Text.Should().Be("# Plan\n\nEdited.\n");
        read.Should().Equal(link);
    }

    [Fact]
    public void SymbolicLink_TargetInAnotherFolder_IsWatchedThere() {
        if (LinkToFileInAnotherFolder(out var target) is not { } link) {
            return;
        }

        using var timer = new SignalTimer();
        using var watcher = new FileWatcher(link, timer);
        watcher.Start(null).Should().BeTrue();

        File.WriteAllText(target, "# Plan\n\nEdited.\n", _utf8);

        timer.Restarted.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
    }

    /// <summary>A debounce timer that only signals a restart, from whatever thread made it.</summary>
    private sealed class SignalTimer: IDebounceTimer {
        public ManualResetEventSlim Restarted { get; } = new();

        public void Restart(TimeSpan delay, Action callback) => Restarted.Set();

        public void Stop() { }

        public void Dispose() => Restarted.Dispose();
    }

    /// <summary>A debounce timer the test fires by hand.</summary>
    private sealed class FakeTimer: IDebounceTimer {
        private Action? _pending;

        public int Restarts { get; private set; }

        public TimeSpan Delay { get; private set; }

        public bool Disposed { get; private set; }

        public bool IsPending => _pending is not null;

        public void Restart(TimeSpan delay, Action callback) {
            Restarts++;
            Delay = delay;
            _pending = callback;
        }

        public void Stop() => _pending = null;

        public void Fire() {
            var pending = _pending;
            _pending = null;
            pending?.Invoke();
        }

        public void Dispose() => Disposed = true;
    }
}
