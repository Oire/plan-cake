#
# PlanCake installer build script
# Copyright (c) 2026 Oire Software SARL.
#
# Compiles the translations, publishes PlanCake (Release, win-x64, single file) and builds the
# Inno Setup installer from installer\plancake.iss. Also creates the portable ZIP archive (use
# -NoPortable to skip it), optionally a signed appcast.xml for NetSparkle updates (-Appcast),
# and optionally uploads the release files to the download server over SCP (-Deploy, which
# reads installer\deploy.json and implies -Appcast).
#
# A release is built from a clean working tree: the script refuses uncommitted or untracked
# files, which would ship under a committed version number, unless -AllowDirty is given (for a
# trial build). With -Appcast or -Deploy it warns when HEAD does not carry the four-part tag
# vX.Y.Z.N of the version it built (see "Releasing" in CLAUDE.md).
#

[CmdletBinding(PositionalBinding=$false)]
param(
    [switch]$SkipBuild,
    [switch]$Appcast,
    [switch]$NoPortable,
    [switch]$Deploy,
    [switch]$OpenOutput,
    [switch]$AllowDirty,
    [string]$InnoSetupPath = "",
    [string]$BaseUrl = "https://plancake.oire.dev",
    [string]$KeyPath = "",
    [string]$ChangeLog = ""
)

# Script directory and paths
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir
$ProjectDir = Join-Path $RepoRoot "src\PlanCake"
$ProjectPath = Join-Path $ProjectDir "PlanCake.csproj"
$CompileTranslationsPath = Join-Path $ProjectDir "locale\scripts\Compile-Translations.ps1"
$IssPath = Join-Path $ScriptDir "plancake.iss"
$OutputDir = Join-Path $ScriptDir "Output"

# The Release build folder and the publish folder inside it; plancake.iss reads SourcePath from
# the same place.
$BuildOutputPath = Join-Path $ProjectDir "bin\x64\Release"
$PublishOutputPath = Join-Path $BuildOutputPath "publish"

# What ships, in the installer and in the portable zip alike (plancake.iss lists the same set).
# The single-file publish leaves everything but the exe beside it: WebView2Loader.dll (a native
# DLL the bundle cannot hold), web\ (the page WebView2 shows), help\ (the user manual), locale\
# (the compiled .mo catalogs), and LICENSE.txt (the repository's LICENSE, renamed by the .csproj
# so Notepad and Explorer know it as text) and THIRD-PARTY-NOTICES.txt (Help > About > Licenses
# opens them). The publish folder also holds files that do not ship: plancake.pdb, the WebView2
# XML docs and a second WebView2Loader.dll under runtimes\.
$ShippedItems = @("plancake.exe", "WebView2Loader.dll", "web", "help", "locale", "LICENSE.txt", "THIRD-PARTY-NOTICES.txt")

Write-Host "PlanCake Installer Build Script" -ForegroundColor Green
Write-Host "===============================" -ForegroundColor Green
Write-Host ""

# -Deploy implies -Appcast (deploying without an appcast would be pointless)
if ($Deploy) {
    $Appcast = $true
}

# Check if the project file exists
if (!(Test-Path $ProjectPath)) {
    Write-Error "Project file not found at: $ProjectPath"
    exit 1
}

# Find InnoSetup compiler
if ([string]::IsNullOrEmpty($InnoSetupPath)) {
    $PossiblePaths = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
    )

    foreach ($Path in $PossiblePaths) {
        if (Test-Path $Path) {
            $InnoSetupPath = $Path
            break
        }
    }
}

if ([string]::IsNullOrEmpty($InnoSetupPath) -or !(Test-Path $InnoSetupPath)) {
    Write-Error "Inno Setup compiler not found. Please install Inno Setup 6 or specify the path using the -InnoSetupPath parameter."
    Write-Host "Download from: https://jrsoftware.org/isdl.php"
    exit 1
}

Write-Host "Using Inno Setup: $InnoSetupPath" -ForegroundColor Yellow

# A release is built from what is committed: an uncommitted change or an untracked file would
# ship under the version number of the commit, and nobody could rebuild it. -AllowDirty is for
# trial builds that are never released.
$GitStatus = @(& git -C $RepoRoot status --porcelain)
if ($LASTEXITCODE -ne 0) {
    Write-Error "git status failed in $RepoRoot; a release is built from a git working tree."
    exit 1
}

