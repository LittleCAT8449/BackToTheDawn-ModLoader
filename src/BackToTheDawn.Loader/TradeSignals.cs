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

    [ThreadStatic]
    private static Stack<SemanticTradeState>? _semanticTrades;

    // Some IL2CPP UI callbacks debit the resource in a preceding callback and
    // only then invoke the public submit method. Keep one short-lived,
    // reason-scoped settlement so the semantic order can still include it.
    private static PendingSettlement? _pendingSettlement;

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
        state.BeforeMoney = GameContext.Player?.Money ?? 0;
        state.BeforeDiscipline = GameContext.Player?.Discipline ?? 0;

        (_semanticTrades ??= new Stack<SemanticTradeState>()).Push(state);
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
        TradePhase phase = TradePhase.Immediate)
    {
        var itemKey = itemId > 0
            ? ItemCatalog.ResolveOrCreateKey(itemId)
            : (ItemKey?)null;
        var state = new SemanticTradeState(
            kind,
            itemKey,
            Math.Max(requestedCount, 0),
            direction,
            source,
            reason,
            (null, null),
            shopId,
            shopKey,
            phase);

        state.BeforeItemCount = itemKey is null
            ? 0
            : GameContext.Inventory?.GetCount(itemKey.Value) ?? 0;
        state.BeforeMoney = GameContext.Player?.Money ?? 0;
        state.BeforeDiscipline = GameContext.Player?.Discipline ?? 0;
        ConsumePendingSettlement(state, reason);

        (_semanticTrades ??= new Stack<SemanticTradeState>()).Push(state);
        return state;
    }

    internal static void EndNpcTrade(
        SemanticTradeState? state,
        bool succeeded = true)
    {
        if (state is null || state.Completed)
        {
            return;
        }

        state.Completed = true;
        var stack = _semanticTrades;
        if (stack is not null && stack.Count > 0)
        {
            if (ReferenceEquals(stack.Peek(), state))
            {
                stack.Pop();
            }
            else
            {
                var remaining = stack.Where(value => !ReferenceEquals(value, state)).Reverse().ToArray();
                stack.Clear();
                foreach (var value in remaining)
                {
                    stack.Push(value);
                }
            }
        }

        if (!succeeded || stack is not null && stack.Count > 0)
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
            var relationshipDelta = state.RelationshipDelta + state.ExternalRelationshipDelta;
            if (relationshipDelta == 0 && state.ExpectedRelationshipDelta != 0)
            {
                relationshipDelta = state.ExpectedRelationshipDelta;
            }

            if (itemDelta == 0 && moneyDelta == 0 && disciplineDelta == 0 &&
                relationshipDelta == 0 && state.Phase != TradePhase.OrderPlaced)
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
                            : TradeCurrencyKind.None,
                moneyDelta != 0
                    ? moneyDelta
                    : disciplineDelta != 0
                        ? disciplineDelta
                        : relationshipDelta,
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
                relationshipDelta);
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError($"[TradeHook] Failed to complete semantic trade: {exception}");
        }
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
        int relationshipDelta = 0)
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
                    relationshipDelta));
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError(
                $"[TradeHook] Failed to publish {kind} ({reason}): {exception}");
        }
    }

    private static bool IsSemanticTradeActive =>
        _semanticTrades is { Count: > 0 };

    internal static bool SemanticTradeActive => IsSemanticTradeActive;

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
        _semanticTrades?.Clear();
    }

    internal sealed class SemanticTradeState
    {
        internal SemanticTradeState(
            TradeKind kind,
            ItemKey? itemKey,
            int requestedCount,
            TradeDirection direction,
            string source,
            string reason,
            (int? Id, string? Name) counterparty,
            int? shopId = null,
            ShopKey? shopKey = null,
            TradePhase phase = TradePhase.Immediate)
        {
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
        }

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
        internal string? ObservedReason { get; set; }
        internal int BeforeItemCount { get; set; }
        internal int BeforeMoney { get; set; }
        internal int BeforeDiscipline { get; set; }
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
}
