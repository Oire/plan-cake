namespace Oire.PlanCake.Utils.Enums;

/// <summary>What the window does when the open file is changed outside PlanCake.</summary>
internal enum ExternalChangeAction {
    /// <summary>Reload it at once, keeping the reading position.</summary>
    AutoReload,

    /// <summary>Ask first; No keeps the view as it is until the user reloads with F5.</summary>
    Ask,
}
