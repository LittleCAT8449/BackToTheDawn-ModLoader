using HarmonyLib;
using BackToTheDawn.ModAPI;

namespace BackToTheDawn.Loader;

[HarmonyPatch(typeof(GameManage), nameof(GameManage.ShowGameStartUI))]
internal static class ShowGameStartUiPatch
{
    private static void Prefix(bool isShowSelectInputModel, int immediateStartArchiveId)
    {
        GameContextAdapter.IsGameplayReady = false;
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
        GameContextAdapter.IsGameplayReady = false;
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
        GameContextAdapter.InitializeEventBaselines();
        GameEvents.RaiseGameplayReady();
        RuntimeItemCatalog.CaptureOnce();
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
    private static void Postfix(ThingPackage __instance) =>
        GameContextAdapter.PublishPlayerIfChanged(__instance, nameof(ThingPackage.ChangeMoney));
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
    private static void Postfix(CharacterAttribute __instance, int itemId, int useCount) =>
        GameContextAdapter.PublishPlayerItemUsed(__instance, itemId, useCount);
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
    private static void Prefix(Thing thing, out PlaceType __state)
    {
        __state = thing?.place?.pt ?? PlaceType.None;
    }

    private static void Postfix(
        ThingPackage __instance,
        Thing thing,
        PlaceType type,
        bool __result,
        PlaceType __state)
    {
        if (!__result || type != PlaceType.Pocket || __state != PlaceType.Equipment)
        {
            return;
        }

        GameContextAdapter.PublishPlayerItemAction(
            __instance,
            thing,
            ItemActionKind.Unequip,
            1,
            true,
            nameof(ThingPackage.MoveThingPlace));
    }
}
