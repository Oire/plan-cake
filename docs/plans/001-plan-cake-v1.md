# PlanCake 1.0: read and annotate Markdown with a screen reader

## Overview

PlanCake is a Windows desktop application for reading long Markdown files, above all the
implementation plans that `/planning:plan-make` writes (800 lines and more), as properly
rendered HTML, and for leaving notes on them for Claude's manual-review step.

The problem it solves: reading raw Markdown in VS Code with JAWS means hearing "hash hash
hash" and "star star" all day, with no heading, list or table navigation. Rendering the file
fixes the reading; the hard part is annotating *a specific place* from the JAWS virtual
cursor, which a web page cannot see. PlanCake solves that by annotating **blocks**, not lines:
every rendered paragraph, list item, heading, table row and code block carries the source line
range it came from, so pressing Enter (or the Applications key) on a block tells the host
exactly where in the `.md` file the note belongs. The user never deals with line numbers.

Notes are written straight into the `.md` file the moment the user confirms them, wrapped in a
pair of markers (default `[usernote]` … `[/usernote]`), which is the convention Debussy's
manual-review step already understands (`noteMarkers` accepts `open...close` pairs). The file
on disk is the single source of truth: there is no Save command, no side file, no export step.

The application is dual-mode like SIC! (`C:\repos\Oire\sic`): run without a subcommand it opens
the window; with a subcommand (`list`, `check`, `clear`, `export`) it works headless, which is
what Claude uses during manual review.

This repository was created from Oire's `winforms-template` and still carries the template's
names; Task 1 adapts it.

## Done when

- [ ] `plancake plan.md` opens a window showing `plan.md` rendered as HTML in WebView2; JAWS
      reads it in its virtual buffer with heading, list, table and button navigation, and no
      Markdown punctuation is read out
- [ ] pressing Enter (and/or the Applications key, whichever survived Task 2) on a paragraph,
      list item, heading, table row or code block opens the note dialog; confirming writes
      `[usernote]text[/usernote]` after that block's last source line, and the view returns to
      the new note
- [ ] notes show in the document as buttons after their block and in a notes list beside it;
      notes can be edited, deleted, navigated with F8 / Shift+F8, undone and redone
- [ ] when the file changes on disk the view reloads (or asks, per settings) and keeps the
      reading position; a note is never written over a change the user has not seen
- [ ] File → Settings changes the language (English, Russian, Ukrainian, French, Hebrew with
      right-to-left layout, German), the note markers, and the other settings listed in
      Technical details; changes apply without a restart
- [ ] `plancake list <file> [--json]`, `check`, `clear` and `export` work headless with the
      output and exit codes in Technical details
- [ ] F1 opens the user manual in the current language
- [ ] the installer installs PlanCake, puts `plancake` on the PATH, checks for the WebView2
      Runtime, and the portable zip runs from any folder
- [ ] all validation commands pass

## Validation commands

- translations: `pwsh -NoProfile -File ./src/PlanCake/locale/scripts/Compile-Translations.ps1 -Strict`
  (before building, once any `.po` exists; in Task 1 the path is still under `src/WinFormsTemplate`)
- build: `dotnet build`
- test: `dotnet test`
- format check: `dotnet format --verify-no-changes`

## Context

- **Template conventions are binding.** Read `CLAUDE.md` before every task: accessibility rules
  (`TableLayoutPanel`, real `Label`s, keyboard path for everything), GetText.NET `.po`/`.mo` and
  **no `.resx`**, GitVersion (no version literals), code style, single project, `App` not
  `Application`, data folder layout, startup error handling, tests that touch `Config` or
  `Localization` not running in parallel.
