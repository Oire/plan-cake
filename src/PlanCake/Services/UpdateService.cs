using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.SignatureVerifiers;
using NetSparkleUpdater.UI.WinForms;
using Oire.PlanCake.Utils.Enums;
using Serilog;
using static Oire.PlanCake.Utils.Localization;
using App = Oire.PlanCake.Utils.Constants.App;

namespace Oire.PlanCake.Services;

/// <summary>Routes NetSparkle's log output through Serilog, so it ends up in PlanCake's log.</summary>
file sealed class SerilogSparkleLogWriter: NetSparkleUpdater.Interfaces.ILogger {
    public void PrintMessage(string message, params object[]? arguments) =>
        Log.Information("NetSparkle: " + message, arguments);
}

/// <summary>
/// Update checks through NetSparkle, against the Ed25519-signed appcast at
/// <see cref="App.AppcastUrl"/> (ported from SIC!). A check never throws and never stops the app:
/// a failure is logged and reported as <see cref="UpdateCheckOutcome.Failed"/>. The caller
/// decides what to tell the user; only an available update shows a window of its own
/// (NetSparkle's).
/// </summary>
/// <remarks>
/// Every PlanCake window is a process of its own. Only one of them, the first to start, checks
/// on startup and in the background (<see cref="DoesBackgroundChecks"/>); otherwise opening five
/// plans would check five times and could offer the same update five times. Help → Check for
/// updates works in every window.
/// </remarks>
internal sealed class UpdateService: IDisposable {
    /// <summary>The named object whose creator does the startup and background checks.</summary>
    internal const string BackgroundChecksName = @"Local\Oire.PlanCake.BackgroundUpdateChecks";

    private readonly SparkleUpdater _sparkle;
    private readonly IDisposable? _backgroundChecks;
    private UpdateCheckInterval _loopInterval = UpdateCheckInterval.Never;
    private bool _loopRunning;
    private bool _disposed;

    private UpdateService(string publicKey, IDisposable? backgroundChecks) {
        _backgroundChecks = backgroundChecks;
        _sparkle = new SparkleUpdater(App.AppcastUrl, new Ed25519Checker(SecurityMode.Strict, publicKey)) {
            UIFactory = new UIFactory(null),
            RelaunchAfterUpdate = false,
            LogWriter = new SerilogSparkleLogWriter(),
            TmpDownloadFileNameWithExtension = $"plancake-update-{Guid.NewGuid()}.exe",
        };

        Log.Information(
            "UpdateService: initialized with appcast {Url}; background checks in this window: {Background}",
            App.AppcastUrl, DoesBackgroundChecks
        );
    }

    /// <summary>
    /// True when this process does the startup and background checks: it is the first PlanCake
    /// window that is still open.
    /// </summary>
    public bool DoesBackgroundChecks => _backgroundChecks is not null;

    /// <summary>
    /// Creates the service on the UI thread (NetSparkle shows its windows through the thread it
    /// was created on), or returns <see langword="null"/> when NetSparkle cannot be set up, which
    /// is logged: this window then never checks.
    /// </summary>
    public static UpdateService? Create() {
        var backgroundChecks = TryClaimBackgroundChecks(BackgroundChecksName);

        try {
            return new UpdateService(App.UpdatePublicKey, backgroundChecks);
        } catch (Exception ex) {
            Log.Error(ex, "UpdateService: unable to initialize; update checks are off");
            backgroundChecks?.Dispose();

            return null;
        }
    }

    /// <summary>
    /// Claims the startup and background checks for this process: the claim is a named object
    /// that exists while its creator keeps it. Returns <see langword="null"/> when another
    /// process holds it (or it cannot be created, which is logged).
    /// </summary>
    internal static IDisposable? TryClaimBackgroundChecks(string name) {
        try {
            var claim = new Mutex(initiallyOwned: false, name, out var createdNew);

            if (createdNew) {
                return claim;
            }

            claim.Dispose();
        } catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException) {
            Log.Warning(ex, "UpdateService: unable to claim the background update checks");
        }

