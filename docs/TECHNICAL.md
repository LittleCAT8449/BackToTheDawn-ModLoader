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
BepInEx/plugins/BackToTheDawn.Loader/BackToTheDawn.PhoneAPI.dll (phone features only)
BepInEx/plugins/BackToTheDawn.Loader/BackToTheDawn.ShopAPI.dll (shop registration only)
```

项目目标框架应为：

```xml
<TargetFramework>net6.0</TargetFramework>
```

核心公共 API 命名空间：

```csharp
using BackToTheDawn.ModAPI;
```

电话扩展单独发布在 `BackToTheDawn.PhoneAPI.dll` 中，需要电话功能的 Mod 额外引用该 DLL，并使用
`BackToTheDawn.PhoneAPI` 命名空间。通过 `PhoneApi.For(context)` 获取当前 Mod 的 API 实例。

商店注册扩展单独发布在 `BackToTheDawn.ShopAPI.dll` 中，需要注册商店、加货、改价或打开原生商店的 Mod 额外引用该 DLL，并使用 `BackToTheDawn.ShopAPI` 命名空间，通过 `ShopApi.For(context)` 获取 API。核心 ModAPI 中的 `ModApi.Shops` 仍保留只读目录查询；两个 `ShopApi` 位于不同命名空间，C# 项目同时引用二者时可用类型别名指向注册 API。

## Mod 清单与运行时上下文

纯 JSON 电话或商店模组使用 `Manifest.json` 声明 `namespace`，并通过 `isPhoneMod`、`isShopMod` 启用对应数据扫描，无需入口 DLL，格式见 [JSON 电话模组](PHONE_JSON.md) 和 [JSON 商店模组](SHOP_JSON.md)。同一清单可以同时启用两者。它们进入同一发现、依赖排序与卸载流程；`ModManifest.IsJsonPhoneMod` 和 `IsJsonShopMod` 可用于识别，描述符的 `AssemblyPath` 为空，运行上下文由 `ModContext.FromDirectory` 创建。

DLL Mod 根目录应包含一个 `mod.json`：

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

`ModApi` 是推荐的统一入口，包含 `Items`、`Events` 和只读的 `Game` 服务。旧的
`ItemRegistry`、`ItemCatalog`、`ItemIdResolver` 与 `GameEvents` 仍然保留，已有 Mod 无需迁移。
Mod 不应依赖底层 `GameManage` 方法或自行重复安装相同 Harmony 补丁。

```csharp
var registration = ModApi.Items.Register(new DebugTokenItem("examplemod"));
_subscriptions.Add(ModApi.Events.Subscribe<ItemUseAfterEvent>(info =>
{
    context.Logger.Info($"used {info.Context.ItemKey}: {info.Result.Succeeded}");
}));

if (ModApi.Game.TryGetSnapshot(out var state) && state is not null)
{
    context.Logger.Info($"gameplay ready: {ModApi.Game.IsGameplayReady}");
}
```

`ModApi.Items` 集中提供注册、目录/效果查询、行为注册，以及显式的运行时数字 ID 转换。
`ModApi.Events.Subscribe<T>()` 返回可释放的订阅句柄；`ModApi.Game` 只返回稳定快照。
统一入口不改变旧静态 API 的语义，方便逐步迁移。

电话 API 单独构建和部署，支持号码/新对话注册、原生选项与分支跳转、原版台词覆盖、台词和选项选择观察；使用示例见
[`PHONE_API.md`](PHONE_API.md)。

### 简单 GUI API

`ModApi.Gui` 提供基于 Unity `OnGUI` 的轻量面板注册，不向 Mod 暴露 Unity 类型：

```csharp
var panel = ModApi.Gui.RegisterPanel("examplemod.status", gui =>
{
    gui.Label("ExampleMod online");
    if (gui.Button("测试按钮"))
    {
        context.Logger.Info("GUI button clicked");
    }
});

// Mod 关闭时释放
panel.Dispose();
```

当前控件包括 `Label`、`Button`、`Toggle` 和 `TextField`。面板由加载器统一绘制在
屏幕右侧；回调应只执行轻量绘制逻辑，持久化状态由 Mod 自行保存。

### UGUI Canvas API
推荐使用现代的保留模式 API：

```csharp
using var window = ModApi.UI.CreateWindow("examplemod.window", new CanvasOptions(
    Width: 320, Height: 220, Draggable: true));