- **SIC! is the reference application** for everything the template does not have yet. Copy
  its patterns, adapted to PlanCake names:
  - dual GUI/CLI entry point: `C:\repos\Oire\sic\src\Sic\Program.cs` (`RunCli`, System.CommandLine
    2.x with `SetAction` and `parseResult.GetValue`)
  - `Utils\TextDirection.cs` (`IsRightToLeft`, `Apply(Form)`) and `Utils\DialogHelper.cs`
    (every message box goes through it, for right-to-left)
  - `SettingsDialog.cs`: tabs, language list built from `locale\<code>\` folders holding a
    `.mo`, native culture names, save + `Localization.SetLanguage`
  - `MainWindow.cs`: `NativeMenuBar` created in `OnHandleCreated` from a declarative
    `NativeMenuSpec` (`BuildMenuSpec`), enabled state via `IsEnabled` on the spec items,
    `ApplyLocalization()` for a live language switch (`Localizer.Revert` + `Localize` +
    `TextDirection.Apply` + menu re-attach), help lookup `help\<culture>\manual.html` →
    `help\<two-letter>\manual.html` → `help\en\manual.html` opened with `UseShellExecute`
  - `AboutDialog.cs`: version, copyright, repository link, "Copy info" button
  - `Services\UpdateService.cs` + `Utils\Enums\UpdateCheckInterval.cs`: NetSparkle with an
    Ed25519-signed appcast
  - `installer\` (`sic.iss`, `CodeDependencies.iss`, `Languages\Custom.<code>.isl`,
    `Build-Installer.ps1`, `deploy.example.json`) and `manifests\` (winget, made with
    `wingetcreate`; see SIC's `CLAUDE.md` for the command and the `|x64` quirk)
- **Oire.WinForms.NativeControls** (`C:\repos\Oire\winforms-native-controls`, the template
  references 1.2.0): `NativeMenuBar` / `NativeMenuSpec` (`Add`, `AddCheckable`, `AddMenu`,
  `AddSeparator`, shortcut text + `Keys`, `null` keys for display-only shortcuts, `Rebuild`),
  `NativeContextMenu` (`AttachTo`, `Show(owner, screenLocation)` at
  `src/Oire.WinForms.NativeControls/NativeContextMenu.cs:90`, `Resolver`, `Rebuild`),
  `NativeListView` (`Columns`, `Items` of `NativeListViewItem` with `Cells`, `ItemActivate`,
  `SelectedIndexChanged`, `EnsureVisible`). Its README explains attach/dispose ordering.
- **New dependencies:** `Microsoft.Web.WebView2` (WinForms control), `Markdig`,
  `System.CommandLine` (same major as SIC), `NetSparkleUpdater.SparkleUpdater` +
  `NetSparkleUpdater.UI.WinForms.NetCore` (same versions as SIC). Latest stable of each.
- **Debussy's note convention:** `C:\Users\User\.claude\plugins\marketplaces\Debussy\plugins\planning\skills\plan-exec\references\settings.md`
  (`noteMarkers`: a single token runs to the end of the line; an `open...close` pair spans
  everything between) and `manual-review.md` beside it. PlanCake must read notes exactly as
  that convention defines them, so a note written by hand in an editor works too.

## Development approach

- One task at a time; the validation commands pass before the next task starts.
- Code changes come with tests for the new and changed behavior, success and error paths, unless
  the change is UI-only or the user said to skip tests. The project has no e2e tests; UI tasks
  end with the manual JAWS checks they list.
- A test that cannot pass until a later task is still written now, marked with a comment naming
  that task.
- Keep backward compatibility unless the user asked for a breaking change.
- When scope changes, update this plan: new tasks get a "➕" prefix, blockers a "⚠️" prefix.
- The primary user is blind and uses JAWS. A UI task is not done until its JAWS checks pass;
  a subagent cannot run JAWS, so it stops and asks the user to run them.
- Every user-visible string goes through `_()` or the designer + `Localizer.Localize`, from the
  first task that introduces it. English only until Task 14.
- Line numbers are **1-based** everywhere a user or a CLI consumer sees them, and in the
  `data-lines` attribute. Markdig's 0-based `Line` is converted at the boundary.

## Implementation steps

### Task 1: Adapt the template to PlanCake

**Files:**
- Rename: `src/WinFormsTemplate/` → `src/PlanCake/`, `tests/WinFormsTemplate.Tests/` →
  `tests/PlanCake.Tests/`, both `.csproj` files, `WinFormsTemplate.slnx` → `PlanCake.slnx`
- Modify: every `.cs` file (namespace), `src/PlanCake/Utils/Constants/App.cs`,
  `src/PlanCake/PlanCake.csproj`, `src/PlanCake/locale/messages.pot`,
  `.github/workflows/dotnet.yml`, `README.md`, `CLAUDE.md`

- [ ] follow the README's "Starting a new application from it" checklist in order: directories,
      project and solution files, `ProjectReference` and `.slnx` paths, `AssemblyName` =
      `plancake` (so the published exe is `plancake.exe`), `RootNamespace` = `Oire.PlanCake`,
      `InternalsVisibleTo` = `PlanCake.Tests`, the `Oire.WinFormsTemplate` namespace everywhere
- [ ] `App.Name` = `PlanCake` (data folder `%APPDATA%\Oire\PlanCake`, config `PlanCake.cfg`);
      remove the unused database constants if nothing refers to them; `Product` = `PlanCake`,
      `Description` = "Read and annotate Markdown files with a screen reader"; catalog name
      in `messages.pot`; the translation-script path in the CI workflow
- [ ] check the gettext scripts: they take the catalog name from `AssemblyName`; with the
      lowercase `plancake` assembly name the catalog becomes `plancake.po`/`.mo` — make sure
      `Localization` looks for the same name (fix whichever side disagrees, and say so in
      `locale/README.md`)
- [ ] replace the README's template text with a short PlanCake README (what it is, build
      commands); rewrite `CLAUDE.md`'s title and structure section for PlanCake, keeping every
      convention
- [ ] the existing tests (`AppConstantsTests`, `ConfigTests`, `LocalizationTests`) pass under
      the new names, adjusted only where they assert on the old name
- [ ] validation commands pass

### Task 2: Host WebView2 and run the JAWS spike

The design depends on three things JAWS must do inside WebView2. This task builds the real
WebView2 host and a throw-away test page, and the user checks them with JAWS. **Its results
decide which note triggers the later tasks build.**

**Files:**
- Modify: `src/PlanCake/PlanCake.csproj`, `src/PlanCake/Ui/MainWindow.cs`,
  `src/PlanCake/Ui/MainWindow.Designer.cs`, `src/PlanCake/Program.cs`
- Create: `src/PlanCake/Ui/DocumentView.cs`, `src/PlanCake/web/spike.html`,
  `src/PlanCake/Utils/StatusAnnouncer.cs`, `src/PlanCake/Utils/TextDirection.cs`,
  `src/PlanCake/Utils/DialogHelper.cs`, `docs/jaws-spike.md`

- [ ] add `Microsoft.Web.WebView2`; `web\**` copied to output; set
      `IncludeNativeLibrariesForSelfExtract` so `WebView2Loader.dll` survives single-file
      publish, and confirm a `dotnet publish -c Release` build starts
- [ ] `DocumentView` (a `UserControl` wrapping the `WebView2` control): creates the
      `CoreWebView2Environment` with its user data folder under `App.DataFolder\WebView2`
      (the install folder is not writable), maps the virtual host `https://app.plancake/` to
      `AppContext.BaseDirectory\web`, disables default context menus, browser accelerator keys,
      the status bar and (in Release) dev tools, and exposes `PostMessage(object)` plus a
      `MessageReceived` event over `chrome.webview` JSON messages
