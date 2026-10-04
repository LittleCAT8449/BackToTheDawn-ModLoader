[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [string]$GameDirectory = (Split-Path -Parent $PSScriptRoot),

    [switch]$IncludeLifecycleTests,

    [switch]$IncludeAssetBundleProbe,

    [switch]$IncludeTaskEventExample,

    [switch]$IncludeTaskApiExample,

    [switch]$SkipExampleMod
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
$phoneApiOutputDll = Join-Path $rootDirectory "src\BackToTheDawn.PhoneAPI\bin\$Configuration\net6.0\BackToTheDawn.PhoneAPI.dll"
$shopApiOutputDll = Join-Path $rootDirectory "src\BackToTheDawn.ShopAPI\bin\$Configuration\net6.0\BackToTheDawn.ShopAPI.dll"
$exampleOutputDll = Join-Path $rootDirectory "examples\BackToTheDawn.ExampleMod\bin\$Configuration\net6.0\BackToTheDawn.ExampleMod.dll"
$taskEventExampleDirectory = Join-Path $rootDirectory "examples\BackToTheDawn.TaskEventExample"
$taskEventExampleDeployDirectory = Join-Path $GameDirectory "BepInEx\mods\BackToTheDawn.TaskEventExample"
$taskEventExampleOutputDll = Join-Path $taskEventExampleDirectory "bin\$Configuration\net6.0\BackToTheDawn.TaskEventExample.dll"
$taskApiExampleDirectory = Join-Path $rootDirectory "examples\BackToTheDawn.TaskApiExample"
$taskApiExampleDeployDirectory = Join-Path $GameDirectory "BepInEx\mods\BackToTheDawn.TaskApiExample"
$taskApiExampleOutputDll = Join-Path $taskApiExampleDirectory "bin\$Configuration\net6.0\BackToTheDawn.TaskApiExample.dll"
$lifecycleTestRoot = Join-Path $rootDirectory "tests"
$dependencyTestProject = Join-Path $lifecycleTestRoot "BackToTheDawn.DependencyMod\BackToTheDawn.DependencyMod.csproj"
$failingTestProject = Join-Path $lifecycleTestRoot "BackToTheDawn.FailingMod\BackToTheDawn.FailingMod.csproj"
$assetBundleProbeProject = Join-Path $lifecycleTestRoot "BackToTheDawn.AssetBundleProbe\BackToTheDawn.AssetBundleProbe.csproj"
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

if ($IncludeAssetBundleProbe) {
    & $dotnetExe build $assetBundleProbeProject --configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "The AssetBundle probe Mod failed to build."
    }
}

New-Item -ItemType Directory -Path $loaderPluginDirectory -Force | Out-Null
Copy-Item -LiteralPath $loaderOutputDll,$apiOutputDll,$phoneApiOutputDll,$shopApiOutputDll -Destination $loaderPluginDirectory -Force
Write-Host "Deployed Loader, ModAPI, PhoneAPI, and ShopAPI to $loaderPluginDirectory"

if (-not $SkipExampleMod) {
    New-Item -ItemType Directory -Path $exampleModDirectory -Force | Out-Null
    $exampleResourceItems = Get-ChildItem -LiteralPath $exampleResourceDirectory -Force -ErrorAction SilentlyContinue
    if ($exampleResourceItems) {
        New-Item -ItemType Directory -Path $exampleResourceOutputDirectory -Force | Out-Null
    }

    Copy-Item -LiteralPath $exampleOutputDll,$exampleManifestPath -Destination $exampleModDirectory -Force
    if ($exampleResourceItems) {
        $exampleResourceItems | Copy-Item -Destination $exampleResourceOutputDirectory -Recurse -Force
    }

    Write-Host "Deployed ExampleMod to $exampleModDirectory"
    if ($exampleResourceItems) {
        Write-Host "Deployed ExampleMod resources to $exampleResourceOutputDirectory"
    }
    if (Test-Path -LiteralPath $legacyExamplePluginDirectory) {
        Write-Warning "Legacy ExampleMod directory still exists at '$legacyExamplePluginDirectory'. Remove or move it to avoid duplicate discovery."
    }
}

if ($IncludeTaskEventExample) {
    New-Item -ItemType Directory -Path $taskEventExampleDeployDirectory -Force | Out-Null
    Copy-Item -LiteralPath `
        $taskEventExampleOutputDll, `
        (Join-Path $taskEventExampleDirectory "mod.json"), `
        (Join-Path $taskEventExampleDirectory "README.md") `
        -Destination $taskEventExampleDeployDirectory -Force
    Write-Host "Deployed Task Event Example Mod to $taskEventExampleDeployDirectory"
}

if ($IncludeTaskApiExample) {
    New-Item -ItemType Directory -Path $taskApiExampleDeployDirectory -Force | Out-Null
    Copy-Item -LiteralPath `
        $taskApiExampleOutputDll, `
        (Join-Path $taskApiExampleDirectory "mod.json"), `
        (Join-Path $taskApiExampleDirectory "README.md") `
        -Destination $taskApiExampleDeployDirectory -Force
    Write-Host "Deployed Task API Example Mod to $taskApiExampleDeployDirectory"
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

if ($IncludeAssetBundleProbe) {
    $assetBundleProbeName = "BackToTheDawn.AssetBundleProbe"
    $assetBundleProbeOutput = Join-Path $lifecycleTestRoot "$assetBundleProbeName\bin\$Configuration\net6.0"
    $assetBundleProbeSourceResources = Join-Path $lifecycleTestRoot "$assetBundleProbeName\resource"
    $assetBundleProbeDirectory = Join-Path $modsOutputDirectory $assetBundleProbeName
    New-Item -ItemType Directory -Path $assetBundleProbeDirectory -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $assetBundleProbeOutput ($assetBundleProbeName + ".dll")),(Join-Path $assetBundleProbeOutput "mod.json") -Destination $assetBundleProbeDirectory -Force
    if (Test-Path -LiteralPath $assetBundleProbeSourceResources) {
        $assetBundleProbeResources = Get-ChildItem -LiteralPath $assetBundleProbeSourceResources -Force
        if ($assetBundleProbeResources) {
            $assetBundleProbeOutputResources = Join-Path $assetBundleProbeDirectory "resource"
            New-Item -ItemType Directory -Path $assetBundleProbeOutputResources -Force | Out-Null
            $assetBundleProbeResources | Copy-Item -Destination $assetBundleProbeOutputResources -Recurse -Force
        }
    }
    Write-Host "Deployed AssetBundle probe Mod to $assetBundleProbeDirectory"
}
