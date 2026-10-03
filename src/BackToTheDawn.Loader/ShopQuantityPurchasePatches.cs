using BackToTheDawn.ModAPI;
using HarmonyLib;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace BackToTheDawn.Loader;

/// <summary>
/// Carries the selected Mod-shop row into the game's separate purchase
/// confirmation widget, whose slider otherwise starts at zero.
/// </summary>
internal static class ShopQuantityPurchaseContext
{
    private static UI_ShopListUnit? _row;
    private static int _shopId;
    private static int _itemId;
    private static int _maxCount;
    private static int _selectedCount;
    private static int _unitPrice;
    private static WidgetItemOperateConfirm? _confirmation;
    private static readonly HashSet<int> DumpedConfirmationLayouts = new();
    private sealed record ConfirmationLayoutState(
        RectTransform Panel,
        Vector2 PanelSize,
        Vector2 PanelPosition,
        Transform CountSelector,
        Vector3 CountSelectorPosition);
    private static readonly Dictionary<int, ConfirmationLayoutState> ConfirmationLayouts = new();

    internal static bool Capture(UI_ShopListUnit row, string source)
    {
        try
        {
            var config = row.config;
            var shopConfig = config?.shopConfig;
            var shopId = row.shopId != 0 ? row.shopId : shopConfig?.shop_id ?? 0;
            if (!ShopCatalog.TryGetByNativeId(shopId, out var shop) ||
                shop.Source != ShopSource.Synthetic)
            {
                return false;
            }

            var selector = row.widgetItemListSelectCount;
            if (selector is null)
            {
                return false;
            }

            var maxCount = selector.maxSelectCount;
            if (maxCount < 1)
            {
                maxCount = config?.GetLeftCount() ?? 0;
            }

            if (maxCount < 1)
            {
                return false;
            }

            var selectedCount = selector.currentSelectCount;
            if (selectedCount < 1)
            {
                selectedCount = Math.Clamp(Math.Max(selector.atLeastCount, 1), 1, maxCount);
                selector.SetCount(selectedCount);
                row.ChangeCountCallBack();
            }

            _row = row;
            _shopId = shopId;
            _itemId = shopConfig?.goods_item ?? row.GetThing()?.id ?? 0;
            _maxCount = maxCount;
            _selectedCount = Math.Clamp(selectedCount, 1, maxCount);
            _unitPrice = shopConfig?.goods_price ?? 0;

            Plugin.Logger?.LogInfo(
                $"[ShopQuantity] Captured confirmation context from {source}: " +
                $"shop={_shopId}, item={_itemId}, selected={_selectedCount}/{_maxCount}, " +
                $"unitPrice={_unitPrice}.");
            return _itemId > 0;
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning(
                $"[ShopQuantity] Capturing purchase context from {source} failed: {exception.Message}");
            return false;
        }
    }

    internal static bool CaptureVisibleRow(int itemId, string source)
    {
        if (itemId <= 0)
        {
            return false;
        }

        try
        {
            var rows = UnityEngine.Object.FindObjectsOfType<UI_ShopListUnit>();
            Plugin.Logger?.LogInfo(
                $"[ShopQuantity] Searching {rows.Length} active shop row(s) for confirmation item {itemId}.");
            foreach (var row in rows)
            {
                var config = row.config;
                var shopConfig = config?.shopConfig;
                var shopId = row.shopId != 0 ? row.shopId : shopConfig?.shop_id ?? 0;
                var rowItemId = shopConfig?.goods_item ?? row.GetThing()?.id ?? 0;
                var synthetic = ShopCatalog.TryGetByNativeId(shopId, out var shop) &&
                                shop.Source == ShopSource.Synthetic;
                Plugin.Logger?.LogInfo(
                    $"[ShopQuantity] Visible row candidate: shop={shopId}, item={rowItemId}, " +
                    $"synthetic={synthetic}, selected={row.widgetItemListSelectCount?.currentSelectCount}, " +
                    $"max={row.widgetItemListSelectCount?.maxSelectCount}, price={shopConfig?.goods_price}.");

                if (!synthetic || rowItemId != itemId)
                {
                    continue;
                }

                if (Capture(row, $"{source}:visible-row"))
                {
                    return true;
                }
            }

            Plugin.Logger?.LogWarning(
                $"[ShopQuantity] No active synthetic shop row matched confirmation item {itemId}.");
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning(
                $"[ShopQuantity] Searching visible shop rows for item {itemId} failed: {exception}");
        }

        return false;
    }

