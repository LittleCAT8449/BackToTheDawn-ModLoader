using BackToTheDawn.ModAPI;

namespace BackToTheDawn.Loader;

/// <summary>
/// Converts the game's low-level change reasons into stable public trade
/// categories. This first version observes settlement legs; it deliberately
/// does not cancel or mutate the original game operation.
/// </summary>
internal static class TradeSignals
{
    private static long _nextObservationId;
    private static long _nextTransactionId;

    [ThreadStatic]
    private static Stack<SemanticTradeState>? _semanticTrades;

    // Some IL2CPP UI callbacks debit the resource in a preceding callback and
    // only then invoke the public submit method. Keep one short-lived,
    // reason-scoped settlement so the semantic order can still include it.
    private static PendingSettlement? _pendingSettlement;

    // A few action methods add the bet/order item first and debit money in a
    // follow-up callback. Keep the semantic state alive until that callback
    // arrives so both legs share one TransactionId.
    private static DeferredSemanticTrade? _deferredSemanticTrade;

    // The ticket is the durable link between the initial bet and its later
    // payout or destruction. Keep only the protagonist's active boxing bet.
    private static ActiveBoxingBet? _activeBoxingBet;
    private static BettingTicketOperationState? _pendingBetTicketOperation;

    internal static void PublishItem(
        int characterId,
        ItemKey itemKey,
        int delta,
        string source,
        string reason)
    {
        if (IsSemanticTradeActive)
        {
            ObserveSemanticReason(reason);
            return;
        }

        // The game mirrors money into a pseudo inventory item. Money has its
        // own ChangeMoney signal, so exposing this mirror as an item leg would
        // make every purchase look like an extra item transaction.
        if (itemKey.Path.Equals("money", StringComparison.OrdinalIgnoreCase))
        {
            if (reason.Equals("BoxingBetWin", StringComparison.OrdinalIgnoreCase) &&
                delta > 0)
            {
                SettleBoxingBetWon(characterId, delta, source, reason);
            }

            return;
        }

        if (reason.Equals("ReceiveGirlFriendPackage", StringComparison.OrdinalIgnoreCase))
        {
            Publish(
                TradeKind.GirlfriendShopPurchase,
                TradeLegKind.Combined,
                characterId,
                itemKey,
                delta,
                TradeCurrencyKind.None,
                0,
                source,
                reason,
                direction: TradeDirection.PlayerReceives,
                shopId: 6,
                shopKey: new ShopKey("backtothedawn", "maggie_shop"),
                phase: TradePhase.Delivered,
                requestedCount: Math.Abs(delta));
            return;
        }

        if (!TryGetKind(reason, delta, out var kind))
        {
            return;
        }

        Publish(
            kind,
            TradeLegKind.Item,
            characterId,
            itemKey,
            delta,
            TradeCurrencyKind.None,
            0,
            source,
            reason);
    }

    internal static void PublishMoney(
        int characterId,
        int delta,
        string source,
        string reason)
    {
        if (TryCompleteDeferredTrade(characterId, delta))
        {
            return;
        }

        if (IsSemanticTradeActive)
        {
            ObserveSemanticReason(reason);
            return;
        }

        if (IsDeferredShopReason(reason))
        {
            _pendingSettlement = new PendingSettlement(
                characterId,
                TradeCurrencyKind.Money,
                delta,
                reason,
                DateTime.UtcNow);
            return;
        }

        if (reason.Equals("BuyLunch", StringComparison.OrdinalIgnoreCase))
        {
            Publish(
                TradeKind.LunchPurchase,
                TradeLegKind.Currency,
                characterId,
                null,
                0,
                TradeCurrencyKind.Money,
                delta,
                source,
                reason,
                direction: TradeDirection.PlayerBuys,
                shopId: 8,
                shopKey: new ShopKey("backtothedawn", "lunch_counter"));
            return;
        }

        if (!TryGetKind(reason, delta, out var kind))
        {
            return;
        }

        Publish(
            kind,
            TradeLegKind.Currency,
            characterId,
            null,
            0,
            TradeCurrencyKind.Money,
            delta,
            source,
            reason);
    }

