using BackToTheDawn.ModAPI;

namespace BackToTheDawn.ExampleMod;

public sealed class ExampleModEntry : IMod
{
    private readonly List<IDisposable> _subscriptions = new();
    private ModContext? _context;
    private bool _logGameplayState;

    public void Initialize(ModContext context)
    {
        _context = context;
        _logGameplayState = context.Config.Get("logGameplayState", true);
        context.Config.Set("configApiVersion", 1);
        context.Config.Save();

        var debugItem = new DebugTokenItem(context.Manifest.Id);
        if (debugItem.Register())
        {
            context.Logger.Info($"Registered virtual item: {debugItem.Key} ({debugItem.DisplayName}).");
        }
        else
        {
            context.Logger.Warning($"Virtual item was already registered: {debugItem.Key}.");
        }

        _subscriptions.Add(GameEvents.Subscribe<StartupStepChangedEvent>(OnStartupStepChanged));
        _subscriptions.Add(GameEvents.Subscribe<MainMenuEnteredEvent>(OnMainMenuEntered));
        _subscriptions.Add(GameEvents.Subscribe<ArchiveLoadStartedEvent>(OnArchiveLoadStarted));
        _subscriptions.Add(
            GameEvents.Subscribe<ArchiveLoadInvocationReturnedEvent>(OnArchiveLoadInvocationReturned));
        if (_logGameplayState)
        {
            _subscriptions.Add(GameEvents.Subscribe<GameplayReadyEvent>(OnGameplayReady));
        }
        _subscriptions.Add(GameEvents.Subscribe<ItemCatalogReadyEvent>(OnItemCatalogReady));
        _subscriptions.Add(GameEvents.Subscribe<TimeChangedEvent>(OnTimeChanged));
        _subscriptions.Add(GameEvents.Subscribe<MapChangedEvent>(OnMapChanged));
        _subscriptions.Add(GameEvents.Subscribe<PlayerItemUsedEvent>(OnPlayerItemUsed));
        _subscriptions.Add(GameEvents.Subscribe<PlayerItemActionEvent>(OnPlayerItemAction));
        if (_logGameplayState)
        {
            _subscriptions.Add(
                GameEvents.Subscribe<PlayerStateChangedEvent>(OnPlayerStateChanged));
        }
        _subscriptions.Add(GameEvents.Subscribe<ModRegistryReadyEvent>(OnModRegistryReady));
        _subscriptions.Add(GameEvents.Subscribe<ModInitializedEvent>(OnModInitialized));
        _subscriptions.Add(
            GameEvents.Subscribe<ModInitializationFailedEvent>(OnModInitializationFailed));

        context.Logger.Info(
            "Example Mod initialized through IMod and static GameEvents.Subscribe<T>().");
        context.Logger.Info(
            $"Config loaded from {context.Config.FilePath} " +
            $"(logGameplayState={_logGameplayState}).");
        if (context.Resources.Exists("README.txt"))
        {
            context.Logger.Info($"Resource directory: {context.Resources.RootDirectory}");
        }
    }

