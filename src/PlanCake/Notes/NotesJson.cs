using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Oire.PlanCake.Rendering;

namespace Oire.PlanCake.Notes;

/// <summary>
/// The notes of a document as JSON, with the block each one follows: what <c>plancake list
/// --json</c> prints and File → Export notes… writes, so the two are always identical
/// (Technical details → "Command line" in the plan).
/// </summary>
/// <remarks>
/// The JSON is a copy for other tools; PlanCake never reads it back. It is indented, with
/// <c>\n</c> line breaks and a final one, and keeps non-Latin text as is rather than as
/// <c>\u</c> escapes, so a Cyrillic or Hebrew note reads the same in the file as in the plan.
/// </remarks>
internal static class NotesJson {
    /// <summary>The <c>blockKind</c> of a note before the first block, which follows no block.</summary>
    public const string StartKind = "start";

    private static readonly JsonWriterOptions _options = new() {
        Indented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// The notes as a JSON array of <c>{ noteStartLine, noteEndLine, blockStartLine,
    /// blockEndLine, blockKind, blockExcerpt, text }</c>; <c>[]</c> without any.
    /// </summary>
    public static string Serialize(IReadOnlyList<RenderedNote> notes) =>
        Encoding.UTF8.GetString(SerializeToUtf8(notes));

    /// <summary>The same JSON as <see cref="Serialize"/>, as UTF-8 bytes without a BOM.</summary>
    public static byte[] SerializeToUtf8(IReadOnlyList<RenderedNote> notes) {
        ArgumentNullException.ThrowIfNull(notes);

        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream, _options)) {
            writer.WriteStartArray();

            foreach (var rendered in notes) {
                var block = rendered.Block;
                writer.WriteStartObject();
                writer.WriteNumber("noteStartLine", rendered.Note.StartLine);
                writer.WriteNumber("noteEndLine", rendered.Note.EndLine);
                writer.WriteNumber("blockStartLine", block?.StartLine ?? 0);
                writer.WriteNumber("blockEndLine", block?.EndLine ?? 0);
                writer.WriteString("blockKind", KindName(block));
                writer.WriteString("blockExcerpt", block?.Excerpt ?? String.Empty);
                writer.WriteString("text", rendered.Note.Text);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        stream.WriteByte((byte)'\n');

        return stream.ToArray();
    }

    /// <summary>
    /// The name a block's kind has in the JSON: <c>paragraph</c>, <c>heading</c>, <c>listItem</c>,
    /// <c>code</c>, <c>tableRow</c>, or <see cref="StartKind"/> for no block.
    /// </summary>
    public static string KindName(BlockInfo? block) => block?.Kind switch {
        null => StartKind,
        BlockKind.Paragraph => "paragraph",
        BlockKind.Heading => "heading",
        BlockKind.ListItem => "listItem",
        BlockKind.Code => "code",
        BlockKind.TableRow => "tableRow",
        var kind => throw new ArgumentOutOfRangeException(nameof(block), kind, null),
    };
}
