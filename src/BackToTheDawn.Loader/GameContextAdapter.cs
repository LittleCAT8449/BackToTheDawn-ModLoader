using BackToTheDawn.ModAPI;

namespace BackToTheDawn.Loader;

internal static class GameContextAdapter
{
    internal static bool IsGameplayReady { get; set; }
    private static GameTimeSnapshot? _lastPublishedTime;
    private static int? _lastPublishedMapId;
    private static PlayerSnapshot? _lastPublishedPlayer;
    private static readonly HashSet<string> ObservedPlayerHookSources = new();
    private static readonly object PendingItemUseSync = new();
    private static readonly Dictionary<string, Stack<ItemUseInvocationState>> PendingItemUses =
        new(StringComparer.Ordinal);

    internal static GameStateSnapshot? Capture()
    {
        var process = GameProcess.singleton;
        if (process is null)
        {
            return null;
        }

        var mapId = MapManage.currentMap?.id ?? process.currentMapId;
        var mapName = GetMapName(mapId);

        return new GameStateSnapshot(
            IsGameplayReady,
            ArchiveData.GetGameArchiveDataId(),
            mapId,
            mapName,
            CaptureTime(),
            CapturePlayer());
    }

    internal static InventorySnapshot? CaptureInventory()
    {
        if (!IsGameplayReady)
        {
            return null;
        }

        try
        {
            var protagonist = CharacterManage.protagonistAttribute;
            var package = protagonist?.thingPackage;
            return package is null
                ? null
                : CaptureInventory(package);
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError($"[Inventory] Failed to capture snapshot: {exception}");
            return null;
        }
    }

    private static InventorySnapshot CaptureInventory(ThingPackage package)
    {
        var stacks = new List<InventoryStack>();
        var things = package.thingList;
        if (things is not null)
        {
            for (var index = 0; index < things.Count; index++)
            {
                var thing = things[index];
                if (thing is null || thing.id <= 0 || thing.count <= 0)
                {
                    continue;
                }

                stacks.Add(
                    new InventoryStack(
                        ItemCatalog.ResolveOrCreateKey(thing.id),
                        thing.count,
                        CaptureInventoryLocation(thing),
                        IsFullGrid(thing)));
            }
        }

        return new InventorySnapshot(package.cId, stacks);
    }

    internal static InventoryOperationResult TryAddInventory(ItemKey itemKey, int count)
    {
        if (count <= 0)
        {
            return InventoryResult(
                itemKey,
                count,
                InventoryOperationStatus.InvalidCount,
                "Count must be greater than zero.");
        }

        if (!TryGetPlayerPackage(out var package))
        {
            return InventoryResult(
                itemKey,
                count,
                InventoryOperationStatus.NotReady,
                "Gameplay is not ready or the player inventory is unavailable.");
        }

        if (!ItemIdResolver.TryGetId(itemKey, out var itemId))
        {
            return InventoryResult(
                itemKey,
                count,
                InventoryOperationStatus.ItemNotFound,
                $"Item '{itemKey}' is not present in the runtime catalog.");
        }

        try
        {
            var before = CaptureInventory(package).GetCount(itemKey);
            package.AddItem(
                itemId,
                count,
                PlaceType.Pocket,
                ThingChangeReason.Default);
            var after = CaptureInventory(package).GetCount(itemKey);
            var changed = Math.Max(after - before, 0);
            return InventoryResult(
                itemKey,
                count,
                changed,
                after,
                changed == 0
                    ? InventoryOperationStatus.NoSpace
                    : changed < count
                        ? InventoryOperationStatus.Partial
                        : InventoryOperationStatus.Succeeded,
                changed == 0 ? "The game did not add the item." : "Item added.");
        }
        catch (Exception exception)
        {
            return InventoryResult(
                itemKey,
                count,
                InventoryOperationStatus.Failed,
                $"Add failed: {exception.Message}");
        }
    }

