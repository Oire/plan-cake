# PlanCake

[![.NET build and tests](https://github.com/Oire/plan-cake/actions/workflows/dotnet.yml/badge.svg)](https://github.com/Oire/plan-cake/actions/workflows/dotnet.yml)
[![License: Apache 2.0](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE)

Read Markdown files comfortably and leave notes right where they belong.

PlanCake is a Windows application that shows a Markdown file the way it is meant to be read:
real headings, lists, tables and code blocks instead of hash signs and asterisks. Click a
paragraph, a list item, a heading, a table row or a code block, type a note, and PlanCake writes
it straight into the `.md` file, right after that block, between a pair of markers
(`[usernote]` … `[/usernote]` by default). Whoever reads the file next, a colleague or an AI
assistant such as Claude, finds your notes exactly where they belong.

## Why

AI coding assistants write long implementation plans, often 800 lines of Markdown and more, and
ask you to review them. Reading them raw in a code editor is tiring, and commenting on them is
worse: you either describe in a chat where each remark belongs ("in Task 4, the third item…") or
edit the file by hand and hope the assistant notices.

PlanCake makes both halves comfortable. It renders the plan, so you read it like a document. And
it knows which lines of the file each rendered block came from, so leaving a note on a block
takes one click, or Enter, and no line numbers. The notes are plain text in the file itself:
there is nothing to save, no side file, and any tool that reads the file sees them. When you are
done, the assistant reads your notes with the exact blocks they refer to, reworks the plan and
removes them.

Like every Oire application, PlanCake is built to work for everyone: everything works equally
well with the mouse, from the keyboard alone, and with a screen reader, which reads the rendered
plan with its usual heading, list and table navigation instead of spelling out Markdown
punctuation.

## Installing

Download PlanCake from [plancake.oire.dev](https://plancake.oire.dev), in one of two forms:

- **The installer** sets everything up: it installs the .NET 10 Desktop Runtime and the
  Microsoft Edge WebView2 Runtime if they are missing, and puts `plancake` on your `PATH`.
- **The portable zip** runs from any folder. Keep the empty `userdata` folder next to
  `plancake.exe`, and PlanCake keeps its settings there instead of in `%APPDATA%\Oire\PlanCake`.

PlanCake is not signed with a code-signing certificate, so the first time you run the installer
or `plancake.exe` from the zip, Windows SmartScreen may say "Windows protected your PC" and name
an unknown publisher. Choose **More info**, then **Run anyway**.

Once the first release is published, PlanCake can also be installed with
[winget](https://learn.microsoft.com/windows/package-manager/winget/):

```powershell
winget install Oire.PlanCake
```

Requirements:

- Windows 10 or 11, x64
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)
  (preinstalled on Windows 11)

The installer brings both runtimes along; the portable zip needs them already there.

PlanCake speaks English, Russian, Ukrainian, French, Hebrew (with a right-to-left interface) and
German. Press F1 in PlanCake for the full user manual, in your language; the installer also puts
it in the Start menu. The English one is [in this repository](src/PlanCake/help/en/manual.html).

## Reading and annotating

- **Open a file** from the command line (`plancake plan.md`), with Ctrl+O, by dropping it on the
  window, from the clipboard (Ctrl+V after copying it in File Explorer, or a copied path), or from
  a link (Ctrl+L; GitHub file links work as they are). A linked file is downloaded to your
  Downloads folder and opened from there, so your notes go into that copy. Each file gets its own
  window, and a file is never open in two windows at once.
- **Add a note**: click a block, or move to it with Alt+Shift+Down Arrow and Alt+Shift+Up Arrow
  and press Enter. Type the note and press Enter. The note is in the file at once, shown after
  its block and in the notes list beside the document. Markdown in a note is shown formatted.
- **Edit, delete, move between notes**: click a note or press Enter on it to edit it; Delete in
  the notes list removes one; F9 and Shift+F9 go to the next and the previous note. Ctrl+Z and
  Ctrl+Y undo and redo everything PlanCake writes.
- **Tick off tasks**: task-list checkboxes (`- [ ]`) are real checkboxes; checking one writes
  `[x]` on that line of the file, after a question you can turn off in Settings.
- **Keep reading while the file changes**: when something else changes the file, PlanCake
  reloads it and keeps your place. It never writes a note over a change you have not seen.

In the file, a note looks like this:

```markdown
Back up the database before you run the migration.
[usernote]Also back up the uploads folder.[/usernote]
```

The markers can be changed in Settings (Ctrl+Comma), including a single marker that runs to the
end of its line.

## The command line

Run with a subcommand, PlanCake works without a window, for scripts and AI assistants:

```powershell
plancake plan.md                          # open the window with plan.md
plancake list plan.md                     # print every note with the block it follows
plancake list plan.md --json -o notes.json   # the same as JSON, to a file
plancake check plan.md                    # count the notes: exit code 0 with none, 3 with some
plancake clear plan.md                    # remove every note from the file
plancake export plan.md -o plan.html      # a standalone web page with the notes marked
plancake check plan.md --open-marker "!NOTE!" --single-token   # other markers for one run
```

Errors go to the error output with exit code 1. Output is UTF-8 without a byte order mark.
`plancake --help` and `plancake <command> --help` list every option.

PlanCake is a Windows application, so an interactive PowerShell or Command Prompt does not wait
for it: typed at the prompt, the output may appear after the prompt returns. Piped or captured
output is always complete (`plancake check plan.md | Out-Host`, or `cmd /c "plancake check
plan.md"` when you need `$LASTEXITCODE`).

### A shorter command: `pk`

To open plans with two letters, add an alias to your shell profile. In PowerShell (the file
`$PROFILE` names):

```powershell
Set-Alias pk plancake
```

In bash (`~/.bashrc`):

```bash
alias pk=plancake
```

Then `pk plan.md` opens a plan, and `pk list plan.md` works like `plancake list plan.md`.

## Reviewing a plan with Claude

When Claude writes a plan and asks you to review it, open it with `pk plan.md`, read it, and
leave notes wherever you want changes. Tell Claude you are done: it runs
`plancake list plan.md --json` to read your notes with the blocks they refer to, reworks the
plan, removes the notes it has dealt with, and confirms with `plancake check plan.md` that none
is left.

The [Debussy](https://debussy.oire.dev/) plugins for Claude Code have this manual-review step
built in. They find notes by the markers in their `noteMarkers` setting, where a pair is written
`open...close`, so point it at PlanCake's markers in `~/.claude/debussy.json` (or the project's
`.claude/debussy.json`):

```json
{
  "noteMarkers": ["[usernote]...[/usernote]"]
}
```

If you change the markers in PlanCake's settings, change them there too.

## Building from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0): `global.json`
pins it to 10.0.401 or a later patch of that feature band, the one CI builds with too.

```powershell
# once: gettext's msgfmt, which compiles the translations
winget install mlocati.GetText

# before every build: .mo files are build output and are gitignored
./src/PlanCake/locale/scripts/Compile-Translations.ps1 -Strict
dotnet restore
dotnet build
dotnet test
dotnet format            # --verify-no-changes is what CI runs
```

Skip the translations step and the app falls back to English, and the localization tests fail.
CI runs these steps too, with the format check before the build.

The installer and the portable zip are built with [Inno Setup 6](https://jrsoftware.org/isinfo.php):

```powershell
./installer/Build-Installer.ps1
```

It compiles the translations, publishes a Release build and writes both to `installer/Output/`.
It refuses to build from a working tree with uncommitted changes unless you pass `-AllowDirty`;
the release procedure is in `CLAUDE.md` (Releasing).

See `src/PlanCake/locale/README.md` for the translation workflow and `CLAUDE.md` for the
conventions this repository follows.

## License

PlanCake is licensed under the [Apache License 2.0](LICENSE). The licenses of the components it
includes are in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt), which ships with it.