    internal static bool TryGet(int itemId, out UI_ShopListUnit row, out int maxCount, out int selectedCount, out int unitPrice)
    {
        row = null!;
        maxCount = 0;
        selectedCount = 0;
        unitPrice = 0;

        if (_row is null || itemId <= 0 || itemId != _itemId)
        {
            return false;
        }

        row = _row;
        maxCount = _maxCount;
        // SubmitBuy may reset the shop row's native selector to its default
        // before TriggerBuyItem runs. The confirmation slider is the source of
        // truth and UpdateSelectedCount keeps this cached value current.
        selectedCount = Math.Clamp(_selectedCount, 1, _maxCount);
        unitPrice = _unitPrice;
        return true;
    }

    internal static bool TryGetCurrent(out UI_ShopListUnit row, out int shopId, out int itemId, out int maxCount, out int selectedCount, out int unitPrice)
    {
        row = null!;
        shopId = 0;
        itemId = 0;
        maxCount = 0;
        selectedCount = 0;
        unitPrice = 0;

        if (_row is null || _itemId <= 0 || _maxCount < 1)
        {
            return false;
        }

        row = _row;
        shopId = _shopId;
        itemId = _itemId;
        maxCount = _maxCount;
        // The row selector can already have been reset by the game's submit
        // flow; retain the count last captured from the confirmation UI.
        selectedCount = Math.Clamp(_selectedCount, 1, _maxCount);
        unitPrice = _unitPrice;
        return true;
    }

    internal static void UpdateSelectedCount(int count)
    {
        if (_row is null || _maxCount < 1)
        {
            return;
        }

        _selectedCount = Math.Clamp(count, 1, _maxCount);
        var selector = _row.widgetItemListSelectCount;
        if (selector is not null && selector.currentSelectCount != _selectedCount)
        {
            selector.SetCount(_selectedCount);
            _row.ChangeCountCallBack();
        }

        _row.widgetItem?.ShowFractionCount(_selectedCount, _maxCount);
    }

    internal static void Clear()
    {
        _row = null;
        _shopId = 0;
        _itemId = 0;
        _maxCount = 0;
        _selectedCount = 0;
        _unitPrice = 0;
        _confirmation = null;
        DumpedConfirmationLayouts.Clear();
        RestoreAllConfirmationLayouts();
    }

    internal static void SetConfirmation(WidgetItemOperateConfirm? confirmation) =>
        _confirmation = confirmation;

    internal static WidgetItemOperateConfirm? GetConfirmation() => _confirmation;

