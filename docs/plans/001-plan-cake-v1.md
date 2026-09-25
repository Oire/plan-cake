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
on disk is the single source of truth: there is no Save command and no side file. The notes
can also be exported as JSON with their references (`plancake list --json -o notes.json`, or
File → Export notes…), but that is a copy, never something PlanCake reads back.

The application is dual-mode like SIC! (`C:\repos\Oire\sic`): run without a subcommand it opens
the window; with a subcommand (`list`, `check`, `clear`, `export`) it works headless, which is
what Claude uses during manual review.

This repository was created from Oire's `winforms-template` and still carries the template's
names; Task 1 adapts it.

## Done when

- [ ] `plancake plan.md` opens a window showing `plan.md` rendered as HTML in WebView2; JAWS
      reads it in its virtual buffer with heading, list and table navigation, and no
      Markdown punctuation is read out
- [ ] pressing Enter on a paragraph, list item, heading, table row or code block (or clicking
      it) opens the note dialog without switching JAWS to forms mode, and the Applications key
      or Shift+F10 opens the context menu; confirming writes `[usernote]text[/usernote]` after
      that block's last source line, and the view returns to the new note
- [ ] notes show in the document after their block, as user notes (`role="note"`) or buttons
      per settings, and in a notes list beside it;
      notes can be edited, deleted, navigated with F9 / Shift+F9, undone and redone
- [ ] a task-list checkbox can be toggled from the document (Space, Enter or a click), after a
      confirmation that can be turned off; the file on disk gets `[x]` or `[ ]` on that item's
      line, and the toggle can be undone and redone
- [ ] when the file changes on disk the view reloads (or asks, per settings) and keeps the
      reading position; a note is never written over a change the user has not seen
- [ ] File → Settings changes the interface language (English, Russian, Ukrainian, French,
      Hebrew with right-to-left layout, German), the default document language, the note
      markers, and the other settings listed in Technical details; changes apply without a
      restart
- [ ] View → Interface language and View → Document language switch those two languages from
      the menu; JAWS reads the plan in the document language (English by default), whatever
      the interface language is
- [ ] links and raw HTML in a plan cannot navigate the view away or run script
- [ ] `plancake list <file> [--json]`, `check`, `clear` and `export` work headless with the
      output and exit codes in Technical details
- [ ] a Markdown file opens from File → Open, the command line, drag and drop, the clipboard
      (Ctrl+V after copying it in Explorer) and a link (downloaded to the Downloads folder)
- [ ] every interaction works with the keyboard and the mouse, left to right and right to left
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
  `System.CommandLine`, `NetSparkleUpdater.SparkleUpdater` +
  `NetSparkleUpdater.UI.WinForms.NetCore`: the latest stable version of each (for the last
  three, the same major version SIC uses, so its code ports as is). Markdig's
  `UseAdvancedExtensions()` covers GitHub Flavored Markdown (pipe tables, task lists,
  strikethrough, autolinks) and adds footnotes, definition lists and more.
- **Debussy's note convention:** `C:\Users\User\.claude\plugins\marketplaces\Debussy\plugins\planning\skills\plan-exec\references\settings.md`
  (`noteMarkers`: a single token runs to the end of the line "and on into following lines that
  plainly continue it"; an `open...close` pair spans everything between) and
  `manual-review.md` beside it. PlanCake reads **one** marker configuration, its own, and a
  note written by hand in an editor with that pair works too. Deliberate divergence: in
  single-token mode a PlanCake note ends at the end of its line, because "plainly continues"
  is a judgment a parser cannot make.

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
  first task that introduces it; page strings come from the host through the `strings` message
  from Task 6 on, never as `app.js` literals. English only until Task 15.
- Every dialog sets its own title in the constructor, right after `Localizer.Localize`
  (`Text = _("Add note");`): the gettext extractor misses a form's bare `Text =` in the
  designer, as SIC's `CLAUDE.md` explains.
- Settings that a task needs before Task 12 exists are hard-coded to their default in that task
  and wired to `Config` in Task 12.
- Every interaction works with the keyboard **and** the mouse, and every screen works left to
  right and right to left (Hebrew): forms through `TextDirection.Apply`, the page through
  `dir` on its chrome and `dir="auto"` on content. Check both directions in every UI task.
- The **interface language** (menus, dialogs, page chrome) and the **document language** (the
  `lang` of the rendered plan, which picks JAWS's voice) are independent. Never derive one from
  the other.
- Never open a dialog, a message box or a menu directly inside a WebView2 event handler
  (`WebMessageReceived`, `NavigationStarting`, …) or inside `ProcessCmdKey` when WebView2
  forwarded the key: defer it with `BeginInvoke`. A nested message loop inside those
  handlers re-enters WebView2, which Microsoft's threading guidance warns against.
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

- [x] follow the README's "Starting a new application from it" checklist in order: directories,
      project and solution files, `ProjectReference` and `.slnx` paths, `AssemblyName` =
      `plancake` (so the published exe is `plancake.exe`), `RootNamespace` = `Oire.PlanCake`,
      `InternalsVisibleTo` = `PlanCake.Tests`, the `Oire.WinFormsTemplate` namespace everywhere
- [x] `App.Name` = `PlanCake` (data folder `%APPDATA%\Oire\PlanCake`, config `PlanCake.cfg`);
      remove the database constants (`DatabaseFileExtension`, `DatabasePath`) and the
      `AppConstantsTests` assertions on them, since PlanCake has no database; `Product` =
      `PlanCake`, `Description` = "Read and annotate Markdown files with a screen reader";
      catalog name in `messages.pot`; the translation-script path in the CI workflow
- [x] catalog name is `PlanCake` (`PlanCake.po` / `.mo`): `Localization` already loads
      `<App.Name>.mo`, but the gettext scripts take the name from `AssemblyName` (`plancake`),
      so change `Get-CatalogName.ps1` to produce `PlanCake`; correct the template README's
      claim that `App.Name` drives the catalog, in `README.md` and `locale/README.md`
- [x] replace the README's template text with a short PlanCake README (what it is, build
      commands); rewrite `CLAUDE.md`'s title and structure section for PlanCake, keeping every
      convention
- [x] the existing tests (`AppConstantsTests`, `ConfigTests`, `LocalizationTests`) pass under
      the new names, adjusted only where they assert on the old name
- [x] repository metadata: copy SIC's Apache 2.0 `LICENSE` (so GitHub detects and shows the
      license), set the GitHub description and topics with `gh repo edit Oire/plan-cake
      --description … --add-topic …` (e.g. `markdown`, `accessibility`, `screen-reader`,
      `jaws`, `winforms`, `webview2`, `dotnet`); the README title carries a pancake emoji 🥞
      (the name plays on "pancake") and gets a "Why" section explaining why PlanCake was built
      (reading long plans with JAWS, and annotating them for Claude)
- [x] validation commands pass

### Task 2: Host WebView2 and run the JAWS spike

The design depends on three things JAWS must do inside WebView2. This task builds the real
WebView2 host and a throw-away test page, and the user checks them with JAWS. **Its results
decide which note triggers the later tasks build.**

**Files:**
- Modify: `src/PlanCake/PlanCake.csproj`, `src/PlanCake/Ui/MainWindow.cs`,
  `src/PlanCake/Ui/MainWindow.Designer.cs`, `src/PlanCake/Program.cs`
