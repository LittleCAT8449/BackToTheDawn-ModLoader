# Back To The Dawn 解包与 Hook 分析手册

本文记录当前 Steam 版本的解包结果、证据来源、分析工具和已经确认的 Hook 目标。
它的目标不是还原游戏源码，而是建立一条可复现的“文件 → 类型 → 方法 → 运行时验证”证据链，
避免根据猜测或 AssetRipper 生成的空方法体直接修改游戏逻辑。

文档基线：2026-08-17。游戏目录：`F:\SteamLibrary\steamapps\common\MetalHeadGames`。

## 1. 当前结论

| 项目 | 已确认结果 | 证据 |
|---|---|---|
| Steam App ID | `1735700` | `appmanifest_1735700.acf` |
| Steam Build ID | `23125213` | `appmanifest_1735700.acf` |
| Unity | `2020.3.2f1c1` | AssetRipper 日志、`ProjectVersion.txt`、BepInEx 启动日志 |
| 脚本后端 | IL2CPP | AssetRipper 日志、BepInEx IL2CPP 预加载日志 |
| IL2CPP Metadata | `27.1` | AssetRipper 日志 |
| 平台 | Windows x64 | Unity/BepInEx 启动日志 |
| 主场景 | `Game`，build index `0` | `EditorBuildSettings.asset`、运行时 `Scene loaded` 日志 |
| 场景数量 | 当前导出项目只有一个启用场景 | `EditorBuildSettings.asset` |
| 运行时入口 | `Back To The Dawn.exe` → Doorstop → BepInEx IL2CPP | `doorstop_config.ini` |

最重要的限制是：这是 IL2CPP 游戏。AssetRipper 能还原类型、字段、属性和方法签名，
但生成的 `Assets/Scripts/Assembly-CSharp/*.cs` 方法体通常只是 `return null`、`return 0`
或空实现。这些返回值不是游戏真实行为，不能作为 Hook 逻辑依据。

实际 Hook 以运行时生成的 `BepInEx/interop/Assembly-CSharp.dll` 为准，运行效果还要通过
`BepInEx/LogOutput.log` 验证。

## 2. 原始文件与目录职责

```text
MetalHeadGames/
├─ Back To The Dawn.exe                         Unity 启动程序
├─ GameAssembly.dll                             IL2CPP 原生代码
├─ Back To The Dawn_Data/
│  ├─ globalgamemanagers                       全局 Unity 对象和场景元数据
│  ├─ level0                                    原始场景文件
│  └─ il2cpp_data/Metadata/global-metadata.dat  IL2CPP 类型/方法元数据
├─ BepInEx/
│  ├─ interop/Assembly-CSharp.dll               运行时 IL2CPP 互操作程序集
│  ├─ interop/Assembly-CSharp-firstpass.dll     firstpass 类型（含 ThingChangeReason）
│  ├─ core/                                     BepInEx、Harmony、Il2CppInterop
│  └─ LogOutput.log                             运行时验证日志
├─ analysis/
│  ├─ AssetRipper.log                            解包过程和版本证据
│  └─ AssetRipperProject/                        AssetRipper 导出项目
├─ tools/AssemblyInspector                     Mono.Cecil 类型/方法检查工具
└─ docs/                                        本项目的分析与 API 文档
```

当前文件大小可以用于确认是否误用了另一份游戏文件：

```text
GameAssembly.dll                                  38,693,888 bytes
global-metadata.dat                                8,754,204 bytes
BepInEx/interop/Assembly-CSharp.dll                21,473,280 bytes
BepInEx/interop/Assembly-CSharp-firstpass.dll       1,548,448 bytes
```

文件大小不是版本识别的唯一依据，更新游戏后应同时检查 Build ID、Unity 版本和程序集哈希。

## 3. AssetRipper 解包记录

解包日志位于 `analysis/AssetRipper.log`。本次使用的关键设置和结果如下：

```text
AssetRipper Version: 1.3.14.0
ScriptContentLevel: Level2
StreamingAssetsMode: Extract
ScriptExportMode: Hybrid
ScriptLanguageVersion: AutoSafe
Target Unity: 2020.3.2f1c1
Scripting backend: IL2Cpp
Metadata version: 27.1
```

日志显示：

- 成功找到 `globalgamemanagers`、`level0` 和 StreamingAssets。
- Cpp2IL 处理了 `81,853` 个方法定义映射。
- 总导出进度为 `372,729` 个资源。
- 导出项目包含 `Assets/Scenes/Game.unity`。
- `ProjectSettings/EditorBuildSettings.asset` 只列出一个启用场景：
  `Assets/Scenes/Game.unity`。
