param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+([-.+][0-9A-Za-z.-]+)?$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$publishDirectory = Join-Path $env:TEMP "PalworldServerManager-$Version-$([Guid]::NewGuid().ToString('N'))"
$releaseDirectory = Join-Path $projectRoot "artifacts\releases\$Version"

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw "Velopack CLI was not found. Install it with: dotnet tool install --global vpk"
}

try {
    dotnet publish (Join-Path $projectRoot 'PalworldServerManager.csproj') `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --output $publishDirectory `
        "-p:Version=$Version"
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
    vpk pack `
        --packId PalworldServerManager `
        --packVersion $Version `
        --packTitle "Palworld Server Manager" `
        --packDir $publishDirectory `
        --mainExe PalworldServerManager.exe `
        --runtime win-x64 `
        --outputDir $releaseDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Velopack packaging failed with exit code $LASTEXITCODE."
    }
}
finally {
    if (Test-Path -LiteralPath $publishDirectory) {
        Remove-Item -LiteralPath $publishDirectory -Recurse -Force
    }
}

Write-Host "Release packages created in $releaseDirectory"