    internal static void PublishRelationship(
        int characterId,
        int delta,
        string source,
        string reason)
    {
        if (IsSemanticTradeActive)
        {
            ObserveSemanticReason(reason);
            var state = _semanticTrades!.Peek();
            state.RelationshipDelta += delta;
            return;
        }

        if (IsDeferredShopReason(reason))
        {
            _pendingSettlement = new PendingSettlement(
                characterId,
                TradeCurrencyKind.Relationship,
                delta,
                reason,
                DateTime.UtcNow);
            return;
        }

        if (!TryGetKind(reason, delta, out var kind))
        {
            kind = TradeKind.Unknown;
        }

        Publish(
            kind,
            TradeLegKind.Currency,
            characterId,
            null,
            0,
            TradeCurrencyKind.Relationship,
            delta,
            source,
            reason,
            direction: delta < 0 ? TradeDirection.PlayerGives : TradeDirection.PlayerReceives,
            relationshipDelta: delta,
            phase: TradePhase.RelationshipAction);
    }

    internal static SemanticTradeState? BeginNpcTrade(
        TradeKind kind,
        CharacterAttribute? counterparty,
        int itemId,
        int requestedCount,
        TradeDirection direction,
        string source,
        string reason)
    {
        var player = CharacterManage.protagonistAttribute;
        var resolvedCounterparty = ResolveCounterparty(player, counterparty);
        if (resolvedCounterparty.Id is null)
        {
            return null;
        }

        var itemKey = itemId > 0
            ? ItemCatalog.ResolveOrCreateKey(itemId)
            : (ItemKey?)null;
        var state = new SemanticTradeState(
            Interlocked.Increment(ref _nextTransactionId),
            kind,
            itemKey,
            Math.Max(requestedCount, 0),
            direction,
            source,
            reason,
            resolvedCounterparty);

        state.BeforeItemCount = itemKey is null
            ? 0
            : GameContext.Inventory?.GetCount(itemKey.Value) ?? 0;
        CaptureCurrencyBaselines(state);
        state.BeforeMoney = GameContext.Player?.Money ?? 0;
        state.BeforeDiscipline = GameContext.Player?.Discipline ?? 0;

        (_semanticTrades ??= new Stack<SemanticTradeState>()).Push(state);
        RaiseTradeStarted(state);
        return state;
    }

    internal static SemanticTradeState BeginShopTrade(
        TradeKind kind,
        int? shopId,
        int itemId,
        int requestedCount,
        TradeDirection direction,
        string source,
        string reason,
        ShopKey? shopKey = null,
        TradePhase phase = TradePhase.Immediate,
        string? lotteryNumber = null)
    {
        var itemKey = itemId > 0
            ? ItemCatalog.ResolveOrCreateKey(itemId)
            : (ItemKey?)null;
        var state = new SemanticTradeState(
            Interlocked.Increment(ref _nextTransactionId),
            kind,
            itemKey,
            Math.Max(requestedCount, 0),
            direction,
            source,
            reason,
            (null, null),
            shopId,
            shopKey,
            phase,
            lotteryNumber);

        state.BeforeItemCount = itemKey is null
            ? 0
            : GameContext.Inventory?.GetCount(itemKey.Value) ?? 0;
        CaptureCurrencyBaselines(state);
        state.BeforeMoney = GameContext.Player?.Money ?? 0;
        state.BeforeDiscipline = GameContext.Player?.Discipline ?? 0;
        ConsumePendingSettlement(state, reason);

        (_semanticTrades ??= new Stack<SemanticTradeState>()).Push(state);
        RaiseTradeStarted(state);
        return state;
    }

