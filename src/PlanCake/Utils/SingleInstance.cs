using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Serilog;

namespace Oire.PlanCake.Utils;

/// <summary>How <see cref="SingleInstance.TryClaim"/> ended.</summary>
internal enum ClaimOutcome {
    /// <summary>The file is this window's now: it may open it.</summary>
    Registered,

    /// <summary>Another window shows the file and was asked to come to the front.</summary>
    ActivatedOther,

    /// <summary>Another window holds the file but did not answer: the file must not be opened here.</summary>
    Unavailable,
}

/// <summary>
/// One window per file (Task 10 of the plan). The window showing a file owns a named pipe named
/// after the file's normalized full path; a second attempt to open the same file, from another
/// PlanCake process or from another window's link or history, connects to that pipe, asks the
/// owner to come to the front, and opens nothing itself.
/// </summary>
internal sealed class SingleInstance: IDisposable {
    /// <summary>The one request the pipe understands.</summary>
    internal const string ActivateRequest = "activate";

    /// <summary>The owner's answer once it has taken the request.</summary>
    internal const string Acknowledgement = "ok";

    private const int ErrorFileNotFound = 2;

    /// <summary>The buffer <see cref="FinalPath"/> starts with; a longer path gets a larger one.</summary>
    private const int MaxPathLength = 260;

    private const string LongPathPrefix = @"\\?\";
    private const string UncPrefix = @"\\?\UNC\";

    /// <summary>How many times <see cref="TryClaim"/> tries to register, then to activate the owner.</summary>
    private const int ClaimAttempts = 3;
    private static readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(2);
    private static readonly UTF8Encoding _utf8 = new(false);

    private readonly NamedPipeServerStream _server;
    private readonly Action _onActivate;
    private readonly ListenerHooks? _hooks;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _listening;
    private bool _disposed;

    private SingleInstance(string path, NamedPipeServerStream server, Action onActivate, ListenerHooks? hooks) {
        Path = path;
        _server = server;
        _onActivate = onActivate;
        _hooks = hooks;
        _listening = Task.Run(ListenAsync);
    }

    /// <summary>
    /// Points in the listener's loop where tests step in, so they know which state the pipe is in
    /// instead of guessing it with a sleep.
    /// </summary>
    /// <param name="BeforeWait">Runs on the listener just before each wait for a connection.</param>
    /// <param name="Waiting">Runs on the listener once each wait for a connection has begun.</param>
    internal sealed record ListenerHooks(Action? BeforeWait = null, Action? Waiting = null);

    /// <summary>The full path of the file this registration is for.</summary>
    public string Path { get; }

    /// <summary>
    /// The path as it names a pipe: the file's final path when it exists (see
    /// <see cref="FinalPath"/>), else the full path (<c>..</c> resolved); without a trailing
    /// separator, and upper-cased, since Windows paths are case-insensitive.
    /// </summary>
    public static string NormalizePath(string path) {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var fullPath = System.IO.Path.GetFullPath(path);

        return System.IO.Path.TrimEndingDirectorySeparator(FinalPath(fullPath) ?? fullPath).ToUpperInvariant();
    }

