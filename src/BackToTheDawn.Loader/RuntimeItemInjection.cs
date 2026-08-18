using BackToTheDawn.ModAPI;

namespace BackToTheDawn.Loader;

/// <summary>
/// Optional bridge from Mod-owned catalog items to the live ConfigData item
/// table. It remains controlled by the Items/EnableRuntimeItemInjection
/// setting because injected IDs are process-local and experimental.
/// </summary>
internal static class RuntimeItemInjection
{
    private const int FirstModItemId = 20_000;
    private static bool _runtimeReadyRaised;

    internal static int TryInject(bool enabled)
    {
        if (!enabled)
        {
            return 0;
        }

        if (!TryGetContainers(
                out var itemList,
                out var itemDictionary))
        {
            return 0;
        }

        var nextId = FindNextId(itemDictionary);
        var injected = 0;
        foreach (var item in ItemRegistry.All)
        {
            var result = InjectOne(item, itemList, itemDictionary, ref nextId);
            if (result.Status == ItemInjectionStatus.Injected)
            {
                injected++;
            }
        }

        if (injected > 0)
        {
            ItemManage.InitItemEffectDict();
        }

        Plugin.Logger?.LogInfo($"[ItemInjection] Injected {injected} Mod item(s) into c_item.");
        return injected;
    }

    internal static ItemInjectionResult TryInjectItem(Item item, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!enabled)
        {
            return new ItemInjectionResult(
                item,
                ItemInjectionStatus.InjectionDisabled,
                null,
                "Runtime item injection is disabled.");
        }

        if (!TryGetContainers(out var itemList, out var itemDictionary))
        {
            return new ItemInjectionResult(
                item,
                ItemInjectionStatus.RuntimeUnavailable,
                null,
                "The game's runtime item containers are not available yet.");
        }

        var nextId = FindNextId(itemDictionary);
        var result = InjectOne(item, itemList, itemDictionary, ref nextId);
        if (result.Status == ItemInjectionStatus.Injected)
        {
            ItemManage.InitItemEffectDict();
        }