window.Label("Example window")
      .Image(context.Resources.GetPath("icon.png"), 64, 64)
      .BeginHorizontal()
      .Button("关闭", () => { /* 回调 */ })
      .EndLayout();
```

`CanvasWindow` 会保留控件树并在内容变化时自动刷新；`Dispose()` 会移除窗口。
`ModApi.Canvas` 仍作为兼容别名保留，旧版 `RegisterCanvas` 回调 API 继续可用。

`ModApi.Canvas` 用于创建标准 Unity Canvas，支持图片、横纵布局、按钮回调、拖动和基础样式：

```csharp
var canvas = ModApi.Canvas.RegisterCanvas("examplemod.canvas", ui =>
{
    ui.Label("Example Canvas");
    ui.Image(context.Resources.GetPath("icon.png"), 64, 64);
    ui.BeginHorizontal();
    ui.Button("关闭", () => { /* 回调 */ });
    ui.EndLayout();
}, new CanvasOptions(Draggable: true));

// Mod 关闭时释放
canvas.Dispose();
```

`CanvasStyle` 可配置背景色、文字色、强调色、字号、内边距和间距；图片路径必须位于
模组资源目录或使用绝对路径。按钮点击由加载器轮询处理，以兼容 IL2CPP 环境。

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

启用实验性运行时注入时，还可以订阅 `ItemRuntimeReadyEvent`。该事件只在当前进程第一次
观察到游戏物品表后触发一次；`InjectedCount` 表示本次自动注入的 Mod 物品数：

```csharp
_subscriptions.Add(GameEvents.Subscribe<ItemRuntimeReadyEvent>(info =>
{
    context.Logger.Info(
        $"Item runtime ready: catalog={info.CatalogCount}, " +
        $"injected={info.InjectedCount}, enabled={info.InjectionEnabled}");
}));
```

事件触发后，`ItemIdResolver` 才适合用于需要当前进程数字 ID 的低级调用。数字 ID 仍然只在
本次游戏进程内有效，不能写入 Mod 的稳定存档格式。

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
var result = item.Register();
if (!result.Succeeded)
{
    context.Logger.Warn($"{result.Status}: {result.Message}");
}
```

如果需要统一处理多个物品，也可以调用 `ItemRegistry.Register(item)`；它和实例的
`item.Register()` 使用同一套注册逻辑。`ItemRegistrationResult` 会区分已注册、重复键、
保留游戏命名空间和生命周期回调失败；只需要布尔值时可以调用 `item.TryRegister()`。

物品可以覆盖注册生命周期，用于准备或释放 Mod 自己的运行时资源：

```csharp
protected override void OnRegistered()
{
    // 注册成功后执行一次
}

protected override void OnUnregistered()
{
    // item.Unregister() 后执行一次
}
```

卸载也提供结构化结果：

```csharp
var result = item.UnregisterDetailed();
switch (result.Status)
{
    case ItemUnregistrationStatus.Unregistered:
        break;
    case ItemUnregistrationStatus.RuntimeBound:
        // 已注入 c_item 的物品会保留到游戏进程退出，不能在运行中强行移除。
        context.Logger.Warn(result.Message);
        break;
    default:
        context.Logger.Warn($"{result.Status}: {result.Message}");
        break;
}
```

`RuntimeBound` 是明确的延迟卸载策略：物品一旦获得当前进程的运行时数字 ID，
Loader 不会尝试从游戏的 `c_item` 列表中删除它，以免 UI、背包或存档仍持有该 ID。
游戏重启后会重新建立目录；`CallbackFailed` 表示目录已经移除，但
`OnUnregistered` 回调抛出了异常，`Succeeded` 仍为 `true`。

`ItemDefinition` 仍保留作为只读目录模型和旧 API 兼容入口，但新建物品不需要手工拼接它。

库存格子占用通过 `Item.OccupiesFullGrid` 声明：`false` 表示半格/小格物品，`true` 表示整格
物品。运行时目录会以 `c_itemExtension.IsFullGrid(c_item)` 为准读取，不直接把原始 `volume`
整数暴露给 Mod。

物品资源路径使用 `ItemResources` 声明，并且只能指向 Mod 自己的 `src/resource`：