    internal static void DumpConfirmationUi(WidgetItemOperateConfirm confirmation, int itemId, string stage, bool includeLayout)
    {
        try
        {
            var dumpLayout = includeLayout && DumpedConfirmationLayouts.Add(itemId);
            var transforms = confirmation.GetComponentsInChildren<Transform>(true);
            Plugin.Logger?.LogInfo(
                $"[ShopQuantity] Confirm UI snapshot: stage={stage}, item={itemId}, " +
                $"costField={confirmation.costMoney}, nodes={transforms.Length}, layout={dumpLayout}.");
            foreach (var transform in transforms)
            {
                var rect = transform.GetComponent<RectTransform>();
                var pathParts = new Stack<string>();
                var current = transform;
                while (current is not null)
                {
                    pathParts.Push(current.name);
                    current = current.parent;
                }
                var path = string.Join("/", pathParts);

                if (dumpLayout)
                {
                    var rectInfo = rect is null
                        ? $"local={transform.localPosition}"
                        : $"local={transform.localPosition}, anchored={rect.anchoredPosition}, " +
                          $"size={rect.rect.size}, " +
                          $"anchors={rect.anchorMin}-{rect.anchorMax}, pivot={rect.pivot}";
                    var components = transform.GetComponents<Component>();
                    var componentNames = string.Join(",", components
                        .Where(component => component is not null)
                        .Select(component => component!.GetIl2CppType().FullName));
                    Plugin.Logger?.LogInfo(
                        $"[ShopQuantity] Confirm node: path={path}, active={transform.gameObject.activeInHierarchy}, " +
                        $"{rectInfo}, components=[{componentNames}].");
                }

            }

            foreach (var text in confirmation.GetComponentsInChildren<Text>(true))
            {
                var pathParts = new Stack<string>();
                var current = text.transform;
                while (current is not null)
                {
                    pathParts.Push(current.name);
                    current = current.parent;
                }
                Plugin.Logger?.LogInfo(
                    $"[ShopQuantity] Confirm text: path={string.Join("/", pathParts)}, " +
                    $"active={text.gameObject.activeInHierarchy}, value={text.text}.");
            }
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning(
                $"[ShopQuantity] Confirm UI snapshot failed at {stage} for item {itemId}: {exception}");
        }
    }

    internal static void RefreshDisplayedCost(WidgetItemOperateConfirm confirmation, int totalCost, int unitPrice)
    {
        try
        {
            var costGroup = confirmation.transform.Find("panel/2/group");
            var costNode = costGroup?.Find("Text (3)");
            var costText = costNode?.GetComponent<Text>();
            if (costText is null && costGroup is not null)
            {
                costText = costGroup.GetComponentInChildren<Text>(true);
            }

            if (costText is null)
            {
                var componentTypes = costNode is null
                    ? "<cost node not found>"
                    : string.Join(",", costNode.GetComponents<Component>()
                        .Where(component => component is not null)
                        .Select(component => component!.GetIl2CppType().FullName));
                Plugin.Logger?.LogWarning(
                    $"[ShopQuantity] Could not find native cost Text for total {totalCost}; " +
                    $"nodeType={componentTypes}, unitPrice={unitPrice}.");
                return;
            }

            var previous = costText.text ?? string.Empty;
            var matches = Regex.Matches(previous, @"\d+");
            string updated;
            if (matches.Count > 0)
            {
                var match = matches[matches.Count - 1];
                updated = previous[..match.Index] + totalCost + previous[(match.Index + match.Length)..];
            }
            else
            {
                updated = string.IsNullOrWhiteSpace(previous)
                    ? totalCost.ToString()
                    : $"{previous}{totalCost}";
            }

            costText.text = updated;
            Plugin.Logger?.LogInfo(
                $"[ShopQuantity] Refreshed native cost text: node={costNode?.name}, " +
                $"previous='{previous}', updated='{updated}', total={totalCost}.");
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning($"[ShopQuantity] Refreshing native cost text failed: {exception}");
        }
    }

