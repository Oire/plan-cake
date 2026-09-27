using System.Runtime.InteropServices;
using AwesomeAssertions;
using Oire.PlanCake.Utils;
using Xunit;

namespace Oire.PlanCake.Tests;

/// <summary>Two paths are the same file whatever way they name it; see <see cref="FileIdentity"/>.</summary>
public class FileIdentityTests: IDisposable {
    private readonly string _folder;

    public FileIdentityTests() {
        _folder = Path.Combine(Path.GetTempPath(), $"PlanCake.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_folder);
    }

    public void Dispose() {
        if (Directory.Exists(_folder)) {
            Directory.Delete(_folder, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private string Create(string name) {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, name);

        return path;
    }

    [Fact]
    public void IsSameFile_SamePathWrittenAnotherWay_IsTheSameFile() {
        var path = Create("plan.md");

        FileIdentity.IsSameFile(path, path).Should().BeTrue();
        FileIdentity.IsSameFile(path, @"\\?\" + path).Should().BeTrue();
        FileIdentity.IsSameFile(path, Path.Combine(_folder, "sub", "..", "PLAN.MD")).Should().BeTrue();
    }

    [Fact]
    public void IsSameFile_HardLink_IsTheSameFile() {
        var path = Create("plan.md");
        var link = Path.Combine(_folder, "link.md");
        NativeMethods.CreateHardLink(link, path, IntPtr.Zero).Should().BeTrue();

        FileIdentity.IsSameFile(link, path).Should().BeTrue();
    }

    [Fact]
    public void IsSameFile_AnotherFile_IsNot() {
        var path = Create("plan.md");
        var other = Create("other.md");

        FileIdentity.IsSameFile(path, other).Should().BeFalse();
        FileIdentity.IsSameFile(path, Path.Combine(_folder, "missing.html")).Should().BeFalse();
    }

    private static class NativeMethods {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateHardLinkW", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);
    }
}
