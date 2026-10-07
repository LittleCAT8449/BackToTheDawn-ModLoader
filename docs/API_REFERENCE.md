# Back To The Dawn Mod Loader API 参考

本文按功能整理当前公开的 ModAPI、PhoneAPI、ShopAPI 与 JSON 注册接口，供 C# 和 JSON 模组作者查阅。

## 目录与分类

| 分类 | 内容 | 章节 |
| --- | --- | --- |
| ModAPI | 模组生命周期、物品、事件、背包、游戏状态、任务等通用接口 | [ModAPI](#mod-api) |
| Task API | 查询、注册、接取和推进任务 | [任务 API](#task-api) |
| JSON 任务 | 任务清单、定义、目标和 C# 调用方式 | [JSON 任务](#task-api-json) |
| PhoneAPI | 电话号码、对话、选项、分支、条件路线和图片 | [C# 接口](#phone-api) · [JSON 格式](#phone-api-json) |
| ShopAPI | 原生商店、商品目录、交易和物品键 | [C# 接口](#shop-api) · [JSON 格式](#shop-api-json) · [状态与目录](#shop-api-status) |
| ItemKey | 游戏物品完整命名空间键表 | [物品键表](#item-keys) |

## ModAPI 通用接口

<a id="mod-api"></a>

### 运行环境

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

### 日志

Mod 可通过 `ModContext.Logger` 写入带有 Mod ID 前缀的 BepInEx 日志：

```csharp
context.Logger.Debug("正在检查自定义数据");
context.Logger.Info("初始化完成");
context.Logger.Warning("找不到可选资源");
context.Logger.Error("注册失败");
```

`Debug` 消息以及 Loader 自身的 Debug 日志由 `BepInEx/config/dev.backtothedawn.loader.cfg` 中的
`[Logging] DebugMode` 控制，默认关闭。`Info`、`Warning` 和 `Error` 不受此开关影响。

### 引用加载器

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

### Mod 清单与运行时上下文

纯 JSON 电话或商店模组使用 `Manifest.json` 声明 `namespace`，并通过 `isPhoneMod`、`isShopMod` 启用对应数据扫描，无需入口 DLL，格式见 [JSON 电话模组](API_REFERENCE.md#phone-api-json) 和 [JSON 商店模组](API_REFERENCE.md#shop-api-json)。同一清单可以同时启用两者。它们进入同一发现、依赖排序与卸载流程；`ModManifest.IsJsonPhoneMod` 和 `IsJsonShopMod` 可用于识别，描述符的 `AssemblyPath` 为空，运行上下文由 `ModContext.FromDirectory` 创建。

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

#### ModConfig

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

#### ModRegistry

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

#### IMod 生命周期

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

### 稳定 API

`ModApi` 是推荐的统一入口，包含 `Items`、`Events`、只读快照服务 `Game` 和 `Tasks`。旧的
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
`ModApi.Events.Subscribe<T>()` 返回可释放的订阅句柄；`ModApi.Game` 和 `ModApi.Tasks` 提供稳定快照。需要注册任务时，用 `TaskApi.For(context)` 获取该模组作用域下的写入接口。
统一入口不改变旧静态 API 的语义，方便逐步迁移。

电话 API 单独构建和部署，支持号码/新对话注册、原生选项与分支跳转、原版台词覆盖、台词和选项选择观察；使用示例见
[PhoneAPI C# 接口](#phone-api)。

#### 简单 GUI API

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

#### UGUI Canvas API
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

#### ItemCatalog 与 ItemIdResolver

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

#### Mod 物品注册与 LoaderConsole

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

#### StartupStepChanged

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

#### MainMenuEntered

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

#### ArchiveLoadStarted

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

#### ArchiveLoadInvocationReturned

```csharp
public static event Action<ArchiveLoadEvent>? ArchiveLoadInvocationReturned;
```

统一订阅入口使用：

```csharp
public sealed record ArchiveLoadInvocationReturnedEvent(int ArchiveId);
```

底层存档读取入口返回时触发。它只表示方法调用已经返回，不保证地图、UI 和角色已经全部初始化完成。

需要等待游戏真正可操作的 Mod 应订阅 `GameplayReady`。

#### GameplayReady

```csharp
public static event Action? GameplayReady;
```

统一订阅入口使用：

```csharp
public sealed record GameplayReadyEvent;
```

当前地图已经显示，并且游戏准备恢复玩家控制时触发。这是目前最可靠的“存档加载完成、可以访问游戏运行状态”信号。

#### TimeChanged

```csharp
public static event Action<TimeChangedEvent>? TimeChanged;
```

游戏完成一次时间推进并执行时间回调后触发：

```csharp
public sealed record TimeChangedEvent(
    GameTimeSnapshot Previous,
    GameTimeSnapshot Current);
```

#### MapChanged

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

#### Room API

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

#### PlayerStateChanged

当主角的 `health`、`mentality`、`satiety`、`energy`、`focus` 或 `money` 发生实际变化时触发：

```csharp
public sealed record PlayerStateChangedEvent(
    PlayerSnapshot Previous,
    PlayerSnapshot Current,
    string Source);
```

`Source` 是加载器确认的高层入口名称，例如 `ThingPackage.ChangeEnergy`。事件只提供不可变快照，
不允许 Mod 直接修改游戏属性；初始化阶段和 NPC 的同类变化会被过滤。

#### PlayerItemUsed

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

#### ItemUseBefore / ItemUseAfter

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

#### InventoryChanged

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

#### InventorySnapshot 与 InventoryMoved

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

#### TradeDetected

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

#### Relationship API

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

#### ShopCatalog 与 ShopKey

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

商店注册、加货、调价与打开原生商店界面见[商店注册 API](API_REFERENCE.md#shop-api)。

`ShopId` 仍保留为原始兼容字段；`ShopKey` 是 Mod 应使用的稳定身份。
游戏中多个配置 ID 映射到同一逻辑商店时（例如副队长商店的 1、2），它们共享
一个命名空间键。理发店等不使用 `c_shop` 的交易可以只有语义 `ShopKey`，其
`ShopId` 为 `null`。

#### InventoryApi

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

#### PlayerItemAction

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

### GameContext 只读状态

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

<a id="task-api"></a>
### Task API 任务查询与注册

`ModApi.Tasks` 提供当前活动任务、任务记录和目标快照，不暴露游戏内部 `TaskDetail` / `TaskTarget` 对象。模组还可以用 `TaskApi.For(context)` 注册自己的任务定义、通过原生任务系统接取任务，并手动完成目标。手动目标在游戏配置中使用一个不会触发的原生目标类型来显示，实际完成条件由模组代码控制。

任务也可以放在模组目录的 `tasks/*.json` 中。C# 模组里的任务 JSON 会在入口 `Initialize` 前注册，可直接用短任务 ID 接取和推进；独立 JSON 任务模组可由声明了它为依赖的 C# 模组通过 `ModTaskKey` 使用。字段、清单和完整示例见[JSON 任务定义](API_REFERENCE.md#task-api-json)。

`ModTaskDefinition.Category` 支持 `Prisoner`、`Mainline`、`Gang`、`DaJiao`、`HeiZhua`、`JianYa`、`BarberShop`、`PrisonGuardCaptain`、`PrisonGuardMailRoom`、`Side` 和 `Escape`。`Gang` 会显示在游戏的帮派分类下；如果任务属于某个具体帮派，可用其专属分类。Loader 按原生 `UI_TaskListTree.GetTitleByTaskType` 映射：`Side` 使用任务类型 8，与原版支线任务共用标题栏；因此它也会和 `PrisonGuardMailRoom` 显示在同一分组。原生类型 0 虽然也显示为“支线”，但使用另一标题对象。`Escape` 使用类型 9；类型 10 显示为旧主线标题。ModAPI 的枚举值是逻辑分类标识，不是原生任务类型数字。

```csharp
var activeTasks = ModApi.Tasks.ActiveTasks;
foreach (var task in activeTasks)
{
    context.Logger.Info(
        $"{task.Id} {task.Name}: {task.CompletedTargetCount}/{task.Targets.Count}");
}

_subscriptions.Add(ModApi.Events.Subscribe<TaskAcceptedEvent>(info =>
    context.Logger.Info($"Task accepted: {info.Task.Id} {info.Task.Name}")));

_subscriptions.Add(ModApi.Events.Subscribe<TaskUpdatedEvent>(info =>
    context.Logger.Info(
        $"Task {info.Current.Id} changed ({info.Source}): " +
        $"{info.Current.CompletedTargetCount}/{info.Current.Targets.Count}")));
```

`ActiveTasks` 仅包含当前活动任务；`AllTasks` 返回任务日志中的所有记录，`GetTasks(id)` 可查询同一任务 ID 的多个记录，`TryGetTask(id, out task)` 用于便捷查询。快照包含任务名称、任务类型名、开始日、完成/失败/放弃/超时状态，以及目标类型、描述和完成状态。Loader 在 `GameplayReady` 时建立任务日志基准，之后在 Unity 主线程每约 0.2 秒读取一次快照：新增记录触发 `TaskAcceptedEvent`，已有记录变化触发 `TaskUpdatedEvent`，其 `Source` 会标记 `give-up`、`target-progress` 等变化类型。首次读档恢复的任务进入初始基准，不作为“新领取”事件重复上报。

```csharp
var tasks = TaskApi.For(context);
var registration = tasks.Register(new ModTaskDefinition(
    "repair-radio",
    "修理收音机",
    "找到零件并修好收音机。",
    new[]
    {
        new ModTaskObjective("find-parts", "收集修理需要的零件。"),
        new ModTaskObjective("repair", "完成收音机修理。"),
    })
{
    Category = ModTaskCategory.Prisoner,
});

if (!registration.Succeeded)
{
    context.Logger.Error(registration.Message);
}

// 在模组自己的条件满足时调用；这里只用 GameplayReady 演示接取。
_subscriptions.Add(ModApi.Events.Subscribe<GameplayReadyEvent>(_ =>
{
    var result = tasks.Accept("repair-radio");
    context.Logger.Info(result.Message);
}));

// 在模组自己的条件处理回调里调用。
void OnPartsCollected() =>
    tasks.CompleteObjective("repair-radio", "find-parts");

void OnRadioRepaired() =>
    tasks.CompleteObjective("repair-radio", "repair");
```

任务定义会注入游戏原生任务配置表，因此接取后显示在游戏任务日志中。当前版本不自动创建 NPC 或电话任务入口，也不替模组判断目标条件；模组需要在自己的事件或剧情条件满足时调用 `Accept` 和 `CompleteObjective`。任务记录会保存在存档中，使用该任务的存档需要保留注册它的模组。

### 使用示例

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

### Mod 目录与资源

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

### 事件规则

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

<a id="task-api-json"></a>

### JSON 任务定义

任务 JSON 用来声明任务标题、描述、分类和目标。目标是否完成仍由 C# 模组根据游戏事件或剧情条件判断，再调用 `TaskApi` 推进。

#### 放置位置

把 JSON 文件放在模组目录的 `tasks/` 中；加载器会递归扫描该目录下的 `.json` 文件。

```text
MyTaskMod/
  mod.json                 # C# 模组；或者使用下方的 Manifest.json
  MyTaskMod.dll
  tasks/
    repair-radio.json
```

如果模组使用 `mod.json` 并带有 C# 入口，任务会在 `IMod.Initialize` 之前注册，因此 C# 代码可以立刻接取或推进这些任务。如果模组只有 JSON，则在 `Manifest.json` 中设置 `isTaskMod: true`，并至少提供一个有效任务文件。

#### 任务 JSON 格式

```json
{
  "type": "task",
  "schemaVersion": 1,
  "key": "repair-radio",
  "name": "修好收音机",
  "description": "找到缺少的零件，修好收音机。",
  "category": "Prisoner",
  "acceptedDescription": "先找齐零件，再检查收音机的线路。",
  "completedDescription": "收音机终于能正常工作了。",
  "objectives": [
    {
      "id": "find-parts",
      "description": "找到修理收音机需要的零件。",
      "completedDescription": "零件已经找齐。"
    },
    {
      "id": "repair",
      "description": "修好收音机。"
    }
  ]
}
```

- `type` 必须是 `task`，`schemaVersion` 当前必须为 `1`。
- `key` 是当前模组命名空间内的本地 ID，不要加 `namespace:` 前缀。可使用英文字母、数字、下划线、连字符和句点。
- `name`、`description` 和至少一个 `objectives` 项为必填；每个目标必须有唯一的 `id` 和 `description`。一个任务最多有 64 个目标。
- `category` 可选，默认为 `Prisoner`。可选值为 `Prisoner`、`Mainline`、`PrisonGuardCaptain`、`PrisonGuardMailRoom`、`BarberShop`、`DaJiao`、`HeiZhua`、`JianYa`、`Gang`、`Side`、`Escape`。
- `acceptedDescription`、`completedDescription` 和目标上的 `completedDescription` 均可省略。

JSON 任务注册后会出现在游戏原生任务日志中。加载器为目标创建可显示的原生目标行，但不会从目标描述中推断条件，也不会自动接取任务。

#### 在同一个 C# 模组中使用

在 C# 模组目录里同时放置 `mod.json`、入口 DLL 和任务 JSON。调用原有短 ID API 即可：

```csharp
using BackToTheDawn.ModAPI;

public sealed class ModEntry : IMod
{
    private TaskApi? _tasks;

    public void Initialize(ModContext context)
    {
        _tasks = TaskApi.For(context);

        ModApi.Events.Subscribe<GameplayReadyEvent>(_ =>
            _tasks.Accept("repair-radio"));

        ModApi.Events.Subscribe<RadioPartsCollectedEvent>(_ =>
            _tasks.CompleteObjective("repair-radio", "find-parts"));
    }

    public void Shutdown() { }
}
```

`RadioPartsCollectedEvent` 只是示例中的模组自定义事件。实际模组应替换成自己监听的游戏事件，并保存、释放订阅句柄。可运行示例见 `examples/BackToTheDawn.TaskApiExample`：其中 C# 入口监听背包事件，获得苹果后完成 JSON 任务目标。

#### C# 模组使用另一个 JSON 任务模组

JSON-only 任务模组的 `Manifest.json` 示例：

```json
{
  "schemaVersion": 1,
  "namespace": "example.sharedtasks",
  "isTaskMod": true,
  "name": "共享任务定义",
  "version": "1.0.0",
  "dependencies": ["dev.backtothedawn.loader"]
}
```

把任务 JSON 放在该目录的 `tasks/` 下。使用这些任务的 C# 模组应在自己的 `mod.json` 的 `dependencies` 数组中声明 `example.sharedtasks`。加载器会先初始化依赖；随后 C# 模组可以用完整的 `ModTaskKey` 接取任务和完成目标：

```csharp
var tasks = TaskApi.For(context);
var taskKey = new ModTaskKey("example.sharedtasks", "repair-radio");

tasks.Accept(taskKey);
tasks.CompleteObjective(taskKey, "find-parts");
```

跨模组调用只允许访问自己或 `dependencies` 中声明的模组命名空间。卸载任务定义所属的模组时，已经写入存档的原生任务记录会按 Task API 现有规则保留。

## PhoneAPI：电话注册与对话

<a id="phone-api"></a>

### C# 接口

无需 DLL 的电话模组可使用 `Manifest.json` 和对话 JSON，格式与示例见 [JSON 电话模组](API_REFERENCE.md#phone-api-json)。

电话功能由独立的 `BackToTheDawn.PhoneAPI.dll` 提供。Mod 项目需引用该 DLL，并通过 `PhoneApi.For(context)` 获取当前 Mod 的 API 实例。它可注册新对话和 5 位电话号码，也可覆盖游戏已有台词。自定义号码会出现在游戏电话簿中；拨打后优先使用游戏原生对话组件逐句显示台词，当前场景没有可用组件时回退到 Loader 对话面板。

```csharp
using BackToTheDawn.ModAPI;
using BackToTheDawn.PhoneAPI;

public sealed class PhoneMod : IMod
{
    private IDisposable? _lineSubscription;

    public void Initialize(ModContext context)
    {
        var phones = PhoneApi.For(context);
        _lineSubscription = phones.SubscribeLineDisplayed(line =>
        {
            if (!line.IsCustomConversation && line.StringKey is not null)
            {
                context.Logger.Info($"Native phone line key: {line.StringKey}");
            }
        });

        var conversation = phones.RegisterConversation(
            "repair-shop",
            new[]
            {
                new PhoneDialogueLine(
                    "修理店",
                    "喂，这里是修理店。",
                    PhoneDialogueSpeakerType.Caller),
                new PhoneDialogueLine(
                    "你",
                    "我想问一下今天的营业时间。",
                    PhoneDialogueSpeakerType.Player),
                new PhoneDialogueLine(
                    "修理店",
                    "晚上六点关门。",
                    PhoneDialogueSpeakerType.Caller),
            });

        if (conversation.Succeeded)
        {
            var number = phones.RegisterNumber(
                "48327",
                "街角修理店",
                "repair-shop");
            if (!number.Succeeded)
            {
                context.Logger.Warning(number.Message);
            }
        }
    }

    public void Shutdown() => _lineSubscription?.Dispose();
}
```

Conversation key 会自动加上 Mod 清单中的 `id` 前缀，例如 `dev.example.mod:repair-shop`。电话号码必须是五位数字，不能与其他 Mod 或游戏内号码重复。电话簿条目和对话只在本次游戏运行中生效，不会修改存档。

#### 按拨打时间生成对话

如果同一个号码需要按拨打时的游戏时间等状态生成不同台词，可以使用 `RegisterDynamicConversation`。传入的函数会在每次拨号开始时调用一次；该次通话使用返回的台词快照，通话中时间变化不会改写正在播放的内容。

```csharp
var phones = PhoneApi.For(context);
var morningLines = new[]
{
    new PhoneDialogueLine("塞琳娜", "早上好，昨晚休息得怎么样？"),
    new PhoneDialogueLine("你", "还不错，今天准备继续调查。", PhoneDialogueSpeakerType.Player)
    {
        EndCall = true,
    },
};
var eveningLines = new[]
{
    new PhoneDialogueLine("塞琳娜", "这么晚了还在忙吗？"),
    new PhoneDialogueLine("你", "我马上休息，明天再继续。", PhoneDialogueSpeakerType.Player)
    {
        EndCall = true,
    },
};

phones.RegisterDynamicConversation("selina", call =>
{
    var hour = call.Time?.Hour ?? 12;
    return hour >= 18 || hour < 6 ? eveningLines : morningLines;
});
phones.RegisterNumber("27621", "塞琳娜", "selina");
```

`PhoneCallContext` 提供拨打的 `Number`、`DisplayName` 和当时的 `Time`（`GameTimeSnapshot?`）。时间为空时应自行选择默认台词。Loader 会在实际拨号前校验动态函数返回的台词和分支；函数抛出异常或返回无效内容时，该次通话不会开始，也不会扣除拨打次数。静态 `RegisterConversation` 不受影响。

#### 给 JSON 注册自定义条件

JSON 路线可以使用内置条件，也可以引用 DLL 模组注册的自定义条件。条件 ID 自动带上注册者的 Mod 命名空间；引用其他 Mod 的条件时，JSON 模组必须在 `Manifest.json` 中依赖该 DLL 模组。

```csharp
var phones = PhoneApi.For(context);
var result = phones.RegisterCondition("knows-secret", call =>
{
    // 这里可以组合本模组的任务状态、配置、剧情标记或其他 ModAPI 状态。
    return StoryFlags.KnowsSecret && call.Time is { Day: >= 3 };
});

if (!result.Succeeded)
    context.Logger.Warning(result.Message);
```

上例注册的完整 ID 是 `你的模组ID:knows-secret`。自定义条件只允许被注册者本身或声明依赖它的模组引用。条件在每次拨号时求值一次；`TryEvaluateCondition` 可供 C# 动态对话直接查询，未知或无权访问的 ID 返回 `false`，同时将 `out result` 设为 `false`。

#### 原生选项与分支

在某句台词的 `Options` 中填写选项。玩家完成这句台词后，Loader 调用游戏的 `CharacterTalk.ShowTalkOptionsList` 显示原生列表，等待玩家选择，再播放对应分支。等待选择时，继续对话的输入不会跳过菜单。取消原生列表会结束通话，并执行已有的挂断和操作恢复流程。

```csharp
var phones = PhoneApi.For(context);
phones.RegisterConversation("service", new[]
{
    new PhoneDialogueLine("修理店", "需要什么服务？")
    {
        Id = "menu",
        Options = new[]
        {
            new PhoneDialogueOption("repair", "修理收音机", "repair-question"),
            new PhoneDialogueOption("hours", "询问营业时间", "hours-answer"),
            new PhoneDialogueOption("hang-up", "挂断", EndCall: true),
        },
    },
    new PhoneDialogueLine("你", "请问能修理收音机吗？", PhoneDialogueSpeakerType.Player)
    {
        Id = "repair-question",
    },
    new PhoneDialogueLine("修理店", "可以，带来后我会检查零件和费用。")
    {
        EndCall = true,
    },
    new PhoneDialogueLine("修理店", "今天晚上六点关门。")
    {
        Id = "hours-answer",
        NextLineId = "menu", // 返回服务菜单，也可以改成 EndCall = true。
    },
});
phones.RegisterNumber("48328", "修理店服务", "service");
```

字段说明：

| 字段 | 作用 |
| --- | --- |
| 台词 `Id` | 分支目标标识，在当前对话内唯一，区分大小写；不需要跳转到的台词可以省略。 |
| 台词 `NextLineId` | 完成台词后的目标；省略则顺序播放下一句。也可跳回前面的台词。 |
| 台词 `EndCall` | 完成台词后挂断；不能同时设置目标或选项。 |
| 台词 `Options` | 完成这句台词后显示的选项列表。 |
| 选项 `Id` | 用于识别选择，同一句台词内必须唯一。 |
| 选项 `Text` | 原生列表显示的文字。 |
| 选项 `NextLineId` | 选择后的目标；省略则沿用所属台词的 `NextLineId` 或顺序进入下一句。 |
| 选项 `EndCall` | 选择后立即挂断；不能同时设置目标。 |

未设置选项和分支的旧对话继续按顺序播放，到最后一句结束时挂断。注册时会检查重复标识、缺失目标和相互冲突的字段，错误定义会抛出 `ArgumentException`。选择和分支跳转不会重复扣除每日拨打次数。

可订阅选项选择事件，执行 Mod 自己的逻辑；订阅会观察所有 Mod 的电话选项，按 `ConversationKey` 筛选。此回调不替代配置的跳转：

```csharp
// 保存 subscription，并在 Mod.Shutdown 中 Dispose。
var subscription = phones.SubscribeOptionSelected(choice =>
{
    if (choice.ConversationKey == context.Manifest.Id + ":service")
    {
        context.Logger.Info($"选择：{choice.OptionId}，来自台词：{choice.LineId}");
    }
});
```

当前无法取得原生选项组件时，会显示 Loader 的后备选项按钮。`48327` 示例号码提供“修理收音机 / 询问营业时间 / 挂断”三个分支。

#### 覆盖游戏台词

先订阅 `PhoneLineDisplayedEvent`，拨打原有电话并记录 `StringKey`，再按完整 key 覆盖文本：

```csharp
var phones = PhoneApi.For(context);
var result = phones.OverrideLine(
    "native_dialogue_key",
    "替换后的台词");
```

文本覆盖针对这个对话 key 的每次显示。它只替换文字，不会改动原对话的选项、剧情动作、条件、关系变化或通话流程。多个 Mod 覆盖同一 key 时，最后注册的文本生效；卸载该 Mod 后会回退到仍注册的覆盖内容。

#### 修改通话中的电话图片

`RegisterConversation` 的 `interactionIconPath` 可指定通话时左侧方框中电话图片或联系人图片所用的 PNG。路径相对于 Mod 的 `resource` 目录；留空时使用游戏原图。运行时替换 `TalkPhone` 中正在显示的联系人图片，并在原生图片动画刷新后维持自定义贴图。挂断或 Mod 卸载时恢复原图。该参数沿用已有名称。

替换只作用于当前面板的 `icon` 层或 NPC 肖像层，游戏的背景、阴影和边框继续正常显示。PNG 按原比例在原图标区域居中显示，使用像素采样；挂断时一并恢复原生图片的显示模式。

```csharp
var conversation = phones.RegisterConversation(
    "repair-shop",
    lines,
    interactionIconPath: "icons/repair-shop.png");
```

将图片放在 Mod 目录的 `resource/icons/repair-shop.png`。`debug_token.png` 可用于快速确认替换是否生效；它是方形瓶子图标，不适合作为最终电话图标。

#### 当前范围

- 新电话对话支持原生选项、按台词标识跳转和主动挂断；目前没有内置条件表达式、语音或原版剧情动作执行接口。
- 新号码通过游戏电话输入界面拨打，通话优先使用游戏原生对话框。成功开始一通自定义电话时，通过 `StoragePhoneInfo.AddCallTimes()` 计入原生每日拨打次数；重复回调不重复扣次。费用由进入电话界面的原生流程处理，Loader 不额外扣费。
- `PhoneDialogueSpeakerType.Caller` 使用电话样式；`PhoneDialogueSpeakerType.Player` 使用玩家普通对话样式和玩家角色身份。两种原生对话框均直接显示 `Text`，不会自动添加说话者名称前缀。`Speaker` 保留为说话者信息，并供 Loader 后备面板单独显示；旧的双参数 `PhoneDialogueLine(speaker, text)` 保持电话方样式。
- 如果当前场景无法取得游戏对话组件，则显示 Loader 后备面板。
- 原版电话对话仍由游戏执行；`OverrideLine` 只改变指定 `TalkString.stringKey` 的显示文本。
- `PhoneLineDisplayedEvent` 可观察原版电话台词和 Mod 对话，适合发现需要覆盖的原版 key。

<a id="phone-api-json"></a>

### JSON 注册

安装前置后，可以用 JSON 注册电话，无需编写或编译 DLL。JSON 模组复用 PhoneAPI 的电话运行时，支持电话簿、原生电话与玩家对话框、原生选项、分支跳转、图片替换和挂断。

#### 目录

将一个模组文件夹放在 `BepInEx/mods/` 的直接子目录：

```text
BepInEx/mods/MyPhoneMod/
  Manifest.json
  dialogues/
    shop/
      repair.json
  resource/
    icons/
      shop.png
```

加载器发现根目录的 `Manifest.json` 后，递归读取这个模组目录及所有子目录中的 JSON。只有 `type` 为 `phoneConversation` 的对象会作为对话加载；`Manifest.json`、`mod.json` 和其他类型的 JSON 不注册为对话。扫描不会跟随目录链接，也不会进入含有独立模组清单的子目录；独立模组应分别放在 `BepInEx/mods/` 下。

一个模组可以有多个对话 JSON，每个文件注册一个号码。修改文件后重新启动游戏生效。

#### Manifest.json

```json
{
  "schemaVersion": 1,
  "namespace": "yourname.phonepack",
  "name": "我的电话模组",
  "version": "1.0.0",
  "isPhoneMod": true,
  "dependencies": ["dev.backtothedawn.loader"]
}
```

| 字段 | 说明 |
| --- | --- |
| `namespace` | 必填，模组的唯一身份及电话对话命名空间。以英文字母或数字开头，只使用英文字母、数字、点、下划线和短横线。与其他 JSON 或 DLL 模组的 ID 不能重复。 |
| `isPhoneMod` | 必填布尔值，`true` 启用电话 JSON 扫描；`false` 跳过这个清单。 |
| `name` | 模组名称，省略时使用命名空间。 |
| `version` | 模组版本，省略时使用 `1.0.0`。 |
| `dependencies` | 依赖的 Mod ID 数组，省略时为空。复用已有的依赖排序和检查。 |
| `schemaVersion` | 格式版本，当前为 `1`，省略也使用 `1`。 |

DLL 模组继续使用已有的 `mod.json`。一个目录只能选择其中一种清单。

#### 对话 JSON

```json
{
  "type": "phoneConversation",
  "key": "shop",
  "number": "48328",
  "displayName": "便利店",
  "lines": [
    {
      "id": "menu",
      "speakerType": "Caller",
      "text": "需要什么服务？",
      "options": [
        { "id": "hours", "text": "询问营业时间", "nextLineId": "hours" },
        { "id": "hang-up", "text": "挂断", "endCall": true }
      ]
    },
    {
      "id": "hours",
      "speakerType": "Caller",
      "text": "晚上六点关门。",
      "endCall": true
    }
  ]
}
```

#### 条件路线

一个号码可以有多条候选对话。拨号时从上到下检查 `routes`，选择第一条条件成立的路线；都不成立时播放 `defaultLines`。条件只在拨号开始时检查一次。旧格式的 `lines` 仍然有效，但不能与 `routes` / `defaultLines` 混用。

```json
{
  "type": "phoneConversation",
  "key": "selina",
  "number": "27621",
  "displayName": "塞琳娜",
  "routes": [
    {
      "id": "after-evidence",
      "when": {
        "all": [
          { "taskCompleted": 12345 },
          { "any": [
            { "timeBetween": { "start": "20:00", "end": "06:00" } },
            { "moneyAtLeast": 100 }
          ] },
          { "custom": "dev.example.story:knows-secret" }
        ]
      },
      "lines": [
        { "speakerType": "Caller", "text": "你已经找到证据了？" },
        { "speakerType": "Player", "text": "是的，我们可以继续计划了。", "endCall": true }
      ]
    }
  ],
  "defaultLines": [
    { "speakerType": "Caller", "text": "喂？找我有什么事？", "endCall": true }
  ]
}
```

`custom` 使用完整条件 ID；上例的电话模组需在 `Manifest.json` 的 `dependencies` 中加入 `dev.example.story`。对应 DLL 模组通过 `RegisterCondition("knows-secret", ...)` 注册它。

支持的条件：

| 条件 | JSON 值 | 说明 |
| --- | --- | --- |
| `all` | 条件数组 | 所有子条件都成立。 |
| `any` | 条件数组 | 至少一个子条件成立。 |
| `not` | 单个条件 | 子条件不成立时为真。 |
| `taskCompleted` | 整数任务 ID | 指定的原生任务已完成；模组自己的剧情任务可注册 C# 自定义条件。 |
| `dayAtLeast` / `dayAtMost` | 整数 | 拨打当天的游戏日下限或上限。 |
| `timeBetween` | `{ "start": "HH:mm", "end": "HH:mm" }` | 24 小时制，起止时间均包含；结束早于开始时表示跨午夜。 |
| `moneyAtLeast` | 整数 | 当前金钱不少于指定值。 |
| `itemCountAtLeast` | `{ "item": "namespace:path", "count": 整数 }` | 背包中指定物品的总数不少于指定值。 |
| `friendAtLeast` / `affectionAtLeast` | `{ "characterId": 整数, "value": 整数 }` | 指定角色的友好度或好感度达到指定值。 |
| `custom` | 条件 ID 字符串 | 调用 DLL 模组通过 PhoneAPI 注册的自定义条件。 |

每个条件对象只能包含一个运算符；`all` 和 `any` 需要非空数组。路线 `id` 在同一对话内唯一，仅用于识别和诊断。引用的自定义条件不存在、提供者未加载或未声明依赖时，该条件按不成立处理，并在日志中记录警告；默认对话仍可播放。

##### 文件字段

| 字段 | 说明 |
| --- | --- |
| `type` | 必须为 `phoneConversation` 才会被识别为对话。 |
| `key` | 必填对话标识。例如 `shop` 会变成 `yourname.phonepack:shop`。也可填写完整的当前模组命名空间 key。 |
| `number` | 必填五位数字字符串，例如 `"48328"`；不能写成数值。不得与游戏或其他模组号码重复。 |
| `displayName` | 必填，电话簿及后备面板显示的联系人名称。 |
| `lines` | 必填且至少包含一句台词。 |
| `addToPhoneBook` | 默认 `true`。设为 `false` 时仍可通过键盘拨号。 |
| `interactionIconPath` | 可选，左侧电话图片的路径，相对于当前模组的 `resource` 目录，如 `icons/shop.png`。 |
| `schemaVersion` | 当前为 `1`，省略也使用 `1`。 |

##### 台词与选项

- 台词 `text` 必填，原生对话框只显示这段文字。
- `speakerType` 为 `Caller` 或 `Player`，省略时使用 `Caller`。
- `speaker` 可选，是说话者信息；省略时电话方使用联系人名称，玩家使用“你”。它不会加到原生台词前面。
- 台词 `id` 是分支目标，在同一个文件中必须唯一，区分大小写。
- `nextLineId` 跳到指定台词；省略时进入下一句，到末尾自动挂断。可以跳回前面的台词。
- `endCall: true` 表示完成台词后挂断，不能同时填写 `nextLineId` 或非空 `options`。
- `options` 在当前台词完成后显示原生选项列表，省略时没有选项。
- 每个选项需要同一句台词内唯一的 `id` 和非空 `text`。`nextLineId` 指定选择后的目标；省略时沿用所属台词的跳转或顺序下一句。`endCall: true` 会直接挂断，不能同时填写目标。

取消原生选项列表会挂断。成功开始一通电话才计入一次每日拨打次数；选择和返回菜单不会重复扣次。

#### 示例与错误日志

工作区的 `examples/BackToTheDawn.JsonPhoneMod/` 是完整示例。复制到 `BepInEx/mods/` 后，启动游戏拨打 `48329`；该示例提供“修理收音机 / 询问营业时间 / 挂断”，询问营业时间后返回菜单。

清单、对话标识、号码或分支定义有误时，`BepInEx/LogOutput.log` 会记录文件或模组及原因。扫描到的 JSON 必须语法正确。当前模组全部对话先解析校验，再开始注册；注册时遇到号码冲突等错误会撤销这个模组本轮注册的电话，其他模组继续加载。

JSON 模式描述对话、跳转和条件路线；需要写模组专属判断时，由 DLL 模组调用 `RegisterCondition`，再由 JSON 引用。需要选择回调中执行其他 C# 逻辑时，使用 DLL 模组和 `SubscribeOptionSelected`。C# 用法见 [PhoneAPI C# 接口](#phone-api)。

## ShopAPI：商店注册与交易

<a id="shop-api"></a>

### C# 接口

独立的 `BackToTheDawn.ShopAPI.dll` 提供 `ShopApi`，支持把商品加入已有原生商店、调整现有售价、注册新的原生商店目录，并在模组运行时打开游戏自己的商店界面。项目同时需要引用 `BackToTheDawn.ModAPI.dll`，因为商店和物品使用 ModAPI 中的 `ShopKey` 与 `ItemKey`。

#### 获取 API

```csharp
using BackToTheDawn.ModAPI;
using ShopApi = BackToTheDawn.ShopAPI.ShopApi;

var shops = ShopApi.For(context);
```

使用模组清单 ID 作为新商店的命名空间。API 调用返回 `ShopMutationResult`；`Scheduled` 表示请求已接受，Loader 会等游戏商品目录就绪后再写入运行时商店表。应用失败的原因会写入 `BepInEx/LogOutput.log`。

游戏物品的完整 `ItemKey` 列表见 [游戏物品键表](API_REFERENCE.md#item-keys)。

#### 注册新商店并打开原生 UI

商店商品必须是游戏目录中已有的物品，或已由 Mod 注册并成功注入运行时的物品。

```csharp
var shops = ShopApi.For(context);
var key = new ShopKey(context.Manifest.Id, "night_market");

var registered = shops.RegisterShop(
    "night_market",
    "夜市",
    new[]
    {
        new ShopOffer(new ItemKey("backtothedawn", "apple"), Price: 25, Stock: 20),
        new ShopOffer(new ItemKey("backtothedawn", "painkiller"), Price: 80),
    });

if (!registered.Succeeded)
    context.Logger.Warning(registered.Message);

// 在游戏内需要显示商店时调用，例如模组自己的交互回调中。
var opened = shops.OpenShop(key);
if (!opened.Succeeded)
    context.Logger.Warning(opened.Message);
```

`Stock` 默认 `int.MaxValue`，即非常大的可购买库存。新商店使用现金价格和普通商店商品行；它不会自动添加 NPC、地图交互点或电话入口，模组需要在自己的交互流程中调用 `OpenShop`。

通过 `RegisterShop` 创建的新商店在确认购买后，会调用 `ModApi.Inventory.TryAdd` 把购买数量放进口袋，并通过游戏的现金接口扣除总价；只有物品完整发放且扣款核对成功后才会减少商店库存。口袋空间、库存或现金校验失败时不会完成购买。已有原生商店仍由游戏自己的购买流程结算。

如果只需要用数据文件注册新商店，可以使用 [JSON 商店模组格式](API_REFERENCE.md#shop-api-json)。JSON 模式可注册商店和商品，但不会创建 NPC、地图交互点或按钮；打开界面仍需由 C# 模组调用 `ShopApi.OpenShop`。

#### 修改已有商店

游戏商店通过稳定的 `ShopKey` 定位。可在 [商店目录文档](API_REFERENCE.md#shop-api-status) 查看当前收录的 key。

```csharp
var shops = ShopApi.For(context);
var vendingMachine = new ShopKey("backtothedawn", "vending_machine");
var apple = new ItemKey("backtothedawn", "apple");

shops.SetPrice(vendingMachine, apple, price: 10);
shops.AddGoods(vendingMachine, new ShopOffer(apple, Price: 15, Stock: 5));
```

`AddGoods` 只新增该店目前没有的物品；已有同一物品时不会重复加货。`SetPrice` 修改该店对应的商品价格。

#### 限制

- 商店数据写入当前运行时游戏配置，不修改安装文件。关闭或重启游戏后会重新应用模组注册。
- 新商店可以通过 Mod 代码打开原生 `UI_Shop`。NPC、场景物件和菜单按钮需要模组另行接入。
- 当前新增商品使用现金价格和普通商店商品行，不包含帮派订购、关系值、表现分、彩票或延迟订单等特殊交易流程。
- Mod 注册的物品只有在运行时注入成功后才能加入商店；若关闭 `Items/EnableRuntimeItemInjection`，请使用游戏原有物品。
- Mod 卸载时会移除它添加的商品并恢复它修改的原价。

<a id="shop-api-json"></a>

### JSON 注册

JSON 商店模组可以在不编写 DLL 的情况下注册新的原生商店目录。商品必须是游戏已经注册的物品；商店通过 ShopAPI 的同一运行时注册流程应用。

游戏物品的完整命名空间键见 [游戏物品键表](API_REFERENCE.md#item-keys)，JSON 的 `offers[].item` 和 C# 的 `ItemKey` 都使用其中的键。

#### 目录结构

```text
BepInEx/mods/MyShopMod/
├── Manifest.json
└── shops/
    └── night-market.json
```

加载器会递归扫描模组目录下的 JSON 文件，只处理 `type` 为 `shop` 的对象；电话配置及其他 JSON 文件会跳过。扫描不会跟随目录链接，也不会进入带有独立模组清单的子目录。

#### Manifest.json

```json
{
  "schemaVersion": 1,
  "namespace": "yourname.market",
  "name": "夜市商店",
  "version": "1.0.0",
  "isShopMod": true,
  "dependencies": ["dev.backtothedawn.loader"]
}
```

`namespace` 是模组唯一 ID。`isShopMod` 必须设为 `true` 才会启用商店 JSON 加载。`isPhoneMod` 可以同时设为 `true`，让同一个模组也加载电话对话。`schemaVersion` 当前为 `1`；`name`、`version` 和 `dependencies` 的行为与电话 JSON 模组相同。

#### 商店定义

```json
{
  "type": "shop",
  "schemaVersion": 1,
  "key": "night_market",
  "displayName": "夜市",
  "offers": [
    {
      "item": "backtothedawn:apple",
      "price": 25,
      "stock": 20
    },
    {
      "item": "backtothedawn:painkiller",
      "price": 80
    }
  ]
}
```

| 字段 | 说明 |
| --- | --- |
| `type` | 必须为 `shop`。 |
| `schemaVersion` | 配置版本，当前为 `1`，省略时使用 `1`。 |
| `key` | 必填的商店路径；不能带命名空间。例子最终生成 `yourname.market:night_market`。 |
| `displayName` | 必填的游戏商店显示名称。 |
| `offers` | 必填且非空的商品数组。一个商店内不能重复列出同一物品。 |
| `offers[].item` | 必须使用完整物品 key，例如 `backtothedawn:apple`。 |
| `offers[].price` | 必填的整数现金单价，不能小于 `0`。 |
| `offers[].stock` | 可选库存，不能小于 `0`；省略时视为不限量。 |

一个 JSON 文件定义一个商店；可以放多个 `type: shop` 文件。加载器会先解析并校验全部定义，再注册商店；定义无效或注册冲突时会在 `BepInEx/LogOutput.log` 中记录原因，并撤销这个模组已经完成的注册。

JSON 模组负责注册商店数据，不会自动生成 NPC、地图交互点或按钮。若要从模组代码打开游戏原生商店界面，可引用 `BackToTheDawn.ShopAPI.dll` 并调用：

```csharp
var shops = ShopApi.For(context);
shops.OpenShop(new ShopKey(context.Manifest.Id, "night_market"));
```

完整的 C# 商店注册、原生商店界面、库存、购买和现有商店修改说明见 [商店 API](API_REFERENCE.md#shop-api)。

<a id="shop-api-status"></a>

### 当前能力与商店目录

> 更新时间：2026-10-03
> 范围：商店身份、商品目录、价格货币、交易生命周期、延迟领取和失败信号。
> 说明：本文按当前源码和已验证日志判断；“已接入”不等于所有字段都已经在每个商店运行验证。

#### 1. 文档证据与系统边界

商店系统的主要证据来自：

- `docs/GAME_SYSTEMS.md`：`ShopManage`、`ShopGoods`、`c_shop` 以及价格/库存/日期字段。
- `docs/TRADE_API_REQUIREMENTS.md`：交易分类、延迟订单和未完成项。
- `docs/TECHNICAL.md`：`ShopKey`、`ShopCatalog` 和交易事件契约。
- Loader：`ShopCatalogBootstrap`、`TradePatches`、`ExtendedTradePatches`、`TradeSignals`。

商店系统不只表示“用金钱购买物品”，还包括：

```text
物品 + 金钱             -> 即时购买
物品 + 表现分/关系值     -> 特殊商店兑换
物品 + 帮派贡献          -> 帮派订购
下单                    -> 次日包裹领取
彩票/下注                -> 金融结算
```

`ThingPackage.ChangeMoney`、`ChangeDiscipline`、`ChangeFriend` 和
`ChangeGangContribution` 是低层资源入口；公共 API 使用 `TradeCurrencyKind` 隐藏这些内部方法。

#### 2. 当前公开 API

##### 2.1 商店身份

- [x] 使用 `ShopKey` 作为稳定商店身份。
- [x] 提供 `ShopCatalog` 和 `ShopApi` 查询入口。
- [x] 保留原始数字 ID 作为兼容查询字段。

```csharp
ModApi.Shops.Catalog
ModApi.Shops.TryGet(shopKey, out var shop)
ModApi.Shops.TryGetByNativeId(nativeShopId, out var shop)
```

稳定身份是 `ShopKey`，原始数字 ID 仅作为兼容查询字段。

除了 ModAPI 中的只读目录查询，当前也通过独立的 `BackToTheDawn.ShopAPI.dll` 提供可写的
`BackToTheDawn.ShopAPI.ShopApi.For(context)`：模组可注册现金交易的新商店、给已有商店加货或调价，并请求打开原生商店界面。接口和限制见
[商店注册 API](API_REFERENCE.md#shop-api)。

##### 2.2 商品目录

- [x] 提供 `ShopGoodsDefinition`。
- [x] 提供 `ShopGoodsCatalog`、`GetGoods` 和 `TryGetGoods`。
- [x] 提供 `ShopGoodsObservedEvent`。
- [x] 启动时从 `c_shop` 全量扫描所有商品（已接入，待运行日志验证）。
- [ ] 统一接入所有特殊商店的商品观测入口。

```csharp
ModApi.Shops.Goods
ModApi.Shops.GetGoods(shopKey)
ModApi.Shops.TryGetGoods(shopKey, itemKey, out var goods)
```

商品记录为 `ShopGoodsDefinition`，目前包含：

- `ShopKey`
- `ItemKey`
- `Price`
- `PriceCurrency`
- `NativePriceType`
- `Stock`
- `AvailableFromDay` / `AvailableToDay`
- `Group`

商品目录是增量目录：商店 UI 或购买入口被加载后才会观察到对应商品。
`ShopGoodsObservedEvent` 会通知新增/更新的商品。

##### 2.3 交易事件

- [x] 提供 `TradeStartedEvent`。
- [x] 提供 `TradeCompletedEvent`。
- [x] 提供 `TradeFailedEvent`。
- [x] 提供 `TradeDetectedEvent`。
- [x] 提供 `TransactionId` 关联语义交易。
- [ ] 为所有交易补齐稳定的物品腿/货币腿关联。

所有已接入的语义商店都可以使用：

```csharp
GameEvents.Subscribe<TradeStartedEvent>(...)
GameEvents.Subscribe<TradeCompletedEvent>(...)
GameEvents.Subscribe<TradeFailedEvent>(...)
GameEvents.Subscribe<TradeDetectedEvent>(...)
```

交易结果通过 `TradeTransaction` 提供：

- `TransactionId`
- `TradeKind`
- `ShopKey`
- `ItemKey` / `ItemDelta`
- `Currency` / `CurrencyDelta`
- `DisciplineDelta`
- `RelationshipDelta`
- `TradePhase`
- `RequestedCount`

#### 3. 商店身份目录

| ShopKey | 原始 ID | 类型 | 当前身份状态 |
|---|---:|---|---|
| `weekly_supplies` | 0 | 每周物资 | [x] 已注册，走普通商店入口 |
| `vice_captain_shop` | 1, 2 | 副队长商店 | [x] 已注册，普通购买已接入 |
| `bigfoot_shop` | 3, 10 | 大脚帮订购 | [x] 已注册，下单事件已接入 |
| `fang_shop` | 4, 11 | 尖牙帮订购 | [x] 已注册，下单事件已接入 |
| `blackclaw_shop` | 5, 12 | 黑爪帮订购 | [x] 已注册，下单事件已接入 |
| `maggie_shop` | 6 | 玛姬 | [x] 已注册，下单事件已接入 |
| `beth_doctor_shop` | 7 | 贝丝医生 | [x] 已注册，普通购买入口已接入 |
| `lunch_counter` | 8 | 午餐点餐 | [x] 已注册，资源结算已接入 |
| `vending_machine` | 9 | 自动售货机 | [x] 已注册，普通购买已验证 |
| `big_bang_pizza` | 13 | 大爆炸披萨 | [x] 已注册，订单入口已接入 |
| `lottery` | 14 | 大乐透 | [x] 已注册，购票/兑奖已接入 |
| `tv_shopping` | 15 | 电视购物 | [x] 已注册，下单入口已接入 |
| `roof_benefit` | 16 | 屋顶福利 | [x] 已注册，双资源结算已接入 |
| `excess_benefit` | 17 | 超额福利 | [x] 已注册，按屋顶路径处理 |
| `church_goods` | 18 | 教会财物 | [x] 已注册，普通购买入口已接入 |
| `rocky_shop` | 19 | 洛奇 | [x] 已注册，普通购买入口已接入 |
| `bank` | 无 | 银行服务 | [x] 语义商店键已注册 |
| `boxing_betting` | 无 | 拳赛下注 | [x] 语义商店键已注册 |
| `match_betting` | 无 | 球赛下注 | [x] 语义商店键已注册 |
| `barber_shop` | 无 | 理发店 | [ ] 仅注册身份，交易 Hook 未完成 |

#### 4. 按商店拆分的交易完成度

状态含义：

- **已完成**：公开 API 和主要 Hook 已存在，且至少有运行日志验证。
- **已接入**：代码已有入口，但缺少完整运行验证或专用字段。
- **部分完成**：只能通过低层 `TradeDetectedEvent` 或普通商店入口观察。
- **未完成**：只有目录身份，没有可用交易语义。

| 商店 | 交易事件 | 货币 | 延迟阶段 | 商品目录观测 | 完成度 |
|---|---|---|---|---|---|
| 普通商店 | `ShopPurchase` + 生命周期 | 金钱 | 即时 | `UI_ShopListUnit` 已接入 | [x] 基础版 |
| 自动售货机 | `VendingMachinePurchase` | 金钱 | 即时 | 已接入，已验证价格/库存 | [x] |
| 副队长商店 | `ViceCaptainPurchase` | 金钱/专用条件 | 即时 | 普通 UI 可观测 | [x] 基础交易事件 |
| 午餐 | `LunchPurchase` | 金钱 | 即时 | 专用商品目录未接入 | [x] 基础交易事件 |
| 教会/医生 | `PriestShopPurchase` / `ShopPurchase` | 通常为金钱 | 即时 | 普通 UI 可观测 | [x] 基础交易事件 |
| 屋顶 | `RoofExchange` | 金钱 + 表现分 | 即时 | 已接入 | [x] 基础版 |
| 玛姬下单 | `GirlfriendShopPurchase` | 关系值 | `OrderPlaced` | [x] 已接入并已验证 | [x] |
| 玛姬收货 | 不再发布独立交易事件 | 无新增货币 | — | 购买已在下单/扣款时完成 | [x] 简化 |
| 帮派下单 | `GangShopPurchase` | 货币以实际变化为准 | `OrderPlaced` | [x] 已接入并已验证 | [x] 基础版 |
| 帮派收货 | 不再发布独立交易事件 | 无新增货币 | — | 购买已在下单/扣款时完成 | [x] 简化 |
| 电视购物 | `TvShopping` | 金钱 | `OrderPlaced` | 未接入订单商品目录 | [x] 基础交易事件 |
| 披萨 | `GenericPurchase` | 金钱 | `OrderPlaced` | 未接入商品目录 | [x] 基础交易事件 |
| 彩票购票 | `Lottery` | 金钱 | 即时 | 号码在交易事件中提供 | [x] |
| 彩票兑奖 | `LotteryPrizeCashedEvent` | 金钱/物品/心情 | 即时 | 不属于普通商品目录 | [x] 基础版 |
| 银行 | `BankDeposit` 等 | 金钱 | 即时 | 不属于商品目录 | [x] 基础交易事件，利息待验证 |
| 拳赛/球赛 | `Betting` + `BetSettledEvent` | 金钱 | 自动结算 | 不属于商品目录 | [x] |
| 理发店 | 无专用交易事件 | 金钱 | 即时 | 无 | [ ] |

#### 5. 货币封装完成度

##### 已公开的货币类型

- [x] `Money`
- [x] `Discipline`
- [x] `Relationship`
- [x] `Chips`
- [x] `GangContribution`

```csharp
TradeCurrencyKind.Money
TradeCurrencyKind.Discipline
TradeCurrencyKind.Relationship
TradeCurrencyKind.Chips
TradeCurrencyKind.GangContribution
```

##### 当前推断规则

当 `c_shop` 的原始价格类型存在时，优先使用原始值；字段为空时使用 `ShopKey` 的保守默认值：

| ShopKey | 默认货币 |
|---|---|
| 普通商店、自动售货机、食堂、电视、彩票 | `Money` |
| `maggie_shop` | `Relationship` |
| `bigfoot_shop` / `fang_shop` / `blackclaw_shop` | 不推断，以实际 `TradeCompleted` 为准 |
| `roof_benefit` / `excess_benefit` | `Discipline` |

最终交易货币仍以 `TradeCompletedEvent.Transaction` 的实际变化为准；屋顶交易的表现分和金钱分别使用 `DisciplineDelta` 与 `CurrencyDelta` 表达。

#### 6. 尚未完成的商店 API

##### 6.1 商品目录

- [x] 全量读取 `c_shop`，并保留 UI/购买时的增量观察（已接入，待运行日志验证）。
- [ ] 午餐、电视和披萨入口补充 `ShopGoodsObservedEvent`。
- [ ] 明确价格类型的原始枚举，而不是只保留文本。
- [ ] 商品刷新、售罄和库存变化事件。

##### 6.2 交易详情

- [ ] `UnitPrice` 与 `TotalPrice` 独立字段。
- [ ] 折扣、讨价还价和技能影响后的实际价格。
- [ ] 余额不足、库存不足、权限限制和售罄的失败原因。
- [ ] 交易历史查询 API。

##### 6.3 特殊商店

- [ ] 电视购物订单号、期号和历史记录。
- [ ] 帮派订单的权限、贡献扣除和商品目录货币字段仍需运行验证。
- [x] 玛姬商品目录和关系值价格读取入口已接入并完成购买验证。
- [ ] 理发店出售物品的专用交易 Hook。

#### 7. 结论

当前 API 已经完成商店系统的“身份层”和“主要交易事件层”。玛姬、帮派、屋顶、彩票和下注的货币/阶段已经有公开事件；普通商店和自动售货机还额外具备商品价格、库存观测。

当前不能称为“完整商店 API”的原因是：商品目录仍是增量的，特殊商店的商品元数据没有全部接入，价格/库存刷新和交易历史也还没有公开。

推荐后续顺序：

1. [ ] 补玛姬、帮派、电视和午餐的商品目录观测。
2. [ ] 增加 `UnitPrice`、`TotalPrice` 和失败原因。
3. [ ] 增加库存刷新/售罄事件。
4. [ ] 最后实现理发店出售和交易历史查询。

## ItemKey：游戏物品完整键名表

<a id="item-keys"></a>

本文列出当前运行时目录中的游戏条目及其 ModAPI `ItemKey`。显示名称和类别来自运行时配置；所有规范键都使用稳定的 ASCII 路径，不随游戏显示语言变化。

范围：游戏构建 `23125213` 的运行时快照，共 406 条，其中 391 条是游戏物品，15 条是属性记录。

JSON 商店与 C# 商店 API 应填写表中的规范键。商店请使用“物品”类别的条目；“属性”记录用于游戏属性系统，不是可购买物品。模组自定义物品使用自己的命名空间，不在此表中。

数据来自游戏构建 `23125213` 的运行时物品目录，以及加载器内置的游戏名称映射表。

| ID | 类别 | 游戏内显示名称 | 规范 ItemKey | 兼容别名 |
|---:|---|---|---|---|
| 1 | 属性 | 体质 | `backtothedawn:physique` | `backtothedawn:item_1` |
| 2 | 属性 | 力量 | `backtothedawn:strength` | `backtothedawn:item_2` |
| 3 | 属性 | 敏捷 | `backtothedawn:agility` | `backtothedawn:item_3` |
| 4 | 属性 | 智力 | `backtothedawn:intelligence` | `backtothedawn:item_4` |
| 5 | 属性 | 精力 | `backtothedawn:energy` | — |
| 6 | 属性 | 健康 | `backtothedawn:health` | — |
| 7 | 属性 | 心态 | `backtothedawn:mentality` | — |
| 8 | 属性 | 饱腹 | `backtothedawn:satiety` | — |
| 9 | 属性 | 表现分 | `backtothedawn:discipline` | — |
| 10 | 属性 | 声望 | `backtothedawn:prestige` | — |
| 11 | 属性 | 金钱 | `backtothedawn:money` | — |
| 12 | 属性 | 好感度 | `backtothedawn:friend` | — |
| 13 | 属性 | 帮派信任感 | `backtothedawn:gang_friend` | — |
| 14 | 物品 | 篮球鞋 | `backtothedawn:basketball_shoes` | — |
| 15 | 物品 | 厚底靴 | `backtothedawn:platform_boots` | `backtothedawn:item_15` |
| 16 | 物品 | 工装鞋 | `backtothedawn:work_shoes` | `backtothedawn:item_16` |
| 17 | 物品 | 拖鞋 | `backtothedawn:slipper` | — |
| 18 | 物品 | 偏光太阳镜 | `backtothedawn:polarized_sunglasses` | `backtothedawn:item_18` |
| 19 | 物品 | 近视眼镜 | `backtothedawn:prescription_glasses` | `backtothedawn:item_19` |
| 20 | 物品 | 前锋头带 | `backtothedawn:forward_headband` | `backtothedawn:item_20` |
| 21 | 物品 | 烈火球帽 | `backtothedawn:fireball_cap` | `backtothedawn:item_21` |
| 22 | 物品 | 睡帽 | `backtothedawn:nightcap` | — |
| 23 | 物品 | 红头巾 | `backtothedawn:red_headband` | — |
| 24 | 物品 | 绿头巾 | `backtothedawn:green_bandana` | `backtothedawn:item_24` |
| 25 | 物品 | 橡胶手套 | `backtothedawn:rubber_gloves` | — |
| 26 | 物品 | 黑手 | `backtothedawn:hei_shou` | — |
| 27 | 物品 | 镀金手表 | `backtothedawn:gold_plated_watch` | `backtothedawn:item_27` |
| 29 | 物品 | 琥珀护身符 | `backtothedawn:hu_po__hu_shen_fu` | — |
| 30 | 物品 | 犬齿项链 | `backtothedawn:canine_tooth_necklace` | `backtothedawn:item_30` |
| 31 | 物品 | 《死灵之书》 | `backtothedawn:necronomicon` | `backtothedawn:item_31` |
| 32 | 物品 | 自制口罩 | `backtothedawn:mask` | — |
| 33 | 物品 | 警用防毒面具 | `backtothedawn:guard_gas_mask` | — |
| 34 | 物品 | 随身听 | `backtothedawn:personal_stereo_on` | — |
| 35 | 物品 | 随身听 | `backtothedawn:personal_stereo_off` | — |
| 36 | 物品 | 随身听 | `backtothedawn:personal_stereo_no_power` | — |
| 37 | 物品 | 酒葫芦 | `backtothedawn:jiu_hu_lu` | — |
| 38 | 物品 | 高级锁具 | `backtothedawn:unlocker_p` | — |
| 39 | 物品 | 黑桃A | `backtothedawn:poker_a` | — |
| 40 | 物品 | 薄荷叶 | `backtothedawn:catnip` | — |
| 41 | 物品 | 薄荷卷 | `backtothedawn:mint_roll` | — |
| 42 | 物品 | 蘑菇 | `backtothedawn:mushroom` | — |
| 43 | 物品 | 蘑菇粉 | `backtothedawn:mushroom_powder` | — |
| 44 | 物品 | 泻药 | `backtothedawn:laxative` | — |
| 45 | 物品 | 紫鸢花 | `backtothedawn:forget_worry_flower` | — |
| 46 | 物品 | 花瓣粉 | `backtothedawn:petal_powder` | `backtothedawn:item_46` |
| 47 | 物品 | 安眠药 | `backtothedawn:sleeping_pill` | — |
| 48 | 物品 | 安眠曲奇 | `backtothedawn:sleeping_cookies` | — |
| 49 | 物品 | 止疼片 | `backtothedawn:painkiller` | — |
| 50 | 物品 | 兴奋剂 | `backtothedawn:stimulant` | `backtothedawn:item_50` |
| 51 | 物品 | 医用酒精 | `backtothedawn:medical_alcohol` | `backtothedawn:item_51` |
| 52 | 物品 | 酒精灯 | `backtothedawn:alcohol_lamp` | — |
| 53 | 物品 | 镇静剂 | `backtothedawn:tranquilizer` | — |
| 54 | 物品 | 啤酒 | `backtothedawn:beer` | — |
| 55 | 物品 | 烈酒 | `backtothedawn:spirits` | `backtothedawn:item_55` |
| 56 | 物品 | 精酿烈酒 | `backtothedawn:craft_spirits` | — |
| 64 | 物品 | 苹果 | `backtothedawn:apple` | — |
| 67 | 物品 | 华夫饼 | `backtothedawn:waffle` | `backtothedawn:item_67` |
| 68 | 物品 | 奶油华夫饼 | `backtothedawn:cream_waffle` | `backtothedawn:item_68` |
| 69 | 物品 | 一把咖啡豆 | `backtothedawn:coffee_bean` | — |
| 70 | 物品 | 口香糖 | `backtothedawn:chewing_gum` | — |
| 71 | 物品 | 曲奇饼干 | `backtothedawn:cookies` | `backtothedawn:item_71` |
| 72 | 物品 | 焦糖棒 | `backtothedawn:caramel_bar` | `backtothedawn:item_72` |
| 73 | 物品 | 汽水 | `backtothedawn:sodas` | — |
| 74 | 物品 | 酸奶 | `backtothedawn:yogurt` | `backtothedawn:item_74` |
| 75 | 物品 | 土豆披萨 | `backtothedawn:potato_pizza` | — |
| 76 | 物品 | 咖啡粉 | `backtothedawn:coffee_powder` | — |
| 77 | 物品 | 茶包 | `backtothedawn:tea_bag` | — |
| 78 | 物品 | 超辣泡面 | `backtothedawn:spicy_noodles` | — |
| 79 | 物品 | 蛋白粉 | `backtothedawn:albumen_powder` | — |
| 80 | 物品 | 枕头 | `backtothedawn:zhen_tou` | — |
| 81 | 物品 | 毯子 | `backtothedawn:tan_zi` | — |
| 82 | 物品 | 假人 | `backtothedawn:jia_ren` | — |
| 83 | 物品 | 床单 | `backtothedawn:chuange_dan` | — |
| 84 | 物品 | 布条 | `backtothedawn:cloth_strip` | `backtothedawn:item_84` |
| 85 | 物品 | 绳索 | `backtothedawn:rope` | — |
| 86 | 物品 | 回形针 | `backtothedawn:paper_clip` | — |
| 87 | 物品 | 开锁器 | `backtothedawn:unlocker` | — |
| 88 | 物品 | 肥皂 | `backtothedawn:soap` | — |
| 89 | 物品 | 香皂 | `backtothedawn:scented_soap` | — |
| 90 | 物品 | 钥匙模具（莱斯特的衣柜） | `backtothedawn:prison_guard8_key_mould` | — |
| 91 | 物品 | 塑料钥匙（莱斯特的衣柜） | `backtothedawn:plastic_key_lester_wardrobe` | `backtothedawn:item_91` |
| 92 | 物品 | 钥匙模具（装备库） | `backtothedawn:key_mold_equipment_room` | `backtothedawn:item_92` |
| 93 | 物品 | 塑料钥匙（装备库） | `backtothedawn:plastic_key_equipment_room` | `backtothedawn:item_93` |
| 94 | 物品 | 钥匙模具（药房） | `backtothedawn:key_mold_pharmacy` | `backtothedawn:item_94` |
| 95 | 物品 | 塑料钥匙（药房） | `backtothedawn:plastic_key_pharmacy` | `backtothedawn:item_95` |
| 96 | 物品 | 腐蚀溶液 | `backtothedawn:corrosive` | — |
| 97 | 物品 | 滑翔服左翼 | `backtothedawn:glider_left_wing` | `backtothedawn:item_97` |
| 98 | 物品 | 滑翔服右翼 | `backtothedawn:glider_right_wing` | `backtothedawn:item_98` |
| 99 | 物品 | 滑翔服尾翼 | `backtothedawn:glider_tail` | `backtothedawn:item_99` |
| 100 | 物品 | 肥皂枪 | `backtothedawn:soap_gun` | `backtothedawn:item_100` |
| 101 | 物品 | 计算器 | `backtothedawn:calculator` | — |
| 102 | 物品 | 《花花世界》（全新） | `backtothedawn:magazine_new` | — |
| 103 | 物品 | 《花花世界》（看过） | `backtothedawn:magazine_seen` | — |
| 104 | 物品 | 《花花世界》（翻烂） | `backtothedawn:magazine_rotten` | — |
| 105 | 物品 | 马女郎海报 | `backtothedawn:horse_girl_poster` | `backtothedawn:item_105` |
| 106 | 物品 | 猫女郎海报 | `backtothedawn:cat_girl_poster` | `backtothedawn:item_106` |
| 107 | 物品 | 狐女郎海报 | `backtothedawn:fox_girl_poster` | `backtothedawn:item_107` |
| 108 | 物品 | 兔女郎海报 | `backtothedawn:bunny_girl_poster` | `backtothedawn:item_108` |
| 109 | 物品 | 咖啡磨 | `backtothedawn:coffee_grinder` | — |
| 110 | 物品 | 掌上游戏机 | `backtothedawn:game_console` | — |
| 111 | 物品 | 掌上游戏机 | `backtothedawn:game_console_no_power` | — |
| 112 | 物品 | 手电筒 | `backtothedawn:flashlight` | — |
| 113 | 物品 | 手电筒 | `backtothedawn:flashlight_no_pwer` | — |
| 114 | 物品 | 电池 | `backtothedawn:battery` | — |
| 115 | 物品 | 牙刷 | `backtothedawn:tooth_brush` | — |
| 116 | 物品 | 牙膏 | `backtothedawn:toothpaste` | — |
| 117 | 物品 | 空的牙膏管 | `backtothedawn:empty_toothpaste_tube` | `backtothedawn:item_117` |
| 118 | 物品 | 消毒液 | `backtothedawn:disinfectant` | `backtothedawn:item_118` |
| 119 | 物品 | 除锈剂 | `backtothedawn:rust_remover` | — |
| 120 | 物品 | 火柴 | `backtothedawn:match` | — |
| 121 | 物品 | 胶带 | `backtothedawn:tape` | — |
| 122 | 物品 | 颜料 | `backtothedawn:pigment` | — |
| 123 | 物品 | 钉子 | `backtothedawn:nail` | — |
| 124 | 物品 | 鞋带 | `backtothedawn:shoelace` | `backtothedawn:item_124` |
| 125 | 物品 | 白纸 | `backtothedawn:paper` | — |
| 126 | 物品 | 纸鸽 | `backtothedawn:zh` | — |
| 127 | 物品 | 花束 | `backtothedawn:bouquet` | `backtothedawn:item_127` |
| 128 | 物品 | 胡乱的涂鸦 | `backtothedawn:messy_graffiti` | `backtothedawn:item_128` |
| 129 | 物品 | 简单的漫画 | `backtothedawn:simple_comic` | `backtothedawn:item_129` |
| 130 | 物品 | 精美的画作 | `backtothedawn:fine_painting` | `backtothedawn:item_130` |
| 131 | 物品 | 铅笔 | `backtothedawn:pencil` | — |
| 133 | 物品 | 圆珠笔 | `backtothedawn:ball_point_pen` | — |
| 135 | 物品 | 硬币 | `backtothedawn:coin` | `backtothedawn:item_135` |
| 136 | 物品 | 长螺丝 | `backtothedawn:long_screw` | `backtothedawn:item_136` |
| 137 | 物品 | 简易螺丝刀 | `backtothedawn:basic_screwdriver` | `backtothedawn:item_137` |
| 138 | 物品 | 螺丝刀 | `backtothedawn:bolt_driver` | — |
| 140 | 物品 | 高效螺丝钻 | `backtothedawn:power_screwdriver` | `backtothedawn:item_140` |
| 142 | 物品 | 扳手 | `backtothedawn:wrench` | `backtothedawn:item_142` |
| 143 | 物品 | 汤匙 | `backtothedawn:spoon` | `backtothedawn:item_143` |
| 144 | 物品 | 锋利汤匙 | `backtothedawn:sharpened_spoon` | `backtothedawn:item_144` |
| 145 | 物品 | 简易鹤嘴锄 | `backtothedawn:basic_pickaxe` | `backtothedawn:item_145` |
| 148 | 物品 | 加固鹤嘴锄 | `backtothedawn:reinforced_pickaxe` | `backtothedawn:item_148` |
| 151 | 物品 | 钉锤 | `backtothedawn:hammer` | — |
| 152 | 物品 | 剪刀 | `backtothedawn:scissors` | — |
| 153 | 物品 | 碎玻璃 | `backtothedawn:shattered_glass` | — |
| 154 | 物品 | 玻璃匕首 | `backtothedawn:glass_dagger` | — |
| 156 | 物品 | 牙刷锥刺 | `backtothedawn:ys_shua_zhui_ci` | — |
| 158 | 物品 | 水果刀 | `backtothedawn:fruit_knife` | — |
| 159 | 物品 | 木条 | `backtothedawn:wooden_strip` | `backtothedawn:item_159` |
| 160 | 物品 | 双节棍 | `backtothedawn:nunchaku` | `backtothedawn:item_160` |
| 162 | 物品 | 钉棒 | `backtothedawn:nail_club` | `backtothedawn:item_162` |
| 164 | 物品 | 铁管 | `backtothedawn:iron_pipe` | `backtothedawn:item_164` |
| 165 | 物品 | 警棍 | `backtothedawn:baton` | — |
| 166 | 物品 | 皮带 | `backtothedawn:belt` | — |
| 168 | 物品 | 烤面包 | `backtothedawn:toast_168` | `backtothedawn:item_168` |
| 169 | 物品 | 南瓜粥 | `backtothedawn:pumpkin_porridge_169` | `backtothedawn:item_169` |
| 170 | 物品 | 苹果派 | `backtothedawn:apple_pie_170` | `backtothedawn:item_170` |
| 171 | 物品 | 煎蛋卷 | `backtothedawn:omelet_171` | `backtothedawn:item_171` |
| 172 | 物品 | 手握饭团 | `backtothedawn:rice_ball_172` | `backtothedawn:item_172` |
| 173 | 物品 | 炸薯条 | `backtothedawn:french_fries_173` | `backtothedawn:item_173` |
| 174 | 物品 | 蘑菇意面 | `backtothedawn:mushroom_pasta_174` | `backtothedawn:item_174` |
| 175 | 物品 | 传统蒸饺 | `backtothedawn:steamed_dumplings_175` | `backtothedawn:item_175` |
| 176 | 物品 | 烤面包 | `backtothedawn:toast_176` | `backtothedawn:item_176` |
| 177 | 物品 | 南瓜粥 | `backtothedawn:pumpkin_porridge_177` | `backtothedawn:item_177` |
| 178 | 物品 | 苹果派 | `backtothedawn:apple_pie_178` | `backtothedawn:item_178` |
| 179 | 物品 | 煎蛋卷 | `backtothedawn:omelet_179` | `backtothedawn:item_179` |
| 180 | 物品 | 手握饭团 | `backtothedawn:rice_ball_180` | `backtothedawn:item_180` |
| 181 | 物品 | 炸薯条 | `backtothedawn:french_fries_181` | `backtothedawn:item_181` |
| 182 | 物品 | 蘑菇意面 | `backtothedawn:mushroom_pasta_182` | `backtothedawn:item_182` |
| 183 | 物品 | 传统蒸饺 | `backtothedawn:steamed_dumplings_183` | `backtothedawn:item_183` |
| 189 | 物品 | 狱警制服 | `backtothedawn:prison_guard_uniform` | — |
| 190 | 物品 | 警哨 | `backtothedawn:police_whistle` | — |
| 191 | 物品 | 洗衣房钥匙 | `backtothedawn:laundry_room_key` | `backtothedawn:item_191` |
| 192 | 物品 | 鞋子里的钥匙 | `backtothedawn:key_in_shoe` | `backtothedawn:item_192` |
| 193 | 物品 | 沉甸甸的钥匙 | `backtothedawn:heavy_key` | — |
| 194 | 物品 | 给贝丝的情书 | `backtothedawn:letter_for_doctor` | — |
| 195 | 物品 | 给玛姬的情书 | `backtothedawn:letter_for_girl_friend` | — |
| 196 | 物品 | 赞美监狱的文章 | `backtothedawn:article_praising_prison` | — |
| 197 | 物品 | 小说手稿 | `backtothedawn:letter_for_fiction` | — |
| 198 | 物品 | 徒手挖掘 | `backtothedawn:barehanded_digging` | `backtothedawn:item_198` |
| 199 | 物品 | 徒手拆卸 | `backtothedawn:barehanded_disassembly` | `backtothedawn:item_199` |
| 200 | 物品 | 发霉的面包 | `backtothedawn:moldy_bread` | `backtothedawn:item_200` |
| 201 | 物品 | 徒手开锁 | `backtothedawn:tu_shou_kai_suo` | — |
| 202 | 物品 | 金龟子 | `backtothedawn:scarab` | — |
| 203 | 物品 | 《森之音》 | `backtothedawn:szz` | — |
| 204 | 物品 | 《森之音》（改造） | `backtothedawn:sen_zhi_yin_processed` | — |
| 205 | 物品 | 神秘纸袋 | `backtothedawn:mystery_paper_bag` | — |
| 209 | 物品 | DEMO限定纸鹤 | `backtothedawn:demo_paper_crane` | `backtothedawn:item_209` |
| 210 | 物品 | 蓝色账簿 | `backtothedawn:bei_yong_zhang_bu` | — |
| 211 | 物品 | 绷带 | `backtothedawn:bandage` | `backtothedawn:item_211` |
| 212 | 物品 | 指虎 | `backtothedawn:zhi_hu` | — |
| 213 | 物品 | 毁灭者 | `backtothedawn:destroyer` | `backtothedawn:item_213` |
| 214 | 物品 | 精致叶卷 | `backtothedawn:jing_zhi_ye_juan` | — |
| 215 | 物品 | 跑鞋 | `backtothedawn:running_shoes` | `backtothedawn:item_215` |
| 216 | 物品 | 皮鞋 | `backtothedawn:leather_shoes` | `backtothedawn:item_216` |
| 217 | 物品 | 帆布鞋 | `backtothedawn:canvas_shoes` | `backtothedawn:item_217` |
| 218 | 物品 | 凉鞋 | `backtothedawn:sandals` | — |
| 219 | 物品 | 朋克墨镜 | `backtothedawn:punk_sunglasses` | — |
| 220 | 物品 | 极客墨镜 | `backtothedawn:geek_sunglasses` | `backtothedawn:item_220` |
| 221 | 物品 | 反光眼镜 | `backtothedawn:reflective_glasses` | `backtothedawn:item_221` |
| 222 | 物品 | 防摔眼镜 | `backtothedawn:anti_shatter_glasses` | `backtothedawn:item_222` |
| 223 | 物品 | 中锋头带 | `backtothedawn:center_headband` | `backtothedawn:item_223` |
| 224 | 物品 | 后卫头带 | `backtothedawn:guard_headband` | `backtothedawn:item_224` |
| 225 | 物品 | 雄鹰球帽 | `backtothedawn:eagle_cap` | `backtothedawn:item_225` |
| 226 | 物品 | 首脑球帽 | `backtothedawn:leader_cap` | `backtothedawn:item_226` |
| 227 | 物品 | 加厚棉帽 | `backtothedawn:padded_cotton_hat` | `backtothedawn:item_227` |
| 228 | 物品 | 针织线帽 | `backtothedawn:knitted_wool_hat` | `backtothedawn:item_228` |
| 229 | 物品 | 电子表 | `backtothedawn:electronic_watch` | — |
| 230 | 物品 | 多功能手表 | `backtothedawn:multifunction_watch` | `backtothedawn:item_230` |
| 231 | 物品 | 石刻护身符 | `backtothedawn:shi_ke__hu_shen_fu` | — |
| 232 | 物品 | 木雕护身符 | `backtothedawn:mu_diao__hu_shen_fu` | — |
| 233 | 物品 | 臼齿项链 | `backtothedawn:molar_necklace` | `backtothedawn:item_233` |
| 234 | 物品 | 门齿项链 | `backtothedawn:incisor_necklace` | `backtothedawn:item_234` |
| 235 | 物品 | 运动腰带 | `backtothedawn:sports_belt` | `backtothedawn:item_235` |
| 236 | 物品 | 酒葫芦 | `backtothedawn:jiu_hu_lu__shui` | — |
| 237 | 物品 | 酒葫芦 | `backtothedawn:jiu_hu_lu__jiu` | — |
| 238 | 物品 | “另一本账簿” | `backtothedawn:fake_ledger` | — |
| 239 | 物品 | 酒吧广告卡 | `backtothedawn:bar_card` | — |
| 240 | 物品 | “优先探视权” | `backtothedawn:priority_visiting_right` | — |
| 241 | 物品 | 回忆的照片 | `backtothedawn:student_photo` | — |
| 242 | 物品 | 照片/地图 | `backtothedawn:student_photo_map` | — |
| 243 | 物品 | 收发室通行证 | `backtothedawn:mail_room_pass` | — |
| 244 | 物品 | 洗衣房工作许可 | `backtothedawn:laundry_room_licence` | — |
| 245 | 物品 | 收发室工作卡 | `backtothedawn:mail_room_licence` | — |
| 246 | 物品 | 拿不走的铅笔 | `backtothedawn:non_removable_pencil` | `backtothedawn:item_246` |
| 247 | 物品 | 仓库门备用密码 | `backtothedawn:mail_room_backup_password` | — |
| 248 | 物品 | 诊疗室备用密码 | `backtothedawn:infirmary_backup_password` | `backtothedawn:item_248` |
| 249 | 物品 | 仓库密码的痕迹 | `backtothedawn:warehouse_pwd_trace` | — |
| 250 | 物品 | 大爆炸订餐卡 | `backtothedawn:da_bao_zha_dck` | — |
| 251 | 物品 | 仓库门密码 | `backtothedawn:warehouse_door_password` | `backtothedawn:item_251` |
| 252 | 物品 | 雷德的信 | `backtothedawn:letter_from_lei_de` | — |
| 253 | 物品 | 透明溶液 | `backtothedawn:transparent_solution` | — |
| 254 | 物品 | 高温的熨斗 | `backtothedawn:hot_iron` | `backtothedawn:item_254` |
| 255 | 物品 | 健康证明 | `backtothedawn:health_certificate` | — |
| 256 | 物品 | 燃烧瓶 | `backtothedawn:ran_shao_ping` | — |
| 257 | 物品 | 精酿燃烧瓶 | `backtothedawn:jing_niang_ran_shao_ping` | — |
| 258 | 物品 | 监狱楼地图 | `backtothedawn:hall_map` | — |
| 259 | 物品 | 操场地图 | `backtothedawn:playground_map` | — |
| 260 | 物品 | 小铁片 | `backtothedawn:small_iron_piece` | `backtothedawn:item_260` |
| 261 | 物品 | 拳赛下注单 | `backtothedawn:boxing_bet_bill` | — |
| 262 | 物品 | 球赛下注单 | `backtothedawn:ball_bet_bill` | — |
| 263 | 物品 | 玛姬的汇款 | `backtothedawn:remit_money_by_maji` | — |
| 264 | 物品 | 大爆炸优惠券 | `backtothedawn:pizza_coupon_coupon` | — |
| 265 | 物品 | 大乐透彩票 | `backtothedawn:lottery_ticket` | — |
| 266 | 属性 | 彩票账户余额 | `backtothedawn:lottery_account_balance` | `backtothedawn:item_266` |
| 267 | 物品 | 已中奖彩票 | `backtothedawn:prize_cashed_ticket` | — |
| 268 | 物品 | 彩票奖金汇款 | `backtothedawn:bonus_remittance` | — |
| 269 | 物品 | 限定款汽水 | `backtothedawn:limited_edition_soda` | `backtothedawn:item_269` |
| 270 | 物品 | 手工香皂 | `backtothedawn:handmade_soap` | — |
| 271 | 物品 | 复合维生素片 | `backtothedawn:multivitamin_tablet` | `backtothedawn:item_271` |
| 272 | 物品 | 焦糖威化饼干 | `backtothedawn:caramel_wafer` | `backtothedawn:item_272` |
| 273 | 物品 | 背光外设 | `backtothedawn:bei_guang_wai_she` | — |
| 274 | 物品 | 筹码 | `backtothedawn:chips` | — |
| 275 | 物品 | 给妮可的“情书” | `backtothedawn:letter_for_nike` | — |
| 276 | 物品 | 冰啤酒 | `backtothedawn:cold_beer` | — |
| 277 | 物品 | 屋顶工作许可 | `backtothedawn:roof_licence` | — |
| 278 | 物品 | 旧工装鞋 | `backtothedawn:old_work_shoes` | `backtothedawn:item_278` |
| 279 | 物品 | 细木杆 | `backtothedawn:thin_wooden_rod` | `backtothedawn:item_279` |
| 280 | 物品 | “有用的”情报 | `backtothedawn:topic` | — |
| 281 | 物品 | 教堂义工许可 | `backtothedawn:church_volunteer_worker` | — |
| 282 | 物品 | 筹款倡议书 | `backtothedawn:raise_funds_proposal` | — |
| 283 | 物品 | 绒布善款袋 | `backtothedawn:contributions_bag` | — |
| 284 | 物品 | 掌纹复制套件 | `backtothedawn:sampling_palmprint_tool` | — |
| 285 | 物品 | 安德森的掌纹膜 | `backtothedawn:priest_palmprint` | — |
| 286 | 物品 | 里卡多的电话号码 | `backtothedawn:li_kaduo_phone_number` | — |
| 287 | 物品 | 空的泡面袋 | `backtothedawn:spicy_noodles_bag` | — |
| 288 | 物品 | 快干胶 | `backtothedawn:quick_drying_glue` | `backtothedawn:item_288` |
| 289 | 物品 | 碘酒 | `backtothedawn:iodine` | — |
| 290 | 物品 | 写有签名的棒球 | `backtothedawn:autographed_baseball` | `backtothedawn:item_290` |
| 291 | 物品 | 富兰克林破解装置 | `backtothedawn:zebra_disable_device` | — |
| 292 | 物品 | 破解装置使用说明 | `backtothedawn:zebra_disable_deviceinstructions` | — |
| 293 | 物品 | 富兰克林的字条 | `backtothedawn:zebra_message` | — |
| 294 | 物品 | 布鲁的字条 | `backtothedawn:prison_guard_captain_message` | — |
| 295 | 物品 | 神父的光盘 | `backtothedawn:priest_cattle_dog_cd` | — |
| 296 | 物品 | 厨房工作许可 | `backtothedawn:kitchen_licence` | — |
| 297 | 物品 | 特硬咸鱼 | `backtothedawn:extra_hard_salted_fish` | `backtothedawn:item_297` |
| 298 | 物品 | 被“贱卖”的钥匙 | `backtothedawn:dcd_key` | — |
| 299 | 物品 | 奶酪 | `backtothedawn:cheese` | — |
| 300 | 物品 | 腌黄瓜 | `backtothedawn:pickle_cucumber` | — |
| 301 | 物品 | 圆石城地图 | `backtothedawn:city_map` | — |
| 302 | 物品 | 螺丝刀 | `backtothedawn:screwdriver` | `backtothedawn:item_302` |
| 303 | 物品 | 塞琳娜公寓的钥匙 | `backtothedawn:sai_lin_na_apartkey` | — |
| 304 | 物品 | 诊疗室门备用密码 | `backtothedawn:treatment_room_backup_password` | — |
| 305 | 物品 | 贝丝的诊疗计划 | `backtothedawn:doctor_treatment_plan` | — |
| 306 | 物品 | 贝丝的生日 | `backtothedawn:doctor_birthday_by_gift` | — |
| 307 | 物品 | 贝丝的生日 | `backtothedawn:doctor_birthday_by_concert` | — |
| 308 | 物品 | 委托的照片 | `backtothedawn:delegated_photo` | — |
| 309 | 物品 | 女神侧身像 | `backtothedawn:dream_girl_profile` | — |
| 310 | 物品 | 棋局图纸 | `backtothedawn:chess_game_blueprint` | — |
| 311 | 物品 | 破局之法 | `backtothedawn:chess_game_methods_cracking` | — |
| 312 | 物品 | 电视台工作卡 | `backtothedawn:tv_station_card` | — |
| 313 | 物品 | 罪证录音 | `backtothedawn:criminal_evidence_recording` | — |
| 314 | 物品 | 匕首 | `backtothedawn:dagger` | `backtothedawn:item_314` |
| 315 | 物品 | 特调香水 | `backtothedawn:special_perfume` | — |
| 316 | 物品 | 小块皂石 | `backtothedawn:xiao_kuai_zao_shi` | — |
| 317 | 物品 | “假”释证明 | `backtothedawn:parole_certificate` | — |
| 318 | 物品 | 监狱信纸 | `backtothedawn:prison_paper` | — |
| 319 | 物品 | 《电影藏经阁》光盘 | `backtothedawn:movie_cd` | — |
| 320 | 物品 | 撕下的书页 | `backtothedawn:tear_off_page` | — |
| 321 | 物品 | 厨房地窖结构图 | `backtothedawn:kitchen_cellar_blueprint` | — |
| 322 | 物品 | 粉色口红 | `backtothedawn:pink_rouge` | — |
| 323 | 物品 | 厨师的钥匙 | `backtothedawn:chef_key` | — |
| 324 | 物品 | 塑料钥匙（厨师） | `backtothedawn:plastic_chef_key` | — |
| 325 | 物品 | 塑料钥匙（贱卖） | `backtothedawn:plastic_key_cheap` | — |
| 326 | 物品 | 钥匙模具（厨师） | `backtothedawn:chef_key_mould` | — |
| 327 | 物品 | 钥匙模具（贱卖） | `backtothedawn:cheap_key_mould` | — |
| 328 | 物品 | 塑料钥匙（鞋子） | `backtothedawn:plastic_key_shoes` | `backtothedawn:item_328` |
| 329 | 物品 | 塑料钥匙（沉甸甸） | `backtothedawn:plastic_key_heavy_object` | `backtothedawn:item_329` |
| 330 | 物品 | 塑料钥匙（赛琳娜） | `backtothedawn:plastic_key_selena` | `backtothedawn:item_330` |
| 331 | 物品 | 钥匙模具（鞋子） | `backtothedawn:key_mold_shoes` | `backtothedawn:item_331` |
| 332 | 物品 | 钥匙模具（沉甸甸） | `backtothedawn:key_mold_heavy_object` | `backtothedawn:item_332` |
| 333 | 物品 | 钥匙模具（赛琳娜） | `backtothedawn:key_mold_selena` | `backtothedawn:item_333` |
| 334 | 物品 | 彩绘纸鸽 | `backtothedawn:paintings_zh` | — |
| 335 | 物品 | 灰色的狼毛 | `backtothedawn:wolf_hair` | — |
| 336 | 物品 | 《杀手之死》光盘 | `backtothedawn:movie_cd336` | — |
| 337 | 物品 | “爱心”硬币 | `backtothedawn:lucky_coin` | — |
| 338 | 物品 | 《柯里昂家族》光盘 | `backtothedawn:movie_cd338` | — |
| 339 | 物品 | 《没人会注意你的鞋》光盘 | `backtothedawn:movie_cd339` | — |
| 340 | 物品 | 《宇宙战争》光盘 | `backtothedawn:movie_cd340` | — |
| 341 | 物品 | 《间谍任务》光盘 | `backtothedawn:movie_cd341` | — |
| 342 | 物品 | 《欢迎来到霍金斯》光盘 | `backtothedawn:movie_cd342` | — |
| 343 | 物品 | 命运骰子 | `backtothedawn:guan_qian_tou_zi` | — |
| 344 | 物品 | 实习记者证 | `backtothedawn:shi_xi_ji_zhe_zheng` | — |
| 345 | 物品 | 录像带#1 | `backtothedawn:videotape1` | — |
| 346 | 物品 | 录像带#2 | `backtothedawn:videotape2` | — |
| 347 | 物品 | 录像带#3 | `backtothedawn:videotape3` | — |
| 348 | 物品 | 全麦面包 | `backtothedawn:wheat_bread` | — |
| 349 | 物品 | 牛油果奶昔 | `backtothedawn:avocado_shake` | — |
| 350 | 物品 | 奶酪大会披萨 | `backtothedawn:cheese_convention_pizza` | — |
| 351 | 物品 | 冰咖啡 | `backtothedawn:iced_coffee` | — |
| 352 | 物品 | 三文鱼塔塔 | `backtothedawn:salmon_tartar` | — |
| 353 | 物品 | 蔬菜浓汤 | `backtothedawn:vegetable_soup` | — |
| 354 | 物品 | 香槟 | `backtothedawn:champagne` | — |
| 355 | 物品 | 金羊毛牌卷烟 | `backtothedawn:golden_fleece_cigarette` | — |
| 356 | 物品 | 拳赛奖杯 | `backtothedawn:ultimate_fight_trophy` | — |
| 357 | 物品 | 警卫活动室工作许可 | `backtothedawn:guard_lounge_licence` | — |
| 358 | 物品 | 花盆底下的钥匙 | `backtothedawn:key_under_flowerpot` | `backtothedawn:item_358` |
| 359 | 物品 | 《鱼美人与美人鱼》光盘 | `backtothedawn:movie_cd359` | — |
| 360 | 物品 | 胶鞋 | `backtothedawn:galoshes` | `backtothedawn:rubber_shoes` |
| 361 | 物品 | 强效钙片 | `backtothedawn:qiang_xiao_gai_pian` | — |
| 362 | 物品 | 增高鞋垫 | `backtothedawn:heightening_insole` | — |
| 363 | 物品 | 警用防护手套 | `backtothedawn:police_gloves` | — |
| 364 | 物品 | 酒吧广告卡 | `backtothedawn:bar_card_unknow` | — |
| 367 | 物品 | 磨损的套筒 | `backtothedawn:sleeve` | — |
| 368 | 物品 | 生锈的摇把 | `backtothedawn:crank` | — |
| 369 | 物品 | 完整的把手 | `backtothedawn:handle` | — |
| 370 | 物品 | 自制的把手 | `backtothedawn:homemade_handle` | — |
| 371 | 物品 | 太阳吊坠 | `backtothedawn:pendant` | — |
| 372 | 物品 | 印有编号的帆布块 | `backtothedawn:canvas_with_number_blood` | — |
| 373 | 物品 | 拼贴画《蓝白红》 | `backtothedawn:pasteup_bwr` | — |
| 374 | 物品 | 附带拼贴画的信 | `backtothedawn:letter_with_pasteup` | — |
| 375 | 物品 | 芭芭拉的电话号码 | `backtothedawn:phone_number_ba_ba_la` | — |
| 376 | 物品 | 比利藏起的钥匙串 | `backtothedawn:key_for_billy_hide` | — |
| 377 | 物品 | 美容磨砂粉 | `backtothedawn:mo_sha_fen` | — |
| 378 | 物品 | 亡者的军籍牌 | `backtothedawn:badge_of_deceased` | — |
| 379 | 物品 | 便携氧气瓶（空） | `backtothedawn:portable_oxygen_tank_empty` | — |
| 380 | 物品 | 便携氧气瓶（满） | `backtothedawn:portable_oxygen_tank` | — |
| 381 | 物品 | 小金鱼 | `backtothedawn:little_gold_fish` | — |
| 382 | 物品 | 医院诊疗许可 | `backtothedawn:hospital_treatment_permit` | — |
| 383 | 物品 | 调频通话器 | `backtothedawn:walkie_talkie` | `backtothedawn:item_383` |
| 389 | 物品 | 撬棍 | `backtothedawn:crowbar` | — |
| 390 | 物品 | 大白鲨海报 | `backtothedawn:shark_pinup` | — |
| 391 | 物品 | 洛奇寄来的小包裹 | `backtothedawn:shar_pei_bag` | — |
| 392 | 物品 | 卫斯理的录音手表 | `backtothedawn:cheetah_recording_watch` | — |
| 393 | 物品 | 联邦洗衣店广告卡 | `backtothedawn:federal_laundry_ad_card` | — |
| 394 | 物品 | 监狱地图 | `backtothedawn:camouflage_prison_map` | — |
| 395 | 物品 | 托马斯的钱包 | `backtothedawn:thomas_wallet` | `backtothedawn:item_395` |
| 396 | 物品 | 机车手套 | `backtothedawn:motorcycle_gloves` | — |
| 397 | 物品 | 哈利医生的门禁卡 | `backtothedawn:labrador_entrance_card` | — |
| 398 | 属性 | 病例观察素材 | `backtothedawn:case_observation_material` | `backtothedawn:item_398` |
| 399 | 物品 | 精神评估证明 | `backtothedawn:proof_of_mental_assessment` | — |
| 400 | 物品 | 医生的门禁卡编号 | `backtothedawn:labrador_entrance_card_number` | — |
| 401 | 物品 | 匿名寄送的小包裹 | `backtothedawn:clinic_card_package` | — |
| 402 | 物品 | 外形普通的梳子 | `backtothedawn:package_comb` | — |
| 403 | 物品 | 安慰剂 | `backtothedawn:placebo` | `backtothedawn:item_403` |
| 404 | 物品 | 麻醉针 | `backtothedawn:anesthetic_needle` | — |
| 405 | 物品 | 特制工具 | `backtothedawn:special_tool__richard` | — |
| 406 | 物品 | 内六角螺丝刀头 | `backtothedawn:special_tool__screw` | — |
| 407 | 物品 | 金属双头连接杆 | `backtothedawn:special_tool__metalbar` | — |
| 408 | 物品 | 短柄铲头撬棍 | `backtothedawn:special_tool__crowbar` | — |
| 409 | 物品 | 特制工具 | `backtothedawn:special_tool__franklin` | — |
| 410 | 物品 | 蓄电池-正极 | `backtothedawn:battery_p` | — |
| 411 | 物品 | 蓄电池-负极 | `backtothedawn:battery_n` | — |
| 412 | 物品 | 特制工具 | `backtothedawn:special_tool__franklin__finish` | — |
| 413 | 物品 | 特制午餐 | `backtothedawn:fenrir_lunch` | — |
| 414 | 物品 | 特制晚餐 | `backtothedawn:fenrir_dinner` | — |
| 415 | 物品 | 体征检测器 | `backtothedawn:physical_detector` | — |
| 416 | 物品 | 滑翔翼蒙布 | `backtothedawn:hang_glider_cloth` | — |
| 417 | 物品 | 滑翔翼龙骨 | `backtothedawn:hang_gliderkeel` | — |
| 418 | 物品 | 滑翔翼三脚架 | `backtothedawn:hang_glider_tripod` | — |
| 419 | 物品 | 滑翔翼吊带 | `backtothedawn:hang_glider_harness` | — |
| 420 | 物品 | 生锈的手术刀 | `backtothedawn:rusty_scalpel` | — |
| 421 | 物品 | 手术刀 | `backtothedawn:scalpel` | — |
| 422 | 物品 | 随身携带的警员证 | `backtothedawn:jing_yuan_zheng` | — |
| 423 | 物品 | 餐盘 | `backtothedawn:plate` | — |
| 424 | 物品 | 上锁的小铁盒 | `backtothedawn:locked_box1` | — |
| 425 | 物品 | 上锁的小铁盒 | `backtothedawn:locked_box2` | — |
| 426 | 物品 | 小瓶乙醚 | `backtothedawn:vial_of_ether` | — |
| 427 | 物品 | 神秘的纸箱 | `backtothedawn:supporter_dlc_package` | — |
| 428 | 物品 | 图案特别的骰子 | `backtothedawn:special_pattern_dice` | — |
| 429 | 物品 | 囚室装修设计图册 | `backtothedawn:cell_design_catalogue` | — |
| 430 | 物品 | 《兽出重围》音乐磁带 | `backtothedawn:old_music_tape` | — |
| 431 | 物品 | 《无间行者》海报 | `backtothedawn:post__panther` | — |
| 432 | 物品 | 《新闻聚焦》海报 | `backtothedawn:post__fox` | — |
| 433 | 物品 | 《房间入口》光盘 | `backtothedawn:cd__romm_entrance` | — |
| 434 | 物品 | 来自铁头工作室的信 | `backtothedawn:note__thanks_ea` | — |
| 435 | 物品 | 大头“大头贴” | `backtothedawn:photo_sticker` | — |
| 436 | 物品 | 帆布储物篮 | `backtothedawn:four_hanging_bag` | — |
| 437 | 物品 | 北方山麓风景画报 | `backtothedawn:northern_foothills_magazine` | `backtothedawn:item_437` |
| 438 | 物品 | 凯文的欠条 | `backtothedawn:iou_of_kevin` | — |
| 439 | 物品 | 委托人的欠条 | `backtothedawn:client_iou` | `backtothedawn:item_439` |
| 440 | 物品 | 诊疗室密码 | `backtothedawn:doctor_birthday_by_pass_word` | — |
| 441 | 物品 | 土拨鼠徽章 | `backtothedawn:marmot_badge` | — |
| 442 | 物品 | 铁头 | `backtothedawn:metal_head` | — |
| 443 | 物品 | 三只小猪扑克 | `backtothedawn:three_pigs_poker` | — |
| 444 | 物品 | “对话框”贴纸 | `backtothedawn:dialogbox_sticker` | — |

### 使用说明

- 以前使用 `backtothedawn:item_<ID>` 的模组仍然可用；这些键保留为兼容别名。
- 已有静态 `ItemID` 名称的物品保留原键；没有静态名称的条目使用固定英文语义键，例如 `backtothedawn:platform_boots`。
- 同一数字 ID 若有多个静态名称，首个名称是规范键，其余名称是兼容别名。
- 游戏显示名可能重复；请用 `ItemKey` 区分物品。

统计：406 条运行时记录，391 个游戏物品，15 条属性记录，126 个条目使用新的可读键并保留旧数字别名。
