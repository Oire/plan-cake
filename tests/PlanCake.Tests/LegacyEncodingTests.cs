using System.Text;
using AwesomeAssertions;
using Oire.PlanCake.Notes;
using Xunit;

namespace Oire.PlanCake.Tests;

/// <summary>
/// Files that are not UTF-8. The machine's ANSI code page is injected as UTF-8 (65001, Windows'
/// "Use Unicode UTF-8 for worldwide language support"), where decoding "as ANSI" once turned a
/// Windows-1251 file into replacement characters and the conversion wrote them over it.
/// </summary>
public class LegacyEncodingTests: IDisposable {
    private static readonly Encoding _utf8Ansi = new UTF8Encoding(false);

    private readonly string _folder;

    static LegacyEncodingTests() {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public LegacyEncodingTests() {
        _folder = Path.Combine(Path.GetTempPath(), $"PlanCake.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_folder);
    }

    public void Dispose() {
        if (Directory.Exists(_folder)) {
            Directory.Delete(_folder, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private const string Russian = "# Проверка кодировки\n\nЭтот файл в кодировке Windows-1251. Съешь же ещё этих мягких булок.\n";
    private const string Ukrainian = "# Перевірка кодування\n\nЦей файл у кодуванні Windows-1251. Їжак, ґанок, єнот.\n";
    private const string Hebrew = "# בדיקת קידוד\n\nהקובץ הזה בקידוד Windows-1255. שלום עולם.\n";
    private const string French = "# Vérification\n\nCe fichier est encodé en Windows-1252 : élève, où, déjà, ça, « œuvre ».\n";

    /// <summary>Bytes no legacy code page PlanCake tries decodes cleanly: each is undefined in Windows-1252 or 1251.</summary>
    private static readonly byte[] _undecodable = [
        (byte)'#', (byte)' ', 0x81, 0x8D, 0x8F, 0x90, 0x9D, 0x98, (byte)'\n',
    ];

    private string WriteBytes(byte[] bytes) {
        var path = Path.Combine(_folder, "plan.md");
        File.WriteAllBytes(path, bytes);

        return path;
    }

    private static MarkdownFileOptions Options(bool convert = false, string? language = null) => new(
        ConvertToUtf8: convert,
        AnsiEncoding: _utf8Ansi,
        RetryDelay: TimeSpan.FromMilliseconds(20),
        DocumentLanguage: language
    );

    [Theory]
    [InlineData(Russian, 1251, null)]
    [InlineData(Russian, 1251, "en")]
    [InlineData(Ukrainian, 1251, "uk")]
    [InlineData(Hebrew, 1255, null)]
    [InlineData(Hebrew, 1255, "en")]
    [InlineData(French, 1252, null)]
    [InlineData(French, 1252, "fr")]
    public void Open_LegacyFileWithUtf8AnsiCodePage_DecodesItCorrectly(string text, int codePage, string? language) {
        var bytes = Encoding.GetEncoding(codePage).GetBytes(text);
        var path = WriteBytes(bytes);

        var file = MarkdownFile.Open(path, Options(language: language));

        file.Text.Should().Be(text);
        file.Encoding.CodePage.Should().Be(codePage);
        file.IsReadOnly.Should().BeTrue();
        file.IsUnrecognized.Should().BeFalse();
        File.ReadAllBytes(path).Should().Equal(bytes);
    }

    [Theory]
    [InlineData("Абзац.\n", "ru", 1251)]
    [InlineData("- [ ] задача\n", "uk", 1251)]
    [InlineData("שלום\n", "he", 1255)]
    [InlineData("Déjà vu.\n", "de", 1252)]
    public void Open_ShortLegacyFile_UsesTheDocumentLanguagesCodePage(string text, string language, int codePage) {
        var bytes = Encoding.GetEncoding(codePage).GetBytes(text);
        var path = WriteBytes(bytes);

        var file = MarkdownFile.Open(path, Options(language: language));

        file.Text.Should().Be(text);
        file.Encoding.CodePage.Should().Be(codePage);
    }

    [Theory]
    [InlineData(Russian, 1251)]
    [InlineData(Hebrew, 1255)]
    [InlineData(French, 1252)]
    public void Open_LegacyFileWithConvertToUtf8_KeepsEveryCharacter(string text, int codePage) {
        var path = WriteBytes(Encoding.GetEncoding(codePage).GetBytes(text));

        var file = MarkdownFile.Open(path, Options(convert: true));

        file.ConvertedFrom!.CodePage.Should().Be(codePage);
        file.Text.Should().Be(text);
        File.ReadAllBytes(path).Should().Equal(new UTF8Encoding(false).GetBytes(text));
        File.ReadAllText(path, Encoding.UTF8).Should().NotContain("�");
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(true, "en")]
    public void Open_UndecodableFile_StaysReadOnlyAndIsNeverWritten(bool convert, string? language) {
        var path = WriteBytes(_undecodable);

        var file = MarkdownFile.Open(path, Options(convert, language));

        file.IsUnrecognized.Should().BeTrue();
        file.IsReadOnly.Should().BeTrue();
        file.ConvertedFrom.Should().BeNull();
        file.Text.Should().Contain("�");

        var write = () => file.Write("# anything\n");

        write.Should().Throw<ReadOnlyFileException>();
        File.ReadAllBytes(path).Should().Equal(_undecodable);
    }

    [Theory]
    [InlineData(false, "ru", 1251)]
    [InlineData(true, "ru", 1251)]
    [InlineData(true, "fr", 1252)]
    public void Open_Utf8WithOneStrayByte_IsDamagedUtf8NotALegacyFile(bool convert, string language, int ansi) {
        // Valid UTF-8 Russian with one Windows-1251 byte pasted in on line 3.
        byte[] bytes = [
            .. new UTF8Encoding(false).GetBytes(Russian.Replace("Windows-1251", "UTF-8", StringComparison.Ordinal)),
            .. new UTF8Encoding(false).GetBytes("Строка "), 0xE9, (byte)'\n',
        ];
        var path = WriteBytes(bytes);

        var file = MarkdownFile.Open(path, Options(convert, language) with { AnsiEncoding = Encoding.GetEncoding(ansi) });

        file.IsUnrecognized.Should().BeTrue();
        file.IsReadOnly.Should().BeTrue();
        file.ConvertedFrom.Should().BeNull();
        file.Encoding.CodePage.Should().Be(65001);
        file.InvalidByteLine.Should().Be(4);
        file.Text.Should().StartWith("# Проверка кодировки").And.Contain("Строка �");
        File.ReadAllBytes(path).Should().Equal(bytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Open_Utf8BomWithAnInvalidByte_IsNeverReadAsALegacyFile(bool convert) {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. new UTF8Encoding(false).GetBytes("# Plan\n\nLine "), 0xFF, (byte)'\n'];
        var path = WriteBytes(bytes);

        var file = MarkdownFile.Open(path, Options(convert) with { AnsiEncoding = Encoding.GetEncoding(1252) });

        file.IsUnrecognized.Should().BeTrue();
        file.IsReadOnly.Should().BeTrue();
        file.HasBom.Should().BeTrue();
        file.InvalidByteLine.Should().Be(3);
        file.Text.Should().Be("# Plan\n\nLine �\n");
        File.ReadAllBytes(path).Should().Equal(bytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Open_Utf16WithAnOddLength_IsNeverReadAsALegacyFile(bool convert) {
        byte[] bytes = [0xFF, 0xFE, .. new UnicodeEncoding(false, false).GetBytes("# План\n"), 0x41];
        var path = WriteBytes(bytes);

        var file = MarkdownFile.Open(path, Options(convert) with { AnsiEncoding = Encoding.GetEncoding(1252) });

        file.IsUnrecognized.Should().BeTrue();
        file.IsReadOnly.Should().BeTrue();
        file.Text.Should().StartWith("# План\n").And.NotContain("\0");
        File.ReadAllBytes(path).Should().Equal(bytes);
    }

    [Fact]
    public void TryDecodeCleanly_RefusesNul() =>
        LegacyEncoding.TryDecodeCleanly([(byte)'a', 0x00, (byte)'b', 0xE9], Encoding.GetEncoding(1252)).Should().BeNull();

    [Theory]
    [InlineData("Съешь же ещё этих мягких булок.")]
    [InlineData("Déjà vu, élève, où, ça.")]
    [InlineData("שלום עולם")]
    public void FindDamagedUtf8_IsNullForALegacyFile(string text) {
        var codePage = text[0] switch {
            >= 'А' and <= 'я' => 1251,
            >= 'א' and <= 'ת' => 1255,
            _ => 1252,
        };

        LegacyEncoding.FindDamagedUtf8(Encoding.GetEncoding(codePage).GetBytes(text)).Should().BeNull();
    }

    [Fact]
    public void Open_LegacyFileThatCannotBeConverted_OpensReadOnlyInsteadOfFailing() {
        var bytes = Encoding.GetEncoding(1251).GetBytes(Russian);
        var path = WriteBytes(bytes);
        File.SetAttributes(path, FileAttributes.ReadOnly);

        try {
            var file = MarkdownFile.Open(path, Options(convert: true) with { Retries = 0 });

            file.ConversionFailed.Should().BeTrue();
            file.ConvertedFrom.Should().BeNull();
            file.IsReadOnly.Should().BeTrue();
            file.Encoding.CodePage.Should().Be(1251);
            file.Text.Should().Be(Russian);
            File.ReadAllBytes(path).Should().Equal(bytes);
        } finally {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Theory]
    [InlineData("ru", 1251)]
    [InlineData("uk", 1251)]
    [InlineData("he", 1255)]
    [InlineData("en", 1252)]
    [InlineData("fr", 1252)]
    [InlineData("de", 1252)]
    public void CodePageOf_MapsEachDocumentLanguage(string language, int codePage) =>
        LegacyEncoding.CodePageOf(language).Should().Be(codePage);

    [Fact]
    public void CodePageOf_IsNullForAnUnknownLanguage() {
        LegacyEncoding.CodePageOf("ja").Should().BeNull();
        LegacyEncoding.CodePageOf(null).Should().BeNull();
    }

    [Fact]
    public void Candidates_NeverOfferUtf8AsALegacyEncoding() =>
        LegacyEncoding.Candidates(_undecodable, null, _utf8Ansi)
            .Should().NotContain(encoding => encoding.CodePage == 65001);

    [Fact]
    public void Candidates_EndWithALegacyAnsiCodePage() {
        var ansi = Encoding.GetEncoding(1250);

        LegacyEncoding.Candidates(_undecodable, "en", ansi).Select(encoding => encoding.CodePage)
            .Should().EndWith([1252, 1250]);
    }

    [Fact]
    public void TryDecodeCleanly_RefusesACodePageThatLeavesBytesUndefined() =>
        LegacyEncoding.TryDecodeCleanly([0x41, 0x81, 0x42], Encoding.GetEncoding(1252)).Should().BeNull();
}
