using System.Runtime.InteropServices;

namespace Oire.PlanCake.Utils;

/// <summary>What following a link in the document does.</summary>
internal enum LinkKind {
    /// <summary>An <c>http</c>, <c>https</c> or <c>mailto</c> link, opened by the system.</summary>
    External,

    /// <summary>An anchor in the document itself (<c>#…</c>), which the page handles.</summary>
    InPage,

    /// <summary>An existing Markdown file, opened in PlanCake.</summary>
    Markdown,

    /// <summary>
    /// An existing folder, or a file of a passive type (plain text, PDF, a picture; see
    /// <see cref="LinkResolver.IsPassiveDocument"/>), opened by the system.
    /// </summary>
    OtherFile,

    /// <summary>
    /// Any other existing file: a program or a script (<c>.exe</c>, <c>.bat</c>, <c>.py</c>,
    /// <c>.lnk</c>, …), or a file whose default action PlanCake cannot vouch for (<c>.rdp</c>,
    /// <c>.iso</c>, <c>.docm</c>, …). Never opened from a link; shown in File Explorer on request.
    /// </summary>
    Program,

    /// <summary>A local target that does not exist.</summary>
    Missing,

    /// <summary>A link PlanCake does not follow: another scheme (<c>javascript:</c>, …), or junk.</summary>
    Unsupported,
}

/// <summary>Where a link leads.</summary>
/// <param name="Kind">What following it does.</param>
/// <param name="Target">
/// The absolute URL (<see cref="LinkKind.External"/>), the anchor without its <c>#</c>
/// (<see cref="LinkKind.InPage"/>), the full local path (<see cref="LinkKind.Markdown"/>,
/// <see cref="LinkKind.OtherFile"/>, <see cref="LinkKind.Program"/>, <see cref="LinkKind.Missing"/>),
/// or the raw <c>href</c> (<see cref="LinkKind.Unsupported"/>).
/// </param>
internal sealed record LinkTarget(LinkKind Kind, string Target);

/// <summary>
/// Decides what a link in the document leads to. The page hands the host the raw <c>href</c>;
/// a relative target resolves against the folder of the open Markdown file.
/// </summary>
/// <remarks>
/// A plan may come from anywhere (a download, an AI assistant), and its link text can say
/// anything. So a link opens only a folder or a passive document, never a program or any other
/// file the system's default action might run (<see cref="LinkKind.Program"/>), and a link to
/// another computer's share (<c>\\host\share\…</c>, <c>file://host/…</c>) is not followed, nor
/// even checked for existence, since reaching the host sends it the user's Windows credentials;
/// only the share the open document is on itself is allowed.
/// </remarks>
internal static class LinkResolver {
    private static readonly string[] _externalSchemes = [Uri.UriSchemeHttp, Uri.UriSchemeHttps, Uri.UriSchemeMailto];