- [ ] copy `TextDirection` and `DialogHelper` from SIC; missing WebView2 Runtime: catch
      `WebView2RuntimeNotFoundException` at first use, show a `DialogHelper` message with the
      download link (`https://go.microsoft.com/fwlink/p/?LinkId=2124703`), exit with
      `ExitCode.Error`
- [ ] `StatusAnnouncer`: sets the status-strip label and raises a UI Automation notification
      (`AccessibilityObject.RaiseAutomationNotification`, `ImportantMostRecent`) so JAWS speaks
      status messages wherever focus is
- [ ] keys pressed while the WebView2 has focus do not pass through the host's message loop:
      handle `CoreWebView2Controller.AcceleratorKeyPressed` (or the control's `ProcessCmdKey`
      forwarding) and route the shortcuts to a single host command table; for the spike, log
      each planned shortcut from Technical details → "Keyboard" to the status announcer
- [ ] `spike.html` per Technical details → "JAWS spike page", loaded at startup for now
- [ ] **stop and ask the user** to run the JAWS checklist in Technical details → "JAWS spike
      checklist" and report the answers; write them to `docs/jaws-spike.md`; then update this
      plan: mark with "⚠️" any trigger that failed and adjust Tasks 6–7 accordingly (if neither
      Enter nor the Applications key reaches the page, stop and rethink with the user)
- [ ] validation commands pass

### Task 3: Parse notes out of a Markdown source

**Files:**
- Create: `src/PlanCake/Notes/NoteMarkers.cs`, `src/PlanCake/Notes/Note.cs`,
  `src/PlanCake/Notes/NoteParser.cs`, `tests/PlanCake.Tests/NoteParserTests.cs`

- [ ] `NoteMarkers` record: `Opening` (required), `Closing` (empty = single-token mode);
      `Validate()` rejects empty opening, leading/trailing whitespace, line breaks, and
      `Closing == Opening`; default `[usernote]` / `[/usernote]`
- [ ] `NoteParser.Parse(string source, NoteMarkers markers)` returns the notes (text, 1-based
      start and end line, character spans) and the **stripped source** with a mapping from
      stripped line numbers to original ones, per Technical details → "Note parsing"
- [ ] tests: paired note on its own line, note spanning several lines, note mid-line with text
      before and after, several notes on one line, single-token mode running to end of line,
      notes inside a fenced code block and a table, line mapping after stripping
- [ ] tests: unterminated opening marker (treated as running to end of file, and reported),
      closing marker without an opening one (left as text), empty note, CRLF source
- [ ] validation commands pass

### Task 4: Render Markdown with source line ranges and notes

**Files:**
- Create: `src/PlanCake/Rendering/MarkdownRenderer.cs`, `src/PlanCake/Rendering/RenderResult.cs`,
  `src/PlanCake/Rendering/BlockInfo.cs`, `tests/PlanCake.Tests/MarkdownRendererTests.cs`
- Modify: `src/PlanCake/PlanCake.csproj` (Markdig)

- [ ] pipeline: `UseAdvancedExtensions()` (pipe tables, task lists, auto-identifiers, …) +
      `UsePreciseSourceLocation()`; parse the stripped source from `NoteParser`
- [ ] walk the AST and stamp each annotatable block with `data-lines="start-end"` in
      **original** line numbers, per Technical details → "Annotatable blocks"; collect a
      `BlockInfo` per block (range, kind, plain-text excerpt)
- [ ] anchor every note to its block and insert its HTML into the AST where Technical details →
      "Note placement in the view" says; two modes: `Interactive` (note buttons) and `Export`
      (static `<aside>`), all note text HTML-encoded
- [ ] every block gets `dir="auto"`; task-list checkboxes stay read-only
- [ ] tests: line ranges for a paragraph, heading, tight and loose list items (a parent item's
      range excludes its nested list), table rows, fenced and indented code, a paragraph in a
      blockquote; ranges correct when notes sit above, inside and below the block
- [ ] tests: note buttons land after the right block for each kind, including a table row and a
      nested list item; note text with `<`, `&` and quotes is encoded; excerpt is plain text
      truncated to 80 characters with an ellipsis
- [ ] validation commands pass