    internal static InventoryOperationResult TryRemoveInventory(ItemKey itemKey, int count)
    {
        if (count <= 0)
        {
            return InventoryResult(
                itemKey,
                count,
                InventoryOperationStatus.InvalidCount,
                "Count must be greater than zero.");
        }

        if (!TryGetPlayerPackage(out var package))
        {
            return InventoryResult(
                itemKey,
                count,
                InventoryOperationStatus.NotReady,
                "Gameplay is not ready or the player inventory is unavailable.");
        }

        if (!ItemIdResolver.TryGetId(itemKey, out var itemId))
        {
            return InventoryResult(
                itemKey,
                count,
                InventoryOperationStatus.ItemNotFound,
                $"Item '{itemKey}' is not present in the runtime catalog.");
        }

        try
        {
            var before = CaptureInventory(package).GetCount(itemKey);
            if (before < count)
            {
                return InventoryResult(
                    itemKey,
                    count,
                    0,
                    before,
                    InventoryOperationStatus.NotEnoughItems,
                    $"Requested {count}, but only {before} item(s) are available.");
            }

            package.ReduceItem(
                itemId,
                count,
                ThingChangeReason.Default,
                true);
            var after = CaptureInventory(package).GetCount(itemKey);
            var changed = Math.Max(before - after, 0);
            return InventoryResult(
                itemKey,
                count,
                changed,
                after,
                changed == 0
                    ? InventoryOperationStatus.Failed
                    : changed < count
                        ? InventoryOperationStatus.Partial
                        : InventoryOperationStatus.Succeeded,
                changed == 0 ? "The game did not remove the item." : "Item removed.");
        }
        catch (Exception exception)
        {
            return InventoryResult(
                itemKey,
                count,
                InventoryOperationStatus.Failed,
                $"Remove failed: {exception.Message}");
        }
    }

    internal static InventoryOperationResult TryMoveInventory(
        ItemKey itemKey,
        InventoryLocation from,
        InventoryLocation to,
        int count)
    {
        if (count <= 0)
        {
            return InventoryResult(
                itemKey,
                count,
                InventoryOperationStatus.InvalidCount,
                "Count must be greater than zero.");
        }

        if (!TryGetPlayerPackage(out var package))
        {
            return InventoryResult(
                itemKey,
                count,
                InventoryOperationStatus.NotReady,
                "Gameplay is not ready or the player inventory is unavailable.");
        }

        if (!ItemIdResolver.TryGetId(itemKey, out var itemId))
        {
            return InventoryResult(
                itemKey,
                count,
                InventoryOperationStatus.ItemNotFound,
                $"Item '{itemKey}' is not present in the runtime catalog.");
        }

        if (!Enum.TryParse<PlaceType>(to.Container, true, out var target) ||
            target == PlaceType.None)
        {
            return InventoryResult(
                itemKey,
                count,
                InventoryOperationStatus.InvalidLocation,
                $"Unknown target container '{to.Container}'.");
        }

        try
        {
            var before = CaptureInventory(package);
            var thing = FindThing(package, itemId, from);
            if (thing is null)
            {
                return InventoryResult(
                    itemKey,
                    count,
                    0,
                    before.GetCount(itemKey),
                    InventoryOperationStatus.InvalidLocation,
                    $"No '{itemKey}' stack was found at {from.Container}.");
            }

            if (thing.count < count)
            {
                return InventoryResult(
                    itemKey,
                    count,
                    0,
                    before.GetCount(itemKey),
                    InventoryOperationStatus.NotEnoughItems,
                    $"Requested {count}, but the selected stack contains {thing.count}.");
            }

            var targetBefore = GetLocationCount(before, itemKey, to);
            var success = count == thing.count
                ? package.MoveThingPlace(thing, target)
                : package.MoveThingPlace(thing, target, count) is not MoveThingPlaceResult.False
                    and not MoveThingPlaceResult.NoPlace;
            var after = CaptureInventory(package);
            var moved = Math.Max(GetLocationCount(after, itemKey, to) - targetBefore, 0);
            if (!success || moved == 0)
            {
                return InventoryResult(
                    itemKey,
                    count,
                    moved,
                    after.GetCount(itemKey),
                    InventoryOperationStatus.Failed,
                    "The game did not move the selected item.");
            }

            return InventoryResult(
                itemKey,
                count,
                moved,
                after.GetCount(itemKey),
                moved < count
                    ? InventoryOperationStatus.Partial
                    : InventoryOperationStatus.Succeeded,
                "Item moved.");
        }
        catch (Exception exception)
        {
            return InventoryResult(
                itemKey,
                count,
                InventoryOperationStatus.Failed,
                $"Move failed: {exception.Message}");
        }
    }

