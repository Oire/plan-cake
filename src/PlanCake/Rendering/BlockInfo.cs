namespace Oire.PlanCake.Rendering;

/// <summary>The kind of an annotatable block.</summary>
internal enum BlockKind {
    Paragraph,
    Heading,
    ListItem,
    Code,
    TableRow,
}

/// <summary>
/// A block of the rendered document the user can land on and annotate, per Technical details →
/// "Annotatable blocks" in the PlanCake plan.
/// </summary>
/// <param name="Kind">What the block is.</param>
/// <param name="StartLine">The 1-based original line the block starts on.</param>
/// <param name="EndLine">The 1-based original line the block ends on, inclusive.</param>
/// <param name="Text">The block's full plain text (table cells joined with <c> | </c>).</param>
/// <param name="Excerpt">
/// The first sentence of the plain text on one line, whitespace collapsed; a sentence longer than
/// <see cref="MarkdownRenderer.ExcerptLength"/> characters is cut at a comma, semicolon, colon or
/// dash, or else at the end of a word, with an ellipsis.
/// </param>
internal sealed record BlockInfo(BlockKind Kind, int StartLine, int EndLine, string Text, string Excerpt) {
    /// <summary>The range as the page's <c>data-lines</c> attribute holds it: <c>start-end</c>.</summary>
    public string Lines => $"{StartLine}-{EndLine}";
}