### Task 5: Write notes to the file safely

**Files:**
- Create: `src/PlanCake/Notes/MarkdownFile.cs`, `src/PlanCake/Notes/NoteStore.cs`,
  `src/PlanCake/Notes/StaleFileException.cs`, `tests/PlanCake.Tests/NoteStoreTests.cs`,
  `tests/PlanCake.Tests/MarkdownFileTests.cs`

- [ ] `MarkdownFile`: reads a file detecting the encoding (UTF-8 with or without BOM, UTF-16
      BOMs; otherwise UTF-8) and the dominant line ending; writes back with the same encoding,
      BOM and line ending, atomically (temp file in the same folder + `File.Replace`, or
      `File.Move` when the target is gone); retries a locked file 5 times over about a second,
      then throws `IOException`
- [ ] `NoteStore` operations, each taking the text the caller last rendered and throwing
      `StaleFileException` without writing if the file differs from it: `Add(afterBlock,
      text)`, `Edit(note, text)`, `Delete(note)`, `Clear()`; placement and indentation per
      Technical details → "Note placement in the file"
- [ ] note text validation: rejects text containing the closing marker (paired mode) or a line
      break (single-token mode); converts pasted line breaks to spaces in single-token mode
- [ ] undo/redo: each operation records the file text before and after; `Undo` writes the
      "before" text only if the file still equals the "after" text (otherwise
      `StaleFileException` and both stacks are cleared); `Redo` symmetric
- [ ] tests: placement after a paragraph, a heading, a nested list item (indent), a table row, a
      fenced code block's closing fence, a paragraph inside a blockquote (`> ` prefix kept);
      two notes on one block stack in order; multi-line note; edit; delete; clear
- [ ] tests: stale file rejected with nothing written; CRLF and LF preserved; BOM preserved and
      not added; closing marker in text rejected; locked file retried then failing; undo, redo,
      and undo refused after an external change
- [ ] validation commands pass

### Task 6: Show the document and talk to the page

**Files:**
- Create: `src/PlanCake/web/index.html`, `src/PlanCake/web/app.js`, `src/PlanCake/web/app.css`,
  `src/PlanCake/Ui/PageMessages.cs`
- Modify: `src/PlanCake/Ui/DocumentView.cs`, `src/PlanCake/Ui/MainWindow.cs`,
  `src/PlanCake/Program.cs`
- Delete: `src/PlanCake/web/spike.html`

- [ ] `index.html` + `app.js`: a `main` element that receives rendered HTML; the message
      protocol in Technical details → "Page protocol"; a delegated `click` listener and a
      `contextmenu` listener on the document (only those Task 2 kept); `tabindex="-1"` on
      annotatable blocks so focusing them moves the JAWS virtual cursor
- [ ] `app.css`: readable defaults, visible focus outline, note buttons distinct from text,
      supports Windows high contrast (`forced-colors`) and light/dark (`prefers-color-scheme`)
- [ ] `MainWindow` opens the file given on the command line: read with `MarkdownFile`, render,
      post to the page, set the window title to `<file name> - PlanCake`; the page's `<title>`
      is the first heading
- [ ] links: `NavigationStarting` and `NewWindowRequested` for anything but the app host are
      canceled and opened in the default browser; in-page anchors (`#…`) scroll and focus
- [ ] position restore: after every re-render the page focuses the block matching the last one
      the user interacted with (same text, else nearest start line), or the element the host
      names (a new note)
- [ ] zoom: Ctrl+Plus, Ctrl+Minus, Ctrl+0 set `ZoomFactor` in steps of 10%, 50%–300%
- [ ] **ask the user** to check with JAWS: an 800-line plan reads with H, I, L, T and B
      navigation, no Markdown punctuation is read, links open in the browser, zoom works
- [ ] validation commands pass

### Task 7: Add, edit and delete notes from the document

**Files:**
- Create: `src/PlanCake/Ui/NoteDialog.cs`, `src/PlanCake/Ui/NoteDialog.Designer.cs`
- Modify: `src/PlanCake/Ui/MainWindow.cs`, `src/PlanCake/web/app.js`

- [ ] `NoteDialog`: title "Add note" or "Edit note"; a read-only label "Note on:" with the block
      excerpt; a multiline, word-wrapping note `TextBox` with a real `Label`; OK and Cancel;
      Enter saves and Ctrl+Enter inserts a line break, or the reverse, per the setting (Task 11;
      hard-code the default until then); OK disabled while the text is blank; a rejected text
      (closing marker) shows why and keeps the dialog open
- [ ] Enter on a block → `NoteDialog` → `NoteStore.Add` → re-render → focus the new note button
      → announce "Note added"; Enter on a note button → edit
- [ ] Applications key / Shift+F10 on a block or note → `NativeContextMenu.Show` at the
      element's screen position (page sends its client rectangle; convert with the zoom factor,
      `devicePixelRatio` and `PointToScreen`): Add note, Edit note, Delete note (only on a
      note), Copy block text
- [ ] `StaleFileException` → nothing written, re-render, announce "The file changed. Please try
      again." with the dialog's text kept for the retry; `IOException` → error message, dialog
      text kept