    internal static void EndNpcTrade(
        SemanticTradeState? state,
        bool succeeded = true,
        string? failureReason = null)
    {
        if (state is null || state.Completed)
        {
            return;
        }

        state.Completed = true;
        var stack = RemoveSemanticTrade(state);

        if (!succeeded)
        {
            RaiseTradeFailed(state, failureReason ?? "Trade method threw an exception.");
            return;
        }

        if (stack is not null && stack.Count > 0)
        {
            return;
        }

        try
        {
            var afterItemCount = state.ItemKey is null
                ? state.BeforeItemCount
                : GameContext.Inventory?.GetCount(state.ItemKey.Value) ?? state.BeforeItemCount;
            var afterMoney = GameContext.Player?.Money ?? state.BeforeMoney;
            var afterDiscipline = GameContext.Player?.Discipline ?? state.BeforeDiscipline;
            var itemDelta = afterItemCount - state.BeforeItemCount;
            var moneyDelta = afterMoney - state.BeforeMoney + state.ExternalMoneyDelta;
            var disciplineDelta = afterDiscipline - state.BeforeDiscipline;
            var chipsDelta = ReadCurrencyCount(274) - state.BeforeChips;
            var gangContributionDelta = ReadCurrencyCount(1001) - state.BeforeGangContribution;
            var relationshipDelta = state.RelationshipDelta + state.ExternalRelationshipDelta;
            if (relationshipDelta == 0 && state.ExpectedRelationshipDelta != 0)
            {
                relationshipDelta = state.ExpectedRelationshipDelta;
            }

            if (itemDelta == 0 && moneyDelta == 0 && disciplineDelta == 0 &&
                relationshipDelta == 0 && chipsDelta == 0 &&
                gangContributionDelta == 0 && state.Phase != TradePhase.OrderPlaced)
            {
                return;
            }

            var kind = state.Kind;
            // The game reuses BuyViceCaptainShopGoods for the vending machine.
            // Shop ID 9 is stable in the game's shop configuration/resources,
            // therefore it must override the ambiguous ThingChangeReason.
            if (state.ShopId == 9)
            {
                kind = TradeKind.VendingMachinePurchase;
            }
            else if (kind == TradeKind.Unknown &&
                TryGetKind(
                    state.ObservedReason,
                    itemDelta != 0 ? itemDelta : moneyDelta != 0 ? moneyDelta : disciplineDelta,
                    out var observedKind))
            {
                kind = observedKind;
            }

            if (kind == TradeKind.Unknown)
            {
                kind = TradeKind.GenericPurchase;
            }

            Publish(
                kind,
                TradeLegKind.Combined,
                CharacterManage.protagonistAttribute?.id ?? 0,
                state.ItemKey,
                itemDelta,
                moneyDelta != 0
                    ? TradeCurrencyKind.Money
                    : disciplineDelta != 0
                        ? TradeCurrencyKind.Discipline
                        : relationshipDelta != 0
                            ? TradeCurrencyKind.Relationship
                            : chipsDelta != 0
                                ? TradeCurrencyKind.Chips
                                : gangContributionDelta != 0
                                    ? TradeCurrencyKind.GangContribution
                                    : TradeCurrencyKind.None,
                moneyDelta != 0
                    ? moneyDelta
                    : disciplineDelta != 0
                        ? disciplineDelta
                        : relationshipDelta != 0
                            ? relationshipDelta
                            : chipsDelta != 0
                                ? chipsDelta
                                : gangContributionDelta,
                state.Source,
                state.ObservedReason ?? state.Reason,
                state.CounterpartyId,
                state.CounterpartyName,
                state.Direction,
                state.ShopId,
                disciplineDelta,
                state.ShopKey,
                state.Phase,
                state.RequestedCount,
                relationshipDelta,
                state.TransactionId,
                TradeStatus.Completed,
                state.LotteryNumber,
                state.Bet);

            RaiseTradeCompleted(
                state,
                itemDelta,
                moneyDelta,
                disciplineDelta,
                relationshipDelta,
                chipsDelta,
                gangContributionDelta,
                kind,
                state.ObservedReason ?? state.Reason);

            RememberBoxingBet(state);
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError($"[TradeHook] Failed to complete semantic trade: {exception}");
        }
    }

    /// <summary>
    /// Keeps a semantic transaction open across a game callback that debits
    /// money after the original action method has returned.
    /// </summary>
    internal static void DeferNpcTrade(SemanticTradeState? state)
    {
        if (state is null || state.Completed)
        {
            return;
        }

        RemoveSemanticTrade(state);
        _deferredSemanticTrade = new DeferredSemanticTrade(state, DateTime.UtcNow);
    }

