namespace BackToTheDawn.ModAPI;

public enum InventoryOperationStatus
{
    Succeeded = 0,
    Partial = 1,
    NotReady = 2,
    InvalidCount = 3,
    ItemNotFound = 4,
    NotEnoughItems = 5,
    InvalidLocation = 6,
    NoSpace = 7,
    Failed = 8,
}

/// <summary>
/// Result returned by a controlled inventory mutation. ChangedCount is the
/// actual number changed, while RemainingCount is the post-operation total.
/// </summary>
public sealed record InventoryOperationResult(
    ItemKey ItemKey,
    int RequestedCount,
    int ChangedCount,
    int RemainingCount,
    InventoryOperationStatus Status,
    string Message)
{
    public bool Succeeded =>
        Status is InventoryOperationStatus.Succeeded or InventoryOperationStatus.Partial;
}

/// <summary>
/// Controlled player-inventory service. Calls must be made on the Unity game
/// thread while GameplayReady is true; all results are based on real Thing
/// snapshots and do not expose game assembly types.
/// </summary>
public sealed class InventoryApi
{
    internal InventoryApi()
    {
    }

    public InventorySnapshot? Current => GameContext.Inventory;

    public bool TryGetSnapshot(out InventorySnapshot? snapshot) =>
        GameContext.TryGetInventorySnapshot(out snapshot);

    public InventoryOperationResult TryAdd(ItemKey itemKey, int count) =>
        GameContext.TryAddInventory(itemKey, count);

    public InventoryOperationResult TryRemove(ItemKey itemKey, int count) =>
        GameContext.TryRemoveInventory(itemKey, count);

    public InventoryOperationResult TryMove(
        ItemKey itemKey,
        InventoryLocation from,
        InventoryLocation to,
        int count = 1) =>
        GameContext.TryMoveInventory(itemKey, from, to, count);
}
