[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$OutputDirectory,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$rootDirectory = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $rootDirectory "dist"
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

if (-not $SkipBuild) {
    $localDotnet = Join-Path $rootDirectory ".tools\dotnet\dotnet.exe"
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $localDotnet) {
        $dotnetExe = $localDotnet
    }
    elseif ($dotnetCommand) {
        $dotnetExe = $dotnetCommand.Source
    }
    else {
        throw ".NET SDK was not found. Run scripts\Install-DotNetSdk.ps1 first."
    }

    $env:DOTNET_CLI_HOME = Join-Path $rootDirectory ".tools\dotnet-home"
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
    & $dotnetExe build (Join-Path $rootDirectory "src\BackToTheDawn.Loader\BackToTheDawn.Loader.csproj") --configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "The Loader build failed."
    }
}

$dllNames = @("BackToTheDawn.Loader", "BackToTheDawn.ModAPI", "BackToTheDawn.PhoneAPI", "BackToTheDawn.ShopAPI")
$sourceFiles = @{}
foreach ($name in $dllNames) {
    $source = Join-Path $rootDirectory "src\$name\bin\$Configuration\net6.0\$name.dll"
    if (-not (Test-Path -LiteralPath $source)) {
        throw "Built DLL is missing: $source"
    }
    $sourceFiles[$name] = $source
}

$version = [Reflection.AssemblyName]::GetAssemblyName($sourceFiles["BackToTheDawn.Loader"]).Version.ToString(3)
$stamp = Get-Date -Format "yyyyMMdd-HHmmss-fff"
$packageName = "BackToTheDawn.ModLoader-$version-$stamp"
$stageDirectory = Join-Path $rootDirectory ".tools\release-stage\$packageName"
$pluginDirectory = Join-Path $stageDirectory "BepInEx\plugins\BackToTheDawn.Loader"
$docDirectory = Join-Path $stageDirectory "docs"
New-Item -ItemType Directory -Path $pluginDirectory | Out-Null
New-Item -ItemType Directory -Path $docDirectory | Out-Null
foreach ($name in $dllNames) {
    Copy-Item -LiteralPath $sourceFiles[$name] -Destination $pluginDirectory
}
$installationText = [IO.File]::ReadAllText((Join-Path $rootDirectory "docs\DISTRIBUTION.md"))
# INSTALL.md is at the package root; its API documents are inside docs/.
$installationText = $installationText -replace '\]\((PHONE_API|PHONE_JSON|SHOP_API|TECHNICAL)\.md\)', '](docs/$1.md)'
[IO.File]::WriteAllText((Join-Path $stageDirectory "INSTALL.md"), $installationText, [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $rootDirectory "docs\PHONE_API.md"),(Join-Path $rootDirectory "docs\PHONE_JSON.md"),(Join-Path $rootDirectory "docs\SHOP_API.md"),(Join-Path $rootDirectory "docs\SHOP_API_STATUS.md"),(Join-Path $rootDirectory "docs\TECHNICAL.md") -Destination $docDirectory
$jsonExampleDirectory = Join-Path $stageDirectory "examples\BackToTheDawn.JsonPhoneMod"
$jsonExampleDialogueDirectory = Join-Path $jsonExampleDirectory "dialogues\shop"
New-Item -ItemType Directory -Path $jsonExampleDialogueDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $rootDirectory "examples\BackToTheDawn.JsonPhoneMod\Manifest.json") -Destination $jsonExampleDirectory
Copy-Item -LiteralPath (Join-Path $rootDirectory "examples\BackToTheDawn.JsonPhoneMod\dialogues\shop\repair.json") -Destination $jsonExampleDialogueDirectory

$manifest = [ordered]@{
    name = "Back To The Dawn Mod Loader"
    version = $version
    configuration = $Configuration
    builtAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    platform = "Windows x64 Unity IL2CPP"
    testedBepInEx = "6.0.0-be.697"
    files = @()
}
foreach ($file in Get-ChildItem -LiteralPath $stageDirectory -File -Recurse | Sort-Object FullName) {
    $relativePath = $file.FullName.Substring($stageDirectory.Length + 1).Replace('\', '/')
    $manifest.files += [ordered]@{
        path = $relativePath
        sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }
}
$utf8 = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $stageDirectory "package.json"), ($manifest | ConvertTo-Json -Depth 5), $utf8)

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$archivePath = Join-Path $OutputDirectory ($packageName + ".zip")
Add-Type -AssemblyName System.IO.Compression.FileSystem
$packageArchive = [IO.Compression.ZipFile]::Open($archivePath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $stageDirectory -File -Recurse | Sort-Object FullName) {
        # Windows PowerShell Compress-Archive writes backslash entry names.
        # Use ZIP's standard slash separators on every PowerShell version.
        $entryPath = $file.FullName.Substring($stageDirectory.Length + 1).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $packageArchive, $file.FullName, $entryPath, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally {
    $packageArchive.Dispose()
}

$archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    foreach ($file in $manifest.files) {
        $entry = $archive.GetEntry($file.path)
        if ($null -eq $entry) {
            throw "ZIP entry is missing: $($file.path)"
        }
        $stream = $entry.Open()
        $hashAlgorithm = [Security.Cryptography.SHA256]::Create()
        try {
            $archiveHash = [BitConverter]::ToString($hashAlgorithm.ComputeHash($stream)).Replace('-', '')
        }
        finally {
            $hashAlgorithm.Dispose()
            $stream.Dispose()
        }
        if ($archiveHash -ne $file.sha256) {
            throw "ZIP entry hash mismatch: $($file.path)"
        }
    }
    Write-Output "Verified ZIP entries: $($manifest.files.Count)"
}
finally {
    $archive.Dispose()
}

$zipHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
[IO.File]::WriteAllText(($archivePath + ".sha256"), "$zipHash  $([IO.Path]::GetFileName($archivePath))" + [Environment]::NewLine, $utf8)
Write-Output "PACKAGE: $archivePath"
Write-Output "SHA256: $zipHash"
