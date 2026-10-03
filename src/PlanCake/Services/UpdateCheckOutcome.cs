namespace Oire.PlanCake.Services;

/// <summary>How an update check ended.</summary>
internal enum UpdateCheckOutcome {
    /// <summary>A newer version exists; NetSparkle's update window has been shown.</summary>
    UpdateAvailable,

    /// <summary>This is the latest version.</summary>
    UpToDate,

    /// <summary>The latest version is one the user chose to skip.</summary>
    Skipped,

    /// <summary>The appcast could not be read or verified (no network, a bad signature, …).</summary>
    Failed,

    /// <summary>The update checks could not be set up in this window (logged), so it never checks.</summary>
    Unavailable,
}
