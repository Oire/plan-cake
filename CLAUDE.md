# PlanCake — Repo Conventions

PlanCake reads Markdown files (above all long implementation plans) as rendered HTML in
WebView2 and writes the user's notes on them straight into the `.md` file. It runs as a window,
or headless with a subcommand (`list`, `check`, `clear`, `export`). .NET 10, Windows x64 only.

The repository was created from Oire's `winforms-template`; the conventions below come from it
and stay binding. The implementation plan is `docs/plans/001-plan-cake-v1.md`.

## Accessibility is load-bearing

The primary maintainer is blind and uses JAWS. This is not a "nice to have" — it drives UI
decisions.

- **Layouts:** use `TableLayoutPanel` for form structure, not absolute positioning or
  `FlowLayoutPanel`.
- **Labels:** use real `Label` controls with their `Text` property. **Do not** rely on
  `AccessibleDescription` / `AccessibleName` as a substitute — screen readers associate a
  label with a control best through a visible `Label` next to it. The one exception is
  `NativeListView`: screen readers do not read a preceding label for a list view, so it is
  named through its own `AccessibleName` (the library's documented way; set it through a
  `NativeListView`-typed reference, and again after anything that walks `Controls`). Keep the
  visible label as well. The system proxy of a list view ignores the window text that
  property sets, so PlanCake also names the list window through MSAA annotation
  (`Utils/WindowAccessibleName.cs`); without it JAWS announces no name.
- **Keyboard alternatives:** every mouse interaction (drag, hover, context menu) must have a
  keyboard equivalent. No exceptions.
- **Confirmations close with Escape, meaning No.** A Yes/No `MessageBox` has no cancel, so
  Escape and the close button do nothing there: ask every yes-or-no question through
  `DialogHelper.Confirm` (a task dialog with Yes, No and `AllowCancel`), never
  `MessageBoxButtons.YesNo`.
- **Native controls:** prefer `Oire.WinForms.NativeControls` over the stock WinForms
  owner-drawn equivalents where it offers one — the native peers are what screen readers
  actually understand.
- **Test with a screen reader** before declaring any UI task done.

## Localization — GetText.NET, not .resx

`.po` / `.mo` catalogs live in `src/PlanCake/locale/`. **`.resx` files are forbidden**
— if the WinForms designer generates one, delete it.

- **`Localizer.Localize(this, Localization.Catalog)`** in a form's constructor — walks the
  control tree and translates designer-set text.
- **`_("literal")`** for strings built at run time. Import via
  `using static Oire.PlanCake.Utils.Localization;`. Plurals `_n`, context `_p`,
  both `_pn`.
- **`.mo` files are build output and are gitignored.** CI compiles them from the `.po`
  sources with `Compile-Translations.ps1 -Strict` before `dotnet build`. Skip that and the
  `locale\**\*.mo` copy glob has nothing to copy, and the app silently falls back to English.
- **No fuzzy entries** — `-Strict` fails the build on them, because `msgfmt` leaves a fuzzy
  entry out of the `.mo` and the app then shows English while looking translated.
- **Menu mnemonics must be unique per menu level in every catalog.** A duplicate `&` letter
  within one menu throws at startup, on that locale only.
