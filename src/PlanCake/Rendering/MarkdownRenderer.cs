using System.Globalization;
using System.Text;
using Markdig;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Helpers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Oire.PlanCake.Notes;

namespace Oire.PlanCake.Rendering;

/// <summary>What the rendered HTML is for.</summary>
internal enum RenderMode {
    /// <summary>The window's document view: notes are user notes the page can activate.</summary>
    Interactive,

    /// <summary>A standalone HTML file: notes are static <c>role="note"</c> elements.</summary>
    Export,
}

/// <summary>How <see cref="MarkdownRenderer.Render"/> renders a source.</summary>
/// <param name="Markers">The markers notes are written with.</param>
/// <param name="Mode">Interactive view or standalone export.</param>
/// <param name="Strings">The localized strings written into the document.</param>
/// <param name="DocumentLanguage">
/// The <c>lang</c> of an exported document. The interactive page sets it on its container instead.
/// </param>
internal sealed record RenderOptions(
    NoteMarkers Markers,
    RenderMode Mode,
    RenderStrings Strings,
    string DocumentLanguage = "en"
);

/// <summary>
/// Renders a Markdown source to HTML with its notes taken out and put back after the blocks
/// they annotate, every annotatable block stamped with its original source line range, per
/// Technical details → "Annotatable blocks" and "Note placement in the view" in the PlanCake plan.
/// </summary>
internal static class MarkdownRenderer {
    /// <summary>The longest a <see cref="BlockInfo.Excerpt"/> gets, ellipsis included.</summary>
    public const int ExcerptLength = 80;

    /// <summary>The content security policy of an exported file: no script at all.</summary>
    public const string ExportContentSecurityPolicy =
        "default-src 'none'; script-src 'none'; style-src 'unsafe-inline'; img-src * data:";

    private const string ExportStyle = """
        body { font-family: "Segoe UI", sans-serif; line-height: 1.5; max-width: 50em; margin: 1em auto; }
        pre { overflow-x: auto; padding: 0.5em; border: 1px solid; }
        table { border-collapse: collapse; }
        th, td { border: 1px solid; padding: 0.25em 0.5em; }
        .note { border-inline-start: 0.3em solid; margin: 0.5em 0; padding: 0.25em 0.75em; font-style: italic; }
        .note > :first-child { margin-top: 0; }
        .note > :last-child { margin-bottom: 0; }
        """;

    private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UsePreciseSourceLocation()
        .Build();

    /// <summary>
    /// The pipeline a note's own text is rendered with: the document's extensions, except that
    /// raw HTML is shown as text (a note cannot fake the page's own attributes), a line break the
    /// user typed stays a line break, and footnotes are left out (their ids would clash with the
    /// document's).
    /// </summary>
    private static readonly MarkdownPipeline _notePipeline = BuildNotePipeline();

    private static MarkdownPipeline BuildNotePipeline() {
        var builder = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseSoftlineBreakAsHardlineBreak()
            .DisableHtml();
        builder.Extensions.RemoveAll(extension => extension is FootnoteExtension);

        return builder.Build();
    }

    /// <summary>A block the user can annotate, and the AST node its notes are inserted after.</summary>
    private sealed record Annotatable(BlockInfo Info, Block Target);