    /// <summary>
    /// Extensions Windows runs rather than opens, beyond what <c>PATHEXT</c> and
    /// <c>AssocIsDangerous</c> say on the machine at hand: the list does not depend on them.
    /// </summary>
    private static readonly HashSet<string> _runnableExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".exe", ".com", ".bat", ".cmd", ".scr", ".pif", ".cpl", ".msi", ".msp", ".mst", ".msc",
        ".lnk", ".url", ".website", ".appref-ms", ".application", ".hta", ".js", ".jse", ".vbs",
        ".vbe", ".wsf", ".wsh", ".ws", ".wsc", ".sct", ".ps1", ".psm1", ".psd1", ".ps1xml",
        ".psc1", ".reg", ".inf", ".scf", ".jar", ".gadget", ".settingcontent-ms", ".library-ms",
        ".search-ms", ".searchconnector-ms", ".diagcab", ".xll", ".xbap", ".chm", ".appx",
        ".appxbundle", ".msix", ".msixbundle", ".py", ".pyw", ".pyz", ".sh", ".bash", ".rb", ".pl",
        ".php", ".tcl", ".lua", ".rdp", ".theme", ".themepack", ".deskthemepack", ".iso", ".img",
        ".vhd", ".vhdx",
    };

    /// <summary>
    /// File types a link opens with the system: documents and pictures whose default action only
    /// shows them. Anything else might run something, whatever the machine says about it.
    /// </summary>
    private static readonly HashSet<string> _passiveExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".txt", ".log", ".pdf", ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff",
        ".ico",
    };

    /// <summary>
    /// True when <paramref name="path"/> is a file a link may open with the system: plain text,
    /// a PDF or a picture, going by its extension.
    /// </summary>
    public static bool IsPassiveDocument(string? path) {
        var extension = Path.GetExtension(path);

        return !String.IsNullOrEmpty(extension) && _passiveExtensions.Contains(extension);
    }

    /// <summary>
    /// True when the system would run <paramref name="path"/> instead of opening it: a program, a
    /// script, a shortcut or an installer, going by its extension.
    /// </summary>
    public static bool IsRunnable(string? path) {
        var extension = Path.GetExtension(path);

        if (String.IsNullOrEmpty(extension)) {
            return false;
        }

        if (_runnableExtensions.Contains(extension)) {
            return true;
        }

        var pathExt = Environment.GetEnvironmentVariable("PATHEXT") ?? String.Empty;

        if (pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(extension, StringComparer.OrdinalIgnoreCase)) {
            return true;
        }

        try {
            return NativeMethods.AssocIsDangerous(extension);
        } catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) {
            return false;
        }
    }

    /// <summary>
    /// The program Windows opens <paramref name="path"/>'s type with by default (the full path of
    /// its executable), or <see langword="null"/> when there is none or it cannot be told.
    /// </summary>
    public static string? DefaultProgramFor(string path) {
        var extension = Path.GetExtension(path);

        if (String.IsNullOrEmpty(extension)) {
            return null;
        }

        try {
            uint length = 0;

            // S_FALSE with the length the answer needs, terminating null included.
            var result = NativeMethods.AssocQueryString(AssocFNone, AssocStrExecutable, extension, null, null, ref length);

            if (result != SFalse || length == 0) {
                return null;
            }

            var buffer = new char[length];
            result = NativeMethods.AssocQueryString(AssocFNone, AssocStrExecutable, extension, null, buffer, ref length);

            if (result != 0) {
                return null;
            }

            var program = new string(buffer).TrimEnd(Char.MinValue);

            return program.Length == 0 ? null : program;
        } catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) {
            return null;
        }
    }

    /// <summary>
    /// True when <paramref name="program"/>, the default program for a file type, is no editor
    /// PlanCake can hand a file to: none at all, or PlanCake itself (this executable, or another
    /// copy of it, which would only bring the window already showing the file to the front).
    /// </summary>
    /// <param name="program">The program's path, from <see cref="DefaultProgramFor"/>.</param>
    /// <param name="ownExecutable">
    /// The path of the running executable (<see cref="Environment.ProcessPath"/>).
    /// </param>
    public static bool IsNoEditor(string? program, string? ownExecutable) {
        if (String.IsNullOrWhiteSpace(program)) {
            return true;
        }

        if (String.IsNullOrEmpty(ownExecutable)) {
            return false;
        }

        return String.Equals(
            Path.GetFileName(program.Trim('"')),
            Path.GetFileName(ownExecutable),
            StringComparison.OrdinalIgnoreCase
        );
    }

    /// <summary>True when <paramref name="path"/> names a Markdown file (<c>.md</c> or <c>.markdown</c>).</summary>
    public static bool IsMarkdownPath(string? path) {
        var extension = Path.GetExtension(path);

        return String.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase)
            || String.Equals(extension, ".markdown", StringComparison.OrdinalIgnoreCase);
    }

    /// <param name="href">The link's <c>href</c> attribute, as written in the source.</param>
    /// <param name="documentFolder">The folder of the open Markdown file, for relative targets.</param>
    /// <param name="exists">
    /// Whether a local path exists; <see langword="null"/> checks the disk for a file or a folder.
    /// </param>
    /// <param name="isFolder">
    /// Whether an existing local path is a folder; <see langword="null"/> checks the disk.
    /// </param>
    public static LinkTarget Resolve(
        string? href,
        string? documentFolder,
        Func<string, bool>? exists = null,
        Func<string, bool>? isFolder = null
    ) {
        exists ??= path => File.Exists(path) || Directory.Exists(path);
        isFolder ??= Directory.Exists;
        var link = href?.Trim() ?? String.Empty;

        if (link.Length == 0) {
            return new LinkTarget(LinkKind.Unsupported, href ?? String.Empty);
        }

        if (link.StartsWith('#')) {
            return new LinkTarget(LinkKind.InPage, link[1..]);
        }

        // A Windows path such as C:\plans\next.md parses as a file URI too.
        if (Uri.TryCreate(link, UriKind.Absolute, out var uri)) {
            if (_externalSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase)) {
                return new LinkTarget(LinkKind.External, uri.AbsoluteUri);
            }

            return uri.IsFile
                ? Local(uri.LocalPath, documentFolder, link, exists, isFolder)
                : new LinkTarget(LinkKind.Unsupported, link);
        }

        if (String.IsNullOrEmpty(documentFolder)) {
            return new LinkTarget(LinkKind.Unsupported, link);
        }

        // other.md#section opens other.md; the anchor and any query are not part of the path.
        var end = link.IndexOfAny(['#', '?']);
        var relative = Uri.UnescapeDataString(end >= 0 ? link[..end] : link);

        if (relative.Length == 0) {
            return new LinkTarget(LinkKind.Unsupported, link);
        }

        try {
            var path = Path.Combine(documentFolder, relative.Replace('/', Path.DirectorySeparatorChar));

            return Local(Path.GetFullPath(path), documentFolder, link, exists, isFolder);
        } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) {
            return new LinkTarget(LinkKind.Unsupported, link);
        }
    }

    private static LinkTarget Local(
        string path,
        string? documentFolder,
        string link,
        Func<string, bool> exists,
        Func<string, bool> isFolder
    ) {
        // Checked before the disk is touched: File.Exists on a UNC path already reaches the host.
        if (!IsReachableWithoutAsking(path, documentFolder)) {
            return new LinkTarget(LinkKind.Unsupported, link);
        }

        if (!exists(path)) {
            return new LinkTarget(LinkKind.Missing, path);
        }

        if (IsMarkdownPath(path)) {
            return new LinkTarget(LinkKind.Markdown, path);
        }

        // An allowlist, not a blocklist: the default action of a type no list names (a .py file
        // with the Python launcher installed, a .sh file with Git for Windows) may run it.
        var opens = !IsRunnable(path) && (IsPassiveDocument(path) || isFolder(path));

        return new LinkTarget(opens ? LinkKind.OtherFile : LinkKind.Program, path);
    }

    /// <summary>
    /// False for a UNC path (<c>\\host\share\…</c>) on another host than the open document's, and
    /// for any device path (<c>\\?\…</c>, <c>\\.\…</c>), which no plan needs.
    /// </summary>
    private static bool IsReachableWithoutAsking(string path, string? documentFolder) {
        var normalized = path.Replace('/', '\\');

        if (!normalized.StartsWith(@"\\", StringComparison.Ordinal)) {
            return true;
        }

        var host = UncHost(normalized);
        var documentHost = documentFolder is null ? null : UncHost(documentFolder.Replace('/', '\\'));

        return host is not null && String.Equals(host, documentHost, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The host of a UNC path, or <see langword="null"/> for anything else (a device path included).</summary>
    private static string? UncHost(string path) {
        if (!path.StartsWith(@"\\", StringComparison.Ordinal) || path.Length < 3 || path[2] is '?' or '.') {
            return null;
        }

        var end = path.IndexOf('\\', 2);
        var host = end < 0 ? path[2..] : path[2..end];

        return host.Length == 0 ? null : host;
    }

    private const uint AssocFNone = 0;
    private const int AssocStrExecutable = 2;
    private const int SFalse = 1;

    private static class NativeMethods {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AssocIsDangerous(string pszAssoc);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "AssocQueryStringW")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int AssocQueryString(
            uint flags,
            int str,
            string pszAssoc,
            string? pszExtra,
            [Out] char[]? pszOut,
            ref uint pcchOut
        );
    }
}
