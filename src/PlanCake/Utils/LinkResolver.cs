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

    /// <summary>Any other existing file or folder, opened by the system.</summary>
    OtherFile,

    /// <summary>
    /// An existing program or script (<c>.exe</c>, <c>.bat</c>, <c>.js</c>, <c>.lnk</c>, …; see
    /// <see cref="LinkResolver.IsRunnable"/>), which the system would run: never opened from a link.
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
/// anything. So a link never runs a program (<see cref="LinkKind.Program"/>), and a link to
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
        ".appxbundle", ".msix", ".msixbundle",
    };

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
    public static LinkTarget Resolve(string? href, string? documentFolder, Func<string, bool>? exists = null) {
        exists ??= path => File.Exists(path) || Directory.Exists(path);
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
                ? Local(uri.LocalPath, documentFolder, link, exists)
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

            return Local(Path.GetFullPath(path), documentFolder, link, exists);
        } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) {
            return new LinkTarget(LinkKind.Unsupported, link);
        }
    }

    private static LinkTarget Local(string path, string? documentFolder, string link, Func<string, bool> exists) {
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

        return new LinkTarget(IsRunnable(path) ? LinkKind.Program : LinkKind.OtherFile, path);
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

    private static class NativeMethods {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AssocIsDangerous(string pszAssoc);
    }
}