    internal static void ApplyConfirmationLayout(
        WidgetItemOperateConfirm confirmation,
        WidgetItemOperateConfirm_SelectCount selector)
    {
        var root = confirmation.transform;
        var rootId = confirmation.GetInstanceID();
        if (ConfirmationLayouts.ContainsKey(rootId))
        {
            return;
        }

        try
        {
            var panelTransform = root.Find("panel");
            var buttonTransform = panelTransform?.Find("buttonA");
            var panel = panelTransform?.GetComponent<RectTransform>();
            var button = buttonTransform?.GetComponent<RectTransform>();
            var countTransform = selector.transform;
            var countRect = countTransform.GetComponent<RectTransform>();
            if (panel is null || button is null || countRect is null)
            {
                Plugin.Logger?.LogWarning(
                    $"[ShopQuantity] Layout adjustment skipped: panelRect={panel is not null}, " +
                    $"buttonRect={button is not null}, countRect={countRect is not null}.");
                return;
            }

            GetVerticalBounds(button, root, out var buttonBottom, out _);
            GetVerticalBounds(countRect, root, out var countBottom, out var countTop);
            var countHeight = Math.Max(1f, countTop - countBottom);
            const float gap = 6f;
            var desiredBottom = buttonBottom - gap - countHeight;
            GetVerticalBounds(panel, root, out var panelBottom, out _);
            var extraHeight = Math.Max(0f, panelBottom - desiredBottom + 8f);

            ConfirmationLayouts[rootId] = new ConfirmationLayoutState(
                panel,
                panel.sizeDelta,
                panel.anchoredPosition,
                countTransform,
                countTransform.localPosition);

            if (extraHeight > 0.5f)
            {
                panel.SetSizeWithCurrentAnchors(
                    RectTransform.Axis.Vertical,
                    panel.rect.height + extraHeight);
                var panelPosition = panel.anchoredPosition;
                panelPosition.y -= extraHeight * (1f - panel.pivot.y);
                panel.anchoredPosition = panelPosition;
                Canvas.ForceUpdateCanvases();
            }

            GetVerticalBounds(button, root, out buttonBottom, out _);
            GetVerticalBounds(countRect, root, out countBottom, out countTop);
            countHeight = Math.Max(1f, countTop - countBottom);
            var desiredCenter = buttonBottom - gap - countHeight * 0.5f;
            var rootPosition = root.InverseTransformPoint(countTransform.position);
            rootPosition.y = desiredCenter;
            var desiredWorldPosition = root.TransformPoint(rootPosition);
            var parent = countTransform.parent;
            countTransform.localPosition = parent is null
                ? desiredWorldPosition
                : parent.InverseTransformPoint(desiredWorldPosition);
            Canvas.ForceUpdateCanvases();

            GetVerticalBounds(panel, root, out panelBottom, out _);
            GetVerticalBounds(countRect, root, out countBottom, out countTop);
            Plugin.Logger?.LogInfo(
                $"[ShopQuantity] Native confirmation layout adjusted: buttonBottom={buttonBottom}, " +
                $"countBounds={countBottom}..{countTop}, panelBottom={panelBottom}, " +
                $"extraHeight={extraHeight}, panelSize={panel.rect.size}, " +
                $"countPosition={countTransform.localPosition}.");
        }
        catch (Exception exception)
        {
            ConfirmationLayouts.Remove(rootId);
            Plugin.Logger?.LogWarning($"[ShopQuantity] Adjusting native confirmation layout failed: {exception}");
        }
    }

    internal static void RestoreConfirmationLayout(WidgetItemOperateConfirm confirmation)
    {
        var rootId = confirmation.GetInstanceID();
        if (!ConfirmationLayouts.Remove(rootId, out var state))
        {
            return;
        }

        try
        {
            state.Panel.sizeDelta = state.PanelSize;
            state.Panel.anchoredPosition = state.PanelPosition;
            state.CountSelector.localPosition = state.CountSelectorPosition;
            Canvas.ForceUpdateCanvases();
            Plugin.Logger?.LogInfo("[ShopQuantity] Restored native confirmation layout.");
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning($"[ShopQuantity] Restoring native confirmation layout failed: {exception.Message}");
        }
    }

    internal static void RestoreAllConfirmationLayouts()
    {
        foreach (var state in ConfirmationLayouts.Values.ToArray())
        {
            try
            {
                state.Panel.sizeDelta = state.PanelSize;
                state.Panel.anchoredPosition = state.PanelPosition;
                state.CountSelector.localPosition = state.CountSelectorPosition;
            }
            catch { }
        }
        ConfirmationLayouts.Clear();
    }