    private static bool TryCompleteDeferredTrade(int characterId, int delta)
    {
        var deferred = _deferredSemanticTrade;
        if (deferred is null)
        {
            return false;
        }

        if (DateTime.UtcNow - deferred.Timestamp > TimeSpan.FromSeconds(2))
        {
            _deferredSemanticTrade = null;
            EndNpcTrade(deferred.State);
            return false;
        }

        if (characterId != CharacterManage.protagonistAttribute?.id || delta == 0)
        {
            return false;
        }

        _deferredSemanticTrade = null;
        EndNpcTrade(deferred.State);
        return true;
    }

    private static Stack<SemanticTradeState>? RemoveSemanticTrade(
        SemanticTradeState state)
    {
        var stack = _semanticTrades;
        if (stack is null || stack.Count == 0)
        {
            return stack;
        }

        if (ReferenceEquals(stack.Peek(), state))
        {
            stack.Pop();
            return stack;
        }

        var remaining = stack
            .Where(value => !ReferenceEquals(value, state))
            .Reverse()
            .ToArray();
        stack.Clear();
        foreach (var value in remaining)
        {
            stack.Push(value);
        }

        return stack;
    }

    private static void Publish(
        TradeKind kind,
        TradeLegKind leg,
        int characterId,
        ItemKey? itemKey,
        int itemDelta,
        TradeCurrencyKind currency,
        int currencyDelta,
        string source,
        string reason,
        int? counterpartyId = null,
        string? counterpartyName = null,
        TradeDirection direction = TradeDirection.Unknown,
        int? shopId = null,
        int disciplineDelta = 0,
        ShopKey? shopKey = null,
        TradePhase phase = TradePhase.Immediate,
        int requestedCount = 0,
        int relationshipDelta = 0,
        long transactionId = 0,
        TradeStatus status = TradeStatus.Completed,
        string? lotteryNumber = null,
        TradeBetInfo? bet = null)
    {
        if (!GameContextAdapter.IsGameplayReady || characterId == 0)
        {
            return;
        }

        try
        {
            var protagonist = CharacterManage.protagonistAttribute;
            if (protagonist is null || protagonist.id != characterId)
            {
                return;
            }

            var resolvedShopKey = shopKey;
            if (resolvedShopKey is null && shopId.HasValue &&
                ShopCatalog.TryGetByNativeId(shopId.Value, out var shopDescriptor))
            {
                resolvedShopKey = shopDescriptor.Key;
            }

            GameEvents.RaiseTradeDetected(
                new TradeDetectedEvent(
                    Interlocked.Increment(ref _nextObservationId),
                    kind,
                    leg,
                    characterId,
                    itemKey,
                    itemDelta,
                    currency,
                    currencyDelta,
                    shopId,
                    counterpartyId,
                    source,
                    reason,
                    counterpartyName,
                    direction,
                    disciplineDelta,
                    resolvedShopKey,
                    phase,
                    requestedCount,
                    relationshipDelta,
                    transactionId,
                    status,
                    lotteryNumber,
                    bet));
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError(
                $"[TradeHook] Failed to publish {kind} ({reason}): {exception}");
        }
    }

    private static void RaiseTradeStarted(SemanticTradeState state)
    {
        var characterId = CharacterManage.protagonistAttribute?.id ?? 0;
        if (!GameContextAdapter.IsGameplayReady || characterId == 0)
        {
            return;
        }

        try
        {
            GameEvents.RaiseTradeStarted(
                CreateTransaction(
                    state,
                    TradeStatus.Started,
                    state.Kind,
                    TradeCurrencyKind.None,
                    0,
                    0,
                    0,
                    0,
                    0,
                    state.Reason));
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError(
                $"[TradeHook] Failed to publish transaction start {state.TransactionId}: {exception}");
        }
    }