    private static bool TryGetPlayerPackage(out ThingPackage package)
    {
        package = null!;
        if (!IsGameplayReady)
        {
            return false;
        }

        var protagonist = CharacterManage.protagonistAttribute;
        package = protagonist?.thingPackage!;
        return package is not null;
    }

    private static Thing? FindThing(
        ThingPackage package,
        int itemId,
        InventoryLocation location)
    {
        var things = package.thingList;
        if (things is null)
        {
            return null;
        }

        for (var index = 0; index < things.Count; index++)
        {
            var thing = things[index];
            if (thing is not null &&
                thing.id == itemId &&
                MatchesLocation(CaptureInventoryLocation(thing), location))
            {
                return thing;
            }
        }

        return null;
    }

    private static int GetLocationCount(
        InventorySnapshot snapshot,
        ItemKey itemKey,
        InventoryLocation location) =>
        snapshot.Items
            .Where(item => item.ItemKey == itemKey && MatchesLocation(item.Location, location))
            .Sum(item => Math.Max(item.Count, 0));

    private static bool MatchesLocation(InventoryLocation left, InventoryLocation right) =>
        left.PlaceId == right.PlaceId &&
        left.SubPlaceId == right.SubPlaceId &&
        left.Container.Equals(right.Container, StringComparison.OrdinalIgnoreCase);

    private static InventoryOperationResult InventoryResult(
        ItemKey itemKey,
        int requestedCount,
        InventoryOperationStatus status,
        string message) =>
        InventoryResult(itemKey, requestedCount, 0, 0, status, message);

    private static InventoryOperationResult InventoryResult(
        ItemKey itemKey,
        int requestedCount,
        int changedCount,
        int remainingCount,
        InventoryOperationStatus status,
        string message) =>
        new(
            itemKey,
            requestedCount,
            changedCount,
            remainingCount,
            status,
            message);

    private static InventoryLocation CaptureInventoryLocation(Thing thing)
    {
        try
        {
            var place = thing.place;
            return place is null
                ? new InventoryLocation("Unknown")
                : new InventoryLocation(
                    place.pt.ToString(),
                    place.placeId,
                    place.subPlaceId);
        }
        catch
        {
            return new InventoryLocation("Unknown");
        }
    }

    private static bool IsFullGrid(Thing thing)
    {
        try
        {
            return thing.IsFullGrid();
        }
        catch
        {
            return false;
        }
    }

    internal static GameTimeSnapshot CaptureTime()
    {
        var time = TimeManage.nowTime;
        return new GameTimeSnapshot(
            time.day,
            time.wakeDay,
            time.hour,
            time.minute,
            time.GetTotalMinutes());
    }

    internal static int GetCurrentMapId() =>
        MapManage.currentMap?.id ?? GameProcess.singleton?.currentMapId ?? 0;

    internal static void InitializeEventBaselines()
    {
        _lastPublishedTime = CaptureTime();
        _lastPublishedMapId = GetCurrentMapId();
        _lastPublishedPlayer = CapturePlayer();
    }

    internal static void PublishTimeIfChanged()
    {
        var current = CaptureTime();
        var previous = _lastPublishedTime;
        _lastPublishedTime = current;

        if (previous is not null && previous != current)
        {
            GameEvents.RaiseTimeChanged(previous, current);
        }
    }

    internal static void PublishMapIfChanged(Map map)
    {
        var currentMapId = map.id;
        var previousMapId = _lastPublishedMapId ?? MapManage.previousMapId;
        _lastPublishedMapId = currentMapId;

        if (previousMapId != currentMapId)
        {
            GameEvents.RaiseMapChanged(
                previousMapId,
                currentMapId,
                GetMapName(currentMapId));
        }
    }

