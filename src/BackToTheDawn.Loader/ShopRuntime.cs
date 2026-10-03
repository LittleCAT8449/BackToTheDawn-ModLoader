using BackToTheDawn.ModAPI;
using BackToTheDawn.ShopAPI;
using ShopRegistrationApi = BackToTheDawn.ShopAPI.ShopApi;
using UnityEngine;

namespace BackToTheDawn.Loader;

/// <summary>Applies Mod shop registrations to the game's live c_shop table.</summary>
internal static class ShopRuntime
{
    private const int FirstModShopId = 10_000;
    private sealed class RegisteredShop(string ownerId, ShopKey key, string displayName, ShopOffer[] offers, int nativeId)
    {
        public string OwnerId { get; } = ownerId;
        public ShopKey Key { get; } = key;
        public string DisplayName { get; } = displayName;
        public ShopOffer[] Offers { get; } = offers;
        public int NativeId { get; } = nativeId;
        public bool Applied { get; set; }
        public readonly List<c_shop> Rows = new();
    }

    private sealed record PendingOperation(string OwnerId, Action Apply, ShopKey? RegistrationKey = null);
    private sealed class PriceChange(string ownerId, c_shop row, int previousPrice, int appliedPrice)
    {
        public string OwnerId { get; } = ownerId;
        public c_shop Row { get; } = row;
        public int PreviousPrice { get; set; } = previousPrice;
        public int AppliedPrice { get; } = appliedPrice;
    }

    private static readonly Dictionary<string, RegisteredShop> Registered = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<PendingOperation> Pending = new();
    private static readonly List<(string OwnerId, c_shop Row)> AddedRows = new();
    private static readonly List<(string OwnerId, c_shop_name Row)> AddedNames = new();
    private static readonly List<PriceChange> PriceChanges = new();
    private static readonly Queue<int> OpenRequests = new();
    private static readonly HashSet<int> OpenRequestSet = new();
    private static int _nextOpenRetryFrame;
    private static int _openRetryCount;
    private static bool _shopUiRequested;

    internal static void InstallProviders()
    {
        ShopRegistrationApi.RegisterShopProvider = RegisterShop;
        ShopRegistrationApi.AddGoodsProvider = AddGoods;
        ShopRegistrationApi.SetPriceProvider = SetPrice;
        ShopRegistrationApi.OpenShopProvider = OpenShop;
    }

    internal static void ClearProviders()
    {
        ShopRegistrationApi.RegisterShopProvider = null;
        ShopRegistrationApi.AddGoodsProvider = null;
        ShopRegistrationApi.SetPriceProvider = null;
        ShopRegistrationApi.OpenShopProvider = null;
    }

    internal static void Tick()
    {
        if (!GameContextAdapter.IsGameplayReady) return;
        if (Pending.Count > 0)
        {
            foreach (var operation in Pending.ToArray())
            {
                try { operation.Apply(); }
                catch (Exception exception)
                {
                    Plugin.Logger?.LogError($"[ShopRuntime] Deferred operation from {operation.OwnerId} failed: {exception}");
                    if (operation.RegistrationKey is { } key)
                    {
                        Registered.Remove(key.ToString());
                        ShopCatalog.UnregisterRuntime(operation.OwnerId, key);
                    }
                }
                finally { Pending.Remove(operation); }
            }
        }

        if (OpenRequests.Count == 0 || Time.frameCount < _nextOpenRetryFrame) return;
        var shopId = OpenRequests.Peek();
        try
        {
            if (UI_Shop.singleton is null)
            {
                if (!_shopUiRequested)
                {
                    UIManage.ShowUINode("UI_Shop");
                    _shopUiRequested = true;
                }
                _openRetryCount++;
                _nextOpenRetryFrame = Time.frameCount + 1;
                if (_openRetryCount < 180) return;
                Plugin.Logger?.LogWarning("[ShopRuntime] Native UI_Shop did not become available.");
            }
            else
            {
                UI_Shop.singleton.ShowGoodsListByShopId(shopId);
            }
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning($"[ShopRuntime] Opening native shop {shopId} failed: {exception.Message}");
            _openRetryCount++;
            if (_openRetryCount < 180)
            {
                _nextOpenRetryFrame = Time.frameCount + 1;
                return;
            }
        }

        OpenRequests.Dequeue();
        OpenRequestSet.Remove(shopId);
        _openRetryCount = 0;
        _nextOpenRetryFrame = Time.frameCount + 1;
    }

