using HarmonyLib;
using BackToTheDawn.ModAPI;

namespace BackToTheDawn.Loader;

[HarmonyPatch(typeof(ConfigData), nameof(ConfigData.InitConfig))]
internal static class TaskConfigInitializedPatch
{
    private static void Postfix() => TaskRuntime.OnConfigInitialized();
}

[HarmonyPatch(typeof(GameManage), nameof(GameManage.ShowGameStartUI))]
internal static class ShowGameStartUiPatch
{
    private static void Prefix(bool isShowSelectInputModel, int immediateStartArchiveId)
    {
        GameContextAdapter.IsGameplayReady = false;
        TaskEventMonitor.Reset();
        if (immediateStartArchiveId == 0)
        {
            GameEvents.RaiseMainMenuEntered(isShowSelectInputModel, immediateStartArchiveId);
        }
        else
        {
            Plugin.Logger?.LogInfo(
                $"[LifecycleHook] Suppressed MainMenuEntered for immediate archive " +
                $"startup: archiveId={immediateStartArchiveId}.");
        }
    }
}

[HarmonyPatch(typeof(GameManage), nameof(GameManage.ReadArchiveDataAndStartGame))]
internal static class ReadArchiveDataAndStartGamePatch
{
    private static void Prefix(int archiveId)
    {
        // Runtime room clones are session objects, not archive resources.
        // Dispose them before MapManage.LoadWholeMap enumerates native maps.
        RoomCloneRuntime.Reset();
        GameContextAdapter.IsGameplayReady = false;
        TaskEventMonitor.Reset();
        GameEvents.RaiseArchiveLoadStarted(archiveId);
    }

    private static void Postfix(int archiveId)
    {
        GameEvents.RaiseArchiveLoadInvocationReturned(archiveId);
    }
}

[HarmonyPatch(typeof(GameManage), nameof(GameManage.StartGameGoToNextStep))]
internal static class StartGameGoToNextStepPatch
{
    private static void Postfix(GameManage __instance)
    {
        GameEvents.RaiseStartupStepChanged(__instance.gameStartStep);
    }
}

[HarmonyPatch(typeof(GameManage), nameof(GameManage.ShowCurrentMapAndCanControl))]
internal static class ShowCurrentMapAndCanControlPatch
{
    private static void Postfix()
    {
        GameContextAdapter.IsGameplayReady = true;
        TaskRuntime.OnGameplayReady();
        GameContextAdapter.InitializeEventBaselines();
        TaskEventMonitor.BeginSession();
        GameEvents.RaiseGameplayReady();
        RuntimeItemCatalog.CaptureOnce();
        var injectedCount = RuntimeItemInjection.TryInject(Plugin.RuntimeItemInjectionEnabled);
        if (RuntimeItemInjection.MarkRuntimeReady())
        {
            GameEvents.RaiseItemRuntimeReady(
                ItemCatalog.All.Count,
                injectedCount,
                Plugin.RuntimeItemInjectionEnabled);
        }
    }
}

[HarmonyPatch(
    typeof(GameProcess),
    nameof(GameProcess.PassMinutes),
    new[] { typeof(int), typeof(bool) })]
internal static class PassMinutesIntegerPatch
{
    private static void Postfix() => GameContextAdapter.PublishTimeIfChanged();
}

[HarmonyPatch(typeof(GameProcess), nameof(GameProcess.PassMinutes), new[] { typeof(TimePass) })]
internal static class PassMinutesTimePassPatch
{
    private static void Postfix() => GameContextAdapter.PublishTimeIfChanged();
}

[HarmonyPatch(typeof(Map), nameof(Map.FocusMap))]
internal static class FocusMapPatch
{
    private static void Postfix(Map __instance) =>
        GameContextAdapter.PublishMapIfChanged(__instance);
}

[HarmonyPatch(typeof(ThingPackage), nameof(ThingPackage.ChangeEnergy))]
internal static class ThingPackageChangeEnergyPatch
{
    private static void Postfix(ThingPackage __instance) =>
        GameContextAdapter.PublishPlayerIfChanged(__instance, nameof(ThingPackage.ChangeEnergy));
}

