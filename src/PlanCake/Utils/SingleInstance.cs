using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Serilog;

namespace Oire.PlanCake.Utils;

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
    private static readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(2);
    private static readonly UTF8Encoding _utf8 = new(false);

    private readonly NamedPipeServerStream _server;
    private readonly Action _onActivate;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _listening;
    private bool _disposed;

    private SingleInstance(string path, NamedPipeServerStream server, Action onActivate) {
        Path = path;
        _server = server;
        _onActivate = onActivate;
        _listening = Task.Run(ListenAsync);
    }

    /// <summary>The full path of the file this registration is for.</summary>
    public string Path { get; }

    /// <summary>
    /// The path as it names a pipe: full (<c>..</c> resolved), without a trailing separator,
    /// upper-cased, since Windows paths are case-insensitive.
    /// </summary>
    public static string NormalizePath(string path) {
        ArgumentException.ThrowIfNullOrEmpty(path);

        return System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path)).ToUpperInvariant();
    }

    /// <summary>The pipe name for <paramref name="path"/>: <c>PlanCake-&lt;SHA-256 of the normalized path&gt;</c>.</summary>
    public static string PipeName(string path) =>
        $"PlanCake-{Convert.ToHexString(SHA256.HashData(_utf8.GetBytes(NormalizePath(path))))}";

    /// <summary>
    /// Registers this window as the one showing <paramref name="path"/>.
    /// <paramref name="onActivate"/> runs on a background thread when another attempt to open the
    /// file asks this window to come to the front.
    /// </summary>
    /// <returns>The registration, or <see langword="null"/> when another window already holds it.</returns>
    public static SingleInstance? TryRegister(string path, Action onActivate) {
        ArgumentNullException.ThrowIfNull(onActivate);
        string name;

        try {
            name = PipeName(path);
        } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) {
            Log.Warning(ex, "Unable to register {Path} for one window per file", path);
            return null;
        }

        try {
            // FirstPipeInstance: the creation fails when the pipe exists, which is the whole test.
            var server = new NamedPipeServerStream(
                name,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance
            );

            return new SingleInstance(NormalizePath(path), server, onActivate);
        } catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) {
            Log.Information("{Path} is already registered by another window", path);
            return null;
        }
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

        // Off the calling thread: the UI thread's synchronization context must not be captured.
        return Task.Run(() => ActivateAsync(name, wait)).GetAwaiter().GetResult();
    }

    private static async Task<bool> ActivateAsync(string name, TimeSpan timeout) {
        using var cancellation = new CancellationTokenSource(timeout);

        try {
            await using var client = new NamedPipeClientStream(
                ".",
                name,
                PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
            );
            await client.ConnectAsync(cancellation.Token).ConfigureAwait(false);

            // The owner may take the foreground only if the process that has it allows so.
            if (NativeMethods.GetNamedPipeServerProcessId(client.SafePipeHandle, out var processId)) {
                NativeMethods.AllowSetForegroundWindow(processId);
            }

            await using var writer = new StreamWriter(client, _utf8, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client, _utf8, false, leaveOpen: true);
            await writer.WriteLineAsync(ActivateRequest.AsMemory(), cancellation.Token).ConfigureAwait(false);
            var answer = await reader.ReadLineAsync(cancellation.Token).ConfigureAwait(false);

            return answer == Acknowledgement;
        } catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException or TimeoutException) {
            Log.Warning(ex, "The window showing the file did not answer");
            return false;
        }
    }

    private async Task ListenAsync() {
        var token = _cancellation.Token;

        while (!token.IsCancellationRequested) {
            try {
                await _server.WaitForConnectionAsync(token).ConfigureAwait(false);
                await ServeAsync(token).ConfigureAwait(false);
            } catch (OperationCanceledException) when (token.IsCancellationRequested) {
                break;
            } catch (ObjectDisposedException) when (token.IsCancellationRequested) {
                break;
            } catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidOperationException) {
                Log.Warning(ex, "A request on the pipe of {Path} failed", Path);
            } finally {
                Disconnect();
            }
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

        // Disconnecting throws away what the client has not read yet.
        _server.WaitForPipeDrain();
    }

    private void Disconnect() {
        try {
            if (!_disposed && _server.IsConnected) {
                _server.Disconnect();
            }
        } catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException) {
            Log.Debug(ex, "Unable to disconnect the pipe of {Path}", Path);
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
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "WaitNamedPipeW")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WaitNamedPipe(string name, uint timeout);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);

        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AllowSetForegroundWindow(uint processId);
    }
}
