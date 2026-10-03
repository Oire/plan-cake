namespace Oire.PlanCake.Utils.Enums;

/// <summary>
/// How often PlanCake checks for updates in the background while it runs. Independent of the
/// one-shot "check on startup" setting. <see cref="Never"/> turns the background checks off.
/// Declared most frequent first, the order of the Settings combo box; the default
/// (<see cref="Weekly"/>) is set in <c>Config</c>, not by the declaration order.
/// </summary>
internal enum UpdateCheckInterval {
    /// <summary>Every 24 hours.</summary>
    Daily,

    /// <summary>Every 3 days.</summary>
    EveryThreeDays,

    /// <summary>Every 7 days.</summary>
    Weekly,

    /// <summary>Every 30 days.</summary>
    Monthly,

    /// <summary>Background checks off.</summary>
    Never,
}
