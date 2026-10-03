namespace BackToTheDawn.ModAPI;

/// <summary>
/// Price metadata observed from a native shop configuration.
/// </summary>
public sealed record ShopGoodsDefinition(
    ShopKey ShopKey,
    ItemKey ItemKey,
    int? Price,
    TradeCurrencyKind PriceCurrency,
    string? NativePriceType,
    int? Stock,
    int? AvailableFromDay,
    int? AvailableToDay,
    string? Group,
    string Source);

/// <summary>
/// Raised when the loader observes a shop goods configuration at runtime.
/// The catalog is incremental because some shops are created only when their
/// UI is opened.
/// </summary>
public sealed record ShopGoodsObservedEvent(ShopGoodsDefinition Goods) : IGameEvent;

/// <summary>
/// Read-only catalog of native shop goods observed during this process.
/// </summary>
public static class ShopGoodsCatalog
{
    private static readonly object SyncRoot = new();
    private static Dictionary<string, ShopGoodsDefinition> _goods =
        new(StringComparer.OrdinalIgnoreCase);
    private static IReadOnlyList<ShopGoodsDefinition> _all = Array.Empty<ShopGoodsDefinition>();

    public static bool IsAvailable { get; private set; }

    public static IReadOnlyList<ShopGoodsDefinition> All => _all;

    public static IReadOnlyList<ShopGoodsDefinition> GetGoods(ShopKey shopKey) =>
        _all.Where(value => value.ShopKey == shopKey).ToArray();

    public static bool TryGet(ShopKey shopKey, ItemKey itemKey, out ShopGoodsDefinition goods)
    {
        lock (SyncRoot)
        {
            return _goods.TryGetValue(Key(shopKey, itemKey), out goods!);
        }
    }

    internal static void Upsert(ShopGoodsDefinition value)
    {
        lock (SyncRoot)
        {
            _goods[Key(value.ShopKey, value.ItemKey)] = value;
            _all = _goods.Values
                .OrderBy(item => item.ShopKey.ToString())
                .ThenBy(item => item.ItemKey.ToString())
                .ToArray();
            IsAvailable = true;
        }

        GameEvents.RaiseShopGoodsObserved(value);
    }

    internal static void Remove(ShopKey shopKey, ItemKey itemKey)
    {
        lock (SyncRoot)
        {
            if (!_goods.Remove(Key(shopKey, itemKey))) return;
            _all = _goods.Values
                .OrderBy(item => item.ShopKey.ToString())
                .ThenBy(item => item.ItemKey.ToString())
                .ToArray();
            IsAvailable = _goods.Count > 0;
        }
    }

    internal static void Reset()
    {
        lock (SyncRoot)
        {
            _goods = new Dictionary<string, ShopGoodsDefinition>(StringComparer.OrdinalIgnoreCase);
            _all = Array.Empty<ShopGoodsDefinition>();
            IsAvailable = false;
        }
    }

    private static string Key(ShopKey shopKey, ItemKey itemKey) =>
        shopKey + "|" + itemKey;
}
