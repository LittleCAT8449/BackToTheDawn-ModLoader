using BackToTheDawn.ModAPI;
using HarmonyLib;

namespace BackToTheDawn.Loader;

// These hooks cover transaction entry points that are not ordinary ShopGoods
// UI callbacks. They intentionally observe the same before/after state as the
// existing shop hooks; no game method is replaced or short-circuited.

[HarmonyPatch(typeof(TVShopping), nameof(TVShopping.BuyOneGoods))]
internal static class TvShoppingBuyPatch
{
    private static void Prefix(
        int order,
        out TradeSignals.SemanticTradeState __state)
    {
        __state = TradeSignals.BeginShopTrade(
            TradeKind.TvShopping,
            15,
            0,
            1,
            TradeDirection.PlayerBuys,
            nameof(TVShopping.BuyOneGoods),
            $"TVShopping(order={order})",
            new ShopKey("backtothedawn", "tv_shopping"),
            TradePhase.OrderPlaced);
    }

    private static void Postfix(TradeSignals.SemanticTradeState __state) =>
        TradeSignals.EndNpcTrade(__state);

    private static Exception? Finalizer(
        TradeSignals.SemanticTradeState __state,
        Exception? __exception)
    {
        if (__exception is not null)
        {
            TradeSignals.EndNpcTrade(__state, succeeded: false, failureReason: __exception.Message);
        }

        return __exception;
    }
}

[HarmonyPatch(typeof(UI_BuyLotteryTickets), nameof(UI_BuyLotteryTickets.AddItemLotteryTicket))]
internal static class LotteryTicketSubmitPatch
{
    private static void Prefix(
        UI_BuyLotteryTickets __instance,
        int currentIssue,
        out TradeSignals.SemanticTradeState __state)
    {
        var shopId = __instance.ShopId != 0 ? __instance.ShopId : 14;
        __state = TradeSignals.BeginShopTrade(
            TradeKind.Lottery,
            shopId,
            265,
            1,
            TradeDirection.PlayerBuys,
            nameof(UI_BuyLotteryTickets.AddItemLotteryTicket),
            $"BuyLotteryTicket(issue={currentIssue},number={__instance.lotteryTicketNumber})",
            new ShopKey("backtothedawn", "lottery"),
            lotteryNumber: __instance.lotteryTicketNumber);
    }

    private static void Postfix(TradeSignals.SemanticTradeState __state) =>
        TradeSignals.EndNpcTrade(__state);

    private static Exception? Finalizer(
        TradeSignals.SemanticTradeState __state,
        Exception? __exception)
    {
        if (__exception is not null)
        {
            TradeSignals.EndNpcTrade(__state, succeeded: false, failureReason: __exception.Message);
        }

        return __exception;
    }
}

[HarmonyPatch(typeof(ActionBoxingBet), nameof(ActionBoxingBet.BetBoxingMatchByActionId))]
internal static class BoxingBetPatch
{
    private static void Prefix(
        int actionId,
        out TradeSignals.SemanticTradeState __state)
    {
        __state = TradeSignals.BeginShopTrade(
            TradeKind.Betting,
            null,
            261,
            1,
            TradeDirection.PlayerGives,
            nameof(ActionBoxingBet.BetBoxingMatchByActionId),
            $"BoxingBet(actionId={actionId})",
            new ShopKey("backtothedawn", "boxing_betting"));
    }

    private static void Postfix(TradeSignals.SemanticTradeState __state) =>
        TradeSignals.EndNpcTrade(__state);

    private static Exception? Finalizer(
        TradeSignals.SemanticTradeState __state,
        Exception? __exception)
    {
        if (__exception is not null)
        {
            TradeSignals.EndNpcTrade(__state, succeeded: false, failureReason: __exception.Message);
        }

        return __exception;
    }
}

[HarmonyPatch(typeof(ActionBoxingBet_ExchangePast), nameof(ActionBoxingBet_ExchangePast.DoExchangePassBill))]
internal static class BoxingBetExchangePatch
{
    private static void Prefix(out TradeSignals.SemanticTradeState __state) =>
        __state = TradeSignals.BeginShopTrade(
            TradeKind.Betting,
            null,
            261,
            1,
            TradeDirection.PlayerReceives,
            nameof(ActionBoxingBet_ExchangePast.DoExchangePassBill),
            "BoxingBetExchange",
            new ShopKey("backtothedawn", "boxing_betting"));

    private static void Postfix(TradeSignals.SemanticTradeState __state) =>
        TradeSignals.EndNpcTrade(__state);

    private static Exception? Finalizer(
        TradeSignals.SemanticTradeState __state,
        Exception? __exception)
    {
        if (__exception is not null)
        {
            TradeSignals.EndNpcTrade(__state, succeeded: false, failureReason: __exception.Message);
        }

        return __exception;
    }
}

[HarmonyPatch(typeof(UI_MatchBet), nameof(UI_MatchBet.OnClickSubmit))]
internal static class MatchBetSubmitPatch
{
    private static void Prefix(
        UI_MatchBet __instance,
        out TradeSignals.SemanticTradeState __state) =>
        __state = TradeSignals.BeginShopTrade(
            TradeKind.Betting,
            null,
            0,
            Math.Max(__instance.selectCount, 1),
            TradeDirection.PlayerGives,
            nameof(UI_MatchBet.OnClickSubmit),
            $"MatchBet(side={__instance.betSide},count={__instance.selectCount})",
            new ShopKey("backtothedawn", "match_betting"));

