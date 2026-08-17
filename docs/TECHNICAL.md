# Back To The Dawn Mod Loader — Technical Reference

本文档对应加载器 `0.1.0`，面向加载器维护者和 Mod 作者。

游戏系统、数据模型和解包证据的完整分析见 [`GAME_SYSTEMS.md`](GAME_SYSTEMS.md)；静态物品 ID 与 `c_item` 字段目录见 [`ITEM_CATALOG.md`](ITEM_CATALOG.md)。

## 运行环境

| 项目 | 当前值 |
|---|---|
| 游戏 | Back To The Dawn / 动物迷城 |
| 平台 | Windows x64 |
| Unity | 2020.3.2f1c1 |
| 脚本后端 | IL2CPP，Metadata 27.1 |
| 底层加载器 | BepInEx 6 IL2CPP |
| Hook 框架 | HarmonyX |
| 插件目标框架 | .NET 6 |

加载顺序：

```text
Steam
  -> Back To The Dawn.exe
  -> BepInEx IL2CPP
  -> BackToTheDawn.Loader.dll
  -> Harmony 生命周期适配器
  -> GameEvents 公共 API
  -> 第三方 Mod
```

游戏主要使用一个名为 `Game` 的 Unity 场景。主菜单、读档和正式游戏不是通过场景切换区分，而是由游戏内部状态和 UI 控制。

## 引用加载器

Mod 项目需要引用：

```text
BepInEx/core/BepInEx.Core.dll
BepInEx/core/BepInEx.Unity.IL2CPP.dll
BepInEx/plugins/BackToTheDawn.Loader/BackToTheDawn.ModAPI.dll
```

项目目标框架应为：

```xml
<TargetFramework>net6.0</TargetFramework>
```

公共 API 命名空间：

```csharp
using BackToTheDawn.ModAPI;
```

## Mod 清单与运行时上下文

每个 Mod 根目录应包含一个 `mod.json`：

```json
{
  "id": "dev.backtothedawn.examplemod",
  "name": "Back To The Dawn Example Mod",
  "version": "0.1.0",
  "entryAssembly": "BackToTheDawn.ExampleMod.dll",
  "entryType": "BackToTheDawn.ExampleMod.ExampleModEntry",
  "dependencies": ["dev.backtothedawn.loader"]
}
```

加载器部署时会把它复制到 Mod DLL 所在目录。Mod 可以用 `ModManifest.Load()` 读取它，并通过
`ModContext` 得到自己的目录和资源访问器：

```csharp
var manifest = ModManifest.Load(manifestPath);
var context = ModContext.FromAssembly(typeof(ModEntry).Assembly, manifest);

string resourceRoot = context.Resources.RootDirectory;
bool exists = context.Resources.Exists("config.json");
using Stream stream = context.Resources.OpenRead("config.json");
```

`GetPath()` 只接受相对路径，并拒绝通过 `..` 越出 Mod 的 `resource` 目录。

### ModConfig

Loader 创建 `ModContext` 时会注入独立的 JSON 配置存储：

```text
BepInEx/config/mods/<mod-id>.json
```

Mod 不需要引用 BepInEx 配置类型即可读写自己的配置：

```csharp
public void Initialize(ModContext context)
{
    bool enabled = context.Config.Get("enabled", true);
    int maxHealth = context.Config.Get("maxHealth", 100);

    context.Config.Set("configApiVersion", 1);
    context.Config.Save();
}
```

`Get<T>()` 会把缺失的默认值加入内存；`TryGet<T>()` 只读取已有值；`Set<T>()` 和
`Remove()` 会标记配置为已修改；`Save()` 会自动创建父目录。配置文件缺失时使用空配置，
JSON 损坏时会记录 `LoadError` 并使用默认值。配置值采用 JSON 类型，建议使用简单的字符串、
数字、布尔值和数组/对象。

### ModRegistry

