namespace BackToTheDawn.ModAPI;

/// <summary>
/// Read-only access to the latest game state exposed by the loader.
/// </summary>
public static class GameContext
{
    internal static Func<GameStateSnapshot?>? SnapshotProvider { get; set; }
    internal static Func<InventorySnapshot?>? InventoryProvider { get; set; }
    internal static Func<ItemKey, int, InventoryOperationResult>? InventoryAddProvider { get; set; }
    internal static Func<ItemKey, int, InventoryOperationResult>? InventoryRemoveProvider { get; set; }
    internal static Func<ItemKey, InventoryLocation, InventoryLocation, int, InventoryOperationResult>?
        InventoryMoveProvider { get; set; }
    internal static Func<IReadOnlyList<CharacterRelationshipSnapshot>>?
        RelationshipProvider { get; set; }
    internal static Func<CharacterRelationshipSnapshot?>?
        InteractiveRelationshipProvider { get; set; }
    internal static Func<CharacterRelationshipSnapshot?>?
        ProtagonistRelationshipProvider { get; set; }

    public static GameStateSnapshot? Current
    {
        get
        {
            try
            {
                return SnapshotProvider?.Invoke();
            }
            catch
            {
                return null;
            }
        }
    }

    public static bool IsGameplayReady => Current?.IsGameplayReady ?? false;
    public static int ArchiveId => Current?.ArchiveId ?? 0;
    public static int MapId => Current?.MapId ?? 0;
    public static GameTimeSnapshot? Time => Current?.Time;
    public static PlayerSnapshot? Player => Current?.Player;

    public static IReadOnlyList<CharacterRelationshipSnapshot> Relationships
    {
        get
        {
            try
            {
                return RelationshipProvider?.Invoke() ??
                    Array.Empty<CharacterRelationshipSnapshot>();
            }
            catch
            {
                return Array.Empty<CharacterRelationshipSnapshot>();
            }
        }
    }

    public static CharacterRelationshipSnapshot? InteractiveRelationship
    {
        get
        {
            try
            {
                return InteractiveRelationshipProvider?.Invoke();
            }
            catch
            {
                return null;
            }
        }
    }

    public static CharacterRelationshipSnapshot? ProtagonistRelationship
    {
        get
        {
            try
            {
                return ProtagonistRelationshipProvider?.Invoke();
            }
            catch
            {
                return null;
            }
        }
    }

    public static InventorySnapshot? Inventory
    {
        get
        {
            try
            {
                return InventoryProvider?.Invoke();
            }
            catch
            {
                return null;
            }
        }
    }

    public static bool TryGetSnapshot(out GameStateSnapshot? snapshot)
    {
        snapshot = Current;
        return snapshot is not null;
    }

    public static bool TryGetInventorySnapshot(out InventorySnapshot? snapshot)
    {
        snapshot = Inventory;
        return snapshot is not null;
    }

    internal static InventoryOperationResult TryAddInventory(ItemKey itemKey, int count) =>
        InventoryAddProvider?.Invoke(itemKey, count) ??
        new InventoryOperationResult(
            itemKey,
            count,
            0,
            0,
            InventoryOperationStatus.NotReady,
            "The inventory service is not available.");

    internal static InventoryOperationResult TryRemoveInventory(ItemKey itemKey, int count) =>
        InventoryRemoveProvider?.Invoke(itemKey, count) ??
        new InventoryOperationResult(
            itemKey,
            count,
            0,
            0,
            InventoryOperationStatus.NotReady,
            "The inventory service is not available.");

    internal static InventoryOperationResult TryMoveInventory(
        ItemKey itemKey,
        InventoryLocation from,
        InventoryLocation to,
        int count) =>
        InventoryMoveProvider?.Invoke(itemKey, from, to, count) ??
        new InventoryOperationResult(
            itemKey,
            count,
            0,
            0,
            InventoryOperationStatus.NotReady,
            "The inventory service is not available.");

    internal static void Reset()
    {
        SnapshotProvider = null;
        InventoryProvider = null;
        InventoryAddProvider = null;
        InventoryRemoveProvider = null;
        InventoryMoveProvider = null;
        RelationshipProvider = null;
        InteractiveRelationshipProvider = null;
        ProtagonistRelationshipProvider = null;
    }
}

public sealed record GameStateSnapshot(
    bool IsGameplayReady,
    int ArchiveId,
    int MapId,
    string MapName,
    GameTimeSnapshot Time,
    PlayerSnapshot? Player);

public sealed record GameTimeSnapshot(
    int Day,
    int WakeDay,
    int Hour,
    int Minute,
    int TotalMinutes);

public sealed record PlayerSnapshot(
    int CharacterId,
    int Health,
    int MaxHealth,
    int Mentality,
    int MaxMentality,
    int Satiety,
    int MaxSatiety,
    int Energy,
    int MaxEnergy,
    int Focus,
    int MaxFocus,
    int Money,
    int Discipline = 0);