[HarmonyPatch(typeof(ThingPackage), nameof(ThingPackage.ChangeMoney))]
internal static class ThingPackageChangeMoneyPatch
{
    private static void Postfix(
        ThingPackage __instance,
        int count,
        ThingChangeReason reason)
    {
        GameContextAdapter.PublishPlayerIfChanged(__instance, nameof(ThingPackage.ChangeMoney));
        TradeSignals.PublishMoney(
            __instance.cId,
            count,
            nameof(ThingPackage.ChangeMoney),
            reason.ToString());
    }
}

[HarmonyPatch(typeof(ThingPackage), nameof(ThingPackage.ChangeFriend))]
internal static class ThingPackageChangeFriendPatch
{
    private static void Postfix(
        ThingPackage __instance,
        int count,
        ThingChangeReason reason)
    {
        TradeSignals.PublishRelationship(
            __instance.cId,
            count,
            nameof(ThingPackage.ChangeFriend),
            reason.ToString());
    }
}

[HarmonyPatch(typeof(ThingPackage), nameof(ThingPackage.ChangeHealthy))]
internal static class ThingPackageChangeHealthyPatch
{
    private static void Postfix(ThingPackage __instance) =>
        GameContextAdapter.PublishPlayerIfChanged(__instance, nameof(ThingPackage.ChangeHealthy));
}

[HarmonyPatch(typeof(ThingPackage), nameof(ThingPackage.ChangeMentality))]
internal static class ThingPackageChangeMentalityPatch
{
    private static void Postfix(ThingPackage __instance) =>
        GameContextAdapter.PublishPlayerIfChanged(__instance, nameof(ThingPackage.ChangeMentality));
}

[HarmonyPatch(typeof(ThingPackage), nameof(ThingPackage.ChangeSatiety))]
internal static class ThingPackageChangeSatietyPatch
{
    private static void Postfix(ThingPackage __instance) =>
        GameContextAdapter.PublishPlayerIfChanged(__instance, nameof(ThingPackage.ChangeSatiety));
}

[HarmonyPatch(typeof(ThingPackage), nameof(ThingPackage.ChangeFocus))]
internal static class ThingPackageChangeFocusPatch
{
    private static void Postfix(ThingPackage __instance) =>
        GameContextAdapter.PublishPlayerIfChanged(__instance, nameof(ThingPackage.ChangeFocus));
}

[HarmonyPatch(typeof(ThingPackage), nameof(ThingPackage.AddAttirbute))]
internal static class ThingPackageAddAttributePatch
{
    private static void Postfix(ThingPackage __instance) =>
        GameContextAdapter.PublishPlayerIfChanged(__instance, nameof(ThingPackage.AddAttirbute));
}

[HarmonyPatch(typeof(ThingPackage), nameof(ThingPackage.ChangeAttirbute))]
internal static class ThingPackageChangeAttributePatch
{
    private static void Postfix(ThingPackage __instance) =>
        GameContextAdapter.PublishPlayerIfChanged(__instance, nameof(ThingPackage.ChangeAttirbute));
}

[HarmonyPatch(typeof(ThingPackage), nameof(ThingPackage.SetAttirbute))]
internal static class ThingPackageSetAttributePatch
{
    private static void Postfix(ThingPackage __instance) =>
        GameContextAdapter.PublishPlayerIfChanged(__instance, nameof(ThingPackage.SetAttirbute));
}

[HarmonyPatch(typeof(CharacterAttribute), nameof(CharacterAttribute.UseItem))]
internal static class CharacterAttributeUseItemPatch
{
    private static bool Prefix(
        CharacterAttribute __instance,
        int itemId,
        ref int useCount,
        out GameContextAdapter.ItemUseInvocationState? __state) =>
        GameContextAdapter.TryBeginItemUse(__instance, itemId, ref useCount, out __state);

    private static void Postfix(
        CharacterAttribute __instance,
        int itemId,
        int useCount,
        GameContextAdapter.ItemUseInvocationState? __state) =>
        GameContextAdapter.PublishItemUseCompleted(
            __instance,
            itemId,
            useCount,
            __state,
            package: __instance.thingPackage);
}

