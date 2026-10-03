namespace BackToTheDawn.ModAPI;

/// <summary>
/// Unified entry point for the stable public Mod API. Existing static classes
/// such as <see cref="ItemRegistry"/>, <see cref="ItemCatalog"/> and
/// <see cref="GameEvents"/> remain available for compatibility; new Mods can
/// use this facade to discover the same capabilities from one place.
/// </summary>
public static class ModApi
{
    public static ItemApi Items { get; } = new();

    public static ModEventApi Events { get; } = new();

    public static ModGameApi Game { get; } = new();

    public static InventoryApi Inventory { get; } = new();

    public static ShopApi Shops { get; } = new();

    public static RelationshipApi Relationships { get; } = new();

    /// <summary>Room focus and room transition API.</summary>
    public static RoomApi Rooms { get; } = new();

    public static GuiApi Gui { get; } = new();

    /// <summary>Modern retained-mode UI facade.</summary>
    public static CanvasApi UI { get; } = new();

    /// <summary>Backward-compatible alias for <see cref="UI"/>.</summary>
    public static CanvasApi Canvas => UI;
}

/// <summary>
/// Namespaced item registration, catalog lookup, behavior registration and
/// explicit low-level ID conversion.
/// </summary>
public sealed class ItemApi
{
    internal ItemApi()
    {
    }

    public IReadOnlyList<Item> Registered => ItemRegistry.All;

    public IReadOnlyList<ItemDefinition> Catalog => ItemCatalog.All;

    public bool IsCatalogAvailable => ItemCatalog.IsAvailable;

    public bool IsRuntimeReady => ItemCatalog.IsRuntimeReady;

    public ItemRegistrationResult Register(Item item) => ItemRegistry.Register(item);

    public ItemUnregistrationResult Unregister(Item item) =>
        ItemRegistry.UnregisterDetailed(item);

    public bool TryGet(ItemKey key, out ItemDefinition definition) =>
        ItemCatalog.TryGet(key, out definition);

    public bool TryGet(string key, out ItemDefinition definition) =>
        ItemCatalog.TryGet(key, out definition);

    public IReadOnlyList<ItemEffectDefinition> GetEffects(ItemKey key) =>
        ItemCatalog.GetEffects(key);

    public IReadOnlyList<ItemEffectDefinition> GetEffects(string key) =>
        ItemCatalog.GetEffects(key);

    public ItemBehaviorRegistrationResult RegisterBehavior(
        ItemKey key,
        IItemBehavior behavior) =>
        ItemBehaviorRegistry.Register(key, behavior);

    public bool TryGetBehavior(ItemKey key, out IItemBehavior behavior) =>
        ItemBehaviorRegistry.TryGet(key, out behavior);

    public bool UnregisterBehavior(ItemKey key) => ItemBehaviorRegistry.Unregister(key);

    /// <summary>
    /// Converts a namespaced item key to the current process-local numeric ID.
    /// This is intentionally explicit because IDs are not stable across runs.
    /// </summary>
    public bool TryGetRuntimeId(ItemKey key, out int id) =>
        ItemIdResolver.TryGetId(key, out id);

    public int GetRuntimeId(ItemKey key) => ItemIdResolver.GetId(key);

    public bool TryGetKey(int id, out ItemKey key) => ItemIdResolver.TryGetKey(id, out key);

    public bool TryGetEffectRuntimeId(ItemKey key, out int id) =>
        EffectIdResolver.TryGetId(key, out id);

    public bool TryGetEffectKey(int id, out ItemKey key) =>
        EffectIdResolver.TryGetKey(id, out key);
}

/// <summary>
/// Event subscription facade. Each subscription returns a disposable handle
/// and is safe to dispose during Mod shutdown.
/// </summary>
public sealed class ModEventApi
{
    internal ModEventApi()
    {
    }

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        where TEvent : IGameEvent => GameEvents.Subscribe(handler);
}

/// <summary>
/// Read-only game state facade. Mutating game services are intentionally kept
/// out until their operation and rollback semantics are defined.
/// </summary>
public sealed class ModGameApi
{
    internal ModGameApi()
    {
    }

    public bool IsGameplayReady => GameContext.IsGameplayReady;

    public GameStateSnapshot? Current => GameContext.Current;

    public bool TryGetSnapshot(out GameStateSnapshot? snapshot) =>
        GameContext.TryGetSnapshot(out snapshot);

    public InventorySnapshot? Inventory => GameContext.Inventory;

    public bool TryGetInventorySnapshot(out InventorySnapshot? snapshot) =>
        GameContext.TryGetInventorySnapshot(out snapshot);
}

/// <summary>
/// Namespaced shop lookup. Native numeric IDs are exposed only for explicit
/// compatibility lookups; normal Mod code should use <see cref="ShopKey"/>.
/// </summary>
public sealed class ShopApi
{
    internal ShopApi()
    {
    }

    public IReadOnlyList<ShopDescriptor> Catalog => ShopCatalog.All;

    public bool IsAvailable => ShopCatalog.IsAvailable;

    public bool TryGet(ShopKey key, out ShopDescriptor descriptor) =>
        ShopCatalog.TryGet(key, out descriptor);

    public bool TryGet(string key, out ShopDescriptor descriptor) =>
        ShopCatalog.TryGet(key, out descriptor);

    public bool TryGetByNativeId(int nativeShopId, out ShopDescriptor descriptor) =>
        ShopCatalog.TryGetByNativeId(nativeShopId, out descriptor);

    public bool IsGoodsAvailable => ShopGoodsCatalog.IsAvailable;

    public IReadOnlyList<ShopGoodsDefinition> Goods => ShopGoodsCatalog.All;

    public IReadOnlyList<ShopGoodsDefinition> GetGoods(ShopKey key) =>
        ShopGoodsCatalog.GetGoods(key);

    public bool TryGetGoods(
        ShopKey shopKey,
        ItemKey itemKey,
        out ShopGoodsDefinition goods) =>
        ShopGoodsCatalog.TryGet(shopKey, itemKey, out goods);
}
