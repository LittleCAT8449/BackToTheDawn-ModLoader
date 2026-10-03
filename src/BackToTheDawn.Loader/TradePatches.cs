using BackToTheDawn.ModAPI;
using HarmonyLib;

namespace BackToTheDawn.Loader;

// The generic ThingChangeReason for prisoner trading is often just "Buy".
// These semantic methods let the loader distinguish an NPC sale to the player
// from an ordinary shop purchase and attach the NPC identity.
[HarmonyPatch(typeof(NpcItemSaleLogic), nameof(NpcItemSaleLogic.SaleItem))]
internal static class NpcItemSaleLogicPatch
{
    private static void Prefix(
        NpcItemSaleLogic __instance,
        ItemIdCount item,
        out TradeSignals.SemanticTradeState? __state) =>
        __state = TradeSignals.BeginNpcTrade(
            TradeKind.NpcBuy,
            __instance.attribute,
            item?.itemId ?? 0,
            item?.itemCount ?? 0,
            TradeDirection.PlayerBuys,
            nameof(NpcItemSaleLogic.SaleItem),
            "NpcSale");

    private static void Postfix(TradeSignals.SemanticTradeState? __state) =>
        TradeSignals.EndNpcTrade(__state);

    private static Exception? Finalizer(
        TradeSignals.SemanticTradeState? __state,
        Exception? __exception)
    {
        if (__exception is not null)
        {
            TradeSignals.EndNpcTrade(__state, succeeded: false);
        }

        return __exception;
    }
}

[HarmonyPatch(typeof(NpcItemBuyLogic), nameof(NpcItemBuyLogic.BuyItem))]
internal static class NpcItemBuyLogicPatch
{
    private static void Prefix(
        NpcItemBuyLogic __instance,
        ItemIdCount item,
        out TradeSignals.SemanticTradeState? __state) =>
        __state = TradeSignals.BeginNpcTrade(
            TradeKind.NpcSell,
            __instance.attribute,
            item?.itemId ?? 0,
            item?.itemCount ?? 0,
            TradeDirection.PlayerSells,
            nameof(NpcItemBuyLogic.BuyItem),
            "NpcBuy");

    private static void Postfix(TradeSignals.SemanticTradeState? __state) =>
        TradeSignals.EndNpcTrade(__state);

    private static Exception? Finalizer(
        TradeSignals.SemanticTradeState? __state,
        Exception? __exception)
    {
        if (__exception is not null)
        {
            TradeSignals.EndNpcTrade(__state, succeeded: false);
        }

        return __exception;
    }
}

// The prisoner UI normally commits through Prefab_OneTransaction instead of
// invoking the two logic helpers directly. These are the semantic entry
// points used by the actual buy/sell callbacks.
[HarmonyPatch(typeof(Prefab_OneTransaction), nameof(Prefab_OneTransaction.SubmitBuy))]
internal static class PrefabOneTransactionSubmitBuyPatch
{
    private static void Prefix(
        Prefab_OneTransaction __instance,
        out TradeSignals.SemanticTradeState? __state)
    {
        var thing = __instance.GetThing();
        __state = TradeSignals.BeginNpcTrade(
            TradeKind.NpcBuy,
            __instance.attribute,
            thing?.id ?? 0,
            SafeSelectCount(__instance, thing),
            TradeDirection.PlayerBuys,
            nameof(Prefab_OneTransaction.SubmitBuy),
            "NpcSale");
    }

    private static void Postfix(TradeSignals.SemanticTradeState? __state) =>
        TradeSignals.EndNpcTrade(__state);

    private static Exception? Finalizer(
        TradeSignals.SemanticTradeState? __state,
        Exception? __exception)
    {
        if (__exception is not null)
        {
            TradeSignals.EndNpcTrade(__state, succeeded: false);
        }

        return __exception;
    }

    private static int SafeSelectCount(Prefab_OneTransaction instance, Thing? thing)
    {
        try
        {
            var count = instance.GetSelectCount();
            return count > 0 ? count : thing?.count ?? 0;
        }
        catch
        {
            return thing?.count ?? 0;
        }
    }
}

[HarmonyPatch(typeof(Prefab_OneTransaction), nameof(Prefab_OneTransaction.DoSell))]
internal static class PrefabOneTransactionDoSellPatch
{
    private static void Prefix(
        Prefab_OneTransaction __instance,
        out TradeSignals.SemanticTradeState? __state)
    {
        var thing = __instance.GetThing();
        __state = TradeSignals.BeginNpcTrade(
            TradeKind.NpcSell,
            __instance.attribute,
            thing?.id ?? 0,
            SafeSelectCount(__instance, thing),
            TradeDirection.PlayerSells,
            nameof(Prefab_OneTransaction.DoSell),
            "NpcBuy");
    }

