namespace BackToTheDawn.ModAPI;

/// <summary>
/// High-level category inferred from the game's semantic hook and settlement
/// metadata (including ThingChangeReason and ShopId).
/// </summary>
public enum TradeKind
{
    Unknown = 0,
    GenericPurchase = 1,
    GenericSale = 2,
    NpcBuy = 3,
    NpcSell = 4,
    NegotiatedTrade = 5,
    LunchPurchase = 6,
    GangShopPurchase = 7,
    PriestShopPurchase = 8,
    ViceCaptainPurchase = 9,
    GirlfriendShopPurchase = 10,
    RoofExchange = 11,
    TvShopping = 12,
    Gift = 13,
    GiftBack = 14,
    Production = 15,
    Lottery = 16,
    Betting = 17,
    BankDeposit = 18,
    BankWithdrawal = 19,
    BankLoan = 20,
    BankRepayment = 21,
    ServicePurchase = 22,
    FreeReceive = 23,
    SpecialExchange = 24,
    /// <summary>
    /// Purchase made through the yard vending machine (shop ID 9).
    /// The game currently reuses the ViceCaptain reason string for this path,
    /// so the semantic shop ID is the authoritative discriminator.
    /// </summary>
    VendingMachinePurchase = 25,
}

/// <summary>
/// Identifies which low-level settlement leg produced an observation.
/// A single in-game transaction can produce one item signal and one money
/// signal; the first version intentionally exposes both without pretending
/// that they are already correlated into one cancellable operation.
/// </summary>
public enum TradeLegKind
{
    Item = 0,
    Currency = 1,
    Combined = 2,
}

public enum TradeDirection
{
    Unknown = 0,
    PlayerBuys = 1,
    PlayerSells = 2,
    PlayerGives = 3,
    PlayerReceives = 4,
}

public enum TradeCurrencyKind
{
    None = 0,
    Money = 1,
    Discipline = 2,
    /// <summary>
    /// Relationship/affection points spent or received by a character
    /// interaction (for example Maggie's mail-order shop).
    /// </summary>
    Relationship = 3,
    /// <summary>
    /// Casino/lottery chips represented by the game's Chips item.
    /// </summary>
    Chips = 4,
    /// <summary>
    /// Gang contribution points represented by the game's gang resource.
    /// </summary>
    GangContribution = 5,
}

/// <summary>
/// Settlement phase for transactions that do not finish in the same UI
/// action. Gang orders and Maggie mail orders are emitted when placed, while
/// a later delivery observation is emitted with <see cref="Delivered"/>.
/// </summary>
public enum TradePhase
{
    Immediate = 0,
    OrderPlaced = 1,
    Delivered = 2,
    RelationshipAction = 3,
}

public enum TradeStatus
{
    Started = 0,
    Completed = 1,
    Failed = 2,
    Cancelled = 3,
}

/// <summary>
/// Optional betting metadata extracted from a boxing or match betting bill.
/// Values remain nullable because a bill can be observed before its result is
/// known, or because the game does not expose a matching detail object.
/// </summary>
public sealed record TradeBetInfo(
    int? Stake,
    int? Payout,
    bool? Won,
    string? TargetName,
    float? Odds);

public enum BetResult
{
    Unknown = 0,
    Won = 1,
    Lost = 2,
}

/// <summary>
/// Raised when a boxing bet is settled. The result is inferred from the
/// game's explicit payout or ticket-exchange reason, not from a timeout.
/// </summary>
public sealed record BetSettledEvent(
    BetResult Result,
    int CharacterId,
    ItemKey TicketKey,
    TradeBetInfo? Bet,
    int Stake,
    int Payout,
    long TransactionId,
    string Source,
    string Reason) : IGameEvent;

/// <summary>
/// Stable transaction envelope shared by the lifecycle events. A transaction
/// ID links the semantic method entry with its eventual settlement; it is
/// process-local and must not be persisted as a game save identifier.
/// </summary>
public sealed record TradeTransaction(
    long TransactionId,
    TradeKind Kind,
    TradeStatus Status,
    TradePhase Phase,
    int CharacterId,
    ItemKey? ItemKey,
    int RequestedCount,
    int ItemDelta,
    TradeCurrencyKind Currency,
    int CurrencyDelta,
    int DisciplineDelta,
    int RelationshipDelta,
    TradeDirection Direction,
    ShopKey? ShopKey,
    int? ShopId,
    int? CounterpartyId,
    string? CounterpartyName,
    string Source,
    string Reason,
    string? FailureReason = null,
    string? LotteryNumber = null,
    TradeBetInfo? Bet = null) : IGameEvent;

public sealed record TradeStartedEvent(TradeTransaction Transaction) : IGameEvent;

public sealed record TradeCompletedEvent(TradeTransaction Transaction) : IGameEvent;

public sealed record TradeFailedEvent(TradeTransaction Transaction) : IGameEvent;

/// <summary>
/// Read-only observation emitted after an item or money settlement call.
/// This event is not cancellable and must not be interpreted as permission
/// to call the underlying game APIs directly.
/// </summary>
public sealed record TradeDetectedEvent(
    long ObservationId,
    TradeKind Kind,
    TradeLegKind Leg,
    int CharacterId,
    ItemKey? ItemKey,
    int ItemDelta,
    TradeCurrencyKind Currency,
    int CurrencyDelta,
    int? ShopId,
    int? CounterpartyId,
    string Source,
    string Reason,
    string? CounterpartyName = null,
    TradeDirection Direction = TradeDirection.Unknown,
    int DisciplineDelta = 0,
    ShopKey? ShopKey = null,
    TradePhase Phase = TradePhase.Immediate,
    int RequestedCount = 0,
    int RelationshipDelta = 0,
    long TransactionId = 0,
    TradeStatus Status = TradeStatus.Completed,
    string? LotteryNumber = null,
    TradeBetInfo? Bet = null) : IGameEvent;
