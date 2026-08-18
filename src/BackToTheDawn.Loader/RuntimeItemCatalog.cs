using System.Text.Json;
using BackToTheDawn.ModAPI;
using BepInEx;

namespace BackToTheDawn.Loader;

internal static class RuntimeItemCatalog
{
    private sealed record RuntimeEffectEntry(
        int Id,
        int FunctionType,
        string Description);

    private sealed record RuntimeItemUseEntry(
        int ItemId,
        int EffectId,
        string Action,
        int ValueType,
        double Value,
        int ValueParameter,
        bool IsPercent,
        int Duration,
        int TimeType,
        double RandomMin,
        double RandomMax,
        string RandomParameter);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static bool _captured;

    internal static void CaptureOnce()
    {
        if (_captured)
        {
            return;
        }

        _captured = true;

        try
        {
            var config = ConfigData.singleton;
            if (config is null || config.item is null)
            {
                Plugin.Logger?.LogWarning(
                    "[ItemCatalog] ConfigData.singleton.item is not available at GameplayReady.");
                return;
            }

            var items = config.item;

            var entries = new List<object>();
            var runtimeDefinitions = new List<(int Id, ItemDefinition Definition)>();
            foreach (var item in items)
            {
                if (item is null)
                {
                    continue;
                }

                entries.Add(CreateEntry(item));
                var itemId = item.item_id;
                var itemKey = ItemCatalog.ResolveOrCreateKey(itemId);
                runtimeDefinitions.Add((itemId, CreateDefinition(item, itemKey)));
            }

            var itemTypes = CaptureItemTypes(config.item_type);
            var itemTypes2 = CaptureItemTypes2(config.item_type_2);
            var itemBuffs = CaptureItemBuffs(config.item_buff);
            var itemEffects = CaptureItemEffects(config.EX_itemeffect);
            var itemUses = CaptureItemUses(config.EX_itemuse);
            var itemPlaceEffects = CaptureItemPlaceEffects(config.EX_itemPlaceEffect);
            var itemConditions = CaptureItemConditions(config.EX_itemecondition);
            var languageX = CaptureLanguageX(config.EX_language_x);

            var document = new
            {
                schemaVersion = 1,
                capturedAtUtc = DateTime.UtcNow,
                gameBuild = "23125213",
                source = new
                {
                    configType = "ConfigData.singleton.item",
                    itemCount = entries.Count,
                    itemTypeCount = itemTypes.Count,
                    itemType2Count = itemTypes2.Count,
                    itemBuffCount = itemBuffs.Count,
                    itemEffectCount = itemEffects.Count,
                    itemUseCount = itemUses.Count,
                    itemPlaceEffectCount = itemPlaceEffects.Count,
                    itemConditionCount = itemConditions.Count,
                    languageXCount = languageX.Count,
                    note = "Read-only runtime snapshot; values come from the live game configuration."
                },
                entries,
                itemTypes,
                itemTypes2,
                itemBuffs,
                itemEffects,
                itemUses,
                itemPlaceEffects,
                itemConditions,
                languageX
            };

            var outputPath = Path.Combine(
                Paths.ConfigPath,
                "dev.backtothedawn.loader.item-runtime-catalog.json");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, JsonSerializer.Serialize(document, JsonOptions));

            foreach (var runtimeDefinition in runtimeDefinitions)
            {
                ItemCatalog.PublishRuntimeEntry(
                    runtimeDefinition.Id,
                    runtimeDefinition.Definition);
            }

            ItemCatalog.BeginRuntimeEffects();
            var effectKeys = itemEffects.ToDictionary(
                effect => effect.Id,
                effect => ItemEffectKeyRegistry.Create(effect.Id, effect.Description));
            foreach (var itemUse in itemUses)
            {
                if (!effectKeys.TryGetValue(itemUse.EffectId, out var effectKey))
                {
                    continue;
                }

                var itemKey = ItemCatalog.ResolveOrCreateKey(itemUse.ItemId);
                var effectDescription = itemEffects
                    .FirstOrDefault(effect => effect.Id == itemUse.EffectId)
                    ?.Description ?? string.Empty;
                ItemCatalog.PublishRuntimeEffect(
                    itemKey,
                    itemUse.EffectId,
                    new ItemEffectDefinition(
                        effectKey,
                        effectDescription,
                        itemUse.Action,
                        itemUse.ValueType,
                        itemUse.Value,
                        itemUse.ValueParameter,
                        itemUse.IsPercent,
                        itemUse.Duration,
                        itemUse.TimeType,
                        itemUse.RandomMin,
                        itemUse.RandomMax,
                        itemUse.RandomParameter));
            }

            ItemCatalog.MarkRuntimeReady();
            GameEvents.RaiseItemCatalogReady(runtimeDefinitions.Count);

            Plugin.Logger?.LogInfo(
                $"[ItemCatalog] Captured {entries.Count} items, {itemTypes.Count} types, " +
                $"{itemBuffs.Count} buffs, {itemUses.Count} uses, and {itemEffects.Count} effects " +
                $"plus {languageX.Count} localization keys to '{outputPath}'.");
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError($"[ItemCatalog] Runtime capture failed: {exception}");
        }
    }

    internal static void Reset() => _captured = false;

    private static object CreateEntry(c_item item)
    {
        var id = item.item_id;
        return new
        {
            id,
            name = ReadString(() => ItemManage.GetItemName(id)),
            backgroundDescription = ReadString(() => ItemManage.GetItemBackgroundDesc(id)),
            itemNameKey = item.L_item_name,
            itemDescriptionAKey = item.L_item_des_A,
            itemDescriptionBKey = item.L_item_des_B,
            backgroundDescriptionKey = item.L_background_des,
            sideEffectDescriptionKey = item.L_side_effect_des,
            useLimitDescriptionKey = item.L_use_limit_des,
            batteryDescriptionKey = item.L_battery_des,
            itemType = item.item_type,
            itemType2 = item.item_type_2,
            occupiesFullGrid = ReadBool(() => c_itemExtension.IsFullGrid(item)) ?? item.volume != 0,
            interactiveType = item.interactive_type,
            handbookType = item.handbook_type,
            itemValue = item.item_value,
            buyPrice = item.buy_item_price,
            contraband = item.contraband,
            tradable = item.tradable,
            treasure = item.treasure,
            maxStack = item.max_stack,
            maxUse = item.max_use,
            batchUse = item.batch_use,
            maxDurability = item.max_durability,
            fightDurability = item.fight_durability,
            diceDurability = item.dice_durability,
            workDurability = item.work_durability,
            maxBattery = item.max_battery,
            batteryParameter = item.battery_p,
            volume = item.volume,
            feedable = item.feedable,
            giftOpinion = item.gift_opinion,
            maintenance = item.maintenance,
            sideEffectParameter = item.side_effect_p,
            useLimitParameter = item.use_limit_p,
            exhaustGetItem = item.exhaust_get_item,
            produceMaterial = item.produce_material,
            produceNumber = item.produce_number,
            produceOrder = item.produce_order,
            produceTime = item.produce_time,
            produceType = item.produce_type,
            produceEnergy = item.produce_energy,
            produceLeader = item.produce_leader,
            produceUnlock = item.produce_unlock_1,
            parameterA = item.item_p_A,
            parameterB = item.item_p_B,
            weakenedParameterA = item.item_p_A_weaken,
            weakenedParameterB = item.item_p_B_weaken,
            pictureKey = item.item_pic_demo,
            objectKey = item.key_to_object,
            isItem = ReadBool(() => ItemManage.IsItem(id)),
            isAttribute = ReadBool(() => ItemManage.IsAttribute(id)),
            isEquipment = ReadBool(() => ItemManage.IsEquipment(id)),
            isWeapon = ReadBool(() => ItemManage.IsWeapon(id))
        };
    }

    private static ItemDefinition CreateDefinition(c_item item, ItemKey key)
    {
        var id = item.item_id;
        return new ItemDefinition(
            key,
            ReadString(() => ItemManage.GetItemName(id)) ?? string.Empty,
            ReadString(() => item.item_type) ?? string.Empty,
            ReadString(() => item.item_type_2) ?? string.Empty,
            ReadString(() => ItemManage.GetItemBackgroundDesc(id)) ?? string.Empty,
            ReadBool(() => ItemManage.IsEquipment(id)) ?? false,
            ReadBool(() => ItemManage.IsWeapon(id)) ?? false,
            item.max_stack,
            item.max_use,
            ReadString(() => item.item_p_A) ?? string.Empty,
            ReadString(() => item.item_p_B) ?? string.Empty,
            ReadBool(() => c_itemExtension.IsFullGrid(item)) ?? item.volume != 0);
    }

    private static List<object> CaptureItemTypes(
        Il2CppSystem.Collections.Generic.List<c_item_type>? values)
    {
        var result = new List<object>();
        if (values is null)
        {
            return result;
        }

        foreach (var value in values)
        {
            if (value is null)
            {
                continue;
            }

            result.Add(new
            {
                id = value.item_type,
                equipmentType = value.equipment_type,
                nameKey = value.L_item_type_name,
                name = ReadLanguage(value.L_item_type_name)
            });
        }

        return result;
    }

    private static List<object> CaptureItemTypes2(
        Il2CppSystem.Collections.Generic.List<c_item_type_2>? values)
    {
        var result = new List<object>();
        if (values is null)
        {
            return result;
        }

        foreach (var value in values)
        {
            if (value is null)
            {
                continue;
            }

            result.Add(new
            {
                id = value.item_type_2,
                nameKey = value.L_item_type_2_name,
                name = ReadLanguage(value.L_item_type_2_name)
            });
        }

        return result;
    }

    private static List<object> CaptureItemBuffs(
        Il2CppSystem.Collections.Generic.List<c_item_buff>? values)
    {
        var result = new List<object>();
        if (values is null)
        {
            return result;
        }

        foreach (var value in values)
        {
            if (value is null)
            {
                continue;
            }

            result.Add(new
            {
                itemId = value.item_id,
                function = value.item_buff_func,
                type = value.item_buff_type,
                parameter = value.item_buff_p,
                display = value.L_item_buff_show
            });
        }

        return result;
    }

    private static List<RuntimeEffectEntry> CaptureItemEffects(
        Il2CppSystem.Collections.Generic.List<c_EX_itemeffect>? values)
    {
        var result = new List<RuntimeEffectEntry>();
        if (values is null)
        {
            return result;
        }

        foreach (var value in values)
        {
            if (value is null)
            {
                continue;
            }

            result.Add(new RuntimeEffectEntry(
                value.effect_id,
                value.effect_function_type,
                value.effect_desc ?? string.Empty));
        }

        return result;
    }

    private static List<RuntimeItemUseEntry> CaptureItemUses(
        Il2CppSystem.Collections.Generic.List<c_EX_itemuse>? values)
    {
        var result = new List<RuntimeItemUseEntry>();
        if (values is null)
        {
            return result;
        }

        foreach (var value in values)
        {
            if (value is null)
            {
                continue;
            }

            result.Add(new RuntimeItemUseEntry(
                value.item_id,
                value.item_effect_id,
                value.effect_action ?? string.Empty,
                value.effect_value_type,
                value.effect_value,
                value.value_p,
                value.effect_is_percent,
                value.effect_duration,
                value.effect_time_type,
                value.effect_random_value_min,
                value.effect_random_value_max,
                value.random_value_p ?? string.Empty));
        }

        return result;
    }

    private static List<object> CaptureItemPlaceEffects(
        Il2CppSystem.Collections.Generic.List<c_EX_itemPlaceEffect>? values)
    {
        var result = new List<object>();
        if (values is null)
        {
            return result;
        }

        foreach (var value in values)
        {
            if (value is null)
            {
                continue;
            }

            result.Add(new
            {
                itemId = value.item_id,
                effectId = value.effect_id,
                itemPlace = value.item_place,
                valueType = value.effect_value_type,
                valueParameter = value.value_p,
                duration = value.effect_duration,
                timeType = value.effect_time_type,
                timeScale = value.effect_time_scale,
                randomParameter = value.random_value_p
            });
        }

        return result;
    }

    private static List<object> CaptureItemConditions(
        Il2CppSystem.Collections.Generic.List<c_EX_itemecondition>? values)
    {
        var result = new List<object>();
        if (values is null)
        {
            return result;
        }

        foreach (var value in values)
        {
            if (value is null)
            {
                continue;
            }

            result.Add(new
            {
                id = value.condition_id,
                description = value.condition_desc
            });
        }

        return result;
    }

    private static List<object> CaptureLanguageX(
        Il2CppSystem.Collections.Generic.List<c_EX_language_x>? values)
    {
        var result = new List<object>();
        if (values is null)
        {
            return result;
        }

        foreach (var value in values)
        {
            if (value is null)
            {
                continue;
            }

            result.Add(new
            {
                id = value.id,
                value = value.x_key
            });
        }

        return result;
    }

    private static string? ReadString(Func<string?> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadLanguage(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return key;
        }

        return ReadString(() => LanguageData.GetLanguage(key, true));
    }

    private static bool? ReadBool(Func<bool> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return null;
        }
    }
}
