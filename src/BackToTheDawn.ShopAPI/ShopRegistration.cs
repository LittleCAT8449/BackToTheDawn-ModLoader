using BackToTheDawn.ModAPI;

namespace BackToTheDawn.ShopAPI;

/// <summary>A Mod item and its cash price in a native shop.</summary>
public sealed record ShopOffer(ItemKey ItemKey, int Price, int Stock = int.MaxValue);

public enum ShopMutationStatus
{
    Scheduled = 0,
    Applied = 1,
    AlreadyExists = 2,
    NotFound = 3,
    InvalidDefinition = 4,
    RuntimeUnavailable = 5,
    Failed = 6,
}

public sealed record ShopMutationResult(
    ShopKey ShopKey,
    ShopMutationStatus Status,
    string Message)
{
    public bool Succeeded => Status is ShopMutationStatus.Scheduled or ShopMutationStatus.Applied;
}

/// <summary>
/// Registers shops and goods and opens the game's native shop screen.
/// Obtain an instance with <see cref="For"/> so registrations are scoped to a Mod.
/// </summary>
public sealed partial class ShopApi
{
    internal static Func<string, ShopKey, string, IReadOnlyList<ShopOffer>, ShopMutationResult>?
        RegisterShopProvider { get; set; }
    internal static Func<string, ShopKey, ShopOffer, ShopMutationResult>?
        AddGoodsProvider { get; set; }
    internal static Func<string, ShopKey, ItemKey, int, ShopMutationResult>?
        SetPriceProvider { get; set; }
    internal static Func<ShopKey, ShopMutationResult>? OpenShopProvider { get; set; }

    private readonly string? _ownerId;

    private ShopApi(string ownerId) => _ownerId = ownerId;

    public static ShopApi For(ModContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new ShopApi(context.Manifest.Id);
    }

    /// <summary>Registers a new shop in the owning Mod's namespace.</summary>
    public ShopMutationResult RegisterShop(
        string path,
        string displayName,
        IEnumerable<ShopOffer> offers)
    {
        EnsureOwner();
        ArgumentNullException.ThrowIfNull(offers);
        var key = new ShopKey(_ownerId!, path);
        var offerArray = offers.ToArray();
        ValidateOffers(offerArray);
        return RegisterShopProvider?.Invoke(_ownerId!, key, displayName, offerArray)
               ?? Unavailable(key);
    }

    /// <summary>Adds an item to a registered native or Mod shop.</summary>
    public ShopMutationResult AddGoods(ShopKey shopKey, ShopOffer offer)
    {
        EnsureOwner();
        ArgumentNullException.ThrowIfNull(offer);
        ValidateOffers(new[] { offer });
        return AddGoodsProvider?.Invoke(_ownerId!, shopKey, offer) ?? Unavailable(shopKey);
    }

    /// <summary>Changes the price of an item already sold by this shop.</summary>
    public ShopMutationResult SetPrice(ShopKey shopKey, ItemKey itemKey, int price)
    {
        EnsureOwner();
        if (price < 0) throw new ArgumentOutOfRangeException(nameof(price));
        return SetPriceProvider?.Invoke(_ownerId!, shopKey, itemKey, price) ?? Unavailable(shopKey);
    }

    /// <summary>Opens the game's native shop screen for this shop.</summary>
    public ShopMutationResult OpenShop(ShopKey shopKey) =>
        OpenShopProvider?.Invoke(shopKey) ?? Unavailable(shopKey);

    private void EnsureOwner()
    {
        if (_ownerId is null)
            throw new InvalidOperationException("Use ShopApi.For(context) to register or modify shops.");
    }

    private static ShopMutationResult Unavailable(ShopKey key) =>
        new(key, ShopMutationStatus.RuntimeUnavailable, "The Loader shop runtime is unavailable.");

    private static void ValidateOffers(IReadOnlyList<ShopOffer> offers)
    {
        if (offers.Count == 0) throw new ArgumentException("At least one shop offer is required.", nameof(offers));
        if (offers.Any(offer => offer.Price < 0 || offer.Stock < 0))
            throw new ArgumentException("Shop prices and stock must be zero or greater.", nameof(offers));
        if (offers.Select(offer => offer.ItemKey.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != offers.Count)
            throw new ArgumentException("An item can only appear once in one shop registration.", nameof(offers));
    }
}
