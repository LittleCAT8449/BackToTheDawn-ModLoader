[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$rootDirectory = Split-Path -Parent $PSScriptRoot
$installDirectory = Join-Path $rootDirectory ".tools\dotnet"
$dotnetExe = Join-Path $installDirectory "dotnet.exe"

if (Test-Path -LiteralPath $dotnetExe) {
    Write-Host "A project-local .NET SDK is already installed."
    exit 0
}

$tempScript = Join-Path ([System.IO.Path]::GetTempPath()) ("dotnet-install-" + [guid]::NewGuid() + ".ps1")
try {
    Write-Host "Downloading Microsoft's official .NET installer..."
    Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $tempScript
    & $tempScript -Channel "6.0" -InstallDir $installDirectory -NoPath
    if (-not (Test-Path -LiteralPath $dotnetExe)) {
        throw "The project-local .NET SDK installation did not produce dotnet.exe."
    }
    Write-Host "Project-local .NET SDK installed in $installDirectory"
}
finally {
    if (Test-Path -LiteralPath $tempScript) {
        Remove-Item -LiteralPath $tempScript -Force
    }
}