    internal static void UnregisterMod(string ownerId)
    {
        Pending.RemoveAll(operation => operation.OwnerId.Equals(ownerId, StringComparison.OrdinalIgnoreCase));
        foreach (var shop in Registered.Values.Where(value => value.OwnerId.Equals(ownerId, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            if (shop.Applied)
            {
                foreach (var row in shop.Rows) RemoveGoodsObservation(shop.Key, row.goods_item);
                RemoveRows(shop.Rows);
                RemoveName(shop.NativeId);
            }
            Registered.Remove(shop.Key.ToString());
        }

        foreach (var entry in AddedRows.Where(entry => entry.OwnerId.Equals(ownerId, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            if (ItemIdResolver.TryGetKey(entry.Row.goods_item, out var itemKey) &&
                ShopCatalog.TryGetByNativeId(entry.Row.shop_id, out var descriptor))
                ShopGoodsCatalog.Remove(descriptor.Key, itemKey);
            RemoveRows(new[] { entry.Row });
            AddedRows.Remove(entry);
        }
        foreach (var entry in AddedNames.Where(entry => entry.OwnerId.Equals(ownerId, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            RemoveName(entry.Row.shop_id);
            AddedNames.Remove(entry);
        }
        foreach (var change in PriceChanges.Where(entry => entry.OwnerId.Equals(ownerId, StringComparison.OrdinalIgnoreCase)).Reverse().ToArray())
        {
            foreach (var laterChange in PriceChanges.Where(entry =>
                         ReferenceEquals(entry.Row, change.Row) &&
                         !entry.OwnerId.Equals(ownerId, StringComparison.OrdinalIgnoreCase) &&
                         entry.PreviousPrice == change.AppliedPrice))
                laterChange.PreviousPrice = change.PreviousPrice;
            try { if (change.Row.goods_price == change.AppliedPrice) change.Row.goods_price = change.PreviousPrice; }
            catch { }
            PriceChanges.Remove(change);
        }
        ShopCatalog.UnregisterRuntimeOwner(ownerId);
    }

    internal static void Reset()
    {
        foreach (var ownerId in Registered.Values.Select(shop => shop.OwnerId)
                     .Concat(AddedRows.Select(row => row.OwnerId))
                     .Concat(PriceChanges.Select(change => change.OwnerId))
                     .Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
            UnregisterMod(ownerId);
        Registered.Clear();
        Pending.Clear();
        AddedRows.Clear();
        AddedNames.Clear();
        PriceChanges.Clear();
        OpenRequests.Clear();
        OpenRequestSet.Clear();
        _nextOpenRetryFrame = 0;
        _openRetryCount = 0;
        _shopUiRequested = false;
    }

    private static ShopMutationResult RegisterShop(
        string ownerId, ShopKey key, string displayName, IReadOnlyList<ShopOffer> offers)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return Result(key, ShopMutationStatus.InvalidDefinition, "A shop display name is required.");
        if (Registered.ContainsKey(key.ToString()) || ShopCatalog.TryGet(key, out _))
            return Result(key, ShopMutationStatus.AlreadyExists, $"Shop '{key}' is already registered.");

        var nativeId = FindNextShopId();
        var registration = new RegisteredShop(ownerId, key, displayName.Trim(), offers.ToArray(), nativeId);
        if (!ShopCatalog.RegisterRuntime(ownerId, new ShopDescriptor(key, displayName.Trim(), new[] { nativeId }, ShopSource.Synthetic)))
            return Result(key, ShopMutationStatus.AlreadyExists, $"Shop key or native ID for '{key}' is already in use.");
        Registered.Add(key.ToString(), registration);
        Pending.Add(new PendingOperation(ownerId, () => ApplyRegisteredShop(registration), key));
        Plugin.Logger?.LogInfo($"[ShopRuntime] Registered Mod shop '{key}' (native ID {nativeId}); waiting for runtime item data.");
        return Result(key, ShopMutationStatus.Scheduled, "Shop registration will be applied when the game item catalog is ready.");
    }

    private static ShopMutationResult AddGoods(string ownerId, ShopKey key, ShopOffer offer)
    {
        if (!ShopCatalog.TryGet(key, out var descriptor)) return Result(key, ShopMutationStatus.NotFound, $"Shop '{key}' was not found.");
        if (descriptor.NativeShopIds.Count == 0) return Result(key, ShopMutationStatus.NotFound, $"Shop '{key}' has no native or registered shop window.");
        if (Registered.TryGetValue(key.ToString(), out var registered) && registered.Offers.Any(existing => existing.ItemKey == offer.ItemKey))
            return Result(key, ShopMutationStatus.AlreadyExists, $"Item '{offer.ItemKey}' is already in shop '{key}'.");

        Pending.Add(new PendingOperation(ownerId, () =>
        {
            var ids = ResolveItemAndShopIds(key, offer.ItemKey, descriptor, out var itemId);
            if (ids is null) throw new InvalidOperationException($"Shop '{key}' or item '{offer.ItemKey}' has no runtime ID.");
            var shopIds = ids.ToArray();
            if (shopIds.Any(shopId => FindRows(shopId).Any(row => row.goods_item == itemId)))
                throw new InvalidOperationException($"Item '{offer.ItemKey}' already exists in shop '{key}'.");
            var rows = shopIds.Select(shopId => CreateOffer(shopId, itemId, NextGoodsId(shopId), offer)).ToArray();
            var added = new List<c_shop>();
            try
            {
                foreach (var row in rows)
                {
                    ConfigData.singleton.shop.Add(row);
                    added.Add(row);
                    AddedRows.Add((ownerId, row));
                    ShopGoodsRuntime.Observe(null, row.shop_id, itemId, key);
                }
            }
            catch
            {
                foreach (var row in added)
                {
                    RemoveGoodsObservation(key, row.goods_item);
                    AddedRows.RemoveAll(entry => entry.OwnerId.Equals(ownerId, StringComparison.OrdinalIgnoreCase) && ReferenceEquals(entry.Row, row));
                }
                RemoveRows(added);
                throw;
            }
        }));
        return Result(key, ShopMutationStatus.Scheduled, "Goods will be added when the game item catalog is ready.");
    }

    private static ShopMutationResult SetPrice(string ownerId, ShopKey key, ItemKey itemKey, int price)
    {
        if (!ShopCatalog.TryGet(key, out var descriptor)) return Result(key, ShopMutationStatus.NotFound, $"Shop '{key}' was not found.");
        Pending.Add(new PendingOperation(ownerId, () =>
        {
            if (!ItemIdResolver.TryGetId(itemKey, out var itemId)) throw new InvalidOperationException($"Item '{itemKey}' has no runtime ID.");
            var rows = descriptor.NativeShopIds.SelectMany(FindRows).Where(row => row.goods_item == itemId).ToArray();
            if (rows.Length == 0) throw new InvalidOperationException($"Item '{itemKey}' is not sold by shop '{key}'.");
            var changes = new List<(c_shop Row, int PreviousPrice)>();
            try
            {
                foreach (var row in rows)
                {
                    var oldPrice = row.goods_price;
                    row.goods_price = price;
                    changes.Add((row, oldPrice));
                    PriceChanges.Add(new PriceChange(ownerId, row, oldPrice, price));
                    ShopGoodsRuntime.Observe(null, row.shop_id, row.goods_item, key);
                }
            }
            catch
            {
                foreach (var change in changes.AsEnumerable().Reverse())
                {
                    try
                    {
                        change.Row.goods_price = change.PreviousPrice;
                        ShopGoodsRuntime.Observe(null, change.Row.shop_id, change.Row.goods_item, key);
                    }
                    catch { }
                    PriceChanges.RemoveAll(entry => entry.OwnerId.Equals(ownerId, StringComparison.OrdinalIgnoreCase) &&
                                                    ReferenceEquals(entry.Row, change.Row) &&
                                                    entry.AppliedPrice == price);
                }
                throw;
            }
        }));
        return Result(key, ShopMutationStatus.Scheduled, "The price change will be applied when the game item catalog is ready.");
    }

    private static ShopMutationResult OpenShop(ShopKey key)
    {
        if (!ShopCatalog.TryGet(key, out var descriptor) || descriptor.NativeShopIds.Count == 0)
            return Result(key, ShopMutationStatus.NotFound, $"Shop '{key}' has no registered shop window.");
        if (!GameContextAdapter.IsGameplayReady) return Result(key, ShopMutationStatus.RuntimeUnavailable, "Start a game before opening a shop.");
        var id = descriptor.NativeShopIds[0];
        if (OpenRequestSet.Add(id)) OpenRequests.Enqueue(id);
        return Result(key, ShopMutationStatus.Scheduled, $"The native shop window for '{key}' will open shortly.");
    }

    private static void ApplyRegisteredShop(RegisteredShop shop)
    {
        if (!TryGetRuntimeRows(out var rows, out var shopNames))
            throw new InvalidOperationException("The game's shop configuration is not available yet.");
        var newRows = new List<c_shop>();
        foreach (var offer in shop.Offers)
        {
            if (!ItemIdResolver.TryGetId(offer.ItemKey, out var itemId))
                throw new InvalidOperationException($"Item '{offer.ItemKey}' has no runtime ID. Register/inject it before using it in a shop.");
            // The native shop UI treats goods_id as a 1-based selection ID.
            // Keep initial registration consistent with AddGoods/NextGoodsId,
            // which also assigns the first offer ID 1.
            newRows.Add(CreateOffer(shop.NativeId, itemId, newRows.Count + 1, offer));
        }
        var nameRow = new c_shop_name { shop_id = shop.NativeId, L_shop_name = shop.DisplayName };
        try
        {
            foreach (var row in newRows) rows.Add(row);
            shopNames.Add(nameRow);
            ConfigData.dict_shop_name?.Add(shop.NativeId, nameRow);
            shop.Rows.AddRange(newRows);
            foreach (var row in newRows) AddedRows.Add((shop.OwnerId, row));
            AddedNames.Add((shop.OwnerId, nameRow));
            foreach (var row in newRows)
                ShopGoodsRuntime.Observe(null, shop.NativeId, row.goods_item, shop.Key);
            shop.Applied = true;
            LogGoodsState(shop.NativeId, "registered");
            Plugin.Logger?.LogInfo($"[ShopRuntime] Added shop '{shop.Key}' as native ID {shop.NativeId} with {newRows.Count} item(s).");
        }
        catch
        {
            foreach (var row in newRows) RemoveGoodsObservation(shop.Key, row.goods_item);
            RemoveRows(newRows);
            RemoveName(shop.NativeId);
            AddedRows.RemoveAll(entry => entry.OwnerId.Equals(shop.OwnerId, StringComparison.OrdinalIgnoreCase) && newRows.Any(row => ReferenceEquals(entry.Row, row)));
            AddedNames.RemoveAll(entry => entry.OwnerId.Equals(shop.OwnerId, StringComparison.OrdinalIgnoreCase) && ReferenceEquals(entry.Row, nameRow));
            shop.Rows.Clear();
            shop.Applied = false;
            throw;
        }
    }

    private static c_shop CreateOffer(int shopId, int itemId, int goodsId, ShopOffer offer) => new()
    {
        shop_id = shopId,
        goods_id = goodsId,
        goods_type = 0,
        goods_item = itemId,
        goods_culture = 0,
        goods_price_type = 0,
        goods_price = offer.Price,
        group_number = 0,
        stock = offer.Stock,
        plural_bug = false,
        week_sale = string.Empty,
        show_date = 0,
    };

    private static IEnumerable<int>? ResolveItemAndShopIds(ShopKey key, ItemKey itemKey, ShopDescriptor descriptor, out int itemId)
    {
        if (!ItemIdResolver.TryGetId(itemKey, out itemId)) return null;
        if (Registered.TryGetValue(key.ToString(), out var registered)) return new[] { registered.NativeId };
        return descriptor.NativeShopIds;
    }

    private static int FindNextShopId()
    {
        var next = FirstModShopId;
        foreach (var id in ShopCatalog.All.SelectMany(shop => shop.NativeShopIds)) next = Math.Max(next, id + 1);
        try
        {
            var table = ConfigData.singleton?.shop;
            if (table is not null)
                foreach (var row in table)
                    if (row is not null) next = Math.Max(next, row.shop_id + 1);
            var names = ConfigData.singleton?.shop_name;
            if (names is not null)
                foreach (var row in names)
                    if (row is not null) next = Math.Max(next, row.shop_id + 1);
        }
        catch { }
        return next;
    }

    private static int NextGoodsId(int shopId) => FindRows(shopId).Select(row => row.goods_id).DefaultIfEmpty(0).Max() + 1;

    internal static void LogGoodsState(int shopId, string phase)
    {
        if (!ShopCatalog.TryGetByNativeId(shopId, out var descriptor) || descriptor.Source != ShopSource.Synthetic)
        {
            return;
        }

        try
        {
            var goodsList = ShopManage.GetGoodsListByShopId(shopId);
            foreach (var goods in goodsList)
            {
                if (goods is null)
                {
                    continue;
                }

                var row = goods.shopConfig;
                if (row is null)
                {
                    continue;
                }

                Plugin.Logger?.LogInfo(
                    $"[ShopRuntime] Stock state ({phase}) {descriptor.Key}: " +
                    $"goodsId={row.goods_id}, itemId={row.goods_item}, " +
                    $"configured={row.stock}, sold={goods.soldCount}, " +
                    $"remaining={goods.GetLeftCount()}, canSell={goods.IsCanSell()}.");
            }
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning($"[ShopRuntime] Reading stock state for '{descriptor.Key}' failed: {exception.Message}");
        }
    }

    internal static bool IsSyntheticShop(int shopId) =>
        ShopCatalog.TryGetByNativeId(shopId, out var descriptor) &&
        descriptor.Source == ShopSource.Synthetic;

    internal static bool TrySettleSyntheticPurchase(
        UI_ShopListUnit row,
        int requestedCount,
        int requestedCost,
        out string failureReason)
    {
        failureReason = string.Empty;
        var shopId = row.shopId != 0 ? row.shopId : row.config?.shopConfig?.shop_id ?? 0;
        var goods = row.config;
        var shopConfig = goods?.shopConfig;
        var itemId = shopConfig?.goods_item ?? row.GetThing()?.id ?? 0;
        var count = requestedCount > 0
            ? requestedCount
            : row.widgetItemListSelectCount?.currentSelectCount ?? 0;

        if (!IsSyntheticShop(shopId))
        {
            return RejectPurchase(row, "这不是模组注册的商店。", out failureReason);
        }

        if (goods is null || shopConfig is null || itemId <= 0 || count <= 0)
        {
            return RejectPurchase(row, "无法读取商品信息或购买数量。", out failureReason);
        }

        if (!goods.IsCanSell() || goods.GetLeftCount() < count)
        {
            return RejectPurchase(row, "商品库存不足。", out failureReason);
        }

        var unitPrice = Math.Max(shopConfig.goods_price, 0);
        var totalCostLong = (long)unitPrice * count;
        if (totalCostLong > int.MaxValue)
        {
            return RejectPurchase(row, "商品总价超出可处理范围。", out failureReason);
        }

        var totalCost = (int)totalCostLong;
        if (requestedCost != totalCost)
        {
            Plugin.Logger?.LogWarning(
                $"[ShopPurchase] Confirmation cost mismatch for shop={shopId}, item={itemId}: " +
                $"requested={requestedCost}, recalculated={totalCost}; using recalculated cost.");
        }

        var player = CharacterManage.protagonistAttribute;
        var package = player?.thingPackage;
        if (player is null || package is null || !GameContextAdapter.IsGameplayReady)
        {
            return RejectPurchase(row, "玩家库存尚未准备好。", out failureReason);
        }

        var moneyBefore = player.money;
        if (moneyBefore < totalCost)
        {
            return RejectPurchase(row, "现金不足。", out failureReason);
        }

        if (!package.CheckCanAddItem(itemId, count, PlaceType.Pocket))
        {
            return RejectPurchase(row, "口袋空间不足。", out failureReason);
        }

        if (!ItemIdResolver.TryGetKey(itemId, out var itemKey))
        {
            return RejectPurchase(row, $"找不到商品 {itemId} 对应的 ModAPI ItemKey。", out failureReason);
        }

        InventoryOperationResult addResult;
        try
        {
            addResult = ModApi.Inventory.TryAdd(itemKey, count);
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError(
                $"[ShopPurchase] Inventory API threw while adding {count} of '{itemKey}': {exception}");
            return RejectPurchase(row, "商品放入库存失败。", out failureReason);
        }

        if (addResult.Status != InventoryOperationStatus.Succeeded || addResult.ChangedCount != count)
        {
            RollbackAddedItems(itemKey, addResult.ChangedCount, shopId, itemId);
            Plugin.Logger?.LogWarning(
                $"[ShopPurchase] Inventory API did not add the full amount: shop={shopId}, " +
                $"item={itemId}, requested={count}, changed={addResult.ChangedCount}, " +
                $"status={addResult.Status}, message={addResult.Message}.");
            return RejectPurchase(row, "商品放入库存失败，尚未扣款。", out failureReason);
        }

        try
        {
            package.ChangeMoney(-totalCost, ThingChangeReason.Buy);
        }
        catch (Exception exception)
        {
            RollbackAddedItems(itemKey, count, shopId, itemId);
            Plugin.Logger?.LogError(
                $"[ShopPurchase] Charging {totalCost} for shop={shopId}, item={itemId} failed: {exception}");
            return RejectPurchase(row, "扣款失败，已撤销商品发放。", out failureReason);
        }

        var moneyAfter = player.money;
        var actualSpent = moneyBefore - moneyAfter;
        if (actualSpent != totalCost)
        {
            try
            {
                if (actualSpent != 0)
                {
                    package.ChangeMoney(actualSpent, ThingChangeReason.Buy);
                }
            }
            catch (Exception exception)
            {
                Plugin.Logger?.LogError(
                    $"[ShopPurchase] Refunding unexpected debit {actualSpent} failed: {exception}");
            }

            RollbackAddedItems(itemKey, count, shopId, itemId);
            Plugin.Logger?.LogError(
                $"[ShopPurchase] Charge verification failed: shop={shopId}, item={itemId}, " +
                $"expected={totalCost}, actual={actualSpent}; inventory grant rolled back.");
            return RejectPurchase(row, "扣款金额异常，已撤销商品发放。", out failureReason);
        }

        try
        {
            goods.soldCount += count;
        }
        catch (Exception exception)
        {
            try
            {
                package.ChangeMoney(totalCost, ThingChangeReason.Buy);
            }
            catch (Exception refundException)
            {
                Plugin.Logger?.LogError(
                    $"[ShopPurchase] Refunding after stock update failure failed: {refundException}");
            }

            RollbackAddedItems(itemKey, count, shopId, itemId);
            Plugin.Logger?.LogError(
                $"[ShopPurchase] Updating stock for shop={shopId}, item={itemId} failed: {exception}");
            return RejectPurchase(row, "更新商品库存失败，已撤销本次购买。", out failureReason);
        }

        try
        {
            row.ChangeCountCallBack();
            row.ShowBuyLog(count);
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning(
                $"[ShopPurchase] Purchase succeeded but the shop row UI refresh failed: {exception.Message}");
        }

        Plugin.Logger?.LogInfo(
            $"[ShopPurchase] Settled Mod shop purchase: shop={shopId}, item={itemId}, " +
            $"count={count}, unitPrice={unitPrice}, total={totalCost}, " +
            $"money={moneyBefore}->{moneyAfter}, stockRemaining={goods.GetLeftCount()}.");
        return true;
    }

    private static void RollbackAddedItems(ItemKey itemKey, int count, int shopId, int itemId)
    {
        if (count <= 0)
        {
            return;
        }

        try
        {
            var rollback = ModApi.Inventory.TryRemove(itemKey, count);
            if (rollback.Status != InventoryOperationStatus.Succeeded || rollback.ChangedCount != count)
            {
                Plugin.Logger?.LogError(
                    $"[ShopPurchase] Could not fully roll back inventory for shop={shopId}, item={itemId}: " +
                    $"requested={count}, removed={rollback.ChangedCount}, status={rollback.Status}.");
            }
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError(
                $"[ShopPurchase] Rolling back inventory for shop={shopId}, item={itemId} threw: {exception}");
        }
    }

    private static bool RejectPurchase(UI_ShopListUnit row, string message, out string failureReason)
    {
        failureReason = message;
        try
        {
            row.ui_headTips?.ShowTips(message);
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning($"[ShopPurchase] Showing rejection message failed: {exception.Message}");
        }

        Plugin.Logger?.LogWarning($"[ShopPurchase] Purchase rejected: {message}");
        return false;
    }

    private static IEnumerable<c_shop> FindRows(int shopId)
    {
        try
        {
            var result = new List<c_shop>();
            var table = ConfigData.singleton?.shop;
            if (table is null) return result;
            foreach (var row in table)
                if (row is not null && row.shop_id == shopId) result.Add(row);
            return result;
        }
        catch { return Array.Empty<c_shop>(); }
    }

    private static bool TryGetRuntimeRows(out Il2CppSystem.Collections.Generic.List<c_shop> rows, out Il2CppSystem.Collections.Generic.List<c_shop_name> names)
    {
        rows = null!;
        names = null!;
        try
        {
            rows = ConfigData.singleton?.shop!;
            names = ConfigData.singleton?.shop_name!;
            return rows is not null && names is not null;
        }
        catch { return false; }
    }

    private static void RemoveRows(IEnumerable<c_shop> rows)
    {
        try
        {
            var table = ConfigData.singleton?.shop;
            if (table is null) return;
            foreach (var row in rows.ToArray()) table.Remove(row);
        }
        catch (Exception exception) { Plugin.Logger?.LogWarning($"[ShopRuntime] Removing Mod shop rows failed: {exception.Message}"); }
    }

    private static void RemoveName(int id)
    {
        try
        {
            var table = ConfigData.singleton?.shop_name;
            if (table is not null)
            {
                var removals = new List<c_shop_name>();
                foreach (var row in table)
                    if (row is not null && row.shop_id == id) removals.Add(row);
                foreach (var row in removals) table.Remove(row);
            }
            ConfigData.dict_shop_name?.Remove(id);
        }
        catch (Exception exception) { Plugin.Logger?.LogWarning($"[ShopRuntime] Removing Mod shop name failed: {exception.Message}"); }
    }

    private static void RemoveGoodsObservation(ShopKey shopKey, int runtimeItemId)
    {
        if (ItemIdResolver.TryGetKey(runtimeItemId, out var itemKey))
            ShopGoodsCatalog.Remove(shopKey, itemKey);
    }

    private static ShopMutationResult Result(ShopKey key, ShopMutationStatus status, string message) => new(key, status, message);
}