Loader 会在第一个 Unity 帧扫描 `BepInEx/mods` 根目录及其直接子目录中的 `mod.json`，然后按依赖顺序
初始化清单中的 `entryType`。旧的 `BepInEx/plugins` 目录仍作为兼容回退路径，但新 Mod 不再需要 BepInEx
插件属性。

```csharp
bool ready = ModRegistry.IsReady;
IReadOnlyList<ModDescriptor> mods = ModRegistry.DiscoveredMods;
IReadOnlyList<ModRejectedEvent> rejected = ModRegistry.RejectedMods;
```

验证内容包括入口 DLL 是否存在、Mod ID 是否重复，以及清单中的依赖是否已经被发现。
`dev.backtothedawn.loader` 作为内置加载器依赖自动视为已满足。

扫描完成后会触发：

```csharp
GameEvents.Subscribe<ModDiscoveredEvent>(info =>
{
    // info.Mod 是一个通过验证的 ModDescriptor
});

GameEvents.Subscribe<ModRejectedEvent>(info =>
{
    // info.Reason 包含拒绝原因
});

GameEvents.Subscribe<ModRegistryReadyEvent>(info =>
{
    // info.Mods 和 info.Rejected 是本次扫描的完整结果
});
```

扫描发生在首帧，`ModRegistryReadyEvent` 会在所有成功初始化的 `IMod` 都完成后触发。

### IMod 生命周期

清单中的 `entryType` 必须是一个拥有公共无参构造函数的具体 `IMod` 类型：

```csharp
public interface IMod
{
    void Initialize(ModContext context);
    void Shutdown();
}
```

Loader 按依赖顺序调用 `Initialize()`，初始化失败只会禁用当前 Mod；关闭时按相反顺序调用 `Shutdown()`。

```csharp
public sealed class ModEntry : IMod
{
    private readonly List<IDisposable> _subscriptions = new();

    public void Initialize(ModContext context)
    {
        _subscriptions.Add(
            GameEvents.Subscribe<GameplayReadyEvent>(
                _ => context.Logger.Info("Gameplay is ready.")));
    }

    public void Shutdown()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }
}
```

生命周期结果事件包括 `ModInitializedEvent`、`ModInitializationFailedEvent` 和 `ModShutdownEvent`。

LoaderOverlay 会显示当前有效、拒绝和已初始化的 Mod 数量。仓库中的两个可选测试 Mod 可以用以下命令部署：

```powershell
.\scripts\Build-And-Deploy.ps1 -IncludeLifecycleTests
```

`BackToTheDawn.DependencyMod` 依赖 ExampleMod，`BackToTheDawn.FailingMod` 会在初始化时故意抛出异常。
预期结果是依赖 Mod 仍能加载，而失败 Mod 不会阻止其他 Mod。

可以使用以下脚本生成基础项目：

```powershell
.\scripts\New-Mod.ps1 -Name MyMod
```

## 稳定 API

`GameEvents` 是当前提供给 Mod 的公共生命周期 API。Mod 不应依赖底层 `GameManage` 方法或自行重复安装相同 Harmony 补丁。

推荐使用统一的静态订阅入口：

```csharp
IDisposable subscription = GameEvents.Subscribe<MainMenuEnteredEvent>(info =>
{
    // 处理事件
});

subscription.Dispose();
```

`Subscribe<T>()` 返回的 `IDisposable` 由 Mod 自己保存，并在 `Unload()` 中释放。这样不需要分别记住
事件的 `+=` / `-=` 委托。每个事件类型只对应一个语义明确的 `T`；例如存档读取开始和读取方法返回分别是
`ArchiveLoadStartedEvent` 与 `ArchiveLoadInvocationReturnedEvent`。

### ItemCatalog 与 ItemIdResolver

物品的默认公共表示是命名空间键，不返回数字 ID：

```csharp
if (ItemCatalog.TryGet("backtothedawn:apple", out var apple))
{
    context.Logger.Info($"{apple.Key}: {apple.DisplayName}");
}
```

