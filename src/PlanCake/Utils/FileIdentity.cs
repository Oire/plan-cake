using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Oire.PlanCake.Utils;

/// <summary>
/// Tells whether two paths name the same file on disk, whatever way they name it: an 8.3 short
/// name, a hard link, a symbolic link or junction, a <c>\\?\</c> or <c>\\localhost\c$</c> path,
/// a <c>subst</c> drive. Compares the volume serial number and the file ID of both.
/// </summary>
internal static class FileIdentity {
    /// <summary>
    /// True when <paramref name="path"/> and <paramref name="otherPath"/> are the same file. When
    /// either cannot be opened (it does not exist, access is denied), their full paths are
    /// compared instead.
    /// </summary>
    public static bool IsSameFile(string path, string otherPath) {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(otherPath);

        if (Identity(path) is { } first && Identity(otherPath) is { } second) {
            return first == second;
        }

        try {
            return String.Equals(Path.GetFullPath(path), Path.GetFullPath(otherPath), StringComparison.OrdinalIgnoreCase);
        } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) {
            return false;
        }
    }

    /// <summary>The volume serial number and file ID of an existing file; <see langword="null"/> when it cannot be opened.</summary>
    private static (uint Volume, ulong File)? Identity(string path) {
        try {
            using var handle = File.OpenHandle(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete
            );

            if (!NativeMethods.GetFileInformationByHandle(handle, out var info)) {
                return null;
            }

            return (info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                         or NotSupportedException) {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    private static class NativeMethods {
        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);
    }
}