- [ ] Undo (Ctrl+Z) and Redo (Ctrl+Y) through `NoteStore`, announced ("Note added undone" etc.)
- [ ] **ask the user** to check with JAWS: add, edit, delete, undo and redo a note on a
      paragraph, a nested list item, a table row and a code block; the virtual cursor lands on
      the new note each time
- [ ] validation commands pass

### Task 8: Notes list beside the document

**Files:**
- Modify: `src/PlanCake/Ui/MainWindow.cs`, `src/PlanCake/Ui/MainWindow.Designer.cs`,
  `src/PlanCake/web/app.js`

- [ ] a `SplitContainer` (document first, notes list second) with a `NativeListView`, columns
      "Lines", "Block", "Note", filled from the render result after every render; a real
      `Label` "Notes" names it
- [ ] F6 switches focus between the document and the list; View → Notes list shows or hides the
      list (hidden list is skipped by F6)
- [ ] Enter (`ItemActivate`) jumps to that note in the document and focuses it; Delete deletes
      it; a context menu on the list offers Edit note and Delete note
- [ ] F8 / Shift+F8 move to the next / previous note from wherever focus is: in the document
      they focus the note button after / before the current position; in the list they move
      the selection; at the ends, announce "No more notes"
- [ ] the list keeps its selection across re-renders when the same note still exists
- [ ] **ask the user** to check the list, F6 and F8 with JAWS
- [ ] validation commands pass

### Task 9: Menu bar and the small dialogs

**Files:**
- Create: `src/PlanCake/Ui/ShortcutsDialog.cs` (+ `.Designer.cs`), `src/PlanCake/Ui/AboutDialog.cs`
  (+ `.Designer.cs`)
- Modify: `src/PlanCake/Ui/MainWindow.cs`

- [ ] `BuildMenuSpec()` with exactly the menus in Technical details → "Menus", shortcuts
      registered through the host command table so they work with focus in the document too;
      items that need a file or a note are disabled without one
- [ ] File → Open (`OpenFileDialog`, filter `*.md;*.markdown`, then all files), Open in editor
      (`UseShellExecute` on the file), Exit; View → Reload
- [ ] `ShortcutsDialog`: a `NativeListView` with "Command" and "Shortcut" columns built from the
      same command table (so it cannot drift), plus the in-document keys (Enter, Applications
      key); a Close button
- [ ] `AboutDialog` modeled on SIC's: product, version, copyright, repository link, "Copy info"
- [ ] Delete all notes asks for confirmation (always), then `NoteStore.Clear`
- [ ] validation commands pass

### Task 10: Follow changes on disk, and one window per file

**Files:**
- Create: `src/PlanCake/Utils/FileWatcher.cs`, `src/PlanCake/Utils/SingleInstance.cs`,
  `tests/PlanCake.Tests/SingleInstanceTests.cs`
- Modify: `src/PlanCake/Ui/MainWindow.cs`, `src/PlanCake/Program.cs`

- [ ] `FileWatcher`: watches the open file's folder for changes, renames and deletions of that
      file (editors often save by rename), debounced 300 ms, marshaled to the UI thread;
      ignores changes whose content equals what PlanCake itself last wrote
- [ ] external change → re-render with position restore and announce "File reloaded", or, when
      the setting says ask, ask first (No keeps the view, announces that F5 reloads, and note
      actions then fail as stale)
- [ ] file deleted or renamed away → announce it, disable note commands and Reload until it
      reappears, then reload
- [ ] `SingleInstance`: per normalized full path, a named pipe `PlanCake-<SHA-256 of the
      upper-cased path>`; a second `plancake same.md` connects, calls
      `AllowSetForegroundWindow`, sends "activate", exits with `ExitCode.Success`; the first
      window restores and activates itself; File → Open re-registers under the new path
- [ ] tests: path normalization (case, `..`, trailing separators) gives one pipe name; a second
      registration for the same path is detected
- [ ] validation commands pass

### Task 11: Settings

**Files:**
- Create: `src/PlanCake/Ui/SettingsDialog.cs` (+ `.Designer.cs`), `src/PlanCake/Utils/Enums/ExternalChangeAction.cs`,
  `src/PlanCake/Utils/Enums/BlockEnterAction.cs`, `src/PlanCake/Utils/Enums/NoteEnterAction.cs`
- Modify: `src/PlanCake/Utils/Config.cs`, `src/PlanCake/Ui/MainWindow.cs`,
  `src/PlanCake/Ui/NoteDialog.cs`, `tests/PlanCake.Tests/ConfigTests.cs`

- [ ] `Config` sections and defaults per Technical details → "Settings"; drop the template's
      `ConfirmExit` (nothing is ever unsaved)
- [ ] `SettingsDialog` like SIC's: a `TabControl` with General and Notes tabs, each a flat
      `TableLayoutPanel`, real `Label`s, OK/Cancel; language list built as SIC builds it;
      markers validated with `NoteMarkers.Validate` and the reason shown next to the field
- [ ] File → Settings (Ctrl+comma); on OK: save, apply language live (`ApplyLocalization`, as
      in SIC, including the menu and list columns), re-render with the new markers, re-apply
      the other settings
- [ ] `NoteDialog` follows the note-field Enter setting; the page follows the block Enter
      setting (Enter opens the context menu instead of the dialog)