if ($GitStatus.Count -gt 0) {
    if ($AllowDirty) {
        Write-Warning "The working tree has uncommitted or untracked files (-AllowDirty): do not release this build."
    } else {
        Write-Error "The working tree has uncommitted or untracked files. Commit or remove them, or pass -AllowDirty for a trial build:"
        $GitStatus | ForEach-Object { Write-Host "  $_" }
        exit 1
    }
}

# Build the application
if (!$SkipBuild) {
    # Clean the Release build folder so nothing stale reaches the installer
    if (Test-Path $BuildOutputPath) {
        Write-Host "Cleaning release build directory..." -ForegroundColor Yellow
        Remove-Item -Path $BuildOutputPath -Recurse -Force
    }

    # The .mo catalogs are build output (gitignored): without this step the publish has no
    # locale folder and every language silently falls back to English. -Strict fails on fuzzy
    # entries, which msgfmt would leave out of the .mo.
    # Run in a child process of the same PowerShell: its exit code is then the script's own,
    # not whatever the last msgfmt call left in $LASTEXITCODE.
    Write-Host "Compiling translations..." -ForegroundColor Yellow
    $PowerShellPath = (Get-Process -Id $PID).Path
    & $PowerShellPath -NoProfile -ExecutionPolicy Bypass -File $CompileTranslationsPath -Strict

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Translation compilation failed with exit code $LASTEXITCODE"
        exit $LASTEXITCODE
    }

    Write-Host "Publishing PlanCake (Release, win-x64)..." -ForegroundColor Yellow

    try {
        $PublishArgs = @(
            $ProjectPath,
            "--configuration", "Release",
            "--runtime", "win-x64",
            "--output", $PublishOutputPath,
            "--verbosity", "minimal"
        )

        & dotnet publish @PublishArgs

        if ($LASTEXITCODE -ne 0) {
            Write-Error "Publish failed with exit code $LASTEXITCODE"
            exit $LASTEXITCODE
        }

        Write-Host "Publish completed successfully!" -ForegroundColor Green
    } catch {
        Write-Error "Publish failed: $_"
        exit 1
    }
} else {
    Write-Host "Skipping build (using existing binaries)..." -ForegroundColor Yellow
}

# Verify the publish output holds everything that ships
Write-Host "Verifying publish output..." -ForegroundColor Yellow

$RequiredFiles = @(
    "plancake.exe",
    "WebView2Loader.dll",
    "web\index.html",
    "help\en\manual.html",
    "LICENSE.txt",
    "THIRD-PARTY-NOTICES.txt"
)

foreach ($File in $RequiredFiles) {
    $FilePath = Join-Path $PublishOutputPath $File
    if (!(Test-Path $FilePath)) {
        Write-Error "Required file not found: $FilePath"
        exit 1
    }
}

# One compiled catalog per .po source; a missing one means that language ships in English.
$PoCount = @(Get-ChildItem -Path (Join-Path $ProjectDir "locale") -File -Recurse |
    Where-Object { $_.Extension -eq ".po" }).Count
$MoCount = @(Get-ChildItem -Path (Join-Path $PublishOutputPath "locale") -File -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -eq ".mo" }).Count
if ($MoCount -ne $PoCount) {
    Write-Error "Found $MoCount compiled catalogs in the publish output for $PoCount .po files. Run Compile-Translations.ps1 -Strict and publish again."
    exit 1
}

# The translation glossaries are for writing the manual, not for shipping.
if (Test-Path (Join-Path $PublishOutputPath "help\glossaries")) {
    Write-Error "help\glossaries must not be in the publish output: check the help\glossaries exclusion in PlanCake.csproj."
    exit 1
}

# Clean and (re)create output directory
if (Test-Path $OutputDir) {
    Write-Host "Cleaning output directory..." -ForegroundColor Yellow
    Remove-Item -Path "$OutputDir\*" -Recurse -Force
} else {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

# Compile installer
Write-Host "Compiling installer..." -ForegroundColor Yellow
Write-Host "ISS File: $IssPath" -ForegroundColor Gray

try {
    & "$InnoSetupPath" "$IssPath"

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Installer compilation failed with exit code $LASTEXITCODE"
        exit $LASTEXITCODE
    }

    Write-Host "Installer created successfully!" -ForegroundColor Green
} catch {
    Write-Error "Installer compilation failed: $_"
    exit 1
}

# Find the created installer file and extract version
$InstallerFiles = @(Get-ChildItem -Path $OutputDir -Filter "plancake-v*-setup.exe" |
    Sort-Object LastWriteTime -Descending)

