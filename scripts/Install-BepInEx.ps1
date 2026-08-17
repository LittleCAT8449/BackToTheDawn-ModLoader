[CmdletBinding()]
param(
    [string]$GameDirectory = (Split-Path -Parent $PSScriptRoot),
    [string]$Version = "6.0.0-pre.2"
)

$ErrorActionPreference = "Stop"
$gameExe = Join-Path $GameDirectory "Back To The Dawn.exe"
if (-not (Test-Path -LiteralPath $gameExe)) {
    throw "Back To The Dawn.exe was not found in '$GameDirectory'."
}

if (Test-Path -LiteralPath (Join-Path $GameDirectory "BepInEx\core\BepInEx.Core.dll")) {
    Write-Host "BepInEx is already installed. Nothing changed."
    exit 0
}

$archiveName = "BepInEx-Unity.IL2CPP-win-x64-$Version.zip"
$downloadUrl = "https://github.com/BepInEx/BepInEx/releases/download/v$Version/$archiveName"
$tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("back-to-the-dawn-loader-" + [guid]::NewGuid())
$archivePath = Join-Path $tempDirectory $archiveName

New-Item -ItemType Directory -Path $tempDirectory | Out-Null
try {
    Write-Host "Downloading $archiveName..."
    Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath
    Write-Host "Installing BepInEx into $GameDirectory..."
    Expand-Archive -LiteralPath $archivePath -DestinationPath $GameDirectory -Force
    Write-Host "BepInEx installed. Start the game once to generate IL2CPP interop assemblies."
}
finally {
    if (Test-Path -LiteralPath $tempDirectory) {
        Remove-Item -LiteralPath $tempDirectory -Recurse -Force
    }
}