```csharp
public DebugTokenItem(string @namespace)
    : base(
        new ItemKey(@namespace, "debug_token"),
        "调试令牌",
        resources: new ItemResources(
            iconPath: "items/debug_token/icon.png",
            namePath: "items/debug_token/name.txt",
            descriptionPath: "items/debug_token/description.txt"))
{
}

var iconPath = item.Resources.ResolveIconPath(context.Resources);
```

路径解析会拒绝绝对路径和 `..` 穿越。运行时注入时，`namePath` 和 `descriptionPath`
会按 UTF-8 文本读取并写入游戏语言字典；`iconPath` 支持 PNG/JPG，Loader 会在
`WidgetItem.UpdateImage` 后创建 Unity `Sprite` 并替换物品图像。没有资源文件时会回退到
注册时的 `displayName`/`backgroundDescription`，图标则继续使用游戏模板图标并记录警告。

资源仍然只存在于当前 Mod 的 `src/resource`（部署后为该 Mod 目录下的 `resource`），不会
写入游戏原始资源包。

真实 `c_item` 注入由 `Items/EnableRuntimeItemInjection` 控制，默认值为 `true`。开启后，
Loader 会在 `GameplayReady` 后为每个已注册 Mod 物品分配 `20000+` 的运行时 ID，复制一个
相同半格/整格类型的现有 `c_item` 模板，写入 `ConfigData.item` 和 `ConfigData.dict_item`，
并把物品名称/描述加入运行时语言字典。该过程只改当前进程内存；物品被加入背包并保存后，
才可能影响存档，因此首次测试应使用备份存档。

运行时注入桥接会返回 `ItemInjectionResult`，其中包含 `ItemInjectionStatus`、当前数字 ID
（如果已分配）和可读错误信息。启用注入后，在首次 `ItemRuntimeReadyEvent` 之后注册的新物品
会尝试立即注入；注入关闭时则只进入虚拟目录，需下一次启用注入或使用控制台命令处理。

注入使用的是当前进程内存中的运行时 ID，不会修改原始资源包。图标资源和语言文本会通过
Loader 的运行时桥接接入 UI；物品加入背包并保存后仍可能进入存档，因此首次测试应使用备份存档。

Loader 自带的开发控制台默认启用，进入场景后按 `F8` 打开。命令如下：

```text
help
items [filter]
item get <namespace:path>
item effects <namespace:path>
item register <namespace:path> <display name>
item inject
item give <namespace:path> [count]
inventory [add|remove] <namespace:path> [count]
mods
state
clear
```

`item inject` 和 `item give` 是实验性命令：前者写入当前进程的 `c_item`，后者调用
`ThingPackage.AddItem` 改变当前角色背包；二者都可能在游戏保存后进入存档，测试前应备份。
其他命令只调用公共目录、Mod 注册表和只读 `GameContext`；控制台不会执行任意 C#。可以在 `BepInEx/config/dev.backtothedawn.loader.cfg` 中将
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

### Room API

房间切换已从地图事件中独立封装为 `ModApi.Rooms`。底层仍使用 `Map.FocusMap`，但 Mod 不需要接触 Harmony：

```csharp
var subscription = ModApi.Rooms.Subscribe(info =>
{
    var previous = info.Previous?.Name ?? "<none>";
    var current = info.Current.Name;
    context.Logger.Info($"Room: {previous} -> {current}");
});

var currentRoom = ModApi.Rooms.Current;
```

实验性的运行时克隆接口（不写入存档）：

```csharp
var result = ModApi.Rooms.RegisterClone(
    "examplemod:test_room",
    "backtothedawn:church",
    "教堂副本");
if (result.Succeeded)
    ModApi.Rooms.GoTo("examplemod:test_room");
```

`RoomChangedEvent` 提供 `Previous` 和 `Current` 两个 `RoomSnapshot`，包含稳定的地图 ID 和名称。
当前克隆接口只用于运行时 POC，克隆房间不会持久化到存档。

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

当前底层有两层入口：`ThingPackage.UseThing(Thing, UseThingReason)` /
`UseBatchThing(Thing, int, UseThingReason)` 负责背包实例的消耗，
`CharacterAttribute.UseItem(int itemId, int useCount, ThingChangeReason reason)` 负责角色使用效果。
Loader 会优先在 `ThingPackage` 层派发前置事件，因此取消发生在数量扣除之前；如果游戏代码直接调用
`CharacterAttribute.UseItem`，则由后者的 Prefix 兜底。事件只报告主角，NPC 使用物品会被过滤；底层整数
ID 不会出现在默认事件对象中。

