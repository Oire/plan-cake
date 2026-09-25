# Compile PO files to MO format for use by the application
# This script uses msgfmt to compile .po files to .mo files

param(
    [string]$Language = "",
    # Treat fuzzy entries as an error. A fuzzy entry is msgmerge's guess, not a translation:
    # msgfmt leaves it out of the .mo, so the application silently shows English and nobody
    # notices until someone clears the fuzzies and ships whatever the guess was. CI passes
    # this switch; interactive runs stay warnings so translation work in progress still compiles.
    [switch]$Strict
)

# Keep the native-command contract the same everywhere. GitHub Actions runs pwsh steps with
# $ErrorActionPreference = 'stop', and PowerShell 7.4 turns a non-zero exit code from a native
# command into a terminating error, either of which would abort the loop on the first bad
# catalog instead of reporting all of them.
$ErrorActionPreference = 'Continue'
if (Test-Path Variable:PSNativeCommandUseErrorActionPreference) {
    $PSNativeCommandUseErrorActionPreference = $false
}

# Import the Get-CatalogName function
. (Join-Path $PSScriptRoot "Get-CatalogName.ps1")

# Get the catalog name automatically
$catalogName = Get-CatalogName

# Find msgfmt tool. PATH first, so Chocolatey, winget, MSYS2 and a manual install all work;
# the GnuWin32 locations are the fallback because its installer does not touch PATH.
$msgfmt = (Get-Command "msgfmt" -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1).Source

if (!$msgfmt) {
    # mlocati's build, per-user (winget's default) and machine-wide (what Chocolatey does on
    # CI), then the GnuWin32 locations. A fresh install is on PATH but an already-open shell
    # will not see it until it restarts, so the probe has to cover the disk as well.
    $possiblePaths = @(
        (Join-Path $env:LOCALAPPDATA "Programs\gettext-iconv\bin\msgfmt.exe"),
        "C:\Program Files\gettext-iconv\bin\msgfmt.exe",
        "C:\Program Files (x86)\GnuWin32\bin\msgfmt.exe",
        "C:\Program Files\GnuWin32\bin\msgfmt.exe"
    )

    foreach ($path in $possiblePaths) {
        if (Test-Path $path) {
            $msgfmt = $path
            break
        }
    }
}

if (!$msgfmt) {
    Write-Error "❌ msgfmt not found. Install gettext tools: winget install mlocati.GetText"
    exit 1
}

Write-Host "Using msgfmt: $msgfmt" -ForegroundColor Gray

# Adjust paths to work from scripts subfolder
$LocaleRoot = Split-Path $PSScriptRoot -Parent

# Get languages to compile
$languages = @()
if ($Language) {
    $languages = @($Language)
} else {
    $languages = Get-ChildItem -Directory -Path $LocaleRoot | Where-Object { $_.Name -match '^[a-z]{2}(-[A-Z]{2})?$' } | ForEach-Object { $_.Name }
}

if ($languages.Count -eq 0) {
    Write-Warning "⚠️ No language directories found. Use New-Language.ps1 to create a language first."
    exit 0
}

Write-Host "Compiling translations for languages: $($languages -join ', ')" -ForegroundColor Green

$successCount = 0
$totalCount = 0
$failedLanguages = @()
$fuzzyLanguages = @()
$untranslatedLanguages = @()

foreach ($lang in $languages) {
    $langDir = Join-Path $LocaleRoot $lang
    $poFile = Join-Path $langDir "$catalogName.po"
    $moFile = Join-Path $langDir "$catalogName.mo"

    if (!(Test-Path $poFile)) {
        Write-Warning "⚠️ PO file not found: $poFile. Skipping $lang."
        continue
    }

    $totalCount++

    try {
        Write-Host "Compiling $lang..." -ForegroundColor Yellow

        # --check validates the header, the plural rule and every format specifier, so a
        # catalog that would fail silently at runtime fails here instead. --statistics reports
        # the translated/fuzzy/untranslated counts; both go to stderr, hence the log file.
        $statsLog = Join-Path ([System.IO.Path]::GetTempPath()) "$catalogName-msgfmt-$lang.log"
        & "$msgfmt" --check --statistics -o "$moFile" "$poFile" 2>$statsLog
        $msgfmtExitCode = $LASTEXITCODE

        $stats = if (Test-Path $statsLog) { (Get-Content -Path $statsLog -Raw) } else { "" }
        Remove-Item -Path $statsLog -Force -ErrorAction SilentlyContinue

        if ($msgfmtExitCode -eq 0) {
            $fuzzy = if ($stats -match '(\d+)\s+fuzzy\s+translation') { [int]$Matches[1] } else { 0 }
            $untranslated = if ($stats -match '(\d+)\s+untranslated\s+message') { [int]$Matches[1] } else { 0 }

            # msgfmt writes the statistics sentence and any --check warnings to the same
            # stream. Keep them apart so a header warning is not dressed up as success text.
            $statsLines = ($stats -split "`r?`n") | Where-Object { $_.Trim() }
            $summaryLine = $statsLines | Where-Object { $_ -match 'translated message' } | Select-Object -Last 1
            $otherLines = $statsLines | Where-Object { $_ -notmatch 'translated message' }

            Write-Host "✅ Compiled $lang successfully: $(if ($summaryLine) { $summaryLine.Trim() } else { 'no statistics reported' })" -ForegroundColor Green
            $successCount++

            foreach ($line in $otherLines) {
                Write-Warning "⚠️ ${lang}: $($line.Trim())"
            }

            if ($fuzzy -gt 0) {
                $fuzzyLanguages += "${lang} ($fuzzy)"
                Write-Warning "⚠️ ${lang} has $fuzzy fuzzy entries. They are excluded from the MO file, so the application will show English for them."
            }

            if ($untranslated -gt 0) {
                $untranslatedLanguages += "${lang} ($untranslated)"
                Write-Warning "⚠️ ${lang} has $untranslated untranslated entries."
            }
        } else {
            $failedLanguages += $lang
            Write-Warning "⚠️ msgfmt failed for ${lang}: $($stats.Trim())"
        }
    } catch {
        $failedLanguages += $lang
        Write-Error "❌ Failed to compile ${lang}: $_"
    }
}

Write-Host "Compilation completed: $successCount/$totalCount languages compiled successfully" -ForegroundColor Cyan

if ($fuzzyLanguages.Count -gt 0) {
    Write-Host "Fuzzy entries: $($fuzzyLanguages -join ', ')" -ForegroundColor Yellow
}

if ($untranslatedLanguages.Count -gt 0) {
    Write-Host "Untranslated entries: $($untranslatedLanguages -join ', ')" -ForegroundColor Yellow
}

if ($failedLanguages.Count -gt 0) {
    Write-Error "❌ Compilation failed for: $($failedLanguages -join ', ')"
    exit 1
}

if ($Strict -and $fuzzyLanguages.Count -gt 0) {
    Write-Error "❌ Fuzzy entries present in: $($fuzzyLanguages -join ', '). Resolve them in the PO files: a fuzzy entry ships as English."
    exit 1
}

if ($successCount -gt 0) {
    Write-Host "Next step: Build the project to copy MO files to output directory" -ForegroundColor Gray
    Write-Host "Command: dotnet build -c Release" -ForegroundColor Gray
}
