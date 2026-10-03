# 自定义房间与 AssetBundle 开发计划

## 1. 文档目标

本文档规划从当前运行时房间克隆原型，逐步发展为支持模组作者制作、加载和使用自定义房间的完整系统。

目标是让模组作者能够在 Unity 中制作房间，将房间构建为 AssetBundle，然后通过 Mod API 注册到游戏中。

```csharp
var result = ModApi.Rooms.RegisterAssetRoom(
    "examplemod:warehouse",
    "rooms/warehouse.bundle",
    "Warehouse");

if (result.Succeeded)
{
    ModApi.Rooms.GoTo("examplemod:warehouse");
}
```

## 2. 当前基础

当前已经完成：

- `RoomApi`
- `RoomChangedEvent`
- 房间切换 Hook
- 运行时房间克隆
- 教堂副本测试
- 虚拟房间 ID 管理
- 存档重载时清理运行时克隆
- 独立的 `RoomMonitor` 测试模组

当前克隆流程如下：

```text
原版房间 GameObject
        ↓
运行时 Instantiate
        ↓
修改 Map ID
        ↓
注册到 MapManage.mapDict
```

这个方案适合验证房间注册、查询和清理机制，但不能作为可切换的原生地图，也不能直接加载模组作者制作的房间资源。当前 `GoTo` 对这类纯运行时克隆 fail closed；只有后续接入资源支持的地图加载和初始化链后才能开放切换。

## 3. 目标目录结构

```text
MyMod/
├─ mod.json
├─ MyMod.dll
└─ resource/
   └─ rooms/
      └─ warehouse.bundle
```

Unity 工程建议使用独立的房间 SDK：

```text
BackToTheDawnRoomSDK/
├─ Assets/
│  ├─ Scripts/
│  ├─ Prefabs/
│  ├─ Materials/
│  └─ Editor/
├─ ProjectSettings/
└─ BuildRoomBundle.cs
```

## 4. 核心设计原则

### 4.1 不直接加载 `.prefab`

Unity 的 `.prefab` 源文件不能直接被游戏运行时加载。模组需要将 Prefab、材质、贴图和其他依赖构建为 AssetBundle。

### 4.2 第一版采用逻辑模板模式

第一版不从零构造游戏内部的 `Map`，而是使用原版房间作为逻辑模板，再把自定义 AssetBundle Prefab 挂载到模板下。

```text
原版 Map GameObject
├─ Map 组件
├─ 房间初始化逻辑
├─ 摄像机边界
├─ 出入口逻辑
└─ 自定义 AssetBundle 视觉和碰撞对象
```

这种方式可以复用游戏已有的摄像机、地图初始化和交互逻辑，降低崩溃风险。

### 4.3 完全独立房间作为高级功能

完整自定义 `Map` 需要适配游戏内部的地图初始化、摄像机、出口、交互、NPC、存档等逻辑，不能只依靠 AssetBundle 保存 IL2CPP 组件。

后续应使用 AssetBundle 保存视觉节点和标记节点，再由加载器创建游戏原生组件。

## 5. 开发阶段

## 阶段 0：完善现有 Room API

目标：稳定当前运行时房间注册、查询和清理 API；对缺少原生资源/初始化记录的克隆房间明确、安全地拒绝切换。

### 阶段 0 当前进度

状态：已完成（现有 Room API 的注册、查询、清理已验证；纯运行时克隆的 `GoTo` 已在部署版本中 fail closed，且原版存档和受控拒绝测试均通过）。这不代表当前支持进入克隆房间；资源支持的原生过场转入阶段 2。