if ($InstallerFiles.Count -eq 0) {
    Write-Error "No installer file found in $OutputDir after compilation."
    exit 1
}

$Installer = $InstallerFiles[0]
$FileSize = [math]::Round($Installer.Length / 1MB, 2)

if ($Installer.Name -match 'v([\d.]+)-setup') {
    $Version = $Matches[1]
} else {
    Write-Error "Could not extract version from filename: $($Installer.Name)"
    exit 1
}

Write-Host ""
Write-Host "Installer Details:" -ForegroundColor Green
Write-Host "  File: $($Installer.Name)" -ForegroundColor White
Write-Host "  Size: $FileSize MB" -ForegroundColor White
Write-Host "  Version: $Version" -ForegroundColor White
Write-Host "  Path: $($Installer.FullName)" -ForegroundColor White

# A release is the commit tagged vX.Y.Z.N with the version it builds as (see "Releasing" in
# CLAUDE.md). Only a warning: the tag can be added after a trial of the appcast.
if ($Appcast) {
    $HeadTags = @(& git -C $RepoRoot tag --points-at HEAD)
    if ($HeadTags -notcontains "v$Version") {
        $FourPartTags = @($HeadTags | Where-Object { $_ -match '^v\d+\.\d+\.\d+\.\d+$' })
        if ($FourPartTags.Count -eq 0) {
            Write-Warning "HEAD has no four-part release tag. Tag the release commit v$Version before publishing it."
        } else {
            Write-Warning "HEAD is tagged $($FourPartTags -join ', '), but this build is $Version. The release tag must be v$Version."
        }
    }
}

# Create portable ZIP archive
if (!$NoPortable) {
    Write-Host ""
    Write-Host "Creating portable ZIP archive..." -ForegroundColor Yellow

    # Find 7-Zip
    $SevenZipPath = ""
    $SevenZipCmd = Get-Command 7z -ErrorAction SilentlyContinue

    if ($null -ne $SevenZipCmd) {
        $SevenZipPath = $SevenZipCmd.Source
    } else {
        $PossiblePaths = @(
            "${env:ProgramFiles}\7-Zip\7z.exe",
            "${env:ProgramFiles(x86)}\7-Zip\7z.exe",
            "${env:LOCALAPPDATA}\7-Zip\7z.exe"
        )

        foreach ($Path in $PossiblePaths) {
            if (Test-Path $Path) {
                $SevenZipPath = $Path
                break
            }
        }
    }

    $Use7Zip = ![string]::IsNullOrEmpty($SevenZipPath)
    if ($Use7Zip) {
        Write-Host "Using 7-Zip: $SevenZipPath" -ForegroundColor Yellow
    } else {
        Write-Warning "7-Zip not found. Using .NET's ZipFile (larger file size). Install 7-Zip for better compression."
    }

    $PortableName = "plancake-v$Version-portable"
    $PortableZip = Join-Path $OutputDir "$PortableName.zip"
    $PortableTempDir = Join-Path $env:TEMP $PortableName

    # Clean up any previous temp directory
    if (Test-Path $PortableTempDir) {
        Remove-Item -Path $PortableTempDir -Recurse -Force
    }

    # Copy what ships (the same set as the installer) to the temp directory
    New-Item -ItemType Directory -Path $PortableTempDir -Force | Out-Null
    foreach ($Item in $ShippedItems) {
        Copy-Item -Path (Join-Path $PublishOutputPath $Item) -Destination $PortableTempDir -Recurse
        Write-Host "  Added: $Item" -ForegroundColor Gray
    }

    # An empty userdata folder next to the exe is what turns portable mode on (App.IsPortable):
    # settings, logs and the WebView2 working files then stay in it.
    New-Item -ItemType Directory -Path (Join-Path $PortableTempDir "userdata") -Force | Out-Null
    Write-Host "  Created: userdata/" -ForegroundColor Gray

    # Remove existing ZIP if present
    if (Test-Path $PortableZip) {
        Remove-Item -Path $PortableZip -Force
    }

    # Create ZIP archive: the files at its root, so it unpacks into any folder as is
    if ($Use7Zip) {
        & "$SevenZipPath" a -tzip -mx=9 -mfb=258 -mpass=15 "$PortableZip" "$PortableTempDir\*"

        if ($LASTEXITCODE -ne 0) {
            Write-Error "7-Zip compression failed with exit code $LASTEXITCODE"
            exit $LASTEXITCODE
        }
    } else {
        # Unlike Compress-Archive, ZipFile keeps the empty userdata folder in the archive.
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::CreateFromDirectory(
            $PortableTempDir,
            $PortableZip,
            [System.IO.Compression.CompressionLevel]::Optimal,
            $false
        )
    }

    # Clean up temp directory
    Remove-Item -Path $PortableTempDir -Recurse -Force

    $PortableSize = [math]::Round((Get-Item $PortableZip).Length / 1MB, 2)
    Write-Host ""
    Write-Host "Portable ZIP Details:" -ForegroundColor Green
    Write-Host "  File: $PortableName.zip" -ForegroundColor White
    Write-Host "  Size: $PortableSize MB" -ForegroundColor White
    Write-Host "  Path: $PortableZip" -ForegroundColor White
} else {
    Write-Host ""
    Write-Host "Skipping portable ZIP (-NoPortable specified)..." -ForegroundColor Yellow
}