### ItemUseBefore / ItemUseAfter

新 API 在同一个 `UseItem` 入口上提供可取消的前置事件和统一的后置结果：

```csharp
_subscriptions.Add(GameEvents.Subscribe<ItemUseBeforeEvent>(info =>
{
    if (info.Context.ItemKey == new ItemKey("example", "restricted_tool"))
    {
        info.Cancel("当前状态不能使用这个工具。");
    }
}));

_subscriptions.Add(GameEvents.Subscribe<ItemUseAfterEvent>(info =>
{
    context.Logger.Info(
        $"use={info.Context.ItemKey}, source={info.Context.Source}, " +
        $"success={info.Result.Succeeded}, consumed={info.Result.ConsumedCount}, " +
        $"remaining={info.Result.RemainingCount?.ToString() ?? "<unknown>"}");
}));
```

`ItemUseContext` 只包含命名空间物品键、角色 ID、来源、请求数量和可选
`ItemTarget`，不引用 `CharacterAttribute` 或 `Thing`。前置订阅者可以修改
`RequestedCount`/`Target`，也可以调用 `Cancel()`；取消后不会调用游戏原始方法，
并且后置结果的 `Cancelled` 为 `true`、消耗数量为 `0`。

Mod 物品可以注册不依赖游戏程序集的行为：

```csharp
private sealed class ToolBehavior : IItemBehavior
{
    public ItemUseResult Use(ItemUseContext context) =>
        new(true, false, context.RequestedCount, "工具行为已执行");
}

var behaviorResult = ItemBehaviorRegistry.Register(item.Key, new ToolBehavior());
```

注册行为后，Loader 会在前置事件通过时执行该行为并跳过游戏模板物品的原始
`UseThing`/`UseItem`。行为异常会转成 `ExecutionFailed` 结果并记录日志，不会让其他订阅者或游戏进程崩溃。
内置物品没有 Mod 行为时仍走原始游戏逻辑；当前版本在原始方法返回后把请求数量作为
消耗数量，精确背包数量将在后续 Inventory API 中补齐。

### InventoryChanged

库存增删 Hook 在 `ThingPackage` 返回后比较玩家背包中该物品的实际数量，避免把游戏内部
的 `Thing` 暴露给 Mod：

```csharp
_subscriptions.Add(ModApi.Events.Subscribe<InventoryChangedEvent>(info =>
{
    context.Logger.Info(
        $"{info.ItemKey}: {info.Delta:+#;-#;0}, " +
        $"total={info.TotalCount}, source={info.Source}, success={info.Succeeded}");
}));
```

`Delta` 为正表示新增、为负表示减少，`TotalCount` 是操作完成后的当前总数。
`Source` 当前覆盖 `UseThing`、`UseBatchThing`、`AddItem`、`AddItemOneByOne`、`ReduceItem`、
`ReduceThingCount` 和 `RemoveThing`；`Reason` 是游戏原因枚举的稳定字符串，不要求 Mod
引用 firstpass 程序集。使用物品的库存快照与内部扣除 Hook 会自动去重。
事件只观察主角，初始化阶段和 NPC 背包操作会被过滤。库存操作仍由游戏原始方法执行，
该事件不提供修改或取消语义。

### InventorySnapshot 与 InventoryMoved

Mod 可以在 GameplayReady 后读取当前玩家背包的不可变快照：

```csharp
if (ModApi.Game.TryGetInventorySnapshot(out var inventory) && inventory is not null)
{
    var count = inventory.GetCount(new ItemKey("backtothedawn", "painkiller"));
    foreach (var stack in inventory.Items)
    {
        context.Logger.Info(
            $"{stack.ItemKey} x{stack.Count} at {stack.Location.Container}, " +
            $"fullGrid={stack.IsFullGrid}");
    }
}
```

快照中的 `InventoryStack` 只包含命名空间键、数量、容器/格子坐标和占格信息，永远不暴露
`Thing` 或运行时数字 ID。整理、装备、卸下和其他容器移动会触发独立的
`InventoryMovedEvent`；移动不改变总数量，因此不会伪装成 `InventoryChangedEvent`。

