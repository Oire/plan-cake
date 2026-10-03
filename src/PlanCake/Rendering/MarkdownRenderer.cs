using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Helpers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Oire.PlanCake.Notes;

namespace Oire.PlanCake.Rendering;

/// <summary>What the rendered HTML is for.</summary>
internal enum RenderMode {
    /// <summary>The window's document view: notes are user notes the page can activate.</summary>
    Interactive,

    /// <summary>A standalone HTML file: notes are static <c>role="region"</c> elements.</summary>
    Export,
}

/// <summary>How <see cref="MarkdownRenderer.Render"/> renders a source.</summary>
/// <param name="Markers">The markers notes are written with.</param>
/// <param name="Mode">Interactive view or standalone export.</param>
/// <param name="Strings">The localized strings written into the document.</param>
/// <param name="DocumentLanguage">
/// The <c>lang</c> of an exported document. The interactive page sets it on its container instead.
/// </param>
/// <param name="FallbackTitle">
/// The <c>title</c> of an exported document without a heading (the file name, say), so an
/// exported file is never left without one.
/// </param>
/// <param name="DocumentFolder">
/// The folder of the Markdown file: an export embeds the local pictures it names
/// (<see cref="ExportImages"/>), relative paths starting from there.
/// </param>
internal sealed record RenderOptions(
    NoteMarkers Markers,
    RenderMode Mode,
    RenderStrings Strings,
    string DocumentLanguage = "en",
    string? FallbackTitle = null,
    string? DocumentFolder = null
);

/// <summary>
/// Renders a Markdown source to HTML with its notes taken out and put back after the blocks
/// they annotate, every annotatable block stamped with its original source line range, per
/// Technical details → "Annotatable blocks" and "Note placement in the view" in the PlanCake plan.
/// </summary>
internal static partial class MarkdownRenderer {
    /// <summary>The longest a <see cref="BlockInfo.Excerpt"/> gets, ellipsis included.</summary>
    public const int ExcerptLength = 80;

    /// <summary>The longest <see cref="FirstSentence"/> gets, ellipsis included.</summary>
    public const int SentenceLength = 120;

    /// <summary>
    /// The content security policy of an exported file: no script at all, no form that sends
    /// anywhere and no <c>&lt;base&gt;</c> that moves the plan's relative links. Pictures come
    /// from the web or from inside the file (local ones are embedded, see
    /// <see cref="ExportImages"/>), never from <c>file:</c>, which for a <c>file://host/…</c>
    /// picture in raw HTML would make the browser reach another computer's share.
    /// </summary>
    public const string ExportContentSecurityPolicy =
        "default-src 'none'; script-src 'none'; style-src 'unsafe-inline'; img-src https: http: data:; "
        + "base-uri 'none'; form-action 'none'";

    private const string ExportStyle = """
        body { font-family: "Segoe UI", sans-serif; line-height: 1.5; max-width: 50em; margin: 1em auto; }
        pre { overflow-x: auto; padding: 0.5em; border: 1px solid; }
        table { border-collapse: collapse; }
        th, td { border: 1px solid; padding: 0.25em 0.5em; }
        .note { border-inline-start: 0.3em solid; margin: 0.5em 0; padding: 0.25em 0.75em; font-style: italic; }
        .note > :first-child { margin-top: 0; }
        .note > :last-child { margin-bottom: 0; }
        """;

    /// <summary>
    /// The attribute the page finds a task-list check box by. An attribute, not a class: a class
    /// in raw HTML can be spelled with character references, an attribute name cannot, so
    /// <see cref="NeutralizeProtocolMarkers"/> catches every spelling of it.
    /// </summary>
    public const string TaskCheckboxAttribute = "data-plancake-task";

    /// <summary>The prefix a protocol attribute written by the plan itself gets, so the page ignores it.</summary>
    internal const string NeutralizedPrefix = "x-";

    private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UsePreciseSourceLocation()
        .Build();

    /// <summary>The pipeline a document is parsed with, for <see cref="NoteParser"/> to find its code blocks.</summary>
    internal static MarkdownPipeline Pipeline => _pipeline;

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
        NeutralizeProtocolMarkers(document);
        var walker = new BlockWalker(parse);
        walker.Walk(document);

