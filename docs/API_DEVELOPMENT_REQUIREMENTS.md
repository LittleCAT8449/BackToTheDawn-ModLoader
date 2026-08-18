# Back To The Dawn Mod API 需求与开发计划

> 状态：Draft v0.1  
> 目标：先稳定公共 API，再逐步扩展物品行为、背包和游戏服务。  
> 原则：每完成一个阶段都必须编译、运行验证，并更新本文件的进度。

## 1. 文档范围

本文档描述第三方 Mod 使用的公共 API 需求，不描述具体 Harmony 实现细节。

范围包括：

- Mod 生命周期、版本和依赖
- 命名空间 ID 与物品注册
- 运行时物品注入和生命周期
- 物品资源、本地化和图标
- 物品使用行为与事件
- 背包查询、修改和变化事件
- 游戏状态、主线程和调度服务
- 配置、错误处理和兼容性

不允许公共 Mod 直接依赖 `ConfigData`、`ThingPackage`、`c_item`、IL2CPP 类型或
游戏内部数字 ID。Loader 内部可以使用这些类型，但必须转换成稳定的 ModAPI 对象。

## 2. 当前基线

当前已经存在并应继续保持兼容的 API：

- `IMod`、`ModContext`、`ModRegistry`
- `GameEvents.Subscribe<T>()` 和 `IDisposable` 订阅句柄
- `GameContext` 只读快照
- `ItemKey` 命名空间 ID，例如 `examplemod:debug_token`
- `Item`、`ItemRegistry`、`ItemRegistrationResult`
- `ItemCatalog` 只读物品目录
- `ItemIdResolver` 数字 ID 低级桥接
- `ModApi.Items`、`ModApi.Events`、`ModApi.Game` 统一门面（推荐入口）
- `ItemResources` 图标、名称和描述资源
- `ItemUseBeforeEvent`、`ItemUseAfterEvent` 和 `IItemBehavior`
- F8 开发控制台的 `item inject`、`item give` 命令

当前已验证：

- Mod 可以从 `src/main` 编译 C# 代码
- Mod 可以从 `src/resource` 读取文本资源
- 512×512 PNG 图标可以被运行时加载并显示在物品控件中
- 物品可以声明半格或整格占用
- 物品可以注入当前进程的 `c_item` 表

当前仍属于实验性实现或待补齐能力：

- 运行时数字 ID 从 `20000+` 动态分配，不能作为存档稳定标识
- 运行时绑定的物品不能在进程内完整移除，卸载会返回 `RuntimeBound`
- `ItemEffectDefinition` 只能读取游戏已有的效果
- 物品行为已经可以通过 `ItemBehaviorRegistry` 声明，但复杂目标/效果仍需继续扩展
- 还没有正式的背包查询、修改和精确剩余数量 API

## 3. 总体设计原则

### 3.1 稳定 ID 优先

所有公共读取、事件和存档相关数据必须使用 `ItemKey`。数字 ID 只能通过
`ItemIdResolver` 显式获取，并且必须标注为低级、短生命周期数据。

### 3.2 只读快照与命令分离

查询 API 返回不可变快照；修改游戏状态必须通过明确的 Service 或 Command API，
不能暴露可变的 IL2CPP 对象。

### 3.3 事件优先，底层 Hook 隔离

Mod 作者订阅稳定事件，不直接写 Harmony Hook。底层方法变化时，只修改 Loader 适配层。

### 3.4 生命周期明确

每个资源、订阅、任务和运行时对象都必须有创建、失效和释放语义。

### 3.5 主线程安全

所有会触碰 Unity 或游戏数据的 API 必须明确只能在游戏主线程调用，或由 Loader 自动调度。

## 4. 功能需求

### R1. Mod 身份、版本与依赖

需求：

- Mod manifest 必须声明 API 兼容版本。
- 依赖支持版本范围，而不只是裸 Mod ID。
- Loader 能报告缺失依赖、版本不匹配和循环依赖。
- Mod 可以查询当前 Loader/API 能力。
- 相同 Mod ID 只能加载一个实例。

建议 API 形态：