    private static void GetVerticalBounds(RectTransform target, Transform root, out float minimum, out float maximum)
    {
        // RectTransform.GetWorldCorners does not reliably populate a managed
        // Vector3[] through this game's IL2CPP bridge. Transform the rect's
        // corners directly so layout calculations use the actual UI bounds.
        var rect = target.rect;
        var corners = new[]
        {
            new Vector3(rect.xMin, rect.yMin, 0f),
            new Vector3(rect.xMin, rect.yMax, 0f),
            new Vector3(rect.xMax, rect.yMin, 0f),
            new Vector3(rect.xMax, rect.yMax, 0f)
        };
        minimum = float.PositiveInfinity;
        maximum = float.NegativeInfinity;
        foreach (var corner in corners)
        {
            var world = target.TransformPoint(corner);
            var local = root.InverseTransformPoint(world);
            minimum = Math.Min(minimum, local.y);
            maximum = Math.Max(maximum, local.y);
        }
    }

    internal static void ConfigureConfirmation(int itemId, int requestedCount)
    {
        if (_confirmation is null ||
            !TryGet(itemId, out var row, out var maxCount, out var selectedCount, out var unitPrice))
        {
            Plugin.Logger?.LogWarning(
                $"[ShopQuantity] Confirmation root for item {itemId} was not captured.");
            return;
        }

        try
        {
            var selector = _confirmation.selectCount;
            if (selector is null)
            {
                Plugin.Logger?.LogWarning(
                    $"[ShopQuantity] Confirmation root for item {itemId} has no count selector.");
                return;
            }

            selector.ShowCanSelectCount(maxCount);
            var initialCount = Math.Clamp(requestedCount > 0 ? requestedCount : selectedCount, 1, maxCount);
            ShopBuyConfirmCountPatch.ConfigureSelector(selector, maxCount, initialCount);
            UpdateSelectedCount(selector.GetSelectCount());

            var totalCost = Math.Max(0, unitPrice) * selector.GetSelectCount();
            _confirmation.costMoney = totalCost;
            _confirmation.widgetItem?.ShowFractionCount(selector.GetSelectCount(), maxCount);
            row.widgetItem?.ShowFractionCount(selector.GetSelectCount(), maxCount);
            RefreshDisplayedCost(_confirmation, totalCost, unitPrice);
            ApplyConfirmationLayout(_confirmation, selector);

            Plugin.Logger?.LogInfo(
                $"[ShopQuantity] Confirmation root configured: shop={_shopId}, item={itemId}, " +
                $"count={selector.GetSelectCount()}/{maxCount}, slider={selector.countSlider?.value}, " +
                $"cost={totalCost}.");
            DumpConfirmationUi(_confirmation, itemId, "root-configured", includeLayout: true);
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning(
                $"[ShopQuantity] Configuring confirmation root for item {itemId} failed: {exception}");
        }
    }
}

[HarmonyPatch(typeof(UI_ShopListUnit), nameof(UI_ShopListUnit.OnClickBuy))]
internal static class UiShopListUnitCaptureBuyContextPatch
{
    private static void Prefix(UI_ShopListUnit __instance)
    {
        var captured = ShopQuantityPurchaseContext.Capture(__instance, nameof(UI_ShopListUnit.OnClickBuy));
        Plugin.Logger?.LogInfo(
            $"[ShopQuantity] OnClickBuy hook: shop={__instance.shopId}, " +
            $"item={__instance.config?.shopConfig?.goods_item}, " +
            $"selected={__instance.widgetItemListSelectCount?.currentSelectCount}, captured={captured}.");
    }
}

[HarmonyPatch(typeof(UI_ShopListUnit), nameof(UI_ShopListUnit.SubmitBuy))]
internal static class UiShopListUnitCaptureSubmitContextPatch
{
    private static void Prefix(UI_ShopListUnit __instance)
    {
        var captured = ShopQuantityPurchaseContext.Capture(__instance, nameof(UI_ShopListUnit.SubmitBuy));
        Plugin.Logger?.LogInfo(
            $"[ShopQuantity] SubmitBuy hook: shop={__instance.shopId}, " +
            $"item={__instance.config?.shopConfig?.goods_item}, " +
            $"selected={__instance.widgetItemListSelectCount?.currentSelectCount}, captured={captured}.");
    }
}

