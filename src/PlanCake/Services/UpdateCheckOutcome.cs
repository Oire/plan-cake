namespace Oire.PlanCake.Services;

/// <summary>How an update check ended.</summary>
public enum UpdateCheckOutcome {
    /// <summary>A newer version exists; NetSparkle's update window has been shown.</summary>
    UpdateAvailable,

    /// <summary>This is the latest version.</summary>
    UpToDate,

    /// <summary>The latest version is one the user chose to skip.</summary>
    Skipped,

    /// <summary>The appcast could not be read or verified (no network, a bad signature, …).</summary>
    Failed,

    /// <summary>This build has no usable update key, so it never checks.</summary>
    NotConfigured,
}
