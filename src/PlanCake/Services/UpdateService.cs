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
/// plans would check five times and could offer the same update five times. When that window
/// closes, one of the others takes the background checks over (<see cref="TakeOverInterval"/>).
/// Help → Check for updates works in every window.
/// </remarks>
internal sealed class UpdateService: IDisposable {
    /// <summary>The named object whose creator does the startup and background checks.</summary>
    internal const string BackgroundChecksName = @"Local\Oire.PlanCake.BackgroundUpdateChecks";

    /// <summary>How often a window that does not do the background checks tries to take them over.</summary>
    internal static readonly TimeSpan TakeOverInterval = TimeSpan.FromMinutes(1);

    private readonly SparkleUpdater _sparkle;

    /// <summary>
    /// The interval Settings asks for now, read again from the file; <see langword="null"/> when
    /// the window cannot take the checks over at the moment.
    /// </summary>
    private readonly Func<UpdateCheckInterval?> _currentInterval;

    private IDisposable? _backgroundChecks;

    /// <summary>Retries the claim while another window does the background checks; null once this one does.</summary>
    private System.Windows.Forms.Timer? _takeOver;

    /// <summary>The interval the background checks are to run at, once this window does them.</summary>
    private UpdateCheckInterval _wantedInterval = UpdateCheckInterval.Never;

    private UpdateCheckInterval _loopInterval = UpdateCheckInterval.Never;
    private bool _loopRunning;
    private bool _disposed;

    private UpdateService(string publicKey, IDisposable? backgroundChecks, Func<UpdateCheckInterval?> currentInterval) {
        _backgroundChecks = backgroundChecks;
        _currentInterval = currentInterval;
        _sparkle = new SparkleUpdater(App.AppcastUrl, new Ed25519Checker(SecurityMode.Strict, publicKey)) {
            UIFactory = new UIFactory(null),
            RelaunchAfterUpdate = false,
            LogWriter = new SerilogSparkleLogWriter(),
            TmpDownloadFileNameWithExtension = $"plancake-update-{Guid.NewGuid()}.exe",
        };

        if (backgroundChecks is null) {
            // Created on the UI thread, like the service, so it ticks there.
            _takeOver = new System.Windows.Forms.Timer { Interval = (int)TakeOverInterval.TotalMilliseconds };
            _takeOver.Tick += (_, _) => TryTakeOverBackgroundChecks();
            _takeOver.Start();
        }

        Log.Information(
            "UpdateService: initialized with appcast {Url}; background checks in this window: {Background}",
            App.AppcastUrl, DoesBackgroundChecks
        );
    }

    /// <summary>
    /// True when this process does the startup and background checks: it is the first PlanCake
    /// window that is still open, or took the background checks over from one that closed.
    /// </summary>
    public bool DoesBackgroundChecks => _backgroundChecks is not null;

    /// <summary>
    /// Creates the service on the UI thread (NetSparkle shows its windows through the thread it
    /// was created on), or returns <see langword="null"/> when NetSparkle cannot be set up, which
    /// is logged: this window then never checks.
    /// </summary>
    /// <param name="currentInterval">
    /// The interval Settings asks for, read again from the file (another window may have changed
    /// it), for a window taking the background checks over; <see langword="null"/> when it cannot
    /// take them over now.
    /// </param>
    public static UpdateService? Create(Func<UpdateCheckInterval?> currentInterval) {
        ArgumentNullException.ThrowIfNull(currentInterval);
        var backgroundChecks = TryClaimBackgroundChecks(BackgroundChecksName);

        try {
            return new UpdateService(App.UpdatePublicKey, backgroundChecks, currentInterval);
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
    /// Takes the background checks over when the window that did them has closed, at the
    /// interval Settings asks for now (<see cref="TryTakeOver"/>). Called by <see cref="_takeOver"/>.
    /// </summary>
    /// <returns>True when this window has just taken the checks over.</returns>
    internal bool TryTakeOverBackgroundChecks() {
        if (_disposed || DoesBackgroundChecks) {
            return false;
        }

        if (TryTakeOver(BackgroundChecksName, _currentInterval) is not var (claim, interval)) {
            return false;
        }

        if (_disposed) {
            claim.Dispose();

            return false;
        }

        _backgroundChecks = claim;
        _wantedInterval = interval;
        _takeOver?.Dispose();
        _takeOver = null;
        Log.Information(
            "UpdateService: the window doing the background checks closed; this one does them now, at {Interval}",
            interval
        );
        ApplyPeriodicChecks();

        return true;
    }

    /// <summary>
    /// Whether a window takes the background checks over, and at what interval. The claim
    /// decides first: of several windows trying at once, one gets it. The interval is then read
    /// through <paramref name="currentInterval"/>, never taken from what this window read
    /// earlier: another window may have changed it (to Never, say) since. When
    /// <paramref name="currentInterval"/> gives <see langword="null"/>, the claim is let go, for
    /// the next attempt.
    /// </summary>
    /// <returns>The claim and the interval, or <see langword="null"/> when the checks are not taken over.</returns>
    internal static (IDisposable Claim, UpdateCheckInterval Interval)? TryTakeOver(
        string name,
        Func<UpdateCheckInterval?> currentInterval
    ) {
        if (TryClaimBackgroundChecks(name) is not { } claim) {
            return null;
        }

        if (currentInterval() is { } interval) {
            return (claim, interval);
        }

        claim.Dispose();

        return null;
    }

    /// <summary>
    /// Starts, stops or re-times the background checks to match <paramref name="interval"/>.
    /// Passing the interval already in effect does nothing; <see cref="UpdateCheckInterval.Never"/>
    /// stops them. The loop does no check when it starts: the startup check is a setting of its
    /// own. Does nothing in a process that does not do the background checks: one that takes
    /// them over reads the interval again then.
    /// </summary>
    public void ConfigurePeriodicChecks(UpdateCheckInterval interval) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _wantedInterval = interval;
        ApplyPeriodicChecks();
    }

    private void ApplyPeriodicChecks() {
        var interval = _wantedInterval;

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
        _takeOver?.Dispose();
        _takeOver = null;
        _sparkle.Dispose();
        _backgroundChecks?.Dispose();
    }
}