- [x] `Current` 当前房间查询
- [x] `Subscribe` 房间切换事件订阅
- [x] `RegisterClone` 运行时房间注册
- [x] `GoTo` 对不受支持的纯运行时克隆安全拒绝：不激活克隆、不调用原生地图过场，并记录目标 Map ID、资源路径和拒绝原因
- [x] `IsRegistered` 房间注册状态查询
- [x] `TryGet` 房间信息查询
- [x] `GetRegisteredRooms` 已注册房间列表查询
- [x] `Unregister` 单个运行时房间卸载
- [x] `UnregisterAll` 按模组命名空间批量卸载
- [x] 存档加载前清理运行时房间
- [x] 在 RoomMonitor 未启用时验证原版存档可进入（ArchiveId=10，MapId=9，A103囚室，触发 `GameplayReady`）
- [x] RoomMonitor 受控验证：克隆 `GoTo` 返回 `false`，记录虚拟 Map ID，且不进入 `MapManage.GoToMap` / `LoadMap` / `LoadOneMap`
- [x] 游戏内验证重复注册、房间查询、批量卸载和当前房间注销
- [x] 验证 RoomMonitor 启用时原版存档仍可进入，拒绝切换后没有 Unity/Harmony/NavMesh 异常

此前的集成测试记录：

- 重复注册返回 `AlreadyRegistered`
- `IsRegistered`、`TryGet` 和列表查询返回正确的 `9000` 房间
- 批量注销成功移除临时房间
- 注销当前房间成功，并可重新注册为 `9002`
- 注销和重新注册后的 `RoomChangedEvent.Previous.Name` 仍保持为“教堂副本”
- 存档加载前清理逻辑此前通过了重载测试

### 已诊断限制与后续阶段

后续 Unity `Player-prev.log` 显示克隆房间切换期间仍有异常：

- `UI_Transition.Update -> MapManage.LoadMap -> MapManage.GetMapFromDictAutoCreate -> MapManage.LoadOneMap` 尝试实例化空对象。虚拟 Map ID 没有完整的游戏资源/地图加载记录，无法走原生过场加载流程。
- `ChurchEvent.FocusMapEvent` 出现空引用。直接复制教堂 GameObject 没有建立事件组件依赖的所有运行时引用。
- 克隆对象被放置在原场景约 10000 个 Unity 单位以外，角色 NavMeshAgent 无法找到 NavMesh。

历史测试的异常链是 `UI_Transition.Update -> MapManage.LoadMap -> GetMapFromDictAutoCreate -> LoadOneMap`（资源对象为空），以及 `ChurchEvent.FocusMapEvent` 空引用和 NavMeshAgent 找不到 NavMesh。受控测试确认 Room ID `9000` 在 `mapDict` 中，但 `GetMapPathGOById(9000)` 返回空资源路径；`RoomApi.GoTo` 记录该诊断后返回 `false`，并且原生 `GoToMap` / `LoadMap` / `LoadOneMap` 对 ID `9000` 均未被调用，因此这条异常链已被 fail-closed 边界阻断。

RoomMonitor 测试模组已临时启用并验证拒绝行为；只会注册一个非激活克隆并记录 `GoTo` 的 `false` 结果，不会让角色进入克隆或触发 NavMesh/ChurchEvent 逻辑。其源文件仍保存在 `examples/BackToTheDawn.RoomMonitor`，原先的停用副本仍保存在 `disabled-mods/BackToTheDawn.RoomMonitor`。如果要继续使用该测试模组，可保持当前行为；普通运行也可在关闭游戏后移除活动目录。

部署与运行时回归（2026-10-02）：解决方案 Debug 构建成功（0 警告、0 错误）；Loader 和 ModAPI 已部署到 Steam 安装目录，部署 DLL 与构建产物 SHA-256 一致。启用 RoomMonitor 的运行扫描为 `1 valid / 0 rejected`。原版存档 ArchiveId=10 成功触发 `ArchiveLoadStarted`、`ArchiveLoadInvocationReturned`、`MapChanged`（0 -> 9，A103囚室）和 `GameplayReady`；`Player.log` 未发现 Error、Exception 或 NavMesh 错误。原版 `LoadOneMap` 跟踪记录：0=`Prefab/Scenes/zero`、19=`Prefab/Scenes/laundryRoom`、11=`Prefab/Scenes/fighting`、2=`Prefab/Scenes/hall1`、3=`Prefab/Scenes/hall2`、4=`Prefab/Scenes/corridor1+2_Vlight`、6=`Prefab/Scenes/corridor3+4_Vlight`、10=`Prefab/Scenes/bathroom`、9=`Prefab/Scenes/cell2_3`。受控克隆测试中，`RoomApi.GoTo` 目标 ID `9000`、`mapDictContains=True`、资源路径为空，API 返回 `false`；日志未出现对 ID `9000` 的原生 `GoToMap`、`LoadMap` 或 `LoadOneMap` 调用，也没有 Unity/Harmony/NavMesh 异常。启动期间另有 ModCanvasRenderer 的 3 条 IL2CPP 不支持签名警告，与本阶段 Room API 无关，renderer 类型仍已注册。

