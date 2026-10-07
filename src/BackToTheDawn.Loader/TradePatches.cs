using BackToTheDawn.ModAPI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

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

[HarmonyPatch(typeof(UI_ShopListUnit), nameof(UI_ShopListUnit.InitShopListUnit))]
internal static class UiShopListUnitInitPatch
{
    private static void Postfix(UI_ShopListUnit __instance, int shopId)
    {
        if (!ShopCatalog.TryGetByNativeId(shopId, out var shop) ||
            shop.Source != ShopSource.Synthetic)
        {
            return;
        }

        var selector = __instance.widgetItemListSelectCount;
        if (selector is null || selector.maxSelectCount < 1)
        {
            return;
        }

        if (selector.currentSelectCount == 0)
        {
            var targetCount = Math.Max(selector.atLeastCount, 1);
            if (!selector.SetCount(targetCount))
            {
                Plugin.Logger?.LogWarning(
                    $"[ShopQuantity] Could not set Mod shop {shopId} purchase count to {targetCount} after row initialization.");
            }
        }

        // InitShopListUnit initializes the selector and then updates the item
        // icon from soldCount, which can overwrite the visible purchase count.
        // Re-run the native callback after the whole row is initialized.
        try
        {
            __instance.ChangeCountCallBack();
            __instance.widgetItem?.ShowFractionCount(
                selector.currentSelectCount,
                selector.maxSelectCount);
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning(
                $"[ShopQuantity] Refreshing Mod shop row {shopId} failed: {exception.Message}");
        }
        Plugin.Logger?.LogInfo(
            $"[ShopQuantity] Mod shop row initialized: shop={shopId}, " +
            $"current={selector.currentSelectCount}, max={selector.maxSelectCount}, " +
            $"display={__instance.widgetItem?.numerator}/{__instance.widgetItem?.denominator}.");
    }
}

// The game fills this label from its native item-price table. Mod-registered
// offers carry their price on c_shop.goods_price instead, so the native row
// otherwise leaves the visible price at zero even though checkout charges the
// configured amount.
[HarmonyPatch(typeof(UI_ShopListUnit), nameof(UI_ShopListUnit.ChangeCountCallBack))]
internal static class UiShopListUnitPriceDisplayPatch
{
    private static void Postfix(UI_ShopListUnit __instance)
    {
        if (!ModShopPriceDisplay.TryGetPrice(__instance, out var shopId, out var shopConfig, out var unitPrice))
        {
            return;
        }

        var priceText = __instance.costMoneyText;
        if (priceText is null)
        {
            return;
        }

        var displayedPrice = unitPrice.ToString();
        if (priceText.text == displayedPrice)
        {
            return;
        }

        var previousPrice = priceText.text;
        priceText.text = displayedPrice;
        Plugin.Logger?.LogInfo(
            $"[ShopQuantity] Refreshed Mod shop unit price: shop={shopId}, " +
            $"item={shopConfig.goods_item}, previous='{previousPrice}', price={displayedPrice}.");
    }
}

internal static class ModShopPriceDisplay
{
    internal static bool TryGetPrice(
        UI_ShopListUnit row,
        out int shopId,
        out c_shop shopConfig,
        out int unitPrice)
    {
        shopId = row.shopId != 0 ? row.shopId : row.config?.shopConfig?.shop_id ?? 0;
        shopConfig = row.config?.shopConfig!;
        unitPrice = 0;
        if (!ShopRuntime.IsSyntheticShop(shopId) || shopConfig is null)
        {
            return false;
        }

        unitPrice = Math.Max(0, shopConfig.goods_price);
        return true;
    }
}

// The game performs a later row refresh after InitShopListUnit and writes its
// item-table price (zero for injected offers) straight back into the label.
// Intercept that write so native refreshes cannot erase the registered price.
[HarmonyPatch(typeof(Text), "set_text")]
internal static class ModShopPriceTextSetterPatch
{
    private static readonly HashSet<int> LoggedNativeOverwrites = new();

