namespace BackToTheDawn.ModAPI;

/// <summary>
/// Identifies who initiated an item use without exposing the game's character
/// implementation to Mod code.
/// </summary>
public enum ItemUseSource
{
    Player = 0,
    Npc = 1,
}

public enum ItemTargetKind
{
    Unknown = 0,
    Self = 1,
    Character = 2,
    Item = 3,
    Location = 4,
}

/// <summary>
/// Optional target information supplied by a caller or adjusted by a
/// cancellable before-use subscriber.
/// </summary>
public sealed record ItemTarget(
    ItemTargetKind Kind,
    int? CharacterId = null,
    string? Identifier = null);

/// <summary>
/// Mutable, game-assembly-free context for one item use attempt. Before-use
/// subscribers may adjust RequestedCount or Target, or cancel the operation
/// through <see cref="ItemUseBeforeEvent.Cancel"/>.
/// </summary>
public sealed class ItemUseContext
{
    public ItemUseContext(
        ItemKey itemKey,
        int characterId,
        int requestedCount,
        ItemUseSource source,
        ItemTarget? target = null)
    {
        ItemKey = itemKey;
        CharacterId = characterId;
        RequestedCount = requestedCount;
        Source = source;
        Target = target;
    }

    public ItemKey ItemKey { get; }

    public int CharacterId { get; }

    public int RequestedCount { get; set; }

    public ItemUseSource Source { get; }

    public ItemTarget? Target { get; set; }
}

public enum ItemUseFailureReason
{
    None = 0,
    Cancelled = 1,
    InvalidTarget = 2,
    NotEnoughItems = 3,
    ExecutionFailed = 4,
    Unknown = 5,
}

/// <summary>
/// Result reported after an item use attempt. For built-in game items the
/// Loader reports the requested count as consumed when UseItem returns; Mod
/// behaviors can provide their exact consumed count.
/// </summary>
public sealed record ItemUseResult(
    bool Succeeded,
    bool Cancelled,
    int ConsumedCount,
    string? Message,
    ItemUseFailureReason FailureReason = ItemUseFailureReason.None,
    int? RemainingCount = null);

/// <summary>
/// Cancellable event raised immediately before the game or a registered Mod
/// behavior handles an item use.
/// </summary>
public sealed class ItemUseBeforeEvent : IGameEvent
{
    internal ItemUseBeforeEvent(ItemUseContext context) => Context = context;

    public ItemUseContext Context { get; }

    public bool IsCancelled { get; private set; }

    public string? CancellationMessage { get; private set; }

    public void Cancel(string? message = null)
    {
        IsCancelled = true;
        CancellationMessage = string.IsNullOrWhiteSpace(message)
            ? "Item use was cancelled by a Mod."
            : message.Trim();
    }
}

/// <summary>
/// Event raised after a built-in item or a registered Mod behavior finishes.
/// </summary>
public sealed record ItemUseAfterEvent(
    ItemUseContext Context,
    ItemUseResult Result) : IGameEvent;

public interface IItemBehavior
{
    ItemUseResult Use(ItemUseContext context);
}

public enum ItemBehaviorRegistrationStatus
{
    Registered = 0,
    AlreadyRegistered = 1,
    GameNamespaceReserved = 2,
    ItemNotFound = 3,
}

public sealed record ItemBehaviorRegistrationResult(
    ItemKey ItemKey,
    ItemBehaviorRegistrationStatus Status,
    string Message)
{
    public bool Succeeded => Status == ItemBehaviorRegistrationStatus.Registered;
}

/// <summary>
/// Associates a namespaced item with a game-assembly-free use behavior. A
/// registered behavior is executed by the Loader instead of the game's
/// template UseItem implementation.
/// </summary>
public static class ItemBehaviorRegistry
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, IItemBehavior> Behaviors =
        new(StringComparer.OrdinalIgnoreCase);

    public static ItemBehaviorRegistrationResult Register(
        ItemKey itemKey,
        IItemBehavior behavior)
    {
        ArgumentNullException.ThrowIfNull(behavior);

        if (itemKey.Namespace.Equals("backtothedawn", StringComparison.OrdinalIgnoreCase))
        {
            return new ItemBehaviorRegistrationResult(
                itemKey,
                ItemBehaviorRegistrationStatus.GameNamespaceReserved,
                "The 'backtothedawn' namespace is reserved for game items.");
        }

        if (!ItemCatalog.TryGet(itemKey, out _))
        {
            return new ItemBehaviorRegistrationResult(
                itemKey,
                ItemBehaviorRegistrationStatus.ItemNotFound,
                $"Item '{itemKey}' is not present in the virtual catalog.");
        }

        lock (SyncRoot)
        {
            if (Behaviors.ContainsKey(itemKey.ToString()))
            {
                return new ItemBehaviorRegistrationResult(
                    itemKey,
                    ItemBehaviorRegistrationStatus.AlreadyRegistered,
                    $"A behavior for '{itemKey}' is already registered.");
            }

            Behaviors[itemKey.ToString()] = behavior;
        }

        return new ItemBehaviorRegistrationResult(
            itemKey,
            ItemBehaviorRegistrationStatus.Registered,
            $"Behavior for '{itemKey}' registered.");
    }

    public static bool TryGet(ItemKey itemKey, out IItemBehavior behavior)
    {
        lock (SyncRoot)
        {
            return Behaviors.TryGetValue(itemKey.ToString(), out behavior!);
        }
    }

    public static bool Unregister(ItemKey itemKey)
    {
        lock (SyncRoot)
        {
            return Behaviors.Remove(itemKey.ToString());
        }
    }

    internal static void Reset()
    {
        lock (SyncRoot)
        {
            Behaviors.Clear();
        }
    }
}
