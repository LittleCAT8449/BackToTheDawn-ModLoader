using System.Reflection;
using HarmonyLib;

namespace BackToTheDawn.Loader;

/// <summary>Installs read-only traces for the game's native parcel lifecycle.</summary>
internal static class PackageHistoryDiagnostics
{
    internal static void Install(Harmony harmony)
    {
        Patch(
            harmony,
            AccessTools.Method(
                typeof(StorageBuyHistoryListCommon),
                nameof(StorageBuyHistoryListCommon.AddBuyItemCommonLog),
                new[] { typeof(int), typeof(int), typeof(ThingChangeReason), typeof(Thing) }),
            typeof(AddBuyItemCommonLogTrace),
            "Prefix",
            "Postfix");
        Patch(
            harmony,
            AccessTools.Method(
                typeof(StorageBuyHistoryListCommon),
                nameof(StorageBuyHistoryListCommon.ReceiveOnePackage),
                new[] { typeof(BuyHistoryCommon) }),
            typeof(ReceiveOnePackageTrace),
            "Prefix",
            "Postfix");
        Patch(
            harmony,
            AccessTools.Method(
                typeof(StorageBuyHistoryListCommon),
                nameof(StorageBuyHistoryListCommon.ReceiveOnePackageByItemId),
                new[] { typeof(int), typeof(ThingChangeReason) }),
            typeof(ReceiveOnePackageByItemIdTrace),
            "Prefix",
            "Postfix");
        Patch(
            harmony,
            AccessTools.Method(
                typeof(StorageBuyHistoryListCommon),
                "CheckTimeInMorning",
                new[] { typeof(int) }),
            typeof(CheckTimeInMorningTrace),
            null,
            "Postfix");
        Patch(
            harmony,
            AccessTools.Method(typeof(UI_ReceivePackage), nameof(UI_ReceivePackage.ShowReceivePackageUI)),
            typeof(ShowReceivePackageTrace),
            null,
            "Postfix");
    }

    private static void Patch(
        Harmony harmony,
        MethodInfo? target,
        Type patchType,
        string? prefixName,
        string? postfixName)
    {
        if (target is null)
        {
            Plugin.Logger?.LogWarning($"[PackageTrace] Could not resolve {patchType.Name} target method.");
            return;
        }

        var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var prefixMethod = prefixName is null ? null : patchType.GetMethod(prefixName, flags);
        var postfixMethod = postfixName is null ? null : patchType.GetMethod(postfixName, flags);
        try
        {
            harmony.Patch(
                target,
                prefix: prefixMethod is null ? null : new HarmonyMethod(prefixMethod),
                postfix: postfixMethod is null ? null : new HarmonyMethod(postfixMethod));
            Plugin.Logger?.LogInfo($"[PackageTrace] Observing {target.DeclaringType?.Name}.{target.Name}.");
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning(
                $"[PackageTrace] Could not observe {target.DeclaringType?.Name}.{target.Name}: {exception.Message}");
        }
    }

    private static long CountFor(
        StorageBuyHistoryListCommon storage,
        int itemId,
        ThingChangeReason reason)
    {
        var history = storage.itemBuyHistoryList;
        if (history is null) return 0;

        long count = 0;
        for (var index = 0; index < history.Count; index++)
        {
            var entry = history[index];
            if (entry is not null && entry.itemId == itemId && entry.reason == reason)
                count += entry.count;
        }

        return count;
    }

    private static int CountRows(StorageBuyHistoryListCommon storage) =>
        storage.itemBuyHistoryList?.Count ?? 0;

    private static string DescribeTime() =>
        GameProcess.singleton is { } process
            ? $"day={process.allDay}, clock={process.todayHour:D2}:{process.todayMinute:D2}, gameTime={process.gameTime}"
            : "gameTime=unavailable";

    private sealed record AddCallState(
        int ItemId,
        int RequestedCount,
        ThingChangeReason Reason,
        long CountBefore,
        int RowsBefore);