    private static void Prefix(Text __instance, ref string value)
    {
        if (!string.Equals(value, "0", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var row = __instance.GetComponentInParent<UI_ShopListUnit>();
            if (row is null || row.costMoneyText is null ||
                row.costMoneyText.GetInstanceID() != __instance.GetInstanceID() ||
                !ModShopPriceDisplay.TryGetPrice(row, out var shopId, out var shopConfig, out var unitPrice) ||
                unitPrice <= 0)
            {
                return;
            }

            value = unitPrice.ToString();
            if (LoggedNativeOverwrites.Add(__instance.GetInstanceID()))
            {
                Plugin.Logger?.LogInfo(
                    $"[ShopQuantity] Preserved Mod shop unit price during native text refresh: " +
                    $"shop={shopId}, item={shopConfig.goods_item}, price={value}.");
            }
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning(
                $"[ShopQuantity] Preserving Mod shop price during native text refresh failed: {exception.Message}");
        }
    }
}

// InitShopListUnit can run before the selector receives its final max/min/current
// values. Set the initial quantity after the selector itself is initialized so
// that the game's InitCount(…, …, 0) cannot overwrite the default afterwards.
[HarmonyPatch(typeof(WidgetItemListSelectCount), nameof(WidgetItemListSelectCount.InitCount))]
internal static class ShopQuantityInitCountPatch
{
    private static void Postfix(
        WidgetItemListSelectCount __instance,
        int maxSelectCount,
        int atLeastCount,
        int currentSelectCount)
    {
        UI_ShopListUnit? shopUnit;
        try
        {
            shopUnit = FindShopListUnit(__instance.transform);
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning(
                $"[ShopQuantity] Could not resolve the owner of an initialized quantity selector: {exception.Message}");
            return;
        }

        if (shopUnit is null)
        {
            return;
        }

        var shopId = shopUnit.shopId;
        if (!ShopCatalog.TryGetByNativeId(shopId, out var shop) ||
            shop.Source != ShopSource.Synthetic)
        {
            return;
        }

        var selector = __instance;
        if (maxSelectCount < 1)
        {
            Plugin.Logger?.LogInfo(
                $"[ShopQuantity] Selector initialized for Mod shop {shopId}: " +
                $"min={atLeastCount}, max={maxSelectCount}, current={currentSelectCount}; no selectable stock.");
            return;
        }

        var targetCount = currentSelectCount > 0
            ? currentSelectCount
            : Math.Max(atLeastCount, 1);
        var countChanged = currentSelectCount == targetCount || selector.SetCount(targetCount);
        var callbackRefreshed = false;
        try
        {
            shopUnit.ChangeCountCallBack();
            callbackRefreshed = true;
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning(
                $"[ShopQuantity] Native count callback failed for Mod shop {shopId}: {exception.Message}");
        }

        // The game's row icon renders the visible current/max fraction through
        // WidgetItem separately from the selector's currentSelectCount value.
        // Keep that display synchronized with the initialized purchase amount.
        var widgetItem = shopUnit.widgetItem;
        if (widgetItem is not null)
        {
            widgetItem.ShowFractionCount(selector.currentSelectCount, maxSelectCount);
        }

        Plugin.Logger?.LogInfo(
            $"[ShopQuantity] Selector initialized for Mod shop {shopId}: " +
            $"min={atLeastCount}, max={maxSelectCount}, requested={targetCount}, " +
            $"setOrKept={countChanged}, callback={callbackRefreshed}, " +
            $"current={selector.currentSelectCount}, " +
            $"display={widgetItem?.numerator}/{widgetItem?.denominator}.");
    }