    private static void Postfix(TradeSignals.SemanticTradeState? __state) =>
        TradeSignals.EndNpcTrade(__state);

    private static Exception? Finalizer(
        TradeSignals.SemanticTradeState? __state,
        Exception? __exception)
    {
        if (__exception is not null)
        {
            TradeSignals.EndNpcTrade(__state, succeeded: false);
        }

        return __exception;
    }

    private static int SafeSelectCount(Prefab_OneTransaction instance, Thing? thing)
    {
        try
        {
            var count = instance.GetSelectCount();
            return count > 0 ? count : thing?.count ?? 0;
        }
        catch
        {
            return thing?.count ?? 0;
        }
    }
}

[HarmonyPatch(typeof(Prefab_OneGift), nameof(Prefab_OneGift.DoGive))]
internal static class PrefabOneGiftDoGivePatch
{
    private static void Prefix(
        Prefab_OneGift __instance,
        out TradeSignals.SemanticTradeState? __state)
    {
        var thing = __instance.GetThing();
        __state = TradeSignals.BeginNpcTrade(
            TradeKind.Gift,
            ResolveTarget(__instance),
            thing?.id ?? 0,
            SafeSelectCount(__instance, thing),
            TradeDirection.PlayerGives,
            nameof(Prefab_OneGift.DoGive),
            "GiveGift");
    }

    private static void Postfix(TradeSignals.SemanticTradeState? __state) =>
        TradeSignals.EndNpcTrade(__state);

    private static Exception? Finalizer(
        TradeSignals.SemanticTradeState? __state,
        Exception? __exception)
    {
        if (__exception is not null)
        {
            TradeSignals.EndNpcTrade(__state, succeeded: false);
        }

        return __exception;
    }

    private static CharacterAttribute? ResolveTarget(Prefab_OneGift instance)
    {
        try
        {
            return instance.person ?? instance.attribute;
        }
        catch
        {
            return null;
        }
    }

    private static int SafeSelectCount(Prefab_OneGift instance, Thing? thing)
    {
        try
        {
            var count = instance.selectCount;
            return count > 0 ? count : thing?.count ?? 0;
        }
        catch
        {
            return thing?.count ?? 0;
        }
    }
}

[HarmonyPatch(typeof(WidgetGiftItemTips), nameof(WidgetGiftItemTips.SubmitReceiveGiftBack))]
internal static class WidgetGiftItemTipsReceivePatch
{
    private static void Prefix(
        WidgetGiftItemTips __instance,
        out TradeSignals.SemanticTradeState? __state)
    {
        var thing = __instance.giftBackThing;
        __state = TradeSignals.BeginNpcTrade(
            TradeKind.GiftBack,
            __instance.prisoner,
            thing?.id ?? 0,
            thing?.count ?? 0,
            TradeDirection.PlayerReceives,
            nameof(WidgetGiftItemTips.SubmitReceiveGiftBack),
            "GiftBack");
    }

    private static void Postfix(TradeSignals.SemanticTradeState? __state) =>
        TradeSignals.EndNpcTrade(__state);

    private static Exception? Finalizer(
        TradeSignals.SemanticTradeState? __state,
        Exception? __exception)
    {
        if (__exception is not null)
        {
            TradeSignals.EndNpcTrade(__state, succeeded: false);
        }

        return __exception;
    }
}

[HarmonyPatch(typeof(UI_ShopListUnit), nameof(UI_ShopListUnit.SubmitBuy))]
internal static class UiShopListUnitSubmitBuyPatch
{
    private static void Prefix(
        UI_ShopListUnit __instance,
        out TradeSignals.SemanticTradeState? __state)
    {
        var config = __instance.config;
        var shopConfig = config?.shopConfig;
        var thing = __instance.GetThing();
        var itemId = shopConfig?.goods_item ?? thing?.id ?? 0;
        var shopId = __instance.shopId != 0
            ? __instance.shopId
            : shopConfig?.shop_id ?? 0;
        ShopGoodsRuntime.Observe(config, shopId == 0 ? null : shopId, itemId);

        __state = TradeSignals.BeginShopTrade(
            TradeKind.ShopPurchase,
            shopId == 0 ? null : shopId,
            itemId,
            thing?.count ?? 1,
            TradeDirection.PlayerBuys,
            nameof(UI_ShopListUnit.SubmitBuy),
            "ShopBuy");
    }