- Create: `src/PlanCake/Ui/DocumentView.cs`, `src/PlanCake/web/spike.html`,
  `src/PlanCake/Utils/StatusAnnouncer.cs`, `src/PlanCake/Utils/TextDirection.cs`,
  `src/PlanCake/Utils/DialogHelper.cs`, `docs/jaws-spike.md`, `src/PlanCake/Ui/HostCommands.cs`,
  `tests/PlanCake.Tests/HostCommandsTests.cs`, `Directory.Build.targets` (drops the WebView2
  WPF reference, whose WindowsBase clashes with WinForms: MSB3277)

- [x] add `Microsoft.Web.WebView2`; `web\**` copied to output; confirm a
      `dotnet publish -c Release` build starts (single-file publish leaves `WebView2Loader.dll`
      next to the exe, which the installer and the portable zip must ship). Two different
      things, not to be confused: `WebView2Loader.dll` is a small DLL from the NuGet package
      that PlanCake always ships itself; the **WebView2 Runtime** (the Edge engine) is never
      presumed present: the installer installs it through `CodeDependencies.iss` when missing
      (Task 17), and the app checks for it at startup (next item), which covers the portable
      zip
- [x] `DocumentView` (a `UserControl` wrapping the `WebView2` control): creates the
      `CoreWebView2Environment` with its user data folder under `App.DataFolder\WebView2`
      (the install folder is not writable), maps the virtual host `https://app.plancake/` to
      `AppContext.BaseDirectory\web`, disables default context menus, browser accelerator keys,
      the status bar and (in Release) dev tools, and exposes `PostMessage(object)` plus a
      `MessageReceived` event over `chrome.webview` JSON messages