### 阶段 0 回归测试命令

进入存档后按 `F8` 打开加载器控制台：

```text
rooms
rooms get dev.backtothedawn.roommonitor:church_clone
rooms goto dev.backtothedawn.roommonitor:church_clone
```

用于验证注销行为：

```text
rooms unregister dev.backtothedawn.roommonitor:church_clone
rooms unregister-all dev.backtothedawn.roommonitor
```

预期结果：

- `rooms` 能列出已注册房间及其命名空间 ID
- `rooms get` 能返回房间信息
- 重复注册返回 `AlreadyRegistered`
- 当前纯运行时克隆的 `rooms goto` 预期返回拒绝；只有阶段 2 完成资源支持的原生地图加载后才支持进入
- 注销当前房间时先回到基础房间，再移除虚拟房间
- 批量注销返回实际移除数量

### API

```csharp
ModApi.Rooms.Current
ModApi.Rooms.Subscribe(...)
ModApi.Rooms.RegisterClone(...)
ModApi.Rooms.GoTo(...)
```

### 计划补充的 API

```csharp
ModApi.Rooms.IsRegistered(string key)
ModApi.Rooms.TryGet(string key, out RoomInfo room)
ModApi.Rooms.Unregister(string key)
ModApi.Rooms.UnregisterAll(string modId)
```

### 房间信息

```csharp
public sealed record RoomInfo(
    string Key,
    int NativeId,
    string Name,
    RoomSource Source,
    bool IsLoaded);
```

```csharp
public enum RoomSource
{
    Native,
    Clone,
    AssetBundle
}
```

### 完成标准

- 重复注册不会崩溃
- 模组卸载时房间能够清理
- 重新加载存档不会残留虚拟地图
- 房间事件顺序稳定

## 阶段 1：AssetBundle 资源加载器

目标：允许模组携带 Unity 资源，并在运行时安全加载。

### 阶段 1 当前进度

状态：进行中（资源 API、路径边界、缓存/卸载逻辑、Prefab 依赖诊断和边界测试已实现；尚未用游戏 Unity 版本构建的真实 AssetBundle/Prefab 做正向运行时验证，因此阶段 1 暂不标记完成）。

- [x] 通过每个模组自己的 `ModContext.Resources` 提供资源操作，避免不同模组共享资源目录或 Bundle 缓存
- [x] 相对路径限制在模组 `resource` 目录内，拒绝绝对路径、路径穿越和现存 symbolic link/reparse point
- [x] `LoadBundle` 检查文件存在、加载失败返回明确状态，并按模组缓存重复加载的 Bundle
- [x] `LoadAsset<T>` 要求先加载 Bundle，桥接 `System.Type` 到 IL2CPP 类型，并报告缺失资源/类型不匹配
- [x] 对加载出的 `GameObject` Prefab 做只读诊断，报告空材质槽、缺失 Shader 和未赋值纹理属性；诊断异常不会让成功加载变成失败
- [x] `UnloadBundle` 支持 `unloadAllObjects` 选项；Mod 关闭时自动以 `false` 卸载缓存，保留已实例化对象
- [x] API 文件路径、错误状态、缓存/卸载合同与参数转发测试通过（17 项断言；Bundle provider 采用 fake，不代替 Unity 实测）
- [x] Steam 启动集成探针通过：模组加载成功，路径穿越返回 `InvalidPath`，缺失 Bundle 返回 `NotFound`，未加载 Bundle 时读取 Prefab 返回 `BundleNotLoaded`
- [x] Windows Junction/reparse-point 路径逃逸测试通过
- [ ] 使用与游戏相同 Unity 版本构建的 Bundle，在游戏内验证真实 Bundle 加载、Prefab 读取和显式卸载