# Generate appcast if requested
if ($Appcast) {
    Write-Host ""
    Write-Host "Generating appcast..." -ForegroundColor Green
    Write-Host "=================================" -ForegroundColor Green
    Write-Host ""

    # Find key path. keys\ is gitignored: the private key must never be committed.
    if ([string]::IsNullOrEmpty($KeyPath)) {
        $KeyPath = Join-Path $RepoRoot "keys"
    }

    $PrivKeyFile = Join-Path $KeyPath "NetSparkle_Ed25519.priv"
    $PubKeyFile = Join-Path $KeyPath "NetSparkle_Ed25519.pub"

    if (!(Test-Path $PrivKeyFile)) {
        Write-Error "Private key not found at: $PrivKeyFile"
        Write-Host "Generate keys with: netsparkle-generate-appcast --generate-keys --key-path `"$KeyPath`""
        exit 1
    }

    if (!(Test-Path $PubKeyFile)) {
        Write-Error "Public key not found at: $PubKeyFile"
        exit 1
    }

    Write-Host "Using keys from: $KeyPath" -ForegroundColor Yellow

    # Ensure netsparkle-generate-appcast is available
    $AppcastTool = Get-Command netsparkle-generate-appcast -ErrorAction SilentlyContinue
    if ($null -eq $AppcastTool) {
        Write-Error "netsparkle-generate-appcast not found. Install it with:"
        Write-Host "  dotnet tool install --global NetSparkleUpdater.Tools.AppCastGenerator"
        exit 1
    }

    if ([string]::IsNullOrEmpty($ChangeLog)) {
        $ChangeLog = Join-Path $RepoRoot "changelogs"
    }

    Write-Host "Download base URL: $BaseUrl" -ForegroundColor Yellow

    $AppcastArgs = @(
        "--single-file", $Installer.FullName,
        "--key-path", $KeyPath,
        "--appcast-output-directory", $OutputDir,
        "--os", "windows",
        "--base-url", $BaseUrl,
        "--file-version", $Version,
        "--product-name", "PlanCake"
    )

    # Release notes. The generator looks for "<Version>.md" with the full four-part version
    # (1.0.0.7 for the release tagged v1.0.0.7), while changelogs\ is named after the
    # three-part version the cycle opened with (1.0.0.md), since the fourth field is only known
    # once the release commit exists: take a four-part file if there is one, else the
    # three-part one, and hand it to the generator under the name it expects.
    $StagedChangeLogDir = ""
    $ReleaseVersion = ($Version -split '\.')[0..2] -join '.'
    $ChangeLogFile = @(
        (Join-Path $ChangeLog "$Version.md"),
        (Join-Path $ChangeLog "$ReleaseVersion.md")
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1

    if ($null -ne $ChangeLogFile) {
        $StagedChangeLogDir = Join-Path $env:TEMP "plancake-changelog-$Version"
        if (Test-Path $StagedChangeLogDir) {
            Remove-Item -Path $StagedChangeLogDir -Recurse -Force
        }
        New-Item -ItemType Directory -Path $StagedChangeLogDir -Force | Out-Null
        Copy-Item -Path $ChangeLogFile -Destination (Join-Path $StagedChangeLogDir "$Version.md")

        $AppcastArgs += @("--change-log-path", $StagedChangeLogDir)
        Write-Host "Using release notes from: $ChangeLogFile" -ForegroundColor Yellow
    } else {
        Write-Host "No release notes for $Version or $ReleaseVersion in $ChangeLog, skipping them" -ForegroundColor Gray
        Write-Host "  Create per-release files like: changelogs/1.0.0.md" -ForegroundColor Gray
    }

    Write-Host "Generating signed appcast..." -ForegroundColor Yellow

    & netsparkle-generate-appcast @AppcastArgs
    $AppcastExitCode = $LASTEXITCODE

    if (![string]::IsNullOrEmpty($StagedChangeLogDir)) {
        Remove-Item -Path $StagedChangeLogDir -Recurse -Force
    }

    if ($AppcastExitCode -ne 0) {
        Write-Error "Appcast generation failed with exit code $AppcastExitCode"
        exit $AppcastExitCode
    }

    # Verify output
    $AppcastFile = Join-Path $OutputDir "appcast.xml"
    if (!(Test-Path $AppcastFile)) {
        Write-Error "appcast.xml was not created"
        exit 1
    }

    Write-Host ""
    Write-Host "Appcast generated successfully!" -ForegroundColor Green
}

Write-Host ""
Write-Host "Output files:" -ForegroundColor Green

Get-ChildItem -Path $OutputDir | ForEach-Object {
    Write-Host "  $($_.Name)" -ForegroundColor White
}

Write-Host ""
Write-Host "Build completed successfully!" -ForegroundColor Green

# Deploy via SCP if requested
if ($Deploy) {
    Write-Host ""
    Write-Host "Deploying release files..." -ForegroundColor Green
    Write-Host "=================================" -ForegroundColor Green
    Write-Host ""

    # Load deploy configuration (deploy.json is gitignored: it names the SSH host and path)
    $DeployConfigPath = Join-Path $ScriptDir "deploy.json"
    $DeployExamplePath = Join-Path $ScriptDir "deploy.example.json"

    if (!(Test-Path $DeployConfigPath)) {
        Write-Error "Deploy configuration not found at: $DeployConfigPath"
        if (Test-Path $DeployExamplePath) {
            Write-Host "Copy $DeployExamplePath to $DeployConfigPath and fill in your SSH details." -ForegroundColor Yellow
        }
        exit 1
    }

    $DeployConfig = Get-Content $DeployConfigPath -Raw | ConvertFrom-Json

    if ([string]::IsNullOrEmpty($DeployConfig.SshHost) -or [string]::IsNullOrEmpty($DeployConfig.RemotePath)) {
        Write-Error "deploy.json must contain 'SshHost' and 'RemotePath' fields."
        exit 1
    }

    $SshHost = $DeployConfig.SshHost
    $RemotePath = $DeployConfig.RemotePath

    # Collect files to upload
    $FilesToUpload = @($Installer.FullName)

    if (!$NoPortable -and (Test-Path $PortableZip)) {
        $FilesToUpload += $PortableZip
    }

    $AppcastFile = Join-Path $OutputDir "appcast.xml"
    $AppcastSig = Join-Path $OutputDir "appcast.xml.signature"

    if (Test-Path $AppcastFile) {
        $FilesToUpload += $AppcastFile
    }

    if (Test-Path $AppcastSig) {
        $FilesToUpload += $AppcastSig
    }

    Write-Host "Uploading to ${SshHost}:${RemotePath}" -ForegroundColor Yellow
    foreach ($File in $FilesToUpload) {
        Write-Host "  $(Split-Path -Leaf $File)" -ForegroundColor White
    }

    Write-Host ""

    & scp -o BatchMode=yes @FilesToUpload "${SshHost}:${RemotePath}"

    if ($LASTEXITCODE -ne 0) {
        Write-Error "SCP upload failed with exit code $LASTEXITCODE"
        exit $LASTEXITCODE
    }

    Write-Host ""
    Write-Host "Deploy completed successfully!" -ForegroundColor Green
} elseif ($Appcast) {
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Cyan
    $UploadFiles = "$($Installer.Name), appcast.xml, and appcast.xml.signature"
    if (!$NoPortable) {
        $UploadFiles = "$($Installer.Name), $PortableName.zip, appcast.xml, and appcast.xml.signature"
    }
    Write-Host "  Upload $UploadFiles to: $BaseUrl/" -ForegroundColor White
    Write-Host "  Or re-run with -Deploy to upload automatically." -ForegroundColor White
}

if ($OpenOutput) {
    $SelectFile = if ($Appcast) { Join-Path $OutputDir "appcast.xml" } else { $Installer.FullName }
    Start-Process -FilePath "explorer.exe" -ArgumentList "/select,`"$SelectFile`""
}
