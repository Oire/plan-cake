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
    /// no byte may be undefined in it, and the text may hold no replacement character and no C1
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
            if (c == '�' || c is >= '\u0080' and <= '\u009F') {
                return null;
            }
        }

        return text;
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