```csharp
public static class LoaderInfo
{
    public static Version ApiVersion { get; }
    public static bool HasCapability(string capability);
}

public sealed record ModDependency(string Id, string VersionRange);
```

验收标准：

- 版本不兼容的 Mod 在初始化前被拒绝，并产生结构化原因。
- Mod 可以在初始化阶段判断某项能力是否存在。
- 旧版 manifest 仍能按兼容规则加载。

### R2. 物品注册与命名空间所有权

需求：

- 物品命名空间默认绑定当前 Mod manifest 的 ID。
- 禁止 Mod 注册其他 Mod 的命名空间。
- `ItemKey` 必须拒绝空路径、重复分隔符和非法路径段。
- 注册失败必须返回可区分的状态，而不是只有 `false`。
- 保留 `ItemRegistry`，逐步将裸 `ItemCatalog.TryRegister` 标记为兼容 API。

建议 API 形态：

```csharp
public sealed class ModItemRegistry
{
    public ItemRegistrationResult Register(Item item);
    public bool Unregister(Item item);
}
```

验收标准：

- `context.Items.Register(item)` 自动使用当前 Mod 命名空间。
- 跨命名空间注册会被拒绝并返回明确错误。
- 同一 key、大小写变体和重复实例都有稳定结果。

### R3. 运行时物品生命周期

需求：

- 提供静态目录注册和运行时注入两个明确阶段。
- 提供运行时目录就绪事件。
- 支持在首次注入后注册新物品，或明确返回“需下一次注入”的状态。
- 记录 `ItemKey -> runtime ID` 的当前进程映射。
- 明确卸载策略：禁止卸载、延迟卸载或完整移除，不能静默失败。
- 不把动态数字 ID 写入 Mod 自己的持久化数据。

建议 API 形态：

```csharp
public static event Action<ItemRuntimeReadyEvent>? ItemRuntimeReady;

public sealed record ItemRuntimeReadyEvent(
    int Count,
    bool InjectionEnabled);

public sealed record ItemInjectionResult(
    Item Item,
    ItemInjectionStatus Status,
    int? RuntimeId,
    string Message);

public enum ItemUnregistrationStatus
{
    Unregistered,
    NotRegistered,
    RuntimeBound,
    CallbackFailed,
}

public sealed record ItemUnregistrationResult(
    Item Item,
    ItemUnregistrationStatus Status,
    string Message);
```

验收标准：

- Mod 能知道什么时候可以调用低级运行时 ID API。
- 运行时注入失败不会让整个 Mod 初始化失败。
- 迟注册物品有明确、可测试的结果。
- 游戏重启后 Mod 仍使用命名空间 ID 工作。

### R4. 物品定义与类型安全元数据

需求：

- `MaxStack`、`MaxUse` 等数值在注册阶段验证范围。
- `IsEquipment`、`IsWeapon` 等字段必须真正映射到运行时定义。
- 逐步减少 `ItemType`、`ParameterA/B` 等裸字符串的直接使用。
- 提供可选的 Builder 或 typed options，保留旧构造函数兼容。
- 明确半格/整格占用、可赠送、可摧毁、可堆叠等能力。

验收标准：

- 非法数值在注册时返回错误，而不是注入时静默修正。
- 物品定义的装备/武器属性与游戏 UI 行为一致。
- 旧 Mod 仍能使用原有 `Item` 构造方式。

### R5. 物品资源与本地化

需求：

- 保持 `ItemResources` 的安全相对路径规则。
- 支持 PNG/JPG 图标，并在缺失时安全回退模板图标。
- 支持按语言提供名称、描述和背景文本。
- 资源加载失败必须记录 Mod、key、相对路径和错误原因。
- Loader 负责 Unity Sprite 生命周期，Mod 不需要管理 IL2CPP 对象。
- 为资源增加缓存和释放策略，避免每次刷新背包重复解码。

建议资源布局：

```text
resource/
  items/debug_token/icon.png
  lang/zh-cn/items.json
  lang/en-us/items.json
```

验收标准：