- [x] copy `TextDirection` and `DialogHelper` from SIC; missing WebView2 Runtime: before
      creating the window, `Program` checks
      `CoreWebView2Environment.GetAvailableBrowserVersionString()` (catching
      `WebView2RuntimeNotFoundException`), shows a `DialogHelper` message with the download link
      (`https://go.microsoft.com/fwlink/p/?LinkId=2124703`) and returns `ExitCode.Error`;
      `DocumentView` never exits the process itself (deciding to stop is `Program`'s job)
- [x] `StatusAnnouncer`: sets the status-strip label and raises a UI Automation notification
      (`AccessibilityObject.RaiseAutomationNotification`, `ImportantMostRecent`) so JAWS speaks
      status messages wherever focus is
- [x] keys pressed while the WebView2 has focus do not pass through the host's message loop,
      and the native menu bar's accelerator table never sees them. The WinForms `WebView2`
      control forwards accelerator keys to `ProcessCmdKey`: override `ProcessCmdKey` in
      `MainWindow` and route shortcuts to a single host command table (the same table the menu
      uses); for the spike, log each planned shortcut from Technical details → "Keyboard" to
      the status announcer. Found while building it: the WinForms control (1.0.4191.47) does
      not call `ProcessCmdKey`; it raises its own `KeyDown` from the browser's
      `AcceleratorKeyPressed`, so `DocumentView.AcceleratorKeyDown` carries those keys and
      `MainWindow` sends both paths through `HostCommands` (`Ui/HostCommands.cs`)
- [x] `spike.html` per Technical details → "JAWS spike page", loaded at startup for now
- [x] **stop and ask the user** to run the JAWS checklist in Technical details → "JAWS spike
      checklist" and report the answers; write them to `docs/jaws-spike.md`; then update this
      plan: mark with "⚠️" any trigger that failed and adjust Tasks 6–7 accordingly (if neither
      Enter nor the Applications key reaches the page, stop and rethink with the user).
      Results: Enter and the Applications key / Shift+F10 both reach the page; Enter cannot be
      told from a mouse click; Enter on a focusable block puts JAWS in forms mode; UIA
      notifications are heard; F8 is taken by JAWS (note navigation moves to F9); task-list
      checkboxes should be toggleable (new Task 7a)
- [x] validation commands pass

### Task 3: Parse notes out of a Markdown source

**Files:**
- Create: `src/PlanCake/Notes/NoteMarkers.cs`, `src/PlanCake/Notes/Note.cs`,
  `src/PlanCake/Notes/NoteParser.cs`, `tests/PlanCake.Tests/NoteParserTests.cs`

- [x] `NoteMarkers` record: `Opening` (required), `Closing` (empty = single-token mode);
      `Validate()` rejects empty opening, leading/trailing whitespace, line breaks, and
      `Closing == Opening`; default `[usernote]` / `[/usernote]`
- [x] `NoteParser.Parse(string source, NoteMarkers markers)` returns the notes (text, 1-based
      start and end line, character spans) and the **stripped source** with a mapping from
      stripped line numbers to original ones, per Technical details → "Note parsing"
- [x] tests: paired note on its own line, note spanning several lines, note mid-line with text
      before and after, several notes on one line, single-token mode running to end of line,
      notes inside a fenced code block and a table, line mapping after stripping
- [x] continuation lines of a multi-line note lose the indentation PlanCake writes in front of
      them (Technical details → "Note parsing"), so a note inside a nested list item reads back
      exactly as it was typed
- [x] tests: unterminated opening marker (treated as running to end of file, and reported),
      closing marker without an opening one (left as text), empty note, CRLF source,
      multi-line notes with list indentation read back without it; a hand-written
      `> [usernote]…[/usernote]` line inside a quote is removed entirely
- [x] validation commands pass

### Task 4: Render Markdown with source line ranges and notes

**Files:**
- Create: `src/PlanCake/Rendering/MarkdownRenderer.cs`, `src/PlanCake/Rendering/RenderResult.cs`,
  `src/PlanCake/Rendering/BlockInfo.cs`, `src/PlanCake/Rendering/RenderStrings.cs`,
  `src/PlanCake/Utils/Enums/NoteStyle.cs`, `tests/PlanCake.Tests/MarkdownRendererTests.cs`
- Modify: `src/PlanCake/PlanCake.csproj` (Markdig)

- [x] pipeline: `UseAdvancedExtensions()` (pipe tables, task lists, auto-identifiers, …) +
      `UsePreciseSourceLocation()`; parse the stripped source from `NoteParser`
- [x] walk the AST and stamp each annotatable block with `data-lines="start-end"` in
      **original** line numbers, per Technical details → "Annotatable blocks"; collect a
      `BlockInfo` per block (range, kind, full plain text, and an 80-character excerpt of it)
- [x] the renderer never calls `_()`: the localized strings it writes ("Note:", "user note",
      "unote") come in a `RenderStrings` record, so its tests do not touch `Localization`;
      `NoteStyle` (`Note` | `Button`) is an enum in `Utils/Enums/`
- [x] anchor every note to its block and insert its HTML into the AST where Technical details →
      "Note placement in the view" says; two modes: `Interactive` (a user note or a button, per
      the note style) and `Export`
      (static `role="note"` element), all note text HTML-encoded
- [x] every block gets `dir="auto"` and no `lang` of its own (the document language is set once
      on the container, see Task 6); task-list checkboxes stay as Markdig renders them
      (disabled) until Task 7a enables them
- [x] raw HTML in the source is rendered (so `<details>` or `<kbd>` work); scripts are blocked
      by the page's content security policy (Task 6); `Export` mode puts a policy fit for a
      standalone file in a `<meta http-equiv="Content-Security-Policy">`: `default-src
      'none'; script-src 'none'; style-src 'unsafe-inline'; img-src * data:`
- [x] tests: line ranges for a paragraph, heading, tight and loose list items (a parent item's
      range excludes its nested list), a list item whose first child is a code block, table
      rows (`TableRow.Line` and `Span` are populated for pipe tables), fenced and indented code,
      a paragraph in a blockquote; ranges correct when notes sit above, inside and below
- [x] tests: notes land after the right block for each kind, including a table row, a
      nested list item, a note before the first block and an unterminated note; note text with
      `<`, `&` and quotes is encoded; excerpt is plain text truncated to 80 characters with an
      ellipsis; export output contains the CSP meta and a raw `<script>` from the source stays
      under it
- [x] validation commands pass

### Task 5: Write notes to the file safely

**Files:**
- Create: `src/PlanCake/Notes/MarkdownFile.cs`, `src/PlanCake/Notes/NoteStore.cs`,
  `src/PlanCake/Notes/StaleFileException.cs`, `tests/PlanCake.Tests/NoteStoreTests.cs`,
  `tests/PlanCake.Tests/MarkdownFileTests.cs`

- [x] `MarkdownFile`: reads a file detecting the encoding (UTF-8 with or without BOM, UTF-16
      BOMs; otherwise strict UTF-8 with `throwOnInvalidBytes: true`) and the dominant line
      ending; a file that is not valid in its detected encoding (say, Windows-1251) is decoded
      with the Windows ANSI code page (`Encoding.RegisterProvider(CodePagesEncodingProvider.
      Instance)`, then `Encoding.GetEncoding(0)`; never `CurrentCulture`, which PlanCake sets to
      the interface language), injectable so tests pass Windows-1251 explicitly whatever the
      machine's code page, and then either opens read-only with a warning (default)
      and is never written, so PlanCake cannot damage it, or, when the Advanced setting
      `ConvertToUtf8` is on (hard-coded off until Task 12), is converted: written back once as
      UTF-8 without BOM, same line endings, and announced ("Converted from Windows-1251 to
      UTF-8"); writes back with the same encoding,
      BOM and line ending, atomically (temp file in the same folder + `File.Replace`, or
      `File.Move` when the target is gone); retries a locked file 5 times over about a second,
      then throws `IOException`
- [x] `NoteStore` operations, each taking the text the caller last rendered and throwing
      `StaleFileException` without writing if the file differs from it: `Add(afterBlock,
      text)`, `Edit(note, text)`, `Delete(note)`, `Clear()`; placement and indentation per
      Technical details → "Note placement in the file"
- [x] note text validation: rejects text containing the closing marker (paired mode) or a line
      break or the opening token (single-token mode, where it would split the note in two
      when read back); converts pasted line breaks to spaces in single-token mode
- [x] undo/redo: each operation records the file text before and after; `Undo` writes the
      "before" text only if the file still equals the "after" text (otherwise
      `StaleFileException` and both stacks are cleared); `Redo` symmetric
- [x] tests: placement after a paragraph, a heading, a nested list item (indent), a table row, a
      fenced code block's closing fence, a paragraph inside a blockquote (no `>` written, and
      the note still anchors to that paragraph); two notes on one block stack in order;
      multi-line note; edit; delete; clear
- [x] tests: stale file rejected with nothing written; CRLF and LF preserved; BOM preserved and
      not added; a Windows-1251 file opens read-only and every write is refused; with
      `ConvertToUtf8` on, the same file is rewritten as UTF-8 without BOM, text and line
      endings intact; closing marker in text rejected; locked file retried then failing; undo,
      redo, and undo refused after an external change
- [x] tests: round trip (add, parse, edit, parse) of a multi-line note inside a nested list item
      leaves exactly one indentation per line
- [x] validation commands pass

### Task 6: Show the document and talk to the page

**Files:**
- Create: `src/PlanCake/web/index.html`, `src/PlanCake/web/app.js`, `src/PlanCake/web/app.css`,
  `src/PlanCake/Ui/PageMessages.cs`, `src/PlanCake/Rendering/PositionRestorer.cs`,
  `src/PlanCake/Utils/LinkResolver.cs`, `tests/PlanCake.Tests/PositionRestorerTests.cs`,
  `tests/PlanCake.Tests/LinkResolverTests.cs`
- Modify: `src/PlanCake/Ui/DocumentView.cs`, `src/PlanCake/Ui/MainWindow.cs`,
  `src/PlanCake/Program.cs`
- Delete: `src/PlanCake/web/spike.html`

- [ ] `index.html` + `app.js`: a `main` element that receives rendered HTML; the message
      protocol in Technical details → "Page protocol", including the `strings` message for
      every page string; a delegated `click` listener and a `contextmenu` listener on the
      document (Task 2 kept both)
- [ ] no forms mode on Enter: the Task 2 spike found that Enter on a block with a permanent
      `tabindex="-1"` switches JAWS to forms mode. Blocks and notes carry no `tabindex` by
      default; when the page must move the virtual cursor (`focusLines`, `focusNote`, a new
      note), it sets `tabindex="-1"` on that one element, focuses it, and removes the attribute
      on `blur`. Belt and braces: a `keydown` Enter on a block or note element itself (not on a
      link, button or form control inside it; JAWS in forms mode, or the element has real
      focus) is handled like a click, with `preventDefault()`, so a second Enter never does
      nothing
- [ ] languages: `<html lang>` and `dir` follow the **interface** language (page chrome);
      `main` gets `lang` from the **document** language (hard-coded `en` until Task 12), sent
      with every `render`
- [ ] `index.html` carries a strict content security policy: `default-src 'none'; script-src
      https://app.plancake; style-src https://app.plancake 'unsafe-inline'; img-src
      https://app.plancake data:` (no inline script, no `eval`), so raw HTML in a plan renders
      but cannot run script
- [ ] `app.css`: readable defaults, visible focus outline, notes distinct from text,
      supports Windows high contrast (`forced-colors`) and light/dark (`prefers-color-scheme`)
- [ ] `Program.Main()` becomes `Main(string[] args)`; for now the first argument, if any, is
      the file to open (Task 13 replaces this with System.CommandLine). Every way of opening a
      file (command line, File → Open, drag and drop, `.md` links, and later the clipboard
      and links in Task 11) goes through one `MainWindow.OpenFile(path)`. It reads it
      with `MarkdownFile`, renders, posts to the page, sets the window title to
      `<file name> - PlanCake`; the page's `<title>` is the first heading
- [ ] navigation lockdown: after the initial load of `index.html`, `NavigationStarting` cancels
      **every** navigation, and `NewWindowRequested` every new window. In-page anchors (`#…`)
      are handled by `app.js` (scroll and focus, no navigation). `app.js` intercepts link
      clicks and sends the host the raw `href`: absolute `http(s)`/`mailto` open with
      `UseShellExecute`; a relative target resolves against the `.md` file's folder, and a
      `.md`/`.markdown` target opens in PlanCake, anything else with `UseShellExecute`; missing
      targets are announced. The decision (external, in-page, relative `.md`, relative other,
      missing) is a pure `LinkResolver`, tested for each case
- [ ] position restore, host side so it can be tested: the page reports only the lines of the
      last block the user interacted with (`position`); the host looks up that block's full
      text in the previous render's `BlockInfo` list, and after a re-render
      `PositionRestorer` picks the target from the new list (a block with identical full
      text, else the block whose start line is nearest) and sends `focusLines`; a new note
      is focused by index instead
- [ ] tests for `PositionRestorer`: same text found after lines shifted, text changed so the
      nearest line wins, block deleted at the end of the file, empty document
- [ ] mouse, alongside the keyboard: ⚠️ the Task 2 spike found that a JAWS Enter and a mouse
      click cannot be told apart (JAWS emulates a mouse click, `pointerType` is `mouse` for
      both), so the page does not try: Enter and a single click both activate (on a block: add
      a note; on a note: edit it), a double-click does nothing extra, and a click that ends a
      text selection (`getSelection()` not collapsed) does not activate, so text can still be
      selected with the mouse; a right-click opens the same context menu as the Applications
      key
- [ ] a Markdown file dragged from Explorer onto the window opens (`AllowDrop`, first `.md` /
      `.markdown` of the drop)
- [ ] zoom: Ctrl+Plus, Ctrl+Minus (main keyboard and numpad: `Oemplus`, `OemMinus`, `Add`,
      `Subtract`), Ctrl+0 set `ZoomFactor` in steps of 10%, 50%–300%
- [ ] **ask the user** to check with JAWS: an 800-line plan reads with H, I, L, T and B
      navigation, no Markdown punctuation is read, links open in the browser, zoom works,
      Enter on a block does not switch JAWS to forms mode
- [ ] validation commands pass

### Task 7: Add, edit and delete notes from the document

**Files:**
- Create: `src/PlanCake/Ui/NoteDialog.cs`, `src/PlanCake/Ui/NoteDialog.Designer.cs`,
  `src/PlanCake/Notes/NoteActionRunner.cs`, `tests/PlanCake.Tests/NoteActionRunnerTests.cs`
- Modify: `src/PlanCake/Ui/MainWindow.cs`, `src/PlanCake/web/app.js`

- [ ] `NoteDialog`: title "Add note" or "Edit note"; a read-only label "Note on:" with the block
      excerpt; a multiline, word-wrapping note `TextBox` with a real `Label`; OK and Cancel;
      Enter saves and Ctrl+Enter inserts a line break, or the reverse, per the setting (Task 12;
      hard-code the default until then); OK disabled while the text is blank; a rejected text
      (closing marker) shows why and keeps the dialog open
- [ ] Enter on a block → `NoteDialog` → `NoteStore.Add` → re-render → focus the new note
      → announce "Note added"; Enter on a note → edit (the note style is `Note` by default,
      hard-coded until Task 12)
- [ ] Applications key / Shift+F10 on a block or note → `NativeContextMenu.Show` at the
      element's screen position (page sends its client rectangle in CSS pixels; multiply by
      `devicePixelRatio` only, which already includes the zoom, then `webView.PointToScreen`):
      Add note, Edit note, Delete note (only on a note), Copy block text (the block's full
      plain text from `BlockInfo`)
