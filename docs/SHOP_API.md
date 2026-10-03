# 商店注册 API

独立的 `BackToTheDawn.ShopAPI.dll` 提供 `ShopApi`，支持把商品加入已有原生商店、调整现有售价、注册新的原生商店目录，并在模组运行时打开游戏自己的商店界面。项目同时需要引用 `BackToTheDawn.ModAPI.dll`，因为商店和物品使用 ModAPI 中的 `ShopKey` 与 `ItemKey`。

## 获取 API

```csharp
using BackToTheDawn.ModAPI;
using ShopApi = BackToTheDawn.ShopAPI.ShopApi;

var shops = ShopApi.For(context);
```

使用模组清单 ID 作为新商店的命名空间。API 调用返回 `ShopMutationResult`；`Scheduled` 表示请求已接受，Loader 会等游戏商品目录就绪后再写入运行时商店表。应用失败的原因会写入 `BepInEx/LogOutput.log`。

## 注册新商店并打开原生 UI

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

## 修改已有商店

游戏商店通过稳定的 `ShopKey` 定位。可在 [商店目录文档](SHOP_API_STATUS.md) 查看当前收录的 key。

```csharp
var shops = ShopApi.For(context);
var vendingMachine = new ShopKey("backtothedawn", "vending_machine");
var apple = new ItemKey("backtothedawn", "apple");

shops.SetPrice(vendingMachine, apple, price: 10);
shops.AddGoods(vendingMachine, new ShopOffer(apple, Price: 15, Stock: 5));
```

`AddGoods` 只新增该店目前没有的物品；已有同一物品时不会重复加货。`SetPrice` 修改该店对应的商品价格。

## 限制

- 商店数据写入当前运行时游戏配置，不修改安装文件。关闭或重启游戏后会重新应用模组注册。
- 新商店可以通过 Mod 代码打开原生 `UI_Shop`。NPC、场景物件和菜单按钮需要模组另行接入。
- 当前新增商品使用现金价格和普通商店商品行，不包含帮派订购、关系值、表现分、彩票或延迟订单等特殊交易流程。
- Mod 注册的物品只有在运行时注入成功后才能加入商店；若关闭 `Items/EnableRuntimeItemInjection`，请使用游戏原有物品。
- Mod 卸载时会移除它添加的商品并恢复它修改的原价。
