# Localization

Translations use [GetText.NET](https://github.com/perpetualKid/GetText.NET): `.po` sources in
this directory, compiled to `.mo` by the scripts in `scripts/`. **`.resx` is not used** — if
the WinForms designer ever generates one, delete it.

`.mo` files are build output. They are gitignored, so a clean checkout has only the `.po`
sources and CI compiles them before `dotnet build`. Without that step the `locale\**\*.mo`
copy glob in the `.csproj` has nothing to copy and the app silently falls back to English.

## Prerequisites

```powershell
winget install mlocati.GetText              # msgfmt, msgmerge
dotnet tool install --global GetText.NET.Extractor
```

## Usage

All scripts live in `scripts/` and take their catalog name from the project's `AssemblyName`,
so nothing needs renaming when the template is adapted for a new app.

| Script | What it does | When to run it |
| --- | --- | --- |
| `Extract-Strings.ps1` | Scans the source for `_()`, `_n()`, `_p()`, `_pn()` and rewrites `messages.pot` | After adding or changing any user-visible string |
| `New-Language.ps1 -Language fr` | Creates `fr/<App>.po` from the template | When adding a language |
| `Update-Translations.ps1` | Merges new strings from `messages.pot` into every existing `.po` | After `Extract-Strings.ps1` |
| `Compile-Translations.ps1` | Compiles every `.po` to `.mo` | Before building, and in CI |

```powershell
cd src/WinFormsTemplate/locale/scripts
./Extract-Strings.ps1
./Update-Translations.ps1
./Compile-Translations.ps1
```

## Rules

- **No fuzzy entries.** `Compile-Translations.ps1 -Strict` (what CI runs) fails on any of
  them. A fuzzy entry is `msgmerge`'s guess, not a translation: `msgfmt` leaves it out of the
  `.mo`, so the app quietly shows English and nobody notices for months.
- **Menu mnemonics must be unique per menu level in every catalog.** A duplicate `&` letter
  within one menu throws at startup on that locale only, so it will not show up in testing
  unless someone runs the app in that language.
- **Designer strings are written in English** and translated at run time by
  `Localizer.Localize(this, Localization.Catalog)` in the form's constructor. Strings built at
  run time go through `_()` instead.
- Add every shipping language to `<SatelliteResourceLanguages>` in the `.csproj` and to the
  language picker in the settings UI. Both are easy to forget.