`ItemCatalog.All`、`ItemCatalog.TryGet()` 和物品事件中的 `ItemKey` 都使用
`namespace:path` 格式。静态目录会在 Loader 启动时可用；进入 `GameplayReady` 后，运行时配置会补充
显示名、类型、耐久/堆叠相关字段和参数。完整运行时目录就绪时会触发：

```csharp
_subscriptions.Add(GameEvents.Subscribe<ItemCatalogReadyEvent>(info =>
{
    context.Logger.Info($"Item catalog ready: {info.Count}");
}));
```

只有确实需要调用游戏底层整数参数的方法时，才使用独立的 `ItemIdResolver`：

```csharp
if (ItemIdResolver.TryGetId("backtothedawn:apple", out var rawId))
{
    // rawId 只在这个低层调用边界使用
}
```

物品效果同样通过命名空间键读取：

```csharp
var effects = ItemCatalog.GetEffects("backtothedawn:painkiller");
foreach (var effect in effects)
{
    context.Logger.Info($"{effect.Key}: {effect.Value}, duration={effect.Duration}");
}
```

效果定义默认包含效果键、显示名、动作、数值、百分比标记、持续时间、时间类型和随机参数，
不包含数字效果 ID。只有底层调用确实需要原始效果 ID 时，才使用独立的
`EffectIdResolver`；未知效果会得到确定性的哈希键，并保留原始中文显示名。

当前游戏静态目录中的重复 ID 会保留别名，但反向 ID 查询返回第一个稳定名称作为规范键。

### Mod 物品注册与 LoaderConsole

新物品应继承 `Item`，由基类保存通用元数据，并通过 `Register()` 完成注册：

```csharp
private sealed class DebugTokenItem : Item
{
    public DebugTokenItem(string @namespace)
        : base(
            new ItemKey(@namespace, "debug_token"),
            "调试令牌",
            "custom",
            backgroundDescription: "由 ExampleMod 注册的虚拟物品。")
    {
    }
}

var item = new DebugTokenItem(context.Manifest.Id);
if (!item.Register())
{
    context.Logger.Warn("Item key already exists or uses the game namespace.");
}
```

如果需要统一处理多个物品，也可以调用 `ItemRegistry.Register(item)`；它和实例的
`item.Register()` 使用同一套注册逻辑。`ItemDefinition` 仍保留作为只读目录模型和旧 API
兼容入口，但新建物品不需要手工拼接它。

这一步建立的是 Mod API 的虚拟目录项：它没有数字游戏 ID，不会修改 `ConfigData`、`c_item`、
存档或玩家背包。因此它适合先让 Mod 共享稳定的命名空间标识；要把物品真正放入游戏，
还需要单独实现游戏数据、图标、UI 和背包写入适配。

Loader 自带的开发控制台默认启用，进入场景后按 `F8` 打开。命令如下：

```text
help
items [filter]
item get <namespace:path>
item effects <namespace:path>
item register <namespace:path> <display name>
mods
state
clear
```

控制台只调用上述公共目录、Mod 注册表和只读 `GameContext`；它不是作弊控制台，也不会执行
任意 C# 或直接写入游戏数据。可以在 `BepInEx/config/dev.backtothedawn.loader.cfg` 中将
`Interface/ShowConsole` 设为 `false`，关闭该组件。

### StartupStepChanged

```csharp
public static event Action<int>? StartupStepChanged;
```

使用 `Subscribe<T>()` 时对应的事件数据类型为：

```csharp
public sealed record StartupStepChangedEvent(int Step);
```

游戏启动流程推进时触发，参数为游戏内部步骤编号。

已观察到：

| 步骤 | 当前观察结果 |
|---:|---|
| 1 | 初始启动流程开始 |
| 2 | 主菜单初始化阶段完成 |

步骤编号属于游戏实现细节。Mod 不应假定未来游戏版本只存在步骤 1 和 2。

### MainMenuEntered

```csharp
public static event Action<MainMenuEnteredEvent>? MainMenuEntered;
```

游戏调用主菜单显示入口时触发。

