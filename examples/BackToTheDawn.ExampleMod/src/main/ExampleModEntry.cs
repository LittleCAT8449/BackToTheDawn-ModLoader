using BackToTheDawn.ModAPI;
using BackToTheDawn.PhoneAPI;

namespace BackToTheDawn.ExampleMod;

public sealed class ExampleModEntry : IMod
{
    private readonly List<IDisposable> _subscriptions = new();
    private ModContext? _context;
    private DebugTokenItem? _debugItem;
    private bool _logGameplayState;
    private bool _cancelDebugTokenUse;
    private bool _unregistrationApiTested;


    public void Initialize(ModContext context)
    {
        _context = context;
        _logGameplayState = context.Config.Get("logGameplayState", true);
        _cancelDebugTokenUse = context.Config.Get("cancelDebugTokenUse", false);
        context.Config.Set("configApiVersion", 1);
        context.Config.Set("cancelDebugTokenUse", _cancelDebugTokenUse);
        context.Config.Save();

        RegisterExamplePhone(context);

        _debugItem = new DebugTokenItem(context.Manifest.Id);
        var registration = ModApi.Items.Register(_debugItem);
        if (registration.Succeeded)
        {
            context.Logger.Info(
                $"Registered virtual item: {_debugItem.Key} ({_debugItem.DisplayName}).");
        }
        else
        {
            context.Logger.Warning(
                $"Virtual item registration failed: {registration.Status} - {registration.Message}");
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
        _subscriptions.Add(GameEvents.Subscribe<ItemRuntimeReadyEvent>(OnItemRuntimeReady));
        _subscriptions.Add(GameEvents.Subscribe<TimeChangedEvent>(OnTimeChanged));
        _subscriptions.Add(ModApi.Rooms.Subscribe(OnRoomChanged));
        _subscriptions.Add(GameEvents.Subscribe<PlayerItemUsedEvent>(OnPlayerItemUsed));
        _subscriptions.Add(GameEvents.Subscribe<PlayerItemActionEvent>(OnPlayerItemAction));
        _subscriptions.Add(ModApi.Events.Subscribe<InventoryChangedEvent>(OnInventoryChanged));
        _subscriptions.Add(ModApi.Events.Subscribe<InventoryMovedEvent>(OnInventoryMoved));
        _subscriptions.Add(ModApi.Events.Subscribe<TradeDetectedEvent>(OnTradeDetected));
        _subscriptions.Add(ModApi.Events.Subscribe<TradeStartedEvent>(OnTradeStarted));
        _subscriptions.Add(ModApi.Events.Subscribe<TradeCompletedEvent>(OnTradeCompleted));
        _subscriptions.Add(ModApi.Events.Subscribe<TradeFailedEvent>(OnTradeFailed));
        _subscriptions.Add(ModApi.Events.Subscribe<BetSettledEvent>(OnBetSettled));
        _subscriptions.Add(
            ModApi.Events.Subscribe<LotteryPrizeCashedEvent>(OnLotteryPrizeCashed));
        _subscriptions.Add(
            ModApi.Events.Subscribe<ShopGoodsObservedEvent>(OnShopGoodsObserved));
        _subscriptions.Add(ModApi.Events.Subscribe<ItemUseBeforeEvent>(OnItemUseBefore));
        _subscriptions.Add(ModApi.Events.Subscribe<ItemUseAfterEvent>(OnItemUseAfter));
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
            $"(logGameplayState={_logGameplayState}, " +
            $"cancelDebugTokenUse={_cancelDebugTokenUse}).");
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
        _cancelDebugTokenUse = false;
        _unregistrationApiTested = false;



        _debugItem = null;
        _context?.Logger.Info("Example Mod IMod entry shut down.");
        _context = null;
    }

    private void OnStartupStepChanged(StartupStepChangedEvent info) =>
        Info($"Startup step changed to {info.Step}.");

