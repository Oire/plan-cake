using System.CommandLine;
using System.Globalization;
using System.Text;
using Oire.PlanCake.Notes;
using Oire.PlanCake.Rendering;
using Oire.PlanCake.Utils;
using Serilog;
using static Oire.PlanCake.Utils.Localization;
using ExitCode = Oire.PlanCake.Utils.Constants.ExitCode;

namespace Oire.PlanCake.Cli;

/// <summary>
/// The command line: <c>plancake [file]</c> opens the window, and the subcommands <c>list</c>,
/// <c>check</c>, <c>clear</c> and <c>export</c> work headless, per Technical details → "Command
/// line" in the plan. Output and errors go to the writers it is given, so tests need no console.
/// </summary>
/// <remarks>
/// The note markers, the document language and Convert to UTF-8 come from <see cref="Config"/>
/// unless an option overrides them; messages are in the interface language. <c>list</c>'s
/// text lines and JSON are data, not messages: they are never translated. Only <c>clear</c>
/// ever writes the file, and a file PlanCake opens read-only for its encoding is never written.
/// </remarks>
internal sealed class CliRunner {
    private static readonly UTF8Encoding _utf8 = new(false);

    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly RootCommand _root;
    private readonly Argument<string?> _fileArgument;

    public CliRunner(TextWriter output, TextWriter error) {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        _output = output;
        _error = error;

        // Hidden from the help, where the parser would list it under every subcommand too; the
        // description says how to open the window instead.
        _fileArgument = new Argument<string?>("file") {
            Arity = ArgumentArity.ZeroOrOne,
            Hidden = true,
        };

        // The parser takes an unknown option for the file, which would open the window on it.
        // A file whose name starts with a dash opens as .\-name.md.
        _fileArgument.Validators.Add(result => {
            if (result.Tokens is [{ Value: ['-', ..] option }]) {
                result.AddError(_("Unrecognized option: {0}", option));
            }
        });

        _root = new RootCommand(_(
            "PlanCake: read Markdown files comfortably and leave notes right where they belong. Without a command, \"plancake [file]\" opens the window, with the file if one is given."
        )) {
            _fileArgument,
            ListCommand(),
            CheckCommand(),
            ClearCommand(),
            ExportCommand(),
        };

        // Opening the window is the caller's job (see OpensWindow); the action only keeps the
        // parser from asking for a subcommand.
        _root.SetAction(_ => ExitCode.Success);
    }

    /// <summary>
    /// True when <paramref name="args"/> ask for the window: no subcommand, no <c>--help</c> or
    /// <c>--version</c>, and nothing the parser rejects (a parse error is reported on the command
    /// line, never by opening the window).
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="file">The file to open, if one was given.</param>
    public static bool OpensWindow(IReadOnlyList<string> args, out string? file) {
        var runner = new CliRunner(TextWriter.Null, TextWriter.Null);
        var result = runner.Parse(args);
        var opensWindow = result.Errors.Count == 0
            && result.CommandResult.Command == runner._root
            && ReferenceEquals(result.Action, runner._root.Action);
        file = opensWindow ? result.GetValue(runner._fileArgument) : null;

        return opensWindow;
    }

    /// <summary>Runs a subcommand (or prints the help, the version or a parse error).</summary>
    /// <returns>The process exit code.</returns>
    public int Run(IReadOnlyList<string> args) {
        var result = Parse(args);

        try {
            return result.Invoke(new InvocationConfiguration {
                Output = _output,
                Error = _error,
                EnableDefaultExceptionHandler = false,
            });
        } catch (Exception ex) {
            Log.Error(ex, "CLI: {Command} failed", result.CommandResult.Command.Name);
            WriteLine(_error, _("Error: {0}", ex.Message));

            return ExitCode.Error;
        }
    }

    private ParseResult Parse(IReadOnlyList<string> args) =>
        // No response files: a file name starting with @ is a file name.
        _root.Parse(args, new ParserConfiguration { ResponseFileTokenReplacer = null });

    // The subcommands.

