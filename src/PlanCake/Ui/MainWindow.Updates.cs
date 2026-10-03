using Oire.PlanCake.Services;
using Oire.PlanCake.Utils;
using Oire.PlanCake.Utils.Enums;
using static Oire.PlanCake.Utils.Localization;

namespace Oire.PlanCake.Ui;

internal sealed partial class MainWindow {
    /// <summary>
    /// Sets up the update checks once the window is visible: the silent startup check and the
    /// background checks, each as Settings says, in the first PlanCake window only
    /// (<see cref="UpdateService.DoesBackgroundChecks"/>); another window takes the background
    /// checks over when that one closes.
    /// </summary>
    protected override void OnShown(EventArgs e) {
        base.OnShown(e);
        InitializeUpdates();
    }

    private void InitializeUpdates() {
        if (_updatesInitialized || StartupFailed || IsDisposed) {
            return;
        }

        _updatesInitialized = true;
        _updateService = UpdateService.Create(CurrentUpdateCheckInterval);

        // A window that does not do the background checks may take them over later, reading the
        // interval again then (CurrentUpdateCheckInterval).
        if (_updateService is not { DoesBackgroundChecks: true } updates) {
            return;
        }

        updates.ConfigurePeriodicChecks(Config.General.UpdateCheckInterval);

        if (Config.General.CheckForUpdatesOnStartup) {
            // Fire and forget: the check never throws, says nothing unless there is an update,
            // and never takes the focus otherwise. A named local, since _ is the gettext method here.
            var startupCheck = updates.CheckForUpdatesAsync();
            GC.KeepAlive(startupCheck);
        }
    }

    /// <summary>
    /// The update check interval for this window taking the background checks over from one that
    /// closed: the settings file is read again first, as on activation, since another window may
    /// have changed it; a file that cannot be read keeps the settings in memory.
    /// </summary>
    /// <returns>
    /// The interval, or <see langword="null"/> while a dialog of this window is open: the action
    /// that opened it must finish with the settings the user saw (see <see cref="OnActivated"/>).
    /// </returns>
    private UpdateCheckInterval? CurrentUpdateCheckInterval() {
        if (StartupFailed || IsDisposed || !IsHandleCreated || !NativeMethods.IsWindowEnabled(Handle)) {
            return null;
        }

        ReloadSettingsIfChanged();

        return Config.General.UpdateCheckInterval;
    }

    /// <summary>
    /// Help → Check for updates: an available update shows NetSparkle's window; every other
    /// outcome (up to date, skipped, no network, checks that could not be set up) is said in a
    /// message box.
    /// </summary>
    private async void CheckForUpdates() {
        if (_checkingUpdates) {
            return;
        }

        _checkingUpdates = true;

        try {
            var outcome = _updateService is { } updates
                ? await updates.CheckForUpdatesAsync()
                : UpdateCheckOutcome.Unavailable;

            if (IsDisposed) {
                return;
            }

            if (UpdateService.Describe(outcome) is { } message) {
                var icon = outcome is UpdateCheckOutcome.UpToDate or UpdateCheckOutcome.Skipped
                    ? MessageBoxIcon.Information
                    : MessageBoxIcon.Warning;
                DialogHelper.Show(message, _("Check for updates"), MessageBoxButtons.OK, icon);
                ReturnFocus();
            }
        } finally {
            _checkingUpdates = false;
        }
    }
}