- [ ] note deletion asks for confirmation (the `ConfirmNoteDelete` default, hard-coded until
      Task 12)
- [ ] the outcome of each note action lives in a small UI-free `NoteActionRunner` (success →
      announcement text and focus target; `StaleFileException` → nothing written, re-render,
      "The file changed. Please try again.", dialog text kept for the retry; `IOException` →
      error message, dialog text kept; read-only file → refused with the reason), with tests
      for each outcome in `tests/PlanCake.Tests/NoteActionRunnerTests.cs`
- [ ] Undo (Ctrl+Z) and Redo (Ctrl+Y) through `NoteStore`, announced ("Note added undone" etc.)
- [ ] **ask the user** to check with JAWS: add, edit, delete, undo and redo a note on a
      paragraph, a nested list item, a table row and a code block; the virtual cursor lands on
      the new note each time; Enter on a block does not switch JAWS to forms mode, and Enter on
      a note opens the editor every time, not only the first
- [ ] validation commands pass

### ➕ Task 7a: Toggle task-list items

Added after the Task 2 spike: task-list checkboxes render disabled, and the user wants to
check tasks off from the document.

**Files:**
- Create: `src/PlanCake/Notes/TaskToggle.cs`, `tests/PlanCake.Tests/TaskToggleTests.cs`
- Modify: `src/PlanCake/Rendering/MarkdownRenderer.cs`, `src/PlanCake/Notes/NoteStore.cs`,
  `src/PlanCake/Notes/NoteActionRunner.cs`, `src/PlanCake/Ui/MainWindow.cs`,
  `src/PlanCake/web/app.js`, `tests/PlanCake.Tests/NoteStoreTests.cs`

- [ ] the renderer's `Interactive` mode renders task-list checkboxes enabled (no `disabled`);
      `Export` mode keeps them disabled
- [ ] toggling a checkbox (Space, Enter or a mouse click) sends the host `toggleTask`
      `{ lines, checked, generation }` (Technical details → "Page protocol"), `lines` being the
      item's `data-lines`; the host ignores it when the generation is stale, like every other
      page message
- [ ] when `ConfirmTaskToggle` is on (default true, hard-coded until Task 12) the host asks
      first, deferred with `BeginInvoke`; the question says plainly that the file on disk will
      be changed. Cancel sends `taskState` `{ lines, checked }` back with the file's state, so
      the checkbox reverts
- [ ] `TaskToggle.SetChecked(source, line, isChecked)`: a pure function that rewrites the task
      marker (`[ ]`, `[x]` or `[X]`) of the list item starting on that original line, leaving
      everything else on the line, the line endings and every other line untouched; it throws
      when the line holds no task marker
- [ ] `NoteStore.ToggleTask(renderedText, line, isChecked)` writes it through the same stale-safe
      path as the note operations (Task 5): `StaleFileException` → nothing written, re-render,
      "The file changed. Please try again."; `IOException` and read-only files handled as in
      `NoteActionRunner`. On success: re-render, focus stays on that checkbox (the item's
      lines), announce "Task checked" or "Task unchecked"
