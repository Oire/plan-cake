using System.Runtime.InteropServices;

namespace Oire.PlanCake.Ui;

/// <summary>What a key pressed in the document does to the window's menu bar.</summary>
internal enum MenuKeyAction {
    /// <summary>Nothing: the key goes on to the page.</summary>
    None,

    /// <summary>The menu bar is entered, as Alt alone or F10 does anywhere else in Windows.</summary>
    EnterMenuBar,

    /// <summary>The menu whose mnemonic is the key's character opens, as Alt+letter does.</summary>
    OpenMenu,
}

/// <summary>
/// The menu bar's keys for the document. Keys pressed while the WebView2 has the focus never pass
/// through the host's message loop, so Windows never sees the Alt+letter, the bare Alt or the F10
/// that would enter the native menu bar; the browser reports them and does nothing with them.
/// <c>MainWindow</c> feeds every key the browser reports through <see cref="KeyDown"/> and
/// <see cref="KeyUp"/> and, for an action other than <see cref="MenuKeyAction.None"/> on a key
/// that is not a host command, enters the menu bar itself with <c>WM_SYSCOMMAND</c> /
/// <c>SC_KEYMENU</c>. The classification is pure, for the tests.
/// </summary>
internal sealed class MenuKeys {
    /// <summary>True while Alt is down and nothing else was pressed since it went down.</summary>
    private bool _altAlone;

    /// <summary>A key went down in the document (<paramref name="keyData"/>: key code plus modifiers).</summary>
    public MenuKeyAction KeyDown(Keys keyData) {
        var key = keyData & Keys.KeyCode;
        var modifiers = keyData & Keys.Modifiers;

        if (IsAlt(key)) {
            // Alt with Ctrl (AltGr on many layouts) or with Shift (a layout switch) is not Alt alone.
            _altAlone = (modifiers & (Keys.Control | Keys.Shift)) == Keys.None;

            return MenuKeyAction.None;
        }

        _altAlone = false;

        if (keyData == Keys.F10) {
            return MenuKeyAction.EnterMenuBar;
        }

        // Alt and a letter only: with Ctrl it may be AltGr typing a character, and with Shift it
        // may be a layout switch.
        return modifiers == Keys.Alt && IsCharacterKey(key) ? MenuKeyAction.OpenMenu : MenuKeyAction.None;
    }

    /// <summary>A key went up in the document: Alt released with nothing pressed since enters the menu bar.</summary>
    public MenuKeyAction KeyUp(Keys keyData) {
        if (!IsAlt(keyData & Keys.KeyCode)) {
            return MenuKeyAction.None;
        }

        var alone = _altAlone;
        _altAlone = false;

        return alone ? MenuKeyAction.EnterMenuBar : MenuKeyAction.None;
    }

    /// <summary>
    /// Forgets a pending Alt: the window lost the activation (Alt+Tab), so an Alt released later
    /// is not the one pressed here.
    /// </summary>
    public void Reset() => _altAlone = false;

    private static bool IsAlt(Keys key) => key is Keys.Menu or Keys.LMenu or Keys.RMenu;

    /// <summary>
    /// A key that types a character on some layout, and so may be a menu mnemonic: a letter, a
    /// digit, or one of the OEM keys that carry letters on other layouts (Russian х and ж,
    /// Ukrainian ї and є, Hebrew ף and ץ).
    /// </summary>
    internal static bool IsCharacterKey(Keys key) => key is
        >= Keys.A and <= Keys.Z
        or >= Keys.D0 and <= Keys.D9
        or >= Keys.OemSemicolon and <= Keys.Oemtilde
        or >= Keys.OemOpenBrackets and <= Keys.Oem8
        or Keys.OemBackslash;

    /// <summary>
    /// The character <paramref name="key"/> types without modifiers on the keyboard layout of the
    /// window with the focus (the browser's), which is what a menu matches its mnemonics with; on
    /// a Russian layout the F key is а. <see langword="null"/> for a dead key or no character.
    /// </summary>
    public static char? CharacterOf(Keys key) {
        var focus = NativeMethods.GetFocus();
        var thread = focus == IntPtr.Zero ? 0 : NativeMethods.GetWindowThreadProcessId(focus, out _);
        var layout = NativeMethods.GetKeyboardLayout(thread);
        var scanCode = NativeMethods.MapVirtualKeyEx((uint)key, MapVkToVsc, layout);
        var state = new byte[256];
        var buffer = new char[4];

        // Leaves the keyboard state alone, so a dead key the user typed before is not lost.
        var count = NativeMethods.ToUnicodeEx(
            (uint)key, scanCode, state, buffer, buffer.Length, DoNotChangeKeyboardState, layout
        );

        return count == 1 && !Char.IsControl(buffer[0]) ? buffer[0] : null;
    }

    private const uint MapVkToVsc = 0;
    private const uint DoNotChangeKeyboardState = 0x4;

    private static class NativeMethods {
        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern IntPtr GetFocus();

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern IntPtr GetKeyboardLayout(uint threadId);

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint MapVirtualKeyEx(uint code, uint mapType, IntPtr layout);

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int ToUnicodeEx(
            uint virtualKey,
            uint scanCode,
            byte[] keyState,
            [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)] char[] buffer,
            int bufferSize,
            uint flags,
            IntPtr layout
        );
    }
}
