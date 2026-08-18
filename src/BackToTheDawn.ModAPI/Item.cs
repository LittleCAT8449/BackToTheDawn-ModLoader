namespace BackToTheDawn.ModAPI;

public enum ItemRegistrationStatus
{
    Registered = 0,
    AlreadyRegistered = 1,
    KeyAlreadyExists = 2,
    GameNamespaceReserved = 3,
    CallbackFailed = 4,
}

/// <summary>
/// Explains the outcome of registering a Mod-owned item.
/// </summary>
public sealed record ItemRegistrationResult(
    Item Item,
    ItemRegistrationStatus Status,
    string Message)
{
    public bool Succeeded => Status == ItemRegistrationStatus.Registered;
}

public enum ItemUnregistrationStatus
{
    Unregistered = 0,
    NotRegistered = 1,
    RuntimeBound = 2,
    CallbackFailed = 3,
}

/// <summary>
/// Explains the outcome of removing a Mod-owned item from the virtual catalog.
/// Runtime-bound items cannot be removed safely while the game process is
/// alive, because the game may already hold the assigned numeric ID.
/// </summary>
public sealed record ItemUnregistrationResult(
    Item Item,
    ItemUnregistrationStatus Status,
    string Message)
{
    /// <summary>
    /// True when the catalog entry was removed. CallbackFailed is still a
    /// successful removal; it only means the optional lifecycle callback threw.
    /// </summary>
    public bool Succeeded => Status is
        ItemUnregistrationStatus.Unregistered or
        ItemUnregistrationStatus.CallbackFailed;
}

/// <summary>
/// Relative files owned by a Mod item. Paths are resolved against the Mod's
/// <see cref="ModResources"/> instance and never against the game directory.
/// </summary>
public sealed class ItemResources
{
    public ItemResources(
        string? iconPath = null,
        string? namePath = null,
        string? descriptionPath = null)
    {
        IconPath = Normalize(iconPath, nameof(iconPath));
        NamePath = Normalize(namePath, nameof(namePath));
        DescriptionPath = Normalize(descriptionPath, nameof(descriptionPath));
    }

    public static ItemResources Empty { get; } = new();

    public string IconPath { get; }

    public string NamePath { get; }

    public string DescriptionPath { get; }

    public bool HasIcon => IconPath.Length > 0;

    public bool HasName => NamePath.Length > 0;

    public bool HasDescription => DescriptionPath.Length > 0;

    public string? ResolveIconPath(ModResources resources) => Resolve(resources, IconPath);

    public string? ResolveNamePath(ModResources resources) => Resolve(resources, NamePath);

    public string? ResolveDescriptionPath(ModResources resources) =>
        Resolve(resources, DescriptionPath);

    private static string Normalize(string? path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = path.Trim().Replace('\\', '/');
        if (Path.IsPathRooted(normalized) ||
            normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Any(part => part is "." or ".."))
        {
            throw new ArgumentException(
                "Item resource paths must stay inside the Mod resource directory.",
                parameterName);
        }

        return normalized;
    }

    private static string? Resolve(ModResources resources, string path)
    {
        ArgumentNullException.ThrowIfNull(resources);
        return path.Length == 0 ? null : resources.GetPath(path);
    }
}

/// <summary>
/// Base class for Mod-owned items. It contains the common catalog metadata and
/// exposes the single registration entry point used by Mod authors.
/// </summary>
public abstract class Item
{
    private int _registered;

    protected Item(
        ItemKey key,
        string displayName,
        string itemType = "custom",
        string itemType2 = "",
        string backgroundDescription = "",
        bool isEquipment = false,
        bool isWeapon = false,
        int maxStack = 99,
        int maxUse = 0,
        string parameterA = "",
        string parameterB = "",
        bool occupiesFullGrid = false,
        ItemResources? resources = null)
    {
        Key = key;
        DisplayName = displayName ?? string.Empty;
        ItemType = itemType ?? string.Empty;
        ItemType2 = itemType2 ?? string.Empty;
        BackgroundDescription = backgroundDescription ?? string.Empty;
        IsEquipment = isEquipment;
        IsWeapon = isWeapon;
        MaxStack = maxStack;
        MaxUse = maxUse;
        ParameterA = parameterA ?? string.Empty;
        ParameterB = parameterB ?? string.Empty;
        OccupiesFullGrid = occupiesFullGrid;
        Resources = resources ?? ItemResources.Empty;
    }