// The inventory layer can consume the Thing before CharacterAttribute.UseItem
// is reached. Intercept both ThingPackage entry points so a cancelled Mod
// event prevents the count from being reduced in the first place.
[HarmonyPatch(typeof(ThingPackage), nameof(ThingPackage.UseThing))]
internal static class ThingPackageUseThingPatch
{
    private static bool Prefix(
        ThingPackage __instance,
        Thing thing,
        out GameContextAdapter.ItemUseInvocationState? __state)
    {
        var useCount = 1;
        return GameContextAdapter.TryBeginItemUse(
            __instance,
            thing,
            ref useCount,
            out __state,
            nameof(ThingPackage.UseThing));
    }

    private static void Postfix(
        ThingPackage __instance,
        Thing thing,
        bool __result,
        GameContextAdapter.ItemUseInvocationState? __state)
    {
        if (__state is null || __instance.attribute is null || thing is null)
        {
            return;
        }

        GameContextAdapter.PublishItemUseCompleted(
            __instance.attribute,
            thing.id,
            1,
            __state,
            __result,
            __instance);
    }
}

[HarmonyPatch(typeof(ThingPackage), nameof(ThingPackage.UseBatchThing))]
internal static class ThingPackageUseBatchThingPatch
{
    private static bool Prefix(
        ThingPackage __instance,
        Thing thing,
        ref int useCount,
        out GameContextAdapter.ItemUseInvocationState? __state) =>
        GameContextAdapter.TryBeginItemUse(
            __instance,
            thing,
            ref useCount,
            out __state,
            nameof(ThingPackage.UseBatchThing));

    private static void Postfix(
        ThingPackage __instance,
        Thing thing,
        int useCount,
        bool __result,
        GameContextAdapter.ItemUseInvocationState? __state)
    {
        if (__state is null || __instance.attribute is null || thing is null)
        {
            return;
        }

        GameContextAdapter.PublishItemUseCompleted(
            __instance.attribute,
            thing.id,
            useCount,
            __state,
            __result,
            __instance);
    }
}

[HarmonyPatch(typeof(ThingPackage), nameof(ThingPackage.AddItem),
    new[] { typeof(int), typeof(int), typeof(PlaceType), typeof(ThingChangeReason) })]
internal static class ThingPackageAddItemPatch
{
    private static void Prefix(
        ThingPackage __instance,
        int id,
        int changeCount,
        ThingChangeReason reason,
        out GameContextAdapter.InventoryOperationState? __state) =>
        __state = GameContextAdapter.BeginInventoryOperation(
            __instance,
            id,
            changeCount,
            nameof(ThingPackage.AddItem),
            reason.ToString());

    private static void Postfix(
        ThingPackage __instance,
        Thing __result,
        GameContextAdapter.InventoryOperationState? __state) =>
        GameContextAdapter.PublishInventoryChanged(
            __instance,
            __state,
            __result is not null);
}

[HarmonyPatch(typeof(ThingPackage), nameof(ThingPackage.AddItemOneByOne),
    new[] { typeof(int), typeof(int), typeof(PlaceType), typeof(ThingChangeReason) })]
internal static class ThingPackageAddItemOneByOnePatch
{
    private static void Prefix(
        ThingPackage __instance,
        int id,
        int addCount,
        ThingChangeReason reason,
        out GameContextAdapter.InventoryOperationState? __state) =>
        __state = GameContextAdapter.BeginInventoryOperation(
            __instance,
            id,
            addCount,
            nameof(ThingPackage.AddItemOneByOne),
            reason.ToString());

    private static void Postfix(
        ThingPackage __instance,
        GameContextAdapter.InventoryOperationState? __state) =>
        GameContextAdapter.PublishInventoryChanged(__instance, __state, false);
}

[HarmonyPatch(typeof(ThingPackage), nameof(ThingPackage.ReduceItem),
    new[] { typeof(int), typeof(int), typeof(ThingChangeReason), typeof(bool) })]
internal static class ThingPackageReduceItemPatch
{
    private static void Prefix(
        ThingPackage __instance,
        int id,
        int reduceCount,
        ThingChangeReason reason,
        out GameContextAdapter.InventoryOperationState? __state) =>
        __state = GameContextAdapter.BeginInventoryOperation(
            __instance,
            id,
            -reduceCount,
            nameof(ThingPackage.ReduceItem),
            reason.ToString());

    private static void Postfix(
        ThingPackage __instance,
        int __result,
        GameContextAdapter.InventoryOperationState? __state) =>
        GameContextAdapter.PublishInventoryChanged(
            __instance,
            __state,
            __result > 0);
}

