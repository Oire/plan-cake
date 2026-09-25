using System.Text;

namespace Oire.PlanCake.Notes;

/// <summary>How <see cref="MarkdownFile"/> reads and writes a file.</summary>
/// <param name="ConvertToUtf8">
/// Rewrite a file that is not valid in its detected encoding as UTF-8 without BOM when opening it,
/// instead of opening it read-only (the Advanced setting of the same name).
/// </param>
/// <param name="AnsiEncoding">
/// The encoding that decodes such a file; <see langword="null"/> for the Windows ANSI code page.
/// Tests pass Windows-1251 explicitly, whatever the machine's code page is.
/// </param>
/// <param name="Retries">How many times a locked file is retried after the first attempt.</param>
/// <param name="RetryDelay">The wait between two attempts; <see langword="null"/> for 200 ms.</param>
internal sealed record MarkdownFileOptions(
    bool ConvertToUtf8 = false,
    Encoding? AnsiEncoding = null,
    int Retries = 5,
    TimeSpan? RetryDelay = null
) {
    public static MarkdownFileOptions Default { get; } = new();
}

/// <summary>
/// A Markdown file on disk: its text, and how to write it back exactly as it was stored (same
/// encoding, BOM and line endings), atomically, per Task 5 of the PlanCake plan.
/// </summary>
/// <remarks>
/// A file is decoded from its BOM (UTF-8, UTF-16 LE or BE) or else as strict UTF-8. A file that
/// is not valid in that encoding is decoded with the Windows ANSI code page and is then either
/// read-only, so PlanCake cannot damage it, or, with <see cref="MarkdownFileOptions.ConvertToUtf8"/>,
/// rewritten once as UTF-8 without BOM.
/// </remarks>
internal sealed class MarkdownFile {
    private static readonly byte[] _utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly byte[] _utf16LeBom = [0xFF, 0xFE];
    private static readonly byte[] _utf16BeBom = [0xFE, 0xFF];
    private static readonly TimeSpan _defaultRetryDelay = TimeSpan.FromMilliseconds(200);

    private static readonly Lazy<Encoding> _systemAnsi = new(() => {
        // Without the provider .NET only knows the Unicode encodings; code page 0 is the system's
        // ANSI code page. Never CurrentCulture: PlanCake sets it to the interface language.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        return Encoding.GetEncoding(0);
    });

    private readonly MarkdownFileOptions _options;

    private MarkdownFile(string path, MarkdownFileOptions options) {
        Path = path;
        _options = options;
    }

    /// <summary>The full path of the file.</summary>
    public string Path { get; }

    /// <summary>The file's text as last read or written.</summary>
    public string Text { get; private set; } = string.Empty;

    /// <summary>The encoding the text is written back with (without its BOM).</summary>
    public Encoding Encoding { get; private set; } = new UTF8Encoding(false);

    /// <summary>True when the file starts with a byte order mark, which is written back.</summary>
    public bool HasBom { get; private set; }

    /// <summary>
    /// The file's dominant line ending (<c>\n</c>, <c>\r\n</c> or <c>\r</c>), which new lines are
    /// written with; <c>\n</c> for a file without any.
    /// </summary>
    public string LineEnding { get; private set; } = "\n";

    /// <summary>
    /// True when the file is not valid in its detected encoding and was decoded with the ANSI code
    /// page without being converted: it is never written.
    /// </summary>
    public bool IsReadOnly { get; private set; }

    /// <summary>
    /// The encoding the file was converted from when it was last read, or <see langword="null"/>;
    /// the caller announces the conversion.
    /// </summary>
    public Encoding? ConvertedFrom { get; private set; }

    /// <summary>Reads the file at <paramref name="path"/>.</summary>
    /// <exception cref="IOException">The file cannot be read, even after the retries.</exception>
    public static MarkdownFile Open(string path, MarkdownFileOptions? options = null) {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var file = new MarkdownFile(System.IO.Path.GetFullPath(path), options ?? MarkdownFileOptions.Default);
        file.Reload();

        return file;
    }

    /// <summary>
    /// Reads the file again, detecting its encoding and line ending afresh (converting it when it
    /// is not valid in its encoding and the options say so), and returns its text.
    /// </summary>
    /// <exception cref="IOException">The file cannot be read, even after the retries.</exception>
    public string Reload() {
        var bytes = WithRetries(() => ReadAllBytes(Path));
        var ansi = _options.AnsiEncoding ?? _systemAnsi.Value;
        var decoded = Decode(bytes, ansi);

        Text = decoded.Text;
        Encoding = decoded.Encoding;
        HasBom = decoded.HasBom;
        LineEnding = DominantLineEnding(decoded.Text);
        IsReadOnly = decoded.IsFallback;
        ConvertedFrom = null;

        if (decoded.IsFallback && _options.ConvertToUtf8) {
            ConvertedFrom = decoded.Encoding;
            Encoding = new UTF8Encoding(false);
            HasBom = false;
            IsReadOnly = false;
            Write(Text);
        }

        return Text;
    }

