# winforms-template

Template repository for Windows Forms applications at Oire Software.

It is a working, buildable app — not a skeleton. Clone it, rename it, and start writing
features on top of a stack that already has logging, configuration, localization, tests,
versioning and CI wired together.

## What you get

| Area | Choice |
| --- | --- |
| Runtime | .NET 10, `net10.0-windows`, x64 |
| UI | Windows Forms, [Oire.WinForms.NativeControls](https://www.nuget.org/packages/Oire.WinForms.NativeControls) for accessible native controls |
| Logging | Serilog — full logs, a terse pair for bug reports, compact-JSON telemetry |
| Configuration | SharpConfig INI under `%APPDATA%`, or next to the EXE in portable mode |
| Localization | GetText.NET (`.po` / `.mo`), **never `.resx`** |
| Versioning | GitVersion from the tag history — no version literal anywhere |
| Tests | xUnit + [AwesomeAssertions](https://awesomeassertions.org/) (Apache-2.0 fork of FluentAssertions), running on every push |
| CI | GitHub Actions: format check, build, test |

Analyzers run at `latest` with `TreatWarningsAsErrors`, so the build stays clean from day one.

## Layout

```
├── .github/              -- CI workflow, Dependabot, release-notes categories
├── src/WinFormsTemplate/
│   ├── Ui/               -- MainWindow and dialogs
│   ├── Utils/            -- Config, Localization
│   │   └── Constants/    -- App, Logging, ExitCode
│   └── locale/           -- .po catalogs and the gettext scripts
├── tests/WinFormsTemplate.Tests/
├── GitVersion.yml
└── WinFormsTemplate.slnx
```

Single project with folder/namespace separation; namespaces follow folder names
(`Oire.WinFormsTemplate.{Folder}`). Split into more projects only when something is genuinely
reused across applications — at which point it belongs in a NuGet package, not a sibling
project.

## Starting a new application from it

1. Create the new repository from this template on GitHub ("Use this template").
2. Rename, in this order:
   - the directories `src/WinFormsTemplate` and `tests/WinFormsTemplate.Tests`;
   - the two `.csproj` files and `WinFormsTemplate.slnx`, plus the `<ProjectReference>` and
     the project paths inside the `.slnx`;
   - `AssemblyName`, `RootNamespace` and `InternalsVisibleTo` in the `.csproj` files;
   - the `Oire.WinFormsTemplate` namespace across the sources;
   - `App.Name` in `src/.../Utils/Constants/App.cs` — this drives the data folder, the config
     file name and the gettext catalog name, so nothing else needs to know the product name.
3. Set `Product` and `Description` in the `.csproj`, and the catalog name in
   `locale/messages.pot`.
4. Update the `locale/scripts/Compile-Translations.ps1` path in
   `.github/workflows/dotnet.yml`.
5. Delete this section of the README and write the real one.
6. Tag `v1.0.0` when the first release is ready; until then GitVersion reports `1.0.0.<n>`.

## Development

```powershell
dotnet restore
dotnet build
dotnet test
dotnet format            # --verify-no-changes is what CI runs
```

Translations are compiled separately, since `.mo` files are build output and are gitignored:

```powershell
./src/WinFormsTemplate/locale/scripts/Compile-Translations.ps1
```

See `src/WinFormsTemplate/locale/README.md` for the full translation workflow and
`CLAUDE.md` for the conventions this repository expects.

## Conventions worth knowing before the first commit

- **Accessibility is load-bearing.** `TableLayoutPanel` for structure, real `Label` controls
  next to their inputs, and a keyboard path for every mouse interaction. Test with a screen
  reader before calling any UI work done.
- **Line endings are LF everywhere** except `.bat` and `.cmd`; `.gitattributes` enforces it.
- **No version literals.** GitVersion owns `Version`, `FileVersion` and
  `InformationalVersion`.
- **Every user-visible string goes through `_()`** or is set by the designer and translated by
  `Localizer.Localize`.
