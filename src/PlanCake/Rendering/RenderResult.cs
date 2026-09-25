using Oire.PlanCake.Notes;

namespace Oire.PlanCake.Rendering;

/// <summary>A note of the rendered document and the block it follows.</summary>
/// <param name="Index">The note's position in source order; the page's <c>data-note</c> value.</param>
/// <param name="Note">The note as the parser found it.</param>
/// <param name="Block">
/// The block the note is anchored to, or <see langword="null"/> for a note before the first block,
/// which is shown at the top of the document.
/// </param>
internal sealed record RenderedNote(int Index, Note Note, BlockInfo? Block);

/// <summary>What <see cref="MarkdownRenderer.Render"/> produces.</summary>
/// <param name="Html">
/// In <see cref="RenderMode.Interactive"/> mode the document body, for the page's <c>main</c>
/// element; in <see cref="RenderMode.Export"/> mode a complete standalone HTML document.
/// </param>
/// <param name="Blocks">Every annotatable block, in document order.</param>
/// <param name="Notes">Every note, in source order, with its anchor.</param>
/// <param name="Title">The plain text of the first heading, or <see langword="null"/> without one.</param>
/// <param name="Parse">The notes and stripped source the document was rendered from.</param>
internal sealed record RenderResult(
    string Html,
    IReadOnlyList<BlockInfo> Blocks,
    IReadOnlyList<RenderedNote> Notes,
    string? Title,
    NoteParseResult Parse
);
