using HarmonyLib;

namespace BackToTheDawn.Loader;

/// <summary>Applies Mod-provided source labels to rows in the native parcel pickup UI.</summary>
[HarmonyPatch(typeof(UI_ReceivePackageListUnit), nameof(UI_ReceivePackageListUnit.InitShopListUnit))]
internal static class ReceivePackageSourcePatch
{
    private static void Postfix(UI_ReceivePackageListUnit __instance, BuyHistoryCommon config)
    {
        if (!ShopParcelSettlement.TryGetDeliverySource(config, out var source))
            return;

        var sourceText = __instance.goodsFormText;
        if (sourceText is null)
        {
            Plugin.Logger?.LogWarning(
                $"[ShopParcel] Could not display configured parcel source '{source}': goodsFormText is missing.");
            return;
        }

        sourceText.text = $"来自 {source}";
        Plugin.Logger?.LogInfo(
            $"[ShopParcel] Displayed custom parcel source for item={config.itemId}, " +
            $"count={config.count}, source='{source}'.");
    }
}
