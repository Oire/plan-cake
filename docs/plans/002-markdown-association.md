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
change.

This ships in PlanCake 1.1; the release waits for it.

## Done when

- [ ] After installing, PlanCake is listed in Explorer's Open with for a `.md` and a `.markdown`
      file, and in Windows Settings → Default apps, where both extensions can be set to it
- [ ] With PlanCake set as the default, double-clicking a plan opens it in PlanCake (a path with
      spaces included), and double-clicking a plan that is already open brings its window forward
- [ ] Installing does not change which program opens Markdown files when the user had chosen one
- [ ] Uninstalling removes every registry entry the installer added for the association, and
      leaves the extensions' other handlers alone
- [ ] Settings → Advanced has a label and the button "Open &Markdown files with PlanCake...", which
      opens PlanCake's page in Default apps (the general Default apps page on Windows versions
      without the per-app link); both are hidden in the portable version and when the
      registration is missing
- [ ] The new installer messages are in all six `Custom.<code>.isl` files and a test keeps their
      keys in step; the new interface strings are in all five catalogs with no fuzzy entries and
      unique mnemonics
- [ ] All six manuals describe the registration (Installing) and the button (Settings →
      Advanced), and `CHANGELOG.md` has the entry under Unreleased
- [ ] All validation commands pass

## Validation commands

Run from the repository root in PowerShell. While a PlanCake window is open, `bin\Debug` is
locked, so build and test into a temporary artifacts folder (CLAUDE.md, Tests).

- translations: `./src/PlanCake/locale/scripts/Compile-Translations.ps1 -Strict`
- build: `dotnet build --artifacts-path "$env:TEMP\plancake-artifacts"`
- test: `dotnet test --artifacts-path "$env:TEMP\plancake-artifacts"`
- format check: `dotnet format --verify-no-changes`
- installer (Task 2 and the end): `./installer/Build-Installer.ps1 -AllowDirty` compiles
  `plancake.iss` with Inno Setup 6. It publishes a Release build, which takes a few minutes. Only
  the compile is checked here: **do not run the installer it produces**; installing is a manual
  check (Post-completion).

## Context

- `installer/plancake.iss`: `[Setup]` (`PrivilegesRequired=admin`, so `HKLM` is the right
  root; `ChangesEnvironment=yes` at line 73 is the model for `ChangesAssociations=yes`),
  `[Registry]` (one machine `PATH` entry today), `[Code]` (`CurUninstallStepChanged`). UTF-8 with
  a BOM.
- `installer/Languages/Custom.{en,ru,uk,fr,he,de}.isl`: `[CustomMessages]`, PascalCase keys,
  each with a `;` comment above it, used as `{cm:Key}`. UTF-8 with a BOM. Nothing checks them
  today.
- `src/PlanCake/Ui/SettingsDialog.Designer.cs` / `SettingsDialog.cs`: three tabs; the Advanced
  tab's `advancedLayout` (Designer, around line 346) is 1 column × 2 rows, holding
  `convertToUtf8CheckBox` and a 100% filler row. The constructor runs `Localizer.Localize`,
  `TextDirection.Apply`, then sets `Text`.
- A button in a dialog body that launches something, wired in the constructor and unwired in
  `OnFormClosed`: `AboutDialog.Designer.cs:114-141` and `AboutDialog.cs:34-35, 55-65, 153-155`
  (`Process.Start(new ProcessStartInfo(x) { UseShellExecute = true })?.Dispose()`, catching
  `Win32Exception` and `InvalidOperationException`, and telling the user through
  `DialogHelper.Show`).
- `src/PlanCake/Utils/Constants/App.cs:43`: `App.IsPortable`. Import with
  `using App = Oire.PlanCake.Utils.Constants.App;`.
- `src/PlanCake/Cli/CliRunner.cs:50-53, 96-105` and `Program.cs:52-57`: a single file argument
  (`ArgumentArity.ZeroOrOne`), then `SingleInstance.TryActivate`. Explorer's `"%1"` starts one
  process per selected file, which this handles; an unquoted `%1` or `%*` would not parse.