### TradeDetected

交易观察事件从游戏的 `ThingChangeReason`、库存增删和 `ThingPackage.ChangeMoney` 入口统一
推断交易类型：

```csharp
_subscriptions.Add(ModApi.Events.Subscribe<TradeDetectedEvent>(info =>
{
    context.Logger.Info(
        $"trade={info.Kind}, leg={info.Leg}, item={info.ItemKey?.ToString() ?? \"<none>\"}, " +
        $"itemDelta={info.ItemDelta}, moneyDelta={info.CurrencyDelta}, " +
        $"disciplineDelta={info.DisciplineDelta}, shopId={info.ShopId?.ToString() ?? \"<none>\"}, " +
        $"shopKey={info.ShopKey?.ToString() ?? \"<none>\"}, " +
         $"phase={info.Phase}, requestedCount={info.RequestedCount}, " +
         $"relationshipDelta={info.RelationshipDelta}, " +
         $"lotteryNumber={info.LotteryNumber ?? \"<none>\"}, " +
         $"reason={info.Reason}, source={info.Source}, direction={info.Direction}, " +
        $"counterparty={info.CounterpartyId?.ToString() ?? \"<none>\"}/" +
        $"{info.CounterpartyName ?? \"<unknown>\"}");
}));
```

交易现在还有统一的事务生命周期事件。`TradeStartedEvent` 在语义入口建立事务时触发，
`TradeCompletedEvent` 在真实库存/货币结算后触发，`TradeFailedEvent` 在入口抛出异常时触发；
三者通过同一个进程内 `TransactionId` 关联：

```csharp
_subscriptions.Add(ModApi.Events.Subscribe<TradeCompletedEvent>(info =>
{
    var tx = info.Transaction;
    context.Logger.Info(
        $"tx={tx.TransactionId}, kind={tx.Kind}, itemDelta={tx.ItemDelta}, " +
        $"currency={tx.Currency}, currencyDelta={tx.CurrencyDelta}, " +
        $"phase={tx.Phase}");
}));
```

`TradeDetectedEvent.ObservationId` 仍表示底层观察记录；需要关联同一笔语义交易时使用
`TradeDetectedEvent.TransactionId` 或生命周期事件中的 `TradeTransaction.TransactionId`。
事务 ID 只在当前游戏进程内有效，不应写入存档或作为永久 ID。

当前 `TradeKind` 可以区分囚犯买卖、普通购买、讨价还价、午餐、帮派/教士/副队长商店、
自动售货机（`ShopId=9`）、屋顶兑换、电视购物、赠送/回礼、生产、彩票、下注、银行、服务购买、
免费领取和特殊兑换。游戏会把自动售货机复用为 `BuyViceCaptainShopGoods` 原因码，Loader
优先使用语义商店 ID，因此不会把自动售货机误报为副队长商店。
囚犯交易使用语义 Hook：`Prefab_OneTransaction.SubmitBuy` 表示玩家从 NPC 处买入，
`Prefab_OneTransaction.DoSell` 表示玩家向 NPC 卖出；`NpcItemSaleLogic.SaleItem` /
`NpcItemBuyLogic.BuyItem` 作为底层逻辑备用入口。语义事件使用 `TradeLegKind.Combined`，
同时给出 `TradeDirection`、`CounterpartyId` 和尽可能解析出的 `CounterpartyName`，
因此模组不需要仅凭 `ThingChangeReason=Buy` 猜测交易对象。
物品和金钱可能分别产生一条事件，因此 `ObservationId` 是观察信号 ID，不是已经关联好的
完整交易 ID；语义交易会在入口方法返回后比较真实库存、金钱和纪律变化并合并成一条事件。
交易事件只读、不可取消；失败事件目前覆盖语义入口抛出的异常，余额不足、售罄和权限限制
等“方法正常返回但未结算”的细分原因仍需针对具体商店补充。

