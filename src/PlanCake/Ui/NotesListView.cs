using System.ComponentModel;
using System.Runtime.InteropServices;
using Oire.WinForms.NativeControls;

namespace Oire.PlanCake.Ui;

/// <summary>
/// The notes list: a <see cref="NativeListView"/> whose first column fills the width the other
/// columns leave, and whose rows show an info tip (the whole note) when the pointer rests on them.
/// Both through the hooks the library leaves a derived type and documented list-view messages on
/// its <see cref="NativeListView.ListHandle"/>: <c>LVS_EX_INFOTIP</c> and <c>LVN_GETINFOTIP</c>,
/// and <c>LVM_SETCOLUMNWIDTH</c> through <see cref="NativeListViewColumn.Width"/>. A Win32 list
/// view can only auto-fill its last column (<c>LVSCW_AUTOSIZE_USEHEADER</c>), so the first one is
/// sized here whenever the list's width changes.
/// </summary>
/// <remarks>
/// <see cref="NativeListView.AccessibleName"/> is a <see langword="new"/> property: set it through
/// a reference of this type or of <see cref="NativeListView"/>, never through <see cref="Control"/>.
/// </remarks>
[DesignerCategory("Code")]
internal sealed class NotesListView: NativeListView {
    private const int LVM_FIRST = 0x1000;
    private const int LVM_SETEXTENDEDLISTVIEWSTYLE = LVM_FIRST + 54;
    private const int LVS_EX_INFOTIP = 0x00000400;
    private const int WM_NOTIFY = 0x004E;
    private const int LVN_GETINFOTIPW = -100 - 58;

    /// <summary>The narrowest the first column gets, in logical pixels, however narrow the list.</summary>
    private const int MinimumFillWidth = 80;

    /// <summary>The text of the info tip of a row, or <see langword="null"/> for none.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<NativeListViewItem, string?>? InfoTip { get; set; }

    /// <summary>
    /// Gives the first column the width the others leave in the list's client area (without its
    /// vertical scroll bar), and at least <see cref="MinimumFillWidth"/>. Call it again when rows
    /// are added or removed, which may show or hide the scroll bar.
    /// </summary>
    public void FitFirstColumn() {
        if (ListHandle == IntPtr.Zero || Columns.Count == 0
            || !NativeMethods.GetClientRect(ListHandle, out var client)) {
            return;
        }

        var others = 0;

        for (var index = 1; index < Columns.Count; index++) {
            others += Columns[index].Width;
        }

        var width = Math.Max(LogicalToDeviceUnits(MinimumFillWidth), client.Right - client.Left - others);

        if (Columns[0].Width != width) {
            Columns[0].Width = width;
        }
    }

    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);
        SetUpListWindow();
    }

    // A right-to-left switch makes the library create the list window again.
    protected override void OnRightToLeftChanged(EventArgs e) {
        base.OnRightToLeftChanged(e);
        SetUpListWindow();
    }

    protected override void OnSizeChanged(EventArgs e) {
        // The library sizes the list window first.
        base.OnSizeChanged(e);
        FitFirstColumn();
    }

    protected override void WndProc(ref Message m) {
        if (m.Msg == WM_NOTIFY && m.LParam != IntPtr.Zero && WriteInfoTip(m.LParam)) {
            m.Result = IntPtr.Zero;
            return;
        }

        base.WndProc(ref m);
    }

    private void SetUpListWindow() {
        if (ListHandle == IntPtr.Zero) {
            return;
        }

        // The mask limits the change to this one style; the library's own styles stay.
        NativeMethods.SendMessage(
            ListHandle, LVM_SETEXTENDEDLISTVIEWSTYLE, (IntPtr)LVS_EX_INFOTIP, (IntPtr)LVS_EX_INFOTIP
        );
        FitFirstColumn();
    }

    /// <summary>Fills in the info tip the list asks for, when the notification is that.</summary>
    private bool WriteInfoTip(IntPtr lParam) {
        var header = Marshal.PtrToStructure<NativeMethods.NMHDR>(lParam);

        if (header.Code != LVN_GETINFOTIPW || header.HwndFrom != ListHandle || InfoTip is not { } infoTip) {
            return false;
        }

        var request = Marshal.PtrToStructure<NativeMethods.NMLVGETINFOTIPW>(lParam);

        if (request.Item < 0 || request.Item >= Items.Count || request.Text == IntPtr.Zero || request.TextMax <= 0) {
            return true;
        }

        var tip = infoTip(Items[request.Item]) ?? String.Empty;
        var length = Math.Min(tip.Length, request.TextMax - 1);

        // Never end inside a surrogate pair.
        if (length > 0 && length < tip.Length && Char.IsHighSurrogate(tip[length - 1])) {
            length--;
        }

        var buffer = new char[length + 1];
        tip.CopyTo(0, buffer, 0, length);
        Marshal.Copy(buffer, 0, request.Text, buffer.Length);

        return true;
    }

    private static class NativeMethods {
        [StructLayout(LayoutKind.Sequential)]
        public struct NMHDR {
            public IntPtr HwndFrom;
            public UIntPtr IdFrom;
            public int Code;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NMLVGETINFOTIPW {
            public NMHDR Header;
            public uint Flags;
            public IntPtr Text;
            public int TextMax;
            public int Item;
            public int SubItem;
            public IntPtr Param;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetClientRect(IntPtr window, out RECT rect);

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    }
}
