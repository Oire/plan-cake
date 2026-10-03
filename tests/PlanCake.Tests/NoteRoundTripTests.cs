using System.Text;
using AwesomeAssertions;
using Oire.PlanCake.Notes;
using Oire.PlanCake.Rendering;
using Xunit;

namespace Oire.PlanCake.Tests;

/// <summary>
/// For every block a user can annotate in a corpus of real documents (copies under
/// <c>Corpus\</c>, so editing the live docs does not change the test): a note added after it reads
/// back with the same text and anchors to that block, and deleting it leaves the file
/// byte-identical. Both marker modes, both line endings. The documents show the markers only as
/// code, so they start with no notes.
/// </summary>
public class NoteRoundTripTests: IDisposable {
    private static readonly RenderStrings _strings = new("User note");
    private static readonly NoteMarkers _singleToken = new("!USERNOTE!");
    private static readonly UTF8Encoding _utf8 = new(false);

    private readonly string _folder;

    public NoteRoundTripTests() {
        _folder = Path.Combine(Path.GetTempPath(), $"PlanCake.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_folder);
    }

    public void Dispose() {
        if (Directory.Exists(_folder)) {
            Directory.Delete(_folder, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    public static TheoryData<string, bool, bool, bool> Cases() {
        var cases = new TheoryData<string, bool, bool, bool>();
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Corpus"), "*.md")
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal);

        foreach (var file in files) {
            foreach (var singleToken in new[] { false, true }) {
                foreach (var crLf in new[] { false, true }) {
                    cases.Add(file!, singleToken, crLf, false);
                }
            }
        }

        // A last line without a line break of its own.
        cases.Add("edge-cases.md", false, false, true);
        cases.Add("edge-cases.md", true, true, true);

        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryBlock_NoteReadsBackAnchoredToItAndDeletesWithoutATrace(
        string file,
        bool singleToken,
        bool crLf,
        bool withoutFinalBreak
    ) {
        var markers = singleToken ? _singleToken : NoteMarkers.Default;
        var lineEnding = crLf ? "\r\n" : "\n";
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Corpus", file))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        if (withoutFinalBreak) {
            text = text.TrimEnd('\n');
        }

        text = text.Replace("\n", lineEnding, StringComparison.Ordinal);

        var original = Render(text, markers);
        original.Notes.Should().BeEmpty();
        original.Blocks.Should().NotBeEmpty();

        var noteText = singleToken ? "Round-trip note" : "Round-trip note\nwith a second line";
        var parse = NoteParser.Parse(text, markers);

        foreach (var block in original.Blocks) {
            // A format string to the assertions: braces in the excerpt are doubled.
            var because = $"a note after {block.Kind} {block.Lines} {block.Excerpt}"
                .Replace("{", "{{", StringComparison.Ordinal)
                .Replace("}", "}}", StringComparison.Ordinal);

            var (withNote, line) = NoteStore.InsertNote(text, parse, block, noteText, markers, lineEnding);

            var render = Render(withNote, markers);
            var note = render.Notes.Should().ContainSingle(because).Subject;
            note.Note.Text.Should().Be(noteText, because);
            note.Note.StartLine.Should().Be(line, because);
            note.Block.Should().Be(block, because);
            _utf8.GetBytes(NoteStore.RemoveNote(withNote, note.Note)).Should().Equal(_utf8.GetBytes(text), because);
        }

        // Through the file itself, for the first and the last block.
        var path = Path.Combine(_folder, file);
        var bytes = _utf8.GetBytes(text);
        File.WriteAllBytes(path, bytes);

        foreach (var block in new[] { original.Blocks[0], original.Blocks[^1] }) {
            var store = new NoteStore(MarkdownFile.Open(path), markers);
            store.Add(text, block, noteText);
            var note = Render(store.File.Text, markers).Notes.Should().ContainSingle().Subject;

            store.Delete(store.File.Text, note.Note);

            File.ReadAllBytes(path).Should().Equal(bytes);
        }
    }

    private static RenderResult Render(string text, NoteMarkers markers) =>
        MarkdownRenderer.Render(text, new RenderOptions(markers, RenderMode.Interactive, _strings));
}
