# PlanCake — Repo Conventions

PlanCake reads Markdown files (above all long implementation plans) as rendered HTML in
WebView2 and writes the user's notes on them straight into the `.md` file. It runs as a window,
or headless with a subcommand (`list`, `check`, `clear`, `export`). .NET 10, Windows x64 only.

The repository was created from Oire's `winforms-template`; the conventions below come from it
and stay binding. The implementation plan is `docs/plans/completed/001-plan-cake-v1.md`.

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

- **A new dialog:** set its title in the constructor right after `Localizer.Localize`
  (`Text = _("…");`), because the extractor misses a designer `Text =` on the form itself; call
  `TextDirection.Apply(this)`; send every message box through `DialogHelper`; and add the form
  to the lists in `MnemonicTests` and `TextDirectionTests` (both fail until you do: each checks
  that its list covers every `Form` type).
- **A label that wraps is a `Ui/WrappingLabel`.** With the system code page set to UTF-8 (the
  "Beta: Use Unicode UTF-8 for worldwide language support" option), Windows text drawing takes
  every letter outside ASCII for a double-byte character and breaks a line between any two of
  them, so a stock `Label` splits Russian, Ukrainian and Hebrew words in the middle.
  `WrappingLabel` measures and draws with `TextFormatFlags.NoFullWidthCharacterBreak`.
- **Text from the document in a right-to-left interface** (an excerpt, a note) goes through
  `TextDirection.Embed`: text that starts left to right is wrapped in LEFT-TO-RIGHT EMBEDDING and
  POP DIRECTIONAL FORMATTING (U+202A … U+202C), so its punctuation stays where it was written.
  Not the isolates U+2068 and U+2069: GDI, which draws labels, list views and message boxes,
  shows them as visible boxes. A box that holds code or a link (the note markers, a URL) stays
  left to right through `TextDirection.KeepLeftToRight`.
- **The user manual follows the UI.** `help/<code>/manual.html` (six languages) is written by
  hand with the `write-manual` skill, using the glossaries in `help/glossaries/`. It repeats
  menu names, shortcuts and setting labels word for word. `ManualParityTests` checks only that
  the six have the same ids, tables and keys, that every `#link` lands, and that the keyboard
  reference lists every `HostCommands` shortcut as the menus spell it; the wording is checked by
  hand. A change to a command, a key, a menu item or a setting updates all six manuals in the
  same change.

- The catalog is `PlanCake.po` / `.mo`, named after `App.Name`; the executable is `plancake.exe`
  (`AssemblyName`). The gettext scripts read `App.Name` from `Utils/Constants/App.cs`, not the
  `AssemblyName`.

See `src/PlanCake/locale/README.md` for the script workflow.

## Versioning

GitVersion owns every version number (`GitVersion.yml`). **Never write a `<Version>` literal
into a `.csproj`.** Every version has four parts, `X.Y.Z.N`, the same model as SIC:

- A three-part tag `vX.Y.Z` (annotated, "Start development of version X.Y.Z") **opens a
  development cycle**. GitVersion counts from it: every commit after it builds as `X.Y.Z.N`,
  where `N` is the commit distance from that tag.
- When the cycle is ready, the release commit gets a **four-part tag `vX.Y.Z.N`** with exactly
  the version that commit builds as (`v1.0.0.7`). That is the release: on GitHub, on
  plancake.oire.dev and on winget, and in the installer and zip names. GitVersion does not count
  from a four-part tag; it only marks the commit.
- Right after the release, the **next cycle's three-part tag** (`v1.1.0`, or `v1.0.1` for a
  patch) goes on the next commit, never on the release commit itself: with two tags on one
  commit GitVersion takes the higher one, and the release commit would then build as `1.1.0.0`.

CI checks out with `fetch-depth: 0` because a shallow clone has no tag history. Releases and
their files are never built on CI; see Releasing below.

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

The plan (`docs/plans/completed/001-plan-cake-v1.md`, Technical details) has the full rules; this is the
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
  row) carries `data-lines="start-end"` in original lines, and each note is a `role="region"`
  element after its block. Line numbers are **1-based** wherever a user, a CLI consumer or
  `data-lines` sees them; Markdig's 0-based lines are converted at the boundary. The strings the
  renderer writes come in through `RenderStrings`, so it never touches the catalog.
  `PositionRestorer` picks the block to return to after a re-render.