    public void Shutdown()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        _logGameplayState = false;
        _context?.Logger.Info("Example Mod IMod entry shut down.");
        _context = null;
    }

    private void OnStartupStepChanged(StartupStepChangedEvent info) =>
        Info($"Startup step changed to {info.Step}.");

    private void OnMainMenuEntered(MainMenuEnteredEvent info) =>
        Info(
            $"Main menu entered: selectInput={info.ShowsInputSelection}, " +
            $"immediateArchiveId={info.ImmediateArchiveId}.");

    private void OnArchiveLoadStarted(ArchiveLoadStartedEvent info) =>
        Info($"Archive load started: {info.ArchiveId}.");

    private void OnArchiveLoadInvocationReturned(ArchiveLoadInvocationReturnedEvent info) =>
        Info($"Archive load invocation returned: {info.ArchiveId}.");

    private void OnGameplayReady(GameplayReadyEvent _)
    {
        if (!GameContext.TryGetSnapshot(out var state) || state is null)
        {
            Warning("Gameplay is ready, but no GameContext snapshot is available.");
            return;
        }

        Info(
            $"Gameplay ready: archive={state.ArchiveId}, " +
            $"map={state.MapId} ('{state.MapName}'), " +
            $"time=day {state.Time.Day} {state.Time.Hour:D2}:{state.Time.Minute:D2}.");

        if (state.Player is not null)
        {
            Info(
                $"Player {state.Player.CharacterId}: " +
                $"health={state.Player.Health}/{state.Player.MaxHealth}, " +
                $"mentality={state.Player.Mentality}/{state.Player.MaxMentality}, " +
                $"satiety={state.Player.Satiety}/{state.Player.MaxSatiety}, " +
                $"energy={state.Player.Energy}/{state.Player.MaxEnergy}, " +
                $"focus={state.Player.Focus}/{state.Player.MaxFocus}, money={state.Player.Money}.");
        }
    }

    private void OnItemCatalogReady(ItemCatalogReadyEvent info)
    {
        if (!ItemCatalog.TryGet("backtothedawn:apple", out var apple))
        {
            Warning($"Item catalog ready with {info.Count} entries, but apple was not found.");
            return;
        }

        var rawIdText = ItemIdResolver.TryGetId(apple.Key, out var rawId)
            ? $"explicit raw ID={rawId}"
            : "raw ID unavailable";
        Info(
            $"Item catalog ready: {info.Count} entries; " +
            $"key={apple.Key}, name={apple.DisplayName}, {rawIdText}.");

        if (ItemCatalog.TryGet("backtothedawn:painkiller", out var painkiller))
        {
            var effects = ItemCatalog.GetEffects(painkiller.Key);
            var effectSummary = string.Join(
                "; ",
                effects.Select(effect =>
                    $"{effect.Key}={effect.Value}" +
                    (effect.Duration > 0 ? $"/{effect.Duration}" : string.Empty)));
            Info($"Painkiller effects: {effectSummary}.");
        }
    }

    private void OnTimeChanged(TimeChangedEvent info) =>
        Info(
            $"Time changed: " +
            $"day {info.Previous.Day} {info.Previous.Hour:D2}:{info.Previous.Minute:D2} -> " +
            $"day {info.Current.Day} {info.Current.Hour:D2}:{info.Current.Minute:D2}.");

    private void OnMapChanged(MapChangedEvent info) =>
        Info(
            $"Map changed: {info.PreviousMapId} -> " +
            $"{info.CurrentMapId} ('{info.CurrentMapName}').");

    private void OnPlayerStateChanged(PlayerStateChangedEvent info) =>
        Info(
            $"Player state changed by {info.Source}: " +
            $"health={info.Previous.Health}->{info.Current.Health}, " +
            $"mentality={info.Previous.Mentality}->{info.Current.Mentality}, " +
            $"satiety={info.Previous.Satiety}->{info.Current.Satiety}, " +
            $"energy={info.Previous.Energy}->{info.Current.Energy}, " +
            $"focus={info.Previous.Focus}->{info.Current.Focus}, " +
            $"money={info.Previous.Money}->{info.Current.Money}.");

    private void OnPlayerItemUsed(PlayerItemUsedEvent info) =>
        Info(
            $"Player item used: itemKey={info.ItemKey}, " +
            $"useCount={info.UseCount}, characterId={info.CharacterId}.");

    private void OnPlayerItemAction(PlayerItemActionEvent info) =>
        Info(
            $"Player item action: action={info.Action}, itemKey={info.ItemKey?.ToString() ?? "<pocket>"}, " +
            $"count={info.Count}, succeeded={info.Succeeded}, source={info.Source}, " +
            $"rawOperationType={info.RawOperationType}.");

    private void OnModRegistryReady(ModRegistryReadyEvent info) =>
        Info($"Mod registry ready: {info.Mods.Length} valid, {info.Rejected.Length} rejected.");

    private void OnModInitialized(ModInitializedEvent info) =>
        Info($"Mod initialized: {info.Mod.Manifest.Id} ({info.EntryType}).");

    private void OnModInitializationFailed(ModInitializationFailedEvent info) =>
        Warning($"Mod initialization failed: {info.ModId ?? "<unknown>"}: {info.Reason}");

    private void Info(string message) => _context?.Logger.Info($"[ExampleMod] {message}");

    private void Warning(string message) => _context?.Logger.Warning($"[ExampleMod] {message}");

    private sealed class DebugTokenItem : Item
    {
        public DebugTokenItem(string @namespace)
            : base(
                new ItemKey(@namespace, "debug_token"),
                "调试令牌",
                "custom",
                backgroundDescription: "由 ExampleMod 注册的虚拟物品。")
        {
        }
    }
}