        return null;
    }

    /// <summary>
    /// Starts, stops or re-times the background checks to match <paramref name="interval"/>.
    /// Passing the interval already in effect does nothing; <see cref="UpdateCheckInterval.Never"/>
    /// stops them. The loop does no check when it starts: the startup check is a setting of its
    /// own. Does nothing in a process that does not do the background checks.
    /// </summary>
    public void ConfigurePeriodicChecks(UpdateCheckInterval interval) {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!DoesBackgroundChecks || interval == _loopInterval) {
            return;
        }

        try {
            // NetSparkle takes the frequency when the loop starts: a new one means stop, then start.
            if (_loopRunning) {
                _sparkle.StopLoop();
                _loopRunning = false;
            }

            if (ToFrequency(interval) is { } frequency) {
                _sparkle.StartLoop(doInitialCheck: false, forceInitialCheck: false, frequency);
                _loopRunning = true;
                Log.Information("UpdateService: background checks every {Frequency}", frequency);
            } else {
                Log.Information("UpdateService: background checks off");
            }

            _loopInterval = interval;
        } catch (Exception ex) {
            Log.Error(ex, "UpdateService: unable to set the background checks to {Interval}", interval);
        }
    }

    /// <summary>The period of the background checks, or <see langword="null"/> for none.</summary>
    internal static TimeSpan? ToFrequency(UpdateCheckInterval interval) => interval switch {
        UpdateCheckInterval.Daily => TimeSpan.FromDays(1),
        UpdateCheckInterval.EveryThreeDays => TimeSpan.FromDays(3),
        UpdateCheckInterval.Weekly => TimeSpan.FromDays(7),
        UpdateCheckInterval.Monthly => TimeSpan.FromDays(30),
        _ => null,
    };

    /// <summary>
    /// Reads the appcast once. When an update is available, NetSparkle's update window is shown;
    /// every other outcome is only returned (and logged), for the caller to announce or not.
    /// Never throws: a failure (no network, an unreachable server, a bad signature) is
    /// <see cref="UpdateCheckOutcome.Failed"/>.
    /// </summary>
    public async Task<UpdateCheckOutcome> CheckForUpdatesAsync() {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Log.Information("UpdateService: checking for updates");

        try {
            var result = await _sparkle.CheckForUpdatesQuietly();
            var status = result?.Status ?? UpdateStatus.CouldNotDetermine;
            Log.Information("UpdateService: update check result: {Status}", status);

            if (status == UpdateStatus.UpdateAvailable && !_disposed) {
                _sparkle.ShowUpdateNeededUI(result!.Updates);
            }

            return ToOutcome(status);
        } catch (Exception ex) {
            // Most often a network failure. Never fatal.
            Log.Warning(ex, "UpdateService: update check failed");

            return UpdateCheckOutcome.Failed;
        }
    }

    /// <summary>What a NetSparkle status means for PlanCake.</summary>
    internal static UpdateCheckOutcome ToOutcome(UpdateStatus status) => status switch {
        UpdateStatus.UpdateAvailable => UpdateCheckOutcome.UpdateAvailable,
        UpdateStatus.UpdateNotAvailable => UpdateCheckOutcome.UpToDate,
        UpdateStatus.UserSkipped => UpdateCheckOutcome.Skipped,
        _ => UpdateCheckOutcome.Failed,
    };

    /// <summary>
    /// What Help → Check for updates tells the user after <paramref name="outcome"/>, in the
    /// interface language; <see langword="null"/> when an update is available (NetSparkle's
    /// window says so).
    /// </summary>
    internal static string? Describe(UpdateCheckOutcome outcome) => outcome switch {
        UpdateCheckOutcome.UpdateAvailable => null,
        UpdateCheckOutcome.UpToDate => _("PlanCake is up to date."),
        UpdateCheckOutcome.Skipped => _("The latest version of PlanCake is one you chose to skip."),
        UpdateCheckOutcome.Unavailable => _("Update checks could not be started. The log has the details."),
        _ => _("Unable to check for updates. Please try again later."),
    };

    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        _sparkle.Dispose();
        _backgroundChecks?.Dispose();
    }
}
