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
/// both kinds of key press here, and the menu shows the same keys from here too.
/// </summary>
internal static class HostCommands {
    private static readonly Dictionary<Keys, HostCommand> _shortcuts = new() {
        [Keys.Control | Keys.O] = HostCommand.Open,
        [Keys.Control | Keys.V] = HostCommand.OpenFromClipboard,
        [Keys.Control | Keys.L] = HostCommand.OpenFromLink,
        [Keys.Control | Keys.E] = HostCommand.OpenInEditor,
        [Keys.Control | Keys.Oemcomma] = HostCommand.Settings,
        [Keys.F5] = HostCommand.Reload,
        [Keys.Alt | Keys.Left] = HostCommand.Back,
        [Keys.Back] = HostCommand.Back,
        [Keys.Alt | Keys.Right] = HostCommand.Forward,
        [Keys.F6] = HostCommand.SwitchPane,
        [Keys.F9] = HostCommand.NextNote,
        [Keys.Shift | Keys.F9] = HostCommand.PreviousNote,
        [Keys.Control | Keys.Z] = HostCommand.Undo,
        [Keys.Control | Keys.Y] = HostCommand.Redo,
        [Keys.Control | Keys.Oemplus] = HostCommand.ZoomIn,
        [Keys.Control | Keys.Add] = HostCommand.ZoomIn,
        [Keys.Control | Keys.OemMinus] = HostCommand.ZoomOut,
        [Keys.Control | Keys.Subtract] = HostCommand.ZoomOut,
        [Keys.Control | Keys.D0] = HostCommand.ResetZoom,
        [Keys.F1] = HostCommand.UserManual,
        [Keys.Shift | Keys.F1] = HostCommand.About,
    };

    /// <summary>Every shortcut and the command it runs.</summary>
    public static IReadOnlyDictionary<Keys, HostCommand> Shortcuts => _shortcuts;

    /// <summary>
    /// Looks up the command bound to <paramref name="keyData"/> (key code plus modifiers, as
    /// <see cref="Control.ProcessCmdKey"/> and <see cref="KeyEventArgs.KeyData"/> carry it).
    /// </summary>
    public static bool TryGetCommand(Keys keyData, out HostCommand command) =>
        _shortcuts.TryGetValue(keyData, out command);
}
