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
/// <see cref="LinkKind.OtherFile"/>, <see cref="LinkKind.Missing"/>), or the raw <c>href</c>
/// (<see cref="LinkKind.Unsupported"/>).
/// </param>
internal sealed record LinkTarget(LinkKind Kind, string Target);

/// <summary>
/// Decides what a link in the document leads to. The page hands the host the raw <c>href</c>;
/// a relative target resolves against the folder of the open Markdown file.
/// </summary>
internal static class LinkResolver {
    private static readonly string[] _externalSchemes = [Uri.UriSchemeHttp, Uri.UriSchemeHttps, Uri.UriSchemeMailto];

    /// <summary>True when <paramref name="path"/> names a Markdown file (<c>.md</c> or <c>.markdown</c>).</summary>
    public static bool IsMarkdownPath(string? path) {
        var extension = Path.GetExtension(path);

        return string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".markdown", StringComparison.OrdinalIgnoreCase);
    }

    /// <param name="href">The link's <c>href</c> attribute, as written in the source.</param>
    /// <param name="documentFolder">The folder of the open Markdown file, for relative targets.</param>
    /// <param name="exists">
    /// Whether a local path exists; <see langword="null"/> checks the disk for a file or a folder.
    /// </param>
    public static LinkTarget Resolve(string? href, string? documentFolder, Func<string, bool>? exists = null) {
        exists ??= path => File.Exists(path) || Directory.Exists(path);
        var link = href?.Trim() ?? string.Empty;

        if (link.Length == 0) {
            return new LinkTarget(LinkKind.Unsupported, href ?? string.Empty);
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
                ? Local(uri.LocalPath, exists)
                : new LinkTarget(LinkKind.Unsupported, link);
        }

        if (string.IsNullOrEmpty(documentFolder)) {
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

            return Local(Path.GetFullPath(path), exists);
        } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) {
            return new LinkTarget(LinkKind.Unsupported, link);
        }
    }

    private static LinkTarget Local(string path, Func<string, bool> exists) {
        if (!exists(path)) {
            return new LinkTarget(LinkKind.Missing, path);
        }

        return new LinkTarget(IsMarkdownPath(path) ? LinkKind.Markdown : LinkKind.OtherFile, path);
    }
}
