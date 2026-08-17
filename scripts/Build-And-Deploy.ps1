[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [string]$GameDirectory = (Split-Path -Parent $PSScriptRoot),

    [switch]$IncludeLifecycleTests
)

$ErrorActionPreference = "Stop"
$rootDirectory = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $rootDirectory "BackToTheDawnModLoader.sln"
$loaderPluginDirectory = Join-Path $GameDirectory "BepInEx\plugins\BackToTheDawn.Loader"
$exampleModDirectory = Join-Path $GameDirectory "BepInEx\mods\BackToTheDawn.ExampleMod"
$legacyExamplePluginDirectory = Join-Path $GameDirectory "BepInEx\plugins\BackToTheDawn.ExampleMod"
$exampleResourceDirectory = Join-Path $rootDirectory "examples\BackToTheDawn.ExampleMod\src\resource"
$exampleResourceOutputDirectory = Join-Path $exampleModDirectory "resource"
$exampleManifestPath = Join-Path $rootDirectory "examples\BackToTheDawn.ExampleMod\mod.json"
$loaderOutputDll = Join-Path $rootDirectory "src\BackToTheDawn.Loader\bin\$Configuration\net6.0\BackToTheDawn.Loader.dll"
$apiOutputDll = Join-Path $rootDirectory "src\BackToTheDawn.ModAPI\bin\$Configuration\net6.0\BackToTheDawn.ModAPI.dll"
$exampleOutputDll = Join-Path $rootDirectory "examples\BackToTheDawn.ExampleMod\bin\$Configuration\net6.0\BackToTheDawn.ExampleMod.dll"
$lifecycleTestRoot = Join-Path $rootDirectory "tests"
$dependencyTestProject = Join-Path $lifecycleTestRoot "BackToTheDawn.DependencyMod\BackToTheDawn.DependencyMod.csproj"
$failingTestProject = Join-Path $lifecycleTestRoot "BackToTheDawn.FailingMod\BackToTheDawn.FailingMod.csproj"
$modsOutputDirectory = Join-Path $GameDirectory "BepInEx\mods"
$env:DOTNET_CLI_HOME = Join-Path $rootDirectory ".tools\dotnet-home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

$localDotnet = Join-Path $rootDirectory ".tools\dotnet\dotnet.exe"
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $localDotnet) {
    $dotnetExe = $localDotnet
}
elseif ($dotnetCommand) {
    $dotnetExe = $dotnetCommand.Source
}
else {
    throw ".NET SDK was not found. Run scripts\Install-DotNetSdk.ps1, then try again."
}

if (-not (Test-Path -LiteralPath (Join-Path $GameDirectory "Back To The Dawn.exe"))) {
    throw "Back To The Dawn.exe was not found in '$GameDirectory'."
}

if (-not (Test-Path -LiteralPath (Join-Path $GameDirectory "BepInEx\core\BepInEx.Core.dll"))) {
    throw "BepInEx is not installed in '$GameDirectory'."
}

& $dotnetExe build $solutionPath --configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "The plugin build failed."
}

if ($IncludeLifecycleTests) {
    & $dotnetExe build $dependencyTestProject --configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "The dependency lifecycle test Mod failed to build."
    }

    & $dotnetExe build $failingTestProject --configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "The failing lifecycle test Mod failed to build."
    }
}

New-Item -ItemType Directory -Path $loaderPluginDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $exampleModDirectory -Force | Out-Null
$exampleResourceItems = Get-ChildItem -LiteralPath $exampleResourceDirectory -Force -ErrorAction SilentlyContinue
if ($exampleResourceItems) {
    New-Item -ItemType Directory -Path $exampleResourceOutputDirectory -Force | Out-Null
}

Copy-Item -LiteralPath $loaderOutputDll,$apiOutputDll -Destination $loaderPluginDirectory -Force
Copy-Item -LiteralPath $exampleOutputDll,$exampleManifestPath -Destination $exampleModDirectory -Force
if ($exampleResourceItems) {
    $exampleResourceItems | Copy-Item -Destination $exampleResourceOutputDirectory -Recurse -Force
}

Write-Host "Deployed Loader and ModAPI to $loaderPluginDirectory"
Write-Host "Deployed ExampleMod to $exampleModDirectory"
if ($exampleResourceItems) {
    Write-Host "Deployed ExampleMod resources to $exampleResourceOutputDirectory"
}
if (Test-Path -LiteralPath $legacyExamplePluginDirectory) {
    Write-Warning "Legacy ExampleMod directory still exists at '$legacyExamplePluginDirectory'. Remove or move it to avoid duplicate discovery."
}

if ($IncludeLifecycleTests) {
    $lifecycleTestDeployments = @(
        @{
            Name = "BackToTheDawn.DependencyMod"
            Output = Join-Path $lifecycleTestRoot "BackToTheDawn.DependencyMod\bin\$Configuration\net6.0"
        },
        @{
            Name = "BackToTheDawn.FailingMod"
            Output = Join-Path $lifecycleTestRoot "BackToTheDawn.FailingMod\bin\$Configuration\net6.0"
        }
    )

    foreach ($testDeployment in $lifecycleTestDeployments) {
        $testDirectory = Join-Path $modsOutputDirectory $testDeployment.Name
        New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $testDeployment.Output ($testDeployment.Name + ".dll")),(Join-Path $testDeployment.Output "mod.json") -Destination $testDirectory -Force
        Write-Host "Deployed lifecycle test Mod to $testDirectory"
    }
}