- **Two languages, never derived from each other.** The *interface language* (menus, dialogs,
  the page's chrome and announcements) and the *document language* (the `lang` of the rendered
  plan, which picks the screen reader's voice, and a hint for legacy encodings) are separate
  settings with separate View menus. An English plan is read with the English voice whatever
  the interface language is.
- **Adding a language** touches more than the catalog, and each piece is easy to forget:
  `locale/<code>/PlanCake.po` (the interface list is built from the `locale\<code>\` folders
  holding a `.mo`, so the settings picker and View → Interface language follow by themselves);
  `<SatelliteResourceLanguages>` in the `.csproj`; `LanguageList.SupportedCodes` (the document
  languages and the menu order) and `LegacyEncoding.CodePageOf` (its legacy code page); the
  manual, `help/<code>/manual.html`, with a glossary in `help/glossaries/`; and the installer,
  `installer/Languages/Custom.<code>.isl` plus a `[Languages]` line in `plancake.iss`.

- The catalog is `PlanCake.po` / `.mo`, named after `App.Name`; the executable is `plancake.exe`
  (`AssemblyName`). The gettext scripts read `App.Name` from `Utils/Constants/App.cs`, not the
  `AssemblyName`.

See `src/PlanCake/locale/README.md` for the script workflow.

## Versioning

GitVersion owns every version number (`GitVersion.yml`). **Never write a `<Version>` literal
into a `.csproj`.** A release is a `vX.Y.Z` tag; the fourth field is the commit distance from
that tag. CI checks out with `fetch-depth: 0` because a shallow clone has no tag history.

## Code style

`.editorconfig` is authoritative and `dotnet format --verify-no-changes` runs in CI — run
`dotnet format` before pushing.

- File-scoped namespaces, namespaces follow folder names.
- Opening brace on the same line; `catch` / `else` / `finally` on the same line as the closing
  brace.
- `public class Foo: Bar` — no space before the colon, one after.
- Four-space indent, LF line endings (except `.bat` / `.cmd`), 120-column soft limit.
- `TreatWarningsAsErrors` with analyzers at `latest`. Suppress with a targeted `NoWarn` plus a
  comment explaining why, never by lowering the bar globally.

## Structure

Single project with folder/namespace separation:

```
src/PlanCake/          -- namespace Oire.PlanCake, builds plancake.exe
├── Notes/       -- note parsing, reading and writing files, task toggles, notes JSON
├── Rendering/   -- Markdig to HTML with source line ranges, notes placed after their blocks
├── Ui/          -- MainWindow, DocumentView (WebView2), the host command table, dialogs
├── Cli/         -- the headless subcommands and the console they write to
├── Services/    -- update checks (NetSparkle), downloading a Markdown file from a link
├── Utils/       -- Config, Localization, single instance, file watching, status announcer
│   ├── Constants/  -- App, Logging, ExitCode
│   └── Enums/      -- setting values
├── web/         -- the page WebView2 shows: index.html, app.js, morph.js, blocks.js, app.css
├── help/        -- the user manual, help/<code>/manual.html, and its translation glossaries
└── locale/      -- .po catalogs and gettext scripts
tests/PlanCake.Tests/  -- namespace Oire.PlanCake.Tests
installer/       -- Inno Setup script, Build-Installer.ps1
changelogs/      -- release notes per version, for the appcast
PlanCake.slnx
```

Do not split into more projects for organization's sake. Something genuinely shared across
applications belongs in a NuGet package instead.

`Utils/Constants/App.cs` is named `App`, not `Application`, on purpose: with implicit usings
and `UseWindowsForms`, an `Application` class in that namespace shadows
`System.Windows.Forms.Application` in every file that imports it.

## Architecture

The plan (`docs/plans/001-plan-cake-v1.md`, Technical details) has the full rules; this is the
map.

- **The file is the only store.** No Save command, no side file: a note is in the `.md` file the
  moment the user confirms it, and the notes JSON (`plancake list --json`, File → Export notes)
  is a copy PlanCake never reads back.
- **Notes/** is UI-free and shared by the window and the command line. `NoteParser` finds the
  notes by the configured markers (`NoteMarkers`: a pair, or a single token that runs to the end
  of its line) and strips them out, keeping a map from stripped lines to original ones.
  `NoteStore` adds, edits and deletes notes and toggles tasks (`TaskToggle`), with undo and
  redo; every operation takes the text the caller last rendered and throws
  `StaleFileException` without writing when the file on disk differs, so a note never lands on
  a change the user has not seen. `MarkdownFile` reads and decodes (File safety below) and
  writes through a temporary file and `File.Replace`.
- **Rendering/** renders the stripped source with Markdig (`UseAdvancedExtensions`). Every block
  the user can land on (paragraph, heading, a list item's leading paragraph, code block, table
  row) carries `data-lines="start-end"` in original lines, and each note is a `role="note"`
  element after its block. Line numbers are **1-based** wherever a user, a CLI consumer or
  `data-lines` sees them; Markdig's 0-based lines are converted at the boundary. The strings the
  renderer writes come in through `RenderStrings`, so it never touches the catalog.
  `PositionRestorer` picks the block to return to after a re-render.
- **Ui/** holds `MainWindow` (the menu, the notes list, every note action), `DocumentView` (the
  WebView2 control and its lockdown), `HostCommands` (the one table of commands and keys that
  the menu, both key paths and the Keyboard shortcuts dialog all read) and `PageMessages` (the
  page protocol's message shapes).
- **Cli/** is `CliRunner` (System.CommandLine 2.x: `list`, `check`, `clear`, `export`) and
  `ConsoleAttacher`. `list`, `check` and `export` never write the Markdown file; `clear` follows
  the conversion setting like the window. `list`'s text lines are data and stay in English;
  other messages follow the interface language. `ExitCode.NotesRemain` (3) is `check` finding
  notes, distinct from `ExitCode.Error` (1).
- **Services/** is `UpdateService` (Updates below) and `MarkdownDownloader` (File → Open from
  link: http(s) only, a GitHub file page turned into its raw file, 30 seconds, 10 MB, into the
  Downloads folder).
- **web/** is the page. `app.js` shows what the host renders, reports which block or note the
  user acts on, and moves focus when the host asks. `morph.js` updates the page in place when
  the same file is rendered again; `blocks.js` picks the block Alt+Shift+Down and Up move to.
  Both expose a pure function that the tests run in Jint (`MorphPlanTests`, `BlockPickTests`).
  **No user-visible string is written in the page**: all of them come from the host in the
  `strings` message, already translated.

### Page protocol

JSON messages through `chrome.webview.postMessage` / `PostWebMessageAsJson`, each with a `type`;
the full list is in the plan (Technical details → Page protocol) and the shapes in
`Ui/PageMessages.cs`. Host → page: `render` (HTML, document language, title, where to put the
focus), `strings`, `focusNote`, `focusLines`, `nextNote` / `previousNote`, `nextBlock` /
`previousBlock`, `taskState`. Page → host: `activate`, `activateNote`, `contextMenu`,
`toggleTask`, `position`, `openLink`, `noMoreNotes`, `noMoreBlocks`, `dropFiles`, `goBack`,
`ready`. Every render carries a `generation`; the host ignores a message from an older render
(`WhileCurrent` in `MainWindow`). There is no `announce` message: every announcement goes
through `StatusAnnouncer`.

A different file gets a freshly loaded page (the host navigates to `index.html` again and
renders once the page says `ready`). A re-render of the same file (an outside change, a reload,
a note action, undo) is patched in place by `morph.js`. Only a render the user asked for by
acting on a block carries focus; the rest leave the reader where they are.

### WebView2

- **User data folder** under `App.DataFolder\WebView2`: the install folder is not writable.
- **The page's own host.** `web\` is mapped to `https://app.plancake/`. The only navigation
  allowed is the host's own `Navigate` to a page there; every other navigation, frame navigation
  and new window is refused, and the CSP in `index.html` allows no script but the app's own.
  Links in a plan go to the host (`openLink`, `LinkResolver`), so raw HTML in a plan can neither
  take the view away nor run script. Browser context menus, accelerator keys and the status bar
  are off; dev tools are on in Debug only.
- **Accelerator keys.** Keys pressed while the page has focus never pass through the host's
  message loop, so the native menu bar's accelerators never see them. The WinForms control does
  not call `ProcessCmdKey` either: it raises its own `KeyDown` from the browser's
  `AcceleratorKeyPressed`, which `DocumentView.AcceleratorKeyDown` passes on. `MainWindow`
  sends that path and `ProcessCmdKey` through `HostCommands`; a new shortcut goes into that
  table, never into a key handler.
- **`BeginInvoke` before any dialog, message box or menu** started from a WebView2 event
  (`WebMessageReceived`, `NavigationStarting`, …) or from a key WebView2 forwarded. A nested
  message loop inside those handlers re-enters WebView2.
- **Two different things named WebView2.** `WebView2Loader.dll` is a small native DLL from the
  NuGet package: the single-file publish leaves it next to the exe, and the installer and the
  portable zip ship it. The WebView2 *Runtime* (the Edge engine) is never presumed present: the
  installer installs it when missing, and `Program` checks for it at startup and offers the
  download page (`ExitCode.Error` without it).

### JAWS findings that shape the code

`docs/jaws-spike.md` records what JAWS does inside WebView2 and why the code is the way it is.
In short:

- **No permanent `tabindex` on blocks or notes.** Enter on a focusable element puts JAWS in forms
  mode. The page adds `tabindex="-1"` only while it moves focus to an element and removes it on
  blur; a `keydown` Enter on a block or note is treated like a click, as a second line of defense.
- **Enter cannot be told from a click** (JAWS answers Enter with a mouse click), so both activate:
  a block gets a note, a note is edited. A double-click does nothing more; a click that ends a
  text selection does nothing.
- **Announcements are UI Automation notifications** (`StatusAnnouncer`), heard wherever the focus
  is, the virtual buffer included.
- **Screen readers own keys.** JAWS takes F8, so notes move with F9 / Shift+F9; screen readers
  take Alt+Down and Alt+Up, so blocks move with Alt+Shift+Down and Up. New key combinations keep
  colliding: a command that does not need a shortcut gets a menu item without one (View →
  Wider / Narrower notes list).
- **JAWS keeps its virtual cursor on DOM nodes.** Replacing the page's content throws it to the
  top, hence `morph.js`; and a different file must be a fresh page load, or JAWS keeps its old
  offset.
- **Notes are `role="note"` elements** with the role descriptions "user note" and, for braille,
  "unote" (localized). A button style was tried and dropped: long labels are hard to listen to.

### One window per file

Every window is its own process. `Utils/SingleInstance.cs`: the window showing a file owns a
named pipe named after the SHA-256 of its normalized full path. Opening a file that is already
open, from the command line, a link, the history, the clipboard or anywhere else, connects to
that pipe, asks the owner to come to the front, and opens nothing (`Program` checks before
creating a window, `MainWindow` before showing a file). Two windows can therefore never write
conflicting notes into one file. The same process-per-window design is why update checks run in
one window only (Updates below).

### Command-line output

`plancake.exe` is a GUI-subsystem exe and gets no console of its own. `ConsoleAttacher` (only
for subcommands, `--help`, `--version` and parse errors; the window never touches the console)
writes redirected output straight to the redirection as UTF-8 without BOM, and calls
`AttachConsole(ATTACH_PARENT_PROCESS)` **only when standard output is not redirected**, so a pipe
or Claude's captured output is never taken over. The caveat to keep in every doc: interactive
PowerShell and cmd do not wait for a GUI exe, so attached output can appear after the prompt
returns, and in PowerShell `$x = plancake list plan.md` does not wait and leaves
`$LASTEXITCODE` unset. Piped or captured output (`| Out-String`, `| Out-Host`, `cmd /c`, bash,
Claude's tools) always waits and is complete. `CliRunner` takes its writers as arguments, so the
tests need no console.

## Data locations

`App.DataFolder` resolves to `%APPDATA%\Oire\PlanCake`, or to `userdata/` next to the EXE
when that folder exists (portable mode, detected once at static init). Config files sit at the
root of the data folder (`PlanCake.cfg`); user content goes under the `data/` subfolder, so clearing user data
never takes the settings with it.

## File safety

PlanCake never writes a file whose text came from a lossy decode (replacement characters,
U+FFFD, introduced by decoding): it would destroy what they stand for. A file that is not UTF-8
is decoded only with a legacy encoding that decodes it cleanly (`Notes/LegacyEncoding.cs`: the
charset detector, then the document language's code page, then the ANSI code page unless that
is UTF-8, 65001); when none does, the file stays read-only and is never converted, whatever
the settings say. Tests inject the ANSI code page, so they never depend on the machine's.

## Updates

`Services/UpdateService.cs` (ported from SIC!) checks `App.AppcastUrl` with NetSparkle, and
verifies the appcast and the download with the Ed25519 public key in `App.UpdatePublicKey`,
which holds the public half of the pair in `keys/`. Should that constant ever not be a base64
32-byte key (a fork that has not made its own pair yet), every update check is off and the log
says why; Help → Check for updates says so too. Every window is a process: only the first one
still open does the startup and background checks (the named `Local\` mutex
`UpdateService.BackgroundChecksName`), so five open plans do not offer one update five times.
Help → Check for updates works in every window. The service returns an `UpdateCheckOutcome`
and `MainWindow` announces it; the service itself shows nothing but NetSparkle's update window.

The key pair lives in `keys/` at the repository root, which is gitignored: **never commit
`keys/NetSparkle_Ed25519.priv`**. To make it (once, or when forking):

```
dotnet tool install --global NetSparkleUpdater.Tools.AppCastGenerator
netsparkle-generate-appcast --generate-keys --key-path keys
```

then paste the contents of `keys/NetSparkle_Ed25519.pub` into `App.UpdatePublicKey`.

## Installer and releases

`installer/Build-Installer.ps1` (or `build-installer.bat`, which also opens the output folder)
compiles the translations with `-Strict`, publishes to `src/PlanCake/bin/x64/Release/publish`,
compiles `installer/plancake.iss` with Inno Setup 6, and writes to `installer/Output/`
(gitignored) the installer `plancake-v<VERSION>-setup.exe` and the portable
`plancake-v<VERSION>-portable.zip`, where `<VERSION>` is the four-part file version.
`-Appcast` adds `appcast.xml` and its signature, signed with the key in `keys/` (it needs
`netsparkle-generate-appcast`); `-Deploy` uploads the lot to plancake.oire.dev over SCP with the
host and path in `installer/deploy.json` (gitignored; copy `deploy.example.json`). Release notes
come from `changelogs/<X.Y.Z>.md`, named after the tag; the script hands the file to the
generator under the four-part name it looks for.

What ships is the same in both: `plancake.exe`, `WebView2Loader.dll`, `web\`, `help\` and
`locale\**\*.mo`. The publish folder holds more (the `.pdb`, WebView2's XML docs, a second
`WebView2Loader.dll` under `runtimes\`), so a new file beside the exe must be added to both the
`[Files]` section of `plancake.iss` and `$ShippedItems` in the script. The installer puts `{app}`
on the machine `PATH` and takes it off on uninstall, and installs the .NET 10 Desktop Runtime
and the WebView2 Runtime when missing (`CodeDependencies.iss`, from InnoDependencyInstaller).
The `AppId` in `plancake.iss` is permanent: Windows and winget know PlanCake by it. The `.iss`
and `.isl` files are UTF-8 with a BOM, which Inno Setup needs to read them as UTF-8.

PlanCake is published to winget as `Oire.PlanCake`. The first release creates the manifest with
`wingetcreate new` on the release's installer URL; after every later GitHub release, update it:

```bash
wingetcreate update -u 'https://github.com/Oire/plan-cake/releases/download/v<VERSION>/plancake-v<VERSION>-setup.exe|x64' -v <VERSION> --submit --token "$(gh auth token)" Oire.PlanCake
```

The installer is x64 only, but wingetcreate detects an Inno Setup installer as x86: without the
`|x64` suffix the update fails with "Multiple matches" (and in `wingetcreate new`, set the
architecture to x64 by hand).

## Error handling at startup

`Program.Main` returns an `ExitCode` and installs handlers for `AppDomain.UnhandledException`
and `TaskScheduler.UnobservedTaskException` — without them, an exception on a background
thread kills the process with nothing in the log.

Utility classes log and degrade; they do not show dialogs and do not call `Application.Exit`.
Deciding to stop is `Program`'s job.

## Tests

xUnit + AwesomeAssertions in `tests/PlanCake.Tests`. The app project grants it
`InternalsVisibleTo`, which is how `Config.OverrideFilePath` lets the config tests write to a
temp directory instead of the developer's real `%APPDATA%`. Static state means tests that
touch `Config` or `Localization` must not run in parallel across classes.

Keep the tests inherited from the template: they are cheap, and they fail loudly if a rename
breaks the data-folder layout or the localization fallback.