    public ItemKey Key { get; }

    public string DisplayName { get; }

    public string ItemType { get; }

    public string ItemType2 { get; }

    public string BackgroundDescription { get; }

    public bool IsEquipment { get; }

    public bool IsWeapon { get; }

    public int MaxStack { get; }

    public int MaxUse { get; }

    public string ParameterA { get; }

    public string ParameterB { get; }

    /// <summary>
    /// True when the item consumes one full inventory grid; false means the
    /// game's small/half-grid variant. The loader maps this to c_item.volume.
    /// </summary>
    public bool OccupiesFullGrid { get; }

    public ItemResources Resources { get; }

    public bool IsRegistered => Volatile.Read(ref _registered) == 1;

    /// <summary>
    /// Gets the immutable catalog representation used by the read API.
    /// </summary>
    public ItemDefinition Definition => new(
        Key,
        DisplayName,
        ItemType,
        ItemType2,
        BackgroundDescription,
        IsEquipment,
        IsWeapon,
        MaxStack,
        MaxUse,
        ParameterA,
        ParameterB,
        OccupiesFullGrid,
        Resources);

    /// <summary>
    /// Registers this item in the Mod-owned virtual catalog and returns the
    /// detailed outcome. Use <see cref="TryRegister"/> when only a boolean is
    /// needed.
    /// </summary>
    public ItemRegistrationResult Register() => ItemRegistry.Register(this);

    /// <summary>
    /// Compatibility convenience for callers that only need success/failure.
    /// </summary>
    public bool TryRegister() => Register().Succeeded;

    /// <summary>
    /// Removes this Mod-owned item from the virtual catalog.
    /// </summary>
    public bool Unregister() => UnregisterDetailed().Succeeded;

    /// <summary>
    /// Removes this Mod-owned item and returns a structured outcome. If the
    /// item has already been injected into the live game catalog, the result
    /// is <see cref="ItemUnregistrationStatus.RuntimeBound"/> and the item is
    /// retained until the process exits.
    /// </summary>
    public ItemUnregistrationResult UnregisterDetailed() =>
        ItemRegistry.UnregisterDetailed(this);

    /// <summary>
    /// Called after the item has been added to the virtual catalog.
    /// </summary>
    protected virtual void OnRegistered()
    {
    }

    /// <summary>
    /// Called after the item has been removed from the virtual catalog.
    /// </summary>
    protected virtual void OnUnregistered()
    {
    }

    internal void MarkRegistered() => Interlocked.Exchange(ref _registered, 1);

    internal void MarkUnregistered() => Interlocked.Exchange(ref _registered, 0);

    internal void InvokeOnRegistered() => OnRegistered();

    internal void InvokeOnUnregistered() => OnUnregistered();
}