        return result;
    }

    internal static void OnItemRegistered(Item item)
    {
        // Registration can happen while the Mod host is still starting. Defer
        // the actual c_item mutation until the first GameplayReady pass so
        // the runtime catalog and localization tables are initialized first.
        if (!_runtimeReadyRaised)
        {
            return;
        }

        var result = TryInjectItem(item, Plugin.RuntimeItemInjectionEnabled);
        if (result.Status is ItemInjectionStatus.Injected or ItemInjectionStatus.AlreadyInjected)
        {
            return;
        }

        if (result.Status is ItemInjectionStatus.Failed or ItemInjectionStatus.TemplateUnavailable)
        {
            Plugin.Logger?.LogWarning(
                $"[ItemInjection] Late registration for {item.Key} was not injected: " +
                result.Message);
        }
    }

    internal static bool MarkRuntimeReady()
    {
        if (_runtimeReadyRaised)
        {
            return false;
        }

        _runtimeReadyRaised = true;
        return true;
    }

    internal static void Reset()
    {
        _runtimeReadyRaised = false;
    }

    private static bool TryGetContainers(
        out Il2CppSystem.Collections.Generic.List<c_item> itemList,
        out Il2CppSystem.Collections.Generic.Dictionary<int, c_item> itemDictionary)
    {
        itemList = null!;
        itemDictionary = null!;
        try
        {
            var config = ConfigData.singleton;
            itemList = config?.item!;
            itemDictionary = ConfigData.dict_item!;
            if (itemList is not null && itemDictionary is not null)
            {
                return true;
            }
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning(
                $"[ItemInjection] Reading ConfigData item containers failed: {exception.Message}");
        }

        Plugin.Logger?.LogWarning(
            "[ItemInjection] ConfigData item containers are not available; injection skipped.");
        return false;
    }

    private static ItemInjectionResult InjectOne(
        Item item,
        Il2CppSystem.Collections.Generic.List<c_item> itemList,
        Il2CppSystem.Collections.Generic.Dictionary<int, c_item> itemDictionary,
        ref int nextId)
    {
        if (ItemIdResolver.TryGetId(item.Key, out var existingId))
        {
            return new ItemInjectionResult(
                item,
                ItemInjectionStatus.AlreadyInjected,
                existingId,
                $"Item '{item.Key}' is already bound to runtime ID {existingId}.");
        }

        var template = FindTemplate(itemList, item.OccupiesFullGrid);
        if (template is null)
        {
            var message =
                $"No {(item.OccupiesFullGrid ? "full" : "small")}-grid template found.";
            Plugin.Logger?.LogWarning($"[ItemInjection] {message} for {item.Key}; item skipped.");
            return new ItemInjectionResult(
                item,
                ItemInjectionStatus.TemplateUnavailable,
                null,
                message);
        }

        while (itemDictionary.ContainsKey(nextId))
        {
            nextId++;
        }

        var runtimeId = nextId;
        try
        {
            var runtimeItem = CloneTemplate(template);
            ApplyDefinition(runtimeItem, item, runtimeId);
            itemList.Add(runtimeItem);
            itemDictionary.Add(runtimeId, runtimeItem);
            AddLocalization(item, runtimeId);
            ItemCatalog.BindRuntimeId(item.Key, runtimeId);
            Plugin.Logger?.LogInfo(
                $"[ItemInjection] Added {item.Key} as runtime item {runtimeId} " +
                $"(fullGrid={item.OccupiesFullGrid}, template={template.item_id}).");
            nextId++;
            return new ItemInjectionResult(
                item,
                ItemInjectionStatus.Injected,
                runtimeId,
                $"Item '{item.Key}' was injected as runtime ID {runtimeId}.");
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError(
                $"[ItemInjection] Failed to inject {item.Key} as runtime item {runtimeId}: " +
                exception);
            return new ItemInjectionResult(
                item,
                ItemInjectionStatus.Failed,
                null,
                exception.Message);
        }
    }

    private static int FindNextId(Il2CppSystem.Collections.Generic.Dictionary<int, c_item> items)
    {
        var nextId = FirstModItemId;
        while (items.ContainsKey(nextId))
        {
            nextId++;
        }

        return nextId;
    }

    private static c_item? FindTemplate(
        Il2CppSystem.Collections.Generic.List<c_item> items,
        bool fullGrid)
    {
        foreach (var item in items)
        {
            if (item is null || !SafeIsItem(item))
            {
                continue;
            }

            if (SafeIsFullGrid(item) == fullGrid && item.max_use > 0)
            {
                return item;
            }
        }

        foreach (var item in items)
        {
            if (item is not null)
            {
                return item;
            }
        }

        return null;
    }

    private static bool SafeIsItem(c_item item)
    {
        try
        {
            return ItemManage.IsItem(item.item_id);
        }
        catch
        {
            return false;
        }
    }

    private static bool SafeIsFullGrid(c_item item)
    {
        try
        {
            return c_itemExtension.IsFullGrid(item);
        }
        catch
        {
            return item.volume != 0;
        }
    }

    private static c_item CloneTemplate(c_item source)
    {
        var target = new c_item
        {
            batch_use = source.batch_use,
            battery_p = source.battery_p,
            buy_item_price = source.buy_item_price,
            contraband = source.contraband,
            dice_durability = source.dice_durability,
            exhaust_get_item = source.exhaust_get_item,
            feedable = source.feedable,
            fight_durability = source.fight_durability,
            gift_opinion = source.gift_opinion,
            handbook_type = source.handbook_type,
            interactive_type = source.interactive_type,
            item_id = source.item_id,
            item_p_A = source.item_p_A,
            item_p_A_weaken = source.item_p_A_weaken,
            item_p_B = source.item_p_B,
            item_p_B_weaken = source.item_p_B_weaken,
            item_pic_demo = source.item_pic_demo,
            item_type = source.item_type,
            item_type_2 = source.item_type_2,
            item_value = source.item_value,
            key_to_object = source.key_to_object,
            L_background_des = source.L_background_des,
            L_battery_des = source.L_battery_des,
            L_item_des_A = source.L_item_des_A,
            L_item_des_B = source.L_item_des_B,
            L_item_name = source.L_item_name,
            L_side_effect_des = source.L_side_effect_des,
            L_use_limit_des = source.L_use_limit_des,
            maintenance = source.maintenance,
            max_battery = source.max_battery,
            max_durability = source.max_durability,
            max_stack = source.max_stack,
            max_use = source.max_use,
            produce_energy = source.produce_energy,
            produce_leader = source.produce_leader,
            produce_material = source.produce_material,
            produce_number = source.produce_number,
            produce_order = source.produce_order,
            produce_time = source.produce_time,
            produce_type = source.produce_type,
            produce_unlock_1 = source.produce_unlock_1,
            side_effect_p = source.side_effect_p,
            tradable = source.tradable,
            treasure = source.treasure,
            use_limit_p = source.use_limit_p,
            volume = source.volume,
            work_durability = source.work_durability
        };
        return target;
    }

    private static void ApplyDefinition(c_item runtimeItem, Item item, int id)
    {
        var definition = item.Definition;
        runtimeItem.item_id = id;
        runtimeItem.item_type = IsCustomType(definition.ItemType)
            ? runtimeItem.item_type
            : definition.ItemType;
        runtimeItem.item_type_2 = string.IsNullOrWhiteSpace(definition.ItemType2)
            ? runtimeItem.item_type_2
            : definition.ItemType2;
        runtimeItem.max_stack = Math.Max(1, definition.MaxStack);
        runtimeItem.max_use = Math.Max(0, definition.MaxUse);
        runtimeItem.volume = definition.OccupiesFullGrid ? 1 : 0;
        runtimeItem.item_p_A = definition.ParameterA;
        runtimeItem.item_p_B = definition.ParameterB;

        var key = definition.Key.ToString();
        runtimeItem.L_item_name = key + ".name";
        runtimeItem.L_item_des_A = key + ".description";
        runtimeItem.L_background_des = key + ".background";
        // Leave the optional second description empty; the game otherwise
        // renders the unresolved key when no second paragraph is supplied.
        runtimeItem.L_item_des_B = string.Empty;
        runtimeItem.L_side_effect_des = string.Empty;
        runtimeItem.L_use_limit_des = string.Empty;
        runtimeItem.L_battery_des = string.Empty;
    }

    private static void AddLocalization(Item item, int _)
    {
        var key = item.Key.ToString();
        RuntimeItemAssets.ApplyLocalization(item);
        AddLanguage(key + ".name", item.DisplayName);
        AddLanguage(key + ".description", item.BackgroundDescription);
        AddLanguage(key + ".background", item.BackgroundDescription);
    }

    internal static void AddLocalizationValues(Item item, string name, string description)
    {
        var key = item.Key.ToString();
        AddLanguage(key + ".name", name);
        AddLanguage(key + ".description", description);
        AddLanguage(key + ".background", description);
    }

    private static void AddLanguage(string key, string value)
    {
        var dictionary = LanguageData.dict_Data;
        if (dictionary is null || dictionary.ContainsKey(key))
        {
            return;
        }

        var entry = new c_languagedata
        {
            ID = key,
            v = value
        };
        dictionary.Add(key, entry);
        LanguageData.singleton?.Data?.Add(entry);
    }

    private static bool IsCustomType(string value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Equals("custom", StringComparison.OrdinalIgnoreCase);
}