    internal static void PublishPlayerIfChanged(ThingPackage package, string source)
    {
        if (!IsGameplayReady || package is null)
        {
            return;
        }

        try
        {
            var protagonist = CharacterManage.protagonistAttribute;
            if (protagonist is null || package.cId != protagonist.id)
            {
                return;
            }

            if (ObservedPlayerHookSources.Add(source))
            {
                Plugin.Logger?.LogInfo(
                    $"[LifecycleHook] Player state source observed: {source} (cId={package.cId}).");
            }

            var current = CapturePlayer();
            var previous = _lastPublishedPlayer;
            _lastPublishedPlayer = current;

            if (previous is not null && current is not null && previous != current)
            {
                GameEvents.RaisePlayerStateChanged(previous, current, source);
            }
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError(
                $"[GameContext] Failed to publish player state after {source}: {exception}");
        }
    }

    internal static void PublishPlayerItemUsed(
        CharacterAttribute attribute,
        int itemId,
        int useCount)
    {
        if (!IsGameplayReady || attribute is null)
        {
            return;
        }

        try
        {
            var protagonist = CharacterManage.protagonistAttribute;
            if (protagonist is null || attribute.id != protagonist.id)
            {
                return;
            }

            var itemKey = ItemCatalog.ResolveOrCreateKey(itemId);
            GameEvents.RaisePlayerItemUsed(attribute.id, itemKey, useCount);
            GameEvents.RaisePlayerItemAction(
                attribute.id,
                itemKey,
                useCount,
                ItemActionKind.Use,
                succeeded: true,
                source: nameof(CharacterAttribute.UseItem));
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError(
                $"[GameContext] Failed to publish player item use for item {itemId}: {exception}");
        }
    }

    internal static bool TryBeginItemUse(
        CharacterAttribute attribute,
        int itemId,
        ref int useCount,
        out ItemUseInvocationState? state)
    {
        if (attribute is not null &&
            TryTakePendingItemUse(attribute.id, itemId, out state))
        {
            if (state.Context.RequestedCount > 0)
            {
                useCount = state.Context.RequestedCount;
            }

            return !state.SkipOriginal;
        }

        return PrepareItemUse(attribute, itemId, ref useCount, out state);
    }

    internal static bool TryBeginItemUse(
        ThingPackage package,
        Thing thing,
        ref int useCount,
        out ItemUseInvocationState? state,
        string source = nameof(ThingPackage.UseThing))
    {
        state = null;
        if (package is null || thing is null || package.attribute is null)
        {
            return true;
        }

        if (TryPeekPendingItemUse(package.attribute.id, thing.id, out var pending))
        {
            state = pending;
            if (state.Context.RequestedCount > 0)
            {
                useCount = state.Context.RequestedCount;
            }

            return !state.SkipOriginal;
        }

        var allowOriginal = PrepareItemUse(
            package.attribute,
            thing.id,
            ref useCount,
            out state);
        if (allowOriginal && state is not null)
        {
            TrackItemUseInventory(package, thing.id, state, source);
            StorePendingItemUse(state);
        }

        return allowOriginal;
    }

    private static bool PrepareItemUse(
        CharacterAttribute? attribute,
        int itemId,
        ref int useCount,
        out ItemUseInvocationState? state)
    {
        state = null;
        if (!IsGameplayReady || attribute is null || attribute.id == 0)
        {
            return true;
        }

        try
        {
            var protagonist = CharacterManage.protagonistAttribute;
            var source = protagonist is not null && protagonist.id == attribute.id
                ? ItemUseSource.Player
                : ItemUseSource.Npc;
            var originalCount = useCount;
            var context = new ItemUseContext(
                ItemCatalog.ResolveOrCreateKey(itemId),
                attribute.id,
                useCount,
                source);
            var before = GameEvents.RaiseItemUseBefore(context);

            if (context.RequestedCount > 0)
            {
                useCount = context.RequestedCount;
            }
            else
            {
                context.RequestedCount = originalCount;
                Plugin.Logger?.LogWarning(
                    $"[ItemUse] Mod supplied invalid requested count for {context.ItemKey}; " +
                    $"keeping original value {originalCount}.");
            }

            state = new ItemUseInvocationState(context);
            if (before.IsCancelled)
            {
                state.Result = new ItemUseResult(
                    Succeeded: false,
                    Cancelled: true,
                    ConsumedCount: 0,
                    Message: before.CancellationMessage,
                    FailureReason: ItemUseFailureReason.Cancelled);
                state.SkipOriginal = true;
                return false;
            }

            if (!ItemBehaviorRegistry.TryGet(context.ItemKey, out var behavior))
            {
                return true;
            }

            state.SkipOriginal = true;
            try
            {
                state.Result = behavior.Use(context) ?? new ItemUseResult(
                    Succeeded: false,
                    Cancelled: false,
                    ConsumedCount: 0,
                    Message: "The Mod item behavior returned no result.",
                    FailureReason: ItemUseFailureReason.ExecutionFailed);
            }
            catch (Exception exception)
            {
                state.Result = new ItemUseResult(
                    Succeeded: false,
                    Cancelled: false,
                    ConsumedCount: 0,
                    Message: exception.Message,
                    FailureReason: ItemUseFailureReason.ExecutionFailed);
                Plugin.Logger?.LogError(
                    $"[ItemUse] Behavior for {context.ItemKey} failed: {exception}");
            }

            return false;
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError(
                $"[ItemUse] Failed to prepare item use for raw ID {itemId}: {exception}");
            return true;
        }
    }

