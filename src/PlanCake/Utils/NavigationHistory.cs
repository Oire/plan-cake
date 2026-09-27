using Oire.PlanCake.Rendering;

namespace Oire.PlanCake.Utils;

/// <summary>A file in the history, and where the user was in it.</summary>
/// <param name="Path">The file's full path.</param>
/// <param name="Position">
/// The block the user last interacted with, which <see cref="PositionRestorer"/> returns to;
/// <see langword="null"/> for the top of the document.
/// </param>
internal sealed record HistoryEntry(string Path, BlockInfo? Position);

/// <summary>How a move through the history ended.</summary>
internal enum HistoryOutcome {
    /// <summary>The previous (or next) file was opened.</summary>
    Moved,

    /// <summary>There is no file in that direction (any more).</summary>
    AtEnd,

    /// <summary>The file exists but could not be opened; the history is unchanged.</summary>
    OpenFailed,
}

/// <summary>The result of <see cref="NavigationHistory.GoBack"/> or <see cref="NavigationHistory.GoForward"/>.</summary>
/// <param name="Outcome">How the move ended.</param>
/// <param name="Missing">
/// The files skipped on the way because they no longer exist; they were dropped from the history.
/// </param>
internal sealed record HistoryMove(HistoryOutcome Outcome, IReadOnlyList<string> Missing);

/// <summary>
/// The window's history of visited files, like a browser's: opening another file pushes the one
/// being left, with its reading position, and clears the forward list; Back and Forward move
/// through it. It opens nothing itself: the window passes in how to check and open a file.
/// </summary>
internal sealed class NavigationHistory {
    // Both lists keep the nearest entry last.
    private readonly List<HistoryEntry> _back = [];
    private readonly List<HistoryEntry> _forward = [];

    public bool CanGoBack => _back.Count > 0;

    public bool CanGoForward => _forward.Count > 0;

    /// <summary>
    /// Records that the user left <paramref name="current"/> for another file by any route but
    /// Back and Forward: it becomes the previous file, and the forward list is cleared.
    /// </summary>
    public void Push(HistoryEntry current) {
        ArgumentNullException.ThrowIfNull(current);
        _back.Add(current);
        _forward.Clear();
    }

    /// <summary>Opens the previous file; <paramref name="current"/> becomes the next one.</summary>
    /// <param name="current">The file open now, or <see langword="null"/> when none is.</param>
    /// <param name="exists">Whether a file still exists.</param>
    /// <param name="open">Opens an entry at its position; false when it could not.</param>
    public HistoryMove GoBack(HistoryEntry? current, Func<string, bool> exists, Func<HistoryEntry, bool> open) =>
        Move(_back, _forward, current, exists, open);

    /// <summary>Opens the next file; <paramref name="current"/> becomes the previous one.</summary>
    /// <inheritdoc cref="GoBack" path="/param"/>
    public HistoryMove GoForward(HistoryEntry? current, Func<string, bool> exists, Func<HistoryEntry, bool> open) =>
        Move(_forward, _back, current, exists, open);

    private static HistoryMove Move(
        List<HistoryEntry> from,
        List<HistoryEntry> to,
        HistoryEntry? current,
        Func<string, bool> exists,
        Func<HistoryEntry, bool> open
    ) {
        ArgumentNullException.ThrowIfNull(exists);
        ArgumentNullException.ThrowIfNull(open);
        var missing = new List<string>();

        while (from.Count > 0) {
            var entry = from[^1];

            if (!exists(entry.Path)) {
                from.RemoveAt(from.Count - 1);
                missing.Add(entry.Path);
                continue;
            }

            if (!open(entry)) {
                return new HistoryMove(HistoryOutcome.OpenFailed, missing);
            }

            from.RemoveAt(from.Count - 1);

            if (current is not null) {
                to.Add(current);
            }

            return new HistoryMove(HistoryOutcome.Moved, missing);
        }

        return new HistoryMove(HistoryOutcome.AtEnd, missing);
    }
}