- **Ui/** holds `MainWindow` (the menu, the notes list, every note action; a partial class split
  by subject into `MainWindow.Menu.cs`, `.Keys.cs`, `.Page.cs`, `.Notes.cs`, `.NotesList.cs` and
  so on), `DocumentView` (the WebView2 control and its lockdown; the navigation rule is
  `NavigationGate`), `HostCommands` (the one table of commands and keys that the menu, both key
  paths and the Keyboard shortcuts dialog all read), `PageMessages` (the page protocol's
  message shapes) and `PageMessageRouter` (what the window does about a page message: UI-free
  and tested, while `MainWindow` does the dialogs and menus).
- **Cli/** is `CliRunner` (System.CommandLine 2.x: `list`, `check`, `clear`, `export`) and
  `ConsoleAttacher`. `list`, `check` and `export` never write the Markdown file; `clear` follows
  the conversion setting like the window. `list`'s text lines are data and stay in English;
  other messages follow the interface language. `ExitCode.NotesRemain` (3) is `check` finding
  notes, distinct from `ExitCode.Error` (1).
- **Services/** is `UpdateService` (Updates below) and `MarkdownDownloader` (File → Open from
  link: http(s) only, a GitHub file page turned into its raw file, 30 seconds, 10 MB, into the
  Downloads folder, always under a `.md` name and with a `Zone.Identifier` stream, as a
  browser marks a download).
- **Links in a plan never run anything.** A plan may come from a download or an AI assistant,
  and its link text can say anything. `LinkResolver` sorts a program or script
  (`LinkKind.Program`: `.exe`, `.bat`, `.js`, `.lnk`, … and `PATHEXT` and `AssocIsDangerous`)
  away from the shell (the window offers to show it in File Explorer), and does not follow, or
  even check, a UNC path on another host than the document's, since touching it sends the
  user's credentials there. File → Open in editor uses the "edit" verb or Notepad for anything
  that is not Markdown, never the default verb; a Markdown file gets the default verb unless
  Windows opens Markdown with PlanCake itself or with nothing (`LinkResolver.DefaultProgramFor`),
  and then goes the same way.
- **web/** is the page. `app.js` shows what the host renders, reports which block or note the
  user acts on, and moves focus when the host asks. `morph.js` updates the page in place when
  the same file is rendered again; `blocks.js` picks the block Alt+Shift+Down and Up move to.
  Both expose a pure function that the tests run in Jint (`MorphPlanTests`, `BlockPickTests`).
  **No user-visible string is written in the page**: all of them come from the host in the
  `strings` message, already translated. Focus moves through `focusElement` in `app.js`, which
  adds a temporary `tabindex="-1"` and marks the element with `data-plancake-current`;
  `app.css` draws the focus outline from that mark, not from `:focus`, which Chromium did not
  show when the page moved the focus itself. An attribute the page manages at run time
  (`data-lines`, `data-note`, `tabindex`, `data-plancake-current`) must be in `morph.js`'s
  `volatileAttributes`, or every re-render counts it as a content change. The page's content
  security policy (`index.html`) is the only thing keeping a plan's raw HTML from running
  script; `PageSecurityPolicyTests` pins it.

### Page protocol

JSON messages through `chrome.webview.postMessage` / `PostWebMessageAsJson`, each with a `type`;
the full list is in the plan (Technical details → Page protocol) and the shapes in
`Ui/PageMessages.cs`. Host → page: `render` (HTML, document language, title, where to put the
focus), `strings`, `focusNote`, `nextNote` / `previousNote`, `nextBlock` /
`previousBlock`, `taskState`. Page → host: `activate`, `activateNote`, `contextMenu`,
`toggleTask`, `position`, `openLink`, `noMoreNotes`, `noMoreBlocks`, `dropFiles`, `goBack`,
`ready`. Every render carries a `generation`; the host ignores a message from an older render
(`PageMessageRouter`, and `WhileCurrent` for an action deferred past a re-render). There is no `announce` message: every announcement goes
through `StatusAnnouncer`.

A different file gets a freshly loaded page (the host navigates to `index.html` again and
renders once the page says `ready`). A re-render of the same file (an outside change, a reload,
a note action, undo) is patched in place by `morph.js`. Only a render the user asked for by
acting on a block carries focus; the rest leave the reader where they are.

### WebView2

- **User data folder** `App.WebView2DataFolder`: `%LOCALAPPDATA%\Oire\PlanCake\WebView2`
  (`userdata\WebView2` when portable). Not the install folder, which is not writable, and not
  the roaming `App.DataFolder`: a browser cache does not belong in a roaming profile.
- **The page's own host.** `web\` is mapped to `https://app.plancake/`. The only navigation
  allowed is the host's own `Navigate` to a page there; every other navigation, frame navigation
  and new window is refused, and the CSP in `index.html` allows no script but the app's own.
  Links in a plan go to the host (`openLink`, `LinkResolver`), so raw HTML in a plan can neither
  take the view away nor run script. Browser context menus, accelerator keys and the status bar
  are off; dev tools are on in Debug only.
- **Accelerator keys.** Keys pressed while the page has focus never pass through the host's
  message loop, so the native menu bar's accelerators never see them. The WinForms control does
  not call `ProcessCmdKey` either: it raises its own `KeyDown` (and `KeyUp`) from the browser's
  `AcceleratorKeyPressed`, which `DocumentView.AcceleratorKeyDown` and `AcceleratorKeyUp` pass
  on. `MainWindow` sends that path and `ProcessCmdKey` through `HostCommands`; a new shortcut
  goes into that table, never into a key handler. For the same reason Windows never sees the
  keys that enter the menu bar from the page: what is not a host command goes through
  `MenuKeys` (pure, tested), and Alt+letter, Alt pressed and released alone, and F10 post
  `WM_SYSCOMMAND` / `SC_KEYMENU` to the form (the mnemonic's character on the focused window's
  keyboard layout, or 0). So a host command must never be Alt+letter, or it hides a menu
  (`MenuKeysTests` checks).
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
- **Notes are `role="region"` elements** (not `role="note"`, which JAWS navigates worse) named
  `aria-label="User note"` (localized). Role descriptions ("user note", and "unote" for braille)
  were tried and dropped as cumbersome to listen to, and so was a button style: long labels are
  hard to listen to.

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

`App.DataFolder` resolves to `%APPDATA%\Oire\PlanCake`, or to `userdata\` next to the EXE when
that folder exists (portable mode, detected once at static init). It holds `PlanCake.cfg`
(written to a temporary file and moved over the old one, since other windows read it; its
`[Window]` section is where the last window closed, its size, the notes list's width and the
zoom, which the next window opens with, moved onto a screen that is still there) and
`logs\` (Serilog: `PlanCake.log`, `PlanCake-short.log`, `errors.log`, `errors-short.log`,
`analysis.json`; every window and every CLI run is its own process, so while one holds a log,
the others write to numbered siblings such as `PlanCake_001.log`). Each log rolls over at 10 MB
and keeps ten files, numbered siblings included; a Release build logs from Information up, a
Debug build from Debug. The browser's user data folder, `App.WebView2DataFolder`, is
`%LOCALAPPDATA%\Oire\PlanCake\WebView2`, or `userdata\WebView2` when portable; the uninstaller
offers to remove both folders. PlanCake keeps no other user content. The logs are the first
thing to read when a user reports a problem.

## File safety

PlanCake never writes a file whose text came from a lossy decode (replacement characters,
U+FFFD, introduced by decoding): it would destroy what they stand for. A file that is not UTF-8
is decoded only with a legacy encoding that decodes it cleanly (`Notes/LegacyEncoding.cs`: the
charset detector, then the document language's code page, then the ANSI code page unless that
is UTF-8, 65001); when none does, the file stays read-only and is never converted, whatever
the settings say. Two cases never reach the legacy code pages at all and stay read-only too: a
file whose BOM states its encoding but that does not decode in it, and UTF-8 with a few damaged
bytes (`LegacyEncoding.FindDamagedUtf8`), which a single-byte code page would turn into
mojibake. Tests inject the ANSI code page, so they never depend on the machine's.

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
`plancake-v<VERSION>-portable.zip`, where `<VERSION>` is the four-part file version, and keeps
`plancake.pdb` in `installer/Output/symbols/<VERSION>/` (never shipped; it is what turns a stack
trace in a user's `errors.log` into source lines). `-Appcast` adds `appcast.xml` and its
signature, signed with the key in `keys/` (it needs `netsparkle-generate-appcast`); `-Deploy`
uploads the lot to plancake.oire.dev over SCP with the host and path in
`installer/deploy.json` (gitignored; copy `deploy.example.json`). Release notes come from
`changelogs/<X.Y.Z>.md`, named after the three-part version of the cycle, since `N` is only
known once the release commit exists; the script hands the file to the generator under the
four-part name it looks for (a `changelogs/<X.Y.Z.N>.md` wins when there is one).

The script refuses a working tree with uncommitted or untracked files (`git status
--porcelain`), which would ship under the version of a commit that does not hold them;
`-AllowDirty` overrides that for a trial build that is never released. With `-Appcast` or
`-Deploy` it warns when HEAD does not carry the tag `v<VERSION>` of the build.

What ships is the same in both: `plancake.exe`, `WebView2Loader.dll`, `web\`, `help\`,
`locale\**\*.mo`, `LICENSE` and `THIRD-PARTY-NOTICES.txt` (the last two copied next to the exe
by the `.csproj`; Help → About → Licenses opens them in Notepad). The publish folder holds more
(the `.pdb`, WebView2's XML docs, a second `WebView2Loader.dll` under `runtimes\`), so a new
file beside the exe must be added to both the `[Files]` section of `plancake.iss` and
`$ShippedItems` in the script. The installer puts `{app}` on the machine `PATH` and takes it
off on uninstall, adds a Start menu shortcut to the manual in the language Setup ran in
(`help\{language}\manual.html`), shows the Ready to Install page (it lists the runtimes Setup
is about to download, and its button is the one that says Install), and installs the .NET 10
Desktop Runtime and the WebView2 Runtime when missing (`CodeDependencies.iss`, from
InnoDependencyInstaller). The `AppId` in `plancake.iss` is permanent: Windows and winget know
PlanCake by it. The `.iss` and `.isl` files are UTF-8 with a BOM, which Inno Setup needs to
read them as UTF-8.

The uninstaller's question about removing settings and logs looks in `{userappdata}` and
`{localappdata}`, the folders of the account it runs as. When a standard user uninstalls with
an administrator's password, that is the administrator, so the question is not asked and the
user's folders stay. Inno Setup cannot reach the original user at uninstall time
(`ExecAsOriginalUser` is install-only), so this is a known limitation, explained in the
manuals' Uninstalling section.

`THIRD-PARTY-NOTICES.txt` (repository root) holds the license of every package bundled into
`plancake.exe` and `WebView2Loader.dll`, with the full text where the license asks for it (the
MPL 1.1 of UTF.Unknown with its source-availability notice, the BSD texts, Apache 2.0 once at
the end). `ThirdPartyNoticesTests` walks `plancake.deps.json` and fails when a runtime package
is not named in it. When a package is added, add its section by hand: the license file in
`~/.nuget/packages/<id>/<version>/` (its `.nuspec` names it), or, for a license expression, the
license file of its repository at the commit the `.nuspec` gives. A version bump needs no
change.

The SDK is pinned by `global.json` (feature band, `rollForward: latestPatch`), and CI's
`setup-dotnet` reads the same file, so a release built locally and CI use the same band. Bump it
deliberately: a new band can bring new analyzer warnings, which are errors here.

PlanCake is published to winget as `Oire.PlanCake`. The first release creates the manifest with
`wingetcreate new` on the release's installer URL; after every later GitHub release, update it:

```bash
wingetcreate update -u 'https://github.com/Oire/plan-cake/releases/download/v<VERSION>/plancake-v<VERSION>-setup.exe|x64' -v <VERSION> --submit --token "$(gh auth token)" Oire.PlanCake
```

The installer is x64 only, but wingetcreate detects an Inno Setup installer as x86: without the
`|x64` suffix the update fails with "Multiple matches" (and in `wingetcreate new`, set the
architecture to x64 by hand). `<VERSION>` here is the four-part version, as in the tag.

### Releasing

From start to finish, by hand on the maintainer's machine (CI never builds a release):

1. On `master`, with every change for the release merged: run
   `src/PlanCake/locale/scripts/Extract-Strings.ps1` and `Update-Translations.ps1`, translate
   anything new in all five catalogs (no fuzzy entries), and check that the six manuals match
   the UI. Write `changelogs/<X.Y.Z>.md` (the update window's notes) and move the
   `[Unreleased]` entries of `CHANGELOG.md` under the version. Commit: the tree must be clean.
2. `./installer/Build-Installer.ps1 -Appcast`, or `-Deploy`, which implies `-Appcast` and
   uploads the installer, the zip, `appcast.xml` and its signature to plancake.oire.dev (build
   without `-Deploy` first to try the installer). The file names carry the version:
   `plancake-v1.0.0.7-setup.exe` comes from the commit at distance 7 from `v1.0.0`.
3. Tag that commit with the same four-part version and push the tag:
   `git tag -s v1.0.0.7 -m "Release 1.0.0.7"`, then `git push origin v1.0.0.7`. Tagging before
   step 2 works too and silences the script's warning. Point the version's link at the end of
   `CHANGELOG.md` to that tag.
4. Create the GitHub release from the tag with the installer and the zip from
   `installer/Output/`:
   `gh release create v1.0.0.7 installer/Output/plancake-v1.0.0.7-setup.exe installer/Output/plancake-v1.0.0.7-portable.zip --title "PlanCake 1.0.0.7" --notes-file changelogs/1.0.0.md`.
   Keep `installer/Output/symbols/1.0.0.7/plancake.pdb` somewhere safe.
5. Update winget with the four-part version (the `wingetcreate update` command above;
   `wingetcreate new` for the first release).
6. Open the next cycle on the next commit, not on the release commit:
   `git tag -s v1.1.0 -m "Start development of version 1.1.0"`, then push it.

## Error handling at startup

`Program.Main` returns an `ExitCode` and installs handlers for `AppDomain.UnhandledException`
and `TaskScheduler.UnobservedTaskException` — without them, an exception on a background
thread kills the process with nothing in the log. For the window it also subscribes
`Application.ThreadException` (with `UnhandledExceptionMode.CatchException`): WinForms catches
an exception from the message loop itself (an `async void` handler, a `BeginInvoke` callback),
and without a handler shows its own dialog and logs nothing. The handler logs it and asks
whether to keep PlanCake open.

Utility classes log and degrade; they do not show dialogs and do not call `Application.Exit`.
Deciding to stop is `Program`'s job.

## Tests

xUnit + AwesomeAssertions in `tests/PlanCake.Tests`. The app project grants it
`InternalsVisibleTo`, which is how `Config.OverrideFilePath` lets the config tests write to a
temp directory instead of the developer's real `%APPDATA%`. Static state means tests that
touch `Config` or `Localization` must not run in parallel across classes.

- A test class that changes `Config` or the interface language, or asserts on text from `_()`,
  goes in `[Collection(LocalizationCollection.Name)]`, which runs alone.
- A test that builds a form or control wraps it in `Sta.Run(...)`; forms are built and
  disposed, never shown.
- `web/morph.js` and `web/blocks.js` expose pure functions that `MorphPlanTests` and
  `BlockPickTests` run in Jint, so the page needs no JS toolchain.
- `DocumentViewLockdownTests` (trait `Category=WebView2`) runs a real WebView2 in a form that is
  never shown and checks that a plan's raw HTML posts nothing and navigates nowhere. It is
  skipped without the WebView2 Runtime; `--filter "Category!=WebView2"` leaves it out.
- Coverage is local and on demand, never run in CI: `dotnet test --collect:"XPlat Code Coverage"`
  writes a Cobertura report under `TestResults\`. Coverlet needs the app's Debug PDB to stay `portable` and the test project's
  `PreserveCompilationContext`, without which it cannot resolve the WinForms and WebView2
  references and instruments nothing.
- `ManualParityTests` keeps the six manuals in step (see Localization), and
  `ThirdPartyNoticesTests` keeps `THIRD-PARTY-NOTICES.txt` in step with the packages (see
  Installer and releases).
- CI (`.github/workflows/dotnet.yml`) compiles the translations, checks the format, builds and
  tests, with a 20-minute limit. `codeql.yml` runs CodeQL on the C# and on the page's
  JavaScript on pushes and pull requests to `master` and weekly.
- While a PlanCake window is open, `bin\Debug\plancake.exe` is locked: build and test with
  `dotnet build --artifacts-path <temp dir>` (and the same for `dotnet test`) rather than
  closing the user's window.

Keep the tests inherited from the template: they are cheap, and they fail loudly if a rename
breaks the data-folder layout or the localization fallback.
