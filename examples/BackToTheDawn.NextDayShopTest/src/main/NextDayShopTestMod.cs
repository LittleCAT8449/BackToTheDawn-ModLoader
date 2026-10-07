using BackToTheDawn.ModAPI;
using BackToTheDawn.ShopAPI;
using ShopRegistrationApi = BackToTheDawn.ShopAPI.ShopApi;

namespace BackToTheDawn.NextDayShopTest;

/// <summary>Registers and opens a shop used to verify next-day parcel delivery.</summary>
public sealed class NextDayShopTestMod : IMod
{
    private static readonly ShopKey TestShopKey = new("dev.backtothedawn.nextdayshoptest", "parcel_test");

    private ShopRegistrationApi? _shops;
    private ModContext? _context;
    private IDisposable? _gameplayReadySubscription;

    public void Initialize(ModContext context)
    {
        _context = context;
        _shops = ShopRegistrationApi.For(context);

        var registration = _shops.RegisterShop(
            "parcel_test",
            "次日配送测试店",
            new[]
            {
                new ShopOffer(new ItemKey("backtothedawn", "apple"), Price: 25, Stock: 10)
                {
                    Delivery = ShopDeliveryMode.NextDayPackage,
                    DeliverySource = "测试快递",
                },
            });

        if (!registration.Succeeded)
        {
            context.Logger.Error($"Could not register the parcel test shop: {registration.Message}");
            return;
        }

        context.Logger.Info(
            "Registered the next-day parcel test shop (apple, $25, stock 10). " +
            "It opens automatically when entering gameplay.");

        _gameplayReadySubscription = GameEvents.Subscribe<GameplayReadyEvent>(_ => OpenTestShop());
        if (ModApi.Game.IsGameplayReady)
            OpenTestShop();
    }

    public void Shutdown()
    {
        _gameplayReadySubscription?.Dispose();
        _gameplayReadySubscription = null;
        _shops = null;
        _context = null;
    }

    private void OpenTestShop()
    {
        if (_shops is null || _context is null)
            return;

        var result = _shops.OpenShop(TestShopKey);
        if (result.Succeeded)
            _context.Logger.Info("Opening the next-day parcel test shop.");
        else
            _context.Logger.Warning($"Could not open the parcel test shop: {result.Message}");
    }
}