- [ ] tests: defaults, round trip of every new setting, invalid enum or marker values in the
      file fall back to defaults
- [ ] validation commands pass

### Task 12: Command-line mode

**Files:**
- Create: `src/PlanCake/Cli/CliRunner.cs`, `src/PlanCake/Cli/ConsoleAttacher.cs`,
  `tests/PlanCake.Tests/CliRunnerTests.cs`
- Modify: `src/PlanCake/Program.cs`, `src/PlanCake/Utils/Constants/ExitCode.cs`,
  `src/PlanCake/PlanCake.csproj` (System.CommandLine)

- [ ] System.CommandLine root command with an optional `file` argument (→ GUI, via
      `SingleInstance`) and the subcommands `list`, `check`, `clear`, `export` with the options,
      output and exit codes in Technical details → "Command line"
- [ ] `ConsoleAttacher`: for subcommands, `--help` and `--version` only,
      `AttachConsole(ATTACH_PARENT_PROCESS)` so output shows in an interactive terminal; the GUI
      path never touches the console
- [ ] `CliRunner` takes `TextWriter`s for output and error so tests need no console; `Config`
      loaded with GUI dialogs off (errors to stderr), markers from config unless overridden
- [ ] tests: `list` text and JSON on a file with notes of each kind; `list` on a file without
      notes; `check` 0 and 3; `clear` removes all notes and leaves the rest byte-identical;
      `export` writes standalone HTML with notes as `<aside>` and no script
- [ ] tests: missing file, unreadable file, invalid marker options → message on stderr and
      `ExitCode.Error`
- [ ] validation commands pass

### Task 13: Updates

**Files:**
- Create: `src/PlanCake/Services/UpdateService.cs`, `src/PlanCake/Utils/Enums/UpdateCheckInterval.cs`
- Modify: `src/PlanCake/Utils/Constants/App.cs`, `src/PlanCake/Utils/Config.cs`,
  `src/PlanCake/Ui/SettingsDialog.cs`, `src/PlanCake/Ui/MainWindow.cs`, `.gitignore`

- [ ] port SIC's `UpdateService` and `UpdateCheckInterval`; `App.AppcastUrl` =
      `https://plancake.oire.dev/appcast.xml` (a placeholder; see Post-completion) and
      `App.UpdatePublicKey` from a key pair the user generates
- [ ] settings: check for updates on startup, background interval (General tab); Help → Check
      for updates
- [ ] `keys/` ignored in git as in SIC
- [ ] validation commands pass

### Task 14: Translations

**Files:**
- Create: `src/PlanCake/locale/{ru,uk,fr,he,de}/<catalog>.po` (catalog name as settled in Task 1)
- Modify: `src/PlanCake/locale/messages.pot`, `src/PlanCake/PlanCake.csproj`,
  `src/PlanCake/web/app.js`

- [ ] every page string (note button prefix, live messages) comes from the host through the
      `strings` message, not from `app.js` literals
- [ ] `Extract-Strings.ps1`, then `New-Language.ps1` for ru, uk, fr, he, de, then translate every
      entry; no fuzzy entries; menu mnemonics unique per menu level in every catalog (run the
      app once in each language to prove it)
- [ ] `SatelliteResourceLanguages` = `en;ru;uk;fr;he;de`
- [ ] Hebrew: every form calls `TextDirection.Apply` after `Localizer.Localize`, every message
      box goes through `DialogHelper`, the page gets `dir="rtl"` on its chrome while blocks keep
      `dir="auto"`
- [ ] test: each shipped catalog compiles and loads (extend `LocalizationTests`)
- [ ] validation commands pass

### Task 15: User manual

**Files:**
- Create: `src/PlanCake/help/{en,ru,uk,fr,he,de}/manual.html`
- Modify: `src/PlanCake/PlanCake.csproj`, `src/PlanCake/Ui/MainWindow.cs`

- [ ] Help → User manual (F1) opens `help\<culture>\manual.html` with SIC's fallback chain;
      `help\**` copied to output
- [ ] write the manual with the `write-manual` skill (English first, then the five
      translations): reading with JAWS, adding and editing notes, the notes list, settings
      (including what changing markers does to existing notes), the command line, the `pk`
      alias, and how PlanCake fits the plan-make manual-review step
- [ ] validation commands pass

### Task 16: Installer, portable zip, winget

**Files:**
- Create: `installer/plancake.iss`, `installer/CodeDependencies.iss`,
  `installer/Languages/Custom.{en,ru,uk,fr,he,de}.isl`, `installer/Build-Installer.ps1`,
  `installer/build-installer.bat`, `installer/deploy.example.json`, `changelogs/1.0.0.md`
- Modify: `.gitignore`

- [ ] `plancake.iss` from SIC's `sic.iss`: PlanCake names, the six languages, `web\*`, `help\*`,
      `locale\*`; `Dependency_AddDotNet100Desktop` **and** `Dependency_AddWebView2`
- [ ] add `{app}` to the machine `PATH` (`[Registry]` on
      `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment`, `ChangesEnvironment=yes`,
      skipped when already present, removed on uninstall)