[HarmonyPatch(typeof(WidgetItemOperateConfirm_BuyItem), nameof(WidgetItemOperateConfirm_BuyItem.ShowBuyItemConfirm))]
internal static class ShopBuyConfirmCountPatch
{
    private static void Prefix(
        WidgetItemOperateConfirm_BuyItem __instance,
        int itemId,
        ref int buyCount,
        ref int costMoney)
    {
        ShopQuantityPurchaseContext.CaptureVisibleRow(itemId, nameof(WidgetItemOperateConfirm_BuyItem.ShowBuyItemConfirm));
        Plugin.Logger?.LogInfo(
            $"[ShopQuantity] Buy-item child confirmation called: item={itemId}, count={buyCount}, cost={costMoney}.");
        if (!ShopQuantityPurchaseContext.TryGet(itemId, out _, out var maxCount, out var selectedCount, out var unitPrice))
        {
            return;
        }

        var requestedCount = buyCount;
        var requestedCost = costMoney;
        buyCount = Math.Clamp(selectedCount, 1, maxCount);
        costMoney = Math.Max(0, unitPrice) * buyCount;

        Plugin.Logger?.LogInfo(
            $"[ShopQuantity] Preparing native buy confirmation for item {itemId}: " +
            $"incoming={requestedCount}, incomingCost={requestedCost}, " +
            $"selected={buyCount}/{maxCount}, cost={costMoney}.");
    }

    private static void Postfix(
        WidgetItemOperateConfirm_BuyItem __instance,
        int itemId,
        int buyCount,
        int costMoney)
    {
        var confirm = __instance.widgetItemOperateConfirm;
        if (confirm is not null)
        {
            ShopQuantityPurchaseContext.SetConfirmation(confirm);
            Plugin.Logger?.LogInfo("[ShopQuantity] Captured confirmation root from buy-item child.");
        }

        if (!ShopQuantityPurchaseContext.TryGet(itemId, out var row, out var maxCount, out var selectedCount, out var unitPrice))
        {
            return;
        }

        try
        {
            if (confirm is null)
            {
                Plugin.Logger?.LogWarning(
                    $"[ShopQuantity] Buy confirmation for item {itemId} has no root widget.");
                return;
            }

            var selection = confirm.selectCount;
            if (selection is null)
            {
                Plugin.Logger?.LogWarning(
                    $"[ShopQuantity] Buy confirmation for item {itemId} has no count selector.");
                return;
            }

            selection.ShowCanSelectCount(maxCount);
            ConfigureSelector(selection, maxCount, Math.Clamp(buyCount > 0 ? buyCount : selectedCount, 1, maxCount));
            ShopQuantityPurchaseContext.UpdateSelectedCount(selection.GetSelectCount());

            var total = Math.Max(0, unitPrice) * selection.GetSelectCount();
            confirm.costMoney = total;
            confirm.widgetItem?.ShowFractionCount(selection.GetSelectCount(), maxCount);
            row.widgetItem?.ShowFractionCount(selection.GetSelectCount(), maxCount);
            ShopQuantityPurchaseContext.RefreshDisplayedCost(confirm, total, unitPrice);
            ShopQuantityPurchaseContext.ApplyConfirmationLayout(confirm, selection);

            Plugin.Logger?.LogInfo(
                $"[ShopQuantity] Native buy confirmation initialized: shop={row.shopId}, item={itemId}, " +
                $"count={selection.GetSelectCount()}/{maxCount}, min={selection.minCount}, " +
                $"max={selection.maxCount}, slider={selection.countSlider?.value}, cost={total}, " +
                $"incomingCost={costMoney}.");
            ShopQuantityPurchaseContext.DumpConfirmationUi(confirm, itemId, "child-initialized", includeLayout: true);
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning(
                $"[ShopQuantity] Initializing native buy confirmation for item {itemId} failed: {exception}");
        }
    }