彩票事务的 `LotteryNumber` 是独立字段，不需要从 `Reason` 文本解析。它来自
`UI_BuyLotteryTickets.AddItemLotteryTicket` 的实际发票入口；`ItemKey` 为
`backtothedawn:lottery_ticket`，而彩票点充值等独立金钱变化不会自动并入购票事务。
游戏内部的 `backtothedawn:money` 伪物品变化会被过滤，金钱只通过 `CurrencyDelta` 报告。
纪律/表现通过 `DisciplineDelta` 报告；屋顶交易可以在同一事件中同时携带金钱和纪律变化。
关系值通过 `TradeCurrencyKind.Relationship` 与 `RelationshipDelta` 报告。
玛姬邮寄和帮派商店使用 `TradePhase.OrderPlaced` 记录下单，第二天实际收到物品时再以
对于玛姬和帮派这类延迟商店，扣款/关系值扣除并创建订单后即视为购买完成；模组作者不需要
等待第二天收货，也不会因为游戏内部库存容器不同而重复处理同一笔交易。`TradePhase.Delivered`
仍保留用于兼容旧 API，但当前商店购买事件统一以 `OrderPlaced` 作为完成阶段。
事件只报告主角的变化；物品仍不会暴露 `Thing`、`c_shop` 或物品数字 ID，NPC 的
`CounterpartyId` 仅用于标识语义 Hook 捕获到的交易对象。

拳赛结算提供专用的 `BetSettledEvent`。Loader 不依赖对话框是否打开，而是监听底层
`ThingChangeReason.BoxingBetWin` 奖金到账和 `BoxingBetExchange` 下注券扣除：

```csharp
_subscriptions.Add(ModApi.Events.Subscribe<BetSettledEvent>(info =>
{
    context.Logger.Info(
        $"result={info.Result}, stake={info.Stake}, payout={info.Payout}, " +
        $"target={info.Bet?.TargetName ?? \"<unknown>\"}, " +
        $"odds={info.Bet?.Odds?.ToString() ?? \"<none>\"}");
}));
```

`BetResult.Won` 只在明确收到 `BoxingBetWin` 时发布；`BetResult.Lost` 只在下注券被
`BoxingBetExchange` 撕碎且操作成功后发布。下注券详情中的金额、目标和赔率通过
`TradeBetInfo` 提供，中奖时 `Payout` 为实际到账金额，未中奖时为 `0`。

彩票兑奖提供专用的 `LotteryPrizeCashedEvent`。它会在同一 Unity 帧内的
`PrizeCashed` 票券消耗和奖励变化完成后发布一次，避免模组自行拼接多条库存日志：

```csharp
_subscriptions.Add(GameEvents.Subscribe<LotteryPrizeCashedEvent>(info =>
{
    foreach (var reward in info.Rewards)
    {
        context.Logger.Info(
            $"lottery reward={reward.Kind}, item={reward.ItemKey?.ToString() ?? "<money>"}, " +
            $"delta={reward.Delta}");
    }
}));
```

`LotteryRewardKind` 当前包括 `Money`、`Mentality`、`Item` 和
`TicketConsumed`。未中奖或只有非物品反馈的彩票也会发布事件，奖励列表至少包含
`TicketConsumed`；`LotteryNumber` 只有在兑奖入口能提供票面号码时才会有值。
比赛和球赛属于自动结算流程，继续使用 `BetSettledEvent`，不走彩票兑奖事件。

### Relationship API

关系值通过只读 API 查询，避免模组直接操作游戏内部的 `CharacterAttribute`：

```csharp
foreach (var relationship in ModApi.Relationships.All)
{
    context.Logger.Info(
        $"character={relationship.CharacterId}, name={relationship.CharacterName}, " +
        $"friend={relationship.Friend}, affection={relationship.Affection}, " +
        $"affectionMode={relationship.IsAffectionMode}");
}

var current = ModApi.Relationships.Interactive;
```

`Affection` 是游戏对进入亲情/恋爱模式角色暴露的当前值，`Friend` 是通用关系值；
`IsAffectionMode` 用于判断当前角色是否已经使用亲情值体系。玛姬邮寄、借钱或恢复关系
等操作的资源变化仍通过 `TradeDetectedEvent.RelationshipDelta` 报告，查询 API 只提供
当前快照，不直接修改数值。

### ShopCatalog 与 ShopKey

商店默认使用命名空间键，而不是直接比较游戏数字 ID：