- Tests to keep in mind:
  - `tests/PlanCake.Tests/MnemonicTests.cs`: every button needs a mnemonic, unique per form, in
    every catalog. It walks hidden controls too, so the new button is checked even where it is
    hidden.
  - `tests/PlanCake.Tests/TextDirectionTests.cs`: the only control named `buttonLayout` is the
    OK / Cancel row, so the new button must not go there. No control may set a `MaximumSize`.
  - `tests/PlanCake.Tests/SettingsDialogTests.cs:63-89`
    (`ChoicesAndCheckBoxes_FitTheirTabInEveryLanguage`) checks widths per language, but only for
    check boxes and combo boxes.
  - `tests/PlanCake.Tests/ManualParityTests.cs`: the six manuals must have the same ids, tables
    and `<kbd>` keys.
- Translations: `src/PlanCake/locale/README.md` for the script workflow;
  `src/PlanCake/help/glossaries/<code>.json` for terms. Ukrainian says «папка», never «тека».
  In Russian and Ukrainian, a name in apposition that does not decline takes « » quotes
  (папка «Загрузки»).

## Development approach

- One task at a time; the validation commands pass before the next task starts.
- Code changes come with tests for the new and changed behavior, success and error paths. The
  installer script has no test harness; Task 1's tests read `plancake.iss` and the `.isl` files
  as text instead.
- A test that cannot pass until a later task is still written now, marked with a comment naming
  that task.
- Keep backward compatibility: no setting is added or renamed, and `PlanCake.cfg` is untouched.
- Never launch the app or the installer from a task: a window steals the maintainer's focus.
  Screen reader and installer checks are in Post-completion.
- When scope changes, update this plan: new tasks get a "➕" prefix, blockers a "⚠️" prefix.

## Implementation steps

### Task 1: Add the registration constants and the Default apps link

#### Files
- Create: `src/PlanCake/Utils/DefaultApps.cs`
- Create: `tests/PlanCake.Tests/DefaultAppsTests.cs`

#### Steps
- [ ] In `Utils/DefaultApps.cs`, add an `internal static class DefaultApps` (namespace
      `Oire.PlanCake.Utils`), UI-free:
  - `RegisteredName` (`"PlanCake"`), the value name under `HKLM\Software\RegisteredApplications`;
  - `CapabilitiesKey` (`@"Software\Oire\PlanCake\Capabilities"`);
  - `ProgId` (`"PlanCake.Markdown"`);
  - `SettingsUri`: `"ms-settings:defaultapps?registeredAppMachine=" +
    Uri.EscapeDataString(RegisteredName)`;
  - `IsRegistered()`: true when `HKLM\Software\RegisteredApplications` has a `RegisteredName`
    value. Read it through `Microsoft.Win32.Registry`, catching `SecurityException`,
    `IOException` and `UnauthorizedAccessException`, so it returns false (and logs at Debug)
    rather than throwing;
  - `ShowsSettingsButton(bool isPortable, bool isRegistered)` =>
    `!isPortable && isRegistered`, kept pure so it can be tested.

  Doc comments say why each constant must match `installer/plancake.iss`.
- [ ] Tests: `SettingsUri` is exactly `ms-settings:defaultapps?registeredAppMachine=PlanCake`.
      `ShowsSettingsButton` is true only for not portable and registered (all four cases).
- [ ] Parity test, `InstallerRegistration_MatchesDefaultApps`: read `installer/plancake.iss`
      (find the repository root the way `ManualParityTests` or `ThirdPartyNoticesTests` finds
      files outside the test bin). Assert that it registers `RegisteredName` under
      `Software\RegisteredApplications` pointing at `CapabilitiesKey`, and that it uses `ProgId`
      for both `.md` and `.markdown`. This test fails until Task 2: say so in a comment.
- [ ] Validation commands pass (all but the parity test, which waits for Task 2).

### Task 2: Register PlanCake for .md and .markdown in the installer