- [ ] Undo and Redo cover toggles exactly like note actions (same stacks, announced "Task
      checked undone" and so on)
- [ ] tests: check an unchecked item, uncheck a checked one, uncheck `[X]` (capital), a nested
      item, an item with a note right after it (the note is untouched), a line without a task
      marker rejected, a stale file rejected with nothing written, undo and redo of a toggle
- [ ] **ask the user** to check with JAWS: Space toggles a task in the virtual cursor, the
      confirmation reads well, cancel leaves the checkbox as it was, focus stays on the
      checkbox, undo and redo work
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
- [ ] F9 / Shift+F9 move to the next / previous note from wherever focus is: in the document
      they focus the note after / before the current position; in the list they move
      the selection; at the ends, announce "No more notes"
- [ ] the list keeps its selection across re-renders when the same note still exists
- [ ] **ask the user** to check the list, F6 and F9 with JAWS
- [ ] validation commands pass

### Task 9: Menu bar and the small dialogs

**Files:**
- Create: `src/PlanCake/Ui/ShortcutsDialog.cs` (+ `.Designer.cs`), `src/PlanCake/Ui/AboutDialog.cs`
  (+ `.Designer.cs`), `src/PlanCake/Utils/LanguageList.cs`
- Modify: `src/PlanCake/Ui/MainWindow.cs`

- [ ] `BuildMenuSpec()` with exactly the menus in Technical details → "Menus", shortcuts
      registered through the host command table so they work with focus in the document too;
      items that need a file or a note are disabled without one; an item whose feature comes
      in a later task (Open from clipboard and Open from link: Task 11; Settings: Task 12;
      Export notes: Task 13; Check for updates: Task 14; User manual: Task 16) is added by
      that task, not stubbed here; a separator left doubled, leading or trailing by a missing
      item is dropped
- [ ] View → Interface language: System default plus every shipped language, current one
      checked, built by a new `Utils/LanguageList.cs` (the `locale\<code>\` scan with native
      names that SIC's Settings dialog does; Task 12's dialog reuses it); choosing one saves
      it to `Config` and applies it live (`ApplyLocalization`, as in SIC, plus a re-render so
      the notes' localized role descriptions follow). View → Document language:
      the six languages, current one checked; choosing one re-renders with the new `lang` for
      this document only (opening another file returns to the default from Settings).
      Defaults: the interface language is `System`, falling back to English when the Windows
      language is not one of the six; the document language is English and never follows the
      system (plans are usually written in English, and a Russian Windows must not make JAWS
      read them with a Russian voice)
- [ ] File → Open (`OpenFileDialog`, filter `*.md;*.markdown`, then all files), Open in editor
      (`UseShellExecute` on the file), Exit; View → Reload
- [ ] `ShortcutsDialog`: a `NativeListView` with "Command" and "Shortcut" columns built from the
      same command table (so it cannot drift), plus the in-document keys (Enter, Applications
      key, Space on a task checkbox); a Close button. It is the one place that lists every shortcut, including the
      in-document keys no menu shows
- [ ] `AboutDialog` modeled on SIC's: product, version, copyright, repository link, "Copy info"
- [ ] Delete all notes asks for confirmation (always), then `NoteStore.Clear`
- [ ] validation commands pass

### Task 10: Follow changes on disk, and one window per file

**Files:**
- Create: `src/PlanCake/Utils/FileWatcher.cs`, `src/PlanCake/Utils/SingleInstance.cs`,
  `tests/PlanCake.Tests/FileWatcherTests.cs`, `tests/PlanCake.Tests/SingleInstanceTests.cs`
- Modify: `src/PlanCake/Ui/MainWindow.cs`, `src/PlanCake/Program.cs`

- [ ] `FileWatcher`: watches the open file's folder for changes, renames and deletions of that
      file (editors often save by rename), debounced 300 ms, marshaled to the UI thread;
      ignores changes whose content equals what PlanCake itself last wrote
- [ ] external change → re-render with position restore and announce "File reloaded"; build the
      "ask first" branch too (No keeps the view, announces that F5 reloads, and note actions
      then fail as stale) but hard-code `AutoReload` until Task 12 wires the setting
- [ ] file deleted or renamed away → announce it, disable note commands and Reload until it
      reappears, then reload
- [ ] `SingleInstance`: per normalized full path, a named pipe `PlanCake-<SHA-256 of the
      upper-cased path>`; a second `plancake same.md` connects, calls
      `AllowSetForegroundWindow`, sends "activate", exits with `ExitCode.Success`; the first
      window restores and activates itself; `MainWindow.OpenFile` (Task 6) re-registers under
      the new path, so every way of opening a file is covered
- [ ] `FileWatcher` takes an injectable clock/timer so its logic is testable without real
      waits
- [ ] tests: bursts of events inside 300 ms produce one reload; PlanCake's own write produces
      none; save-by-rename (write temp, delete original, rename temp) produces one reload and
      no "deleted" state; a real delete produces the deleted state
- [ ] tests: path normalization (case, `..`, trailing separators) gives one pipe name; a second
      registration for the same path is detected
- [ ] validation commands pass

### ➕ Task 11: Open from the clipboard and from a link

**Files:**
- Create: `src/PlanCake/Ui/OpenLinkDialog.cs` (+ `.Designer.cs`), `src/PlanCake/Utils/UrlHelper.cs`,
  `src/PlanCake/Services/MarkdownDownloader.cs`, `src/PlanCake/Utils/ClipboardClassifier.cs`,
  `tests/PlanCake.Tests/UrlHelperTests.cs`, `tests/PlanCake.Tests/MarkdownDownloaderTests.cs`,
  `tests/PlanCake.Tests/ClipboardClassifierTests.cs`
- Modify: `src/PlanCake/Ui/MainWindow.cs`

- [ ] File → Open from clipboard (Ctrl+V, ported from SIC's paste handling): a file drop list
      (files copied in Explorer) opens its first `.md` / `.markdown` file; text holding a local
      path to such a file opens it (surrounding quotes, as Explorer's "Copy as path" adds,
      are stripped); text holding an http(s) link opens it as below; anything else is
      announced ("The clipboard holds no Markdown file or link"). The decision is a pure
      `ClipboardClassifier` over the clipboard's contents (drop list, text), so it is tested
      without a real clipboard
- [ ] port SIC's `UrlHelper.IsValidHttpUrl`; File → Open from link… (Ctrl+L) opens
      `OpenLinkDialog`, modeled on SIC's `AddUrlDialog` (a real `Label`, a URL `TextBox`
      pre-filled from the clipboard when it holds a link, OK/Cancel, title set in the
      constructor)
- [ ] `MarkdownDownloader`: rewrites a GitHub `github.com/<owner>/<repo>/blob/<ref>/<path>` link
      to its `raw.githubusercontent.com` form; downloads with `HttpClient` (30-second timeout,
      10 MB limit); refuses a non-success HTTP status (announcing it) and an HTML page ("This
      link leads to a web page, not a Markdown file"); names the file after the last URL
      segment, adding `.md` when it has no extension; saves it to the user's Downloads folder
      (`SHGetKnownFolderPath(FOLDERID_Downloads)`, which may be relocated) under that name, adding
      ` (2)`, ` (3)`… when taken, as browsers do; the window then opens that local copy, and
      notes go into it
- [ ] tests: `UrlHelper` accepts http(s) and rejects other schemes and junk; GitHub blob links
      are rewritten and other links left alone; a 404 is refused; an HTML response is refused;
      an oversized response is refused; a URL without an extension saves as `.md`; a taken file name gets ` (2)` (with an injectable `HttpMessageHandler`
      and a temp Downloads folder)
- [ ] tests for `ClipboardClassifier`: a drop list with and without a Markdown file, a quoted
      and an unquoted path, a link, other text, an empty clipboard
- [ ] validation commands pass

### Task 12: Settings

**Files:**
- Create: `src/PlanCake/Ui/SettingsDialog.cs` (+ `.Designer.cs`), `src/PlanCake/Utils/Enums/ExternalChangeAction.cs`,
  `src/PlanCake/Utils/Enums/BlockEnterAction.cs`, `src/PlanCake/Utils/Enums/NoteEnterAction.cs`
- Modify: `src/PlanCake/Utils/Config.cs`, `src/PlanCake/Ui/MainWindow.cs`,
  `src/PlanCake/Ui/NoteDialog.cs`, `tests/PlanCake.Tests/ConfigTests.cs`

- [ ] `Config` sections and defaults per Technical details → "Settings"; drop the template's
      `ConfirmExit` (nothing is ever unsaved)
- [ ] `SettingsDialog` like SIC's: a `TabControl` with General, Notes and Advanced tabs, each a flat
      `TableLayoutPanel`, real `Label`s, OK/Cancel; interface language list from
      `LanguageList` (Task 9); default document language a combo box of the six languages (native names);
      `ConfirmNoteDelete` and `ConfirmTaskToggle` as check boxes on the General tab;
      markers validated with `NoteMarkers.Validate` and the reason shown next to the field
- [ ] File → Settings (Ctrl+comma); on OK: save, then apply live
- [ ] wire every setting to its behavior, replacing the hard-coded defaults of earlier tasks:
      `Language` → `ApplyLocalization` (menu, list columns, page `strings`, and a re-render
      with new `RenderStrings`) and the View →
      Interface language check mark; `DefaultDocumentLanguage` → the `lang` of newly opened
      documents; `ConfirmNoteDelete` → the confirmation in Task 7 (Delete all notes always
      confirms); `ConfirmTaskToggle` → the confirmation in Task 7a; `ExternalChangeAction` → Task 10's reload-or-ask branch; `ShowNotesList` →
      the list's visibility at startup; `OpeningMarker`/`ClosingMarker` → `NoteParser`,
      `NoteStore` and a re-render; `NoteStyle` → the renderer and a re-render; `BlockEnterAction` → the page (Enter opens the context menu
      instead of the dialog); `NoteEnterAction` → `NoteDialog`; `ConvertToUtf8` →
      `MarkdownFile` (Task 5)
- [ ] tests: defaults, round trip of every new setting, invalid enum, marker or document
      language values in the file fall back to defaults
- [ ] validation commands pass

### Task 13: Command-line mode

**Files:**
- Create: `src/PlanCake/Cli/CliRunner.cs`, `src/PlanCake/Cli/ConsoleAttacher.cs`,
  `src/PlanCake/Notes/NotesJson.cs`, `tests/PlanCake.Tests/CliRunnerTests.cs`
- Modify: `src/PlanCake/Program.cs`, `src/PlanCake/Utils/Constants/ExitCode.cs`,
  `src/PlanCake/PlanCake.csproj` (System.CommandLine)

- [ ] System.CommandLine root command with an optional `file` argument (→ GUI, via
      `SingleInstance`) and the subcommands `list`, `check`, `clear`, `export` with the options,
      output and exit codes in Technical details → "Command line"
- [ ] `ConsoleAttacher`: for subcommands, `--help`, `--version` and parse errors only; calls
      `AttachConsole(ATTACH_PARENT_PROCESS)` **only when stdout is not already redirected**
      (`GetStdHandle` + `GetFileType`), so captured output from Claude's tools or a pipe is
      never taken over; only after a successful attach does it set `Console.OutputEncoding`
      to UTF-8 (the setter fails without a console); when output is redirected, `CliRunner`
      and System.CommandLine's invocation output get `new StreamWriter(Console.
      OpenStandardOutput(), new UTF8Encoding(false))` (and the same for stderr); the GUI path
      never touches the console
- [ ] `NotesJson` (in `src/PlanCake/Notes/`) builds the JSON that `list --json` prints, and
      File → Export notes… (a `SaveFileDialog`, default name `<plan name>.notes.json`) writes
      the same JSON from the window, so both stay identical
- [ ] `CliRunner` takes `TextWriter`s for output and error so tests need no console; markers
      and the document language come from `Config` unless overridden by options
- [ ] tests: `list` text and JSON on a file with notes of each kind; a Cyrillic and a Hebrew note
      survive `list --json` byte for byte as UTF-8; `list --json -o` writes the same bytes to the
      file; `list` on a file without notes; `check` 0
      and 3; `clear` removes all notes and leaves the rest byte-identical; `export` writes
      standalone HTML with notes as `role="note"` elements, the CSP meta, and `lang` from the document
      language (not the interface language); `--single-token` switches the marker mode
- [ ] tests: missing file, unreadable file, invalid marker options → message on stderr and
      `ExitCode.Error`
- [ ] validation commands pass

### Task 14: Updates

**Files:**
- Create: `src/PlanCake/Services/UpdateService.cs`, `src/PlanCake/Utils/Enums/UpdateCheckInterval.cs`
- Modify: `src/PlanCake/Utils/Constants/App.cs`, `src/PlanCake/Utils/Config.cs`,
  `src/PlanCake/Ui/SettingsDialog.cs`, `src/PlanCake/Ui/MainWindow.cs`, `.gitignore`

- [ ] port SIC's `UpdateService` and `UpdateCheckInterval`; `App.AppcastUrl` =
      `https://plancake.oire.dev/appcast.xml`
- [ ] **stop and ask the user** to generate the key pair in the repository root, giving them
      the commands to paste: `dotnet tool install --global
      NetSparkleUpdater.Tools.AppCastGenerator` (once per machine), then
      `netsparkle-generate-appcast --generate-keys --key-path keys`; then set
      `App.UpdatePublicKey` from `keys/NetSparkle_Ed25519.pub`
- [ ] settings: check for updates on startup, background interval (General tab); Help → Check
      for updates
- [ ] `keys/` ignored in git as in SIC
- [ ] validation commands pass

### Task 15: Translations

**Files:**
- Create: `src/PlanCake/locale/{ru,uk,fr,he,de}/PlanCake.po`
- Modify: `src/PlanCake/locale/messages.pot`, `src/PlanCake/PlanCake.csproj`,
  `src/PlanCake/web/app.js`

- [ ] check that no user-visible literal crept into `app.js` or `index.html` (all page strings
      come through the `strings` message since Task 6)
- [ ] `Extract-Strings.ps1`, then `New-Language.ps1` for ru, uk, fr, he, de, then translate every
      entry; no fuzzy entries; menu mnemonics unique per menu level in every catalog (run the
      app once in each language to prove it)
- [ ] `SatelliteResourceLanguages` = `en;ru;uk;fr;he;de`
- [ ] Hebrew: every form calls `TextDirection.Apply` after `Localizer.Localize`, every message
      box goes through `DialogHelper`, the page gets `dir="rtl"` on its chrome while blocks keep
      `dir="auto"`
- [ ] test: each shipped catalog compiles and loads (extend `LocalizationTests`)
- [ ] validation commands pass

### Task 16: User manual

**Files:**
- Create: `src/PlanCake/help/{en,ru,uk,fr,he,de}/manual.html`
- Modify: `src/PlanCake/PlanCake.csproj`, `src/PlanCake/Ui/MainWindow.cs`

- [ ] Help → User manual (F1) opens `help\<culture>\manual.html` with SIC's fallback chain;
      `help\**` copied to output
- [ ] write the manual with the `write-manual` skill (English first, then the five
      translations): reading with JAWS, adding and editing notes, the notes list, settings
      (including what changing markers does to existing notes), opening from the clipboard
      and from a link, the mouse alongside the keyboard, the command line, the `pk`
      alias, and how PlanCake fits the plan-make manual-review step
- [ ] validation commands pass

### Task 17: Installer, portable zip, winget

**Files:**
- Create: `installer/plancake.iss`, `installer/CodeDependencies.iss`,
  `installer/Languages/Custom.{en,ru,uk,fr,he,de}.isl`, `installer/Build-Installer.ps1`,
  `installer/build-installer.bat`, `installer/deploy.example.json`, `changelogs/1.0.0.md`
- Modify: `.gitignore`

- [ ] `plancake.iss` from SIC's `sic.iss`: PlanCake names, the six languages, the exe,
      `WebView2Loader.dll`, `web\*`, `help\*`, `locale\*`; `Dependency_AddDotNet100Desktop`
      **and** `Dependency_AddWebView2`; the portable zip carries the same files
- [ ] add `{app}` to the machine `PATH` (`[Registry]` on
      `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment`, `ChangesEnvironment=yes`,
      skipped when already present, removed on uninstall)
- [ ] `Build-Installer.ps1` from SIC's (publish, ISCC, portable zip with an empty `userdata\`,
      appcast with `netsparkle-generate-appcast`, optional deploy via `deploy.json`); `deploy.json`
      and `installer/Output/` ignored in git
- [ ] document the `wingetcreate` command for `Oire.PlanCake` in `CLAUDE.md` (manifests are
      created at the first release, not now)
- [ ] validation commands pass

### Task 18: Update documentation

**Files:**
- Modify: `README.md`, `CLAUDE.md`, `CHANGELOG.md`, `changelogs/1.0.0.md`

- [ ] README: what PlanCake is, installing, reading and annotating, the command line with
      examples, the `pk` alias (`Set-Alias pk plancake` in the PowerShell profile,
      `alias pk=plancake` in bash), how to point Debussy's `noteMarkers` at PlanCake's markers
- [ ] CLAUDE.md: architecture (Notes, Rendering, Ui, Cli, Services, `web/`), the page protocol,
      the WebView2 gotchas (user data folder, accelerator keys, single-file native loader,
      `BeginInvoke` before any dialog or menu started from a WebView2 event),
      the JAWS spike results, the three pieces of a language, the AttachConsole caveat
- [ ] CHANGELOG.md and `changelogs/1.0.0.md`: the 1.0.0 feature list

## Technical details

### Note parsing

- Paired mode: a note starts at any occurrence of `Opening` and ends at the next `Closing`;
  its text is everything between, trimmed, internal line breaks kept. Single-token mode: from
  `Opening` to the end of that line. Matching is ordinal and case-sensitive.
- Stripping: the note's characters (markers included) are removed. A line left empty or
  holding only whitespace (and `>` characters, for notes written by hand inside a quote) by
  the removal is removed entirely; a line with
  other text left on it stays (with the note cut out). The stripped source therefore has the
  same Markdown structure as the file without any notes.
- The line map records, for every stripped line, its original 1-based line number, so a block
  at stripped lines 40–42 reports original lines 41–44 when a note sat inside it.
- An opening marker with no closing marker runs to the end of the file and is flagged
  `Unterminated`; the GUI announces it, `list` prints a warning to stderr. It is anchored like
  any other note, by its start line.
- Continuation lines: for the second and later lines of a multi-line note, the parser removes
  the indentation PlanCake writes (leading whitespace, up to the length of the first line's
  indentation) before keeping the text, so reading back what
  "Note placement in the file" wrote gives exactly the typed text.

### Annotatable blocks

The innermost block the user can land on carries `data-lines="start-end"` (original, 1-based,
inclusive) and no `tabindex`: a permanent one makes JAWS switch to forms mode on Enter (Task 2
spike), so the page adds `tabindex="-1"` only while it moves focus to a block (Task 6):
- `ParagraphBlock` (but see list items), `HeadingBlock`, `FencedCodeBlock` and `CodeBlock`
  (range includes both fences), `TableRow`; `ThematicBreakBlock` is not annotatable;
- `ListItemBlock`: stamped with the range of its **leading paragraph only**, because in a tight
  list Markdig renders the paragraph without a `<p>` and its attributes would be lost; nested
  lists inside the item get their own stamps. In a loose list the `<p>` is stamped too, with
  the same range. An item whose first child is not a paragraph (a code block, a heading) is
  not stamped itself; that first child is stamped as usual;
- blocks inside a `QuoteBlock` are stamped; the quote itself is not.

The end line comes from the block's `Span.End` mapped to a line through a table of line-start
offsets of the stripped source, then through the line map.

### Note placement in the view

A note is anchored to the last annotatable block that ends before the note starts (for a note
written by hand in the middle of a block: the block containing its line). A note before the
first block has no anchor: it is rendered at the top of the document, and `list` reports its
block as `0-0` with an empty excerpt and `blockKind` `"start"` (the other values:
`"paragraph"`, `"heading"`, `"listItem"`, `"code"`, `"tableRow"`). The renderer inserts an
`HtmlBlock` right after the anchor in the anchor's parent container; for a `TableRow` anchor it
appends the `HtmlBlock` to the row's last cell (Markdig then wraps that cell's text in `<p>`,
which is harmless); for a list item's leading paragraph, inside the item after that paragraph.

