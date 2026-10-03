namespace Oire.PlanCake.Utils;

/// <summary>
/// Message boxes that follow the active language's reading direction. WinForms mirrors a message
/// box only when told to through <see cref="MessageBoxOptions"/>, and unlike
/// <see cref="Control.RightToLeft"/> that is not an ambient property a form can pass down — so
/// PlanCake routes every message box through here instead of calling <see cref="MessageBox"/> directly.
/// </summary>
internal static class DialogHelper {
    private static MessageBoxOptions DirectionOptions => TextDirection.IsRightToLeft
        ? MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading
        : 0;

    /// <summary>
    /// A message box. Ask a yes-or-no question with <see cref="Confirm"/> instead:
    /// a Yes/No message box cannot be closed with Escape.
    /// </summary>
    public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon) =>
        MessageBox.Show(text, caption, buttons, icon, MessageBoxDefaultButton.Button1, DirectionOptions);

    /// <summary>
    /// Asks a yes-or-no question, Yes being the default button. Escape and the close button
    /// answer No: a Yes/No message box has no cancel, so neither closed it.
    /// </summary>
    /// <returns>True when the user chose Yes.</returns>
    public static bool Confirm(string text, string caption, MessageBoxIcon icon = MessageBoxIcon.Question) =>
        IsYes(TaskDialog.ShowDialog(ConfirmationPage(text, caption, icon), TaskDialogStartupLocation.CenterOwner));

    /// <summary>The task dialog page <see cref="Confirm"/> shows; internal for the tests.</summary>
    internal static TaskDialogPage ConfirmationPage(string text, string caption, MessageBoxIcon icon) {
        var page = new TaskDialogPage {
            Caption = caption,
            Text = text,
            Icon = ToTaskDialogIcon(icon),
            Buttons = { TaskDialogButton.Yes, TaskDialogButton.No },
            DefaultButton = TaskDialogButton.Yes,
            AllowCancel = true,
            RightToLeftLayout = TextDirection.IsRightToLeft,
        };

        return page;
    }

    /// <summary>
    /// The answer a closed confirmation gives: Yes, or No for anything else (No, Escape, the close
    /// button).
    /// </summary>
    internal static bool IsYes(TaskDialogButton? button) => button == TaskDialogButton.Yes;

    private static TaskDialogIcon? ToTaskDialogIcon(MessageBoxIcon icon) => icon switch {
        MessageBoxIcon.None => null,
        MessageBoxIcon.Error => TaskDialogIcon.Error,
        MessageBoxIcon.Warning => TaskDialogIcon.Warning,
        MessageBoxIcon.Information => TaskDialogIcon.Information,

        // A task dialog has no question icon of its own.
        _ => new TaskDialogIcon(SystemIcons.Question),
    };
}