/// <summary>
/// Registration entry point for Mod-owned <see cref="Item"/> instances.
/// Registration only affects the public virtual catalog; it does not assign a
/// game numeric ID or mutate c_item, saves, or player inventory.
/// </summary>
public static class ItemRegistry
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, Item> RegisteredItems =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Internal Loader bridge used to process items registered after the first
    /// runtime injection pass. It is not exposed to Mod authors.
    /// </summary>
    internal static event Action<Item>? ItemRegistered;

    public static IReadOnlyList<Item> All
    {
        get
        {
            lock (SyncRoot)
            {
                return RegisteredItems.Values.ToArray();
            }
        }
    }

    public static ItemRegistrationResult Register(Item item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.IsRegistered)
        {
            return new ItemRegistrationResult(
                item,
                ItemRegistrationStatus.AlreadyRegistered,
                $"Item '{item.Key}' is already registered by this instance.");
        }

        if (item.Key.Namespace.Equals("backtothedawn", StringComparison.OrdinalIgnoreCase))
        {
            return new ItemRegistrationResult(
                item,
                ItemRegistrationStatus.GameNamespaceReserved,
                "The 'backtothedawn' namespace is reserved for game items.");
        }

        lock (SyncRoot)
        {
            var key = item.Key.ToString();
            if (RegisteredItems.ContainsKey(key) || ItemCatalog.TryGet(item.Key, out _))
            {
                return new ItemRegistrationResult(
                    item,
                    ItemRegistrationStatus.KeyAlreadyExists,
                    $"Item key '{key}' is already present in the catalog.");
            }

            if (!ItemCatalog.TryRegister(item.Definition))
            {
                return new ItemRegistrationResult(
                    item,
                    ItemRegistrationStatus.KeyAlreadyExists,
                    $"Item key '{key}' could not be added to the catalog.");
            }

            RegisteredItems[key] = item;
            item.MarkRegistered();
        }

        try
        {
            item.InvokeOnRegistered();
            NotifyItemRegistered(item);
            return new ItemRegistrationResult(
                item,
                ItemRegistrationStatus.Registered,
                $"Item '{item.Key}' registered.");
        }
        catch (Exception exception)
        {
            lock (SyncRoot)
            {
                RegisteredItems.Remove(item.Key.ToString());
                ItemCatalog.TryUnregister(item.Key);
                item.MarkUnregistered();
            }

            return new ItemRegistrationResult(
                item,
                ItemRegistrationStatus.CallbackFailed,
                $"Item '{item.Key}' registration callback failed: {exception.Message}");
        }
    }

    public static bool TryRegister(Item item) => Register(item).Succeeded;

    private static void NotifyItemRegistered(Item item)
    {
        var handlers = ItemRegistered;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Action<Item>>())
        {
            try
            {
                handler(item);
            }
            catch
            {
                // Runtime injection is best effort and must not make a
                // successful virtual registration fail.
            }
        }
    }

    /// <summary>
    /// Compatibility convenience for callers that only need success/failure.
    /// </summary>
    public static bool Unregister(Item item) => UnregisterDetailed(item).Succeeded;

    /// <summary>
    /// Removes an item from the virtual catalog and explains why removal was
    /// rejected when the item is already bound to the live game catalog.
    /// </summary>
    public static ItemUnregistrationResult UnregisterDetailed(Item item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.IsRegistered)
        {
            return new ItemUnregistrationResult(
                item,
                ItemUnregistrationStatus.NotRegistered,
                $"Item '{item.Key}' is not registered by this instance.");
        }

        lock (SyncRoot)
        {
            var key = item.Key.ToString();
            if (!RegisteredItems.TryGetValue(key, out var registered) ||
                !ReferenceEquals(registered, item))
            {
                return new ItemUnregistrationResult(
                    item,
                    ItemUnregistrationStatus.NotRegistered,
                    $"Item '{item.Key}' is not registered by this instance.");
            }

            if (!ItemCatalog.TryUnregister(item.Key))
            {
                if (ItemCatalog.TryGetId(item.Key, out var runtimeId))
                {
                    return new ItemUnregistrationResult(
                        item,
                        ItemUnregistrationStatus.RuntimeBound,
                        $"Item '{item.Key}' is bound to runtime ID {runtimeId}; " +
                        "it remains available until the game process exits.");
                }

                return new ItemUnregistrationResult(
                    item,
                    ItemUnregistrationStatus.NotRegistered,
                    $"Item '{item.Key}' is no longer present in the virtual catalog.");
            }

            RegisteredItems.Remove(key);
            item.MarkUnregistered();
        }

        try
        {
            item.InvokeOnUnregistered();
        }
        catch (Exception exception)
        {
            // Unregistration has already completed; lifecycle callbacks are
            // best effort, but expose the failure to detailed callers.
            return new ItemUnregistrationResult(
                item,
                ItemUnregistrationStatus.CallbackFailed,
                $"Item '{item.Key}' was removed, but its unregistration callback " +
                $"failed: {exception.Message}");
        }

        return new ItemUnregistrationResult(
            item,
            ItemUnregistrationStatus.Unregistered,
            $"Item '{item.Key}' unregistered.");
    }

    internal static void Reset()
    {
        lock (SyncRoot)
        {
            foreach (var item in RegisteredItems.Values)
            {
                item.MarkUnregistered();
            }

            RegisteredItems.Clear();
        }
    }
}