    internal static void ConfigureSelector(WidgetItemOperateConfirm_SelectCount selector, int maxCount, int selectedCount)
    {
        maxCount = Math.Max(1, maxCount);
        selectedCount = Math.Clamp(selectedCount, 1, maxCount);
        selector.minCount = 1;
        selector.maxCount = maxCount;
        selector.selectCount = selectedCount;

        var slider = selector.countSlider;
        if (slider is not null)
        {
            slider.minValue = 1f;
            slider.maxValue = maxCount;
            slider.wholeNumbers = true;
            slider.value = selectedCount;
        }

        selector.OnSelectCountValueChange();
    }
}

[HarmonyPatch(typeof(WidgetItemOperateConfirm), nameof(WidgetItemOperateConfirm.CreateWidgetItemOperateConfirm))]
internal static class ShopBuyConfirmCreatePatch
{
    private static void Postfix(WidgetItemOperateConfirm __result)
    {
        if (__result is null)
        {
            return;
        }

        ShopQuantityPurchaseContext.SetConfirmation(__result);
        Plugin.Logger?.LogInfo("[ShopQuantity] Captured native purchase confirmation root.");
    }
}

[HarmonyPatch(typeof(WidgetItemOperateConfirm), nameof(WidgetItemOperateConfirm.ShowBuyItemConfirm))]
internal static class ShopBuyConfirmRootShowPatch
{
    private static void Prefix(int itemId, ref int buyCount, ref int costMoney)
    {
        var contextFound = ShopQuantityPurchaseContext.TryGet(
            itemId,
            out _,
            out var maxCount,
            out var selectedCount,
            out var unitPrice);
        if (!contextFound)
        {
            ShopQuantityPurchaseContext.CaptureVisibleRow(
                itemId,
                nameof(WidgetItemOperateConfirm.ShowBuyItemConfirm));
            contextFound = ShopQuantityPurchaseContext.TryGet(
                itemId,
                out _,
                out maxCount,
                out selectedCount,
                out unitPrice);
        }

        Plugin.Logger?.LogInfo(
            $"[ShopQuantity] Native root confirmation called: item={itemId}, " +
            $"incomingCount={buyCount}, incomingCost={costMoney}, context={contextFound}.");
        if (!contextFound)
        {
            return;
        }

        buyCount = Math.Clamp(selectedCount, 1, maxCount);
        costMoney = Math.Max(0, unitPrice) * buyCount;
    }

    private static void Postfix(int itemId, int buyCount)
    {
        ShopQuantityPurchaseContext.ConfigureConfirmation(itemId, buyCount);
    }
}

[HarmonyPatch(typeof(WidgetItemOperateConfirm_SelectCount), nameof(WidgetItemOperateConfirm_SelectCount.ShowCanSelectCount))]
internal static class ShopBuyConfirmShowCountPatch
{
    private static void Prefix(WidgetItemOperateConfirm_SelectCount __instance, ref int maxCount)
    {
        var requestedMax = maxCount;
        if (ShopQuantityPurchaseContext.TryGetCurrent(out _, out _, out _, out var shopMaxCount, out _, out _))
        {
            maxCount = shopMaxCount;
        }

        Plugin.Logger?.LogInfo(
            $"[ShopQuantity] ShowCanSelectCount prefix: incoming={requestedMax}, effective={maxCount}, " +
            $"min={__instance.minCount}, current={__instance.GetSelectCount()}, slider=" +
            $"{__instance.countSlider?.value}, active={__instance.gameObject.activeInHierarchy}.");
    }

