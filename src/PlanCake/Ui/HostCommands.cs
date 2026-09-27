using static Oire.PlanCake.Utils.Localization;

namespace Oire.PlanCake.Ui;

/// <summary>A command the host window carries out, whichever key or menu item asked for it.</summary>
internal enum HostCommand {
    Open,
    OpenFromClipboard,
    OpenFromLink,
    OpenInEditor,
    Settings,
    Reload,
    Back,
    Forward,
    SwitchPane,
    NextNote,
    PreviousNote,
    NextBlock,
    PreviousBlock,
    Undo,
    Redo,
    ZoomIn,
    ZoomOut,
    ResetZoom,
    UserManual,
    About,
}

/// <summary>
/// The one table of host keyboard shortcuts. Keys pressed while the WebView2 has focus never
/// reach the native menu bar's accelerator table or <see cref="Control.ProcessCmdKey"/>: the
/// WebView2 control reports them as a <c>KeyDown</c> of its own. <c>MainWindow</c> looks up
/// both kinds of key press here, and the menu and the keyboard shortcuts dialog show the same
/// keys from here too, so neither can drift from what the keys do.
/// </summary>
internal static class HostCommands {
    /// <summary>
    /// Every shortcut, in the order the shortcuts dialog lists them; a command's first key is the
    /// one its menu item shows.
    /// </summary>
    private static readonly (Keys Keys, HostCommand Command)[] _table = [
        (Keys.Control | Keys.O, HostCommand.Open),
        (Keys.Control | Keys.V, HostCommand.OpenFromClipboard),
        (Keys.Control | Keys.L, HostCommand.OpenFromLink),
        (Keys.Control | Keys.E, HostCommand.OpenInEditor),
        (Keys.Control | Keys.Oemcomma, HostCommand.Settings),
        (Keys.Control | Keys.Z, HostCommand.Undo),
        (Keys.Control | Keys.Y, HostCommand.Redo),
        (Keys.F6, HostCommand.SwitchPane),
        (Keys.Control | Keys.Oemplus, HostCommand.ZoomIn),
        (Keys.Control | Keys.Add, HostCommand.ZoomIn),
        (Keys.Control | Keys.OemMinus, HostCommand.ZoomOut),
        (Keys.Control | Keys.Subtract, HostCommand.ZoomOut),
        (Keys.Control | Keys.D0, HostCommand.ResetZoom),
        (Keys.Alt | Keys.Left, HostCommand.Back),
        (Keys.Back, HostCommand.Back),
        (Keys.Alt | Keys.Right, HostCommand.Forward),
        (Keys.F5, HostCommand.Reload),
        (Keys.F9, HostCommand.NextNote),
        (Keys.Shift | Keys.F9, HostCommand.PreviousNote),
        // Not Alt+Down and Alt+Up: screen readers take those (JAWS opens and closes a list box).
        // JAWS passes Alt+Shift+Down and Alt+Shift+Up on unless its JAWS cursor is active.
        (Keys.Alt | Keys.Shift | Keys.Down, HostCommand.NextBlock),
        (Keys.Alt | Keys.Shift | Keys.Up, HostCommand.PreviousBlock),
        (Keys.F1, HostCommand.UserManual),
        (Keys.Shift | Keys.F1, HostCommand.About),
    ];

    /// <summary>
    /// Keys that run a command without being shown for it: what a key labeled as the shown one
    /// sends on some layouts. On a US or UK layout the + of the main keyboard is Shift+=, so
    /// Ctrl+Plus arrives as Ctrl+Shift+=.
    /// </summary>
    private static readonly (Keys Keys, HostCommand Command)[] _layoutVariants = [
        (Keys.Control | Keys.Shift | Keys.Oemplus, HostCommand.ZoomIn),
    ];

    private static readonly Dictionary<Keys, HostCommand> _shortcuts =
        _table.Concat(_layoutVariants).ToDictionary(entry => entry.Keys, entry => entry.Command);

    /// <summary>
    /// Looks up the command bound to <paramref name="keyData"/> (key code plus modifiers, as
    /// <see cref="Control.ProcessCmdKey"/> and <see cref="KeyEventArgs.KeyData"/> carry it).
    /// </summary>
    public static bool TryGetCommand(Keys keyData, out HostCommand command) =>
        _shortcuts.TryGetValue(keyData, out command);