- `Assets/Scripts/Assembly-CSharp` 当前约有 `3,278` 个 `.cs` 文件。

AssetRipper 导出的价值主要有三类：

1. Unity 场景、Prefab、材质、贴图、文本和配置资源。
2. 游戏类的可读名称、字段、属性和方法签名。
3. 场景对象层级和序列化字段线索。

它不能替代运行时程序集检查，也不能证明某个方法的真实执行顺序。

## 4. 运行时程序集检查

项目中的 `tools/AssemblyInspector` 使用 Mono.Cecil 读取程序集，不加载游戏进程，适合做只读检查。
如果系统没有全局 .NET 运行时，可以使用项目内 SDK：

```powershell
$dotnet = '.\.tools\dotnet\dotnet.exe'
$inspector = '.\tools\bin\Release\net6.0\AssemblyInspector.dll'
$assembly = '.\BepInEx\interop\Assembly-CSharp.dll'

& $dotnet $inspector $assembly GameManage GameProcess TimeManage Map MapManage
```

按名称搜索类型：

```powershell
& $dotnet $inspector $assembly --find Character Player Attribute GameManage GameProcess
```

`ThingChangeReason` 位于 firstpass 程序集：

```powershell
$firstpass = '.\BepInEx\interop\Assembly-CSharp-firstpass.dll'
& $dotnet $inspector $firstpass ThingChangeReason
```

检查时必须记录三项：程序集路径、类型全名、方法参数列表。只记录“方法名”不足以区分重载。

## 5. 核心类型关系

```text
GameManage
├─ 启动步骤：StartGameGoToNextStep()
├─ 主菜单：ShowGameStartUI(bool, int)
├─ 读档：ReadArchiveDataAndStartGame(int)
└─ 可操作状态：ShowCurrentMapAndCanControl()

GameProcess.singleton
└─ 时间推进：PassMinutes(int, bool) / PassMinutes(TimePass)

TimeManage
└─ nowTime（当前游戏时间）

MapManage
├─ currentMap
└─ previousMapId

CharacterManage
└─ protagonistAttribute
   └─ CharacterAttribute：health、mentality、satiety、energy、focus、money

ThingPackage
├─ cId（所属角色 ID）
├─ ChangeEnergy(int, ThingChangeReason)
├─ ChangeMoney(int, ThingChangeReason)
├─ ChangeHealthy(int, ThingChangeReason)
├─ ChangeMentality(int, ThingChangeReason)
├─ ChangeSatiety(int, ThingChangeReason)
└─ ChangeFocus(int, ThingChangeReason)
```

物品和效果还可能先经过以下属性分发入口，再调用具体的 `Change*` 方法：

```text
ThingPackage.AddAttirbute(ItemType, int, ThingChangeReason)
ThingPackage.ChangeAttirbute(ItemType, int, ThingChangeReason)
ThingPackage.SetAttirbute(ItemType, int)
```

物品使用本身的入口是：

```text
CharacterAttribute.UseItem(int itemId, int useCount, ThingChangeReason reason)
```

它比属性变化 Hook 更适合表达“玩家使用了物品”，因为物品可能被上限、条件或效果类型抵消，
最终不一定改变当前公开的玩家快照。

其中 `CharacterAttribute` 的属性是读取入口，`ThingPackage.Change*` 是更适合观察属性变化的高层入口。
直接 Hook 每个属性 getter 会产生高频调用，且无法表达“这次变化由什么游戏行为触发”。

## 6. 已确认的方法签名

以下签名来自运行时 `Assembly-CSharp.dll`，不是从空方法体推断出来的：