    private Command ListCommand() {
        var file = FileArgument(_("The Markdown file to list the notes of"));
        var json = new Option<bool>("--json") { Description = _("Print the notes as JSON") };
        var output = new Option<string?>("--output", "-o") {
            Description = _("Write to this file (UTF-8 without BOM) instead of the standard output"),
        };
        var markers = new MarkerOptions();
        var command = new Command("list", _("List the notes of a file with the blocks they follow")) {
            file, json, output,
        };
        markers.AddTo(command);

        command.SetAction(result => {
            if (!TryResolveMarkers(result, markers, out var noteMarkers)
                || !TryOpen(result.GetValue(file)!, convertToUtf8: false, null, out var markdown)) {
                return ExitCode.Error;
            }

            var outputPath = result.GetValue(output);

            if (outputPath is not null && IsSameFile(outputPath, markdown.Path)) {
                return Fail(_("The output file cannot be the file itself: {0}", outputPath));
            }

            var render = Render(markdown, noteMarkers, RenderMode.Interactive, null);
            WarnAboutUnterminatedNotes(render.Parse);
            Log.Information("CLI: list {Path}: {Count} notes", markdown.Path, render.Notes.Count);

            var text = result.GetValue(json) ? NotesJson.Serialize(render.Notes) : ListText(render.Notes);

            return outputPath is null ? Print(text) : WriteFile(outputPath, text);
        });

        return command;
    }

    private Command CheckCommand() {
        var file = FileArgument(_("The Markdown file to check for notes"));
        var markers = new MarkerOptions();
        var command = new Command("check", _("Print how many notes a file has; exit code 3 when it has any")) {
            file,
        };
        markers.AddTo(command);

        command.SetAction(result => {
            if (!TryResolveMarkers(result, markers, out var noteMarkers)
                || !TryOpen(result.GetValue(file)!, convertToUtf8: false, null, out var markdown)) {
                return ExitCode.Error;
            }

            var parse = NoteParser.Parse(markdown.Text, noteMarkers);
            WarnAboutUnterminatedNotes(parse);
            var count = parse.Notes.Count;
            Log.Information("CLI: check {Path}: {Count} notes", markdown.Path, count);
            WriteLine(_output, _n("{0} note", "{0} notes", count, count));

            return count == 0 ? ExitCode.Success : ExitCode.NotesRemain;
        });

        return command;
    }

    private Command ClearCommand() {
        var file = FileArgument(_("The Markdown file to remove every note from"));
        var markers = new MarkerOptions();
        var command = new Command("clear", _("Remove every note from a file")) { file };
        markers.AddTo(command);

        command.SetAction(result => {
            if (!TryResolveMarkers(result, markers, out var noteMarkers)
                || !TryOpen(result.GetValue(file)!, Config.Advanced.ConvertToUtf8, null, out var markdown)) {
                return ExitCode.Error;
            }

            if (markdown.ConvertedFrom is { } convertedFrom) {
                WriteLine(_output, _("Converted from {0} to UTF-8.", EncodingName(convertedFrom)));
            }

            var parse = NoteParser.Parse(markdown.Text, noteMarkers);
            WarnAboutUnterminatedNotes(parse);

            if (parse.Notes.Count > 0) {
                if (markdown.IsUnrecognized) {
                    return Fail(_(
                        "The encoding of {0} could not be recognized, so PlanCake never changes it.", markdown.Path
                    ));
                }

                if (markdown.IsReadOnly) {
                    return Fail(_(
                        "{0} is not in UTF-8 but in {1}, so PlanCake does not change it. To convert it, turn on converting files that are not UTF-8 in PlanCake's settings.",
                        markdown.Path, EncodingName(markdown.Encoding)
                    ));
                }

                try {
                    new NoteStore(markdown, noteMarkers).Clear(markdown.Text);
                } catch (Exception ex) when (ex is IOException or StaleFileException or ReadOnlyFileException) {
                    Log.Error(ex, "CLI: unable to clear the notes of {Path}", markdown.Path);

                    return Fail(_("Unable to write {0}: {1}", markdown.Path, ex.Message));
                }
            }

            Log.Information("CLI: clear {Path}: {Count} notes removed", markdown.Path, parse.Notes.Count);
            WriteLine(_output, _n("Removed {0} note.", "Removed {0} notes.", parse.Notes.Count, parse.Notes.Count));

            return ExitCode.Success;
        });

        return command;
    }