    private static class AddBuyItemCommonLogTrace
    {
        private static void Prefix(
            StorageBuyHistoryListCommon __instance,
            int itemId,
            int count,
            ThingChangeReason reason,
            out AddCallState __state)
        {
            __state = new AddCallState(
                itemId,
                count,
                reason,
                CountFor(__instance, itemId, reason),
                CountRows(__instance));
        }

        private static void Postfix(StorageBuyHistoryListCommon __instance, AddCallState __state)
        {
            var after = CountFor(__instance, __state.ItemId, __state.Reason);
            Plugin.Logger?.LogInfo(
                $"[PackageTrace] Added parcel record: item={__state.ItemId}, reason={__state.Reason}, " +
                $"requested={__state.RequestedCount}, recordedDelta={after - __state.CountBefore}, " +
                $"rows={__state.RowsBefore}->{CountRows(__instance)}, {DescribeTime()}.");
        }
    }

    private sealed record ReceiveState(int ItemId, int Count, int BuyTime, ThingChangeReason Reason);

    private static class ReceiveOnePackageTrace
    {
        private static void Prefix(BuyHistoryCommon one, out ReceiveState __state)
        {
            __state = one is null
                ? new ReceiveState(0, 0, 0, ThingChangeReason.Default)
                : new ReceiveState(one.itemId, one.count, one.buyTime, one.reason);
        }

        private static void Postfix(StorageBuyHistoryListCommon __instance, ReceiveState __state)
        {
            ShopParcelSettlement.PruneDeliverySources(__instance);
            Plugin.Logger?.LogInfo(
                $"[PackageTrace] Received parcel: item={__state.ItemId}, count={__state.Count}, " +
                $"buyTime={__state.BuyTime}, reason={__state.Reason}, " +
                $"remainingRows={CountRows(__instance)}, {DescribeTime()}.");
        }
    }

    private sealed record ReceiveByItemState(int ItemId, ThingChangeReason Reason, long CountBefore);

    private static class ReceiveOnePackageByItemIdTrace
    {
        private static void Prefix(
            StorageBuyHistoryListCommon __instance,
            int itemId,
            ThingChangeReason thingChangeReason,
            out ReceiveByItemState __state)
        {
            __state = new ReceiveByItemState(
                itemId,
                thingChangeReason,
                CountFor(__instance, itemId, thingChangeReason));
        }

        private static void Postfix(StorageBuyHistoryListCommon __instance, ReceiveByItemState __state)
        {
            ShopParcelSettlement.PruneDeliverySources(__instance);
            var after = CountFor(__instance, __state.ItemId, __state.Reason);
            Plugin.Logger?.LogInfo(
                $"[PackageTrace] Receive by item: item={__state.ItemId}, reason={__state.Reason}, " +
                $"countBefore={__state.CountBefore}, countAfter={after}, " +
                $"rows={CountRows(__instance)}, {DescribeTime()}.");
        }
    }

    private static class CheckTimeInMorningTrace
    {
        private static readonly HashSet<(int BuyTime, bool Result)> Logged = new();

        private static void Postfix(int buyTime, bool __result)
        {
            if (Logged.Add((buyTime, __result)))
                Plugin.Logger?.LogInfo(
                    $"[PackageTrace] Morning delivery check: buyTime={buyTime}, available={__result}, {DescribeTime()}.");
        }
    }

    private static class ShowReceivePackageTrace
    {
        private static void Postfix(UI_ReceivePackage? __result)
        {
            var storage = GameProcess.singleton?.buyHistoryListCommon;
            ShopParcelSettlement.PruneDeliverySources(storage);
            Plugin.Logger?.LogInfo(
                $"[PackageTrace] Receive parcel UI opened: uiAvailable={__result is not null}, " +
                $"hasPackage={storage?.IsHavePackage().ToString() ?? "unavailable"}, " +
                $"rows={storage?.itemBuyHistoryList?.Count.ToString() ?? "unavailable"}, {DescribeTime()}.");
        }
    }
}