    private static void RegisterExamplePhone(ModContext context)
    {
        const string conversationKey = "repair-shop";
        var phones = PhoneApi.For(context);
        var conversation = phones.RegisterConversation(
            conversationKey,
            new[]
            {
                new PhoneDialogueLine(
                    "街角修理店",
                    "喂，这里是街角修理店。你需要什么服务？",
                    PhoneDialogueSpeakerType.Caller)
                {
                    Id = "menu",
                    Options = new[]
                    {
                        new PhoneDialogueOption("repair", "修理收音机", "repair-question"),
                        new PhoneDialogueOption("hours", "询问营业时间", "hours-question"),
                        new PhoneDialogueOption("hang-up", "挂断", EndCall: true),
                    },
                },
                new PhoneDialogueLine(
                    "你",
                    "请问能修理收音机吗？",
                    PhoneDialogueSpeakerType.Player) { Id = "repair-question" },
                new PhoneDialogueLine(
                    "街角修理店",
                    "可以，带来后我会检查零件和费用。",
                    PhoneDialogueSpeakerType.Caller) { EndCall = true },
                new PhoneDialogueLine(
                    "你",
                    "请问今天几点关门？",
                    PhoneDialogueSpeakerType.Player) { Id = "hours-question" },
                new PhoneDialogueLine(
                    "街角修理店",
                    "今天晚上六点关门，记得早点过来。",
                    PhoneDialogueSpeakerType.Caller) { EndCall = true },
            },
            interactionIconPath: "debug_token.png");

        if (!conversation.Succeeded)
        {
            context.Logger.Warning(
                $"Could not register ExampleMod phone conversation: {conversation.Message}");
            return;
        }

        const string phoneNumber = "48327";
        var number = phones.RegisterNumber(phoneNumber, "街角修理店", conversationKey);
        if (number.Succeeded)
        {
            context.Logger.Info(
                $"Registered ExampleMod phone number {phoneNumber} (街角修理店).");
        }
        else
        {
            context.Logger.Warning(
                $"Could not register ExampleMod phone number {phoneNumber}: {number.Message}");
        }
    }

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

