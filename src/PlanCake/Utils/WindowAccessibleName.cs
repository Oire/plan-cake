using System.Runtime.InteropServices;
using Serilog;

namespace Oire.PlanCake.Utils;

/// <summary>
/// Names a native window for screen readers through MSAA dynamic annotation
/// (<c>IAccPropServices.SetHwndPropStr</c> with <c>PROPID_ACC_NAME</c>).
/// </summary>
/// <remarks>
/// The <c>SysListView32</c> that <c>NativeListView</c> creates reports an empty MSAA name: the
/// system proxy for a list view does not use the window text that
/// <c>NativeListView.AccessibleName</c> sets, and the window has no sibling label for the proxy
/// to fall back on. JAWS reads the MSAA name, so the list went unnamed (Task 8 JAWS check).
/// An annotation is what the proxy consults first, for MSAA and UI Automation clients alike.
/// </remarks>
internal static class WindowAccessibleName {
    private const uint ObjIdClient = 0xFFFFFFFC;
    private const uint ChildIdSelf = 0;

    private static readonly Guid _accPropServicesClass = new("B5F8350B-0548-48B1-A6EE-88BD00B4A5E7");
    private static readonly Guid _propIdAccName = new("608D3DF8-8128-4AA7-A428-F55E49267291");

    /// <summary>
    /// Gives the client area of <paramref name="window"/> the accessible name
    /// <paramref name="name"/>. Failures are logged; a missing name is not worth stopping for.
    /// </summary>
    /// <returns>True when the name was set.</returns>
    public static bool Set(IntPtr window, string name) {
        if (window == IntPtr.Zero) {
            return false;
        }

        object? instance = null;

        try {
            var type = Type.GetTypeFromCLSID(_accPropServicesClass, throwOnError: true)!;
            instance = Activator.CreateInstance(type);

            if (instance is not IAccPropServices services) {
                Log.Warning("IAccPropServices is not available: the list keeps its default name");
                return false;
            }

            var result = services.SetHwndPropStr(window, ObjIdClient, ChildIdSelf, _propIdAccName, name);

            if (result < 0) {
                Log.Warning("Unable to set the accessible name {Name}: HRESULT 0x{Result:X8}", name, result);
                return false;
            }

            return true;
        } catch (Exception ex) when (ex is COMException or InvalidCastException or TypeLoadException) {
            Log.Warning(ex, "Unable to set the accessible name {Name}", name);
            return false;
        } finally {
            if (instance is not null && Marshal.IsComObject(instance)) {
                Marshal.ReleaseComObject(instance);
            }
        }
    }

    /// <summary>
    /// The start of <c>IAccPropServices</c> (oleacc.h), up to the one method used. The methods
    /// before it only hold their places in the vtable and are never called.
    /// </summary>
    [ComImport]
    [Guid("6E26E776-04F0-495D-80E4-3330352E3169")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAccPropServices {
        void SetPropValue();

        void SetPropServer();

        void ClearProps();

        void SetHwndProp();

        [PreserveSig]
        int SetHwndPropStr(
            IntPtr hwnd,
            uint idObject,
            uint idChild,
            Guid idProp,
            [MarshalAs(UnmanagedType.LPWStr)] string str
        );
    }
}
