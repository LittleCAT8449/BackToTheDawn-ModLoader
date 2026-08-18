# Back To The Dawn Mod Loader

An early, minimal mod-loader prototype for the Windows x64 IL2CPP build of
**Back To The Dawn**. It loads managed Mods through BepInEx and exposes stable
lifecycle, player-state, and item-action events without changing game data.

## Requirements

- Windows x64
- The game installed in this repository directory
- PowerShell 5.1 or newer
- .NET 6 SDK or newer (only needed to build the plugin; a project-local install is supported)

## Quick start

1. Install BepInEx IL2CPP:

   ```powershell
   .\scripts\Install-BepInEx.ps1
   ```

2. Start the game through Steam (App ID `1735700`) once. The first launch may
   take longer while BepInEx generates IL2CPP interop assemblies. Close the game
   after reaching the main menu.

3. If no .NET SDK is installed, install a project-local copy:

   ```powershell
   .\scripts\Install-DotNetSdk.ps1
   ```

4. Build and deploy the prototype plugin:

   ```powershell
   .\scripts\Build-And-Deploy.ps1
   ```

5. Start the game again and inspect `BepInEx\LogOutput.log`. A successful load
   contains `Back To The Dawn Mod Loader v0.1.0 loaded successfully.`

## Create a Mod

Generate a starter Mod project:

```powershell
.\scripts\New-Mod.ps1 -Name MyMod
```

The generated project contains `mod.json`, `src/main/ModEntry.cs`, and
`src/resource/`. Add the generated project to the solution, then build and
deploy it with the normal build script. It is a Loader-owned Mod and does not
need BepInEx plugin attributes.

The loader creates `ModContext` for the `IMod` entry and injects a per-Mod JSON
configuration store. For tests or tools that construct a context manually, the
overload without `ModConfig` uses a `config.json` beside the entry assembly:

```csharp
var manifest = ModManifest.Load(manifestPath);
var context = ModContext.FromAssembly(typeof(ModEntry).Assembly, manifest);
var enabled = context.Config.Get("enabled", true);
context.Config.Set("schemaVersion", 1);
context.Config.Save();
```

When the loader creates the context, the file is stored at
`BepInEx/config/mods/<mod-id>.json`. `Get<T>()` returns typed values and records
missing defaults in memory; call `Save()` to create or update the file.

At runtime the loader scans `BepInEx/mods` first, with the old
`BepInEx/plugins` layout retained as a compatibility fallback. It validates
the entry assembly and dependencies, then creates the manifest's `IMod` entry
type in dependency order. Results are exposed through `ModRegistry` and
`ModRegistryReadyEvent`.

推荐通过统一入口 `ModApi.Items`、`ModApi.Events` 和 `ModApi.Game` 使用公共 API；旧的静态类仍保持兼容。
`ModApi.Game.TryGetInventorySnapshot()` 可读取只读背包快照，`InventoryChangedEvent` 和
`InventoryMovedEvent` 分别表示数量变化与容器移动。
`ModApi.Inventory.TryAdd/TryRemove/TryMove()` 提供受控修改，并返回实际变化量和结构化失败状态。
物品 API 默认使用命名空间键，例如 `backtothedawn:apple`，不会把数字 ID 放进物品定义或物品事件。
只有需要调用游戏底层整数参数时，才显式使用 `ItemIdResolver`；完整运行时目录就绪后可订阅
`ItemCatalogReadyEvent` 和 `ItemRuntimeReadyEvent`，物品效果可通过 `ItemCatalog.GetEffects()` 查询。

新物品建议继承 `Item` 并调用 `item.Register()` 注册；返回的
`ItemRegistrationResult` 可用于读取失败原因，`ItemRegistry.Register(item)` 可用于统一注册。
物品可以用 `ItemResources` 声明 `src/resource` 下的图标、名称和描述文件。运行时注入默认开启，
名称/描述会进入游戏语言字典，PNG/JPG 图标会在背包控件刷新时替换模板图标；缺失资源会安全回退。
真实物品注入由 `Items/EnableRuntimeItemInjection` 控制；关闭后仍可读取虚拟目录，但不会写入游戏
运行时 `c_item` 表。注入可能影响存档，首次测试前请备份存档。
加载器还提供一个开发用 F8 控制台。常用命令包括 `help`、`items [filter]`、
`item get <namespace:path>`、`item inject`、`item give <key> [count]`、
`inventory [add|remove] <key> [count]`、`mods` 和 `state`。
`item register <namespace:path> <name>`
只注册 Mod API 中的虚拟目录项，不会写入存档、背包或游戏的 `c_item` 表；真正可使用的
游戏物品还需要后续接入游戏数据和 UI。

Run the optional lifecycle tests with:

```powershell
.\scripts\Build-And-Deploy.ps1 -IncludeLifecycleTests
```

This temporarily deploys a dependency-order test and an intentionally failing
Mod. Remove those two test directories from `BepInEx/mods` after verification.

## Layout

- `src/BackToTheDawn.ModAPI`: stable public API for third-party mods
- `src/BackToTheDawn.Loader`: BepInEx IL2CPP loader and game adapters
- `examples/BackToTheDawn.ExampleMod`: external ModAPI consumer example
  - `src/main`: C# mod source files
  - `src/resource`: mod-owned assets and data files
- `scripts/Install-BepInEx.ps1`: installs the pinned BepInEx runtime
- `scripts/Build-And-Deploy.ps1`: builds the Loader and deploys Mods into `BepInEx/mods`
- `scripts/New-Mod.ps1`: generates a starter Mod project
- `docs/TECHNICAL.md`: public API, event semantics, lifecycle, and internal Hook mapping
- `docs/API_DEVELOPMENT_REQUIREMENTS.md`: API 需求、阶段计划和验收标准
- `docs/GAME_UNPACKING.md`: game files, IL2CPP metadata, reconstructed signatures, and Hook evidence
- `docs/GAME_SYSTEMS.md`: game systems, data models, runtime evidence, and recommended Hook boundaries
- `docs/ITEM_CATALOG.md`: static ItemID names/values and the runtime `c_item` schema
- `analysis/item-catalog.json`: machine-readable static item catalog
- `docs/ITEM_RUNTIME_CATALOG.md`: live `ConfigData.singleton.item` snapshot and decoded examples
- `analysis/item-runtime-catalog.json`: raw runtime item configuration snapshot
- `analysis/item-catalog-runtime.json`: static IDs merged with runtime entries

每个 Mod 都可以采用相同的结构：

```text
MyMod/
  MyMod.csproj
  src/
    main/       # C# 脚本
    resource/   # 图片、配置、文本等素材
```

构建脚本会把 `src/resource` 复制到：

```text
BepInEx/mods/<YourMod>/resource/
```

加载器不会替 Mod 解释素材格式；Mod 可以使用普通文件 API 读取自己的资源。

## Safety

The prototype does not modify `GameAssembly.dll`, game resources, or save files.
Remove `BepInEx`, `dotnet`, `.tools`, `winhttp.dll`, and `doorstop_config.ini` to uninstall
the runtime. Back up saves before adding future gameplay-changing mods.
