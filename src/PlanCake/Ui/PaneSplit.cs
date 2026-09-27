namespace Oire.PlanCake.Ui;

/// <summary>
/// View → Wider notes list / Narrower notes list: the notes list's share of the room it shares
/// with the document moves a step of <see cref="StepPercent"/> at a time, snapped to whole steps
/// so the share read out is a round number, and stays within both panes' minimum sizes so neither
/// disappears. Pure, for the tests.
/// </summary>
internal static class PaneSplit {
    /// <summary>One step, in percent of the room both panes share.</summary>
    public const int StepPercent = 10;

    /// <summary>
    /// How far off a whole step a share may be and still count as on it, in steps: a snapped size
    /// is rounded to whole pixels, so 40% can come back as 39.9%.
    /// </summary>
    private const double Tolerance = 0.05;

    /// <summary>The notes list's size one step larger or smaller.</summary>
    /// <param name="available">The room both panes share, without the splitter.</param>
    /// <param name="list">The notes list's current size.</param>
    /// <param name="listMinimum">The smallest the notes list may get.</param>
    /// <param name="documentMinimum">The smallest the document may get.</param>
    /// <param name="larger">True for a larger notes list, false for a smaller one.</param>
    /// <returns>
    /// The new size, clamped to the minimums; the current size when there is no room to move
    /// (a window too small for both minimums).
    /// </returns>
    public static int Resize(int available, int list, int listMinimum, int documentMinimum, bool larger) {
        var largest = available - documentMinimum;

        if (available <= 0 || largest < listMinimum) {
            return list;
        }

        var steps = (double)list * 100 / available / StepPercent;
        var target = larger ? Math.Floor(steps + Tolerance) + 1 : Math.Ceiling(steps - Tolerance) - 1;
        var size = (int)Math.Round(target * StepPercent * available / 100);

        return Math.Clamp(size, listMinimum, largest);
    }

    /// <summary>The notes list's share of <paramref name="available"/>, in whole percent.</summary>
    public static int Percent(int available, int list) =>
        available <= 0 ? 0 : (int)Math.Round((double)list * 100 / available);
}
