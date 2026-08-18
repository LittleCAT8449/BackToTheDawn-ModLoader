using BackToTheDawn.ModAPI;

namespace BackToTheDawn.Loader;

internal static class ShopCatalogBootstrap
{
    private const string GameNamespace = "backtothedawn";

    internal static void Initialize()
    {
        ShopCatalog.InitializeStatic(
            new[]
            {
                Native("weekly_supplies", "每周物资", 0),
                Native("vice_captain_shop", "副队长商店", 1, 2),
                Native("bigfoot_shop", "大脚帮订购", 3, 10),
                Native("fang_shop", "尖牙帮订购", 4, 11),
                Native("blackclaw_shop", "黑爪帮订购", 5, 12),
                Native("maggie_shop", "玛姬", 6),
                Native("beth_doctor_shop", "贝丝医生", 7),
                Native("lunch_counter", "午餐点餐", 8),
                Native("vending_machine", "自动售货机", 9),
                Native("big_bang_pizza", "大爆炸披萨", 13),
                Native("lottery", "大乐透彩票", 14),
                Native("tv_shopping", "电视购物", 15),
                Native("roof_benefit", "屋顶福利", 16),
                Native("excess_benefit", "超额福利", 17),
                Native("church_goods", "教会财物", 18),
                Native("rocky_shop", "洛奇", 19),
                Semantic("bank", "银行"),
                Semantic("boxing_betting", "拳赛下注"),
                Semantic("match_betting", "比赛下注"),
                Semantic("barber_shop", "理发店"),
            });

        Plugin.Logger?.LogInfo(
            $"[ShopCatalog] Namespaced shop registry ready: " +
            $"{ShopCatalog.All.Count} shops, " +
            $"{ShopCatalog.All.Sum(shop => shop.NativeShopIds.Count)} native IDs.");
    }

    private static ShopDescriptor Native(
        string path,
        string displayName,
        params int[] nativeIds) =>
        new(
            new ShopKey(GameNamespace, path),
            displayName,
            nativeIds,
            ShopSource.NativeShopConfig,
            "L_shop_name_" + nativeIds[0]);

    private static ShopDescriptor Semantic(string path, string displayName) =>
        new(
            new ShopKey(GameNamespace, path),
            displayName,
            Array.Empty<int>(),
            ShopSource.SemanticHook);

    internal static ShopKey? ResolveGangShopKey(int nativeShopId, int gangId)
    {
        if (nativeShopId != 0 &&
            ShopCatalog.TryGetByNativeId(nativeShopId, out var descriptor) &&
            descriptor.Key.Path.EndsWith("_shop", StringComparison.OrdinalIgnoreCase))
        {
            return descriptor.Key;
        }

        try
        {
            var name = GangManage.GetGangName(gangId);
            if (name.Contains("大脚", StringComparison.OrdinalIgnoreCase))
            {
                return new ShopKey(GameNamespace, "bigfoot_shop");
            }

            if (name.Contains("尖牙", StringComparison.OrdinalIgnoreCase))
            {
                return new ShopKey(GameNamespace, "fang_shop");
            }

            if (name.Contains("黑爪", StringComparison.OrdinalIgnoreCase))
            {
                return new ShopKey(GameNamespace, "blackclaw_shop");
            }
        }
        catch
        {
            // Gang data may not be initialized during Harmony discovery.
        }

        return null;
    }
}