### 计划提供的 API

```csharp
var loaded = context.Resources.LoadBundle("rooms/warehouse.bundle");
if (!loaded.Succeeded)
{
    context.Logger.Error($"Bundle load failed: {loaded.Status} - {loaded.Message}");
}
```

```csharp
var prefab = context.Resources.LoadAsset<GameObject>(
    "rooms/warehouse.bundle",
    "Warehouse");
if (!prefab.Succeeded)
{
    context.Logger.Error($"Prefab load failed: {prefab.Status} - {prefab.Message}");
}
```

```csharp
context.Resources.UnloadBundle(
    "rooms/warehouse.bundle",
    unloadAllObjects: true);
```

### 加载器职责

- 限制资源路径在当前模组目录内
- 防止路径穿越及经现存符号链接/重解析点逃出模组资源目录
- 防止重复加载相同 Bundle
- 缓存 AssetBundle
- 检查 Prefab 是否存在
- 处理 Bundle 卸载
- 记录资源加载日志
- 报告材质、贴图和 Shader 缺失
- Unity/AssetBundle 操作只允许在 Unity 主线程执行

当前实现的 `ResourceStatus` 包含 `InvalidPath`、`NotFound`、`LoaderUnavailable`、`BundleLoadFailed`、`BundleNotLoaded`、`WrongThread`、`InvalidAssetName`、`InvalidAssetType`、`AssetNotFound`、`AssetLoadFailed`、`AssetTypeMismatch` 和 `UnloadFailed`。重复加载已缓存 Bundle 返回成功状态 `AlreadyLoaded`。

AssetBundle 必须使用与目标游戏兼容的 Unity 版本构建。当前目标游戏为 Unity 2020.3.2f1c1；本机只有 Unity 6 编辑器，没有可直接使用的 Unity 2020 测试 Bundle，因此真实 Prefab 加载及材质/贴图/Shader 诊断验证留待提供匹配 Bundle 后完成。

环境复核（2026-10-02）：当前 Unity Hub 仅安装 `6000.0.27f1`。测试构建脚本明确拒绝非 Unity 2020.3 编辑器，所以真实 AssetBundle 正向验证仍是阶段 1 唯一未完成项；待安装/提供 Unity 2020.3（优先 `2020.3.2f1c1`）环境后，再构建并运行探针，不使用 Unity 6 产物冒险验证。

为便于完成剩余正向验证，仓库增加 Unity Editor 辅助脚本 `tests/BackToTheDawn.AssetBundleProbe/Editor/BuildProbeBundle.cs`：可生成测试 Cube Prefab、构建 Windows x64 Bundle，并复制到已部署测试 Mod 的资源目录；脚本只允许 Unity 2020.3，并会在覆盖既有 Bundle 前确认。该辅助脚本尚未在 Unity Editor 内执行。

Steam 集成探针记录（2026-10-02）：通过 `D:\Steam\steam.exe -applaunch 1735700` 启动正式 Steam 游戏，Loader 启动完成，`dev.backtothedawn.assetbundleprobe` 被发现并初始化；路径穿越、缺失 Bundle 和未加载 Bundle 的资源请求均返回预期结果。更新 Prefab 诊断代码后再次构建并部署 Loader，Steam 重启成功；`BepInEx/LogOutput.log` 和 Unity `Player.log` 未发现新的 Loader、Harmony 或 Unity 异常。由于测试 Bundle 仍不存在，本次启动没有调用 Unity 原生成功加载路径。探针 Mod 位于 `BepInEx/mods/BackToTheDawn.AssetBundleProbe`；原有 `BackToTheDawn.RoomMonitor` 未改动。

### 完成标准

- 能从模组目录加载 AssetBundle
- 能加载其中的 Prefab
- 资源不存在时返回明确错误
- 资源卸载后不会留下不可控对象

## 阶段 2：视觉房间模式

目标：使用原版房间作为逻辑模板，AssetBundle 负责自定义外观和碰撞。