- 中文和英文切换后物品文本正确回退。
- 图标只加载一次，物品控件刷新后仍使用同一 Sprite。
- Mod 卸载或 Loader 重载时不会留下可见的 Unity 资源对象。

### R6. 物品使用行为与可取消事件

需求：

- Mod 可以声明物品使用行为，而不需要直接 Hook `CharacterAttribute.UseItem`。
- 使用前事件可以取消操作、修改目标或调整消耗数量。
- 使用后事件必须包含成功状态、实际消耗和剩余数量。
- 失败、取消、目标无效和效果执行异常必须区分。
- NPC 使用和玩家使用必须明确区分。

建议 API 形态：

```csharp
public interface IItemBehavior
{
    ItemUseResult Use(ItemUseContext context);
}

public sealed class ItemUseContext
{
    public ItemKey ItemKey { get; }
    public int CharacterId { get; }
    public int RequestedCount { get; set; }
    public ItemUseSource Source { get; }
    public ItemTarget? Target { get; set; }
}

public sealed record ItemUseResult(
    bool Succeeded,
    bool Cancelled,
    int ConsumedCount,
    string? Message,
    ItemUseFailureReason FailureReason = ItemUseFailureReason.None,
    int? RemainingCount = null);
```

验收标准：

- 一个测试 Mod 可以实现“使用后恢复生命”而不引用游戏程序集。
- 取消使用不会减少背包数量。
- 行为异常会被隔离并记录，不会中断其他事件订阅者。
- 现有 `PlayerItemUsedEvent` 和 `PlayerItemActionEvent` 保持兼容。

### R7. 背包与物品数量服务

需求：

- 提供当前玩家背包的只读快照。
- 提供按 `ItemKey` 查询数量、位置和占格信息的 API。
- 提供受控的添加、移除、移动和整理命令。
- 所有修改命令返回成功、失败原因和实际变化量。
- 提供统一的 `InventoryChangedEvent`。
- 处理背包满、数量不足、物品不存在和非法位置。

建议 API 形态：

```csharp
public interface IInventoryService
{
    InventorySnapshot GetPlayerInventory();
    InventoryOperationResult TryAdd(ItemKey key, int count);
    InventoryOperationResult TryRemove(ItemKey key, int count);
}
```

验收标准：

- Mod 不需要调用 `ThingPackage.AddItem`。
- 查询结果只包含命名空间 ID，不暴露 `Thing`。
- 失败操作不产生部分修改，或明确报告部分成功数量。
- 整理、移动、摧毁和装备行为都能产生统一事件。

### R8. 事件系统

需求：

- 保持强类型事件和 `IDisposable` 订阅句柄。
- 事件参数必须是不可变快照。
- 需要优先级时提供明确排序规则。
- 需要取消时使用专门的可取消事件，不修改普通观察事件语义。
- 所有事件必须说明触发线程、触发时机和异常处理策略。
- 事件订阅者异常必须相互隔离，并进入结构化日志。

当前优先补充的事件：

- `ItemRuntimeReadyEvent`
- `BeforeItemUseEvent`
- `AfterItemUseEvent`
- `InventoryChangedEvent`
- `InventoryMovedEvent`
- `PlayerArchiveChangedEvent`
- `LanguageChangedEvent`

验收标准：

- 每个事件都有公开 record、订阅示例和生命周期说明。
- 事件顺序在同一游戏版本内稳定。
- 订阅句柄重复 Dispose 安全。

### R9. GameContext、主线程与调度

需求：

- 保留只读 `GameContext` 快照。
- 增加主线程执行入口，避免 Mod 直接从后台线程修改 Unity 对象。
- 提供 Mod 级定时任务或帧更新能力，但必须可取消、可释放。
- 明确游戏未进入 `GameplayReady` 时哪些服务不可用。

建议 API 形态：

```csharp
public interface IGameScheduler
{
    void Post(Action action);
    IDisposable Schedule(TimeSpan delay, Action action);
}
```

验收标准：

- 后台线程调用查询不会造成 Unity 崩溃。
- 未准备好时的服务调用返回明确状态。
- Mod Shutdown 后所有任务都停止执行。

### R10. 配置、诊断与兼容性

需求：