    private static void Postfix(TradeSignals.SemanticTradeState? __state) =>
        TradeSignals.EndNpcTrade(__state);

    private static Exception? Finalizer(
        TradeSignals.SemanticTradeState? __state,
        Exception? __exception)
    {
        if (__exception is not null)
        {
            TradeSignals.EndNpcTrade(__state, succeeded: false);
        }

        return __exception;
    }
}

[HarmonyPatch(typeof(UI_ShopListUnitRoof), nameof(UI_ShopListUnitRoof.SubmitBuy))]
internal static class UiShopListUnitRoofSubmitBuyPatch
{
    private static void Prefix(
        UI_ShopListUnitRoof __instance,
        out TradeSignals.SemanticTradeState? __state)
    {
        var config = __instance.config;
        var shopConfig = config?.shopConfig;
        var thing = __instance.GetThing();
        var itemId = shopConfig?.goods_item ?? thing?.id ?? 0;
        var shopId = shopConfig?.shop_id ?? 0;
        ShopGoodsRuntime.Observe(config, shopId == 0 ? null : shopId, itemId);

        __state = TradeSignals.BeginShopTrade(
            TradeKind.RoofExchange,
            shopId == 0 ? null : shopId,
            itemId,
            thing?.count ?? 1,
            TradeDirection.PlayerBuys,
            nameof(UI_ShopListUnitRoof.SubmitBuy),
            "RoofExchange");
    }

    private static void Postfix(TradeSignals.SemanticTradeState? __state) =>
        TradeSignals.EndNpcTrade(__state);

    private static Exception? Finalizer(
        TradeSignals.SemanticTradeState? __state,
        Exception? __exception)
    {
        if (__exception is not null)
        {
            TradeSignals.EndNpcTrade(__state, succeeded: false);
        }

        return __exception;
    }
}

// Maggie's mail room consumes relationship/affection and records the item in
// a persistent buy-history list. The item itself is delivered later, so this
// hook emits an OrderPlaced observation even when the inventory is unchanged.
[HarmonyPatch(typeof(UI_MailItemListUnit), "OnSubmit")]
internal static class UiMailItemListUnitSubmitPatch
{
    private static void Prefix(
        UI_MailItemListUnit __instance,
        out TradeSignals.SemanticTradeState? __state)
    {
        var config = __instance.config;
        var shopConfig = config?.shopConfig;
        var itemId = shopConfig?.goods_item ?? 0;
        var cost = 0;
        try
        {
            cost = __instance.GetGoodsPrice();
        }
        catch
        {
            // Fall back to the action-level affection requirement below.
        }

        if (cost <= 0)
        {
            try
            {
                cost = ActionGirlFriend.GetNeedAffection();
            }
            catch
            {
                // Some builds calculate the value only inside SubmitMailItem.
            }
        }

        ShopGoodsRuntime.Observe(
            config,
            6,
            itemId,
            new ShopKey("backtothedawn", "maggie_shop"));

        __state = TradeSignals.BeginShopTrade(
            TradeKind.GirlfriendShopPurchase,
            6,
            itemId,
            1,
            TradeDirection.PlayerBuys,
            "UI_MailItemListUnit.OnSubmit",
            "BuyGirlFriendShopGoods",
            new ShopKey("backtothedawn", "maggie_shop"),
            TradePhase.OrderPlaced);
        TradeSignals.ObserveSemanticRelationshipCost(cost);
    }

    private static void Postfix(TradeSignals.SemanticTradeState? __state) =>
        TradeSignals.EndNpcTrade(__state);

    private static Exception? Finalizer(
        TradeSignals.SemanticTradeState? __state,
        Exception? __exception)
    {
        if (__exception is not null)
        {
            TradeSignals.EndNpcTrade(__state, succeeded: false);
        }

        return __exception;
    }
}

[HarmonyPatch(typeof(UI_MailItem), nameof(UI_MailItem.SubmitMailItem))]
internal static class UiMailItemSubmitMailItemPatch
{
    private static void Prefix(
        UI_MailItem __instance,
        int itemId,
        int count,
        ShopGoods config,
        int costValue,
        out TradeSignals.SemanticTradeState? __state)
    {
        if (TradeSignals.SemanticTradeActive)
        {
            TradeSignals.ObserveSemanticRelationshipCost(costValue);
            __state = null;
            return;
        }

        var shopId = 0;
        try
        {
            shopId = config?.shopConfig?.shop_id ?? __instance.shopId;
        }
        catch
        {
            // Keep the semantic key even if an optional UI field is absent.
        }

        ShopGoodsRuntime.Observe(
            config,
            shopId == 0 ? 6 : shopId,
            itemId,
            new ShopKey("backtothedawn", "maggie_shop"));

        __state = TradeSignals.BeginShopTrade(
            TradeKind.GirlfriendShopPurchase,
            shopId == 0 ? 6 : shopId,
            itemId,
            count,
            TradeDirection.PlayerBuys,
            nameof(UI_MailItem.SubmitMailItem),
            "BuyGirlFriendShopGoods",
            new ShopKey("backtothedawn", "maggie_shop"),
            TradePhase.OrderPlaced);
        TradeSignals.ObserveSemanticRelationshipCost(costValue);
    }