```csharp
if (info.ShopKey == new ShopKey("backtothedawn", "vending_machine"))
{
    // 自动售货机购买
}

foreach (var shop in ModApi.Shops.Catalog)
{
    context.Logger.Info(
        $"{shop.Key}: {shop.DisplayName}; " +
        $"nativeIds={string.Join(\",\", shop.NativeShopIds)}");
}
```

商品配置会在商店 UI 首次加载或购买入口执行时被安全读取，并写入只读的
`ShopGoodsCatalog`。Loader 会在运行时延迟扫描 `c_shop` 的静态配置集合，先建立全量目录，
再通过商店 UI/购买入口做增量补充；如果配置尚未初始化，扫描会自动重试：

```csharp
foreach (var goods in ModApi.Shops.GetGoods(
             new ShopKey("backtothedawn", "vending_machine")))
{
    context.Logger.Info(
        $"item={goods.ItemKey}, price={goods.Price}, " +
        $"currency={goods.PriceCurrency}, stock={goods.Stock}");
}

_subscriptions.Add(GameEvents.Subscribe<ShopGoodsObservedEvent>(info =>
{
    context.Logger.Info(
        $"observed {info.Goods.ShopKey}/{info.Goods.ItemKey}: " +
        $"price={info.Goods.Price}, stock={info.Goods.Stock}");
}));
```

`Price`、`Stock`、日期和分组字段可能因游戏版本或商店类型不存在，缺失时返回
`null`；`NativePriceType` 保留原始价格类型文本。该 API 只读，不会修改商店库存。
当原始价格类型为空时，Loader 会依据 `ShopKey` 使用保守的默认货币：普通商店使用
金钱，玛姬商店使用关系值，帮派商店使用帮派贡献，屋顶商店使用表现分；最终交易事件
仍以实际 `TradeCompletedEvent` 的资源变化为准。

商店注册、加货、调价与打开原生商店界面见[商店注册 API](SHOP_API.md)。

`ShopId` 仍保留为原始兼容字段；`ShopKey` 是 Mod 应使用的稳定身份。
游戏中多个配置 ID 映射到同一逻辑商店时（例如副队长商店的 1、2），它们共享
一个命名空间键。理发店等不使用 `c_shop` 的交易可以只有语义 `ShopKey`，其
`ShopId` 为 `null`。

### InventoryApi

需要修改当前玩家背包时使用受控服务，不要直接调用 `ThingPackage`：

```csharp
var add = ModApi.Inventory.TryAdd(new ItemKey("examplemod", "debug_token"), 1);
var remove = ModApi.Inventory.TryRemove(new ItemKey("backtothedawn", "apple"), 1);
var move = ModApi.Inventory.TryMove(
    new ItemKey("backtothedawn", "apple"),
    new InventoryLocation("Pocket", 1, 0),
    new InventoryLocation("Equipment", 1, 0));
```

服务只允许在 GameplayReady 的游戏主线程调用，返回 `InventoryOperationResult`，其中
`ChangedCount` 和 `RemainingCount` 来自操作前后的真实快照。数量不足、物品未注册、目标
容器无效、空间不足和部分成功都有独立状态；服务不直接向 Mod 暴露 `ThingPackage`、
`Thing` 或数字 ID。底层调用仍会触发对应的 `InventoryChangedEvent`/`InventoryMovedEvent`。

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

`GameTimeSnapshot` 包含天数、醒来日、小时、分钟和总分钟数。`PlayerSnapshot` 当前包含角色 ID、生命、心态、饱食、精力、专注、金钱和纪律/表现。

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
| `TradeDetected` | `Prefab_OneTransaction.SubmitBuy` / `DoSell`、`Prefab_OneGift.DoGive`、`WidgetGiftItemTips.SubmitReceiveGiftBack`、`UI_ShopListUnit.SubmitBuy`、`UI_ShopListUnitRoof.SubmitBuy`、`UI_MailItem.SubmitMailItem`、`UI_GangShopListUnit.Submitbuy`、`NpcItemBuyLogic.BuyItem` / `NpcItemSaleLogic.SaleItem`、库存/金钱/关系值入口 | 语义交易 Prefix/Postfix + 下单/支付结算 + 库存/金钱/纪律/关系值快照 + `ThingChangeReason` 分类 |

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
| `Items` | `EnableRuntimeItemInjection` | `true` | 实验性注入 Mod 物品到运行时 `c_item` |
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