    private static void RaiseTradeCompleted(
        SemanticTradeState state,
        int itemDelta,
        int moneyDelta,
        int disciplineDelta,
        int relationshipDelta,
        int chipsDelta,
        int gangContributionDelta,
        TradeKind kind,
        string reason)
    {
        var currency = ResolveCurrency(
            moneyDelta,
            disciplineDelta,
            relationshipDelta,
            chipsDelta,
            gangContributionDelta);
        var currencyDelta = currency switch
        {
            TradeCurrencyKind.Money => moneyDelta,
            TradeCurrencyKind.Discipline => disciplineDelta,
            TradeCurrencyKind.Relationship => relationshipDelta,
            TradeCurrencyKind.Chips => chipsDelta,
            TradeCurrencyKind.GangContribution => gangContributionDelta,
            _ => 0,
        };

        var characterId = CharacterManage.protagonistAttribute?.id ?? 0;
        if (!GameContextAdapter.IsGameplayReady || characterId == 0)
        {
            return;
        }

        try
        {
            GameEvents.RaiseTradeCompleted(
                CreateTransaction(
                    state,
                    TradeStatus.Completed,
                    kind,
                    currency,
                    currencyDelta,
                    itemDelta,
                    moneyDelta,
                    disciplineDelta,
                    relationshipDelta,
                    reason));
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError(
                $"[TradeHook] Failed to publish transaction completion {state.TransactionId}: {exception}");
        }
    }

    private static void RaiseTradeFailed(
        SemanticTradeState state,
        string failureReason)
    {
        var characterId = CharacterManage.protagonistAttribute?.id ?? 0;
        if (!GameContextAdapter.IsGameplayReady || characterId == 0)
        {
            return;
        }

        try
        {
            GameEvents.RaiseTradeFailed(
                CreateTransaction(
                    state,
                    TradeStatus.Failed,
                    state.Kind,
                    TradeCurrencyKind.None,
                    0,
                    0,
                    0,
                    0,
                    0,
                    state.ObservedReason ?? state.Reason,
                    failureReason));
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError(
                $"[TradeHook] Failed to publish transaction failure {state.TransactionId}: {exception}");
        }
    }

    private static TradeTransaction CreateTransaction(
        SemanticTradeState state,
        TradeStatus status,
        TradeKind kind,
        TradeCurrencyKind currency,
        int currencyDelta,
        int itemDelta,
        int moneyDelta,
        int disciplineDelta,
        int relationshipDelta,
        string reason,
        string? failureReason = null)
    {
        var shopKey = state.ShopKey;
        if (shopKey is null && state.ShopId.HasValue &&
            ShopCatalog.TryGetByNativeId(state.ShopId.Value, out var descriptor))
        {
            shopKey = descriptor.Key;
        }

        return new TradeTransaction(
            state.TransactionId,
            kind,
            status,
            state.Phase,
            CharacterManage.protagonistAttribute?.id ?? 0,
            state.ItemKey,
            state.RequestedCount,
            itemDelta,
            currency,
            currencyDelta,
            disciplineDelta,
            relationshipDelta,
            state.Direction,
            shopKey,
            state.ShopId,
            state.CounterpartyId,
            state.CounterpartyName,
            state.Source,
            reason,
            failureReason,
            state.LotteryNumber,
            state.Bet);
    }

    private static TradeCurrencyKind ResolveCurrency(
        int moneyDelta,
        int disciplineDelta,
        int relationshipDelta,
        int chipsDelta,
        int gangContributionDelta) =>
        moneyDelta != 0
            ? TradeCurrencyKind.Money
            : disciplineDelta != 0
                ? TradeCurrencyKind.Discipline
                : relationshipDelta != 0
                    ? TradeCurrencyKind.Relationship
                    : chipsDelta != 0
                        ? TradeCurrencyKind.Chips
                        : gangContributionDelta != 0
                            ? TradeCurrencyKind.GangContribution
                            : TradeCurrencyKind.None;

    private static void CaptureCurrencyBaselines(SemanticTradeState state)
    {
        state.BeforeChips = ReadCurrencyCount(274);
        state.BeforeGangContribution = ReadCurrencyCount(1001);
    }

    private static int ReadCurrencyCount(int nativeItemId)
    {
        try
        {
            var inventory = GameContext.Inventory;
            if (inventory is null)
            {
                return 0;
            }

            var key = ItemCatalog.ResolveOrCreateKey(nativeItemId);
            return inventory.GetCount(key);
        }
        catch
        {
            return 0;
        }
    }

    private static bool IsSemanticTradeActive =>
        _semanticTrades is { Count: > 0 };

        internal static bool SemanticTradeActive => IsSemanticTradeActive;

