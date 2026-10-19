# Open Markdown files with PlanCake

## Overview

PlanCake opens `.md` and `.markdown` files, but Windows does not know it: the installer registers
nothing, so PlanCake is missing from Explorer's Open with list and from Windows Settings →
Default apps, and a double-click on a plan never reaches it.

Since Windows 10 no program can make itself the default for a file type: the user's choice
(`UserChoice`) is protected by a hash only Windows' own interface writes. So the change has two
halves, agreed with the maintainer:

1. **The installer always registers PlanCake as a handler** for `.md` and `.markdown`, with no
   checkbox: a file type (ProgID) `PlanCake.Markdown`, PlanCake in each extension's
   `OpenWithProgids`, an `Applications\plancake.exe` entry, and capabilities listed under
   `RegisteredApplications`, so PlanCake appears in Open with and in Default apps. It never
   overrides the user's choice, and it does not write the extensions' default value either (that
   belongs to whichever program the user or Windows picked). The uninstaller removes all of it.
2. **Settings → Advanced gets a button** that opens PlanCake's own page in Windows Settings →
   Default apps (`ms-settings:defaultapps?registeredAppMachine=PlanCake`), where the user makes
   PlanCake the default for both extensions in Windows' own interface. Not a checkbox: unchecking
   could undo nothing, and it would be wrong whenever the default changes elsewhere. The button
   and its label are shown only when the installer's registration is there and PlanCake is not
   portable, since the portable version registers nothing.

File → Open in editor already handles PlanCake being the default program
(`LinkResolver.DefaultProgramFor` and `IsNoEditor`, used in `MainWindow.Commands.cs` `OpenInEditor`):
it then uses the "edit" verb or Notepad, so it never reopens the file in PlanCake. That needs no
change, and the ProgID must **not** define an "edit" verb, or Open in editor would find it.

This ships in PlanCake 1.1; the release waits for it.

## Done when

- [ ] After installing, PlanCake is listed in Explorer's Open with for a `.md` and a `.markdown`
      file, and in Windows Settings → Default apps (with its icon), where both extensions can be
      set to it
- [ ] With PlanCake set as the default, double-clicking a plan opens it in PlanCake (a path with
      spaces included), and double-clicking a plan that is already open brings its window forward
- [ ] Installing does not change which program opens Markdown files when the user had chosen one
- [ ] Uninstalling removes every registry entry and every key the installer created for the
      association, and leaves the extensions' other handlers alone
- [ ] Settings → Advanced has a label and the button "Open &Markdown files with PlanCake...", which
      opens PlanCake's page in Default apps (the general Default apps page on Windows versions
      without the per-app link); both are hidden in the portable version and when the
      registration is missing
- [ ] The new installer message is in all six `Custom.<code>.isl` files, and a test keeps their
      keys in step and the BOMs in place; the new interface strings are in all five catalogs
      with no fuzzy entries and unique mnemonics
- [ ] All six manuals describe the registration (Installing, Opening a file) and the button
      (Settings → Advanced), and `CHANGELOG.md` has the entry under Unreleased
- [ ] All validation commands pass

## Validation commands

Run from the repository root in PowerShell. While a PlanCake window is open, `bin\Debug` is
locked, so build and test into a temporary artifacts folder (CLAUDE.md, Tests).

- translations: `./src/PlanCake/locale/scripts/Compile-Translations.ps1 -Strict`
- build: `dotnet build --artifacts-path "$env:TEMP\plancake-artifacts"`
- test: `dotnet test --artifacts-path "$env:TEMP\plancake-artifacts"`
- format check: `dotnet format --verify-no-changes`
- installer (Task 2 and Task 6): `./installer/Build-Installer.ps1 -AllowDirty` compiles
  `plancake.iss` with Inno Setup 6. It publishes a Release build first, which takes a few
  minutes. Only the compile is checked: **do not run the installer it produces**; installing is
  a manual check (Post-completion).

## Context

- `installer/plancake.iss`: `#define`s at the top (`EnvironmentKey` is the model for the new
  ones), `[Setup]` (`PrivilegesRequired=admin`, so `HKLM` is the right root;
  `ChangesEnvironment=yes` at line 73 is the model for `ChangesAssociations=yes`), `[Registry]`
  (one machine `PATH` entry today), `[Code]` (`CurUninstallStepChanged`). UTF-8 with a BOM, and
  it contains "©", which a lost BOM garbles without failing the compile.
