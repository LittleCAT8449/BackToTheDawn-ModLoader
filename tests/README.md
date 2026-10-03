# ModHost lifecycle tests

These two projects are not deployed by default.

- `BackToTheDawn.DependencyMod` depends on ExampleMod and verifies dependency order.
- `BackToTheDawn.FailingMod` throws from `Initialize()` and verifies failure isolation.

Deploy them temporarily with:

```powershell
.\scripts\Build-And-Deploy.ps1 -IncludeLifecycleTests
```

## Mod resource API tests

`BackToTheDawn.ModAPI.ResourceTests` exercises the mod-root/reparse-point path
boundary, missing-file results, typed result handling, bundle cache/unload
contracts, and shutdown cleanup forwarding without launching Unity. Run it with:

```powershell
dotnet run --project .\tests\BackToTheDawn.ModAPI.ResourceTests
```

These tests use a fake bundle provider; they do not replace an in-game test
with a Unity 2020.3-compatible AssetBundle and Prefab.

The optional `BackToTheDawn.AssetBundleProbe` Mod can be deployed without
enabling the regular ExampleMod using:

```powershell
.\scripts\Build-And-Deploy.ps1 -GameDirectory "<Steam game install folder>" -SkipExampleMod -IncludeAssetBundleProbe
```

It verifies rejected traversal and missing-bundle results automatically. To
exercise real Unity loading, place a compatible bundle at
`BepInEx/mods/BackToTheDawn.AssetBundleProbe/resource/probe/test.bundle`; it
must contain a Prefab named `ResourceProbePrefab`. Restart the game through
Steam after adding the file. The probe only loads the Prefab reference, does
not instantiate it, and unloads the bundle afterward.

To build a disposable cube Prefab and compatible bundle, copy
`tests/BackToTheDawn.AssetBundleProbe/Editor/BuildProbeBundle.cs` into an
existing Unity 2020.3 project's `Assets/Editor` folder. In Unity, run
`Tools > Back To The Dawn > Build AssetBundle Probe`, then select the deployed
probe Mod's `resource` folder in the folder picker. The builder targets
Windows x64 and checks the editor version; Unity 6 is intentionally rejected.