    internal static void PublishItemUseCompleted(
        CharacterAttribute attribute,
        int itemId,
        int useCount,
        ItemUseInvocationState? state,
        bool? operationSucceeded = null,
        ThingPackage? package = null)
    {
        if (state is null)
        {
            // Preserve the old observation path when the game call happened
            // before GameplayReady or outside the new dispatch path.
            PublishPlayerItemUsed(attribute, itemId, useCount);
            return;
        }

        if (state.Completed)
        {
            return;
        }

        state.Completed = true;

        var result = state.Result ?? new ItemUseResult(
            Succeeded: operationSucceeded ?? true,
            Cancelled: false,
            ConsumedCount: operationSucceeded == false ? 0 : Math.Max(useCount, 0),
            Message: operationSucceeded == false ? "The game item operation returned false." : null,
            FailureReason: operationSucceeded == false
                ? ItemUseFailureReason.Unknown
                : ItemUseFailureReason.None);
        PublishItemUseInventory(
            package ?? state.InventoryPackage,
            state,
            result.Succeeded);
        GameEvents.RaiseItemUseAfter(state.Context, result);

        if (result.Succeeded)
        {
            PublishPlayerItemUsed(attribute, itemId, result.ConsumedCount);
            return;
        }

        PublishPlayerItemAction(
            attribute?.id ?? 0,
            itemId,
            result.ConsumedCount,
            ItemActionKind.Use,
            succeeded: false,
            source: nameof(CharacterAttribute.UseItem));
    }

    internal static void PublishPlayerItemAction(
        ThingPackage package,
        Thing? thing,
        ItemActionKind action,
        int count,
        bool succeeded,
        string source)
    {
        if (package is null)
        {
            return;
        }

        PublishPlayerItemAction(
            package.cId,
            thing?.id ?? 0,
            count,
            action,
            succeeded,
            source);
    }

    internal static void PublishPlayerItemAction(
        CharacterAttribute? attribute,
        Thing? thing,
        ItemActionKind action,
        int count,
        bool succeeded,
        string source,
        int rawOperationType = -1)
    {
        PublishPlayerItemAction(
            attribute?.id ?? 0,
            thing?.id ?? 0,
            count,
            action,
            succeeded,
            source,
            rawOperationType);
    }

    internal static void PublishPlayerItemAction(
        int characterId,
        int itemId,
        int count,
        ItemActionKind action,
        bool succeeded,
        string source,
        int rawOperationType = -1)
    {
        if (!IsGameplayReady || characterId == 0)
        {
            return;
        }

        try
        {
            var protagonist = CharacterManage.protagonistAttribute;
            if (protagonist is null || protagonist.id != characterId)
            {
                return;
            }

            var itemKey = itemId > 0
                ? ItemCatalog.ResolveOrCreateKey(itemId)
                : (ItemKey?)null;
            GameEvents.RaisePlayerItemAction(
                characterId,
                itemKey,
                count,
                action,
                succeeded,
                source,
                rawOperationType);
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError(
                $"[GameContext] Failed to publish item action {action} from {source}: {exception}");
        }
    }

