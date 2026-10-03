[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z][A-Za-z0-9._-]*$')]
    [string]$Name,

    [string]$Id,

    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+([.-][A-Za-z0-9.-]+)?$')]
    [string]$Version = "0.1.0"
)

$ErrorActionPreference = "Stop"
$rootDirectory = Split-Path -Parent $PSScriptRoot
$examplesDirectory = Join-Path $rootDirectory "examples"
$modDirectory = Join-Path $examplesDirectory $Name
$namespace = $Name -replace '[^A-Za-z0-9_]', '_'
$normalizedId = ($Name -replace '[^A-Za-z0-9]+', '-').ToLowerInvariant()
if ([string]::IsNullOrWhiteSpace($Id)) {
    $Id = "dev.backtothedawn.$normalizedId"
}

if (Test-Path -LiteralPath $modDirectory) {
    $existingItems = Get-ChildItem -LiteralPath $modDirectory -Force
    if ($existingItems) {
        throw "The target directory '$modDirectory' already exists and is not empty."
    }
}

$mainDirectory = Join-Path $modDirectory "src\main"
$resourceDirectory = Join-Path $modDirectory "src\resource"
New-Item -ItemType Directory -Path $mainDirectory,$resourceDirectory -Force | Out-Null

$projectTemplate = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <AssemblyName>__NAME__</AssemblyName>
    <RootNamespace>__NAMESPACE__</RootNamespace>
    <Version>__VERSION__</Version>
    <CopyLocalLockFileAssemblies>false</CopyLocalLockFileAssemblies>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\BackToTheDawn.ModAPI\BackToTheDawn.ModAPI.csproj">
      <Private>false</Private>
    </ProjectReference>
    <ProjectReference Include="..\..\src\BackToTheDawn.PhoneAPI\BackToTheDawn.PhoneAPI.csproj">
      <Private>false</Private>
    </ProjectReference>
  </ItemGroup>

  <ItemGroup>
    <None Include="mod.json" CopyToOutputDirectory="PreserveNewest" />
    <None Include="src\resource\**\*" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
'@
$projectTemplate = $projectTemplate.Replace("__NAME__", $Name)
$projectTemplate = $projectTemplate.Replace("__NAMESPACE__", $namespace)
$projectTemplate = $projectTemplate.Replace("__VERSION__", $Version)

$entryTemplate = @'
using BackToTheDawn.ModAPI;
using BackToTheDawn.PhoneAPI;

namespace __NAMESPACE__;

public sealed class ModEntry : IMod
{
    private readonly List<IDisposable> _subscriptions = new();
    private ModContext? _context;

    public void Initialize(ModContext context)
    {
        _context = context;

        var enabled = context.Config.Get("enabled", true);
        context.Config.Set("configApiVersion", 1);
        context.Config.Save();

        if (enabled)
        {
            _subscriptions.Add(GameEvents.Subscribe<GameplayReadyEvent>(OnGameplayReady));
            _subscriptions.Add(GameEvents.Subscribe<PlayerItemActionEvent>(OnPlayerItemAction));
        }
        context.Logger.Info(
            $"{context.Manifest.Name} initialized through IMod " +
            $"(enabled={enabled}). Config: {context.Config.FilePath}");
    }

    public void Shutdown()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        _context?.Logger.Info("IMod entry shut down.");
        _context = null;
    }

    private void OnGameplayReady(GameplayReadyEvent _)
    {
        _context?.Logger.Info("Gameplay is ready.");
    }

    private void OnPlayerItemAction(PlayerItemActionEvent info)
    {
        _context?.Logger.Info(
            $"Item action: {info.Action}, item={info.ItemKey?.ToString() ?? "<pocket>"}, count={info.Count}, " +
            $"success={info.Succeeded}, source={info.Source}, " +
            $"rawOperationType={info.RawOperationType}.");
    }
}
'@
$entryTemplate = $entryTemplate.Replace("__NAMESPACE__", $namespace)

$manifest = [ordered]@{
    id = $Id
    name = $Name
    version = $Version
    entryAssembly = "$Name.dll"
    entryType = "$namespace.ModEntry"
    dependencies = @("dev.backtothedawn.loader")
} | ConvertTo-Json -Depth 3

[System.IO.File]::WriteAllText(
    (Join-Path $modDirectory "$Name.csproj"),
    $projectTemplate.TrimStart(),
    [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText(
    (Join-Path $mainDirectory "ModEntry.cs"),
    $entryTemplate.TrimStart(),
    [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText(
    (Join-Path $modDirectory "mod.json"),
    $manifest + [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText(
    (Join-Path $resourceDirectory "README.txt"),
    "Place mod-owned assets in this directory." + [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false))

Write-Host "Created Mod '$Name' at $modDirectory"
Write-Host "This is a Loader-owned Mod; it does not need BepInEx plugin attributes."
Write-Host "Next: add the project to BackToTheDawnModLoader.sln and build it."
