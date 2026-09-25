# Helper function to get the catalog name from the project
# This is shared by all localization scripts

function Get-CatalogName {
    param(
        [string]$ProjectPath = (Join-Path (Split-Path $PSScriptRoot -Parent) "..")
    )

    # The application loads "<App.Name>.mo" (see Utils/Localization.cs), so the catalog name
    # comes from the same constant. AssemblyName is not a substitute: it is "plancake", which
    # names the executable, not the catalog.
    $appFile = Join-Path $ProjectPath "Utils/Constants/App.cs"

    if (Test-Path $appFile) {
        $content = Get-Content -Path $appFile -Raw
        if ($content -match 'const\s+string\s+Name\s*=\s*"([^"]+)"') {
            $catalogName = $Matches[1]
            Write-Host "Found App.Name in ${appFile}: $catalogName" -ForegroundColor Gray
            return $catalogName
        }
    }

    throw "Unable to determine catalog name: no 'const string Name' found in $appFile."
}