### 注册 API

```csharp
var result = ModApi.Rooms.RegisterAssetRoom(
    key: "examplemod:warehouse",
    bundlePath: "rooms/warehouse.bundle",
    prefabName: "Warehouse",
    options: new RoomOptions
    {
        BaseRoom = "backtothedawn:church",
        DisplayName = "仓库",
        Position = new Vector3(1000, 0, 0)
    });
```

### 加载过程

```text
读取 AssetBundle
    ↓
加载自定义 Prefab
    ↓
复制原版 Map 模板
    ↓
实例化自定义 Prefab
    ↓
挂载到 Map 对象下
    ↓
注册虚拟 Room ID
    ↓
触发房间初始化
```

### 适用内容

- 新地图外观
- 自定义碰撞区域
- 装饰物
- 简单 NPC 和物品摆放
- 自定义房间标记

### 完成标准

- 能进入自定义房间
- 摄像机正常
- 玩家不会掉出地图
- 碰撞正常
- 返回原房间正常
- 重新加载存档不崩溃

## 阶段 3：房间入口和出口

目标：让自定义房间具备正式的入口和出口。

### 数据结构

```csharp
public sealed record RoomExitDefinition(
    string Id,
    Vector3 Position,
    string TargetRoom,
    string TargetExit);
```

### API

```csharp
ModApi.Rooms.AddExit(roomKey, exit);
ModApi.Rooms.RemoveExit(roomKey, exitId);
ModApi.Rooms.GetExits(roomKey);
```

### 事件

```csharp
RoomEnterEvent
RoomLeaveEvent
RoomExitUsedEvent
```

```csharp
public sealed record RoomExitUsedEvent(
    string Room,
    string Exit,
    string TargetRoom);
```

### 完成标准

- 可以从入口进入自定义房间
- 可以通过出口离开
- 事件包含来源房间和目标房间
- 目标房间不存在时不会卡死

## 阶段 4：交互点和内容生成

目标：支持房间中的物品、容器、NPC 和交互内容。

### 示例

```csharp
ModApi.Rooms.AddInteraction(
    "examplemod:warehouse",
    new InteractionDefinition
    {
        Id = "storage_box",
        Position = new Vector3(2, 0, 4),
        Prompt = "打开箱子",
        OnInteract = context =>
        {
            context.Player.AddItem("examplemod:tool", 1);
        }
    });
```

### 计划支持

- 物品拾取
- 容器
- 门
- 床
- NPC
- 商店
- 触发器
- 自定义对话
- 任务触发点

### 事件

```csharp
RoomInteractionEvent
RoomObjectSpawnedEvent
RoomObjectDestroyedEvent
```

## 阶段 5：完整自定义房间模式

目标：允许房间不依赖原版 Map 模板。

### 需要适配的系统

- 自定义 `Map` 数据
- 摄像机边界
- 初始化流程
- 玩家出生点
- 房间区域
- 出入口
- 场景对象注册
- NPC 和物品生成
- 存档状态

### 推荐资源结构

```text
CustomRoomRoot
├─ Visual
├─ Collision
├─ SpawnPoints
├─ Exits
├─ Interactions
└─ ModRoomMetadata
```

AssetBundle 保存这些节点，加载器负责将标记转换成游戏内部对象。

## 阶段 6：Unity 房间 SDK

目标：降低模组作者制作房间的门槛。

### SDK 功能

- 房间工程模板
- 自定义房间根节点
- 出口标记组件
- 出生点标记组件
- 交互点标记组件
- AssetBundle 一键构建
- 构建前自动检查

### 自动检查内容

- 是否存在 `CustomRoomRoot`
- 是否存在出生点
- 是否存在至少一个出口
- 是否有缺失材质
- 是否有非法 Shader
- 是否使用不兼容组件
- 是否有过大的贴图
- 是否有重复资源名称

## 阶段 7：房间状态和持久化

当前运行时克隆只存在于当前进程，不写入存档。

后续只保存房间状态，不把 Unity 对象直接写入存档。