| 类型 | 方法 | 参数 | 当前用途 |
|---|---|---|---|
| `GameManage` | `ShowGameStartUI` | `bool, int` | 主菜单入口 |
| `GameManage` | `ReadArchiveDataAndStartGame` | `int` | 读档前后边界 |
| `GameManage` | `StartGameGoToNextStep` | 无 | 启动步骤变化 |
| `GameManage` | `ShowCurrentMapAndCanControl` | 无 | 进入可操作游戏 |
| `GameProcess` | `PassMinutes` | `int, bool` | 时间推进重载 1 |
| `GameProcess` | `PassMinutes` | `TimePass` | 时间推进重载 2 |
| `Map` | `FocusMap` | 无 | 地图获得焦点 |
| `ThingPackage` | `ChangeEnergy` | `int, ThingChangeReason` | 精力变化 |
| `ThingPackage` | `ChangeMoney` | `int, ThingChangeReason` | 金钱变化 |
| `ThingPackage` | `ChangeHealthy` | `int, ThingChangeReason` | 健康/生命变化 |
| `ThingPackage` | `ChangeMentality` | `int, ThingChangeReason` | 心态变化 |
| `ThingPackage` | `ChangeSatiety` | `int, ThingChangeReason` | 饱食变化 |
| `ThingPackage` | `ChangeFocus` | `int, ThingChangeReason` | 专注变化 |
| `ThingPackage` | `AddAttirbute` | `ItemType, int, ThingChangeReason` | 属性增加分发 |
| `ThingPackage` | `ChangeAttirbute` | `ItemType, int, ThingChangeReason` | 属性变化分发 |
| `ThingPackage` | `SetAttirbute` | `ItemType, int` | 属性设置分发 |
| `ThingPackage` | `UseThing` / `UseBatchThing` | `Thing, UseThingReason` / `Thing, int, UseThingReason` | 背包扣除前的物品使用边界 |
| `ThingPackage` | `AddItem` / `AddItemOneByOne` | `int, int, PlaceType, ThingChangeReason` | 背包新增 |
| `ThingPackage` | `ReduceItem` | `int, int, ThingChangeReason, bool` / `int, int, PlaceType, ThingChangeReason, bool` | 按 ID 扣除 |
| `ThingPackage` | `ReduceThingCount` / `RemoveThing` | `Thing, int, ThingChangeReason` / `Thing` | 按实例扣除或移除 |
| `CharacterAttribute` | `UseItem` | `int, int, ThingChangeReason` | 物品使用完成 |
| `WidgetItemMiddleTools` | `ArrangePocketItemList` | 无 | 口袋整理 |
| `WidgetItemOperationButton` | `SubmitConfirmDestoryItem` / `SubmitConfirmDestoryOneItem` | `int` / 无 | 口袋物品摧毁确认 |
| `CharacterAttribute` | `EquipmentItem` / `RemoveEquipmentItem` | `Thing` | 装备 / 卸下 |
| `ThingPackage` | `MoveThingPlace` | `Thing, PlaceType` | `Equipment → Pocket` 卸下 |
| `WidgetItemOperationButton` | `ClickA` | 无 | 物品菜单操作的兜底入口 |

`ThingChangeReason` 是 firstpass 程序集中的枚举，包含 `Default`、`Sleep`、`Buy`、`BattleSuccess`、
`HourlyConsumeSatiety` 等大量游戏原因。第一版公共事件不直接暴露这个游戏内部枚举，避免让 Mod
必须引用 firstpass 程序集；如果未来需要原因，再设计稳定的字符串或独立枚举映射。

## 7. 当前 Hook 映射

```text
GameManage.StartGameGoToNextStep       Postfix → StartupStepChanged
GameManage.ShowGameStartUI              Prefix  → MainMenuEntered
GameManage.ReadArchiveDataAndStartGame  Prefix  → ArchiveLoadStarted
GameManage.ReadArchiveDataAndStartGame  Postfix → ArchiveLoadInvocationReturned
GameManage.ShowCurrentMapAndCanControl  Postfix → GameplayReady
GameProcess.PassMinutes                 Postfix → TimeChanged（快照去重）
Map.FocusMap                            Postfix → MapChanged（地图 ID 去重）
ThingPackage.UseThing / UseBatchThing   Prefix + Postfix → ItemUseBefore/After（先于背包扣除）
CharacterAttribute.UseItem              Prefix + Postfix → ItemUseBefore/After（直接调用兜底）
ThingPackage.UseThing / UseBatchThing    Prefix + Postfix → InventoryChanged（使用前后快照）
ThingPackage.AddItem / AddItemOneByOne    Prefix + Postfix → InventoryChanged（实际数量差）
ThingPackage.ReduceItem / ReduceThingCount / RemoveThing
                                         Prefix + Postfix → InventoryChanged（实际数量差）
ThingPackage.MoveThingPlace               Prefix + Postfix → InventoryMoved（容器变化）
WidgetItemMiddleTools.ArrangePocketItemList Postfix → PlayerItemAction(Arrange)
CharacterAttribute.EquipmentItem          Postfix → PlayerItemAction(Equip)
CharacterAttribute.RemoveEquipmentItem    Postfix → PlayerItemAction(Unequip)
ThingPackage.MoveThingPlace               Prefix + Postfix → PlayerItemAction(Unequip, Equipment → Pocket)
WidgetItemOperationButton.SubmitConfirm... Postfix → PlayerItemAction(Destroy)
WidgetItemOperationButton.ClickA         Postfix → PlayerItemAction(OperationSelected)
```

