using System.Runtime.CompilerServices;
using System.Text.Json;
using BepInEx;

namespace BackToTheDawn.Loader;

/// <summary>Settles Mod shop purchases through the game's next-day parcel queue.</summary>
internal static class ShopParcelSettlement
{
    private const ThingChangeReason ParcelReason = ThingChangeReason.OrderPizza;
    private readonly record struct ParcelSourceKey(int BuyTime, int ItemId, int Count, ThingChangeReason Reason);
    private sealed record PersistedParcelSource(int BuyTime, int ItemId, int Count, int Reason, string Source);
    private sealed record ParcelSourceTag(string Source);
    private static readonly JsonSerializerOptions SourceJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private static readonly ConditionalWeakTable<BuyHistoryCommon, ParcelSourceTag> SourcesByRow = new();
    private static readonly Dictionary<ParcelSourceKey, string> SourcesBySignature = new();
    private static bool _sourcesLoaded;

    private sealed record RowSnapshot(
        int Index,
        BuyHistoryCommon Row,
        int BuyTime,
        int Count,
        int ItemId,
        ThingChangeReason Reason);

    internal static bool TrySettle(
        UI_ShopListUnit row,
        ShopGoods goods,
        ThingPackage package,
        CharacterAttribute player,
        int shopId,
        int itemId,
        int count,
        int unitPrice,
        int totalCost,
        out string failureReason)
    {
        failureReason = string.Empty;
        var source = ShopRuntime.TryGetNextDayPackageSource(shopId, itemId, out var configuredSource)
            ? configuredSource
            : "大爆炸披萨";
        if (string.IsNullOrWhiteSpace(source))
            source = "大爆炸披萨";

        var process = GameProcess.singleton;
        var storage = process?.buyHistoryListCommon;
        var history = storage?.itemBuyHistoryList;
        if (storage is null || history is null)
        {
            Plugin.Logger?.LogWarning(
                $"[ShopPurchase] Parcel storage unavailable: shop={shopId}, item={itemId}, " +
                $"processAvailable={process is not null}, storageAvailable={storage is not null}, historyAvailable=false.");
            return Reject(row, "游戏的次日包裹记录列表尚未准备好。", out failureReason);
        }

        var thing = row.GetThing();
        var createdThing = false;
        if (thing is null)
        {
            try
            {
                // Synthetic shop rows are built from c_shop data and may not carry
                // the Thing instance normally supplied by an inventory-backed row.
                thing = new Thing(itemId, count);
                createdThing = thing is not null;
            }
            catch (Exception exception)
            {
                Plugin.Logger?.LogError(
                    $"[ShopPurchase] Could not create parcel Thing: shop={shopId}, item={itemId}, " +
                    $"count={count}: {exception}");
            }

            if (thing is null)
            {
                Plugin.Logger?.LogWarning(
                    $"[ShopPurchase] Parcel Thing unavailable: shop={shopId}, item={itemId}, " +
                    $"count={count}, rowThingAvailable=false.");
                return Reject(row, "无法为商品创建次日包裹记录。", out failureReason);
            }
        }

        Plugin.Logger?.LogInfo(
            $"[ShopPurchase] Parcel prerequisites ready: shop={shopId}, item={itemId}, count={count}, " +
            $"processAvailable=true, storageAvailable=true, historyAvailable=true, " +
            $"rowThingAvailable={!createdThing}, createdThingFallback={createdThing}.");

        var snapshot = SnapshotRows(storage, itemId);
        var countBefore = snapshot.Sum(entry => (long)entry.Count);
        try
        {
            storage.AddBuyItemCommonLog(itemId, count, ParcelReason, thing);
        }
        catch (Exception exception)
        {
            RestoreRows(storage, itemId, snapshot, FindNewRows(storage, itemId, snapshot));
            Plugin.Logger?.LogError(
                $"[ShopPurchase] Creating next-day parcel failed: shop={shopId}, item={itemId}, count={count}: {exception}");
            return Reject(row, "创建次日包裹失败，未扣款。", out failureReason);
        }

        var addedRows = FindNewRows(storage, itemId, snapshot);
        var sourceRows = addedRows.Count > 0
            ? addedRows
            : FindUpdatedRows(storage, itemId, snapshot);
        var recordedCount = CountItems(storage, itemId) - countBefore;
        if (recordedCount != count)
        {
            RestoreRows(storage, itemId, snapshot, addedRows);
            Plugin.Logger?.LogError(
                $"[ShopPurchase] Parcel record verification failed: shop={shopId}, item={itemId}, " +
                $"requested={count}, recorded={recordedCount}; matching parcel records restored.");
            return Reject(row, "包裹记录数量异常，已撤销本次下单。", out failureReason);
        }

        var moneyBefore = player.money;
        if (moneyBefore < totalCost)
        {
            RestoreRows(storage, itemId, snapshot, addedRows);
            return Reject(row, "现金不足，已撤销本次下单。", out failureReason);
        }

        try
        {
            package.ChangeMoney(-totalCost, ParcelReason);
        }
        catch (Exception exception)
        {
            Refund(package, moneyBefore - player.money, shopId, itemId);
            RestoreRows(storage, itemId, snapshot, addedRows);
            Plugin.Logger?.LogError(
                $"[ShopPurchase] Charging parcel order failed: shop={shopId}, item={itemId}, cost={totalCost}: {exception}");
            return Reject(row, "扣款失败，已撤销包裹订单。", out failureReason);
        }

        var moneyAfter = player.money;
        var actualSpent = moneyBefore - moneyAfter;
        if (actualSpent != totalCost)
        {
            Refund(package, actualSpent, shopId, itemId);
            RestoreRows(storage, itemId, snapshot, addedRows);
            Plugin.Logger?.LogError(
                $"[ShopPurchase] Parcel charge verification failed: shop={shopId}, item={itemId}, " +
                $"expected={totalCost}, actual={actualSpent}; matching parcel records restored.");
            return Reject(row, "扣款金额异常，已撤销包裹订单。", out failureReason);
        }

        var soldBefore = goods.soldCount;
        try
        {
            goods.soldCount += count;
            ShopRuntime.RecordManagedSale(shopId, goods.shopConfig.goods_id, count, totalCost);
        }
        catch (Exception exception)
        {
            try { goods.soldCount = soldBefore; }
            catch { }
            Refund(package, actualSpent, shopId, itemId);
            RestoreRows(storage, itemId, snapshot, addedRows);
            Plugin.Logger?.LogError(
                $"[ShopPurchase] Updating parcel-order stock failed: shop={shopId}, item={itemId}: {exception}");
            return Reject(row, "更新商品库存失败，已撤销包裹订单。", out failureReason);
        }

        try
        {
            row.ChangeCountCallBack();
            row.ShowBuyLog(count);
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning(
                $"[ShopPurchase] Parcel order succeeded but the shop row UI refresh failed: {exception.Message}");
        }

        Plugin.Logger?.LogInfo(
            $"[ShopPurchase] Placed next-day parcel order: shop={shopId}, item={itemId}, " +
            $"count={count}, unitPrice={unitPrice}, total={totalCost}, " +
            $"money={moneyBefore}->{moneyAfter}, stockRemaining={goods.GetLeftCount()}, reason={ParcelReason}.");
        foreach (var parcelRow in sourceRows)
            SetDeliverySource(parcelRow, source);
        return true;
    }