- Interactive, note style `Note` (default): `<div class="note" role="note"
  aria-roledescription="user note" aria-brailleroledescription="unote"
  data-note="<index>"><text></div>`, with no `tabindex` of its own (like blocks, it gets one
  only while the page focuses it); Enter or a click on it edits it (same click and `keydown`
  Enter delegation as blocks); F9 / Shift+F9 move between notes.
- Interactive, note style `Button`: `<button type="button" class="note" data-note="<index>">Note:
  <text></button>`; B / Shift+B also move between notes.
- Both: "user note", "unote" and "Note:" localized; line breaks as `<br>`.
- Export: `<div class="note" role="note" aria-roledescription="user note"
  aria-brailleroledescription="unote"><text></div>`, both role descriptions localized (from
  the catalog; in the CLI, the interface language from `Config`). Not `<aside>`: its implicit
  `complementary` role would make every note a landmark.

### Note placement in the file

- The note goes on new line(s) directly after the anchor block's last original line, **after
  any notes already anchored there**, so notes stack in the order added.
- Prefix: indentation only, never `>`. For a list item, spaces up to the item's content
  column, so a reader of the raw file sees which item the note belongs to; for anything else,
  the anchor's own leading spaces. PlanCake takes notes out before parsing, so the prefix
  never affects rendering, and a `>` would only be one more thing to strip back out. Every
  line of a multi-line note gets the prefix.
