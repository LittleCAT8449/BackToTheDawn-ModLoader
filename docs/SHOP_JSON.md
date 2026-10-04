# JSON 商店模组

JSON 商店模组可以在不编写 DLL 的情况下注册新的原生商店目录。商品必须是游戏已经注册的物品；商店通过 ShopAPI 的同一运行时注册流程应用。

游戏物品的完整命名空间键见 [游戏物品键表](ITEM_KEYS.md)，JSON 的 `offers[].item` 和 C# 的 `ItemKey` 都使用其中的键。

## 目录结构

```text
BepInEx/mods/MyShopMod/
├── Manifest.json
└── shops/
    └── night-market.json
```

加载器会递归扫描模组目录下的 JSON 文件，只处理 `type` 为 `shop` 的对象；电话配置及其他 JSON 文件会跳过。扫描不会跟随目录链接，也不会进入带有独立模组清单的子目录。

## Manifest.json

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

## 商店定义

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

完整的 C# 商店注册、原生商店界面、库存、购买和现有商店修改说明见 [商店 API](SHOP_API.md)。