    internal static bool TryGetDeliverySource(BuyHistoryCommon? row, out string source)
    {
        EnsureSourcesLoaded();
        if (row is not null)
        {
            if (SourcesByRow.TryGetValue(row, out var tag))
            {
                source = tag.Source;
                return true;
            }

            if (SourcesBySignature.TryGetValue(GetSourceKey(row), out source!))
                return true;
        }

        source = string.Empty;
        return false;
    }

    internal static void PruneDeliverySources(StorageBuyHistoryListCommon? storage)
    {
        EnsureSourcesLoaded();
        var history = storage?.itemBuyHistoryList;
        if (history is null || SourcesBySignature.Count == 0)
            return;

        var activeKeys = new HashSet<ParcelSourceKey>();
        for (var index = 0; index < history.Count; index++)
        {
            var row = history[index];
            if (row is not null)
                activeKeys.Add(GetSourceKey(row));
        }

        var staleKeys = SourcesBySignature.Keys.Where(key => !activeKeys.Contains(key)).ToArray();
        if (staleKeys.Length == 0)
            return;

        foreach (var key in staleKeys)
            SourcesBySignature.Remove(key);
        SaveSources();
    }

    private static void SetDeliverySource(BuyHistoryCommon row, string source)
    {
        SourcesByRow.Remove(row);
        SourcesByRow.Add(row, new ParcelSourceTag(source));
        EnsureSourcesLoaded();
        SourcesBySignature[GetSourceKey(row)] = source;
        SaveSources();
        Plugin.Logger?.LogInfo(
            $"[ShopPurchase] Parcel source registered: item={row.itemId}, count={row.count}, " +
            $"buyTime={row.buyTime}, source='{source}'.");
    }