    private static void Postfix(TradeSignals.SemanticTradeState? __state) =>
        TradeSignals.EndNpcTrade(__state);

    private static Exception? Finalizer(
        TradeSignals.SemanticTradeState? __state,
        Exception? __exception)
    {
        if (__exception is not null)
        {
            TradeSignals.EndNpcTrade(__state, succeeded: false);
        }

        return __exception;
    }
}

// Gang orders are stored for next-day delivery under the bed. The public
// SubmitGangShopItem method is the stable semantic callback and receives the
// actual item/count arguments, unlike the generic ChangeMoney reason.
[HarmonyPatch(typeof(UI_GangShopListUnit), "Submitbuy")]
internal static class UiGangShopListUnitSubmitBuyPatch
{
    private static void Prefix(
        UI_GangShopListUnit __instance,
        out TradeSignals.SemanticTradeState? __state)
    {
        var config = __instance.config;
        var shopConfig = config?.shopConfig;
        var itemId = shopConfig?.goods_item ?? 0;
        var nativeShopId = shopConfig?.shop_id ?? 0;
        var requestedCount = 1;
        try
        {
            requestedCount = UI_GangShop.singleton?.buyItemCount ?? 1;
        }
        catch
        {
            // One is the game's default when no selector is active.
        }

        var key = ShopCatalogBootstrap.ResolveGangShopKey(nativeShopId, __instance.gangId);
        ShopGoodsRuntime.Observe(
            config,
            nativeShopId == 0 ? null : nativeShopId,
            itemId,
            key);
        __state = TradeSignals.BeginShopTrade(
            TradeKind.GangShopPurchase,
            nativeShopId == 0 ? null : nativeShopId,
            itemId,
            Math.Max(requestedCount, 1),
            TradeDirection.PlayerBuys,
            "UI_GangShopListUnit.Submitbuy",
            "BuyGangShopGoods",
            key,
            TradePhase.OrderPlaced);
    }

    private static void Postfix(TradeSignals.SemanticTradeState? __state) =>
        TradeSignals.EndNpcTrade(__state);

    private static Exception? Finalizer(
        TradeSignals.SemanticTradeState? __state,
        Exception? __exception)
    {
        if (__exception is not null)
        {
            TradeSignals.EndNpcTrade(__state, succeeded: false);
        }

        return __exception;
    }
}

[HarmonyPatch(typeof(UI_GangShop), nameof(UI_GangShop.SubmitGangShopItem))]
internal static class UiGangShopSubmitItemPatch
{
    private static void Prefix(
        UI_GangShop __instance,
        int itemId,
        int count,
        out TradeSignals.SemanticTradeState? __state)
    {
        if (TradeSignals.SemanticTradeActive)
        {
            __state = null;
            return;
        }

        var rawShopId = 0;
        var gangId = 0;
        try
        {
            rawShopId = __instance.shopId;
            gangId = __instance.shopId;
        }
        catch
        {
            // Older builds may expose only the singleton field; the event
            // remains useful even without a native shop discriminator.
        }

        var key = ShopCatalogBootstrap.ResolveGangShopKey(rawShopId, gangId);
        __state = TradeSignals.BeginShopTrade(
            TradeKind.GangShopPurchase,
            rawShopId == 0 ? null : rawShopId,
            itemId,
            Math.Max(count, 1),
            TradeDirection.PlayerBuys,
            nameof(UI_GangShop.SubmitGangShopItem),
            "BuyGangShopGoods",
            key,
            TradePhase.OrderPlaced);
    }

    private static void Postfix(TradeSignals.SemanticTradeState? __state) =>
        TradeSignals.EndNpcTrade(__state);

    private static Exception? Finalizer(
        TradeSignals.SemanticTradeState? __state,
        Exception? __exception)
    {
        if (__exception is not null)
        {
            TradeSignals.EndNpcTrade(__state, succeeded: false);
        }

        return __exception;
    }
}