    internal static void ObserveBoxingBet(
        SemanticTradeState? state,
        Thing? bill)
    {
        if (state is null || bill is null)
        {
            return;
        }

        try
        {
            var detail = bill.boxingBill;
            if (detail is null)
            {
                return;
            }

            state.Bet = new TradeBetInfo(
                detail.GetBetMoney(),
                detail.GetWinMoney(),
                detail.IsBetWin(),
                detail.GetBetCharacterName(),
                detail.GetRate());
        }
        catch
        {
            // Some save states do not hydrate the bill detail until the UI
            // opens. The ordinary item/currency transaction remains valid.
        }
    }

    internal static void BeginBetTicketExchange(
        Thing? thing,
        int reduceCount,
        string reason)
    {
        if (thing is null ||
            thing.id != 261 ||
            !reason.Equals("BoxingBetExchange", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        TradeBetInfo? bet = null;
        try
        {
            var detail = thing.boxingBill;
            if (detail is not null)
            {
                bet = new TradeBetInfo(
                    detail.GetBetMoney(),
                    detail.GetWinMoney(),
                    detail.IsBetWin(),
                    detail.GetBetCharacterName(),
                    detail.GetRate());
            }
        }
        catch
        {
            // Fall back to the metadata captured when the ticket was created.
        }

        var active = _activeBoxingBet;
        _pendingBetTicketOperation = new BettingTicketOperationState(
            CharacterManage.protagonistAttribute?.id ?? 0,
            ItemCatalog.ResolveOrCreateKey(261),
            Math.Max(reduceCount, 1),
            bet ?? active?.Bet,
            active?.TransactionId ?? 0,
            nameof(ThingPackage.ReduceThingCount),
            reason);
    }

    internal static void CompleteBetTicketExchange(bool succeeded)
    {
        var state = _pendingBetTicketOperation;
        _pendingBetTicketOperation = null;
        if (state is null || !succeeded || state.CharacterId == 0)
        {
            return;
        }

        var active = _activeBoxingBet;
        if (active?.Result is BetResult.Won or BetResult.Lost)
        {
            _activeBoxingBet = null;
            return;
        }

        var bet = NormalizeBet(state.Bet, BetResult.Lost, 0);
        GameEvents.RaiseBetSettled(
            new BetSettledEvent(
                BetResult.Lost,
                state.CharacterId,
                state.TicketKey,
                bet,
                bet?.Stake ?? 0,
                0,
                state.TransactionId,
                state.Source,
                state.Reason));
        _activeBoxingBet = null;
    }

    private static void SettleBoxingBetWon(
        int characterId,
        int payout,
        string source,
        string reason)
    {
        if (characterId == 0 || payout <= 0)
        {
            return;
        }

        var active = _activeBoxingBet;
        if (active?.Result is BetResult.Won or BetResult.Lost)
        {
            return;
        }

        var bet = NormalizeBet(active?.Bet, BetResult.Won, payout);
        GameEvents.RaiseBetSettled(
            new BetSettledEvent(
                BetResult.Won,
                characterId,
                ItemCatalog.ResolveOrCreateKey(261),
                bet,
                bet?.Stake ?? 0,
                payout,
                active?.TransactionId ?? 0,
                source,
                reason));

        if (active is not null)
        {
            active.Result = BetResult.Won;
        }
    }

    private static void RememberBoxingBet(SemanticTradeState state)
    {
        if (state.Bet is null ||
            state.ItemKey?.Path.Equals("boxing_bet_bill", StringComparison.OrdinalIgnoreCase) != true)
        {
            return;
        }

        _activeBoxingBet = new ActiveBoxingBet(
            state.TransactionId,
            state.Bet,
            BetResult.Unknown);
    }

    private static TradeBetInfo? NormalizeBet(
        TradeBetInfo? bet,
        BetResult result,
        int payout)
    {
        if (bet is null)
        {
            return null;
        }

        return bet with
        {
            Payout = payout,
            Won = result == BetResult.Won,
        };
    }

    internal static void ObserveSemanticRelationshipCost(int cost)
    {
        if (!IsSemanticTradeActive || cost <= 0)
        {
            return;
        }

        var state = _semanticTrades!.Peek();
        if (state.Kind == TradeKind.GirlfriendShopPurchase)
        {
            state.ExpectedRelationshipDelta = -Math.Abs(cost);
        }
    }

    private static bool IsDeferredShopReason(string? reason) =>
        !string.IsNullOrWhiteSpace(reason) &&
        (reason.Contains("GangShop", StringComparison.OrdinalIgnoreCase) ||
         reason.Contains("GirlFriendShop", StringComparison.OrdinalIgnoreCase) ||
         reason.Contains("GirlFriendMail", StringComparison.OrdinalIgnoreCase) ||
         reason.Contains("Maggie", StringComparison.OrdinalIgnoreCase) ||
         reason.Contains("Maji", StringComparison.OrdinalIgnoreCase));

    private static void ConsumePendingSettlement(
        SemanticTradeState state,
        string semanticReason)
    {
        var pending = _pendingSettlement;
        if (pending is null ||
            pending.CharacterId != CharacterManage.protagonistAttribute?.id ||
            DateTime.UtcNow - pending.Timestamp > TimeSpan.FromSeconds(2) ||
            !IsDeferredShopReason(pending.Reason))
        {
            return;
        }

        _pendingSettlement = null;
        if (pending.Currency == TradeCurrencyKind.Money)
        {
            state.ExternalMoneyDelta += pending.Delta;
        }
        else if (pending.Currency == TradeCurrencyKind.Relationship)
        {
            state.ExternalRelationshipDelta += pending.Delta;
        }

        if (string.IsNullOrWhiteSpace(state.ObservedReason))
        {
            state.ObservedReason = pending.Reason;
        }
    }

    private static void ObserveSemanticReason(string? reason)
    {
        if (_semanticTrades is not { Count: > 0 } || string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        var state = _semanticTrades.Peek();
        if (string.IsNullOrWhiteSpace(state.ObservedReason))
        {
            state.ObservedReason = reason;
        }
    }

    private static (int? Id, string? Name) ResolveCounterparty(
        CharacterAttribute? player,
        CharacterAttribute? candidate)
    {
        if (candidate is null || player is null || candidate.id == player.id)
        {
            return (null, null);
        }

        try
        {
            if (!candidate.IsNPC() && !candidate.IsPrisoner())
            {
                return (null, null);
            }

            var name = candidate.GetFullName();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = candidate.GetName();
            }

            return (candidate.id, name);
        }
        catch
        {
            return (candidate.id, null);
        }
    }

    private static bool TryGetKind(
        string? reason,
        int delta,
        out TradeKind kind)
    {
        switch (reason?.Trim())
        {
            case "BuyFromOtherPrisoner":
                kind = TradeKind.NpcBuy;
                return true;
            case "Sell":
                kind = TradeKind.GenericSale;
                return true;
            case "Buy":
                kind = TradeKind.GenericPurchase;
                return true;
            case "Bargain":
                kind = TradeKind.NegotiatedTrade;
                return true;
            case "BuyLunch":
                kind = TradeKind.LunchPurchase;
                return true;
            case "BuyGangShopGoods":
                kind = TradeKind.GangShopPurchase;
                return true;
            case "BuyPriestShopGoods":
                kind = TradeKind.PriestShopPurchase;
                return true;
            case "BuyViceCaptainShopGoods":
            case "BuyViceCaptainMailRoomPass":
                kind = TradeKind.ViceCaptainPurchase;
                return true;
            case "TVShopping":
                kind = TradeKind.TvShopping;
                return true;
            case "GiveGift":
                kind = TradeKind.Gift;
                return true;
            case "GiftBack":
                kind = TradeKind.GiftBack;
                return true;
            case "Produce":
            case "CookingRoomGame":
                kind = TradeKind.Production;
                return true;
            case "BuyLotteryTicket":
            case "PrizeCashed":
                kind = TradeKind.Lottery;
                return true;
            case "BoxingBet":
            case "BoxingBetExchange":
            case "MatchBet":
            case "BetInGambleRound":
            case "ExchangeChips":
                kind = TradeKind.Betting;
                return true;
            case "BankDeposit":
                kind = TradeKind.BankDeposit;
                return true;
            case "BankTakeDepositMoney":
                kind = TradeKind.BankWithdrawal;
                return true;
            case "BankLoan":
                kind = TradeKind.BankLoan;
                return true;
            case "BankClearLoan":
            case "BankLoanOverdue":
                kind = TradeKind.BankRepayment;
                return true;
            case "BuyCertificate":
            case "BuyDiscipline":
            case "BuySZZ":
            case "BuyApplication":
                kind = TradeKind.ServicePurchase;
                return true;
            case "ReceiveGirlFriendPackage":
            case "ReceiveWeekFreeGoods":
                kind = TradeKind.FreeReceive;
                return true;
            case "ExchangeMoney":
            case "ExachangeColdBeer":
            case "ColdBeerBackToBeer":
            case "ChangeGameCard":
                kind = TradeKind.SpecialExchange;
                return true;
            case "BuyWaffle":
            case "OrderPizza":
                kind = TradeKind.GenericPurchase;
                return true;
            case "GiveMedicine":
            case "GiveMedicineFailed":
                kind = TradeKind.Gift;
                return true;
            default:
                kind = TradeKind.Unknown;
                return false;
        }
    }

    internal static void Reset()
    {
        _nextObservationId = 0;
        _nextTransactionId = 0;
        _semanticTrades?.Clear();
        _pendingSettlement = null;
        _deferredSemanticTrade = null;
        _activeBoxingBet = null;
        _pendingBetTicketOperation = null;
    }

    internal sealed class SemanticTradeState
    {
        internal SemanticTradeState(
            long transactionId,
            TradeKind kind,
            ItemKey? itemKey,
            int requestedCount,
            TradeDirection direction,
            string source,
            string reason,
            (int? Id, string? Name) counterparty,
            int? shopId = null,
            ShopKey? shopKey = null,
            TradePhase phase = TradePhase.Immediate,
            string? lotteryNumber = null)
        {
            TransactionId = transactionId;
            Kind = kind;
            ItemKey = itemKey;
            RequestedCount = requestedCount;
            Direction = direction;
            Source = source;
            Reason = reason;
            CounterpartyId = counterparty.Id;
            CounterpartyName = counterparty.Name;
            ShopId = shopId;
            ShopKey = shopKey;
            Phase = phase;
            LotteryNumber = lotteryNumber;
        }

        internal long TransactionId { get; }
        internal TradeKind Kind { get; }
        internal ItemKey? ItemKey { get; }
        internal int RequestedCount { get; }
        internal TradeDirection Direction { get; }
        internal string Source { get; }
        internal string Reason { get; }
        internal int? CounterpartyId { get; }
        internal string? CounterpartyName { get; }
        internal int? ShopId { get; }
        internal ShopKey? ShopKey { get; }
        internal TradePhase Phase { get; }
        internal string? LotteryNumber { get; }
        internal TradeBetInfo? Bet { get; set; }
        internal string? ObservedReason { get; set; }
        internal int BeforeItemCount { get; set; }
        internal int BeforeMoney { get; set; }
        internal int BeforeDiscipline { get; set; }
        internal int BeforeChips { get; set; }
        internal int BeforeGangContribution { get; set; }
        internal int RelationshipDelta { get; set; }
        internal int ExternalMoneyDelta { get; set; }
        internal int ExternalRelationshipDelta { get; set; }
        internal int ExpectedRelationshipDelta { get; set; }
        internal bool Completed { get; set; }
    }

    private sealed record PendingSettlement(
        int CharacterId,
        TradeCurrencyKind Currency,
        int Delta,
        string Reason,
        DateTime Timestamp);

    private sealed class ActiveBoxingBet
    {
        internal ActiveBoxingBet(
            long transactionId,
            TradeBetInfo bet,
            BetResult result)
        {
            TransactionId = transactionId;
            Bet = bet;
            Result = result;
        }

        internal long TransactionId { get; }
        internal TradeBetInfo Bet { get; }
        internal BetResult Result { get; set; }
    }

    internal sealed record BettingTicketOperationState(
        int CharacterId,
        ItemKey TicketKey,
        int Count,
        TradeBetInfo? Bet,
        long TransactionId,
        string Source,
        string Reason);

    private sealed record DeferredSemanticTrade(
        SemanticTradeState State,
        DateTime Timestamp);
}
