using BackToTheDawn.ModAPI;
using BackToTheDawn.ShopAPI;
using ShopApi = BackToTheDawn.ShopAPI.ShopApi;

namespace BackToTheDawn.ShopTestMod;

public sealed class ShopTestModEntry : IMod
{
    private static readonly ShopKey TestShopKey =
        new("dev.backtothedawn.shoptestmod", "test_market");

    private readonly List<IDisposable> _subscriptions = new();
    private ModContext? _context;
    private ShopApi? _shops;
    private bool _registrationSucceeded;
    private bool _openRequested;

    public void Initialize(ModContext context)
    {
        _context = context;
        _shops = ShopApi.For(context);

        var registration = _shops.RegisterShop(
            "test_market",
            "Mod 商店测试",
            new[]
            {
                new ShopOffer(new ItemKey("backtothedawn", "apple"), Price: 25, Stock: 20),
                new ShopOffer(new ItemKey("backtothedawn", "painkiller"), Price: 80, Stock: 10),
            });

        _registrationSucceeded = registration.Succeeded;
        context.Logger.Info(
            $"Test shop registration: status={registration.Status}, " +
            $"key={registration.ShopKey}, message={registration.Message}.");

        if (!_registrationSucceeded)
        {
            return;
        }

        _subscriptions.Add(
            ModApi.Events.Subscribe<GameplayReadyEvent>(_ => TryOpenTestShop()));

        // Also handle the case where the Mod is loaded after gameplay began.
        if (ModApi.Game.IsGameplayReady)
        {
            TryOpenTestShop();
        }
    }

    public void Shutdown()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        _shops = null;
        _registrationSucceeded = false;
        _openRequested = false;
        _context?.Logger.Info("Shop Test Mod shut down.");
        _context = null;
    }

    private void TryOpenTestShop()
    {
        if (!_registrationSucceeded || _openRequested || _shops is null)
        {
            return;
        }

        _openRequested = true;
        var result = _shops.OpenShop(TestShopKey);
        _context?.Logger.Info(
            $"Test shop open request: status={result.Status}, " +
            $"key={result.ShopKey}, message={result.Message}.");
    }
}
