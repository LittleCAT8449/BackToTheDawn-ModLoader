namespace BackToTheDawn.ModAPI;

/// <summary>
/// Base class for Mod-owned items. It contains the common catalog metadata and
/// exposes the single registration entry point used by Mod authors.
/// </summary>
public abstract class Item
{
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
        string parameterB = "")
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
        ParameterB);

    /// <summary>
    /// Registers this item in the Mod-owned virtual catalog.
    /// Returns false when the key already exists or uses the game namespace.
    /// </summary>
    public bool Register() => ItemRegistry.Register(this);
}

/// <summary>
/// Registration entry point for Mod-owned <see cref="Item"/> instances.
/// Registration only affects the public virtual catalog; it does not assign a
/// game numeric ID or mutate c_item, saves, or player inventory.
/// </summary>
public static class ItemRegistry
{
    public static bool Register(Item item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return ItemCatalog.TryRegister(item.Definition);
    }
}