- 配置支持 schema 版本和迁移。
- 保存尽量采用临时文件替换，避免进程中断造成半文件。
- API 错误包含 Mod ID、key、操作和底层原因。
- 提供 API 版本、Loader 版本和游戏版本诊断信息。
- 为公共 API 增加不依赖游戏进程的契约测试。

验收标准：

- 配置升级可以从旧版本自动迁移。
- 关键失败可通过日志定位到具体 Mod 和具体 key。
- `dotnet build`、API 契约测试和游戏冒烟测试都有记录。

## 5. 分阶段开发顺序

### Phase 0：契约冻结与测试基线

- [ ] 建立 API 契约测试项目。
- [ ] 为 `ItemKey`、注册结果、资源路径和事件订阅补充测试。
- [ ] 标记兼容 API 与实验性 API。
- [ ] 更新 README 和技术文档中的状态说明。

### Phase 1：运行时物品生命周期

- [x] 增加 `ItemRuntimeReadyEvent`。
- [x] 增加结构化 `ItemInjectionResult`。
- [x] 处理迟注册物品（启用运行时注入时自动尝试）。
- [x] 明确注入后的卸载策略：运行时绑定的物品延迟到进程退出，并返回结构化结果。
- [ ] 补齐装备/武器等定义字段的运行时映射。

### Phase 2：物品行为与使用事件

- [x] 定义 `ItemUseContext`、`ItemUseResult`。
- [x] 增加使用前/使用后事件。
- [x] 建立行为注册表和异常隔离。
- [ ] 用一个测试 Mod 实现恢复生命的自定义物品。

### Phase 3：背包服务

- [x] 定义库存快照和只读查询结果。
- [x] 实现查询数量、位置和占格。
- [x] 实现添加、移除和移动适配；整理继续使用游戏原生操作事件。
- [x] 增加 `InventoryChangedEvent` 和 `InventoryMovedEvent`。
- [x] Hook `ThingPackage.UseThing`、`UseBatchThing`、`AddItem`、`ReduceItem`、
  `ReduceThingCount`、`RemoveThing`，统一发布库存变化。
- [x] Hook `ThingPackage.MoveThingPlace`，发布容器移动事件。

### Phase 4：资源与本地化完善

- [ ] 支持多语言资源文件和回退顺序。
- [ ] 统一图标、文本和资源缓存生命周期。
- [ ] 增加资源缺失、格式错误和重复加载测试。

### Phase 5：Mod 基础设施

- [ ] API/Loader 版本和能力查询。
- [ ] 依赖版本范围。
- [ ] 主线程调度和可取消任务。
- [ ] 配置 schema、迁移和原子保存。

## 6. 每阶段通用验收流程

1. 更新本文件对应的任务勾选状态。
2. 执行：

   ```powershell
   .\.tools\dotnet\dotnet.exe build BackToTheDawnModLoader.sln --configuration Release
   ```

3. 运行不依赖游戏的 API 契约测试。
4. 通过 Steam 启动游戏，不直接启动游戏 exe。
5. 进入主场景，查看 `BepInEx/LogOutput.log`。
6. 使用 ExampleMod 或专用测试 Mod 做最小场景验证。
7. 记录成功日志、失败日志和已知限制。

## 7. 非目标

以下内容暂不纳入近期公共 API：

- 修改或替换 `GameAssembly.dll`
- 直接编辑原始资源包
- 让 Mod 任意执行未隔离的底层 Harmony Patch
- 在线 Mod 下载、自动更新和远程代码执行
- 在没有版本适配层的情况下承诺跨游戏版本稳定
- 将游戏动态数字 ID 作为公开存档格式

## 8. 开发决策规则

- 新功能先进入本文件，再进入公共 API。
- 公共 API 一旦发布，优先增加新类型和新重载，不直接改变已有语义。
- 实验性 API 必须使用清晰命名或配置开关，并写明可能影响存档。
- 每个新事件都必须有触发时机、线程、异常和取消语义说明。
- 每个需要数字 ID 的入口都必须经过 `ItemIdResolver` 或同等低级桥接，并在文档中标明。