- [ ] `Build-Installer.ps1` from SIC's (publish, ISCC, portable zip with an empty `userdata\`,
      appcast with `netsparkle-generate-appcast`, optional deploy via `deploy.json`); `deploy.json`
      and `installer/Output/` ignored in git
- [ ] document the `wingetcreate` command for `Oire.PlanCake` in `CLAUDE.md` (manifests are
      created at the first release, not now)
- [ ] validation commands pass

### Task 17: Update documentation

- [ ] README: what PlanCake is, installing, reading and annotating, the command line with
      examples, the `pk` alias (`Set-Alias pk plancake` in the PowerShell profile,
      `alias pk=plancake` in bash), how to point Debussy's `noteMarkers` at PlanCake's markers
- [ ] CLAUDE.md: architecture (Notes, Rendering, Ui, Cli, Services, `web/`), the page protocol,
      the WebView2 gotchas (user data folder, accelerator keys, single-file native loader),
      the JAWS spike results, the three pieces of a language, the AttachConsole caveat
- [ ] CHANGELOG.md and `changelogs/1.0.0.md`: the 1.0.0 feature list

## Technical details

### Note parsing

- Paired mode: a note starts at any occurrence of `Opening` and ends at the next `Closing`;
  its text is everything between, trimmed, internal line breaks kept. Single-token mode: from
  `Opening` to the end of that line. Matching is ordinal and case-sensitive.
- Stripping: the note's characters (markers included) are removed. A line left empty or
  holding only whitespace and `>` characters by the removal is removed entirely; a line with
  other text left on it stays (with the note cut out). The stripped source therefore has the
  same Markdown structure as the file without any notes.
- The line map records, for every stripped line, its original 1-based line number, so a block
  at stripped lines 40–42 reports original lines 41–44 when a note sat inside it.
- An opening marker with no closing marker runs to the end of the file and is flagged
  `Unterminated`; the GUI announces it, `list` prints a warning to stderr.

### Annotatable blocks

The innermost block the user can land on carries `data-lines="start-end"` (original, 1-based,
inclusive) and `tabindex="-1"`:
- `ParagraphBlock` (but see list items), `HeadingBlock`, `FencedCodeBlock` and `CodeBlock`
  (range includes both fences), `TableRow`; `ThematicBreakBlock` is not annotatable;
- `ListItemBlock`: stamped with the range of its **leading paragraph only**, because in a tight
  list Markdig renders the paragraph without a `<p>` and its attributes would be lost; nested
  lists inside the item get their own stamps. In a loose list the `<p>` is stamped too, with
  the same range;
- blocks inside a `QuoteBlock` are stamped; the quote itself is not.

The end line comes from the block's `Span.End` mapped to a line through a table of line-start
offsets of the stripped source, then through the line map.

### Note placement in the view

A note is anchored to the last annotatable block that ends before the note starts (for a note
written by hand in the middle of a block: the block containing its line). The renderer inserts
an `HtmlBlock` right after the anchor in the anchor's parent container; for a `TableRow` anchor
it appends the `HtmlBlock` to the row's last cell; for a list item's leading paragraph, inside
the item after that paragraph.

- Interactive: `<button type="button" class="note" data-note="<index>">Note: <text></button>`
  ("Note:" localized; line breaks as `<br>`).
- Export: `<aside class="note"><strong>Note:</strong> <text></aside>`.

### Note placement in the file

- The note goes on new line(s) directly after the anchor block's last original line, **after
  any notes already anchored there**, so notes stack in the order added.
- Prefix: the anchor's first line's leading run of whitespace and `>` characters; for a list
  item, whitespace up to the item's content column (so the note sits inside the item) plus any
  `>` prefix. Every line of a multi-line note gets the prefix.
- Written form: `<prefix>[usernote]text[/usernote]`; a multi-line note puts the opening marker
  before its first line and the closing marker after its last. Single-token mode:
  `<prefix>!USERNOTE! text`.
- No blank lines are added or removed.

### Page protocol

JSON messages through `chrome.webview.postMessage` / `PostWebMessageAsJson`, each with a `type`:
- host → page: `render` `{ html, generation, focus: { lines?, note? } }`, `strings`
  `{ notePrefix, … }`, `focusNote` `{ note }`, `focusLines` `{ lines }`, `nextNote` /
  `previousNote` `{}`, `announce` `{ text }` (only if Task 2 showed UIA notifications are
  not heard in the virtual buffer)
- page → host: `activate` `{ lines, generation }` (Enter on a block), `activateNote`
  `{ note, generation }`, `contextMenu` `{ lines, note?, rect, generation }`, `position`
  `{ lines, text }` (last block interacted with), `noMoreNotes` `{}`, `ready` `{}`
- the host ignores any message whose `generation` is not the latest render

### Keyboard

Host shortcuts (must work with focus in the document and in the list): Ctrl+O, Ctrl+E,
Ctrl+comma, F5, F6, F8, Shift+F8, Ctrl+Z, Ctrl+Y, Ctrl+Plus, Ctrl+Minus, Ctrl+0, F1, Alt+F4.
In the document: Enter on a block / note, Applications key and Shift+F10. In the list: Enter,
Delete, Applications key.

### Menus