    private static ParcelSourceKey GetSourceKey(BuyHistoryCommon row) =>
        new(row.buyTime, row.itemId, row.count, row.reason);

    private static string SourceStorePath =>
        Path.Combine(Paths.ConfigPath, "dev.backtothedawn.loader.parcel-sources.json");

    private static void EnsureSourcesLoaded()
    {
        if (_sourcesLoaded)
            return;

        _sourcesLoaded = true;
        var path = SourceStorePath;
        var temporaryPath = path + ".tmp";
        var loadPath = File.Exists(temporaryPath) &&
                       (!File.Exists(path) || File.GetLastWriteTimeUtc(temporaryPath) >= File.GetLastWriteTimeUtc(path))
            ? temporaryPath
            : path;
        if (!File.Exists(loadPath))
            return;

        try
        {
            var entries = JsonSerializer.Deserialize<List<PersistedParcelSource>>(
                File.ReadAllText(loadPath), SourceJsonOptions) ?? new List<PersistedParcelSource>();
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Source))
                    continue;
                var key = new ParcelSourceKey(
                    entry.BuyTime,
                    entry.ItemId,
                    entry.Count,
                    (ThingChangeReason)entry.Reason);
                SourcesBySignature[key] = entry.Source;
            }
            Plugin.Logger?.LogInfo($"[ShopParcel] Loaded {SourcesBySignature.Count} custom parcel source label(s).");
            if (loadPath.Equals(temporaryPath, StringComparison.OrdinalIgnoreCase))
            {
                Plugin.Logger?.LogWarning("[ShopParcel] Recovering custom parcel source labels from a previous temporary file.");
                SaveSources();
            }
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError($"[ShopParcel] Loading custom parcel sources failed: {exception}");
        }
    }

    private static void SaveSources()
    {
        try
        {
            var path = SourceStorePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var entries = SourcesBySignature
                .OrderBy(entry => entry.Key.BuyTime)
                .ThenBy(entry => entry.Key.ItemId)
                .Select(entry => new PersistedParcelSource(
                    entry.Key.BuyTime,
                    entry.Key.ItemId,
                    entry.Key.Count,
                    (int)entry.Key.Reason,
                    entry.Value))
                .ToArray();
            File.WriteAllText(path, JsonSerializer.Serialize(entries, SourceJsonOptions));
            var temporaryPath = path + ".tmp";
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError($"[ShopParcel] Saving custom parcel sources failed: {exception}");
        }
    }

    private static List<RowSnapshot> SnapshotRows(StorageBuyHistoryListCommon storage, int itemId)
    {
        var result = new List<RowSnapshot>();
        var history = storage.itemBuyHistoryList;
        for (var index = 0; index < history.Count; index++)
        {
            var entry = history[index];
            if (entry is not null && entry.itemId == itemId && entry.reason == ParcelReason)
                result.Add(new RowSnapshot(index, entry, entry.buyTime, entry.count, entry.itemId, entry.reason));
        }
        return result;
    }

    private static long CountItems(StorageBuyHistoryListCommon storage, int itemId)
    {
        long result = 0;
        var history = storage.itemBuyHistoryList;
        for (var index = 0; index < history.Count; index++)
        {
            var entry = history[index];
            if (entry is not null && entry.itemId == itemId && entry.reason == ParcelReason)
                result += entry.count;
        }
        return result;
    }

    private static List<BuyHistoryCommon> FindNewRows(
        StorageBuyHistoryListCommon storage,
        int itemId,
        IReadOnlyList<RowSnapshot> snapshot)
    {
        var result = new List<BuyHistoryCommon>();
        var history = storage.itemBuyHistoryList;
        for (var index = 0; index < history.Count; index++)
        {
            var entry = history[index];
            if (entry is not null && entry.itemId == itemId && entry.reason == ParcelReason &&
                !snapshot.Any(original => ReferenceEquals(original.Row, entry)))
                result.Add(entry);
        }
        return result;
    }

    private static List<BuyHistoryCommon> FindUpdatedRows(
        StorageBuyHistoryListCommon storage,
        int itemId,
        IReadOnlyList<RowSnapshot> snapshot)
    {
        var result = new List<BuyHistoryCommon>();
        var history = storage.itemBuyHistoryList;
        for (var index = 0; index < history.Count; index++)
        {
            var entry = history[index];
            if (entry is null || entry.itemId != itemId || entry.reason != ParcelReason)
                continue;

            var original = snapshot.FirstOrDefault(value => ReferenceEquals(value.Row, entry));
            if (original is not null && (entry.count != original.Count || entry.buyTime != original.BuyTime))
                result.Add(entry);
        }

        return result;
    }

    /// <summary>Restores only this item's OrderPizza rows, preserving every other parcel row.</summary>
    private static void RestoreRows(
        StorageBuyHistoryListCommon storage,
        int itemId,
        IReadOnlyList<RowSnapshot> snapshot,
        IReadOnlyList<BuyHistoryCommon> addedRows)
    {
        try
        {
            var history = storage.itemBuyHistoryList;
            for (var index = history.Count - 1; index >= 0; index--)
            {
                var entry = history[index];
                if (entry is null || !addedRows.Any(added => ReferenceEquals(added, entry)))
                    continue;
                history.RemoveAt(index);
            }

            foreach (var original in snapshot.OrderBy(entry => entry.Index))
            {
                original.Row.buyTime = original.BuyTime;
                original.Row.count = original.Count;
                original.Row.itemId = original.ItemId;
                original.Row.reason = original.Reason;

                var present = false;
                for (var index = 0; index < history.Count; index++)
                {
                    if (!ReferenceEquals(history[index], original.Row)) continue;
                    present = true;
                    break;
                }

                if (!present)
                    history.Insert(Math.Min(original.Index, history.Count), original.Row);
            }
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError(
                $"[ShopPurchase] Restoring matching parcel records failed for item={itemId}, reason={ParcelReason}: {exception}");
        }
    }

    private static void Refund(ThingPackage package, int amount, int shopId, int itemId)
    {
        if (amount == 0) return;
        try { package.ChangeMoney(amount, ParcelReason); }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError(
                $"[ShopPurchase] Refunding {amount} after parcel-order failure for shop={shopId}, item={itemId} failed: {exception}");
        }
    }

    private static bool Reject(UI_ShopListUnit row, string message, out string failureReason)
    {
        failureReason = message;
        try { row.ui_headTips?.ShowTips(message); }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning($"[ShopPurchase] Showing parcel-order rejection failed: {exception.Message}");
        }
        Plugin.Logger?.LogWarning($"[ShopPurchase] Parcel order rejected: {message}");
        return false;
    }
}