- Written form: `<prefix>[usernote]text[/usernote]`; a multi-line note puts the opening marker
  before its first line and the closing marker after its last. Single-token mode:
  `<prefix>!USERNOTE! text`.
- No blank lines are added or removed.

### Page protocol

JSON messages through `chrome.webview.postMessage` / `PostWebMessageAsJson`, each with a `type`:
- host → page: `render` `{ html, generation, documentLang, focus: { lines?, note? } }`,
  `strings` `{ uiLang, uiDir, … }` (page chrome and live messages only: the note labels are
  already in the rendered HTML, so the page never adds its own "Note:"), `focusNote` `{ note }`, `focusLines`
  `{ lines }`, `nextNote` / `previousNote` `{}`, `taskState` `{ lines, checked }` (Task 7a:
  sets a task checkbox back after a canceled toggle). No `announce` message: the Task 2 spike
  showed that the host's UIA notifications are heard in the virtual buffer, so every
  announcement goes through `StatusAnnouncer`
- page → host: `activate` `{ lines, generation }` (Enter or a click on a block),
  `activateNote` `{ note, generation }`, `contextMenu` `{ lines, note?, rect, generation }`,
  `toggleTask` `{ lines, checked, generation }` (Task 7a), `position` `{ lines }` (last block
  interacted with), `openLink` `{ href }`, `noMoreNotes` `{}`, `ready` `{}`
