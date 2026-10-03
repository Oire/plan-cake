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
    private readonly Encoding? _ansiEncoding;
    private readonly RootCommand _root;
    private readonly Argument<string?> _fileArgument;

    public CliRunner(TextWriter output, TextWriter error) : this(output, error, null) { }

    /// <param name="ansiEncoding">
    /// The Windows ANSI code page files are decoded with as a last resort (see
    /// <see cref="MarkdownFileOptions.AnsiEncoding"/>); <see langword="null"/> for the machine's.
    /// Tests inject it, so they do not depend on the machine.
    /// </param>
    internal CliRunner(TextWriter output, TextWriter error, Encoding? ansiEncoding) {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        _output = output;
        _error = error;
        _ansiEncoding = ansiEncoding;

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
            } else if (result.Tokens is [{ Value: var word }] && MistypedCommand(word) is { } command) {
                // A mistyped command would otherwise open the window on a file that does not exist,
                // and a script waiting for it would wait until someone closes the window.
                result.AddError(_("Unrecognized command: {0}. Did you mean {1}?", word, command));
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

    /// <summary>The subcommands, which <see cref="MistypedCommand"/> compares a lone word with.</summary>
    internal static readonly string[] CommandNames = ["list", "check", "clear", "export"];

    /// <summary>
    /// The subcommand <paramref name="word"/> is likely a typo of, or <see langword="null"/>: a word
    /// with no folder and no extension, naming no file or folder, within two edits of a
    /// subcommand. Any other word is a file to open, so an existing file without an extension
    /// still opens.
    /// </summary>
    internal static string? MistypedCommand(string word) {
        ArgumentNullException.ThrowIfNull(word);

        if (word.Length == 0 || word.AsSpan().IndexOfAny('\\', '/', ':') >= 0 || Path.HasExtension(word)
            || File.Exists(word) || Directory.Exists(word)) {
            return null;
        }

        var lowered = word.ToLowerInvariant();

        return CommandNames
            .Select(command => (Command: command, Distance: EditDistance(lowered, command)))
            .Where(candidate => candidate.Distance <= 2)
            .OrderBy(candidate => candidate.Distance)
            .Select(candidate => candidate.Command)
            .FirstOrDefault();
    }

    /// <summary>The Levenshtein distance between two words.</summary>
    private static int EditDistance(string first, string second) {
        var previous = new int[second.Length + 1];
        var current = new int[second.Length + 1];

        for (var j = 0; j <= second.Length; j++) {
            previous[j] = j;
        }

        for (var i = 1; i <= first.Length; i++) {
            current[0] = i;

            for (var j = 1; j <= second.Length; j++) {
                var substitution = previous[j - 1] + (first[i - 1] == second[j - 1] ? 0 : 1);
                current[j] = Math.Min(substitution, Math.Min(previous[j], current[j - 1]) + 1);
            }

            (previous, current) = (current, previous);
        }

        return previous[second.Length];
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

            if (outputPath is not null && FileIdentity.IsSameFile(outputPath, markdown.Path)) {
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
        var convert = new Option<bool>("--convert") {
            Description = _("Convert a file that is not UTF-8 to UTF-8 (without BOM), whatever the settings say"),
        };
        var markers = new MarkerOptions();
        var command = new Command("clear", _("Remove every note from a file")) { file, convert };
        markers.AddTo(command);

        command.SetAction(result => {
            var convertToUtf8 = result.GetValue(convert) || Config.Advanced.ConvertToUtf8;

            if (!TryResolveMarkers(result, markers, out var noteMarkers)
                || !TryOpen(result.GetValue(file)!, convertToUtf8, null, out var markdown)) {
                return ExitCode.Error;
            }

            if (markdown.ConvertedFrom is { } convertedFrom) {
                WriteLine(
                    _output, _("The file was converted from {0} to UTF-8.", LegacyEncoding.DisplayName(convertedFrom))
                );
            }

            var parse = NoteParser.Parse(markdown.Text, noteMarkers);
            WarnAboutUnterminatedNotes(parse);

            if (parse.Notes.Count > 0) {
                if (markdown.IsUnrecognized) {
                    return Fail(_(
                        "The encoding of {0} could not be recognized, so PlanCake never changes it.", markdown.Path
                    ));
                }

                // A note without a closing marker runs to the end of the file: removing it would
                // take the rest of the document with it.
                if (parse.HasUnterminated) {
                    return Fail(_(
                        "Nothing was removed: a note without a closing marker would take the rest of the file with it. Add the closing marker first."
                    ));
                }

                if (markdown.ConversionFailed) {
                    return Fail(_(
                        "{0} is not in UTF-8 but in {1}, and it could not be converted, so PlanCake does not change it.",
                        markdown.Path, LegacyEncoding.DisplayName(markdown.Encoding)
                    ));
                }

                if (markdown.IsReadOnly) {
                    return Fail(_(
                        "{0} is not in UTF-8 but in {1}, so PlanCake does not change it. To convert it, run the command again with --convert, or turn on converting files that are not UTF-8 in PlanCake's settings.",
                        markdown.Path, LegacyEncoding.DisplayName(markdown.Encoding)
                    ));
                }

                try {
                    new NoteStore(markdown, noteMarkers).Clear(markdown.Text);
                } catch (Exception ex) when (ex is IOException or NoteWriteException) {
                    Log.Error(ex, "CLI: unable to clear the notes of {Path}", markdown.Path);

                    return Fail(_("Unable to write {0}: {1}", markdown.Path, ex.Message));
                }
            }

            Log.Information("CLI: clear {Path}: {Count} notes removed", markdown.Path, parse.Notes.Count);
            WriteLine(_output, _n("Removed {0} note", "Removed {0} notes", parse.Notes.Count, parse.Notes.Count));

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

            if (!LanguageList.IsCulture(documentLanguage)) {
                return Fail(_("{0} is not a language code.", documentLanguage));
            }

            if (!TryResolveMarkers(result, markers, out var noteMarkers)
                || !TryOpen(result.GetValue(file)!, convertToUtf8: false, documentLanguage, out var markdown)) {
                return ExitCode.Error;
            }

            var outputPath = result.GetValue(output)!;

            if (FileIdentity.IsSameFile(outputPath, markdown.Path)) {
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
        markers = new NoteMarkers(opening, singleToken ? String.Empty : closing ?? Config.Notes.ClosingMarker);

        if (singleToken && closing is not null) {
            Fail(_("--single-token and --close-marker cannot be used together."));

            return false;
        }

        if (LocalizedText.MarkersError(markers.Validate()) is { } reason) {
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

        if (Directory.Exists(path)) {
            Fail(_("{0} is a folder, not a file.", path));

            return false;
        }

        try {
            file = MarkdownFile.Open(path, new MarkdownFileOptions(
                ConvertToUtf8: convertToUtf8,
                AnsiEncoding: _ansiEncoding,
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

        if (file.IsUnrecognized && file.InvalidByteLine is { } invalidLine) {
            WriteLine(_error, _(
                "Warning: {0} is in {1} but has an invalid byte on line {2}, which shows as a replacement character.",
                file.Path, LegacyEncoding.DisplayName(file.Encoding), invalidLine
            ));
        } else if (file.IsUnrecognized) {
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
            LocalizedText.RenderStrings(),
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
    /// "&lt;excerpt&gt;": &lt;text&gt;</c>, or <c>&lt;noteStart&gt;-&lt;noteEnd&gt; at the start:
    /// &lt;text&gt;</c> for a note before the first block; line numbers are 1-based, and line
    /// breaks in the text are shown as <c> / </c>. Empty without notes.
    /// </summary>
    internal static string ListText(IReadOnlyList<RenderedNote> notes) {
        var text = new StringBuilder();

        foreach (var rendered in notes) {
            var note = rendered.Note;
            text.Append(CultureInfo.InvariantCulture, $"{note.StartLine}-{note.EndLine} ");

            if (rendered.Block is { } block) {
                text.Append(CultureInfo.InvariantCulture, $"after {block.StartLine}-{block.EndLine} \"{block.Excerpt}\": ");
            } else {
                text.Append("at the start: ");
            }

            text.Append(note.Text.Replace("\n", " / ", StringComparison.Ordinal)).Append('\n');
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
}