    internal static InventoryOperationState? BeginInventoryOperation(
        ThingPackage package,
        int itemId,
        int requestedCount,
        string source,
        string reason)
    {
        if (!IsGameplayReady || package is null || package.cId == 0 || itemId <= 0)
        {
            return null;
        }

        try
        {
            var protagonist = CharacterManage.protagonistAttribute;
            if (protagonist is null || protagonist.id != package.cId)
            {
                return null;
            }

            TryPeekPendingItemUse(package.cId, itemId, out var itemUseState);
            return new InventoryOperationState(
                package,
                itemId,
                CaptureInventoryCount(package, itemId),
                requestedCount,
                source,
                reason,
                itemUseState);
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError(
                $"[InventoryHook] Failed to capture count before {source}: {exception}");
            return null;
        }
    }

    internal static void PublishInventoryChanged(
        ThingPackage package,
        InventoryOperationState? state,
        bool operationSucceeded)
    {
        if (state is null || package is null || state.Published)
        {
            return;
        }

        state.Published = true;
        try
        {
            var totalCount = CaptureInventoryCount(package, state.ItemId);
            var delta = totalCount - state.BeforeCount;
            var kind = delta > 0
                ? InventoryChangeKind.Added
                : delta < 0
                    ? InventoryChangeKind.Removed
                    : InventoryChangeKind.Unknown;
            var succeeded = operationSucceeded || delta != 0;

            if (delta == 0)
            {
                return;
            }

            state.ItemUseState?.MarkInventoryPublished();

            GameEvents.RaiseInventoryChanged(
                package.cId,
                ItemCatalog.ResolveOrCreateKey(state.ItemId),
                delta,
                totalCount,
                kind,
                succeeded,
                state.Source,
                state.Reason);
            TradeSignals.PublishItem(
                package.cId,
                ItemCatalog.ResolveOrCreateKey(state.ItemId),
                delta,
                state.Source,
                state.Reason);
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError(
                $"[InventoryHook] Failed to publish {state.Source}: {exception}");
        }
    }

    internal static void TrackItemUseInventory(
        ThingPackage package,
        int itemId,
        ItemUseInvocationState state,
        string source)
    {
        state.InventoryPackage = package;
        state.InventoryItemId = itemId;
        state.InventoryBeforeCount = CaptureInventoryCount(package, itemId);
        state.InventorySource = source;
        state.TracksInventory = true;
    }

    internal static void PublishItemUseInventory(
        ThingPackage? package,
        ItemUseInvocationState state,
        bool operationSucceeded)
    {
        if (package is null || !state.TracksInventory || state.InventoryPublished)
        {
            return;
        }

        var totalCount = CaptureInventoryCount(package, state.InventoryItemId);
        var delta = totalCount - state.InventoryBeforeCount;
        if (delta == 0)
        {
            return;
        }

        state.MarkInventoryPublished();
        GameEvents.RaiseInventoryChanged(
            package.cId,
            state.Context.ItemKey,
            delta,
            totalCount,
            delta > 0 ? InventoryChangeKind.Added : InventoryChangeKind.Removed,
            operationSucceeded || delta != 0,
            state.InventorySource,
            "UseThing");
        TradeSignals.PublishItem(
            package.cId,
            state.Context.ItemKey,
            delta,
            state.InventorySource,
            "UseThing");
    }

    internal static InventoryMoveState? BeginInventoryMove(
        ThingPackage package,
        Thing thing,
        int requestedCount)
    {
        if (!IsGameplayReady || package is null || thing is null || package.cId == 0)
        {
            return null;
        }

        try
        {
            var protagonist = CharacterManage.protagonistAttribute;
            if (protagonist is null || protagonist.id != package.cId || thing.id <= 0)
            {
                return null;
            }

            return new InventoryMoveState(
                ItemCatalog.ResolveOrCreateKey(thing.id),
                requestedCount > 0 ? Math.Min(requestedCount, thing.count) : thing.count,
                CaptureInventoryLocation(thing));
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError($"[InventoryHook] Failed to capture move: {exception}");
            return null;
        }
    }

