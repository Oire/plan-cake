using System.Runtime.InteropServices;
using System.Text;
using AwesomeAssertions;
using Oire.PlanCake.Notes;
using Oire.PlanCake.Utils;
using Xunit;

namespace Oire.PlanCake.Tests;

public class MarkdownFileTests: IDisposable {
    private static readonly Encoding _windows1251 = CodePagesEncodingProvider.Instance.GetEncoding(1251)!;

    private readonly string _folder;

    public MarkdownFileTests() {
        _folder = Path.Combine(Path.GetTempPath(), $"PlanCake.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_folder);
    }

    public void Dispose() {
        if (Directory.Exists(_folder)) {
            Directory.Delete(_folder, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private string WriteBytes(byte[] bytes, string name = "plan.md") {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, bytes);

        return path;
    }

    private static MarkdownFileOptions Options(bool convert = false) =>
        new(ConvertToUtf8: convert, AnsiEncoding: _windows1251, RetryDelay: TimeSpan.FromMilliseconds(20));

    private static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

    [Fact]
    public void Open_Utf8WithoutBom_WritesBackWithoutBom() {
        var path = WriteBytes(Utf8("# Привет\n"));

        var file = MarkdownFile.Open(path, Options());
        file.Text.Should().Be("# Привет\n");
        file.HasBom.Should().BeFalse();
        file.IsReadOnly.Should().BeFalse();

        file.Write("# Привет, мир\n");

        File.ReadAllBytes(path).Should().Equal(Utf8("# Привет, мир\n"));
    }

    [Fact]
    public void Open_Utf8WithBom_KeepsTheBom() {
        var path = WriteBytes([0xEF, 0xBB, 0xBF, .. Utf8("text\r\n")]);

        var file = MarkdownFile.Open(path, Options());
        file.Text.Should().Be("text\r\n");
        file.HasBom.Should().BeTrue();

        file.Write("new\r\n");

        File.ReadAllBytes(path).Should().Equal([0xEF, 0xBB, 0xBF, .. Utf8("new\r\n")]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Open_Utf16WithBom_RoundTrips(bool bigEndian) {
        var encoding = new UnicodeEncoding(bigEndian, true);
        var path = WriteBytes([.. encoding.GetPreamble(), .. encoding.GetBytes("Hello, שלום\n")]);

        var file = MarkdownFile.Open(path, Options());
        file.Text.Should().Be("Hello, שלום\n");
        file.HasBom.Should().BeTrue();

        file.Write("Bye\n");

        File.ReadAllBytes(path).Should().Equal([.. encoding.GetPreamble(), .. encoding.GetBytes("Bye\n")]);
    }

    [Theory]
    [InlineData("a\nb\nc\n", "\n")]
    [InlineData("a\r\nb\r\nc\n", "\r\n")]
    [InlineData("a\rb\rc", "\r")]
    [InlineData("no line break", "\n")]
    [InlineData("a\nb\r\n", "\n")]
    public void DominantLineEnding_PicksTheMostFrequent(string text, string expected) =>
        MarkdownFile.DominantLineEnding(text).Should().Be(expected);

    [Fact]
    public void Open_Windows1251_OpensReadOnlyAndRefusesToWrite() {
        var bytes = _windows1251.GetBytes("# План\r\nТекст\r\n");
        var path = WriteBytes(bytes);

        var file = MarkdownFile.Open(path, Options());

        file.Text.Should().Be("# План\r\nТекст\r\n");
        file.IsReadOnly.Should().BeTrue();
        file.ConvertedFrom.Should().BeNull();
        file.Encoding.CodePage.Should().Be(1251);

        var write = () => file.Write("anything");

        write.Should().Throw<ReadOnlyFileException>();
        File.ReadAllBytes(path).Should().Equal(bytes);
    }

    [Fact]
    public void Open_Windows1251WithConvertToUtf8_RewritesTheFileAsUtf8WithoutBom() {
        var path = WriteBytes(_windows1251.GetBytes("# План\r\nТекст\r\n"));

        var file = MarkdownFile.Open(path, Options(convert: true));

        file.Text.Should().Be("# План\r\nТекст\r\n");
        file.IsReadOnly.Should().BeFalse();
        file.HasBom.Should().BeFalse();
        file.ConvertedFrom!.CodePage.Should().Be(1251);
        file.LineEnding.Should().Be("\r\n");
        File.ReadAllBytes(path).Should().Equal(Utf8("# План\r\nТекст\r\n"));

        // Read again, it is plain UTF-8 now.
        var again = MarkdownFile.Open(path, Options(convert: true));
        again.ConvertedFrom.Should().BeNull();
        again.Text.Should().Be("# План\r\nТекст\r\n");
    }

    [Fact]
    public void OpenWithoutConverting_Windows1251_LeavesTheFileUntilConvertToUtf8() {
        var bytes = _windows1251.GetBytes("# План\r\nТекст\r\n");
        var path = WriteBytes(bytes);

        var file = MarkdownFile.OpenWithoutConverting(path, Options(convert: true));

        file.Text.Should().Be("# План\r\nТекст\r\n");
        file.IsReadOnly.Should().BeTrue();
        file.ConvertedFrom.Should().BeNull();
        file.NeedsConversion.Should().BeTrue();
        File.ReadAllBytes(path).Should().Equal(bytes);

        file.ConvertToUtf8().Should().Be("# План\r\nТекст\r\n");

        file.IsReadOnly.Should().BeFalse();
        file.ConvertedFrom!.CodePage.Should().Be(1251);
        file.NeedsConversion.Should().BeFalse();
        File.ReadAllBytes(path).Should().Equal(Utf8("# План\r\nТекст\r\n"));
    }

    [Fact]
    public void ConvertToUtf8_AfterAChangeOnDisk_ConvertsWhatTheFileHoldsNow() {
        var path = WriteBytes(_windows1251.GetBytes("# План\n"));
        var file = MarkdownFile.OpenWithoutConverting(path, Options(convert: true));
        File.WriteAllBytes(path, _windows1251.GetBytes("# План\nНовая строка\n"));

        file.ConvertToUtf8().Should().Be("# План\nНовая строка\n");

        file.Text.Should().Be("# План\nНовая строка\n");
        File.ReadAllBytes(path).Should().Equal(Utf8("# План\nНовая строка\n"));
    }

    [Fact]
    public void ConvertToUtf8_WhenTheOptionsDoNotConvert_LeavesTheFileReadOnly() {
        var bytes = _windows1251.GetBytes("# План\n");
        var path = WriteBytes(bytes);
        var file = MarkdownFile.OpenWithoutConverting(path, Options());

        file.NeedsConversion.Should().BeFalse();
        file.ConvertToUtf8().Should().Be("# План\n");

        file.IsReadOnly.Should().BeTrue();
        File.ReadAllBytes(path).Should().Equal(bytes);
    }

    [Fact]
    public void ConvertToUtf8_Utf8File_NeedsNoConversion() {
        var path = WriteBytes(Utf8("# Plan\n"));
        var file = MarkdownFile.OpenWithoutConverting(path, Options(convert: true));

        file.NeedsConversion.Should().BeFalse();
        file.IsReadOnly.Should().BeFalse();
    }

    [Fact]
    public void Reload_AfterOpenWithoutConverting_FollowsTheOptions() {
        var path = WriteBytes(_windows1251.GetBytes("# План\n"));
        var file = MarkdownFile.OpenWithoutConverting(path, Options(convert: true));

        file.Reload();

        file.ConvertedFrom!.CodePage.Should().Be(1251);
        File.ReadAllBytes(path).Should().Equal(Utf8("# План\n"));
    }

    [Fact]
    public void Write_LeavesNoTemporaryFileBehind() {
        var path = WriteBytes(Utf8("a\n"));
        var file = MarkdownFile.Open(path, Options());

        file.Write("b\n");

        Directory.GetFiles(_folder).Should().Equal(path);
    }

    [Fact]
    public void Write_TargetGone_CreatesIt() {
        var path = WriteBytes(Utf8("a\n"));
        var file = MarkdownFile.Open(path, Options());
        File.Delete(path);

        file.Write("b\n");

        File.ReadAllText(path).Should().Be("b\n");
    }

    [Fact]
    public void Write_LockedFile_RetriesThenThrowsIOException() {
        var path = WriteBytes(Utf8("a\n"));
        var file = MarkdownFile.Open(path, Options());

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
            var write = () => file.Write("b\n");

            write.Should().Throw<IOException>();
        }

        File.ReadAllText(path).Should().Be("a\n");
        Directory.GetFiles(_folder).Should().Equal(path);
    }

    [Fact]
    public void Write_FileWithTheReadOnlyAttribute_ThrowsIOExceptionWithoutRetrying() {
        var path = WriteBytes(Utf8("a\n"));
        var retries = new List<int>();
        var file = MarkdownFile.Open(path, Options() with { OnRetry = retries.Add });
        File.SetAttributes(path, FileAttributes.ReadOnly);

        try {
            var write = () => file.Write("b\n");

            write.Should().Throw<IOException>();
            retries.Should().BeEmpty();
            File.ReadAllText(path).Should().Be("a\n");
            Directory.GetFiles(_folder).Should().Equal(path);
        } finally {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact]
    public void Write_FileUnlockedDuringTheRetries_Succeeds() {
        var path = WriteBytes(Utf8("a\n"));
        var failedAttempts = new List<int>();
        FileStream? locked = null;

        // The lock goes when the first attempt has failed on it: no timer racing the retries.
        var file = MarkdownFile.Open(path, Options() with {
            OnRetry = attempt => {
                failedAttempts.Add(attempt);
                locked?.Dispose();
            },
        });
        locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        try {
            file.Write("b\n");
        } finally {
            locked.Dispose();
        }

        failedAttempts.Should().Equal(0);
        File.ReadAllText(path).Should().Be("b\n");
    }

    [Fact]
    public void Open_Folder_FailsWithoutRetrying() {
        var retries = new List<int>();

        var open = () => MarkdownFile.Open(_folder, Options() with { OnRetry = retries.Add });

        open.Should().Throw<IOException>();
        retries.Should().BeEmpty();
    }

    [Fact]
    public void Write_ThroughASymbolicLink_WritesTheTargetAndKeepsTheLink() {
        var target = WriteBytes(Utf8("a\n"), "real.md");
        var link = Path.Combine(_folder, "link.md");

        try {
            File.CreateSymbolicLink(link, target);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            // Creating a symbolic link takes Developer Mode or elevation; without either there is
            // nothing to test.
            return;
        }

        var file = MarkdownFile.Open(link, Options());
        file.Write("b\n");

        File.ReadAllText(target).Should().Be("b\n");
        new FileInfo(link).LinkTarget.Should().Be(target);
        file.Path.Should().Be(link);
        Directory.GetFiles(_folder).Order().Should().Equal(link, target);
    }

    [Fact]
    public void Write_FileWithASecondHardLink_UpdatesBothNames() {
        var path = WriteBytes(Utf8("a\n"));
        var other = Path.Combine(_folder, "other.md");
        NativeMethods.CreateHardLink(other, path, IntPtr.Zero).Should().BeTrue();

        var file = MarkdownFile.Open(path, Options());
        file.Write("longer text\n");
        file.Write("b\n");

        File.ReadAllText(path).Should().Be("b\n");
        File.ReadAllText(other).Should().Be("b\n");
        FileIdentity.IsSameFile(path, other).Should().BeTrue();
        FileIdentity.LinkCount(path).Should().Be(2);
    }

    private static class NativeMethods {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true, EntryPoint = "CreateHardLinkW")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);
    }

    [Fact]
    public void Open_MissingFile_ThrowsFileNotFound() {
        var open = () => MarkdownFile.Open(Path.Combine(_folder, "missing.md"), Options());

        open.Should().Throw<FileNotFoundException>();
    }
}
