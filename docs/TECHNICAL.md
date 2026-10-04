# Back To The Dawn Mod Loader 技术文档

本文记录加载器内部 Hook、配置、兼容性和仍在演进中的实现细节。面向模组作者的完整公开接口见 [API 参考](API_REFERENCE.md)。

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