[HarmonyPatch(typeof(ThingPackage), nameof(ThingPackage.ReduceItem),
    new[] { typeof(int), typeof(int), typeof(PlaceType), typeof(ThingChangeReason), typeof(bool) })]
internal static class ThingPackageReduceItemAtPlacePatch
{
    private static void Prefix(
        ThingPackage __instance,
        int id,
        int reduceCount,
        ThingChangeReason reason,
        out GameContextAdapter.InventoryOperationState? __state) =>
        __state = GameContextAdapter.BeginInventoryOperation(
            __instance,
            id,
            -reduceCount,
            nameof(ThingPackage.ReduceItem),
            reason.ToString());

    private static void Postfix(
        ThingPackage __instance,
        int __result,
        GameContextAdapter.InventoryOperationState? __state) =>
        GameContextAdapter.PublishInventoryChanged(
            __instance,
            __state,
            __result > 0);
}

[HarmonyPatch(typeof(ThingPackage), nameof(ThingPackage.ReduceThingCount),
    new[] { typeof(Thing), typeof(int), typeof(ThingChangeReason) })]
internal static class ThingPackageReduceThingCountPatch
{
    private static void Prefix(
        ThingPackage __instance,
        Thing thing,
        int reduceCount,
        ThingChangeReason reason,
        out GameContextAdapter.InventoryOperationState? __state)
    {
        __state = GameContextAdapter.BeginInventoryOperation(
            __instance,
            thing?.id ?? 0,
            -reduceCount,
            nameof(ThingPackage.ReduceThingCount),
            reason.ToString());
        TradeSignals.BeginBetTicketExchange(
            thing,
            reduceCount,
            reason.ToString());
    }

    private static void Postfix(
        ThingPackage __instance,
        bool __result,
        GameContextAdapter.InventoryOperationState? __state)
    {
        GameContextAdapter.PublishInventoryChanged(__instance, __state, __result);
        TradeSignals.CompleteBetTicketExchange(__result);
    }
}

[HarmonyPatch(typeof(ThingPackage), nameof(ThingPackage.RemoveThing),
    new[] { typeof(Thing) })]
internal static class ThingPackageRemoveThingPatch
{
    private static void Prefix(
        ThingPackage __instance,
        Thing thing,
        out GameContextAdapter.InventoryOperationState? __state)
    {
        __state = GameContextAdapter.BeginInventoryOperation(
            __instance,
            thing?.id ?? 0,
            -(thing?.count ?? 0),
            nameof(ThingPackage.RemoveThing),
            string.Empty);
        TradeSignals.BeginMatchBetTicketRemoval(thing);
    }

    private static void Postfix(
        ThingPackage __instance,
        bool __result,
        GameContextAdapter.InventoryOperationState? __state)
    {
        GameContextAdapter.PublishInventoryChanged(__instance, __state, __result);
        TradeSignals.CompleteMatchBetTicketRemoval(__result);
    }
}

[HarmonyPatch(typeof(WidgetItemMiddleTools), nameof(WidgetItemMiddleTools.ArrangePocketItemList))]
internal static class WidgetItemMiddleToolsArrangePocketItemListPatch
{
    private static void Postfix() =>
        GameContextAdapter.PublishPlayerItemAction(
            CharacterManage.protagonistAttribute,
            null,
            ItemActionKind.Arrange,
            0,
            true,
            nameof(WidgetItemMiddleTools.ArrangePocketItemList));
}

[HarmonyPatch(typeof(CharacterAttribute), nameof(CharacterAttribute.EquipmentItem))]
internal static class CharacterAttributeEquipmentItemPatch
{
    private static void Postfix(Thing thing) =>
        GameContextAdapter.PublishPlayerItemAction(
            CharacterManage.protagonistAttribute,
            thing,
            ItemActionKind.Equip,
            1,
            true,
            nameof(CharacterAttribute.EquipmentItem));
}

[HarmonyPatch(typeof(CharacterAttribute), nameof(CharacterAttribute.RemoveEquipmentItem))]
internal static class CharacterAttributeRemoveEquipmentItemPatch
{
    private static void Postfix(CharacterAttribute __instance, Thing thing)
    {
        GameContextAdapter.PublishPlayerItemAction(
            __instance,
            thing,
            ItemActionKind.Unequip,
            1,
            true,
            nameof(CharacterAttribute.RemoveEquipmentItem));
    }
}