- the host ignores any message whose `generation` is not the latest render

### Keyboard

Host shortcuts (must work with focus in the document and in the list): Ctrl+O, Ctrl+V, Ctrl+L,
Ctrl+E, Ctrl+comma, F5, F6, F9, Shift+F9, Ctrl+Z, Ctrl+Y, Ctrl+Plus, Ctrl+Minus (both also on
the numpad), Ctrl+0, F1,
Shift+F1, Alt+F4.
In the document: Enter on a block / note, Space (or Enter) on a task checkbox, Applications key
and Shift+F10. In the list: Enter, Delete, Applications key.

Note navigation is F9 / Shift+F9, not F8: the Task 2 spike found that JAWS takes F8 for its
extended-select mode, so F8 never reaches PlanCake.

### Menus

- **File:** Open… (Ctrl+O), Open from clipboard (Ctrl+V), Open from link… (Ctrl+L), separator,
  Open in editor (Ctrl+E), Export notes…, separator, Settings… (Ctrl+comma), separator,
  Exit (Alt+F4, display only)
- **Edit:** Undo (Ctrl+Z), Redo (Ctrl+Y), separator, Delete all notes…
- **View:** Notes list (checkable), Switch pane (F6), separator, Interface language ▸ (System
  default, then each shipped language by native name), Document language ▸ (English, Русский,
  Українська, Français, עברית, Deutsch), separator, Zoom in (Ctrl+Plus), Zoom out
  (Ctrl+Minus), Reset zoom (Ctrl+0), separator, Reload (F5). Language names carry no
  mnemonics; the two submenus' own mnemonics must differ from every other View item's.
- **Notes:** Edit note, Delete note, separator, Next note (F9), Previous note (Shift+F9)
- **Help:** User manual (F1), Keyboard shortcuts, separator, Check for updates, About PlanCake (Shift+F1)

No menu may end up with a single item. No Select all: in the document JAWS handles Ctrl+A
itself.

### Settings

`PlanCake.cfg`, SharpConfig sections:
- `[General]`: `Language` (interface, default `System`), `DefaultDocumentLanguage` (`en`; one
  of `en`, `ru`, `uk`, `fr`, `he`, `de`), `ConfirmNoteDelete` (true), `ConfirmTaskToggle`
  (true: ask before a task checkbox rewrites the file, Task 7a),
  `ExternalChangeAction` (`AutoReload` | `Ask`, default `AutoReload`), `ShowNotesList` (true),
  `CheckForUpdatesOnStartup` (true), `UpdateCheckInterval` (`Weekly`)
- `[Notes]`: `OpeningMarker` (`[usernote]`), `ClosingMarker` (`[/usernote]`, empty = single
  token), `NoteStyle` (`Note` | `Button`, default `Note`: how a note appears in the document),
  `BlockEnterAction` (`AddNote` | `ContextMenu`, default `AddNote`), `NoteEnterAction`
  (`Save` | `NewLine`, default `Save`; Ctrl+Enter does the other)
- `[Advanced]`: `ConvertToUtf8` (false): "Convert files that are not UTF-8 to UTF-8 (without
  BOM) when opening them"; the checkbox's label says plainly that the file on disk is
  rewritten

### Command line

`plancake [file]` opens the window. Subcommands all accept `--open-marker <text>`,
`--close-marker <text>` and `--single-token` (single-token mode; a flag, because Windows
PowerShell 5.1 drops an empty `""` argument when calling an exe). Output is UTF-8 without BOM.
- `list <file> [--json] [-o <out>]`: one note per entry; `-o` writes to a file (UTF-8 without
  BOM) instead of standard output. Text form, one line per note:
  `<noteStart>-<noteEnd> after <blockStart>-<blockEnd> "<excerpt>": <text>` (line breaks in the
  text shown as ` / `). JSON form: an array of `{ "noteStartLine", "noteEndLine",
  "blockStartLine", "blockEndLine", "blockKind", "blockExcerpt", "text" }`, UTF-8, indented.
  No notes: empty output (text) or `[]` (JSON). Exit 0.
- `check <file>`: prints the note count; exit `ExitCode.Success` (0) with none,
  `ExitCode.NotesRemain` (3, new constant) with any. `ExitCode.Error` (1) stays for failures, so
  a caller can tell "notes left" from "could not read".
- `clear <file>`: removes every note, prints how many; exit 0.
- `export <file> -o <out.html> [--lang <code>]`: standalone HTML (inline CSS, the CSP meta,
  no script of its own, `lang` from `--lang` or else `DefaultDocumentLanguage` — never the
  interface language, `<title>` from the first heading), notes as `role="note"` elements; exit 0.
- Errors: message on stderr, exit 1.

Caveat for the docs: interactive PowerShell and cmd do not wait for a GUI-subsystem exe, so
attached output can appear after the prompt returns. Piped or captured output (Claude's tools,
`| Out-Host`, bash) always waits and is always complete.

### JAWS spike page

`web/spike.html`, served from the app host, `lang="en"`: an `h1`, an `h2`, three paragraphs
(the second hard-wrapped over three source lines), a nested bullet list, a task list with one
checked and one unchecked item, a three-row table, a fenced code block, an external link, and a
`<button class="note">Note: sample note</button>` followed by the same note as a `role="note"`
`div` with its role descriptions (Technical details → "Note placement in the view"). Every block has `data-lines` and
`tabindex="-1"`. Script: a delegated `click` listener reporting `closest('[data-lines]')` plus
the event's `pointerType` and `detail` (to tell a JAWS Enter from a mouse click); a
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
6. Click the second paragraph once with the mouse (a sighted helper, or the JAWS mouse
   commands): does the reported `pointerType` / `detail` differ from what Enter produced?
7. The two sample notes: how does JAWS announce each (on arrival and in the braille display),
   does Enter on the `role="note"` one arrive as a click, and does B find the button one?
8. Each shortcut in Technical details → "Keyboard": does the host announce it (that is, does
   JAWS let it through)?

## Post-completion

- Full JAWS pass on a real plan of 800+ lines: read it end to end, annotate twenty blocks of
  every kind, let Claude act on them through manual review, confirm the reload keeps the place.
- Debussy (`C:\Users\User\.claude\plugins\marketplaces\Debussy`, `plugins/planning`):
  `noteMarkers` already accepts pairs, so the user sets `"noteMarkers": ["[usernote]...[/usernote]"]`
  in `~/.claude/debussy.json`, matching PlanCake's markers. If `!USERNOTE!` stays in that list
  for hand-written notes, remember that PlanCake reads only its own markers, so
  `plancake check` would not count those. Separate change there: plan-make's Manual review
  hand-off starts `plancake <plan>` when `plancake` is on the PATH, and manual review uses
  `plancake list --json` to find notes and `plancake check` for its verify step.
- Idea for later, needing work in both apps: a notes import in Notika
  (`C:\repos\accessmind\notika-windows`) that reads PlanCake's notes JSON. Notika has no
  import surface today (SQLCipher-encrypted SQLite store).
- Add `Set-Alias pk plancake` to the PowerShell profile (and `alias pk=plancake` to the bash
  profile).
- Set up `plancake.oire.dev` to host the appcast and downloads (`deploy.json` points
  `Build-Installer.ps1 -Deploy` at it), create the GitHub release and submit the winget
  manifest with `wingetcreate` at the first release.