    private Command ExportCommand() {
        var file = FileArgument(_("The Markdown file to export"));
        var output = new Option<string>("--output", "-o") {
            Description = _("The HTML file to write"),
            Required = true,
        };
        var language = new Option<string?>("--lang") {
            Description = _("The language of the document, such as en or fr (default: the document language in the settings)"),
        };
        var markers = new MarkerOptions();
        var command = new Command("export", _("Export a file as standalone HTML with its notes")) {
            file, output, language,
        };
        markers.AddTo(command);

        command.SetAction(result => {
            var documentLanguage = result.GetValue(language)?.Trim() ?? Config.General.DefaultDocumentLanguage;

            if (!IsLanguageCode(documentLanguage)) {
                return Fail(_("{0} is not a language code.", documentLanguage));
            }

            if (!TryResolveMarkers(result, markers, out var noteMarkers)
                || !TryOpen(result.GetValue(file)!, convertToUtf8: false, documentLanguage, out var markdown)) {
                return ExitCode.Error;
            }

            var outputPath = result.GetValue(output)!;

            if (IsSameFile(outputPath, markdown.Path)) {
                return Fail(_("The output file cannot be the file itself: {0}", outputPath));
            }

            var render = Render(markdown, noteMarkers, RenderMode.Export, documentLanguage);
            WarnAboutUnterminatedNotes(render.Parse);
            Log.Information("CLI: export {Path} to {Output}, lang={Language}", markdown.Path, outputPath, documentLanguage);

            return WriteFile(outputPath, render.Html);
        });

        return command;
    }

    private static Argument<string> FileArgument(string description) => new("file") { Description = description };

    /// <summary>The note marker options every subcommand takes.</summary>
    private sealed class MarkerOptions {
        public Option<string?> Opening { get; } = new("--open-marker") {
            Description = _("The marker a note starts with (default: the one in the settings)"),
        };

        public Option<string?> Closing { get; } = new("--close-marker") {
            Description = _("The marker a note ends with (default: the one in the settings)"),
        };

        public Option<bool> SingleToken { get; } = new("--single-token") {
            Description = _("A note has no closing marker and ends with its line"),
        };

        public void AddTo(Command command) {
            command.Options.Add(Opening);
            command.Options.Add(Closing);
            command.Options.Add(SingleToken);
        }
    }

    // Shared steps.

    private bool TryResolveMarkers(ParseResult result, MarkerOptions options, out NoteMarkers markers) {
        var opening = result.GetValue(options.Opening) ?? Config.Notes.OpeningMarker;
        var closing = result.GetValue(options.Closing);
        var singleToken = result.GetValue(options.SingleToken);
        markers = new NoteMarkers(opening, singleToken ? string.Empty : closing ?? Config.Notes.ClosingMarker);

        if (singleToken && closing is not null) {
            Fail(_("--single-token and --close-marker cannot be used together."));

            return false;
        }

        var reason = markers.Validate() switch {
            NoteMarkersError.None => null,
            NoteMarkersError.EmptyOpening => _("The opening marker cannot be empty."),
            NoteMarkersError.SurroundingWhitespace => _("A marker cannot start or end with a space."),
            NoteMarkersError.LineBreak => _("A marker cannot contain a line break."),
            NoteMarkersError.ClosingSameAsOpening => _("The closing marker must differ from the opening marker."),
            var error => error.ToString(),
        };

        if (reason is not null) {
            Fail(reason);

            return false;
        }

        return true;
    }

