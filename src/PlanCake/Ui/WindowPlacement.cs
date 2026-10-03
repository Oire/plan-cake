namespace Oire.PlanCake.Ui;

/// <summary>
/// Where the main window opens: the bounds saved when the last window closed, kept on a screen
/// that is still there, or on first run a size that fits the screen it opens on. Pure, for the tests.
/// </summary>
internal static class WindowPlacement {
    /// <summary>The share of the work area a first window takes at most, in each direction.</summary>
    public const double FirstRunShare = 0.9;

    /// <summary>
    /// The first window's size: <paramref name="preferred"/>, but no more than
    /// <see cref="FirstRunShare"/> of <paramref name="workArea"/> in either direction, so the
    /// status bar never opens behind the taskbar (1000 by 700 at 150% is taller than a 1080p screen).
    /// </summary>
    public static Size FirstRunSize(Size preferred, Rectangle workArea) => new(
        Math.Min(preferred.Width, (int)(workArea.Width * FirstRunShare)),
        Math.Min(preferred.Height, (int)(workArea.Height * FirstRunShare))
    );

    /// <summary>
    /// <paramref name="saved"/> moved and shrunk as little as needed to lie whole on one of
    /// <paramref name="workAreas"/>: the one it overlaps most, or the first (the primary screen)
    /// when it overlaps none, a screen having gone since, where it is centered. Never smaller than
    /// <paramref name="minimum"/> unless the work area is.
    /// </summary>
    /// <returns><see langword="null"/> when nothing was saved (an empty size) or there is no screen.</returns>
    public static Rectangle? Restore(Rectangle saved, IReadOnlyList<Rectangle> workAreas, Size minimum) {
        ArgumentNullException.ThrowIfNull(workAreas);

        if (saved.Width <= 0 || saved.Height <= 0 || workAreas.Count == 0) {
            return null;
        }

        var area = workAreas[0];
        var overlap = 0L;

        foreach (var candidate in workAreas) {
            var common = Rectangle.Intersect(candidate, saved);
            var size = (long)common.Width * common.Height;

            if (size > overlap) {
                overlap = size;
                area = candidate;
            }
        }

        var width = Math.Min(Math.Max(saved.Width, minimum.Width), area.Width);
        var height = Math.Min(Math.Max(saved.Height, minimum.Height), area.Height);

        if (overlap == 0) {
            return new Rectangle(
                area.Left + (area.Width - width) / 2,
                area.Top + (area.Height - height) / 2,
                width,
                height
            );
        }

        return new Rectangle(
            Math.Clamp(saved.Left, area.Left, area.Right - width),
            Math.Clamp(saved.Top, area.Top, area.Bottom - height),
            width,
            height
        );
    }

    /// <summary>
    /// The splitter distance that gives the notes list <paramref name="listWidth"/> of
    /// <paramref name="available"/> (the room both panes share), within both panes' minimums;
    /// <see langword="null"/> when no width was saved or there is no room to honor it.
    /// </summary>
    public static int? SplitterDistance(int available, int listWidth, int listMinimum, int documentMinimum) {
        if (listWidth <= 0 || available - documentMinimum < listMinimum) {
            return null;
        }

        return available - Math.Clamp(listWidth, listMinimum, available - documentMinimum);
    }
}