    public static RenderResult Render(string source, RenderOptions options) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);

        var parse = NoteParser.Parse(source, options.Markers);
        var document = Markdown.Parse(parse.StrippedSource, _pipeline);
        var walker = new BlockWalker(parse);
        walker.Walk(document);

        var notes = InsertNotes(document, parse.Notes, walker.Blocks, options);
        var body = ToHtml(document, options.Mode);
        var title = walker.Title;
        var html = options.Mode == RenderMode.Export
            ? ExportDocument(body, title, options.DocumentLanguage)
            : body;

        return new RenderResult(html, walker.Blocks.Select(block => block.Info).ToList(), notes, title, parse);
    }

    private static string ToHtml(MarkdownDocument document, RenderMode mode) {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var renderer = new HtmlRenderer(writer);
        _pipeline.Setup(renderer);

        // The window's task-list check boxes can be toggled (Task 7a); an exported file's stay
        // disabled, as Markdig renders them.
        if (mode == RenderMode.Interactive) {
            renderer.ObjectRenderers.Replace<HtmlTaskListRenderer>(new EnabledTaskListRenderer());
        }

        // The block's attributes (data-lines, dir) belong on the <pre> the user lands on, not on
        // the <code> inside it.
        if (renderer.ObjectRenderers.FindExact<CodeBlockRenderer>() is { } codeRenderer) {
            codeRenderer.OutputAttributesOnPre = true;
        }

        renderer.Render(document);
        writer.Flush();

        return writer.ToString();
    }

    private static List<RenderedNote> InsertNotes(
        MarkdownDocument document,
        IReadOnlyList<Note> notes,
        List<Annotatable> blocks,
        RenderOptions options
    ) {
        var rendered = new List<RenderedNote>(notes.Count);
        var insertedAfter = new Dictionary<Block, int>(ReferenceEqualityComparer.Instance);
        var insertedAtTop = 0;

        for (var index = 0; index < notes.Count; index++) {
            var note = notes[index];
            var anchor = FindAnchor(blocks, note.StartLine);
            var noteBlock = CreateHtmlBlock(NoteHtml(note, index, options));

            if (anchor is null) {
                document.Insert(insertedAtTop++, noteBlock);
            } else if (anchor.Target is TableRow row) {
                // A <div> cannot sit between table rows, so the note goes into the row's last cell.
                var container = row.Count > 0 && row[^1] is ContainerBlock lastCell ? lastCell : row;
                container.Add(noteBlock);
            } else {
                var parent = anchor.Target.Parent
                    ?? throw new InvalidOperationException("An annotatable block has no parent.");
                var alreadyInserted = insertedAfter.GetValueOrDefault(anchor.Target);
                parent.Insert(parent.IndexOf(anchor.Target) + 1 + alreadyInserted, noteBlock);
                insertedAfter[anchor.Target] = alreadyInserted + 1;
            }

            rendered.Add(new RenderedNote(index, note, anchor?.Info));
        }

        return rendered;
    }

    /// <summary>
    /// The block containing <paramref name="noteLine"/> (a note written by hand inside a block),
    /// else the last block ending before it, else <see langword="null"/>.
    /// </summary>
    private static Annotatable? FindAnchor(List<Annotatable> blocks, int noteLine) {
        Annotatable? anchor = null;

        foreach (var block in blocks) {
            if (block.Info.StartLine <= noteLine && noteLine <= block.Info.EndLine) {
                return block;
            }

            if (block.Info.EndLine < noteLine && (anchor is null || block.Info.EndLine > anchor.Info.EndLine)) {
                anchor = block;
            }
        }

        return anchor;
    }

    private static HtmlBlock CreateHtmlBlock(string html) {
        var block = new HtmlBlock(null) { Type = HtmlBlockType.NonInterruptingBlock };
        block.Lines = new StringLineGroup(1);
        block.Lines.Add(new StringSlice(html));

        return block;
    }

    private static string NoteHtml(Note note, int index, RenderOptions options) {
        var strings = options.Strings;
        var roleDescriptions =
            $"""role="note" aria-roledescription="{HtmlEncode(strings.NoteRoleDescription)}" """
            + $"""aria-brailleroledescription="{HtmlEncode(strings.NoteBrailleRoleDescription)}" """;

        if (options.Mode == RenderMode.Export) {
            return $"""<div class="note" {roleDescriptions}dir="auto">{NoteBlockHtml(note.Text)}</div>""";
        }

        var noteIndex = index.ToString(CultureInfo.InvariantCulture);

        return $"""<div class="note" {roleDescriptions}data-note="{noteIndex}" dir="auto">"""
            + $"{NoteBlockHtml(note.Text)}</div>";
    }

    /// <summary>
    /// A note's text rendered as Markdown for a <c>role="note"</c> element: every block gets
    /// <c>dir="auto"</c>, none gets <c>data-lines</c>, and a heading becomes a bold paragraph so
    /// that it never joins the document's heading navigation.
    /// </summary>
    internal static string NoteBlockHtml(string text) {
        var document = Markdown.Parse(text, _notePipeline);

        foreach (var block in document.Descendants<Block>()) {
            block.GetAttributes().AddPropertyIfNotExist("dir", "auto");
        }

        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var renderer = new HtmlRenderer(writer);
        _notePipeline.Setup(renderer);
        renderer.ObjectRenderers.Replace<HeadingRenderer>(new BoldParagraphHeadingRenderer());

        if (renderer.ObjectRenderers.FindExact<CodeBlockRenderer>() is { } codeRenderer) {
            codeRenderer.OutputAttributesOnPre = true;
        }

        renderer.Render(document);
        writer.Flush();

        return writer.ToString().TrimEnd('\n');
    }

    /// <summary>
    /// A note's text as plain text, for places that show a note on one line (the delete
    /// confirmation, the notes list): no Markdown punctuation for a screen reader to read out.
    /// </summary>
    internal static string NotePlainText(string text) {
        var document = Markdown.Parse(text, _notePipeline);

        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var renderer = new HtmlRenderer(writer) {
            EnableHtmlForBlock = false,
            EnableHtmlForInline = false,
            EnableHtmlEscape = false,
        };
        _notePipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();

        return writer.ToString().Trim();
    }

    /// <summary>
    /// A task-list check box the user can toggle: enabled, with a class the page finds it by. The
    /// page sends the toggle to the host, which rewrites the marker in the file.
    /// </summary>
    private sealed class EnabledTaskListRenderer: HtmlObjectRenderer<TaskList> {
        /// <summary>The class the page finds a task-list check box by.</summary>
        public const string CheckboxClass = "task-list-item-checkbox";

        protected override void Write(HtmlRenderer renderer, TaskList obj) {
            if (!renderer.EnableHtmlForInline) {
                renderer.Write(obj.Checked ? "[x]" : "[ ]");
                return;
            }

            renderer.Write($"<input class=\"{CheckboxClass}\" type=\"checkbox\"");

            if (obj.Checked) {
                renderer.Write(" checked=\"checked\"");
            }

            renderer.Write(" />");
        }
    }

    /// <summary>A heading inside a note: a bold paragraph, so it is not a heading of the document.</summary>
    private sealed class BoldParagraphHeadingRenderer: HtmlObjectRenderer<HeadingBlock> {
        protected override void Write(HtmlRenderer renderer, HeadingBlock obj) {
            renderer.EnsureLine();
            renderer.Write("<p dir=\"auto\"><strong>");
            renderer.WriteLeafInline(obj);
            renderer.Write("</strong></p>");
            renderer.WriteLine();
        }
    }

    private static string ExportDocument(string body, string? title, string language) => $"""
        <!DOCTYPE html>
        <html lang="{HtmlEncode(language)}">
        <head>
        <meta charset="utf-8">
        <meta http-equiv="Content-Security-Policy" content="{ExportContentSecurityPolicy}">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{HtmlEncode(title ?? string.Empty)}</title>
        <style>
        {ExportStyle}
        </style>
        </head>
        <body>
        <main>
        {body}</main>
        </body>
        </html>

        """;

    /// <summary>
    /// Encodes the five characters that matter in HTML text and attribute values, and nothing
    /// else, so accented and non-Latin text stays readable in the output.
    /// </summary>
    internal static string HtmlEncode(string text) {
        if (text.AsSpan().IndexOfAny("<>&\"'") < 0) {
            return text;
        }

        var encoded = new StringBuilder(text.Length + 16);

        foreach (var c in text) {
            encoded.Append(c switch {
                '<' => "&lt;",
                '>' => "&gt;",
                '&' => "&amp;",
                '"' => "&quot;",
                '\'' => "&#39;",
                _ => c.ToString(),
            });
        }

        return encoded.ToString();
    }

    /// <summary>
    /// Makes a one-line excerpt of <paramref name="text"/>: whitespace runs become one space, and
    /// text longer than <see cref="ExcerptLength"/> is cut to fit with an ellipsis.
    /// </summary>
    internal static string Excerpt(string text) {
        var line = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (line.Length <= ExcerptLength) {
            return line;
        }

        var cut = ExcerptLength - 1;

        // Never split a surrogate pair.
        if (char.IsHighSurrogate(line[cut - 1])) {
            cut--;
        }

        return string.Concat(line.AsSpan(0, cut).TrimEnd(), "…");
    }

    /// <summary>
    /// Walks the syntax tree, stamps every block with <c>dir="auto"</c> and every annotatable block
    /// with <c>data-lines</c>, and collects the annotatable blocks in document order.
    /// </summary>
    private sealed class BlockWalker {
        private readonly NoteParseResult _parse;
        private readonly int[] _lineStarts;
        private readonly StringWriter _plainWriter = new(CultureInfo.InvariantCulture);
        private readonly HtmlRenderer _plainRenderer;

        public BlockWalker(NoteParseResult parse) {
            _parse = parse;
            _lineStarts = LineStarts(parse.StrippedSource);
            _plainRenderer = new HtmlRenderer(_plainWriter) {
                EnableHtmlForBlock = false,
                EnableHtmlForInline = false,
                EnableHtmlEscape = false,
            };
            _pipeline.Setup(_plainRenderer);
        }

        public List<Annotatable> Blocks { get; } = [];

        public string? Title { get; private set; }

        public void Walk(ContainerBlock container) {
            foreach (var block in container) {
                Visit(block);
            }
        }

        private void Visit(Block block) {
            if (block is HtmlBlock or ThematicBreakBlock or LinkReferenceDefinitionGroup) {
                return;
            }

            SetDirection(block);

            switch (block) {
                case ListItemBlock { Count: > 0 } item when item[0] is ParagraphBlock lead:
                    // A tight list renders its leading paragraph without a <p>, so the item itself
                    // carries the range; the <p> of a loose list gets the same one. Nested lists
                    // after the paragraph get their own.
                    SetDirection(lead);
                    Add(BlockKind.ListItem, lead, lead, PlainText(lead), item);

                    for (var i = 1; i < item.Count; i++) {
                        Visit(item[i]);
                    }

                    break;
                case TableRow row:
                    // The row is what the user lands on; the paragraphs in its cells are not
                    // annotatable on their own.
                    foreach (var cell in row) {
                        SetDirection(cell);
                    }

                    Add(BlockKind.TableRow, row, row, RowText(row));
                    break;
                case ContainerBlock container:
                    Walk(container);
                    break;
                case HeadingBlock heading:
                    var headingText = PlainText(heading);
                    Title ??= headingText;
                    Add(BlockKind.Heading, heading, heading, headingText);
                    break;
                case CodeBlock code:
                    Add(BlockKind.Code, code, code, code.Lines.ToString().TrimEnd('\n'));
                    break;
                case ParagraphBlock paragraph:
                    Add(BlockKind.Paragraph, paragraph, paragraph, PlainText(paragraph));
                    break;
            }
        }

        private void Add(BlockKind kind, Block range, Block target, string text, params Block[] alsoStamp) {
            if (range.Span.IsEmpty) {
                return;
            }

            var startLine = _parse.ToOriginalLine(StrippedLineOf(range.Span.Start));
            var endLine = _parse.ToOriginalLine(StrippedLineOf(Math.Max(range.Span.Start, range.Span.End)));
            var info = new BlockInfo(kind, startLine, endLine, text, Excerpt(text));

            target.GetAttributes().AddProperty("data-lines", info.Lines);

            foreach (var other in alsoStamp) {
                other.GetAttributes().AddProperty("data-lines", info.Lines);
            }

            Blocks.Add(new Annotatable(info, target));
        }

        private static void SetDirection(Block block) => block.GetAttributes().AddPropertyIfNotExist("dir", "auto");

        private string PlainText(LeafBlock block) {
            _plainWriter.GetStringBuilder().Clear();
            _plainRenderer.WriteLeafInline(block);
            _plainWriter.Flush();

            return _plainWriter.ToString().Trim();
        }

        private string RowText(TableRow row) {
            var cells = new List<string>(row.Count);

            foreach (var cell in row) {
                var parts = new List<string>();

                if (cell is ContainerBlock container) {
                    foreach (var child in container) {
                        if (child is LeafBlock leaf) {
                            parts.Add(PlainText(leaf));
                        }
                    }
                }

                cells.Add(string.Join(' ', parts));
            }

            return string.Join(" | ", cells);
        }

        /// <summary>The 1-based stripped line holding the character at <paramref name="offset"/>.</summary>
        private int StrippedLineOf(int offset) {
            var index = Array.BinarySearch(_lineStarts, offset);

            return index >= 0 ? index + 1 : ~index;
        }

        private static int[] LineStarts(string source) {
            var starts = new List<int> { 0 };

            for (var i = 0; i < source.Length; i++) {
                var c = source[i];

                if (c == '\r' && i + 1 < source.Length && source[i + 1] == '\n') {
                    i++;
                } else if (c is not ('\r' or '\n')) {
                    continue;
                }

                if (i + 1 < source.Length) {
                    starts.Add(i + 1);
                }
            }

            return starts.ToArray();
        }
    }
}