    /// <summary>The keys bound to <paramref name="command"/>, the one its menu item shows first.</summary>
    public static IReadOnlyList<Keys> KeysOf(HostCommand command) =>
        _table.Where(entry => entry.Command == command).Select(entry => entry.Keys).ToList();

    /// <summary>
    /// The shortcut text a menu item shows for <paramref name="command"/> (its first key), or
    /// <see langword="null"/> when it has none.
    /// </summary>
    public static string? MenuShortcut(HostCommand command) =>
        KeysOf(command) is [var first, ..] ? KeyText(first) : null;

    /// <summary>
    /// The commands that have keys, in table order, each with every key bound to it: the rows the
    /// shortcuts dialog starts with.
    /// </summary>
    public static IReadOnlyList<(HostCommand Command, IReadOnlyList<Keys> Keys)> Shortcuts() =>
        _table.Select(entry => entry.Command)
            .Distinct()
            .Select(command => (command, KeysOf(command)))
            .ToList();

    /// <summary>What the command does, as the shortcuts dialog names it.</summary>
    public static string DisplayName(HostCommand command) => command switch {
        HostCommand.Open => _("Open a file"),
        HostCommand.OpenFromClipboard => _("Open from the clipboard"),
        HostCommand.OpenFromLink => _("Open from a link"),
        HostCommand.OpenInEditor => _("Open in the editor"),
        HostCommand.Settings => _("Settings"),
        HostCommand.Reload => _("Reload the file"),
        HostCommand.Back => _("Back to the previous file"),
        HostCommand.Forward => _("Forward to the next file"),
        HostCommand.SwitchPane => _("Switch between the document and the notes list"),
        HostCommand.NextNote => _("Next note"),
        HostCommand.PreviousNote => _("Previous note"),
        HostCommand.NextBlock => _("Next block"),
        HostCommand.PreviousBlock => _("Previous block"),
        HostCommand.Undo => _("Undo"),
        HostCommand.Redo => _("Redo"),
        HostCommand.ZoomIn => _("Zoom in"),
        HostCommand.ZoomOut => _("Zoom out"),
        HostCommand.ResetZoom => _("Reset zoom"),
        HostCommand.UserManual => _("User manual"),
        HostCommand.About => _("About PlanCake"),
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
    };

    /// <summary>
    /// A key combination as a menu shows it: <c>Ctrl+O</c>, <c>Shift+F9</c>, <c>Alt+Left Arrow</c>,
    /// with the modifiers in the Windows order Ctrl, Alt, Shift (<c>Alt+Shift+Down Arrow</c>).
    /// The modifier and key names are translated (German writes <c>Strg</c>).
    /// </summary>
    public static string KeyText(Keys keyData) {
        var parts = new List<string>();

        if ((keyData & Keys.Control) != 0) {
            parts.Add(_("Ctrl"));
        }

        if ((keyData & Keys.Alt) != 0) {
            parts.Add(_("Alt"));
        }

        if ((keyData & Keys.Shift) != 0) {
            parts.Add(_("Shift"));
        }

        parts.Add(KeyName(keyData & Keys.KeyCode));

        return String.Join("+", parts);
    }

    private static string KeyName(Keys key) => key switch {
        >= Keys.A and <= Keys.Z => ((char)key).ToString(),
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
        >= Keys.F1 and <= Keys.F24 => $"F{key - Keys.F1 + 1}",
        Keys.Left => _("Left Arrow"),
        Keys.Right => _("Right Arrow"),
        Keys.Up => _("Up Arrow"),
        Keys.Down => _("Down Arrow"),
        Keys.Back => _("Backspace"),
        Keys.Delete => _("Delete"),
        Keys.Enter => _("Enter"),
        Keys.Space => _("Space"),
        Keys.Tab => _("Tab"),
        Keys.Apps => _("Applications"),
        Keys.Oemcomma => _("Comma"),
        Keys.Oemplus => _("Plus"),
        Keys.OemMinus => _("Minus"),
        Keys.Add => _("Numpad Plus"),
        Keys.Subtract => _("Numpad Minus"),
        _ => key.ToString(),
    };
}