        var notes = InsertNotes(document, parse.Notes, walker.Blocks, options);
        var body = ToHtml(document, options.Mode, FindMixedTasks(document), walker.TaskLabels);
        var title = walker.Title;
        var html = options.Mode == RenderMode.Export
            ? ExportDocument(
                ExportImages.Embed(body, options.DocumentFolder),
                title ?? options.FallbackTitle,
                options.DocumentLanguage
            )
            : body;

        return new RenderResult(html, walker.Blocks.Select(block => block.Info).ToList(), notes, title, parse);
    }

    /// <summary>
    /// Matches, in raw HTML, a name the page and the host trust to come from the renderer
    /// (<c>data-lines</c>, <c>data-note</c>, <c>data-mixed</c> and every <c>data-plancake-*</c>)
    /// wherever the browser could read it as an attribute name: after whitespace, a slash or a
    /// quote, and before whitespace, a slash, <c>&gt;</c>, <c>=</c> or the end of the line. HTML
    /// lowercases attribute names, so the match ignores case. It may also match such a word in
    /// the text of a raw HTML block, which then shows with the prefix: a much smaller cost than
    /// tracking the browser's tokenizer through comments, raw text elements and foreign content.
    /// </summary>
    [GeneratedRegex(
        """(?<=^|[\s/"'])(?:data-lines|data-note|data-mixed|data-plancake-[^\s/>=]*)(?=[\s/>=]|$)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex ProtocolAttributeName();

    /// <summary>
    /// Keeps the plan itself from imitating the markers the page acts on. A block or note the user
    /// clicks is found by <c>data-lines</c> and <c>data-note</c>, a task check box by
    /// <see cref="TaskCheckboxAttribute"/>; raw HTML or a generic attribute (<c>{data-lines=40-40}</c>)
    /// carrying one of them would send the user's click to another block, or toggle a task they did
    /// not touch. Raw HTML gets <see cref="NeutralizedPrefix"/> before such a name; a generic
    /// attribute with such a name is dropped. Runs before the walker stamps the real ones.
    /// </summary>
    internal static void NeutralizeProtocolMarkers(MarkdownDocument document) {
        foreach (var node in document.Descendants()) {
            switch (node) {
                case HtmlBlock block:
                    block.Lines = NeutralizedLines(block.Lines);
                    break;
                case HtmlInline inline:
                    inline.Tag = NeutralizeRawHtml(inline.Tag);
                    break;
            }

            node.TryGetAttributes()?.Properties?.RemoveAll(property => IsProtocolAttribute(property.Key));
        }
    }

    /// <summary>Raw HTML with every name the page trusts renamed (see <see cref="ProtocolAttributeName"/>).</summary>
    internal static string NeutralizeRawHtml(string html) =>
        ProtocolAttributeName().Replace(html, match => NeutralizedPrefix + match.Value);

    private static bool IsProtocolAttribute(string name) =>
        ProtocolAttributeName().Match(name) is { Success: true, Index: 0 } match && match.Length == name.Length;

    private static StringLineGroup NeutralizedLines(StringLineGroup lines) {
        var neutralized = new StringLineGroup(lines.Count);

        foreach (var line in lines.Lines.AsSpan(0, lines.Count)) {
            var slice = new StringSlice(NeutralizeRawHtml(line.Slice.ToString()), line.Slice.NewLine);
            var copy = line with { Slice = slice };
            neutralized.Add(ref copy);
        }

        return neutralized;
    }

    private static string ToHtml(
        MarkdownDocument document,
        RenderMode mode,
        IReadOnlySet<TaskList> mixedTasks,
        IReadOnlyDictionary<TaskList, string> taskLabels
    ) {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var renderer = new HtmlRenderer(writer);
        _pipeline.Setup(renderer);

        // The window's task-list check boxes can be toggled; an exported file's stay disabled, as
        // Markdig renders them. Both show a partially checked parent.
        renderer.ObjectRenderers.Replace<HtmlTaskListRenderer>(new TaskListRenderer(mode, mixedTasks, taskLabels));

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
        var anchors = FindAnchors(blocks, notes);
        var atTop = new List<Block>();
        var after = new Dictionary<Block, List<Block>>(ReferenceEqualityComparer.Instance);

        for (var index = 0; index < notes.Count; index++) {
            var note = notes[index];
            var anchor = anchors[index];
            var noteBlock = CreateHtmlBlock(NoteHtml(note, index, options));

            if (anchor is null) {
                atTop.Add(noteBlock);
            } else if (anchor.Target is TableRow row) {
                // A <div> cannot sit between table rows, so the note goes into the row's last cell.
                var container = row.Count > 0 && row[^1] is ContainerBlock lastCell ? lastCell : row;
                container.Add(noteBlock);
            } else if (after.TryGetValue(anchor.Target, out var list)) {
                list.Add(noteBlock);
            } else {
                after[anchor.Target] = [noteBlock];
            }

            rendered.Add(new RenderedNote(index, note, anchor?.Info));
        }

        // Each container is rebuilt once with its notes in place, rather than one insert per note.
        var parents = new HashSet<ContainerBlock>(ReferenceEqualityComparer.Instance);

        foreach (var target in after.Keys) {
            parents.Add(target.Parent ?? throw new InvalidOperationException("An annotatable block has no parent."));
        }

        if (atTop.Count > 0) {
            parents.Add(document);
        }

        foreach (var parent in parents) {
            var children = parent.ToArray();
            parent.Clear();

            if (ReferenceEquals(parent, document)) {
                atTop.ForEach(parent.Add);
            }

            foreach (var child in children) {
                parent.Add(child);

                if (after.TryGetValue(child, out var list)) {
                    list.ForEach(parent.Add);
                }
            }
        }

        return rendered;
    }

    /// <summary>
    /// For every note, the block containing its first line (a note written by hand inside a
    /// block), else the last block ending before it, else <see langword="null"/>; ties go to the
    /// block first in document order. One pass over both: the notes come in source order, and the
    /// blocks are taken by their first line.
    /// </summary>
    private static Annotatable?[] FindAnchors(List<Annotatable> blocks, IReadOnlyList<Note> notes) {
        var anchors = new Annotatable?[notes.Count];

        // Footnotes are rendered at the end, so document order is not line order.
        var byStart = Enumerable.Range(0, blocks.Count).OrderBy(index => blocks[index].Info.StartLine).ToArray();
        var next = 0;

        // The blocks started by the note's line, by the line they end on, and by document order.
        var started = new PriorityQueue<int, (int EndLine, int Index)>();
        var open = new SortedSet<int>();
        var lastEnded = -1;

        for (var n = 0; n < notes.Count; n++) {
            var noteLine = notes[n].StartLine;

            while (next < byStart.Length && blocks[byStart[next]].Info.StartLine <= noteLine) {
                var index = byStart[next++];
                started.Enqueue(index, (blocks[index].Info.EndLine, index));
                open.Add(index);
            }

            while (started.TryPeek(out var index, out var key) && key.EndLine < noteLine) {
                started.Dequeue();
                open.Remove(index);

                if (lastEnded < 0 || key.EndLine > blocks[lastEnded].Info.EndLine
                    || (key.EndLine == blocks[lastEnded].Info.EndLine && index < lastEnded)) {
                    lastEnded = index;
                }
            }

            anchors[n] = open.Count > 0 ? blocks[open.Min] : lastEnded >= 0 ? blocks[lastEnded] : null;
        }

        return anchors;
    }

    private static HtmlBlock CreateHtmlBlock(string html) {
        var block = new HtmlBlock(null) { Type = HtmlBlockType.NonInterruptingBlock };
        block.Lines = new StringLineGroup(1);
        block.Lines.Add(new StringSlice(html));

        return block;
    }

    private static string NoteHtml(Note note, int index, RenderOptions options) {
        var region = $"""role="region" aria-label="{HtmlEncode(options.Strings.NoteLabel)}" """;

        if (options.Mode == RenderMode.Export) {
            return $"""<div class="note" {region}dir="auto">{NoteBlockHtml(note.Text)}</div>""";
        }

        var noteIndex = index.ToString(CultureInfo.InvariantCulture);

        return $"""<div class="note" {region}data-note="{noteIndex}" dir="auto">"""
            + $"{NoteBlockHtml(note.Text)}</div>";
    }

    /// <summary>
    /// A note's text rendered as Markdown for a <c>role="region"</c> element: every block gets
    /// <c>dir="auto"</c>, none gets <c>data-lines</c>, and a heading becomes a bold paragraph so
    /// that it never joins the document's heading navigation.
    /// </summary>
    internal static string NoteBlockHtml(string text) {
        var document = Markdown.Parse(text, _notePipeline);
        NeutralizeProtocolMarkers(document);

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
    /// The unchecked task-list items that are partially done: some, but not all, of the task-list
    /// items nested under them (at any depth) are checked. Markdown has no third state, so this
    /// is shown only, never written; a checked item shows checked whatever its children say.
    /// </summary>
    internal static HashSet<TaskList> FindMixedTasks(MarkdownDocument document) {
        var mixed = new HashSet<TaskList>(ReferenceEqualityComparer.Instance);

        foreach (var item in document.Descendants<ListItemBlock>()) {
            if (TaskOf(item) is not { Checked: false } task) {
                continue;
            }

            var anyChecked = false;
            var anyUnchecked = false;

            foreach (var descendant in item.Descendants<ListItemBlock>()) {
                if (TaskOf(descendant) is { } child) {
                    anyChecked |= child.Checked;
                    anyUnchecked |= !child.Checked;
                }
            }

            if (anyChecked && anyUnchecked) {
                mixed.Add(task);
            }
        }

        return mixed;
    }

    /// <summary>The task marker a list item starts with, or <see langword="null"/>.</summary>
    private static TaskList? TaskOf(ListItemBlock item) =>
        item.Count > 0 && item[0] is ParagraphBlock { Inline.FirstChild: TaskList task } ? task : null;

    /// <summary>
    /// A task-list check box. In the window it is enabled, with an attribute the page finds it by, and
    /// the page sends a toggle to the host, which rewrites the marker in the file; a partially
    /// done item carries <c>data-mixed</c>, which the page turns into the check box's
    /// <c>indeterminate</c> state. In an exported file it stays disabled as Markdig renders it,
    /// and a partially done item gets <c>aria-checked="mixed"</c>, since no script runs there.
    /// Both carry the item's text as their <c>aria-label</c>: an input takes no name from the text
    /// after it, and a screen reader that focuses the check box (after a toggle, in forms mode)
    /// would say only "check box". Not a <c>&lt;label&gt;</c> around the text, which would make a
    /// click on the text toggle the task instead of adding a note.
    /// </summary>
    private sealed class TaskListRenderer(
        RenderMode mode,
        IReadOnlySet<TaskList> mixedTasks,
        IReadOnlyDictionary<TaskList, string> labels
    ): HtmlObjectRenderer<TaskList> {
        protected override void Write(HtmlRenderer renderer, TaskList obj) {
            if (!renderer.EnableHtmlForInline) {
                renderer.Write(obj.Checked ? "[x]" : "[ ]");
                return;
            }

            var mixed = mixedTasks.Contains(obj);

            if (mode == RenderMode.Interactive) {
                renderer.Write($"<input {TaskCheckboxAttribute}=\"true\" type=\"checkbox\"");

                if (mixed) {
                    renderer.Write(" data-mixed=\"true\"");
                }
            } else {
                renderer.Write("<input disabled=\"disabled\" type=\"checkbox\"");

                if (mixed) {
                    renderer.Write(" aria-checked=\"mixed\"");
                }
            }

            if (labels.TryGetValue(obj, out var label) && label.Length > 0) {
                renderer.Write($" aria-label=\"{HtmlEncode(label)}\"");
            }

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
        <title>{HtmlEncode(title ?? String.Empty)}</title>
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
    /// text longer than <see cref="ExcerptLength"/> is cut to fit at the end of a word, with an
    /// ellipsis.
    /// </summary>
    internal static string Excerpt(string text) => Shorten(OneLine(text), ExcerptLength);

    /// <summary>
    /// The first sentence of <paramref name="text"/> on one line, for a cell that shows the start
    /// of a note: up to the first full stop, question mark, exclamation mark or ellipsis followed by
    /// a space and not by a lowercase letter (so <c>e.g. this</c> goes on), with any closing quote
    /// or bracket after it. A text without such an end is a sentence of its own. A sentence longer
    /// than <see cref="SentenceLength"/> is cut at the end of a word, with an ellipsis.
    /// </summary>
    internal static string FirstSentence(string text) {
        var line = OneLine(text);
        var end = SentenceEnd(line);

        return Shorten(end < 0 ? line : line[..end], SentenceLength);
    }

    /// <summary>The text on one line: every run of whitespace becomes one space, none at either end.</summary>
    internal static string OneLine(string text) =>
        String.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// <paramref name="line"/> cut to at most <paramref name="maxLength"/> characters, ellipsis
    /// included: at the last space that leaves at least half of it, so no word is cut in two; a
    /// single word longer than that (a link, say) is cut where it must, never inside a surrogate pair.
    /// </summary>
    private static string Shorten(string line, int maxLength) {
        if (line.Length <= maxLength) {
            return line;
        }

        var limit = maxLength - 1;

        if (Char.IsHighSurrogate(line[limit - 1])) {
            limit--;
        }

        // A space at the limit itself means the word before it ends there.
        var space = line.LastIndexOf(' ', limit);
        var cut = space >= limit / 2 ? space : limit;

        return String.Concat(line.AsSpan(0, cut).TrimEnd(), "…");
    }

    /// <summary>
    /// Where the first sentence of <paramref name="line"/> ends (see <see cref="FirstSentence"/>), or -1.
    /// </summary>
    private static int SentenceEnd(string line) {
        for (var i = 0; i < line.Length; i++) {
            if (!IsSentenceEnd(line[i])) {
                continue;
            }

            var end = i + 1;

            while (end < line.Length && (IsSentenceEnd(line[end]) || IsClosing(line[end]))) {
                end++;
            }

            // The full stops of Chinese and Japanese take no space after them.
            if (line[i] is '。' or '！' or '？') {
                return end;
            }

            if (end + 1 < line.Length && line[end] == ' ' && !Char.IsLower(line[end + 1])) {
                return end;
            }

            i = end - 1;
        }

        return -1;
    }

    private static bool IsSentenceEnd(char c) => c is '.' or '!' or '?' or '…' or '。' or '！' or '？';

    private static bool IsClosing(char c) => c is '"' or '\'' or ')' or ']' or '»' or '”' or '’' or '“';

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

            // Markdig writes a footnote link as HTML whatever the renderer's settings: the back
            // link at the end of a footnote and a reference in the text would show as markup in
            // an excerpt, the copied text and the command line's output.
            _plainRenderer.ObjectRenderers.Replace<HtmlFootnoteLinkRenderer>(new PlainFootnoteLinkRenderer());
        }

        public List<Annotatable> Blocks { get; } = [];

        /// <summary>The text of each task-list item, without its marker, for its check box's name.</summary>
        public Dictionary<TaskList, string> TaskLabels { get; } = new(ReferenceEqualityComparer.Instance);

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
                    var itemText = PlainText(lead);
                    Add(BlockKind.ListItem, lead, itemText, item);

                    // The whole item, not the excerpt: a name cut with an ellipsis tells a screen
                    // reader user less than the text it stands for.
                    if (TaskOf(item) is { } task) {
                        TaskLabels[task] = TaskToggle.WithoutMarker(OneLine(itemText));
                    }

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

                    Add(BlockKind.TableRow, row, RowText(row));
                    break;
                case ContainerBlock container:
                    Walk(container);
                    break;
                case HeadingBlock heading:
                    var headingText = PlainText(heading);
                    Title ??= headingText;
                    Add(BlockKind.Heading, heading, headingText);
                    break;
                case CodeBlock code:
                    Add(BlockKind.Code, code, code.Lines.ToString().TrimEnd('\n'));
                    break;
                case ParagraphBlock paragraph:
                    Add(BlockKind.Paragraph, paragraph, PlainText(paragraph));
                    break;
            }
        }

        private void Add(BlockKind kind, Block block, string text, params Block[] alsoStamp) {
            if (block.Span.IsEmpty) {
                return;
            }

            var startLine = _parse.ToOriginalLine(StrippedLineOf(block.Span.Start));
            var endLine = _parse.ToOriginalLine(StrippedLineOf(Math.Max(block.Span.Start, block.Span.End)));
            var info = new BlockInfo(kind, startLine, endLine, text, Excerpt(text));

            block.GetAttributes().AddProperty("data-lines", info.Lines);

            foreach (var other in alsoStamp) {
                other.GetAttributes().AddProperty("data-lines", info.Lines);
            }

            Blocks.Add(new Annotatable(info, block));
        }

        private static void SetDirection(Block block) => block.GetAttributes().AddPropertyIfNotExist("dir", "auto");

        /// <summary>
        /// A footnote link in plain text: the reference as its number in brackets, the back link
        /// at the end of a footnote as nothing.
        /// </summary>
        private sealed class PlainFootnoteLinkRenderer: HtmlObjectRenderer<FootnoteLink> {
            protected override void Write(HtmlRenderer renderer, FootnoteLink obj) {
                if (!obj.IsBackLink) {
                    renderer.Write(String.Create(CultureInfo.InvariantCulture, $"[{obj.Footnote.Order}]"));
                }
            }
        }

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

                cells.Add(String.Join(' ', parts));
            }

            return String.Join(" | ", cells);
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
