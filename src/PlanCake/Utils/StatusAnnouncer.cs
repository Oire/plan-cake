using System.Windows.Forms.Automation;

namespace Oire.PlanCake.Utils;

/// <summary>
/// Shows a status message in the status strip and has the screen reader speak it. A status
/// label alone is silent: JAWS reads it only when asked. The UI Automation notification is what
/// makes the message heard wherever focus is, the WebView2 document included.
/// </summary>
internal sealed class StatusAnnouncer {
    private readonly Control _notifier;
    private readonly ToolStripStatusLabel _label;

    /// <param name="notifier">The control that raises the notification. It must have a handle
    /// by the time <see cref="Announce"/> is called; the status strip that holds
    /// <paramref name="label"/> is the natural choice.</param>
    /// <param name="label">The status-strip label that keeps the last message on screen.</param>
    public StatusAnnouncer(Control notifier, ToolStripStatusLabel label) {
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(label);
        _notifier = notifier;
        _label = label;
    }

    /// <summary>Sets the status text and speaks it. Safe to call from any thread.</summary>
    public void Announce(string message) {
        if (_notifier.IsDisposed) {
            return;
        }

        if (_notifier.InvokeRequired) {
            _notifier.BeginInvoke(() => Announce(message));
            return;
        }

        _label.Text = message;

        if (_notifier.IsHandleCreated) {
            _notifier.AccessibilityObject.RaiseAutomationNotification(
                AutomationNotificationKind.ActionCompleted,
                AutomationNotificationProcessing.ImportantMostRecent,
                message
            );
        }
    }
}