    internal static void PublishInventoryMoved(
        ThingPackage package,
        Thing thing,
        InventoryMoveState? state,
        bool succeeded,
        string source)
    {
        if (!succeeded || package is null || thing is null || state is null)
        {
            return;
        }

        try
        {
            var to = CaptureInventoryLocation(thing);
            if (state.From == to)
            {
                return;
            }

            GameEvents.RaiseInventoryMoved(
                package.cId,
                state.ItemKey,
                state.Count,
                state.From,
                to,
                true,
                source);
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError($"[InventoryHook] Failed to publish move: {exception}");
        }
    }

    /// <summary>
    /// Counts real Thing instances rather than ThingPackage.GetThingCount.
    /// The latter also reports pseudo item IDs used for attributes such as
    /// health, so it cannot distinguish an inventory change from an effect.
    /// </summary>
    private static int CaptureInventoryCount(ThingPackage package, int itemId)
    {
        if (package is null || itemId <= 0)
        {
            return 0;
        }

        var things = package.thingList;
        if (things is null)
        {
            return 0;
        }

        var total = 0;
        for (var index = 0; index < things.Count; index++)
        {
            var thing = things[index];
            if (thing is not null && thing.id == itemId)
            {
                total += Math.Max(thing.count, 0);
            }
        }

        return total;
    }