    private static void Postfix(TradeSignals.SemanticTradeState __state) =>
        TradeSignals.EndNpcTrade(__state);

    private static Exception? Finalizer(
        TradeSignals.SemanticTradeState __state,
        Exception? __exception)
    {
        if (__exception is not null)
        {
            TradeSignals.EndNpcTrade(__state, succeeded: false, failureReason: __exception.Message);
        }

        return __exception;
    }
}

[HarmonyPatch(typeof(StorageBankInfo), nameof(StorageBankInfo.ApplyDeposit))]
internal static class BankDepositPatch
{
    private static void Prefix(
        int depositMoney,
        out TradeSignals.SemanticTradeState __state) =>
        __state = TradeSignals.BeginShopTrade(
            TradeKind.BankDeposit,
            null,
            0,
            Math.Max(depositMoney, 0),
            TradeDirection.PlayerGives,
            nameof(StorageBankInfo.ApplyDeposit),
            "BankDeposit",
            new ShopKey("backtothedawn", "bank"));

    private static void Postfix(TradeSignals.SemanticTradeState __state) =>
        TradeSignals.EndNpcTrade(__state);

    private static Exception? Finalizer(
        TradeSignals.SemanticTradeState __state,
        Exception? __exception)
    {
        if (__exception is not null)
        {
            TradeSignals.EndNpcTrade(__state, succeeded: false, failureReason: __exception.Message);
        }

        return __exception;
    }
}

[HarmonyPatch(typeof(StorageBankInfo), nameof(StorageBankInfo.ApplyLoan))]
internal static class BankLoanPatch
{
    private static void Prefix(
        int loanMoney,
        out TradeSignals.SemanticTradeState __state) =>
        __state = TradeSignals.BeginShopTrade(
            TradeKind.BankLoan,
            null,
            0,
            Math.Max(loanMoney, 0),
            TradeDirection.PlayerReceives,
            nameof(StorageBankInfo.ApplyLoan),
            "BankLoan",
            new ShopKey("backtothedawn", "bank"));

    private static void Postfix(TradeSignals.SemanticTradeState __state) =>
        TradeSignals.EndNpcTrade(__state);

    private static Exception? Finalizer(
        TradeSignals.SemanticTradeState __state,
        Exception? __exception)
    {
        if (__exception is not null)
        {
            TradeSignals.EndNpcTrade(__state, succeeded: false, failureReason: __exception.Message);
        }

        return __exception;
    }
}

[HarmonyPatch(typeof(StorageBankInfo), nameof(StorageBankInfo.ApplyClearLoan))]
internal static class BankRepaymentPatch
{
    private static void Prefix(out TradeSignals.SemanticTradeState __state) =>
        __state = TradeSignals.BeginShopTrade(
            TradeKind.BankRepayment,
            null,
            0,
            1,
            TradeDirection.PlayerGives,
            nameof(StorageBankInfo.ApplyClearLoan),
            "BankClearLoan",
            new ShopKey("backtothedawn", "bank"));

    private static void Postfix(TradeSignals.SemanticTradeState __state) =>
        TradeSignals.EndNpcTrade(__state);

    private static Exception? Finalizer(
        TradeSignals.SemanticTradeState __state,
        Exception? __exception)
    {
        if (__exception is not null)
        {
            TradeSignals.EndNpcTrade(__state, succeeded: false, failureReason: __exception.Message);
        }

        return __exception;
    }
}

[HarmonyPatch(typeof(ActionBank), nameof(ActionBank.TakeDepositMoney))]
internal static class BankWithdrawalPatch
{
    private static void Prefix(out TradeSignals.SemanticTradeState __state) =>
        __state = TradeSignals.BeginShopTrade(
            TradeKind.BankWithdrawal,
            null,
            0,
            1,
            TradeDirection.PlayerReceives,
            nameof(ActionBank.TakeDepositMoney),
            "BankTakeDepositMoney",
            new ShopKey("backtothedawn", "bank"));

    private static void Postfix(TradeSignals.SemanticTradeState __state) =>
        TradeSignals.EndNpcTrade(__state);

    private static Exception? Finalizer(
        TradeSignals.SemanticTradeState __state,
        Exception? __exception)
    {
        if (__exception is not null)
        {
            TradeSignals.EndNpcTrade(__state, succeeded: false, failureReason: __exception.Message);
        }

        return __exception;
    }
}

[HarmonyPatch(typeof(UI_OrderPizza), nameof(UI_OrderPizza.OrderPizza))]
internal static class PizzaOrderPatch
{
    private static void Prefix(
        UI_OrderPizza __instance,
        out TradeSignals.SemanticTradeState __state) =>
        __state = TradeSignals.BeginShopTrade(
            TradeKind.GenericPurchase,
            __instance.ShopId == 0 ? 13 : __instance.ShopId,
            __instance.goodId,
            Math.Max(__instance.orderCount, 1),
            TradeDirection.PlayerBuys,
            nameof(UI_OrderPizza.OrderPizza),
            "OrderPizza",
            new ShopKey("backtothedawn", "big_bang_pizza"),
            TradePhase.OrderPlaced);

    private static void Postfix(TradeSignals.SemanticTradeState __state) =>
        TradeSignals.EndNpcTrade(__state);

    private static Exception? Finalizer(
        TradeSignals.SemanticTradeState __state,
        Exception? __exception)
    {
        if (__exception is not null)
        {
            TradeSignals.EndNpcTrade(__state, succeeded: false, failureReason: __exception.Message);
        }

        return __exception;
    }
}
