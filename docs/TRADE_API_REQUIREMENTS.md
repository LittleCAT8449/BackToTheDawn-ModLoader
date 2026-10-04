# Back To The Dawn 交易与经济 API 需求清单

商店专项拆分和当前完成度见 [商店 API 状态与目录](API_REFERENCE.md#shop-api-status)。

> 状态：Draft v0.1  
> 目标：把游戏中不同的买卖、交换、赠送、生产和金融行为统一成稳定的 Mod API。  
> 原则：先观察并确认真实结算，再公开事件；不让 Mod 直接依赖 UI、`ThingPackage`、`c_shop` 或数字 ID。

## 1. 范围

本清单中的“交易”包括以下几种状态变化：

- 物品与金钱交换；
- 物品与物品、纪律、筹码或帮派资源交换；
- 物品赠送并换取关系/剧情结果；
- 材料、精力和时间交换为产出物；
- 存款、贷款、下注、兑奖等金融行为；
- 购买证件、申请、通行证等服务型交易。

战斗掉落、普通任务奖励和自动拾取不默认视为交易，但如果它们使用相同的物品/金钱结算入口，仍应保留低级来源信息。

## 2. 已确认的交易分类

| 分类 | 游戏入口/证据 | 当前状态 |
|---|---|---|
| 囚犯买卖 | `NpcItemBuyLogic`、`NpcItemSaleLogic`、`Prefab_OneTransaction` | 已确认入口，待运行验证 |
| 普通商店/自动售货机 | `ShopManage`、`ShopGoods`、`UI_ShopListUnit`、`c_shop`；`ShopId=9` 为自动售货机 | 已确认入口，自动售货机语义已接入 |
| 午餐与特殊商店 | `GetLunchGoodsList`、副队长/教士/女友商店及对应 `ThingChangeReason` | 已确认分类，待逐类验证 |
| 帮派商店 | `UI_GangShop`、`StorageGangShopApply`、`GangShopApplyItem` | 已确认入口，待验证订购/领取/出售语义 |
| 屋顶兑换 | `UI_ShopRoof`、`UI_ShopListUnitRoof` | 已确认金钱 + 纪律的双资源交易 |
| 电视购物 | `TVShopping`、`StorageTVShoppingInfo`、`TVShopGoodsHistory` | 已确认入口，待验证订单结算 |
| 女友商店包裹 | `StorageGirlFriendShopBuyHistory` | 已确认购买记录与后续领取 |
| 赠送与回礼 | `Prefab_OneGift`、`GiftBackLogic`、`ActionGiftBack` | 已确认社会关系交换 |
| 彩票与下注 | `UI_BuyLotteryTickets`、`ActionBoxingBet`、`UI_MatchBet` | 语义入口已接入，待运行验证筹码/奖金变化 |
| 生产与烹饪 | `Prefab_OneProduce`、`StorageProduceInfo`、`ProduceHistory` | 已确认材料/精力/时间结算 |
| 银行金融 | `ActionBank`、`StorageBankInfo` | 语义入口已接入，利息字段待运行验证 |
| 服务和剧情交换 | `ActionBuyCertificate`、`ActionBuyDiscipline`、`ActionBuySZZ`、申请系统及剧情 Action | 已确认专用入口，暂不抽象成普通商店 |

`ThingChangeReason` 已出现的交易相关来源包括：

```text
Buy, Sell, Bargain, BuyFromOtherPrisoner,
BuyLunch, BuyGangShopGoods, BuyPriestShopGoods,
BuyViceCaptainShopGoods, BuyViceCaptainMailRoomPass,
BuyLotteryTicket, TVShopping, BuyCertificate,
BuyDiscipline, BuySZZ, BuyApplication,
GiveGift, GiftBack, Produce,
ExchangeMoney, ExchangeChips, ExachangeColdBeer,
BoxingBet, BoxingBetExchange, MatchBet,
BankDeposit, BankTakeDepositMoney, BankLoan,
BankClearLoan, BankLoanOverdue, PrizeCashed,
ReceiveGirlFriendPackage, ReceiveWeekFreeGoods
```

## 3. 公共 API 需求

### T1. 统一交易模型

- [x] 定义 `TradeKind`，至少覆盖 `NpcBuy`、`NpcSell`、`ShopPurchase`、`GangShopPurchase`、`TvShopping`、`Gift`、`Production`、`Lottery`、`Betting`、`Bank` 和 `ServicePurchase`。
- [x] 定义基础 `TradeStatus`：`Started`、`Completed`、`Cancelled`、`Failed`。
- [x] 定义 `TradeCurrency`：`Money`、`Discipline`、`Chips`、`GangContribution`、`Relationship` 和 `Item`（物品变化由 `ItemDelta` 表示）。
- [ ] 所有物品使用 `ItemKey`，不在公共事件中暴露数字 ID。
- [x] 每次语义交易拥有进程内唯一 `TransactionId`，用于关联生命周期与结算观察。

### T2. 交易事件

- [x] 提供基础只读的 `TradeDetectedEvent`，按库存/金钱/纪律结算信号判断交易类型。
- [x] 为囚犯买卖增加语义交易观察，合并入口方法返回前后的库存/金钱变化。
- [x] 提供只读的 `TradeStartedEvent`。
- [x] 提供只读的 `TradeCompletedEvent`，包含实际物品变化、货币变化、来源和对象。
- [x] 提供基础 `TradeFailedEvent`；余额不足、库存不足、权限不足、库存已满、商品售罄和前置条件不满足的细分原因仍待逐类补齐。
- [ ] 只在底层确实能阻止操作时提供 `BeforeTradeEvent`；不能取消的路径不得伪装成可取消事件。
- [ ] 交易事件必须在真实状态结算之后发布，不能只根据按钮点击发布成功事件。
- [ ] 将同一交易的物品腿和金钱腿关联为稳定的 `TransactionId`。
- [ ] 事件订阅者异常必须隔离，并记录 Mod ID、交易类型和 `TransactionId`。

建议的事件数据：

```csharp
public sealed record TradeCompletedEvent(
    long TransactionId,
    TradeKind Kind,
    TradeStatus Status,
    IReadOnlyList<TradeItemChange> Items,
    IReadOnlyList<TradeCurrencyChange> Currencies,
    string Source,
    int? ShopId,
    int? CounterpartyId,
    string? GameReason);
```

### T3. 囚犯买卖

- [x] Hook `Prefab_OneTransaction.SubmitBuy`，记录玩家买入物品、数量、实际价格和对象角色。
- [x] Hook `Prefab_OneTransaction.DoSell`，记录玩家卖出物品、数量、实际价格和对象角色。
- [x] 保留 `NpcItemBuyLogic.BuyItem` / `NpcItemSaleLogic.SaleItem` 作为底层备用入口。
- [x] 通过 `CounterpartyId`、`CounterpartyName` 和 `TradeDirection` 暴露 NPC 身份与买卖方向。
- [ ] 记录普通价格、折扣、讨价还价和失败原因。
- [ ] 区分玩家与 NPC 的买卖，避免把 NPC 每日经济更新算作玩家交易。
- [ ] 优先使用 `Prefab_OneTransaction.BuySuccess`、`CounterOfferSuccess` 和 `DoSell` 作为语义结算点，UI 按钮仅作诊断备用。

### T4. 普通商店、特殊商店和屋顶兑换

- [x] 提供 `ShopKey`/`ShopCatalog`，将原始商店 ID 映射为稳定命名空间；重复的原始 ID 可共享同一逻辑商店键。
- [x] 记录屋顶兑换的商店 ID、物品 key、实际物品变化、金钱变化和纪律变化。
- [x] 记录普通商店的商品 `ItemKey`、数量、总价和稳定 `ShopKey`。
- [ ] 覆盖午餐、副队长、教士、女友、帮派、游戏卡和免费领取路径。
- [ ] 区分普通金钱购买、纪律兑换、免费领取和“购买后包裹领取”。
- [ ] 处理每日/每周刷新、文化限制、周末销售、售罄和权限限制。
- [ ] 屋顶商店必须同时记录金钱和纪律的变化。

### T5. 帮派商店、电视购物和包裹领取

- [ ] 记录帮派商店的订购、库存、权限、领取和可能的提交/出售行为。
- [ ] 记录电视购物订单号、期号、商品、价格和购买历史。
- [ ] 记录女友包裹从“购买记录”到“实际领取”的两个阶段，不合并成一次物品获得。
- [ ] 验证 `StorageGangShopApply`、`StorageTVShoppingInfo` 和 `StorageGirlFriendShopBuyHistory` 的写入时机。

### T6. 赠送、回礼和特殊剧情交换

- [ ] 记录赠送者、接收者、物品、数量、好感变化和接受/拒绝结果。
- [ ] 区分普通赠送、回礼、医生特殊礼物和任务赠送。
- [ ] 赠送取消或拒绝时不得发布成功事件，也不得错误扣除物品。
- [ ] 保留 `GiveGiftHistory` 的角色和物品关联，但公共 API 不暴露原始存档对象。

### T7. 生产、烹饪和资源兑换

- [ ] 记录配方、材料消耗、生产数量、精力、时间、解锁条件和最终产物。
- [ ] 区分“开始生产”和“完成生产”，支持中途取消/失败时的结果说明。
- [ ] 覆盖 `Produce`、烹饪、啤酒兑换、筹码兑换等非普通商店资源交易。
- [ ] 生产事件必须能关联材料减少与产物增加，避免 Mod 收到两次独立交易。

### T8. 彩票、下注和银行

- [ ] 记录彩票期号、号码、下注筹码、票数、中奖和兑奖金额。
- [ ] 记录拳赛下注、比赛下注、筹码兑换、胜负和奖金。
- [ ] 记录银行存款、取款、贷款、还款、利息和逾期，不把普通金钱变化误判成银行交易。
- [ ] 金融事件必须提供余额变化前后值或可计算的变化量。

### T9. 服务型交易与剧情入口

- [ ] 记录证件、纪律、SZZ、申请、邮件室通行证、披萨、华夫等服务型消费。
- [ ] 服务型交易必须标明 `ServiceId` 或原始动作 ID，不能伪装为普通物品购买。
- [ ] 剧情专用交换保留原始来源，若无法稳定抽象则只提供只读诊断事件。

### T10. 低级兼容层和重复事件保护

- [ ] 保留 `InventoryChangedEvent`、属性变化事件和 `ChangeMoney` 观察能力作为低级信号。
- [ ] 高层交易事件与库存/金钱事件必须通过 `TransactionId` 关联。
- [ ] 同一交易由多个底层方法结算时只发布一次高层成功事件。
- [ ] 未识别来源不得猜测交易类型，应发布 `Unknown` 或只记录低级变化。

## 4. 分阶段开发顺序

### Trade Phase 0：调查和日志

- [x] 从程序集确认交易分类、类名、方法签名和 `ThingChangeReason`。
- [ ] 为每类交易增加只读 Prefix/Postfix 日志，不改变游戏行为。
- [ ] 在真实游戏中逐项完成买、卖、赠送、生产、下注和银行操作。
- [ ] 记录实际调用顺序、库存变化、金钱变化和失败路径。

### Trade Phase 1：第一批高价值事件

- [x] 增加基于 `ThingChangeReason` 的 `TradeDetectedEvent`。
- [x] 实现 NPC 买卖事件（语义 Hook + NPC 身份）。
- [x] 实现普通商店购买事件入口。
- [x] 实现赠送事件（含 NPC 身份和方向）。
- [x] 用 ExampleMod 输出命名空间 ID、数量、价格、商店 ID、纪律变化和结果。

### Trade Phase 2：特殊商店和生产

- [ ] 实现帮派商店、屋顶兑换和电视购物事件。
- [ ] 实现生产开始/完成事件。
- [ ] 补充女友包裹和免费领取的两阶段事件。

### Trade Phase 3：金融、下注和服务

- [x] 接入彩票购票、拳赛下注/兑奖和比赛下注入口；筹码通过 `TradeCurrencyKind.Chips` 表示。
- [x] 接入银行存款、取款、贷款和还款入口；利息仍需在运行时验证并补充专用字段。
- [ ] 实现证件、申请、纪律等服务型交易事件。

### Trade Phase 4：公共服务 API

- [ ] 提供只读交易历史查询，默认按 `ItemKey` 和 `TransactionId` 返回。
- [ ] 提供受控交易命令前，先完成权限、主线程和失败回滚设计。
- [ ] 增加 API 契约测试、重复事件测试和版本兼容测试。
- [ ] 更新 `TECHNICAL.md`、README 和 ExampleMod 文档。

## 5. 当前不承诺的能力

- [ ] 不承诺所有剧情交换都能被统一取消。
- [ ] 不允许 Mod 直接调用 `ChangeMoney`、`AddItem` 或 `ReduceItem` 模拟交易并伪造高层事件。
- [ ] 不把动态数字 ID、UI 文本或按钮名称作为交易的稳定标识。
- [ ] 未经运行时验证，不把同名 `ThingChangeReason` 视为完整的交易结算证明。

## 6. 第一项实现建议

优先实现“囚犯买卖”这一项：它具有明确的语义方法、价格/折扣/对象信息，且能直接验证库存和金钱的联动。建议顺序为：

```text
NpcItemBuyLogic.BuyItem
    -> NpcItemSaleLogic.SaleItem
    -> Prefab_OneTransaction.BuySuccess / DoSell
    -> TradeCompletedEvent
```

完成这一项后，再复用同一交易模型接入普通商店和赠送系统。
