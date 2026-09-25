# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[semantic versioning](https://semver.org/spec/v2.0.0.html) driven by GitVersion: a release is
a `vX.Y.Z` tag, and nothing carries a version literal.

## [Unreleased]

### Added

- Test project (xUnit, AwesomeAssertions, coverlet) covering the configuration round trip and
  the localization fallback, running on every push.
- GitHub Actions workflow: gettext install, translation compile, `dotnet format` check, build
  and test.
- Dependabot for NuGet and Actions, plus release-note categories.
- GitVersion for all version numbers.
- `locale/` scaffolding: the gettext scripts, a POT template and the translation workflow.
- `.gitattributes` normalizing line endings to LF, except in `.bat` and `.cmd`.
- `ExitCode` constants and unhandled-exception logging for background threads and dropped
  tasks.
- Portable mode: a `userdata/` folder next to the EXE moves all user data out of `%APPDATA%`.

### Changed

- Target framework raised to `net10.0-windows`; platform pinned to x64.
- `Directory.Build.props` folded into the project file.
- Solution converted to the XML `.slnx` format and moved to the repository root.
- Packages updated to current versions and aligned with the ones shipping applications use.
- `Config.Load` and `Config.Save` no longer show dialogs or call `Application.Exit` from a
  static utility class: a missing file is written with the defaults and an unreadable one is
  logged and falls back to them, so a bad INI cannot stop the app from starting.
- `Localization` gained `SetLanguage`, a `LanguageChanged` event and a thread-safe catalog,
  so the language can be switched at run time.
- `Utils/Constants/Application.cs` renamed to `App.cs`; the old name shadowed
  `System.Windows.Forms.Application` in every file that imported it.
- Forms moved under `Ui/`, mirroring the namespace layout of shipping applications.
- Copyright year updated to 2026.

### Removed

- The hardcoded `1.0.0.0` version.
- `BaseOutputPath` / `BaseIntermediateOutputPath` overrides pointing at `$(SolutionDir)`.