物品菜单不要按物品 ID 分别写 Hook。当前版本可以先订阅统一的 `PlayerItemActionEvent`，由
`Action` 区分整理、摧毁、装备、卸下和使用；如果游戏更新后出现未识别的菜单项，先记录
`RawOperationType`，再根据一次运行时日志补充映射。

这些 Hook 都只发布事件或更新只读快照，不修改存档、角色属性、地图或时间。

## 8. 下一阶段的 Hook 设计

根据第 5、6 节的关系，第一批玩家状态 Hook 选择 `ThingPackage.Change*` 的六个高层入口，原因是：

- 方法签名明确，且每个目标只有一个当前重载。
- Postfix 能读取变化后的 `CharacterAttribute` 值。
- 通过 `ThingPackage.cId` 可以只观察主角，避免 NPC 变化刷屏。
- `GameContextAdapter` 可以做前后快照去重，过滤“调用了方法但最终值没变”的情况。
- 事件仍然是只读的，不会改变游戏数值或存档。

计划新增：

```csharp
GameEvents.Subscribe<PlayerStateChangedEvent>(info =>
{
    // info.Previous 与 info.Current 都是不可变快照。
});
```

实现时使用 Postfix，并在 `GameplayReady` 建立主角快照基线。事件应在 Unity 主线程发布；
若某次调用发生在主角尚未建立之前，则忽略，不把初始化过程误报成玩家变化。

## 9. Hook 选择规则

1. 先用运行时 Interop 程序集确认全名和重载，再写 Harmony 特性。
2. 观察状态优先用 Postfix；只有需要阻止或改写原逻辑时才使用 Prefix。
3. 不要依赖 AssetRipper 生成方法体中的默认返回值。
4. 不 Hook 高频 `Update` 或属性 getter 作为第一选择。
5. 所有事件都做快照去重和异常隔离。
6. Hook 失败时记录目标签名，Loader 仍应允许其他 Mod 初始化。
7. 验证阶段只读日志和状态，不写入游戏存档。

## 10. 可复现验证流程

```powershell
# 1. 确认游戏进程已关闭
Get-Process | Where-Object { $_.Path -like '*MetalHeadGames*Back To The Dawn.exe' }

# 2. 检查类型和方法签名
& $dotnet $inspector $assembly ThingPackage CharacterAttribute CharacterManage

# 3. 构建并部署 Loader/Mod
.\scripts\Build-And-Deploy.ps1 -Configuration Release `
    -GameDirectory 'F:\SteamLibrary\steamapps\common\MetalHeadGames'

# 4. 启动到主菜单或主场景，读取日志
Get-Content 'F:\SteamLibrary\steamapps\common\MetalHeadGames\BepInEx\LogOutput.log' -Tail 200

# 5. 验证结束后关闭精确的游戏进程
$game = Get-Process | Where-Object {
    $_.Path -eq 'F:\SteamLibrary\steamapps\common\MetalHeadGames\Back To The Dawn.exe'
}
$game | Stop-Process -Force
```

预期日志至少包含 Loader 成功、Hook 安装成功、Mod registry ready，以及目标事件的有限数量输出。
如果只停留在主菜单，不应期待 `GameplayReady` 或玩家状态变化事件。

本轮 Release 验证结果：Loader 与 ExampleMod 正常初始化，日志包含
`Game lifecycle Harmony hooks installed.`，没有 Harmony 或 Mod 初始化异常。进入存档后使用止痛片，
实际观察到 `AddAttirbute` 和 `ChangeHealthy`，并发布了一次 `PlayerStateChangedEvent`：

```text
Player 25: health 32 -> 52
Source: AddAttirbute
```

这证明属性分发入口、主角过滤、快照去重和公共事件链路均已在当前游戏版本中工作。
物品使用则由独立的 `PlayerItemUsedEvent` 表达，即使物品没有造成可观察的属性差值也不会丢失使用行为。

## 11. 已知限制与版本更新检查清单

- AssetRipper 导出项目不是可直接重新构建的游戏源码。
- IL2CPP 互操作程序集会随游戏版本、BepInEx 版本和重新生成过程变化。
- `ThingPackage` 的方法是游戏内部高层入口，不保证未来版本仍保持同名。
- `ThingChangeReason` 属于游戏实现细节，不应直接成为稳定 Mod API 的程序集依赖。
- 游戏更新后必须重新检查 Unity 版本、Metadata 版本、Build ID、目标类型和重载签名。
- 运行时日志中的事件顺序比导出项目中的空方法体更可信。

更新后建议按以下顺序复核：

```text
文件版本/哈希
  → AssetRipper 导入日志
  → EditorBuildSettings 场景
  → AssemblyInspector 类型和签名
  → Loader Harmony 安装日志
  → 游戏内事件顺序
```