```json
{
  "roomKey": "examplemod:warehouse",
  "version": 1,
  "state": {
    "openedChest": true,
    "switchEnabled": false
  }
}
```

### API

```csharp
ModApi.Rooms.GetState<T>(roomKey);
ModApi.Rooms.SetState(roomKey, state);
```

## 6. 最终 API 草案

```csharp
public static class RoomApi
{
    RoomSnapshot? Current { get; }

    IDisposable Subscribe(Action<RoomChangedEvent> handler);

    RoomRegistrationResult RegisterClone(
        string key,
        string baseRoom,
        string? displayName = null);

    RoomRegistrationResult RegisterAssetRoom(
        string key,
        string bundlePath,
        string prefabName,
        RoomOptions? options = null);

    bool GoTo(string key);
    bool Unregister(string key);
    bool TryGet(string key, out RoomInfo room);

    IReadOnlyList<RoomInfo> GetRegisteredRooms();
    IReadOnlyList<RoomExitDefinition> GetExits(string key);

    void AddExit(string key, RoomExitDefinition exit);
    void RemoveExit(string key, string exitId);
}
```

## 7. 资源 API 草案

```csharp
// `context` 是 IMod.Initialize(ModContext context) 收到的当前模组上下文。
public sealed class ModResources
{
    AssetBundleResult LoadBundle(string relativePath);

    AssetLoadResult<T> LoadAsset<T>(string bundlePath, string assetName);

    ResourceUnloadResult UnloadBundle(
        string relativePath,
        bool unloadAllObjects = false);
}
```

## 8. 事件系统规划

```text
RoomRegisteredEvent
RoomUnregisteredEvent
RoomChangedEvent
RoomEnterEvent
RoomLeaveEvent
RoomExitUsedEvent
RoomInteractionEvent
RoomLoadFailedEvent
```

```csharp
public sealed record RoomLoadFailedEvent(
    string RoomKey,
    string Reason,
    Exception? Exception);
```

## 9. 里程碑

### M1：运行时 Room API

- 克隆
- 注册
- 切换
- 卸载
- 房间事件

### M2：资源加载

- AssetBundle
- Prefab
- 贴图和材质
- 错误处理

### M3：自定义视觉房间

- 基于教堂模板
- 自定义地图模型
- 自定义碰撞
- 自定义摄像机范围

### M4：房间交互

- 出入口
- 交互点
- 物品
- NPC

### M5：持久化

- 房间状态
- 版本迁移
- 存档兼容

### M6：Unity SDK

- 工程模板
- 一键打包
- 自动校验
- 示例房间

## 10. 推荐实施顺序

1. 完善现有 `RoomApi`
2. 增加 AssetBundle 资源加载器
3. 实现“原版 Map + 自定义 Prefab”模式
4. 添加房间入口和出口
5. 添加交互点
6. 添加房间状态保存
7. 制作 Unity 房间 SDK
8. 最后实现完全独立的原生房间

不建议一开始就实现完全独立的 `Map`，因为它同时涉及地图初始化、摄像机、碰撞、出口、交互和存档等多个系统。

## 11. 下一步任务

阶段 1 的下一步是把 `tests/BackToTheDawn.AssetBundleProbe/Editor/BuildProbeBundle.cs` 放进 Unity 2020.3 项目的 `Assets/Editor` 并运行菜单构建，生成一个含 `ResourceProbePrefab` 的测试 Bundle。该 Bundle 会复制到 `BepInEx/mods/BackToTheDawn.AssetBundleProbe/resource/probe/test.bundle`；随后通过 Steam 重启游戏。测试 Mod 会验证 `LoadBundle`、`LoadAsset<GameObject>`、缓存重复加载、Prefab 依赖诊断和卸载。验证闭环通过后再把阶段 1 标记完成，并开始阶段 2 的最小 `RegisterAssetRoom`：

```text
复制教堂 Map
    ↓
加载一个 AssetBundle Prefab
    ↓
把 Prefab 挂载到房间下
    ↓
注册新的 Room ID
    ↓
进入并显示自定义内容
```

完成这个闭环后，再继续增加出口、交互点和完整房间逻辑。