        if (ModApi.Game.TryGetInventorySnapshot(out var inventory) && inventory is not null)
        {
            Info($"Inventory snapshot: {inventory.Items.Count} stacks, " +
                 $"debug_token={inventory.GetCount(_debugItem?.Key ?? default)}.");
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

    private void OnItemRuntimeReady(ItemRuntimeReadyEvent info)
    {
        Info(
            $"Item runtime ready: catalog={info.CatalogCount}, " +
            $"injected={info.InjectedCount}, injectionEnabled={info.InjectionEnabled}.");

        // Exercise the public structured unload API after the item has been
        // assigned a live runtime ID. The expected result is RuntimeBound;
        // the item must remain registered until the game process exits.
        if (_unregistrationApiTested ||
            !info.InjectionEnabled ||
            info.InjectedCount <= 0 ||
            _debugItem is null)
        {
            return;
        }

        var result = _debugItem.UnregisterDetailed();
        Info(
            $"Unregister API test: status={result.Status}, " +
            $"succeeded={result.Succeeded}, stillRegistered={_debugItem.IsRegistered}, " +
            $"message={result.Message}");
        _unregistrationApiTested = true;
    }

    private void OnTimeChanged(TimeChangedEvent info) =>
        Info(
            $"Time changed: " +
            $"day {info.Previous.Day} {info.Previous.Hour:D2}:{info.Previous.Minute:D2} -> " +
            $"day {info.Current.Day} {info.Current.Hour:D2}:{info.Current.Minute:D2}.");

    private void OnRoomChanged(RoomChangedEvent info) =>
        Info(
            $"Room changed: {info.Previous?.Id} ('{info.Previous?.Name}') -> " +
            $"{info.Current.Id} ('{info.Current.Name}')." );

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

    private void OnInventoryChanged(InventoryChangedEvent info) =>
        Info(
            $"Inventory changed: itemKey={info.ItemKey}, delta={info.Delta}, " +
            $"total={info.TotalCount}, change={info.Change}, succeeded={info.Succeeded}, " +
            $"source={info.Source}, reason={info.Reason}.");

    private void OnInventoryMoved(InventoryMovedEvent info) =>
        Info(
            $"Inventory moved: itemKey={info.ItemKey}, count={info.Count}, " +
            $"from={info.From.Container}({info.From.PlaceId},{info.From.SubPlaceId}) -> " +
            $"to={info.To.Container}({info.To.PlaceId},{info.To.SubPlaceId}), " +
            $"source={info.Source}, succeeded={info.Succeeded}.");

    private void OnTradeDetected(TradeDetectedEvent info) =>
        Info(
            $"Trade detected: kind={info.Kind}, leg={info.Leg}, " +
            $"itemKey={info.ItemKey?.ToString() ?? "<none>"}, " +
            $"itemDelta={info.ItemDelta}, currency={info.Currency}, " +
            $"currencyDelta={info.CurrencyDelta}, disciplineDelta={info.DisciplineDelta}, " +
            $"shopKey={info.ShopKey?.ToString() ?? "<none>"}, " +
             $"phase={info.Phase}, requestedCount={info.RequestedCount}, " +
             $"relationshipDelta={info.RelationshipDelta}, " +
             $"lotteryNumber={info.LotteryNumber ?? "<none>"}, " +
             $"bet={FormatBet(info.Bet)}, " +
             $"tx={info.TransactionId}, status={info.Status}, " +
            $"reason={info.Reason}, " +
            $"source={info.Source}, direction={info.Direction}, " +
            $"counterparty={info.CounterpartyId?.ToString() ?? "<none>"}/" +
            $"{info.CounterpartyName ?? "<unknown>"}, observation={info.ObservationId}.");

    private void OnTradeStarted(TradeStartedEvent info) =>
        Info(
            $"Trade started: tx={info.Transaction.TransactionId}, " +
             $"kind={info.Transaction.Kind}, phase={info.Transaction.Phase}, " +
             $"item={info.Transaction.ItemKey?.ToString() ?? "<none>"}, " +
             $"shopKey={info.Transaction.ShopKey?.ToString() ?? "<none>"}, " +
             $"lotteryNumber={info.Transaction.LotteryNumber ?? "<none>"}, " +
             $"bet={FormatBet(info.Transaction.Bet)}, " +
             $"reason={info.Transaction.Reason}.");

    private void OnTradeCompleted(TradeCompletedEvent info) =>
        Info(
            $"Trade completed: tx={info.Transaction.TransactionId}, " +
            $"kind={info.Transaction.Kind}, itemDelta={info.Transaction.ItemDelta}, " +
             $"currency={info.Transaction.Currency}, " +
             $"currencyDelta={info.Transaction.CurrencyDelta}, " +
             $"lotteryNumber={info.Transaction.LotteryNumber ?? "<none>"}, " +
             $"bet={FormatBet(info.Transaction.Bet)}, " +
             $"phase={info.Transaction.Phase}.");

    private void OnTradeFailed(TradeFailedEvent info) =>
        Warning(
            $"Trade failed: tx={info.Transaction.TransactionId}, " +
            $"kind={info.Transaction.Kind}, reason={info.Transaction.FailureReason ?? "<unknown>"}.");

    private void OnBetSettled(BetSettledEvent info) =>
        Info(
            $"Bet settled: result={info.Result}, ticket={info.TicketKey}, " +
            $"stake={info.Stake}, payout={info.Payout}, bet={FormatBet(info.Bet)}, " +
            $"tx={info.TransactionId}, source={info.Source}, reason={info.Reason}.");

    private void OnLotteryPrizeCashed(LotteryPrizeCashedEvent info)
    {
        Info(
            $"Lottery prize cashed: ticket={info.TicketKey}, " +
            $"ticketDelta={info.TicketDelta}, tx={info.TransactionId}, " +
            $"number={info.LotteryNumber ?? "<none>"}.");
        foreach (var reward in info.Rewards)
        {
            Info(
                $"Lottery reward: kind={reward.Kind}, " +
                $"item={reward.ItemKey?.ToString() ?? "<money>"}, delta={reward.Delta}.");
        }
    }

    private void OnShopGoodsObserved(ShopGoodsObservedEvent info)
    {
        var goods = info.Goods;
        Info(
            $"Shop goods observed: shop={goods.ShopKey}, item={goods.ItemKey}, " +
            $"price={goods.Price?.ToString() ?? "<unknown>"}, " +
            $"currency={goods.PriceCurrency}, stock={goods.Stock?.ToString() ?? "<unknown>"}, " +
            $"group={goods.Group ?? "<none>"}.");
    }

    private void OnItemUseBefore(ItemUseBeforeEvent info)
    {
        // Keep the cancellation test isolated to ExampleMod's own item so
        // normal game items remain usable during API verification.
        if (_cancelDebugTokenUse &&
            _debugItem is not null &&
            info.Context.ItemKey == _debugItem.Key)
        {
            info.Cancel("ExampleMod cancellation test: debug_token is blocked.");
        }

        Info(
            $"Item use before: itemKey={info.Context.ItemKey}, " +
            $"characterId={info.Context.CharacterId}, " +
            $"source={info.Context.Source}, requestedCount={info.Context.RequestedCount}, " +
            $"cancelled={info.IsCancelled}.");
    }

    private void OnItemUseAfter(ItemUseAfterEvent info) =>
        Info(
            $"Item use after: itemKey={info.Context.ItemKey}, " +
            $"source={info.Context.Source}, succeeded={info.Result.Succeeded}, " +
            $"cancelled={info.Result.Cancelled}, consumed={info.Result.ConsumedCount}, " +
            $"remaining={info.Result.RemainingCount?.ToString() ?? "<unknown>"}, " +
            $"reason={info.Result.FailureReason}, message={info.Result.Message ?? "<none>"}.");

    private void OnModRegistryReady(ModRegistryReadyEvent info) =>
        Info($"Mod registry ready: {info.Mods.Length} valid, {info.Rejected.Length} rejected.");

    private void OnModInitialized(ModInitializedEvent info) =>
        Info($"Mod initialized: {info.Mod.Manifest.Id} ({info.EntryType}).");

    private void OnModInitializationFailed(ModInitializationFailedEvent info) =>
        Warning($"Mod initialization failed: {info.ModId ?? "<unknown>"}: {info.Reason}");

    private static string FormatBet(TradeBetInfo? bet) =>
        bet is null
            ? "<none>"
            : $"stake={bet.Stake?.ToString() ?? "<none>"}," +
              $"payout={bet.Payout?.ToString() ?? "<none>"}," +
              $"won={bet.Won?.ToString() ?? "<none>"}," +
              $"target={bet.TargetName ?? "<none>"}," +
              $"odds={bet.Odds?.ToString() ?? "<none>"}";

    private void Info(string message) => _context?.Logger.Info($"[ExampleMod] {message}");

    private void Warning(string message) => _context?.Logger.Warning($"[ExampleMod] {message}");

    private sealed class DebugTokenItem : Item
    {
        public DebugTokenItem(string @namespace)
            : base(
                new ItemKey(@namespace, "debug_token"),
                "调试令牌",
                "custom",
                backgroundDescription: "由 ExampleMod 注册的虚拟物品。",
                maxUse: 1,
                resources: new ItemResources(
                    iconPath: "debug_token.png",
                    namePath: "debug_token.name.txt",
                    descriptionPath: "debug_token.description.txt"))
        {
        }
    }
}
