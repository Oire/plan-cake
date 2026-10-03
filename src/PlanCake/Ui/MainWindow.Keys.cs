using Serilog;

namespace Oire.PlanCake.Ui;

internal sealed partial class MainWindow {
    /// <summary>
    /// Host shortcuts pressed anywhere but in the document. Keys pressed in the document never
    /// get here (see <see cref="OnDocumentAcceleratorKeyDown"/>); both paths end in
    /// <see cref="TryRunShortcut"/>, so the one table in <see cref="HostCommands"/> decides.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) =>
        TryLeaveNotesListByTab(keyData) || TryRunShortcut(keyData) || base.ProcessCmdKey(ref msg, keyData);

    /// <summary>
    /// Tab and Shift+Tab in the notes list go to the document: Tab to its first focusable
    /// element, Shift+Tab to its last. The list passes Tab on only among the controls of its own
    /// panel, where it is the only stop, so without this the focus never left it. The other half
    /// of the cycle is the WebView2's own: Tab from the page's last element and Shift+Tab from its
    /// first go to the next tab stop of the window, which is the list while it is shown, and back
    /// into the page while it is hidden.
    /// </summary>
    private bool TryLeaveNotesListByTab(Keys keyData) {
        if (keyData is not (Keys.Tab or (Keys.Shift | Keys.Tab)) || !IsNotesListFocused || !documentView.IsInitialized) {
            return false;
        }

        documentView.EnterByTab(forward: keyData == Keys.Tab);
        return true;
    }

    /// <summary>
    /// Host shortcuts pressed in the document. The WebView2 control reports them as a
    /// <c>KeyDown</c> from inside a browser event, bypassing the message loop, the native menu's
    /// accelerator table and <see cref="ProcessCmdKey"/>. For the same reason Windows never sees
    /// the keys that enter the menu bar there: Alt+letter and F10 are handed to it here
    /// (<see cref="MenuKeys"/>), after the host commands, so Alt+Shift+Down and the rest keep theirs.
    /// </summary>
    private void OnDocumentAcceleratorKeyDown(object? sender, KeyEventArgs e) {
        var menuAction = _menuKeys.KeyDown(e.KeyData);

        if (TryRunShortcut(e.KeyData, fromDocument: true)) {
            e.Handled = true;
            return;
        }

        e.Handled = EnterMenuBar(menuAction, e.KeyData);
    }

    /// <summary>Alt released alone in the document enters the menu bar, as it does elsewhere in Windows.</summary>
    private void OnDocumentAcceleratorKeyUp(object? sender, KeyEventArgs e) =>
        e.Handled = EnterMenuBar(_menuKeys.KeyUp(e.KeyData), e.KeyData);

    /// <summary>
    /// Enters the menu bar the way Windows does for a key it sees itself: <c>WM_SYSCOMMAND</c> with
    /// <c>SC_KEYMENU</c>, the mnemonic's character for Alt+letter (a menu without that mnemonic
    /// beeps, as it does elsewhere), none for Alt alone and F10. Posted: the key came from inside a
    /// WebView2 event, and the menu's loop must not run inside it.
    /// </summary>
    /// <returns>True when the menu bar takes the key, which then does not reach the page.</returns>
    private bool EnterMenuBar(MenuKeyAction action, Keys keyData) {
        const int WM_SYSCOMMAND = 0x0112;
        const int SC_KEYMENU = 0xF100;

        if (action == MenuKeyAction.None || !IsHandleCreated) {
            return false;
        }

        var character = '\0';

        if (action == MenuKeyAction.OpenMenu) {
            if (MenuKeys.CharacterOf(keyData & Keys.KeyCode) is not { } typed) {
                return false;
            }

            character = typed;
        }

        Log.Debug("Menu bar entered from the document by {Keys}", keyData);

        return NativeMethods.PostMessage(Handle, WM_SYSCOMMAND, (IntPtr)SC_KEYMENU, (IntPtr)character);
    }

    /// <summary>An Alt pressed here and released in another window does not enter this window's menu.</summary>
    protected override void OnDeactivate(EventArgs e) {
        base.OnDeactivate(e);
        _menuKeys.Reset();
    }

    private bool TryRunShortcut(Keys keyData, bool fromDocument = false) {
        if (!HostCommands.TryGetCommand(keyData, out var command)) {
            return false;
        }

        // Backspace means Back only in the document and the window's lists, never in a box the
        // user types in.
        if (keyData == Keys.Back && !fromDocument
            && FocusedControl() is TextBoxBase or ComboBox or UpDownBase) {
            return false;
        }

        // The key may have come from inside a WebView2 event: run the command later, so that a
        // dialog it opens does not start a nested message loop inside that event.
        BeginInvoke(() => RunCommand(command, keyData));
        return true;
    }

    /// <summary>The innermost control of this window that has focus.</summary>
    private Control? FocusedControl() {
        Control? control = ActiveControl;

        while (control is ContainerControl { ActiveControl: { } inner }) {
            control = inner;
        }

        return control;
    }

    private void RunCommand(HostCommand command, Keys keyData) {
        Log.Debug("Host command {Command} from {Keys}", command, keyData);

        switch (command) {
            case HostCommand.Open:
                ShowOpenDialog();
                break;
            case HostCommand.OpenFromClipboard:
                OpenFromClipboard();
                break;
            case HostCommand.OpenFromLink:
                ShowOpenLinkDialog();
                break;
            case HostCommand.OpenInEditor:
                OpenInEditor();
                break;
            case HostCommand.Settings:
                ShowSettings();
                break;
            case HostCommand.Reload:
                ReloadFile();
                break;
            case HostCommand.UserManual:
                ShowUserManual();
                break;
            case HostCommand.About:
                ShowAbout();
                break;
            case HostCommand.ZoomIn:
                SetZoom(DocumentView.StepZoom(documentView.ZoomFactor, 1));
                break;
            case HostCommand.ZoomOut:
                SetZoom(DocumentView.StepZoom(documentView.ZoomFactor, -1));
                break;
            case HostCommand.ResetZoom:
                SetZoom(DocumentView.StepZoom(documentView.ZoomFactor, 0));
                break;
            case HostCommand.Back:
                MoveThroughHistory(back: true);
                break;
            case HostCommand.Forward:
                MoveThroughHistory(back: false);
                break;
            case HostCommand.SwitchPane:
                SwitchPane();
                break;
            case HostCommand.NextNote:
                MoveToNote(forward: true);
                break;
            case HostCommand.PreviousNote:
                MoveToNote(forward: false);
                break;
            case HostCommand.NextBlock:
                MoveToBlock(forward: true);
                break;
            case HostCommand.PreviousBlock:
                MoveToBlock(forward: false);
                break;
            case HostCommand.Undo:
                UndoOrRedo(redo: false);
                break;
            case HostCommand.Redo:
                UndoOrRedo(redo: true);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command, null);
        }
    }
}
