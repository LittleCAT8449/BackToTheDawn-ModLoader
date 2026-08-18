namespace BackToTheDawn.ModAPI;

/// <summary>
/// Stable description of a game inventory location. Container is the game
/// container name (for example <c>Pocket</c> or <c>Equipment</c>), while the
/// numeric place values are only local slot coordinates.
/// </summary>
public sealed record InventoryLocation(
    string Container,
    int PlaceId = 0,
    int SubPlaceId = 0);

/// <summary>
/// One real Thing stack represented without exposing the game's Thing type.
/// </summary>
public sealed record InventoryStack(
    ItemKey ItemKey,
    int Count,
    InventoryLocation Location,
    bool IsFullGrid);

/// <summary>
/// Immutable read-only inventory view for one character.
/// </summary>
public sealed class InventorySnapshot
{
    public InventorySnapshot(int characterId, IEnumerable<InventoryStack> items)
    {
        CharacterId = characterId;
        Items = items?.ToArray() ?? Array.Empty<InventoryStack>();
    }

    public int CharacterId { get; }

    public IReadOnlyList<InventoryStack> Items { get; }

    public int GetCount(ItemKey itemKey) =>
        Items.Where(item => item.ItemKey == itemKey).Sum(item => Math.Max(item.Count, 0));

    public bool TryGetFirst(ItemKey itemKey, out InventoryStack stack)
    {
        stack = Items.FirstOrDefault(item => item.ItemKey == itemKey)!;
        return stack is not null;
    }
}

/// <summary>
/// Raised after a successful item move between two containers. Moving an
/// item does not change its total count, so this is separate from
/// <see cref="InventoryChangedEvent"/>.
/// </summary>
public sealed record InventoryMovedEvent(
    int CharacterId,
    ItemKey ItemKey,
    int Count,
    InventoryLocation From,
    InventoryLocation To,
    bool Succeeded,
    string Source) : IGameEvent;
