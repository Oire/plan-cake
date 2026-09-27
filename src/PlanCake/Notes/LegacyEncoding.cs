using System.Buffers;
using System.Text;
using UtfUnknown;

namespace Oire.PlanCake.Notes;

/// <summary>
/// Finds the legacy (non-Unicode) encoding of a file that is not valid UTF-8, for
/// <see cref="MarkdownFile"/>. The candidates, in order: what a charset detector (UTF.Unknown, a
/// port of Mozilla's) finds with enough confidence in enough text; the code page of the document language; the
/// Windows ANSI code page, unless that is UTF-8 (code page 65001, Windows' "Use Unicode UTF-8 for
/// worldwide language support"), which is no legacy encoding at all. The first candidate that
/// decodes the bytes cleanly wins; when none does, the encoding is not recognized.
/// </summary>
internal static class LegacyEncoding {
    /// <summary>The detector's confidence from which its answer is taken.</summary>
    internal const float MinimumConfidence = 0.5f;

    /// <summary>
    /// The number of non-ASCII bytes below which the detector is not asked: on a few words it
    /// guesses wildly (four Hebrew letters come out as Greek, with a confidence of 0.78).
    /// </summary>
    internal const int MinimumNonAsciiBytes = 8;

    private const int Utf8CodePage = 65001;

    static LegacyEncoding() {
        // Without the provider .NET only knows the Unicode encodings.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// The Windows code page of a document language (a <c>LanguageList</c> code):
    /// Windows-1251 for Russian and Ukrainian, Windows-1255 for Hebrew, Windows-1252 for English,
    /// French and German; <see langword="null"/> for any other or none.
    /// </summary>
    public static int? CodePageOf(string? documentLanguage) => documentLanguage?.ToLowerInvariant() switch {
        "ru" or "uk" => 1251,
        "he" => 1255,
        "en" or "fr" or "de" => 1252,
        _ => null,
    };

    /// <summary>
    /// The encodings to try for <paramref name="bytes"/>, most likely first, without duplicates
    /// and without any Unicode encoding.
    /// </summary>
    /// <param name="bytes">The file's bytes, which are not valid UTF-8.</param>
    /// <param name="documentLanguage">The document language, if known.</param>
    /// <param name="ansiEncoding">The Windows ANSI code page's encoding (injected by tests).</param>
    public static IReadOnlyList<Encoding> Candidates(byte[] bytes, string? documentLanguage, Encoding ansiEncoding) {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(ansiEncoding);

        var codePages = new List<int>();

        if (Detect(bytes) is { } detected) {
            codePages.Add(detected);
        }

        if (CodePageOf(documentLanguage) is { } languagePage) {
            codePages.Add(languagePage);
        }

        codePages.Add(ansiEncoding.CodePage);

        var candidates = new List<Encoding>();

        foreach (var codePage in codePages.Distinct()) {
            if (IsUnicode(codePage)) {
                continue;
            }

            candidates.Add(codePage == ansiEncoding.CodePage ? ansiEncoding : Encoding.GetEncoding(codePage));
        }

        return candidates;
    }

    /// <summary>
    /// Decodes <paramref name="bytes"/> with <paramref name="encoding"/> without losing anything:
    /// no byte may be undefined in it, and the text may hold no replacement character, no NUL
    /// (what UTF-16 read as a single-byte code page yields for every other byte) and no C1
    /// control character (U+0080 to U+009F), which a single-byte code page yields for the bytes
    /// it leaves undefined and which no real text contains.
    /// </summary>
    /// <returns>The text, or <see langword="null"/> when the encoding does not fit the bytes.</returns>
    public static string? TryDecodeCleanly(byte[] bytes, Encoding encoding) {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(encoding);

        var strict = Encoding.GetEncoding(encoding.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        string text;

        try {
            text = strict.GetString(bytes);
        } catch (DecoderFallbackException) {
            return null;
        }

        foreach (var c in text) {
            if (c is '�' or '\0' || c is >= '\u0080' and <= '\u009F') {
                return null;
            }
        }

        return text;
    }

    /// <summary>
    /// How many valid multibyte UTF-8 sequences a file needs per invalid byte to count as damaged
    /// UTF-8 rather than a legacy encoding. In a legacy file nearly every letter outside ASCII is
    /// an invalid byte, and a valid sequence turns up only by chance.
    /// </summary>
    internal const int DamagedUtf8Ratio = 2;

    /// <summary>
    /// Where the first invalid byte is when <paramref name="bytes"/>, which are not valid UTF-8,
    /// are still UTF-8 with a few damaged bytes (a pasted character from another encoding, a
    /// truncated end): they hold valid multibyte sequences, at least <see cref="DamagedUtf8Ratio"/>
    /// per invalid byte. Decoding such a file with a single-byte code page would turn every
    /// character outside ASCII into two or three wrong ones (<c>с</c> into <c>СЃ</c>).
    /// </summary>
    /// <returns>The offset of the first invalid byte, or <see langword="null"/> when the bytes do not look like UTF-8.</returns>
    public static int? FindDamagedUtf8(ReadOnlySpan<byte> bytes) {
        var valid = 0;
        var invalid = 0;
        int? first = null;
        var offset = 0;

        while (offset < bytes.Length) {
            var status = Rune.DecodeFromUtf8(bytes[offset..], out _, out var consumed);

            if (status == OperationStatus.Done) {
                if (consumed > 1) {
                    valid++;
                }
            } else {
                invalid++;
                first ??= offset;
            }

            offset += Math.Max(consumed, 1);
        }

        return invalid > 0 && valid >= invalid * DamagedUtf8Ratio ? first : null;
    }

    /// <summary>An encoding's name as people write it: <c>Windows-1251</c>, <c>UTF-8</c>.</summary>
    public static string DisplayName(Encoding encoding) {
        ArgumentNullException.ThrowIfNull(encoding);

        return encoding.WebName.StartsWith("windows-", StringComparison.OrdinalIgnoreCase)
            ? $"Windows-{encoding.CodePage}"
            : encoding.WebName.ToUpperInvariant();
    }

    /// <summary>The detector's code page for the bytes when it is confident enough, else <see langword="null"/>.</summary>
    private static int? Detect(byte[] bytes) {
        if (bytes.Count(b => b >= 0x80) < MinimumNonAsciiBytes) {
            return null;
        }

        var detected = CharsetDetector.DetectFromBytes(bytes).Detected;

        if (detected?.Encoding is not { } encoding || detected.Confidence < MinimumConfidence) {
            return null;
        }

        // ISO-8859-1 as the detector names it is, in a Windows file, Windows-1252, its superset.
        return encoding.CodePage == 28591 ? 1252 : encoding.CodePage;
    }

    private static bool IsUnicode(int codePage) =>
        codePage is Utf8CodePage or 65000 or 1200 or 1201 or 12000 or 12001;
}