    /// <summary>
    /// The path Windows resolves <paramref name="fullPath"/> to: through junctions, symbolic links
    /// and subst drives, with 8.3 names made long, so that every way to name one file names one
    /// pipe. Not the file ID: notes are written through <see cref="File.Replace(String, String, String)"/>,
    /// which gives the file a new one. Two hard links to one file still name two pipes, and a
    /// <c>\\localhost\c$</c> path stays a network path; the check that the file on disk is the
    /// text the window last rendered (<c>StaleFileException</c>) still keeps a second window on
    /// such a path from writing over the first one's notes.
    /// </summary>
    /// <returns>The final path, or <see langword="null"/> when the file cannot be opened (it does not exist, say).</returns>
    internal static string? FinalPath(string fullPath) {
        // No access asked for, only a handle: it opens whoever else has the file open.
        using var handle = NativeMethods.CreateFile(
            fullPath,
            0,
            FileShare.ReadWrite | FileShare.Delete,
            IntPtr.Zero,
            FileMode.Open,
            0,
            IntPtr.Zero
        );

        if (handle.IsInvalid) {
            return null;
        }

        var buffer = new char[MaxPathLength];

        while (true) {
            var length = NativeMethods.GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Length, 0);

            if (length == 0) {
                Log.Debug("No final path for {Path}: error {Error}", fullPath, Marshal.GetLastPInvokeError());
                return null;
            }

            // A buffer too small gets back the length it needs, the terminating null included.
            if (length >= buffer.Length) {
                buffer = new char[length];
                continue;
            }

            var finalPath = new string(buffer, 0, (int)length);

            if (finalPath.StartsWith(UncPrefix, StringComparison.OrdinalIgnoreCase)) {
                return @"\\" + finalPath[UncPrefix.Length..];
            }

            return finalPath.StartsWith(LongPathPrefix, StringComparison.Ordinal)
                ? finalPath[LongPathPrefix.Length..]
                : finalPath;
        }
    }

    /// <summary>The pipe name for <paramref name="path"/>: <c>PlanCake-&lt;SHA-256 of the normalized path&gt;</c>.</summary>
    public static string PipeName(string path) => PipeNameOfNormalized(NormalizePath(path));

    private static string PipeNameOfNormalized(string normalizedPath) =>
        $"PlanCake-{Convert.ToHexString(SHA256.HashData(_utf8.GetBytes(normalizedPath)))}";

    /// <summary>
    /// Registers this window as the one showing <paramref name="path"/>.
    /// <paramref name="onActivate"/> runs on a background thread when another attempt to open the
    /// file asks this window to come to the front.
    /// </summary>
    /// <returns>The registration, or <see langword="null"/> when another window already holds it.</returns>
    public static SingleInstance? TryRegister(string path, Action onActivate) => TryRegister(path, onActivate, null);

    /// <inheritdoc cref="TryRegister(String, Action)"/>
    /// <param name="path">The file to register.</param>
    /// <param name="onActivate">Runs when another attempt to open the file asks this window to come to the front.</param>
    /// <param name="hooks">Where tests step into the listener; <see langword="null"/> outside tests.</param>
    internal static SingleInstance? TryRegister(string path, Action onActivate, ListenerHooks? hooks) {
        ArgumentNullException.ThrowIfNull(onActivate);
        string normalized;

        try {
            normalized = NormalizePath(path);
        } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) {
            Log.Warning(ex, "Unable to register {Path} for one window per file", path);
            return null;
        }

        var name = PipeNameOfNormalized(normalized);

        try {
            // FirstPipeInstance: the creation fails when the pipe exists, which is the whole test.
            var server = new NamedPipeServerStream(
                name,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance
            );

            return new SingleInstance(normalized, server, onActivate, hooks);
        } catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) {
            Log.Information("{Path} is already registered by another window", path);
            return null;
        }
    }

    /// <summary>
    /// Claims <paramref name="path"/> for this window before it opens the file, so that two
    /// windows never write the same file: registering is the test, and whoever registers first
    /// is the only window showing it. When another window already holds it, that window is
    /// asked to come to the front instead.
    /// </summary>
    /// <param name="path">The file to claim.</param>
    /// <param name="onActivate">As for <see cref="TryRegister"/>.</param>
    /// <param name="registration">The registration when the outcome is <see cref="ClaimOutcome.Registered"/>, else <see langword="null"/>.</param>
    /// <param name="timeout">As for <see cref="TryActivate"/>.</param>
    public static ClaimOutcome TryClaim(
        string path,
        Action onActivate,
        out SingleInstance? registration,
        TimeSpan? timeout = null
    ) {
        for (var attempt = 0; attempt < ClaimAttempts; attempt++) {
            registration = TryRegister(path, onActivate);

            if (registration is not null) {
                return ClaimOutcome.Registered;
            }

            if (TryActivate(path, timeout)) {
                return ClaimOutcome.ActivatedOther;
            }

            // The owner let the file go between the two (it closed, or moved to another file),
            // or was busy: try again.
        }

        registration = null;
        Log.Warning("{Path} is held by another window that does not answer", path);

        return ClaimOutcome.Unavailable;
    }

    /// <summary>
    /// Asks the window showing <paramref name="path"/>, if there is one, to come to the front.
    /// </summary>
    /// <returns>True when a window took the request; false when none shows the file.</returns>
    public static bool TryActivate(string path, TimeSpan? timeout = null) {
        string name;

        try {
            name = PipeName(path);
        } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) {
            Log.Debug(ex, "No window per file check for {Path}", path);
            return false;
        }

        var wait = timeout ?? _defaultTimeout;

        // Connect(timeout) would spin for the whole timeout while no pipe exists: ask Windows first,
        // which answers at once when there is none (and waits while the owner is busy).
        if (!NativeMethods.WaitNamedPipe($@"\\.\pipe\{name}", (uint)wait.TotalMilliseconds)) {
            var error = Marshal.GetLastPInvokeError();

            if (error != ErrorFileNotFound) {
                Log.Debug("No window took {Path}: WaitNamedPipe error {Error}", path, error);
            }

            return false;
        }

        using var cancellation = new CancellationTokenSource(wait);
        var client = new NamedPipeClientStream(
            ".",
            name,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
        );

        try {
            // Here, on the calling thread, which waits anyway: ConnectAsync would block a thread
            // pool thread for the wait, and the window answering may be in this process and need it.
            client.Connect(wait);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException) {
            client.Dispose();
            Log.Warning(ex, "The window showing {Path} did not take the connection", path);
            return false;
        }

        // Off the calling thread: the UI thread's synchronization context must not be captured.
        return Task.Run(() => ActivateAsync(client, cancellation.Token)).GetAwaiter().GetResult();
    }

    /// <summary>Asks the window at the other end of <paramref name="client"/> to come to the front; disposes the client.</summary>
    private static async Task<bool> ActivateAsync(NamedPipeClientStream client, CancellationToken token) {
        await using var connection = client;

        try {
            // The owner may take the foreground only if the process that has it allows so.
            if (NativeMethods.GetNamedPipeServerProcessId(client.SafePipeHandle, out var processId)) {
                NativeMethods.AllowSetForegroundWindow(processId);
            }

            await using var writer = new StreamWriter(client, _utf8, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client, _utf8, false, leaveOpen: true);
            await writer.WriteLineAsync(ActivateRequest.AsMemory(), token).ConfigureAwait(false);
            var answer = await reader.ReadLineAsync(token).ConfigureAwait(false);

            return answer == Acknowledgement;
        } catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException or TimeoutException) {
            Log.Warning(ex, "The window showing the file did not answer");
            return false;
        }
    }

    private async Task ListenAsync() {
        var token = _cancellation.Token;
        var failures = 0;

        while (!token.IsCancellationRequested) {
            var connected = false;

            try {
                _hooks?.BeforeWait?.Invoke();
                var waiting = _server.WaitForConnectionAsync(token);
                _hooks?.Waiting?.Invoke();
                await waiting.ConfigureAwait(false);
                connected = true;
                await ServeAsync(token).ConfigureAwait(false);
                failures = 0;
            } catch (OperationCanceledException) when (token.IsCancellationRequested) {
                break;
            } catch (ObjectDisposedException) when (token.IsCancellationRequested) {
                break;
            } catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidOperationException) {
                failures++;

                // Only the first failure in a row is worth a warning: the rest would flood the log.
                if (failures == 1) {
                    Log.Warning(ex, "A request on the pipe of {Path} failed", Path);
                } else {
                    Log.Debug(ex, "A request on the pipe of {Path} failed again ({Failures} in a row)", Path, failures);
                }
            } finally {
                // A client that left without a word leaves the pipe broken, or, when it came and went
                // before the wait took it, still holding its closed end: either way the pipe has to
                // be disconnected, or every later wait for a connection fails at once.
                if (connected) {
                    Disconnect();
                } else if (!token.IsCancellationRequested) {
                    ResetUnconnected();
                }
            }

            // A single failure is a client that left without a word, and the next client may be
            // waiting already: listen again at once, on this thread. Only failures in a row mean a
            // pipe that is stuck, and only those wait.
            if (failures > 1 && !await BackOffAsync(failures - 1, token).ConfigureAwait(false)) {
                break;
            }
        }
    }

    /// <summary>
    /// Waits a little longer after each repeated failure in a row (up to a second), so that a pipe
    /// in a state it cannot leave never turns the listener into a busy loop.
    /// </summary>
    /// <param name="repeats">The failures in a row after the first one, from 1.</param>
    /// <param name="token">Stops the wait when the registration is being disposed.</param>
    /// <returns>False when the registration is being disposed.</returns>
    private static async Task<bool> BackOffAsync(int repeats, CancellationToken token) {
        var delay = TimeSpan.FromMilliseconds(Math.Min(1000, 50 * (1 << Math.Min(repeats - 1, 5))));

        try {
            await Task.Delay(delay, token).ConfigureAwait(false);
            return true;
        } catch (OperationCanceledException) {
            return false;
        }
    }

    private async Task ServeAsync(CancellationToken token) {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(_defaultTimeout);

        using var reader = new StreamReader(_server, _utf8, false, leaveOpen: true);
        var request = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);

        if (request != ActivateRequest) {
            Log.Warning("Unknown request on the pipe of {Path}: {Request}", Path, request);
            return;
        }

        Log.Information("Another attempt to open {Path}: activating this window", Path);
        _onActivate();

        await using var writer = new StreamWriter(_server, _utf8, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(Acknowledgement.AsMemory(), timeout.Token).ConfigureAwait(false);

        // Disconnecting throws away what the client has not read yet, so wait for the client to
        // close its end, which it does once it has read the answer. Not with WaitForPipeDrain:
        // it blocks a thread pool thread, and a client in this same process (another window)
        // needs one to read the answer at all; on a small or busy pool the two then wait on each
        // other until the client gives up.
        try {
            await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
        } catch (OperationCanceledException) when (!token.IsCancellationRequested) {
            Log.Debug("The client on the pipe of {Path} kept it open after the answer", Path);
        }
    }

    /// <summary>
    /// Ends the current connection, whether the pipe is still connected or broken by a client that
    /// closed its end, so the pipe can wait for the next one. Called only once a connection was made:
    /// the pipe then is neither waiting to connect nor disconnected, the two states it refuses.
    /// </summary>
    private void Disconnect() {
        try {
            if (!_disposed) {
                _server.Disconnect();
            }
        } catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException) {
            Log.Debug(ex, "Unable to disconnect the pipe of {Path}", Path);
        }
    }

    /// <summary>
    /// Frees the pipe after a wait for a connection failed, as it does ("The pipe is being closed")
    /// when a client connected and closed before the wait began. The stream still counts itself as
    /// waiting to connect and so refuses <see cref="NamedPipeServerStream.Disconnect"/>; Windows
    /// does not, and a pipe that has no client ignores the call.
    /// </summary>
    private void ResetUnconnected() {
        try {
            if (!_disposed && !NativeMethods.DisconnectNamedPipe(_server.SafePipeHandle)) {
                Log.Debug(
                    "Unable to reset the pipe of {Path}: DisconnectNamedPipe error {Error}",
                    Path,
                    Marshal.GetLastPInvokeError()
                );
            }
        } catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException) {
            Log.Debug(ex, "Unable to reset the pipe of {Path}", Path);
        }
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        _cancellation.Cancel();

        try {
            _listening.Wait(TimeSpan.FromSeconds(1));
        } catch (AggregateException ex) {
            Log.Debug(ex, "The pipe of {Path} stopped with an error", Path);
        }

        _server.Dispose();
        _cancellation.Dispose();
    }

    private static class NativeMethods {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            FileShare shareMode,
            IntPtr securityAttributes,
            FileMode creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile
        );

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetFinalPathNameByHandleW")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint GetFinalPathNameByHandle(
            SafeFileHandle file,
            [Out] char[] filePath,
            uint filePathLength,
            uint flags
        );

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "WaitNamedPipeW")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WaitNamedPipe(string name, uint timeout);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DisconnectNamedPipe(SafePipeHandle pipe);

        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AllowSetForegroundWindow(uint processId);
    }
}
