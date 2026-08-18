namespace BackToTheDawn.ModAPI;

/// <summary>
/// Describes the observable direction of a player inventory change.
/// </summary>
public enum InventoryChangeKind
{
    Unknown = 0,
    Added = 1,
    Removed = 2,
}

/// <summary>
/// Raised after a player inventory operation returns. Delta is calculated
/// from the real before/after item count, so failed or partially fulfilled
/// game operations are visible without exposing ThingPackage or Thing.
/// </summary>
public sealed record InventoryChangedEvent(
    int CharacterId,
    ItemKey ItemKey,
    int Delta,
    int TotalCount,
    InventoryChangeKind Change,
    bool Succeeded,
    string Source,
    string Reason) : IGameEvent;