#### Files
- Modify: `installer/plancake.iss`
- Modify: `installer/Languages/Custom.en.isl`, `Custom.ru.isl`, `Custom.uk.isl`,
  `Custom.fr.isl`, `Custom.he.isl`, `Custom.de.isl`
- Create: `tests/PlanCake.Tests/InstallerMessagesTests.cs`

#### Steps
- [ ] `[Setup]`: add `ChangesAssociations=yes` next to `ChangesEnvironment=yes`, with a comment
      saying Explorer is told to reload the associations.
- [ ] `[Registry]`: add the entries in Technical details → Registry entries, with a comment block
      saying why the extensions' default value is never written (it would take Markdown files
      away from the program the user or Windows picked), and that the user sets the default
      themselves in Default apps (Settings → Advanced opens it). Keep the `PATH` entry as it is.
- [ ] Add `MarkdownFileType` (the file type's name in Explorer, e.g. "Markdown document") to
      `[CustomMessages]` in all six `Custom.<code>.isl` files, each with a `;` comment like the
      others. Keep the files UTF-8 with a BOM: check the first three bytes are `EF BB BF` after
      editing. Use the terms Windows uses in each language for "Markdown document" (keep
      "Markdown" as is).
- [ ] `InstallerMessagesTests`: every `Custom.<code>.isl` has the same `[CustomMessages]` keys as
      `Custom.en.isl`, and starts with a UTF-8 BOM; every `{cm:Key}` and `CustomMessage('Key')`
      in `plancake.iss` (except Inno Setup's built-in messages: `CreateDesktopIcon`,
      `AdditionalIcons`, `UninstallProgram`, `LaunchProgram`, and any other the script uses
      that `Default.isl` defines) is a key in `Custom.en.isl`.
- [ ] Task 1's parity test passes now: remove its "until Task 2" comment.
- [ ] Validation commands pass, including `./installer/Build-Installer.ps1 -AllowDirty`, which
      compiles the `.iss` (only the compile; do not run the installer).

### Task 3: Add the Default apps button to Settings → Advanced

#### Files
- Modify: `src/PlanCake/Ui/SettingsDialog.Designer.cs`
- Modify: `src/PlanCake/Ui/SettingsDialog.cs`
- Modify: `tests/PlanCake.Tests/SettingsDialogTests.cs`

#### Steps
- [ ] Designer: `advancedLayout` gets two more AutoSize rows after `convertToUtf8CheckBox` and
      before the filler, so it has 4 rows:
  - `defaultAppLabel`, a `Ui/WrappingLabel` (it wraps), with the text: "Windows lets only you
    choose which program opens Markdown files. This opens PlanCake's page in Windows Settings,
    where you can choose PlanCake for .md and .markdown files." Give it a top margin that sets
    it apart from the check box (e.g. 16).
  - `defaultAppButton`, with the text "Open &Markdown files with PlanCake..." and the same
    sizing as the About dialog's buttons (`AutoSize = true`, `MinimumSize = new Size(100, 0)`,
    no `MaximumSize`), anchored left.

  Set the tab order after `convertToUtf8CheckBox`. `M` is free in this dialog's English
  mnemonics.
- [ ] `SettingsDialog.cs`: in the constructor, after the existing setup, set both controls'
      `Visible` to
      `DefaultApps.ShowsSettingsButton(App.IsPortable, DefaultApps.IsRegistered())`. Wire
      `defaultAppButton.Click` in the constructor and unwire it in `OnFormClosed`, as
      `AboutDialog` does.
- [ ] The click opens `DefaultApps.SettingsUri` with
      `Process.Start(new ProcessStartInfo(DefaultApps.SettingsUri) { UseShellExecute = true })?.Dispose()`.
      On `Win32Exception` or `InvalidOperationException`, log a warning and show
      `DialogHelper.Show(_("Unable to open Windows Settings."), …)` the way `AboutDialog`
      reports a failure. The Settings dialog stays open, and nothing is saved by the button.
