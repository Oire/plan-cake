namespace Oire.PlanCake.Rendering;

/// <summary>
/// Keeps the reading position across a re-render. The page reports only the lines of the last
/// block the user interacted with; the host keeps that block's <see cref="BlockInfo"/> from the
/// previous render and, once the document is rendered again, picks the block to return to here.
/// </summary>
internal static class PositionRestorer {
    /// <summary>
    /// The block of <paramref name="blocks"/> that stands for <paramref name="previous"/>: one of
    /// the same kind with identical full text (the nearest one when there are several), else the
    /// block whose start line is nearest to the previous start line (the earlier one on a tie).
    /// </summary>
    /// <returns><see langword="null"/> without a previous position or without any block.</returns>
    public static BlockInfo? FindTarget(BlockInfo? previous, IReadOnlyList<BlockInfo> blocks) {
        ArgumentNullException.ThrowIfNull(blocks);

        if (previous is null || blocks.Count == 0) {
            return null;
        }

        var sameText = blocks.Where(
            block => block.Kind == previous.Kind && string.Equals(block.Text, previous.Text, StringComparison.Ordinal)
        );

        return Nearest(previous.StartLine, sameText) ?? Nearest(previous.StartLine, blocks);
    }

    /// <summary>
    /// Where the view goes in a document just opened (from a link, the command line, a drop, …,
    /// or Back and Forward): the <paramref name="saved"/> position the history kept for this
    /// file, found again, else the first block. Never the position in the file being left,
    /// and never "leave the virtual cursor where it was": after the page's content is replaced,
    /// JAWS would keep its old place in the buffer, which in a shorter file is the end.
    /// </summary>
    /// <returns><see langword="null"/> only for a document without any block.</returns>
    public static BlockInfo? FindOpeningTarget(BlockInfo? saved, IReadOnlyList<BlockInfo> blocks) {
        ArgumentNullException.ThrowIfNull(blocks);

        return FindTarget(saved, blocks) ?? (blocks.Count > 0 ? blocks[0] : null);
    }

    private static BlockInfo? Nearest(int line, IEnumerable<BlockInfo> candidates) {
        BlockInfo? nearest = null;
        var nearestDistance = int.MaxValue;

        foreach (var block in candidates) {
            var distance = Math.Abs(block.StartLine - line);

            if (distance < nearestDistance) {
                nearest = block;
                nearestDistance = distance;
            }
        }

        return nearest;
    }
}
