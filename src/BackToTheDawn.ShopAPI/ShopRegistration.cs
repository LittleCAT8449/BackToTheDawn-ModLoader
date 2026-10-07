using BackToTheDawn.ModAPI;

namespace BackToTheDawn.ShopAPI;

/// <summary>Controls when a purchased offer is delivered.</summary>
public enum ShopDeliveryMode
{
    /// <summary>Add the purchased item to the player's pocket immediately.</summary>
    Immediate = 0,

    /// <summary>Place the purchase in the game's parcel queue for next-day pickup.</summary>
    NextDayPackage = 1,
}

/// <summary>A Mod item and its cash price in a native shop.</summary>
public sealed record ShopOffer(ItemKey ItemKey, int Price, int Stock = int.MaxValue)
{
    /// <summary>
    /// Number of in-game days to wait after the offer sells out before restoring its stock.
    /// Zero restores stock the next time a new shop window session is opened.
    /// </summary>
    public int RestockDays { get; init; } = 1;

    /// <summary>Specifies whether the item is granted now or delivered in a parcel tomorrow.</summary>
    public ShopDeliveryMode Delivery { get; init; } = ShopDeliveryMode.Immediate;

    /// <summary>
    /// Overrides the source label shown in the game's parcel pickup UI.
    /// When omitted, the registered shop's display name is used.
    /// Only applies when <see cref="Delivery"/> is <see cref="ShopDeliveryMode.NextDayPackage"/>.
    /// </summary>
    public string? DeliverySource { get; init; }

    /// <summary>Creates an offer with an explicit delivery mode and unlimited stock.</summary>
    public ShopOffer(ItemKey itemKey, int price, ShopDeliveryMode delivery)
        : this(itemKey, price) => Delivery = delivery;

    /// <summary>Creates an offer with an explicit delivery mode.</summary>
    public ShopOffer(ItemKey itemKey, int price, int stock, ShopDeliveryMode delivery)
        : this(itemKey, price, stock) => Delivery = delivery;
}

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

    private static event Action<ShopClosedEvent>? ShopClosed;
    internal static Action<Exception>? SubscriberErrorLogger { get; set; }

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

    /// <summary>Observes a native shop window closed after being opened by the Loader.</summary>
    public IDisposable SubscribeClosed(Action<ShopClosedEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ShopClosed += handler;
        return new Subscription(() => ShopClosed -= handler);
    }

    internal static void PublishClosed(ShopClosedEvent value)
    {
        var subscribers = ShopClosed;
        if (subscribers is null)
        {
            return;
        }

        foreach (var subscriber in subscribers.GetInvocationList().Cast<Action<ShopClosedEvent>>())
        {
            try
            {
                subscriber(value);
            }
            catch (Exception exception)
            {
                SubscriberErrorLogger?.Invoke(exception);
            }
        }
    }

    internal static void ClearSubscribers()
    {
        ShopClosed = null;
    }

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
        if (offers.Any(offer => offer.Price < 0 || offer.Stock < 0 || offer.RestockDays < 0 ||
                                !Enum.IsDefined(typeof(ShopDeliveryMode), offer.Delivery)))
            throw new ArgumentException(
                "Shop prices, stock and restock days must be zero or greater, and delivery mode must be defined.",
                nameof(offers));
        if (offers.Select(offer => offer.ItemKey.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != offers.Count)
            throw new ArgumentException("An item can only appear once in one shop registration.", nameof(offers));
    }

    private sealed class Subscription : IDisposable
    {
        private Action? _dispose;

        internal Subscription(Action dispose) => _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}

/// <summary>Identifies a native shop window that was just closed.</summary>
public sealed record ShopClosedEvent(ShopKey ShopKey, int NativeShopId);