    private static UI_ShopListUnit? FindShopListUnit(Transform? transform)
    {
        var current = transform;
        for (var depth = 0; current is not null && depth < 16; depth++)
        {
            var shopUnit = current.GetComponent<UI_ShopListUnit>();
            if (shopUnit is not null)
            {
                return shopUnit;
            }

            current = current.parent;
        }

        return null;
    }
}

// The game's shop row can redraw its item fraction after InitCount. Override
// that later redraw too, keeping it bound to the purchase selector value.
[HarmonyPatch(typeof(WidgetItem), nameof(WidgetItem.ShowFractionCount))]
internal static class ShopQuantityFractionDisplayPatch
{
    private static void Prefix(WidgetItem __instance, ref int numerator, ref int denominator)
    {
        UI_ShopListUnit? shopUnit;
        try
        {
            shopUnit = FindShopListUnit(__instance.transform);
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning(
                $"[ShopQuantity] Could not resolve the row for a fraction redraw: {exception.Message}");
            return;
        }

        if (shopUnit is null ||
            !ShopCatalog.TryGetByNativeId(shopUnit.shopId, out var shop) ||
            shop.Source != ShopSource.Synthetic)
        {
            return;
        }

        var selector = shopUnit.widgetItemListSelectCount;
        if (selector is null || selector.maxSelectCount < 1)
        {
            return;
        }

        var selectedCount = selector.currentSelectCount > 0
            ? selector.currentSelectCount
            : Math.Max(selector.atLeastCount, 1);
        if (numerator != selectedCount || denominator != selector.maxSelectCount)
        {
            Plugin.Logger?.LogInfo(
                $"[ShopQuantity] Replacing Mod shop fraction {numerator}/{denominator} " +
                $"with {selectedCount}/{selector.maxSelectCount}.");
            numerator = selectedCount;
            denominator = selector.maxSelectCount;
        }
    }

    private static UI_ShopListUnit? FindShopListUnit(Transform? transform)
    {
        var current = transform;
        for (var depth = 0; current is not null && depth < 16; depth++)
        {
            var shopUnit = current.GetComponent<UI_ShopListUnit>();
            if (shopUnit is not null)
            {
                return shopUnit;
            }

            current = current.parent;
        }

        return null;
    }
}

[HarmonyPatch(typeof(UI_ShopListUnit), nameof(UI_ShopListUnit.TriggerBuyItem))]
internal static class UiShopListUnitPurchasePatch
{
    private static bool Prefix(
        UI_ShopListUnit __instance,
        int spentMoney,
        out TradeSignals.SemanticTradeState? __state)
    {
        var config = __instance.config;
        var shopConfig = config?.shopConfig;
        var thing = __instance.GetThing();
        var itemId = shopConfig?.goods_item ?? thing?.id ?? 0;
        var shopId = __instance.shopId != 0
            ? __instance.shopId
            : shopConfig?.shop_id ?? 0;
        var requestedCount = __instance.widgetItemListSelectCount?.currentSelectCount ?? 0;
        ShopGoodsRuntime.Observe(config, shopId == 0 ? null : shopId, itemId);

        var loaderManagedOffer = ShopRuntime.IsSyntheticShop(shopId) ||
                                 ShopRuntime.IsNextDayPackageOffer(shopId, itemId);
        if (loaderManagedOffer &&
            ShopQuantityPurchaseContext.TryGetCurrent(
                out _,
                out _,
                out var selectedItemId,
                out _,
                out var selectedCount,
                out _) &&
            selectedItemId == itemId)
        {
            // SubmitBuy can reset the row selector to one before calling this
            // method. Use the quantity captured from the confirmation slider.
            requestedCount = selectedCount;
        }

        __state = TradeSignals.BeginShopTrade(
            TradeKind.ShopPurchase,
            shopId == 0 ? null : shopId,
            itemId,
            requestedCount,
            TradeDirection.PlayerBuys,
            nameof(UI_ShopListUnit.TriggerBuyItem),
            "ShopBuy");

        if (!loaderManagedOffer)
        {
            return true;
        }

        if (!ShopRuntime.TrySettleManagedPurchase(
                __instance,
                requestedCount,
                spentMoney,
                out var failureReason))
        {
            TradeSignals.EndNpcTrade(__state, succeeded: false, failureReason);
            __state = null;
        }

        // Loader-managed offers are settled by ShopRuntime. Never let the
        // native handler apply a second inventory/currency change.
        return false;
    }

    private static void Postfix(
        UI_ShopListUnit __instance,
        TradeSignals.SemanticTradeState? __state)
    {
        TradeSignals.EndNpcTrade(__state);
        var shopId = __instance.shopId != 0
            ? __instance.shopId
            : __instance.config?.shopConfig?.shop_id ?? 0;
        if (shopId != 0)
        {
            ShopRuntime.LogGoodsState(shopId, "after TriggerBuyItem");
        }
    }

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
