using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using Oire.PlanCake.Utils;
using Serilog;
using static Oire.PlanCake.Utils.Localization;
using App = Oire.PlanCake.Utils.Constants.App;

namespace Oire.PlanCake.Services;

/// <summary>Why a download was refused or failed.</summary>
internal enum DownloadFailure {
    /// <summary>The text is not an http(s) link.</summary>
    InvalidLink,

    /// <summary>The server answered with a status other than success.</summary>
    HttpStatus,

    /// <summary>The link leads to a web page, not to a Markdown file.</summary>
    WebPage,

    /// <summary>The file is larger than <see cref="MarkdownDownloader.MaxSize"/>.</summary>
    TooLarge,

    /// <summary>The download took longer than <see cref="MarkdownDownloader.Timeout"/>.</summary>
    Timeout,

    /// <summary>The server could not be reached, or the connection broke.</summary>
    Network,

    /// <summary>The file could not be written to the Downloads folder.</summary>
    Save,
}

/// <summary>The outcome of <see cref="MarkdownDownloader.DownloadAsync"/>.</summary>
internal sealed record DownloadResult {
    /// <summary>Where the file was saved, or <see langword="null"/> when the download failed.</summary>
    public string? FilePath { get; private init; }

    /// <summary>Why the download failed, or <see langword="null"/> when it succeeded.</summary>
    public DownloadFailure? Failure { get; private init; }

    /// <summary>What to tell the user about the failure; empty on success.</summary>
    public string Error { get; private init; } = String.Empty;

    public static DownloadResult Saved(string path) => new() { FilePath = path };

    public static DownloadResult Failed(DownloadFailure failure, string error) =>
        new() { Failure = failure, Error = error };
}

/// <summary>
/// Downloads a Markdown file from a link into the user's Downloads folder, so File → Open from
/// link and a link on the clipboard open a local copy that notes can be written to. A GitHub
/// link to a file's page is turned into the link to its raw text first.
/// </summary>
internal sealed class MarkdownDownloader: IDisposable {
    /// <summary>The largest file downloaded: 10 MB, far above any plan.</summary>
    public const long MaxSize = 10 * 1024 * 1024;

    /// <summary>How long the whole download may take.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>The name a file gets when its link has no last path segment.</summary>
    internal const string FallbackName = "download";

    /// <summary>How many bytes are looked at to tell a web page from a text file.</summary>
    private const int SniffLength = 512;

    /// <summary>The known-folder ID of the Downloads folder.</summary>
    private static readonly Guid _folderIdDownloads = new("374DE290-123F-4565-9164-39C4925E467B");

    private readonly HttpClient _client;
    private readonly Func<string> _downloadsFolder;
    private readonly TimeSpan _timeout;

