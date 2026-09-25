# winforms-template — Repo Conventions

Template repository for Oire Software's Windows Forms applications. .NET 10, Windows x64 only.
Everything here is meant to be copied into a new product repo and renamed; see the README for
the rename checklist.

When adapting this template for a new application, apply every convention below to the new
repository as well — that is the point of the file.

## Accessibility is load-bearing

The primary maintainer is blind and uses JAWS. This is not a "nice to have" — it drives UI
decisions.

- **Layouts:** use `TableLayoutPanel` for form structure, not absolute positioning or
  `FlowLayoutPanel`.
- **Labels:** use real `Label` controls with their `Text` property. **Do not** rely on
  `AccessibleDescription` / `AccessibleName` as a substitute — screen readers associate a
  label with a control best through a visible `Label` next to it.
- **Keyboard alternatives:** every mouse interaction (drag, hover, context menu) must have a
  keyboard equivalent. No exceptions.
- **Native controls:** prefer `Oire.WinForms.NativeControls` over the stock WinForms
  owner-drawn equivalents where it offers one — the native peers are what screen readers
  actually understand.
- **Test with a screen reader** before declaring any UI task done.

## Localization — GetText.NET, not .resx

`.po` / `.mo` catalogs live in `src/WinFormsTemplate/locale/`. **`.resx` files are forbidden**
— if the WinForms designer generates one, delete it.

- **`Localizer.Localize(this, Localization.Catalog)`** in a form's constructor — walks the
  control tree and translates designer-set text.
- **`_("literal")`** for strings built at run time. Import via
  `using static Oire.WinFormsTemplate.Utils.Localization;`. Plurals `_n`, context `_p`,
  both `_pn`.
- **`.mo` files are build output and are gitignored.** CI compiles them from the `.po`
  sources with `Compile-Translations.ps1 -Strict` before `dotnet build`. Skip that and the
  `locale\**\*.mo` copy glob has nothing to copy, and the app silently falls back to English.
- **No fuzzy entries** — `-Strict` fails the build on them, because `msgfmt` leaves a fuzzy
  entry out of the `.mo` and the app then shows English while looking translated.
- **Menu mnemonics must be unique per menu level in every catalog.** A duplicate `&` letter
  within one menu throws at startup, on that locale only.
- Adding a language means updating `<SatelliteResourceLanguages>` *and* the language picker in
  the settings UI. Both are easy to forget.

See `src/WinFormsTemplate/locale/README.md` for the script workflow.

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
src/WinFormsTemplate/
├── Ui/          -- MainWindow and dialogs
├── Utils/       -- Config, Localization
│   └── Constants/  -- App, Logging, ExitCode
└── locale/      -- .po catalogs and gettext scripts
tests/WinFormsTemplate.Tests/
```

Do not split into more projects for organization's sake. Something genuinely shared across
applications belongs in a NuGet package instead.

`Utils/Constants/App.cs` is named `App`, not `Application`, on purpose: with implicit usings
and `UseWindowsForms`, an `Application` class in that namespace shadows
`System.Windows.Forms.Application` in every file that imports it.

## Data locations

`App.DataFolder` resolves to `%APPDATA%\Oire\<AppName>`, or to `userdata/` next to the EXE
when that folder exists (portable mode, detected once at static init). Config files sit at the
root of the data folder; user content goes under the `data/` subfolder, so clearing user data
never takes the settings with it.

## Error handling at startup

`Program.Main` returns an `ExitCode` and installs handlers for `AppDomain.UnhandledException`
and `TaskScheduler.UnobservedTaskException` — without them, an exception on a background
thread kills the process with nothing in the log.

Utility classes log and degrade; they do not show dialogs and do not call `Application.Exit`.
Deciding to stop is `Program`'s job.

## Tests

xUnit + AwesomeAssertions in `tests/WinFormsTemplate.Tests`. The app project grants it
`InternalsVisibleTo`, which is how `Config.OverrideFilePath` lets the config tests write to a
temp directory instead of the developer's real `%APPDATA%`. Static state means tests that
touch `Config` or `Localization` must not run in parallel across classes.

Keep the existing tests when adapting the template: they are cheap, and they fail loudly if a
rename breaks the data-folder layout or the localization fallback.