    private static void Postfix(WidgetItemOperateConfirm_SelectCount __instance, int maxCount)
    {
        Plugin.Logger?.LogInfo(
            $"[ShopQuantity] ShowCanSelectCount postfix: requested={maxCount}, min={__instance.minCount}, " +
            $"max={__instance.maxCount}, current={__instance.GetSelectCount()}, slider=" +
            $"{__instance.countSlider?.value}, active={__instance.gameObject.activeInHierarchy}.");
        if (!ShopQuantityPurchaseContext.TryGetCurrent(out _, out _, out _, out var shopMaxCount, out var selectedCount, out _))
        {
            return;
        }

        try
        {
            var targetMax = Math.Min(Math.Max(maxCount, 1), shopMaxCount);
            ShopBuyConfirmCountPatch.ConfigureSelector(
                __instance,
                targetMax,
                Math.Clamp(selectedCount, 1, targetMax));
            Plugin.Logger?.LogInfo(
                $"[ShopQuantity] Native confirmation count selector enabled: " +
                $"selected={__instance.GetSelectCount()}/{__instance.maxCount}, slider={__instance.countSlider?.value}.");
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning(
                $"[ShopQuantity] Enabling native confirmation count selector failed: {exception.Message}");
        }
    }
}

[HarmonyPatch(typeof(WidgetItemOperateConfirm_SelectCount), nameof(WidgetItemOperateConfirm_SelectCount.OnSelectCountValueChange))]
internal static class ShopBuyConfirmCountChangedPatch
{
    private static void Postfix(WidgetItemOperateConfirm_SelectCount __instance)
    {
        Plugin.Logger?.LogInfo(
            $"[ShopQuantity] Count selector changed: min={__instance.minCount}, max={__instance.maxCount}, " +
            $"select={__instance.GetSelectCount()}, slider=" +
            $"{__instance.countSlider?.value}, sliderMin={__instance.countSlider?.minValue}, " +
            $"sliderMax={__instance.countSlider?.maxValue}, active={__instance.gameObject.activeInHierarchy}.");
        if (!ShopQuantityPurchaseContext.TryGetCurrent(
                out var row,
                out var shopId,
                out var itemId,
                out var maxCount,
                out _,
                out var unitPrice))
        {
            return;
        }

        try
        {
            var count = Math.Clamp(__instance.GetSelectCount(), 1, maxCount);
            ShopQuantityPurchaseContext.UpdateSelectedCount(count);

            var confirm = __instance.transform.GetComponentInParent<WidgetItemOperateConfirm>();
            confirm?.widgetItem?.ShowFractionCount(count, maxCount);
            if (confirm is not null)
            {
                var totalCost = Math.Max(0, unitPrice) * count;
                confirm.costMoney = totalCost;
                ShopQuantityPurchaseContext.RefreshDisplayedCost(confirm, totalCost, unitPrice);
                ShopQuantityPurchaseContext.ApplyConfirmationLayout(confirm, __instance);
                ShopQuantityPurchaseContext.DumpConfirmationUi(
                    confirm,
                    itemId,
                    $"count-{count}",
                    includeLayout: false);
            }

            Plugin.Logger?.LogInfo(
                $"[ShopQuantity] Native confirmation count changed: shop={shopId}, item={itemId}, " +
                $"count={count}/{maxCount}, cost={Math.Max(0, unitPrice) * count}.");
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning(
                $"[ShopQuantity] Syncing native confirmation count failed: {exception.Message}");
        }
    }
}

[HarmonyPatch(typeof(WidgetItemOperateConfirm), nameof(WidgetItemOperateConfirm.CloseUI))]
internal static class ShopBuyConfirmClosePatch
{
    private static void Postfix(WidgetItemOperateConfirm __instance)
    {
        ShopQuantityPurchaseContext.RestoreConfirmationLayout(__instance);
        ShopQuantityPurchaseContext.Clear();
    }
}

[HarmonyPatch(typeof(UI_Shop), nameof(UI_Shop.CloseUI))]
internal static class ShopBuyConfirmShopClosePatch
{
    private static void Postfix() => ShopQuantityPurchaseContext.Clear();
}