    /// <summary>A downloader that goes to the network and saves into the user's Downloads folder.</summary>
    public MarkdownDownloader()
        : this(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All }, DownloadsFolder) { }

    /// <param name="handler">Sends the requests; tests pass one that never touches the network.</param>
    /// <param name="downloadsFolder">The folder the file is saved into, asked for at each download.</param>
    /// <param name="timeout">
    /// How long the whole download may take; <see langword="null"/> for <see cref="Timeout"/>.
    /// </param>
    internal MarkdownDownloader(HttpMessageHandler handler, Func<string> downloadsFolder, TimeSpan? timeout = null) {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(downloadsFolder);

        // The timeout is a cancellation of the whole download, body included: HttpClient's own
        // stops counting once the headers are in.
        _client = new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        var version = typeof(MarkdownDownloader).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(App.Name, version));
        _downloadsFolder = downloadsFolder;
        _timeout = timeout ?? Timeout;
    }

    /// <summary>
    /// Downloads <paramref name="url"/> and saves it in the Downloads folder under the last
    /// segment of the link, always as Markdown (see <see cref="FileNameFor"/>; <c> (2)</c>,
    /// <c> (3)</c>… when the name is taken, as browsers do), marked as coming from the internet.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancels the download; an <see cref="OperationCanceledException"/> follows.
    /// </param>
    /// <returns>Where the file was saved, or why it was not.</returns>
    public async Task<DownloadResult> DownloadAsync(string url, CancellationToken cancellationToken = default) {
        if (!UrlHelper.IsValidHttpUrl(url, out var trimmed)) {
            return DownloadResult.Failed(
                DownloadFailure.InvalidLink,
                _("Please enter a valid link starting with http:// or https://.")
            );
        }

        var uri = RewriteGitHubBlob(new Uri(trimmed));
        Log.Information("Downloading {Url} from {Uri}", UrlHelper.ForLog(trimmed), UrlHelper.ForLog(uri));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        byte[] content;

        try {
            using var response = await _client
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode) {
                Log.Warning(
                    "Download of {Uri} refused: HTTP {Status}",
                    UrlHelper.ForLog(uri), (int)response.StatusCode
                );

                return DownloadResult.Failed(
                    DownloadFailure.HttpStatus,
                    _("The download failed: the server answered {0} {1}.", (int)response.StatusCode, StatusText(response))
                );
            }

            if (IsHtmlMediaType(response.Content.Headers.ContentType?.MediaType)) {
                Log.Warning("Download of {Uri} refused: a web page", UrlHelper.ForLog(uri));

                return WebPage();
            }

            if (response.Content.Headers.ContentLength > MaxSize) {
                Log.Warning(
                    "Download of {Uri} refused: {Length} bytes",
                    UrlHelper.ForLog(uri), response.Content.Headers.ContentLength
                );

                return TooLarge();
            }

            var body = await ReadLimitedAsync(response.Content, timeout.Token).ConfigureAwait(false);

            if (body is null) {
                Log.Warning("Download of {Uri} stopped: over {Max} bytes", UrlHelper.ForLog(uri), MaxSize);

                return TooLarge();
            }

            content = body;
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            Log.Warning("Download of {Uri} timed out", UrlHelper.ForLog(uri));

            return DownloadResult.Failed(
                DownloadFailure.Timeout,
                _("The download took longer than {0} seconds and was stopped.", (int)_timeout.TotalSeconds)
            );
        } catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException) {
            // A connection that drops while the body comes in is an IOException (HttpIOException),
            // a corrupt compressed body an InvalidDataException.
            Log.Warning(ex, "Download of {Uri} failed", UrlHelper.ForLog(uri));

            return DownloadResult.Failed(DownloadFailure.Network, _("The download failed: {0}", ex.Message));
        }

        // A web page served as plain text is still a web page.
        if (LooksLikeHtml(content)) {
            Log.Warning("Download of {Uri} refused: the content is a web page", UrlHelper.ForLog(uri));

            return WebPage();
        }

        var folder = _downloadsFolder();

        try {
            var path = SaveUnique(folder, FileNameFor(uri), content);
            MarkFromInternet(path, uri, new Uri(trimmed));
            Log.Information("Downloaded {Uri} to {Path} ({Length} bytes)", UrlHelper.ForLog(uri), path, content.Length);

            return DownloadResult.Saved(path);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException) {
            Log.Error(ex, "Unable to save the download of {Uri} to {Folder}", UrlHelper.ForLog(uri), folder);

            return DownloadResult.Failed(DownloadFailure.Save, _("Unable to save the file to {0}: {1}", folder, ex.Message));
        }
    }

    /// <summary>
    /// A GitHub link to a file's page, <c>github.com/&lt;owner&gt;/&lt;repo&gt;/blob/&lt;ref&gt;/&lt;path&gt;</c>,
    /// as the link to the file's raw text on <c>raw.githubusercontent.com</c>; any other link as it is.
    /// The query (<c>?plain=1</c>) and the anchor (<c>#L10</c>) belong to the page and are dropped.
    /// </summary>
    internal static Uri RewriteGitHubBlob(Uri uri) {
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri
            || !(uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("www.github.com", StringComparison.OrdinalIgnoreCase))) {
            return uri;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments is not [var owner, var repo, "blob", _, _, ..]) {
            return uri;
        }

        return new Uri($"https://raw.githubusercontent.com/{owner}/{repo}/{String.Join('/', segments[3..])}");
    }

    /// <summary>
    /// The file name a download of <paramref name="uri"/> is saved under: the link's last path
    /// segment, unescaped, with characters Windows does not allow in a name replaced, and
    /// <c>.md</c> added unless it already ends in <c>.md</c> or <c>.markdown</c>. A link to
    /// <c>tool.bat</c> is saved as <c>tool.bat.md</c>, so that no download is ever a program or a
    /// script that opening it, or following a link to it, would run.
    /// </summary>
    internal static string FileNameFor(Uri uri) {
        ArgumentNullException.ThrowIfNull(uri);

        var segment = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? String.Empty;
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(Uri.UnescapeDataString(segment).Select(c => invalid.Contains(c) ? '_' : c).ToArray())
            .Trim()
            .TrimEnd('.');

        if (name.Length == 0) {
            name = FallbackName;
        }

        return LinkResolver.IsMarkdownPath(name) ? name : name + ".md";
    }

    /// <summary>
    /// Writes <paramref name="content"/> to <paramref name="fileName"/> in <paramref name="folder"/>,
    /// or, when that name is taken, to the first free one of <c>name (2).md</c>, <c>name (3).md</c>….
    /// A name is claimed by creating the file, so two downloads at once never share one.
    /// </summary>
    /// <returns>The full path written.</returns>
    internal static string SaveUnique(string folder, string fileName, byte[] content) {
        Directory.CreateDirectory(folder);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        for (var number = 1; ; number++) {
            var name = number == 1 ? fileName : $"{stem} ({number}){extension}";
            var path = Path.Combine(folder, name);

            if (File.Exists(path) || Directory.Exists(path)) {
                continue;
            }

            try {
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                stream.Write(content);

                return path;
            } catch (IOException) when (File.Exists(path) && number < 10_000) {
                // Taken between the check and the write: try the next number.
            }
        }
    }

    /// <summary>
    /// Marks the file as downloaded from the internet, as browsers do: a <c>Zone.Identifier</c>
    /// stream with the Internet zone (3) and where it came from, which Windows and other programs
    /// read before they trust the file. A volume without alternate data streams (FAT32, some
    /// network shares) cannot hold the mark; that is logged, and the download stays.
    /// </summary>
    internal static void MarkFromInternet(string path, Uri host, Uri referrer) {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(referrer);

        try {
            File.WriteAllText(path + ":Zone.Identifier", ZoneIdentifier(host, referrer), Encoding.ASCII);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) {
            Log.Warning(ex, "Unable to mark {Path} as downloaded from the internet", path);
        }
    }

    /// <summary>The content of the <c>Zone.Identifier</c> stream for a file from <paramref name="host"/>.</summary>
    internal static string ZoneIdentifier(Uri host, Uri referrer) {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(referrer);

        return $"[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl={referrer.AbsoluteUri}\r\nHostUrl={host.AbsoluteUri}\r\n";
    }

    /// <summary>
    /// The user's Downloads folder, wherever they moved it; <c>%USERPROFILE%\Downloads</c> when
    /// Windows cannot say.
    /// </summary>
    public static string DownloadsFolder() {
        try {
            var result = NativeMethods.SHGetKnownFolderPath(_folderIdDownloads, 0, IntPtr.Zero, out var pointer);

            try {
                if (result == 0 && Marshal.PtrToStringUni(pointer) is { Length: > 0 } path) {
                    return path;
                }

                Log.Warning("SHGetKnownFolderPath(Downloads) failed: 0x{Result:X8}", result);
            } finally {
                Marshal.FreeCoTaskMem(pointer);
            }
        } catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) {
            Log.Warning(ex, "SHGetKnownFolderPath is not available");
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    public void Dispose() => _client.Dispose();

    private static DownloadResult WebPage() =>
        DownloadResult.Failed(DownloadFailure.WebPage, _("This link leads to a web page, not a Markdown file."));

    private static DownloadResult TooLarge() =>
        DownloadResult.Failed(
            DownloadFailure.TooLarge,
            _("The file is larger than {0} MB, the most PlanCake downloads.", MaxSize / (1024 * 1024))
        );

    private static string StatusText(HttpResponseMessage response) =>
        String.IsNullOrWhiteSpace(response.ReasonPhrase) ? response.StatusCode.ToString() : response.ReasonPhrase;

    private static bool IsHtmlMediaType(string? mediaType) =>
        String.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase)
        || String.Equals(mediaType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the text starts, after a byte order mark and white space, like an HTML document.</summary>
    internal static bool LooksLikeHtml(byte[] content) {
        ArgumentNullException.ThrowIfNull(content);

        var start = Encoding.UTF8.GetString(content, 0, Math.Min(content.Length, SniffLength))
            .TrimStart('﻿', ' ', '\t', '\r', '\n');

        return start.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase)
            || start.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The body, or <see langword="null"/> once it grows past <see cref="MaxSize"/>.</summary>
    private static async Task<byte[]?> ReadLimitedAsync(HttpContent content, CancellationToken cancellationToken) {
        using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;

        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0) {
            if (buffer.Length + read > MaxSize) {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static class NativeMethods {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int SHGetKnownFolderPath(
            [MarshalAs(UnmanagedType.LPStruct)] Guid folderId,
            uint flags,
            IntPtr token,
            out IntPtr path
        );
    }
}