    /// <summary>
    /// Reads the file (never converting it unless <paramref name="convertToUtf8"/>); on failure
    /// the reason is on the error output.
    /// </summary>
    private bool TryOpen(string path, bool convertToUtf8, string? documentLanguage, out MarkdownFile file) {
        file = null!;

        try {
            file = MarkdownFile.Open(path, new MarkdownFileOptions(
                ConvertToUtf8: convertToUtf8,
                DocumentLanguage: documentLanguage ?? Config.General.DefaultDocumentLanguage
            ));
        } catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) {
            Log.Warning(ex, "CLI: unable to open {Path}: not found", path);
            Fail(_("The file {0} does not exist.", path));

            return false;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                         or NotSupportedException) {
            Log.Error(ex, "CLI: unable to open {Path}", path);
            Fail(_("Unable to open {0}: {1}", path, ex.Message));

            return false;
        }

        if (file.IsUnrecognized) {
            WriteLine(_error, _(
                "Warning: the encoding of {0} could not be recognized; what could not be decoded shows as replacement characters.",
                file.Path
            ));
        }

        return true;
    }

    private static RenderResult Render(MarkdownFile file, NoteMarkers markers, RenderMode mode, string? language) =>
        MarkdownRenderer.Render(file.Text, new RenderOptions(
            markers,
            mode,
            new RenderStrings(_("user note"), _("unote")),
            language ?? Config.General.DefaultDocumentLanguage,
            Path.GetFileName(file.Path)
        ));

    private void WarnAboutUnterminatedNotes(NoteParseResult parse) {
        foreach (var note in parse.Notes.Where(note => note.Unterminated)) {
            WriteLine(_error, _(
                "Warning: the note on line {0} has no closing marker, so it runs to the end of the file.",
                note.StartLine
            ));
        }
    }

    /// <summary>
    /// One line per note: <c>&lt;noteStart&gt;-&lt;noteEnd&gt; after &lt;blockStart&gt;-&lt;blockEnd&gt;
    /// "&lt;excerpt&gt;": &lt;text&gt;</c>, line breaks in the text shown as <c> / </c>. Empty
    /// without notes.
    /// </summary>
    internal static string ListText(IReadOnlyList<RenderedNote> notes) {
        var text = new StringBuilder();

        foreach (var rendered in notes) {
            var note = rendered.Note;
            var block = rendered.Block;
            text.Append(CultureInfo.InvariantCulture, $"{note.StartLine}-{note.EndLine} after ")
                .Append(CultureInfo.InvariantCulture, $"{block?.StartLine ?? 0}-{block?.EndLine ?? 0} ")
                .Append(CultureInfo.InvariantCulture, $"\"{block?.Excerpt}\": ")
                .Append(note.Text.Replace("\n", " / ", StringComparison.Ordinal))
                .Append('\n');
        }

        return text.ToString();
    }

    private int Print(string text) {
        _output.Write(text);

        return ExitCode.Success;
    }

    private int WriteFile(string path, string text) {
        try {
            File.WriteAllText(path, text, _utf8);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                         or NotSupportedException) {
            Log.Error(ex, "CLI: unable to write {Path}", path);

            return Fail(_("Unable to write {0}: {1}", path, ex.Message));
        }

        return ExitCode.Success;
    }

    private int Fail(string message) {
        WriteLine(_error, message);

        return ExitCode.Error;
    }

    /// <summary>Writes a line ending in <c>\n</c> whatever the platform, like the rest of the output.</summary>
    private static void WriteLine(TextWriter writer, string text) {
        writer.Write(text);
        writer.Write('\n');
    }

    private static bool IsSameFile(string path, string fullPath) {
        try {
            return string.Equals(Path.GetFullPath(path), fullPath, StringComparison.OrdinalIgnoreCase);
        } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) {
            return false;
        }
    }

    private static bool IsLanguageCode(string code) {
        if (string.IsNullOrWhiteSpace(code)) {
            return false;
        }

        try {
            return !string.IsNullOrEmpty(CultureInfo.GetCultureInfo(code, predefinedOnly: true).Name);
        } catch (CultureNotFoundException) {
            return false;
        }
    }

    /// <summary>An encoding's name as people write it: <c>Windows-1251</c>, <c>UTF-8</c>.</summary>
    private static string EncodingName(Encoding encoding) =>
        encoding.WebName.StartsWith("windows-", StringComparison.OrdinalIgnoreCase)
            ? $"Windows-{encoding.CodePage}"
            : encoding.WebName.ToUpperInvariant();
}