当游戏通过同一内部入口立即启动指定存档时不会触发本事件，避免将快速读档错误识别为进入主菜单。

```csharp
public sealed record MainMenuEnteredEvent(
    bool ShowsInputSelection,
    int ImmediateArchiveId);
```

| 属性 | 含义 |
|---|---|
| `ShowsInputSelection` | 是否显示输入方式选择界面 |
| `ImmediateArchiveId` | 请求立即启动的存档 ID；`0` 通常表示没有 |

### ArchiveLoadStarted

```csharp
public static event Action<ArchiveLoadEvent>? ArchiveLoadStarted;
```

用户选择存档、游戏准备调用存档读取逻辑时触发。

```csharp
public sealed record ArchiveLoadEvent(int ArchiveId);
```

统一订阅入口使用 `ArchiveLoadStartedEvent`：

```csharp
public sealed record ArchiveLoadStartedEvent(int ArchiveId);
```

此时游戏尚未进入可操作状态。

### ArchiveLoadInvocationReturned

```csharp
public static event Action<ArchiveLoadEvent>? ArchiveLoadInvocationReturned;
```

统一订阅入口使用：

```csharp
public sealed record ArchiveLoadInvocationReturnedEvent(int ArchiveId);
```

底层存档读取入口返回时触发。它只表示方法调用已经返回，不保证地图、UI 和角色已经全部初始化完成。

需要等待游戏真正可操作的 Mod 应订阅 `GameplayReady`。

### GameplayReady

```csharp
public static event Action? GameplayReady;
```

统一订阅入口使用：

```csharp
public sealed record GameplayReadyEvent;
```

当前地图已经显示，并且游戏准备恢复玩家控制时触发。这是目前最可靠的“存档加载完成、可以访问游戏运行状态”信号。

### TimeChanged

```csharp
public static event Action<TimeChangedEvent>? TimeChanged;
```

游戏完成一次时间推进并执行时间回调后触发：

```csharp
public sealed record TimeChangedEvent(
    GameTimeSnapshot Previous,
    GameTimeSnapshot Current);
```

### MapChanged

```csharp
public static event Action<MapChangedEvent>? MapChanged;
```

当前地图已经设置并显示后触发：

```csharp
public sealed record MapChangedEvent(
    int PreviousMapId,
    int CurrentMapId,
    string CurrentMapName);
```

### PlayerStateChanged

当主角的 `health`、`mentality`、`satiety`、`energy`、`focus` 或 `money` 发生实际变化时触发：

```csharp
public sealed record PlayerStateChangedEvent(
    PlayerSnapshot Previous,
    PlayerSnapshot Current,
    string Source);
```

`Source` 是加载器确认的高层入口名称，例如 `ThingPackage.ChangeEnergy`。事件只提供不可变快照，
不允许 Mod 直接修改游戏属性；初始化阶段和 NPC 的同类变化会被过滤。

### PlayerItemUsed

主角成功调用物品使用入口后触发，不要求物品一定改变生命、饱食或精力：

```csharp
public sealed record PlayerItemUsedEvent(
    int CharacterId,
    ItemKey ItemKey,
    int UseCount);
```

当前底层入口是 `CharacterAttribute.UseItem(int itemId, int useCount, ThingChangeReason reason)` 的
Postfix。事件只报告主角，NPC 使用物品会被过滤；底层整数 ID 不会出现在默认事件对象中。

### PlayerItemAction

如果 Mod 需要观察“玩家对物品做了什么”，应优先订阅统一的物品操作事件，而不是为每个物品
ID 单独写 Hook：

```csharp
public sealed record PlayerItemActionEvent(
    int CharacterId,
    ItemKey? ItemKey,
    int Count,
    ItemActionKind Action,
    bool Succeeded,
    string Source,
    int RawOperationType = -1);
```

`Action` 当前包含 `Use`、`Arrange`、`Destroy`、`Equip`、`Unequip`、`Move` 和
`OperationSelected`。`Arrange` 这类针对整个口袋的操作使用 `ItemKey = null`；底层菜单兜底 Hook
会填充 `RawOperationType`，用于记录尚未映射的菜单选项。