- **File:** Open… (Ctrl+O), Open in editor (Ctrl+E), separator, Settings… (Ctrl+comma),
  separator, Exit (Alt+F4, display only)
- **View:** Notes list (checkable), Switch pane (F6), separator, Zoom in (Ctrl+Plus), Zoom out
  (Ctrl+Minus), Reset zoom (Ctrl+0), separator, Reload (F5)
- **Notes:** Edit note, Delete note, separator, Next note (F8), Previous note (Shift+F8),
  separator, Undo (Ctrl+Z), Redo (Ctrl+Y), separator, Delete all notes…
- **Help:** User manual (F1), Keyboard shortcuts, separator, Check for updates, About PlanCake

No Edit menu; no menu may end up with a single item.

### Settings

`PlanCake.cfg`, SharpConfig sections:
- `[General]`: `Language` (default `System`), `ConfirmNoteDelete` (true),
  `ExternalChangeAction` (`AutoReload` | `Ask`, default `AutoReload`), `ShowNotesList` (true),
  `CheckForUpdatesOnStartup` (true), `UpdateCheckInterval` (`Weekly`)
- `[Notes]`: `OpeningMarker` (`[usernote]`), `ClosingMarker` (`[/usernote]`, empty = single
  token), `BlockEnterAction` (`AddNote` | `ContextMenu`, default `AddNote`), `NoteEnterAction`
  (`Save` | `NewLine`, default `Save`; Ctrl+Enter does the other)

### Command line

`plancake [file]` opens the window. Subcommands (all accept `--open-marker <text>` and
`--close-marker <text>`, where `--close-marker ""` selects single-token mode):
- `list <file> [--json]`: one note per entry. Text form, one line per note:
  `<noteStart>-<noteEnd> after <blockStart>-<blockEnd> "<excerpt>": <text>` (line breaks in the
  text shown as ` / `). JSON form: an array of `{ "noteStartLine", "noteEndLine",
  "blockStartLine", "blockEndLine", "blockKind", "blockExcerpt", "text" }`, UTF-8, indented.
  No notes: empty output (text) or `[]` (JSON). Exit 0.
- `check <file>`: prints the note count; exit `ExitCode.Success` (0) with none,
  `ExitCode.NotesRemain` (3, new constant) with any. `ExitCode.Error` (1) stays for failures, so
  a caller can tell "notes left" from "could not read".
- `clear <file>`: removes every note, prints how many; exit 0.
- `export <file> -o <out.html>`: standalone HTML (inline CSS, no script, `lang` from the UI
  language, `<title>` from the first heading), notes as `<aside>`; exit 0.
- Errors: message on stderr, exit 1.

Caveat for the docs: interactive PowerShell and cmd do not wait for a GUI-subsystem exe, so
attached output can appear after the prompt returns. Piped or captured output (Claude's tools,
`| Out-Host`, bash) always waits and is always complete.

### JAWS spike page

`web/spike.html`, served from the app host, `lang="en"`: an `h1`, an `h2`, three paragraphs
(the second hard-wrapped over three source lines), a nested bullet list, a task list with one
checked and one unchecked item, a three-row table, a fenced code block, an external link, and a
`<button class="note">Note: sample note</button>`. Every block has `data-lines` and
`tabindex="-1"`. Script: a delegated `click` listener reporting `closest('[data-lines]')`; a
`contextmenu` listener doing the same and calling `preventDefault()`; both post a message the
host announces through `StatusAnnouncer` ("click on paragraph, lines 5-7"). A host shortcut
(F9, spike only) asks the page to focus the third paragraph.

### JAWS spike checklist

For the user to run in the spike build, with JAWS in the virtual cursor:
1. Enter on the second paragraph: is "click on paragraph, lines …" announced, with the right
   lines? Same on a nested list item, a table cell, the code block, a heading.
2. Applications key (and Shift+F10) on the same elements: is "contextmenu on …" announced?
3. F9: does JAWS read the third paragraph, and does Down Arrow continue from there?
4. Enter and Space on the task-list checkboxes: what does JAWS say, and does anything toggle?
5. Are the status announcements heard at all while in the document?
6. Each shortcut in Technical details → "Keyboard": does the host announce it (that is, does
   JAWS let it through)?

## Post-completion

- Full JAWS pass on a real plan of 800+ lines: read it end to end, annotate twenty blocks of
  every kind, let Claude act on them through manual review, confirm the reload keeps the place.
- Debussy (`C:\Users\User\.claude\plugins\marketplaces\Debussy`, `plugins/planning`):
  `noteMarkers` already accepts pairs, so the user sets `"noteMarkers": ["[usernote]...[/usernote]",
  "!USERNOTE!"]` in `~/.claude/debussy.json`. Separate change there: plan-make's Manual review
  hand-off starts `plancake <plan>` when `plancake` is on the PATH, and manual review uses
  `plancake list --json` to find notes and `plancake check` for its verify step.
- Add `Set-Alias pk plancake` to the PowerShell profile (and `alias pk=plancake` to the bash
  profile).
- Decide where the appcast and downloads are hosted (`plancake.oire.dev` is a placeholder),
  generate the NetSparkle key pair, create the GitHub release and submit the winget manifest
  with `wingetcreate` at the first release.
