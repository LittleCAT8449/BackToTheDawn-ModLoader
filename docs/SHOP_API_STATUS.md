# 商店 API 拆分与完成度

> 更新时间：2026-10-03
> 范围：商店身份、商品目录、价格货币、交易生命周期、延迟领取和失败信号。  
> 说明：本文按当前源码和已验证日志判断；“已接入”不等于所有字段都已经在每个商店运行验证。

## 1. 文档证据与系统边界

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

## 2. 当前公开 API

### 2.1 商店身份

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
[商店注册 API](SHOP_API.md)。

### 2.2 商品目录

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

### 2.3 交易事件

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

## 3. 商店身份目录

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

## 4. 按商店拆分的交易完成度

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

## 5. 货币封装完成度

### 已公开的货币类型

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

### 当前推断规则

当 `c_shop` 的原始价格类型存在时，优先使用原始值；字段为空时使用 `ShopKey` 的保守默认值：

| ShopKey | 默认货币 |
|---|---|
| 普通商店、自动售货机、食堂、电视、彩票 | `Money` |
| `maggie_shop` | `Relationship` |
| `bigfoot_shop` / `fang_shop` / `blackclaw_shop` | 不推断，以实际 `TradeCompleted` 为准 |
| `roof_benefit` / `excess_benefit` | `Discipline` |

最终交易货币仍以 `TradeCompletedEvent.Transaction` 的实际变化为准；屋顶交易的表现分和金钱分别使用 `DisciplineDelta` 与 `CurrencyDelta` 表达。

## 6. 尚未完成的商店 API

### 6.1 商品目录

- [x] 全量读取 `c_shop`，并保留 UI/购买时的增量观察（已接入，待运行日志验证）。
- [ ] 午餐、电视和披萨入口补充 `ShopGoodsObservedEvent`。
- [ ] 明确价格类型的原始枚举，而不是只保留文本。
- [ ] 商品刷新、售罄和库存变化事件。

### 6.2 交易详情

- [ ] `UnitPrice` 与 `TotalPrice` 独立字段。
- [ ] 折扣、讨价还价和技能影响后的实际价格。
- [ ] 余额不足、库存不足、权限限制和售罄的失败原因。
- [ ] 交易历史查询 API。

### 6.3 特殊商店

- [ ] 电视购物订单号、期号和历史记录。
- [ ] 帮派订单的权限、贡献扣除和商品目录货币字段仍需运行验证。
- [x] 玛姬商品目录和关系值价格读取入口已接入并完成购买验证。
- [ ] 理发店出售物品的专用交易 Hook。

## 7. 结论

当前 API 已经完成商店系统的“身份层”和“主要交易事件层”。玛姬、帮派、屋顶、彩票和下注的货币/阶段已经有公开事件；普通商店和自动售货机还额外具备商品价格、库存观测。

当前不能称为“完整商店 API”的原因是：商品目录仍是增量的，特殊商店的商品元数据没有全部接入，价格/库存刷新和交易历史也还没有公开。

推荐后续顺序：

1. [ ] 补玛姬、帮派、电视和午餐的商品目录观测。
2. [ ] 增加 `UnitPrice`、`TotalPrice` 和失败原因。
3. [ ] 增加库存刷新/售罄事件。
4. [ ] 最后实现理发店出售和交易历史查询。