// This is intentionally a low-level fallback. It lets a diagnostic Mod see
// an operation button even when a future game update moves its implementation
// away from the current WidgetItemMiddleTools methods.
[HarmonyPatch(typeof(WidgetItemOperationButton), nameof(WidgetItemOperationButton.ClickA))]
internal static class WidgetItemOperationButtonClickPatch
{
    private static void Postfix(WidgetItemOperationButton __instance)
    {
        try
        {
            var thing = __instance.thing;
            GameContextAdapter.PublishPlayerItemAction(
                CharacterManage.protagonistAttribute,
                thing,
                ItemActionKind.OperationSelected,
                thing?.count ?? 0,
                true,
                nameof(WidgetItemOperationButton.ClickA),
                __instance.operationType);
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError(
                $"[LifecycleHook] Failed to inspect item operation button: {exception}");
        }
    }
}

// Destruction is confirmed through private UI methods rather than the public
// WidgetItemMiddleTools helper in the current build. Keep these patches
// string-based so Harmony can target the private IL2CPP methods safely.
[HarmonyPatch(typeof(WidgetItemOperationButton), "SubmitConfirmDestoryItem")]
internal static class WidgetItemOperationButtonSubmitDestroyItemPatch
{
    private static void Postfix(WidgetItemOperationButton __instance, int count)
    {
        try
        {
            GameContextAdapter.PublishPlayerItemAction(
                CharacterManage.protagonistAttribute,
                __instance.thing,
                ItemActionKind.Destroy,
                count,
                true,
                "WidgetItemOperationButton.SubmitConfirmDestoryItem");
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError(
                $"[LifecycleHook] Failed to inspect destroy confirmation: {exception}");
        }
    }
}

[HarmonyPatch(typeof(ThingPackage), nameof(ThingPackage.MoveThingPlace), new[] { typeof(Thing), typeof(PlaceType) })]
internal static class ThingPackageMoveThingPlacePatch
{
    private static void Prefix(
        ThingPackage __instance,
        Thing thing,
        out GameContextAdapter.InventoryMoveState? __state) =>
        __state = GameContextAdapter.BeginInventoryMove(__instance, thing, 0);

    private static void Postfix(
        ThingPackage __instance,
        Thing thing,
        PlaceType type,
        bool __result,
        GameContextAdapter.InventoryMoveState? __state)
    {
        GameContextAdapter.PublishInventoryMoved(
            __instance,
            thing,
            __state,
            __result,
            nameof(ThingPackage.MoveThingPlace));

        if (!__result || type != PlaceType.Pocket ||
            __state?.From.Container != nameof(PlaceType.Equipment))
        {
            return;
        }

        GameContextAdapter.PublishPlayerItemAction(
            __instance,
            thing,
            ItemActionKind.Unequip,
            __state.Count,
            true,
            nameof(ThingPackage.MoveThingPlace));
    }
}

[HarmonyPatch(typeof(ThingPackage), nameof(ThingPackage.MoveThingPlace),
    new[] { typeof(Thing), typeof(PlaceType), typeof(int) })]
internal static class ThingPackageMoveThingPlaceCountPatch
{
    private static void Prefix(
        ThingPackage __instance,
        Thing thing,
        int count,
        out GameContextAdapter.InventoryMoveState? __state) =>
        __state = GameContextAdapter.BeginInventoryMove(__instance, thing, count);

    private static void Postfix(
        ThingPackage __instance,
        Thing thing,
        PlaceType type,
        MoveThingPlaceResult __result,
        GameContextAdapter.InventoryMoveState? __state)
    {
        var succeeded = __result != MoveThingPlaceResult.False &&
                        __result != MoveThingPlaceResult.NoPlace;
        GameContextAdapter.PublishInventoryMoved(
            __instance,
            thing,
            __state,
            succeeded,
            "ThingPackage.MoveThingPlace(count)");

        if (!succeeded || type != PlaceType.Pocket ||
            __state?.From.Container != nameof(PlaceType.Equipment))
        {
            return;
        }

        GameContextAdapter.PublishPlayerItemAction(
            __instance,
            thing,
            ItemActionKind.Unequip,
            __state.Count,
            true,
            "ThingPackage.MoveThingPlace(count)");
    }
}