- `installer/Languages/Custom.{en,ru,uk,fr,he,de}.isl`: `[CustomMessages]`, PascalCase keys,
  each with a `;` comment above it, used as `{cm:Key}`. UTF-8 with a BOM. Nothing checks them
  today.
- **Reading source files from a test:** `LocalizationTests.TemplateMessageIds`
  (`tests/PlanCake.Tests/LocalizationTests.cs:92-94`) takes `[CallerFilePath] string testFile`
  and goes up three directories to the repository root. Use the same for `installer\`. (The
  manual and notices tests read copies in the build output; `installer\` is not copied there, and
  must not be.)
- `src/PlanCake/Ui/SettingsDialog.Designer.cs` / `SettingsDialog.cs`: three tabs; the Advanced
  tab's `advancedLayout` (Designer, around line 346) is 1 column × 2 rows, holding
  `convertToUtf8CheckBox` and a 100% filler row. A wrapping label to copy:
  `openingMarkerLabel` (Designer, line 267: `Anchor = Left | Right`, `AutoSize = true`). The
  constructor runs `Localizer.Localize`, `TextDirection.Apply`, then sets `Text`.
- A button in a dialog body that launches something, wired in the constructor and unwired in
  `OnFormClosed`: `AboutDialog.Designer.cs:114-141` and `AboutDialog.cs:34-35, 55-65, 153-155`
  (`Process.Start(new ProcessStartInfo(x) { UseShellExecute = true })?.Dispose()`, catching
  `Win32Exception` and `InvalidOperationException`, then `Log.Error(ex, …)` and
  `DialogHelper.Show(…, _("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error)`).
  `DialogHelper` is in `src/PlanCake/Utils/DialogHelper.cs`.
- `src/PlanCake/Utils/Constants/App.cs:43`: `App.IsPortable`. Import with
  `using App = Oire.PlanCake.Utils.Constants.App;`.
- `src/PlanCake/Cli/CliRunner.cs:50-53, 96-105` and `Program.cs:52-57`: a single file argument
  (`ArgumentArity.ZeroOrOne`), then `SingleInstance.TryActivate`. Explorer's `"%1"` starts one
  process per selected file, which this handles. An unquoted `%1` or a `%*` would not parse.
- Tests to keep in mind:
  - `tests/PlanCake.Tests/MnemonicTests.cs`: every button needs a mnemonic, unique per form (labels
    included, across all tabs), in every catalog. It walks hidden controls too.
  - `tests/PlanCake.Tests/TextDirectionTests.cs`: the only control named `buttonLayout` is the
    OK / Cancel row, so the new button must not go there. No control may set a `MaximumSize`.
  - `tests/PlanCake.Tests/SettingsDialogTests.cs:63-89`
    (`ChoicesAndCheckBoxes_FitTheirTabInEveryLanguage`) checks widths per language, but only for
    check boxes and combo boxes.
  - `tests/PlanCake.Tests/ManualParityTests.cs`: the six manuals must have the same ids, tables
    and `<kbd>` keys, and every `#link` must land.
- Translations: `src/PlanCake/locale/README.md` for the script workflow. The glossaries are
  `src/PlanCake/help/glossaries/<code>.json` (a `terms` array of `term` / `translation` /
  `context` / `notes`). Ukrainian says «папка», never «тека». In Russian and Ukrainian, a name in
  apposition that does not decline takes « » quotes (меню «Пуск», папка «Загрузки»).

## Development approach

- One task at a time; the validation commands pass before the next task starts. The one known
  exception is named in Task 3.
- Code changes come with tests for the new and changed behavior, success and error paths. The
  installer script has no test harness, so Task 2's tests read `plancake.iss` and the `.isl` files
  as text.
- A test that cannot pass until a later task is still written now, marked with a comment naming
  that task.
- Keep backward compatibility: no setting is added or renamed, and `PlanCake.cfg` is untouched.
- Never launch the app or the installer from a task: a window steals the maintainer's focus.
  Screen reader and installer checks are in Post-completion.
- When scope changes, update this plan: new tasks get a "➕" prefix, blockers a "⚠️" prefix.

## Implementation steps

### Task 1: Add the Default apps link and the button's visibility rule

#### Files
- Create: `src/PlanCake/Utils/DefaultApps.cs`
- Create: `tests/PlanCake.Tests/DefaultAppsTests.cs`

#### Steps
- [x] In `Utils/DefaultApps.cs`, add an `internal static class DefaultApps` (namespace
      `Oire.PlanCake.Utils`), UI-free:
  - `public const string RegisteredName = "PlanCake";`: the value name under
    `HKLM\Software\RegisteredApplications`. Its doc comment says it must equal
    `RegisteredAppName` in `installer/plancake.iss` (Task 2's test checks), and must stay
    URI-safe, since it goes into the link unescaped.
  - `public const string SettingsUri = "ms-settings:defaultapps?registeredAppMachine=" + RegisteredName;`
    with a doc comment on what it opens and on older Windows (Technical details → The Settings
    button).
  - `IsRegistered()`: true when `HKLM\Software\RegisteredApplications` has a `RegisteredName`
    value. Read it through `Microsoft.Win32.Registry`. Catch `SecurityException`, `IOException`
    and `UnauthorizedAccessException`, log at Debug and return false; never throw.
  - `ShowsSettingsButton(bool isPortable, bool isRegistered)` =>
    `!isPortable && isRegistered`, kept pure so it can be tested.
- [x] Tests:
  - `SettingsUri` is exactly `ms-settings:defaultapps?registeredAppMachine=PlanCake`;
  - `ShowsSettingsButton` is true only when not portable and registered (all four cases);
  - `IsRegistered()` returns without throwing on the test machine, whatever the answer.
- [x] Validation commands pass.

### Task 2: Register PlanCake for .md and .markdown in the installer

#### Files
- Modify: `installer/plancake.iss`
- Modify: `installer/Languages/Custom.en.isl`, `Custom.ru.isl`, `Custom.uk.isl`,
  `Custom.fr.isl`, `Custom.he.isl`, `Custom.de.isl`
- Create: `tests/PlanCake.Tests/InstallerTests.cs`

#### Steps
- [x] Add three `#define`s to `plancake.iss` next to `EnvironmentKey`, each with a comment:
  - `RegisteredAppName "PlanCake"`, which must equal `DefaultApps.RegisteredName`;
  - `AppProgId "PlanCake.Markdown"`;
  - `CapabilitiesKey "Software\Oire\PlanCake\Capabilities"`.

  Add `ChangesAssociations=yes` to `[Setup]` next to `ChangesEnvironment=yes`, with a comment
  that it tells Explorer to reload the associations.
- [x] Add to `[Registry]` the lines in Technical details → Registry entries, exactly as given
      there and in that order. Put a comment block above them saying three things:
  - the extensions' default value is never written, because it would take Markdown files away
    from the program the user or Windows picked;
  - the user sets the default in Default apps, which Settings → Advanced opens;
  - the parent keys come first because Inno Setup uninstalls in reverse order, so an emptied key
    is checked after its children are gone.

  Keep the `PATH` entry as it is.
- [x] Add `MarkdownFileType` (the file type's name in Explorer, "Markdown document" in English) to
      `[CustomMessages]` in all six `Custom.<code>.isl` files, each with a `;` comment like the
      others. Use each language's usual word for "document" and keep "Markdown" as is. Keep every
      edited file UTF-8 with a BOM (first three bytes `EF BB BF`), `plancake.iss` included.
- [x] `InstallerTests`, finding the repository root through `[CallerFilePath]` as Context says:
  - every `Custom.<code>.isl` has the same `[CustomMessages]` keys as `Custom.en.isl`;
  - `plancake.iss` and every `Custom.<code>.isl` start with a UTF-8 BOM;
  - registration parity: the `#define RegisteredAppName` value equals
    `DefaultApps.RegisteredName`. `[Registry]` has a `Software\RegisteredApplications` line with
    `ValueName: "{#RegisteredAppName}"` and `ValueData: "{#CapabilitiesKey}"`. Both
    `.md\OpenWithProgids` and `.markdown\OpenWithProgids` carry `{#AppProgId}`. Both
    `shell\open\command` lines have the quoted `""%1""`.

  Plain text matching on lines is enough; no Inno parser.
- [x] Validation commands pass, including `./installer/Build-Installer.ps1 -AllowDirty`, which
      compiles the `.iss` (the compile only; do not run the installer).

### Task 3: Add the Default apps button to Settings → Advanced

#### Files
- Modify: `src/PlanCake/Ui/SettingsDialog.Designer.cs`
- Modify: `src/PlanCake/Ui/SettingsDialog.cs`
- Modify: `tests/PlanCake.Tests/SettingsDialogTests.cs`

#### Steps
- [x] Designer: `advancedLayout` gets two more AutoSize rows after `convertToUtf8CheckBox` and
      before the filler, for 4 rows in all:
  - `defaultAppLabel`, a `Ui/WrappingLabel` laid out like `openingMarkerLabel`
    (`Anchor = AnchorStyles.Left | AnchorStyles.Right`, `AutoSize = true`), with
    `Margin = new Padding(3, 16, 3, 3)` to set it apart from the check box. Its text: "Windows
    lets only you choose which program opens Markdown files. This opens PlanCake's page in
    Windows Settings, where you can choose PlanCake for .md and .markdown files."
  - `defaultAppButton`, with the text "Open &Markdown files with PlanCake...". Size it like the
    About dialog's buttons (`AutoSize = true`, `MinimumSize = new Size(100, 0)`, no
    `MaximumSize`), anchored left.

  Set the tab order after `convertToUtf8CheckBox`. `M` is free in this dialog's English
  mnemonics.
- [x] `SettingsDialog.cs`: in the constructor, after the existing setup, set both controls'
      `Visible` to
      `DefaultApps.ShowsSettingsButton(App.IsPortable, DefaultApps.IsRegistered())`. Wire
      `defaultAppButton.Click` in the constructor and unwire it in `OnFormClosed`, as
      `AboutDialog` does.
- [x] The click opens `DefaultApps.SettingsUri` with
      `Process.Start(new ProcessStartInfo(DefaultApps.SettingsUri) { UseShellExecute = true })?.Dispose()`.
  - On `Win32Exception` or `InvalidOperationException`: `Log.Error(ex, …)`, then
    `DialogHelper.Show(_("Unable to open Windows Settings."), _("Error"), MessageBoxButtons.OK,
    MessageBoxIcon.Error)`, as `AboutDialog` does.
  - The Settings dialog stays open, and the button saves nothing.
- [x] Tests in `SettingsDialogTests`:
  - the button is a child of `advancedLayout` and not of `buttonLayout`;
  - the label is a `WrappingLabel`;
  - extend the fit-in-every-language check, or add a sibling test, so the button's preferred
    width fits its tab in every language, like the check boxes.

  Run `MnemonicTests` and `TextDirectionTests`, which already cover the dialog, and fix what they
  report, with one exception. **Expected failure until Task 4:** `Forms_HaveUniqueMnemonics("de")`
  fails. Until the button is translated, every catalog shows the English **&Markdown**, and the
  German dialog already uses M (`&Mausklick`, `de/PlanCake.po:157`). Do not edit any `.po` file
  in this task; Task 4 fixes it. Every other test passes.

### Task 4: Translate the new interface strings and add the Windows terms to the glossaries

#### Files
- Modify: `src/PlanCake/locale/messages.pot`
- Modify: `src/PlanCake/locale/{ru,uk,fr,he,de}/PlanCake.po`
- Modify: `src/PlanCake/help/glossaries/{ru,uk,fr,he,de}.json` (and `base.json` if it lists
  terms)

#### Steps
- [ ] Add these Windows terms to the `terms` of each glossary, as Windows itself names them in
      that language:
  - "Default apps" (the Settings page);
  - "Windows Settings";
  - "Open with";
  - "Choose another app".

  Give each a `context`, and `notes` where declension or « » quotes matter. These are what the
  catalogs (here) and the manuals (Task 5) use.
- [ ] Run `./src/PlanCake/locale/scripts/Extract-Strings.ps1`, then `Update-Translations.ps1`.
      Expect three new messages: the label, the button and "Unable to open Windows Settings.".
      Expect line re-wrapping in the catalogs too; it is harmless.
- [ ] Translate them in all five catalogs with the glossary terms. Leave no entry fuzzy or empty.
  - The button needs a mnemonic, in every catalog, that no other control in the Settings dialog
    uses in that catalog. `MnemonicTests` checks labels and buttons across all three tabs; German
    must not use M.
  - In Hebrew, keep `.md` and `.markdown` out of the label's running text, or put them where a
    right-to-left label cannot move their leading period (for example "Markdown (md, markdown)").
- [ ] Validation commands pass, starting with `Compile-Translations.ps1 -Strict`. Task 3's German
      mnemonic failure is gone now.

### Task 5: Update the six manuals

#### Files
- Modify: `src/PlanCake/help/{en,ru,uk,fr,he,de}/manual.html`

#### Steps
- [ ] Edit by hand, in the style the `write-manual` skill set for these manuals: plain
      sentences, labels word for word in `<strong>`, glossary terms. Add no new ids, or add each
      new id to all six.
- [ ] English, Installing:
  - `#installer`: a paragraph saying the installer makes PlanCake one of the programs that can
    open `.md` and `.markdown` files, so it appears in Open with and in Windows Settings →
    Default apps, without changing which program opens them. To have PlanCake open them on a
    double-click, choose it there, or use the button in Settings → Advanced (link to
    `#settings-advanced`).
  - `#portable`: one sentence that the portable version registers nothing, so you pick it through
    Open with → Choose another app.
  - `#uninstalling`: add that the uninstaller also removes PlanCake from those lists.
- [ ] English `#settings-advanced`: add a `<dt>` with the button's label word for word ("Open
      Markdown files with PlanCake...", without the `&`). Its `<dd>` says what the button opens,
      that you pick PlanCake for `.md` and `.markdown` there, that on older Windows versions it
      opens the general Default apps page, and that the portable version has no such button.
- [ ] English `#opening-files`, in `#open-dialog` (it already speaks of File Explorer): one
      sentence that once PlanCake opens Markdown files, a double-click on one in File Explorer
      opens it, linking to `#installer`.
- [ ] Carry all of it into the other five manuals in the same places. Word the button label exactly
      as that language's catalog does (Task 4), and use the glossary terms from Task 4.
- [ ] Validation commands pass (`ManualParityTests` above all).

### Task 6: Update documentation

#### Files
- Modify: `CHANGELOG.md`
- Modify: `README.md`
- Modify: `CLAUDE.md`

#### Steps
- [ ] `CHANGELOG.md`, under `[Unreleased]` → `### Added`: an entry in the style of the existing
      ones. **Open Markdown files with PlanCake**: the installer lists PlanCake among the
      programs that open `.md` and `.markdown` files, and Settings → Advanced opens Windows'
      Default apps page to make it the default.
- [ ] `README.md` "Installing": one sentence that the installer registers PlanCake for Markdown
      files without taking them over, and how to make it the default.
- [ ] `CLAUDE.md`:
  - "Installer and releases": a short paragraph on the registration. Say what is written, that
    the extensions' default value never is, and that there must be no "edit" verb (Open in
    editor relies on it). Mention `ChangesAssociations`, and that the uninstaller removes the
    entries with `uninsdeletekeyifempty` on the shared keys. Say that `RegisteredAppName` in
    the `.iss` and `DefaultApps.RegisteredName` must match, and that `InstallerTests` checks
    this, the `.isl` keys and the BOMs.
  - "Adding a language": a new `Custom.<code>.isl` needs every key of `Custom.en.isl`, which
    `InstallerTests` checks.
- [ ] Validation commands pass, including `./installer/Build-Installer.ps1 -AllowDirty` once
      more.

## Technical details

### Registry entries

All under `HKLM` (the installer is per machine, `PrivilegesRequired=admin`), in the 64-bit view
(`ArchitecturesInstallIn64BitMode`). Write the `[Registry]` lines exactly as follows, in this
order:

```
Root: HKLM; Subkey: "Software\Classes\{#AppProgId}"; ValueType: string; ValueName: ""; ValueData: "{cm:MarkdownFileType}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\{#AppProgId}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"
Root: HKLM; Subkey: "Software\Classes\{#AppProgId}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""
Root: HKLM; Subkey: "Software\Classes\.md"; Flags: uninsdeletekeyifempty
Root: HKLM; Subkey: "Software\Classes\.md\OpenWithProgids"; ValueType: string; ValueName: "{#AppProgId}"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKLM; Subkey: "Software\Classes\.markdown"; Flags: uninsdeletekeyifempty
Root: HKLM; Subkey: "Software\Classes\.markdown\OpenWithProgids"; ValueType: string; ValueName: "{#AppProgId}"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKLM; Subkey: "Software\Classes\Applications\{#MyAppExeName}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#MyAppName}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".md"; ValueData: ""
Root: HKLM; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".markdown"; ValueData: ""
Root: HKLM; Subkey: "Software\Classes\Applications\{#MyAppExeName}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""
Root: HKLM; Subkey: "Software\Oire"; Flags: uninsdeletekeyifempty
Root: HKLM; Subkey: "Software\Oire\PlanCake"; Flags: uninsdeletekeyifempty
Root: HKLM; Subkey: "{#CapabilitiesKey}"; ValueType: string; ValueName: "ApplicationName"; ValueData: "{#MyAppName}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "{#CapabilitiesKey}"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "{cm:AppDescription}"
Root: HKLM; Subkey: "{#CapabilitiesKey}"; ValueType: string; ValueName: "ApplicationIcon"; ValueData: "{app}\{#MyAppExeName},0"
Root: HKLM; Subkey: "{#CapabilitiesKey}\FileAssociations"; ValueType: string; ValueName: ".md"; ValueData: "{#AppProgId}"
Root: HKLM; Subkey: "{#CapabilitiesKey}\FileAssociations"; ValueType: string; ValueName: ".markdown"; ValueData: "{#AppProgId}"
Root: HKLM; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "{#RegisteredAppName}"; ValueData: "{#CapabilitiesKey}"; Flags: uninsdeletevalue
```

Notes:
- **Quoting.** In an Inno Setup string a `"` is written `""`, so `"""{app}\{#MyAppExeName}"" ""%1"""`
  stores `"C:\Program Files\Oire\PlanCake\plancake.exe" "%1"`. Both parts must be quoted. An
  unquoted path with spaces splits into several arguments, `CliRunner` rejects them as a parse
  error, and the error goes to a console a double-click does not have, so the user sees nothing.
- **What is never written.** The `(default)` value of `Software\Classes\.md` and `.markdown`, and
  any verb but `open` (no `edit`: see Overview). Nothing gets `uninsdeletekey` on `.md`,
  `.markdown`, `OpenWithProgids`, `Software\Oire` or `RegisteredApplications`, which other
  programs share. Those get `uninsdeletevalue`, or `uninsdeletekeyifempty`, which removes a key
  only when nothing is left in it.
- **Order.** Inno Setup uninstalls entries in reverse order, so the parent keys with
  `uninsdeletekeyifempty` come before their children, and are checked for emptiness after the
  children are gone.
- **A line with only `Flags`** (`ValueType` left out, so `none`) creates the key if it is missing
  and writes no value.

### The Settings button

- `ms-settings:defaultapps?registeredAppMachine=PlanCake` opens PlanCake's page in Default apps
  on Windows 11 21H2 and 22H2 with the 2023-04 cumulative update, and on 23H2 and later.
  Windows versions without it ignore the parameter and open the general Default apps page
  ([Microsoft Learn: Launch the Default Apps settings page](https://learn.microsoft.com/windows/apps/develop/launch/launch-default-apps-settings)).
  `registeredAppMachine` matches the `HKLM\Software\RegisteredApplications` registration.
- Visibility is decided once, when the dialog is built. The button is a launcher, not a
  setting: nothing goes into `PlanCake.cfg`, and OK and Cancel are unaffected by it.

## Post-completion

- **Installer check, by the maintainer.** On a machine (or VM) where another program already
  opens `.md`:
  - install, and check the default did not change;
  - check that PlanCake is in Open with for both extensions, and in Default apps with its icon;
  - set it as the default there, double-click a plan whose path has spaces, and double-click it
    again while it is open (its window comes forward);
  - uninstall, and check that `PlanCake.Markdown`, `Applications\plancake.exe`, the Capabilities
    key and the `RegisteredApplications` value are gone, that `OpenWithProgids` lost only
    PlanCake's value, and that the other program still opens `.md`.

  Repeat the install on Windows 10 if one is at hand.
- **JAWS check, one item at a time.** In Settings → Advanced:
  - the label reads, and the button reads with its name and mnemonic;
  - the button opens PlanCake's page in Default apps, and that page lets the user set `.md` and
    `.markdown` with the keyboard;
  - the button is absent in the portable build (a `userdata` folder next to the exe).

  Check the Hebrew interface too: the mirrored layout, and that the label's extensions read
  right.
- **Release 1.1** resumes after this merges: the steps in CLAUDE.md → Releasing, with this
  feature added to `changelogs/1.1.0.md`.
- **The old update host.** PlanCake 1.0.0.11 looks for updates at
  `https://plancake.oire.dev/appcast.xml`, which answers 404. Until that host redirects to
  `plan-cake.oire.dev` or serves the appcast, 1.0.0.11 users are never offered 1.1.