    internal static string GetMapName(int mapId)
    {
        if (mapId == 0)
        {
            return string.Empty;
        }

        try
        {
            return MapManage.GetMapName(mapId) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    internal static void Reset()
    {
        IsGameplayReady = false;
        _lastPublishedTime = null;
        _lastPublishedMapId = null;
        _lastPublishedPlayer = null;
        ObservedPlayerHookSources.Clear();
        lock (PendingItemUseSync)
        {
            PendingItemUses.Clear();
        }

        GameContext.Reset();
    }

    private static void StorePendingItemUse(ItemUseInvocationState state)
    {
        var key = GetPendingItemUseKey(state.Context.CharacterId, state.Context.ItemKey);
        lock (PendingItemUseSync)
        {
            if (!PendingItemUses.TryGetValue(key, out var states))
            {
                states = new Stack<ItemUseInvocationState>();
                PendingItemUses[key] = states;
            }

            states.Push(state);
        }
    }

    private static bool TryPeekPendingItemUse(
        int characterId,
        int itemId,
        out ItemUseInvocationState state)
    {
        var key = GetPendingItemUseKey(characterId, ItemCatalog.ResolveOrCreateKey(itemId));
        lock (PendingItemUseSync)
        {
            if (PendingItemUses.TryGetValue(key, out var states) && states.Count > 0)
            {
                state = states.Peek();
                return true;
            }
        }

        state = null!;
        return false;
    }

    private static bool TryTakePendingItemUse(
        int characterId,
        int itemId,
        out ItemUseInvocationState state)
    {
        var key = GetPendingItemUseKey(characterId, ItemCatalog.ResolveOrCreateKey(itemId));
        lock (PendingItemUseSync)
        {
            if (PendingItemUses.TryGetValue(key, out var states) && states.Count > 0)
            {
                state = states.Pop();
                if (states.Count == 0)
                {
                    PendingItemUses.Remove(key);
                }

                return true;
            }
        }

        state = null!;
        return false;
    }

    private static string GetPendingItemUseKey(int characterId, ItemKey itemKey) =>
        characterId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
        itemKey.ToString();

    private static PlayerSnapshot? CapturePlayer()
    {
        var player = CharacterManage.protagonistAttribute;
        if (player is null)
        {
            return null;
        }

        return new PlayerSnapshot(
            player.id,
            player.health,
            player.healthMax,
            player.mentality,
            player.mentalityMax,
            player.satiety,
            player.satietyMax,
            player.energy,
            player.energyMax,
            player.focus,
            player.focusMax,
            player.money,
            player.discipline);
    }

    internal static IReadOnlyList<CharacterRelationshipSnapshot> CaptureRelationships()
    {
        var protagonist = CharacterManage.protagonistAttribute;
        var interactive = CharacterManage.interactiveCharacterAttribute;
        var snapshots = new List<CharacterRelationshipSnapshot>();

        try
        {
            var attributes = CharacterManage.characterAttributeDict;
            if (attributes is not null)
            {
                foreach (var attribute in attributes.Values)
                {
                    if (attribute is null)
                    {
                        continue;
                    }

                    snapshots.Add(CaptureRelationship(
                        attribute,
                        protagonist,
                        interactive));
                }
            }
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogDebug(
                $"[RelationshipApi] Character dictionary capture failed: {exception.Message}");
        }

        AddIfMissing(snapshots, protagonist, protagonist, interactive);
        AddIfMissing(snapshots, interactive, protagonist, interactive);
        return snapshots;
    }

    internal static CharacterRelationshipSnapshot? CaptureInteractiveRelationship()
    {
        var interactive = CharacterManage.interactiveCharacterAttribute;
        return interactive is null
            ? null
            : CaptureRelationship(
                interactive,
                CharacterManage.protagonistAttribute,
                interactive);
    }

    internal static CharacterRelationshipSnapshot? CaptureProtagonistRelationship()
    {
        var protagonist = CharacterManage.protagonistAttribute;
        return protagonist is null
            ? null
            : CaptureRelationship(
                protagonist,
                protagonist,
                CharacterManage.interactiveCharacterAttribute);
    }

    private static void AddIfMissing(
        ICollection<CharacterRelationshipSnapshot> snapshots,
        CharacterAttribute? candidate,
        CharacterAttribute? protagonist,
        CharacterAttribute? interactive)
    {
        if (candidate is null || snapshots.Any(snapshot => snapshot.CharacterId == candidate.id))
        {
            return;
        }

        snapshots.Add(CaptureRelationship(candidate, protagonist, interactive));
    }

    private static CharacterRelationshipSnapshot CaptureRelationship(
        CharacterAttribute attribute,
        CharacterAttribute? protagonist,
        CharacterAttribute? interactive)
    {
        string? name = null;
        try
        {
            name = attribute.GetFullName();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = attribute.GetName();
            }
        }
        catch
        {
            // Character names are optional for the relationship API.
        }

        var isProtagonist = protagonist is not null &&
                            protagonist.id == attribute.id;
        var isInteractive = interactive is not null &&
                            interactive.id == attribute.id;

        return new CharacterRelationshipSnapshot(
            attribute.id,
            name,
            attribute.friend,
            attribute.affection,
            attribute.friendLowerLimit,
            attribute.friendUpperLimit,
            attribute.IsFriendToAffection(),
            isProtagonist,
            isInteractive);
    }

    internal sealed class ItemUseInvocationState
    {
        public ItemUseInvocationState(ItemUseContext context) => Context = context;

        public ItemUseContext Context { get; }

        public ItemUseResult? Result { get; set; }

        public bool SkipOriginal { get; set; }

        public bool Completed { get; set; }

        public ThingPackage? InventoryPackage { get; set; }

        public int InventoryItemId { get; set; }

        public int InventoryBeforeCount { get; set; }

        public string InventorySource { get; set; } = nameof(ThingPackage.UseThing);

        public bool TracksInventory { get; set; }

        public bool InventoryPublished { get; private set; }

        public void MarkInventoryPublished() => InventoryPublished = true;
    }

    internal sealed class InventoryOperationState
    {
        public InventoryOperationState(
            ThingPackage package,
            int itemId,
            int beforeCount,
            int requestedCount,
            string source,
            string reason,
            ItemUseInvocationState? itemUseState = null)
        {
            Package = package;
            ItemId = itemId;
            BeforeCount = beforeCount;
            RequestedCount = requestedCount;
            Source = source;
            Reason = reason;
            ItemUseState = itemUseState;
        }

        public ThingPackage Package { get; }

        public int ItemId { get; }

        public int BeforeCount { get; }

        public int RequestedCount { get; }

        public string Source { get; }

        public string Reason { get; }

        public bool Published { get; set; }

        public ItemUseInvocationState? ItemUseState { get; }
    }

    internal sealed class InventoryMoveState
    {
        public InventoryMoveState(ItemKey itemKey, int count, InventoryLocation from)
        {
            ItemKey = itemKey;
            Count = Math.Max(count, 1);
            From = from;
        }

        public ItemKey ItemKey { get; }

        public int Count { get; }

        public InventoryLocation From { get; }
    }
}
