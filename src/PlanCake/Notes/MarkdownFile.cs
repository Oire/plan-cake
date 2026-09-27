using System.Text;
using Serilog;

namespace Oire.PlanCake.Notes;

/// <summary>How <see cref="MarkdownFile"/> reads and writes a file.</summary>
/// <param name="ConvertToUtf8">
/// Rewrite a file that is not valid in its detected encoding as UTF-8 without BOM when opening it,
/// instead of opening it read-only (the Advanced setting of the same name).
/// </param>
/// <param name="AnsiEncoding">
/// The Windows ANSI code page, the last encoding tried for such a file (see
/// <see cref="LegacyEncoding"/>); <see langword="null"/> for the machine's. Tests inject it, so they
/// do not depend on the machine: a code page of 65001 (UTF-8) is never used as a legacy encoding.
/// </param>
/// <param name="Retries">How many times a locked file is retried after the first attempt.</param>
/// <param name="RetryDelay">The wait between two attempts; <see langword="null"/> for 200 ms.</param>
/// <param name="DocumentLanguage">
/// The document language, whose code page is tried when the charset detector is not sure.
/// </param>
/// <param name="OnRetry">
/// Called on the calling thread after an attempt that is going to be retried, before the wait, with
/// the number of the attempt that failed (0 for the first). Lets tests free a locked file between
/// two attempts without racing the retries with a timer.
/// </param>
internal sealed record MarkdownFileOptions(
    bool ConvertToUtf8 = false,
    Encoding? AnsiEncoding = null,
    int Retries = 5,
    TimeSpan? RetryDelay = null,
    string? DocumentLanguage = null,
    Action<int>? OnRetry = null
) {
    public static MarkdownFileOptions Default { get; } = new();
}