```csharp
_subscriptions.Add(GameEvents.Subscribe<PlayerItemActionEvent>(info =>
{
    context.Logger.Info(
        $"item action={info.Action}, item={info.ItemKey?.ToString() ?? "<pocket>"}, count={info.Count}, " +
        $"success={info.Succeeded}, source={info.Source}, " +
        $"rawOperation={info.RawOperationType}");
}));
```

当前实现观察 `CharacterAttribute.UseItem`、`WidgetItemMiddleTools` 的整理方法、
`CharacterAttribute.EquipmentItem` / `RemoveEquipmentItem`、装备从 `Equipment` 移入 `Pocket` 的
`ThingPackage.MoveThingPlace`，以及物品摧毁确认方法和 `WidgetItemOperationButton.ClickA`。
`PlayerItemUsedEvent` 仍然保留，适合只关心“使用完成”的旧
Mod；统一事件会同时报告该次 `Use` 操作。

## GameContext 只读状态

`GameContext` 返回普通不可变快照，不向 Mod 暴露游戏内部 IL2CPP 类型。

```csharp
GameStateSnapshot? state = GameContext.Current;

bool ready = GameContext.IsGameplayReady;
int archiveId = GameContext.ArchiveId;
int mapId = GameContext.MapId;
GameTimeSnapshot? time = GameContext.Time;
PlayerSnapshot? player = GameContext.Player;
```

推荐使用一次性一致快照：

```csharp
if (GameContext.TryGetSnapshot(out var state) && state is not null)
{
    // 同一次读取中的时间、地图和玩家数据彼此一致。
}
```

`GameStateSnapshot` 包含：

```csharp
public sealed record GameStateSnapshot(
    bool IsGameplayReady,
    int ArchiveId,
    int MapId,
    string MapName,
    GameTimeSnapshot Time,
    PlayerSnapshot? Player);
```

`GameTimeSnapshot` 包含天数、醒来日、小时、分钟和总分钟数。`PlayerSnapshot` 当前包含角色 ID、生命、心态、饱食、精力、专注及金钱。

## 使用示例

```csharp
using BackToTheDawn.ModAPI;

namespace ExampleMod;

public sealed class ModEntry : IMod
{
    private readonly List<IDisposable> _subscriptions = new();

    public void Initialize(ModContext context)
    {
        _subscriptions.Add(GameEvents.Subscribe<MainMenuEnteredEvent>(info =>
            context.Logger.Info($"Main menu entered; immediate archive: {info.ImmediateArchiveId}")));
        _subscriptions.Add(GameEvents.Subscribe<ArchiveLoadStartedEvent>(info =>
            context.Logger.Info($"Loading archive {info.ArchiveId}")));
        _subscriptions.Add(GameEvents.Subscribe<GameplayReadyEvent>(_ =>
            context.Logger.Info("Gameplay is ready.")));
    }

    public void Shutdown()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }
}
```

## Mod 目录与资源

建议每个 Mod 使用以下目录：

```text
MyMod/
  MyMod.csproj
  src/
    main/       # C# 脚本
    resource/   # Mod 自己的图片、配置、文本等文件
```

`scripts/Build-And-Deploy.ps1` 会把 `mod.json` 和 `src/resource` 原样复制到：

```text
BepInEx/mods/<YourMod>/resource/
```

资源目前只负责随 Mod 部署，不由加载器自动解析。Mod 可以通过
`Path.Combine(Paths.PluginPath, "<YourMod>", "resource", "...")` 读取文件，或在构建时使用自己的资源管理器。

## 事件规则