- [ ] Tests in `SettingsDialogTests`:
  - the button is a child of `advancedLayout` and not of `buttonLayout`;
  - the label is a `WrappingLabel`;
  - extend the fit-in-every-language check (or add a sibling test) so the button's preferred
    width fits its tab in every language, like the check boxes.

  `MnemonicTests` and `TextDirectionTests` already cover the dialog: run them and fix what they
  report.
- [ ] Validation commands pass.

### Task 4: Translate the new interface strings

#### Files
- Modify: `src/PlanCake/locale/messages.pot`
- Modify: `src/PlanCake/locale/{ru,uk,fr,he,de}/PlanCake.po`

#### Steps
- [ ] Run `./src/PlanCake/locale/scripts/Extract-Strings.ps1`, then
      `Update-Translations.ps1`. Expect three new messages: the label, the button and
      "Unable to open Windows Settings.". Expect line re-wrapping in the catalogs too; it is
      harmless.
- [ ] Translate them in all five catalogs, using `help/glossaries/<code>.json` for the terms
      ("Windows Settings", "Default apps" as Windows names them in that language). Leave no
      entry fuzzy or empty.
- [ ] Give the button a mnemonic in every catalog that no other control in the Settings dialog
      uses in that catalog (`MnemonicTests` checks across all three tabs).
- [ ] Validation commands pass, starting with `Compile-Translations.ps1 -Strict`.

### Task 5: Update the six manuals

#### Files
- Modify: `src/PlanCake/help/{en,ru,uk,fr,he,de}/manual.html`

#### Steps
- [ ] English `#installer` (Installing → With the installer): add a paragraph. The installer
      makes PlanCake one of the programs that can open `.md` and `.markdown` files, so it
      appears in Open with and in Windows Settings → Default apps, but it does not change which
      program opens them. To have PlanCake open them on a double-click, choose it there, or use
      the button in Settings → Advanced (link to `#settings-advanced`). Say that the portable
      version registers nothing, so it is chosen through Open with → Choose another app (in
      `#portable`, one sentence). Say in `#uninstalling` that the uninstaller also removes
      PlanCake from those lists.
- [ ] English `#settings-advanced`: add a `<dt>` with the button's label word for word
      ("Open Markdown files with PlanCake...", without the `&`) and a `<dd>` saying what it
      opens, that the user picks PlanCake for `.md` and `.markdown` there, that on older
      Windows versions it opens the general Default apps page, and that the button is not
      there in the portable version.
- [ ] Carry both changes into the other five manuals in the same places, wording the button
      label exactly as that language's catalog does (Task 4), with the glossary's terms. Add no
      new ids, or add each new id to all six.
- [ ] Validation commands pass (`ManualParityTests` above all).

### Task 6: Update documentation

#### Files
- Modify: `CHANGELOG.md`
- Modify: `README.md`
- Modify: `CLAUDE.md`

#### Steps
- [ ] `CHANGELOG.md`: under `[Unreleased]` → `### Added`, an entry in the style of the existing
      ones: **Open Markdown files with PlanCake**. The installer lists PlanCake among the
      programs that open `.md` and `.markdown` files, and Settings → Advanced opens Windows'
      Default apps page to make it the default.
- [ ] `README.md` "Installing": one sentence that the installer registers PlanCake for Markdown
      files without taking them over, and how to make it the default.
- [ ] `CLAUDE.md` "Installer and releases": a short paragraph on the registration (what is
      written, that the extensions' default value never is, `ChangesAssociations`, the
      uninstaller removing it), that `Utils/DefaultApps.cs` names and the `.iss` must match
      (`DefaultAppsTests` checks), and that `InstallerMessagesTests` keeps the `.isl` keys in
      step. Mention the Settings button under "Adding a language" only if the new `.isl` key
      needs remembering (it does: a new language's `Custom.<code>.isl` needs
      `MarkdownFileType`).
- [ ] Validation commands pass, including `./installer/Build-Installer.ps1 -AllowDirty` once
      more.

## Technical details