/// <summary>
/// A Markdown file on disk: its text, and how to write it back exactly as it was stored (same
/// encoding, BOM and line endings), atomically, per Task 5 of the PlanCake plan.
/// </summary>
/// <remarks>
/// A file is decoded from its BOM (UTF-8, UTF-16 LE or BE) or else as strict UTF-8. A file that
/// is not valid in that encoding is decoded with the legacy encoding <see cref="LegacyEncoding"/>
/// finds and is then either read-only, so PlanCake cannot damage it, or, with
/// <see cref="MarkdownFileOptions.ConvertToUtf8"/>, rewritten once as UTF-8 without BOM (and
/// read-only after all when that write fails). When no legacy encoding decodes it without loss,
/// its encoding is not recognized: the text shown has replacement characters, and the file is
/// never written, whatever the options say. The same goes, without trying any legacy encoding,
/// for a file whose BOM states its encoding and for UTF-8 with a few damaged bytes.
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
    public string Text { get; private set; } = String.Empty;

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
    /// True when the file is not valid in its detected encoding and was decoded with a legacy
    /// encoding without being converted, or its encoding was not recognized: it is never written.
    /// </summary>
    public bool IsReadOnly { get; private set; }

    /// <summary>
    /// True when no encoding decodes the file without loss: <see cref="Text"/> holds replacement
    /// characters, and writing it would destroy what they stand for, so the file is read-only
    /// and is never converted.
    /// </summary>
    public bool IsUnrecognized { get; private set; }

    /// <summary>
    /// The encoding the file was converted from when it was last read, or <see langword="null"/>;
    /// the caller announces the conversion.
    /// </summary>
    public Encoding? ConvertedFrom { get; private set; }

    /// <summary>
    /// True when the file was to be converted to UTF-8 when it was last read, but could not be
    /// written: it is open read-only in its legacy encoding, and the caller says so.
    /// </summary>
    public bool ConversionFailed { get; private set; }

    /// <summary>
    /// For a file in a Unicode encoding with invalid bytes (<see cref="IsUnrecognized"/>: its BOM
    /// states the encoding, or it is UTF-8 with a few damaged bytes), the 1-based line of the
    /// first invalid byte, for the caller to name; <see langword="null"/> otherwise.
    /// </summary>
    public int? InvalidByteLine { get; private set; }

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
        var decoded = Decode(bytes, ansi, _options.DocumentLanguage);

        Text = decoded.Text;
        Encoding = decoded.Encoding;
        HasBom = decoded.HasBom;
        LineEnding = DominantLineEnding(decoded.Text);
        IsReadOnly = decoded.IsFallback;
        IsUnrecognized = decoded.IsLossy;
        InvalidByteLine = decoded.InvalidByteLine;
        ConvertedFrom = null;
        ConversionFailed = false;

        // Never a lossy decode: the replacement characters would be written over the original bytes.
        if (decoded.IsFallback && !decoded.IsLossy && _options.ConvertToUtf8) {
            ConvertedFrom = decoded.Encoding;
            Encoding = new UTF8Encoding(false);
            HasBom = false;
            IsReadOnly = false;

            try {
                Write(Text);
            } catch (IOException ex) {
                // A read-only file or folder, a file locked past the retries: the legacy decode
                // stands, and the file is shown read-only as it is on disk.
                Log.Warning(ex, "Unable to convert {Path} from {Encoding} to UTF-8; it is open read-only", Path, decoded.Encoding.WebName);
                Encoding = decoded.Encoding;
                HasBom = decoded.HasBom;
                IsReadOnly = true;
                ConvertedFrom = null;
                ConversionFailed = true;
            }
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
        EnsureWritable();

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

    /// <summary>Throws when the file is never written (<see cref="IsReadOnly"/> or <see cref="IsUnrecognized"/>).</summary>
    /// <exception cref="ReadOnlyFileException">The file is read-only.</exception>
    public void EnsureWritable() {
        if (IsReadOnly || IsUnrecognized) {
            throw new ReadOnlyFileException(
                $"{Path} is not valid {Encoding.WebName} and is open read-only; it is never written."
            );
        }
    }

    /// <summary>The result of decoding a file's bytes.</summary>
    /// <param name="IsFallback">True when the bytes were not valid in their Unicode encoding and were decoded with a legacy one.</param>
    /// <param name="IsLossy">
    /// True when no encoding decoded them without loss: the text holds replacement characters.
    /// </param>
    /// <param name="InvalidByteLine">
    /// For a file in a Unicode encoding (stated by its BOM, or damaged UTF-8) with invalid bytes,
    /// the 1-based line of the first one; <see langword="null"/> otherwise.
    /// </param>
    internal readonly record struct DecodedText(
        string Text,
        Encoding Encoding,
        bool HasBom,
        bool IsFallback,
        bool IsLossy = false,
        int? InvalidByteLine = null
    );

    internal static DecodedText Decode(byte[] bytes, Encoding ansiEncoding, string? documentLanguage = null) {
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
            // Shown with replacement characters so the user sees something, never written.
            if (bomLength > 0) {
                // The BOM states the encoding outright: a legacy code page would read the BOM
                // itself as letters (and UTF-16 as a NUL after every letter).
                var damaged = (Encoding)encoding.Clone();
                damaged.DecoderFallback = new DecoderReplacementFallback("�");
                var damagedText = damaged.GetString(bytes, bomLength, bytes.Length - bomLength);

                return new DecodedText(damagedText, damaged, true, true, IsLossy: true, InvalidByteLine: 1 + LineOf(damagedText));
            }

            var lossy = new UTF8Encoding(false, false);

            if (LegacyEncoding.FindDamagedUtf8(bytes) is { } offset) {
                var line = 1 + bytes.AsSpan(0, offset).Count((byte)'\n');

                return new DecodedText(lossy.GetString(bytes), lossy, false, true, IsLossy: true, InvalidByteLine: line);
            }

            foreach (var candidate in LegacyEncoding.Candidates(bytes, documentLanguage, ansiEncoding)) {
                if (LegacyEncoding.TryDecodeCleanly(bytes, candidate) is { } legacyText) {
                    return new DecodedText(legacyText, candidate, false, true);
                }
            }

            return new DecodedText(lossy.GetString(bytes), lossy, false, true, IsLossy: true);
        }
    }

    /// <summary>The number of line breaks before the first replacement character of <paramref name="text"/>.</summary>
    private static int LineOf(string text) {
        var index = text.IndexOf('�', StringComparison.Ordinal);

        return index < 0 ? 0 : text.AsSpan(0, index).Count('\n');
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
                _options.OnRetry?.Invoke(attempt);
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