- 生命周期事件在 Unity 主线程触发。
- 事件按订阅顺序调用。
- 加载器会分别捕获每个订阅者抛出的异常。
- 一个 Mod 的订阅者失败不会阻止其他订阅者执行。
- 使用 `IMod` 时应在 `Shutdown()` 中释放 `Subscribe<T>()` 返回的 `IDisposable`；BepInEx 兼容桥仍可在 `Unload()` 中做自己的清理。
- 事件处理器不应执行长时间阻塞、文件下载或后台等待。
- Unity 对象只能在 Unity 主线程安全访问。

订阅者异常会写入：

```text
BepInEx/LogOutput.log
```

日志前缀为：

```text
[GameEvents]
```

## 底层 Hook 映射

以下内容属于加载器内部实现，不视为稳定 Mod API：

| 公共事件 | 当前底层信号 | Harmony 类型 |
|---|---|---|
| `StartupStepChanged` | `GameManage.StartGameGoToNextStep` | Postfix |
| `MainMenuEntered` | `GameManage.ShowGameStartUI` | Prefix |
| `ArchiveLoadStarted` | `GameManage.ReadArchiveDataAndStartGame` | Prefix |
| `ArchiveLoadInvocationReturned` | `GameManage.ReadArchiveDataAndStartGame` | Postfix |
| `GameplayReady` | `GameManage.ShowCurrentMapAndCanControl` | Postfix |
| `TimeChanged` | `GameProcess.PassMinutes` overloads | Postfix + snapshot deduplication |
| `MapChanged` | `Map.FocusMap` | Postfix + map ID deduplication |
| `PlayerItemAction` | `CharacterAttribute.UseItem` / `EquipmentItem` / `RemoveEquipmentItem`、`ThingPackage.MoveThingPlace`、`WidgetItemMiddleTools.ArrangePocketItemList`、物品摧毁确认方法、`WidgetItemOperationButton.ClickA` | Prefix + Postfix |

加载器可在游戏更新后更换底层 Hook，而不改变公共事件的语义。

## 配置

配置文件：

```text
BepInEx/config/dev.backtothedawn.loader.cfg
```

当前配置项：

| 分组 | 配置项 | 默认值 | 说明 |
|---|---|---:|---|
| `General` | `Enabled` | `true` | 启用加载器原型 |
| `Interface` | `ShowStatusOverlay` | `true` | 显示左上角状态面板 |
| `Interface` | `ShowConsole` | `true` | 启用 F8 开发控制台 |
| `Diagnostics` | `EnableRuntimeProbe` | `true` | 场景加载后执行一次对象探针 |
| `Diagnostics` | `EnableLifecycleHooks` | `true` | 安装生命周期 Hook 并提供 `GameEvents` |

`EnableRuntimeProbe` 属于开发诊断功能。完成类型发现后，正式发布版本通常应默认关闭。

当前 Loader 在 `GameplayReady` 后还会执行一次只读物品配置快照，读取
`ConfigData.singleton.item`、`ItemManage.GetItemName()` 和基础分类方法，输出到：

```text
BepInEx/config/dev.backtothedawn.loader.item-runtime-catalog.json
```

快照仅用于分析，不会修改 `c_item`、背包、角色状态或存档。它包含当前运行时的 406 条配置；不同游戏版本可能有不同数量。

## 版本兼容性

当前 Hook 已在以下环境验证：

```text
Unity: 2020.3.2f1c1
Game scene: Game
Steam App ID: 1735700
Game build ID: 23125213
```

游戏更新后应先验证：

1. `Assembly-CSharp.dll` 中仍存在目标类型和方法。
2. Harmony 补丁可以正常安装。
3. 生命周期事件顺序没有改变。
4. `GameplayReady` 仍代表地图显示且玩家可控制。

不要在未知游戏版本中直接修改存档、物品或角色状态。

## 当前非稳定部分

以下内容暂不属于公共 API：

- `Plugin.Logger`
- `RuntimeObjectProbe`
- `LoaderOverlay`
- `LoaderConsole`
- `GameLifecyclePatches`
- `GameManage` 及其他游戏内部类型
- 运行时对象路径和内部存档 ID 规则

Mod 作者应优先使用 `GameEvents`，避免直接依赖这些实现细节。