### Registry entries

All under `HKLM` (the installer is per machine, `PrivilegesRequired=admin`), 64-bit view
(`ArchitecturesInstallIn64BitMode`). `{app}` is the install folder.

| Key | Value | Data | Flags |
| --- | --- | --- | --- |
| `Software\Classes\PlanCake.Markdown` | (default) | `{cm:MarkdownFileType}` | `uninsdeletekey` |
| `Software\Classes\PlanCake.Markdown\DefaultIcon` | (default) | `{app}\plancake.exe,0` | |
| `Software\Classes\PlanCake.Markdown\shell\open\command` | (default) | `"{app}\plancake.exe" "%1"` | |
| `Software\Classes\.md\OpenWithProgids` | `PlanCake.Markdown` | empty string | `uninsdeletevalue` |
| `Software\Classes\.markdown\OpenWithProgids` | `PlanCake.Markdown` | empty string | `uninsdeletevalue` |
| `Software\Classes\Applications\plancake.exe` | `FriendlyAppName` | `PlanCake` | `uninsdeletekey` |
| `Software\Classes\Applications\plancake.exe\SupportedTypes` | `.md`, `.markdown` | empty string | |
| `Software\Classes\Applications\plancake.exe\shell\open\command` | (default) | `"{app}\plancake.exe" "%1"` | |
| `Software\Oire\PlanCake\Capabilities` | `ApplicationName` | `PlanCake` | `uninsdeletekey` |
| `Software\Oire\PlanCake\Capabilities` | `ApplicationDescription` | `{cm:AppDescription}` | |
| `Software\Oire\PlanCake\Capabilities\FileAssociations` | `.md`, `.markdown` | `PlanCake.Markdown` | |
| `Software\RegisteredApplications` | `PlanCake` | `Software\Oire\PlanCake\Capabilities` | `uninsdeletevalue` |

Notes:
- **Quoting.** The command quotes both the exe and `"%1"`. Unquoted, a path with spaces splits
  into several arguments, `CliRunner` rejects them as a parse error, and the error goes to a
  console a double-click does not have, so the user sees nothing.
- **What is never written.** The `(default)` value of `Software\Classes\.md` and `.markdown`. And
  no `uninsdeletekey` on `.md`, `.markdown` or `OpenWithProgids`, which other programs share.
- **Inno Setup syntax.** In Inno Setup, an empty `ValueName` is the default value. A value with
  empty data is `ValueType: string; ValueData: ""`. Mark the deepest keys with
  `uninsdeletekey` only at their root, as in the table: deleting the root removes the subkeys.
- **`Software\Oire\PlanCake`.** Check whether anything else lives there. PlanCake keeps its
  settings in `%APPDATA%`, not the registry, so likely nothing does. Even so, delete only
  `Capabilities`, never `Software\Oire` (other Oire programs).

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
  - check that PlanCake is in Open with for both extensions and in Default apps;
  - set it as the default there, double-click a plan whose path has spaces, and double-click it
    again while open (its window comes forward);
  - uninstall, and check the registry keys in the table are gone, `OpenWithProgids` has lost only
    PlanCake's value, and the other program still opens `.md`.

  Repeat the install on Windows 10 if one is at hand.
- **JAWS check, one item at a time.** In Settings → Advanced:
  - the label reads, and the button reads with its name and mnemonic;
  - the button opens PlanCake's page in Default apps, and that page lets the user set `.md` and
    `.markdown` with the keyboard;
  - the button is absent in the portable build (a `userdata` folder next to the exe).

  Check in Hebrew too, for the mirrored layout.
- **Release 1.1** resumes after this merges: the steps in CLAUDE.md → Releasing, with this
  feature added to `changelogs/1.1.0.md`.
- **The old update host.** PlanCake 1.0.0.11 looks for updates at
  `https://plancake.oire.dev/appcast.xml`, which answers 404. Until that host redirects to
  `plan-cake.oire.dev` or serves the appcast, 1.0.0.11 users are never offered 1.1.
