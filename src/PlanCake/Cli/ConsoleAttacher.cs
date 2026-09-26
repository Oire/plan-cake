using System.Runtime.InteropServices;
using System.Text;

namespace Oire.PlanCake.Cli;

/// <summary>
/// The standard output and error of a subcommand. <c>plancake.exe</c> is a Windows (GUI)
/// application, which gets no console of its own: output that is redirected (a pipe, a file,
/// Claude's tools) is written straight to the redirection as UTF-8 without BOM; otherwise the
/// process attaches to the console of the shell that started it, if any.
/// </summary>
/// <remarks>
/// Only for subcommands, <c>--help</c>, <c>--version</c> and parse errors; the window never
/// touches the console. The console is attached only when the standard output is not
/// redirected, so captured output is never taken over by the console. Interactive PowerShell
/// and cmd do not wait for a GUI application, so attached output can appear after the prompt
/// returns; redirected output is always complete.
/// </remarks>
internal sealed class ConsoleAttacher: IDisposable {
    private static readonly UTF8Encoding _utf8 = new(false);

    private readonly uint _previousCodePage;
    private bool _disposed;

    private ConsoleAttacher(TextWriter output, TextWriter error, bool isAttached, uint previousCodePage) {
        Output = output;
        Error = error;
        IsAttached = isAttached;
        _previousCodePage = previousCodePage;
    }

    /// <summary>Where the subcommand writes its output.</summary>
    public TextWriter Output { get; }

    /// <summary>Where the subcommand writes its errors and warnings.</summary>
    public TextWriter Error { get; }

    /// <summary>True when the process attached to its parent's console.</summary>
    public bool IsAttached { get; }

    /// <summary>
    /// Sets up the output and error writers, attaching to the parent's console when the standard
    /// output is not redirected.
    /// </summary>
    public static ConsoleAttacher Attach() {
        var outputRedirected = IsRedirected(NativeMethods.STD_OUTPUT_HANDLE);

        // Opened before attaching, which may replace the standard handles with the console's.
        var error = IsRedirected(NativeMethods.STD_ERROR_HANDLE)
            ? Utf8Writer(Console.OpenStandardError())
            : null;

        if (outputRedirected || !NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS)) {
            return new ConsoleAttacher(
                Utf8Writer(Console.OpenStandardOutput()),
                error ?? Utf8Writer(Console.OpenStandardError()),
                isAttached: false,
                previousCodePage: 0
            );
        }

        var previousCodePage = NativeMethods.GetConsoleOutputCP();

        try {
            // Only possible with a console: the setter fails without one.
            Console.OutputEncoding = _utf8;
        } catch (IOException) {
            previousCodePage = 0;
        }

        return new ConsoleAttacher(Console.Out, error ?? Console.Error, isAttached: true, previousCodePage);
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        Output.Flush();
        Error.Flush();

        if (!IsAttached) {
            Output.Dispose();
            Error.Dispose();

            return;
        }

        // Leave the shell's console as it was found.
        if (_previousCodePage != 0) {
            NativeMethods.SetConsoleOutputCP(_previousCodePage);
        }

        NativeMethods.FreeConsole();
    }

    private static StreamWriter Utf8Writer(Stream stream) => new(stream, _utf8) { AutoFlush = true };

    /// <summary>
    /// True when the standard handle is a file, a pipe or a character device other than a
    /// console (<c>NUL</c>); false when there is none or it is a console.
    /// </summary>
    private static bool IsRedirected(int standardHandle) {
        var handle = NativeMethods.GetStdHandle(standardHandle);

        if (handle == IntPtr.Zero || handle == NativeMethods.INVALID_HANDLE_VALUE) {
            return false;
        }

        return NativeMethods.GetFileType(handle) switch {
            NativeMethods.FILE_TYPE_DISK or NativeMethods.FILE_TYPE_PIPE => true,
            NativeMethods.FILE_TYPE_CHAR => !NativeMethods.GetConsoleMode(handle, out _),
            _ => false,
        };
    }

    private static class NativeMethods {
        public const int STD_OUTPUT_HANDLE = -11;
        public const int STD_ERROR_HANDLE = -12;
        public const uint ATTACH_PARENT_PROCESS = unchecked((uint)-1);
        public const uint FILE_TYPE_DISK = 0x0001;
        public const uint FILE_TYPE_CHAR = 0x0002;
        public const uint FILE_TYPE_PIPE = 0x0003;
        public static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern IntPtr GetStdHandle(int standardHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint GetFileType(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetConsoleMode(IntPtr handle, out uint mode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachConsole(uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FreeConsole();

        [DllImport("kernel32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint GetConsoleOutputCP();

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetConsoleOutputCP(uint codePage);
    }
}