    /// <summary>
    /// Writes <paramref name="text"/> to the file with its encoding and BOM, atomically: to a
    /// temporary file in the same folder that then replaces the file.
    /// </summary>
    /// <exception cref="ReadOnlyFileException">The file is read-only (see <see cref="IsReadOnly"/>).</exception>
    /// <exception cref="IOException">The file cannot be written, even after the retries.</exception>
    public void Write(string text) {
        ArgumentNullException.ThrowIfNull(text);

        if (IsReadOnly) {
            throw new ReadOnlyFileException(
                $"{Path} is not valid {Encoding.WebName} and is open read-only; it is never written."
            );
        }

        var preamble = HasBom ? Encoding.GetPreamble() : [];
        var body = Encoding.GetBytes(text);
        var bytes = new byte[preamble.Length + body.Length];
        preamble.CopyTo(bytes, 0);
        body.CopyTo(bytes, preamble.Length);

        WithRetries(() => {
            WriteAtomically(Path, bytes);

            return true;
        });

        Text = text;
    }

    /// <summary>The result of decoding a file's bytes.</summary>
    /// <param name="IsFallback">True when the bytes were not valid and were decoded as ANSI.</param>
    internal readonly record struct DecodedText(string Text, Encoding Encoding, bool HasBom, bool IsFallback);

    internal static DecodedText Decode(byte[] bytes, Encoding ansiEncoding) {
        var (encoding, bomLength) = bytes.AsSpan() switch {
            var span when span.StartsWith(_utf8Bom) => ((Encoding)new UTF8Encoding(true, true), _utf8Bom.Length),
            var span when span.StartsWith(_utf16LeBom) => (new UnicodeEncoding(false, true, true), _utf16LeBom.Length),
            var span when span.StartsWith(_utf16BeBom) => (new UnicodeEncoding(true, true, true), _utf16BeBom.Length),
            _ => (new UTF8Encoding(false, true), 0),
        };

        try {
            var text = encoding.GetString(bytes, bomLength, bytes.Length - bomLength);

            return new DecodedText(text, encoding, bomLength > 0, false);
        } catch (DecoderFallbackException) {
            return new DecodedText(ansiEncoding.GetString(bytes), ansiEncoding, false, true);
        }
    }

    /// <summary>The most frequent line ending in <paramref name="text"/>; <c>\n</c> when it has none.</summary>
    internal static string DominantLineEnding(string text) {
        var crLf = 0;
        var lf = 0;
        var cr = 0;

        for (var i = 0; i < text.Length; i++) {
            if (text[i] == '\n') {
                lf++;
            } else if (text[i] == '\r') {
                if (i + 1 < text.Length && text[i + 1] == '\n') {
                    crLf++;
                    i++;
                } else {
                    cr++;
                }
            }
        }

        if (crLf > lf && crLf >= cr) {
            return "\r\n";
        }

        return cr > lf && cr > crLf ? "\r" : "\n";
    }

    private static byte[] ReadAllBytes(string path) {
        // Share everything, so an editor holding the file open is never locked out by PlanCake.
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete
        );
        using var memory = new MemoryStream();
        stream.CopyTo(memory);

        return memory.ToArray();
    }

    private static void WriteAtomically(string path, byte[] bytes) {
        var folder = System.IO.Path.GetDirectoryName(path) ?? ".";
        var temp = System.IO.Path.Combine(folder, $".{System.IO.Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                stream.Write(bytes);
                stream.Flush(true);
            }

            if (File.Exists(path)) {
                File.Replace(temp, path, null, ignoreMetadataErrors: true);
            } else {
                File.Move(temp, path);
            }
        } finally {
            if (File.Exists(temp)) {
                File.Delete(temp);
            }
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/>, retrying it while the file is locked; after the last retry
    /// the failure is thrown as an <see cref="IOException"/>. A missing file is not retried.
    /// </summary>
    private T WithRetries<T>(Func<T> action) {
        var delay = _options.RetryDelay ?? _defaultRetryDelay;

        for (var attempt = 0; ; attempt++) {
            try {
                return action();
            } catch (Exception ex) when (IsRetryable(ex) && attempt < _options.Retries) {
                Thread.Sleep(delay);
            } catch (UnauthorizedAccessException ex) {
                throw new IOException(ex.Message, ex);
            }
        }
    }

    private static bool IsRetryable(Exception ex) =>
        ex is UnauthorizedAccessException
        || ex is IOException and not (FileNotFoundException or DirectoryNotFoundException);
}
