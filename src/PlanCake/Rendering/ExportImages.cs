using System.Net;
using System.Text.RegularExpressions;
using Oire.PlanCake.Utils;
using Serilog;

namespace Oire.PlanCake.Rendering;

/// <summary>
/// Puts the local pictures of an exported file inside it, as <c>data:</c> URIs, so the export
/// shows them wherever it is opened and its content security policy needs no <c>file:</c>
/// (which would let a plan's raw HTML make the browser reach another computer's share). Pictures
/// from Markdown (<c>![alt](diagram.png)</c>) and from raw HTML (<c>&lt;img src&gt;</c>) alike;
/// remote pictures (http, https) stay links.
/// </summary>
internal static partial class ExportImages {
    /// <summary>A picture larger than this stays a link, with a line in the log.</summary>
    public const long MaxImageBytes = 10 * 1024 * 1024;

    /// <summary>The types a browser shows in an <c>&lt;img&gt;</c>, by extension.</summary>
    private static readonly Dictionary<string, string> _mediaTypes = new(StringComparer.OrdinalIgnoreCase) {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".avif"] = "image/avif",
        [".bmp"] = "image/bmp",
        [".ico"] = "image/x-icon",
        [".svg"] = "image/svg+xml",
    };

    /// <summary>
    /// The <c>src</c> of an <c>&lt;img&gt;</c> start tag: double-quoted, single-quoted or bare.
    /// Text in code blocks is escaped (<c>&amp;lt;img</c>), so only real tags match.
    /// </summary>
    [GeneratedRegex(
        """(?<before><img\b[^>]*?\ssrc\s*=\s*)(?:"(?<value>[^"]*)"|'(?<value>[^']*)'|(?<value>[^\s"'=<>`]+))""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex ImageSourceRegex();

    /// <summary>
    /// <paramref name="html"/> with every picture that is a readable local file of a known type,
    /// no larger than <see cref="MaxImageBytes"/>, as a <c>data:</c> URI.
    /// </summary>
    /// <param name="html">The rendered body.</param>
    /// <param name="documentFolder">The folder of the Markdown file, which relative paths start from.</param>
    public static string Embed(string html, string? documentFolder) {
        ArgumentNullException.ThrowIfNull(html);

        return ImageSourceRegex().Replace(html, match => {
            var source = WebUtility.HtmlDecode(match.Groups["value"].Value);
            var path = LocalPath(source, documentFolder);

            return path is not null && DataUri(path) is { } dataUri
                ? $"{match.Groups["before"].Value}\"{dataUri}\""
                : match.Value;
        });
    }

    /// <summary>
    /// The local file a picture's <c>src</c> names, or <see langword="null"/> for anything else: a
    /// remote or <c>data:</c> picture, a relative path without a document folder, and a UNC path
    /// on another host than the document's, which is never read (reading it would send that
    /// host the user's Windows credentials).
    /// </summary>
    internal static string? LocalPath(string source, string? documentFolder) {
        var value = source.Trim();

        if (value.Length == 0 || value.StartsWith('#')) {
            return null;
        }

        try {
            string path;

            // A Windows path such as C:\plans\diagram.png parses as a file URI too.
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri)) {
                if (!uri.IsFile) {
                    return null;
                }

                path = uri.LocalPath;
            } else {
                if (String.IsNullOrEmpty(documentFolder)) {
                    return null;
                }

                var end = value.IndexOfAny(['#', '?']);
                var relative = Uri.UnescapeDataString(end >= 0 ? value[..end] : value);

                if (relative.Length == 0) {
                    return null;
                }

                path = Path.GetFullPath(Path.Combine(documentFolder, relative.Replace('/', Path.DirectorySeparatorChar)));
            }

            return LinkResolver.IsReachableWithoutAsking(path, documentFolder) ? path : null;
        } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException
                                         or UriFormatException) {
            return null;
        }
    }

    /// <summary>The file as a <c>data:</c> URI, or <see langword="null"/> when it is not embedded.</summary>
    private static string? DataUri(string path) {
        if (!_mediaTypes.TryGetValue(Path.GetExtension(path), out var mediaType)) {
            Log.Information("Export: {Path} is not a picture type a browser shows; left as a link", path);
            return null;
        }

        try {
            var file = new FileInfo(path);

            if (!file.Exists) {
                Log.Information("Export: picture {Path} not found; left as a link", path);
                return null;
            }

            if (file.Length > MaxImageBytes) {
                Log.Warning(
                    "Export: picture {Path} is {Size} bytes, over {Limit}; left as a link",
                    path, file.Length, MaxImageBytes
                );
                return null;
            }

            return $"data:{mediaType};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}";
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                         or System.Security.SecurityException) {
            Log.Warning(ex, "Export: unable to read picture {Path}; left as a link", path);
            return null;
        }
    }
}
