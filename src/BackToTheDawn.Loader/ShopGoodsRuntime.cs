using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using BackToTheDawn.ModAPI;

namespace BackToTheDawn.Loader;

/// <summary>
/// Reads optional c_shop fields without binding the public API to generated
/// IL2CPP field names. Different game builds expose slightly different names.
/// </summary>
internal static class ShopGoodsRuntime
{
    private static int _scanFrames = -1;
    private static int _scanAttempts;
    private static int _scanCount;

    internal static void ScheduleFullScan()
    {
        _scanFrames = 60;
        _scanAttempts = 0;
        _scanCount = 0;
    }

    internal static void TickFullScan()
    {
        if (_scanFrames < 0)
        {
            return;
        }

        if (_scanFrames-- > 0)
        {
            return;
        }

        _scanAttempts++;
        _scanCount = ScanAll();
        if (_scanCount == 0 && _scanAttempts < 5)
        {
            _scanFrames = 60;
            return;
        }

        _scanFrames = -1;
        Plugin.Logger?.LogInfo(
            $"[ShopGoodsRuntime] Full c_shop scan finished: {_scanCount} goods observed.");
    }

    internal static void Reset()
    {
        _scanFrames = -1;
        _scanAttempts = 0;
        _scanCount = 0;
    }

    internal static void Observe(
        object? nativeGoods,
        int? nativeShopId,
        int itemId,
        ShopKey? explicitShopKey = null)
    {
        if (itemId <= 0)
        {
            return;
        }

        var shopKey = explicitShopKey;
        if (shopKey is null && nativeShopId.HasValue &&
            ShopCatalog.TryGetByNativeId(nativeShopId.Value, out var descriptor))
        {
            shopKey = descriptor.Key;
        }

        if (shopKey is null)
        {
            return;
        }

        var config = ReadObject(nativeGoods, "shopConfig", "ShopConfig") ?? nativeGoods;
        var itemKey = ItemCatalog.ResolveOrCreateKey(itemId);
        var nativePriceType = ReadString(
            config,
            "price_type", "priceType", "cost_type", "costType", "currencyType");

        ShopGoodsCatalog.Upsert(
            new ShopGoodsDefinition(
                shopKey.Value,
                itemKey,
                ReadInt(config, "price", "goods_price", "buy_price", "buyPrice", "cost", "money"),
                ResolveCurrency(nativePriceType, shopKey.Value),
                nativePriceType,
                ReadInt(config, "stock", "goods_stock", "remain", "remainCount", "count"),
                ReadInt(config, "show_day", "showDay", "start_day", "startDay", "dayStart"),
                ReadInt(config, "end_day", "endDay", "stop_day", "stopDay", "dayEnd"),
                ReadString(config, "group", "group_id", "groupId", "goods_group"),
                "ShopGoodsRuntime"));
    }

    private static int ScanAll()
    {
        var cShopType = FindType("c_shop");
        if (cShopType is null)
        {
            Plugin.DebugLog("[ShopGoodsRuntime] c_shop type is not available yet.");
            return 0;
        }

        var seen = new HashSet<int>();
        var observed = 0;
        var visited = 0;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var type in GetTypesSafe(assembly))
            {
                if (!IsShopDataType(type, cShopType))
                {
                    continue;
                }

                foreach (var member in type.GetMembers(
                             BindingFlags.Static |
                             BindingFlags.Public |
                             BindingFlags.NonPublic))
                {
                    if (member is not FieldInfo and not PropertyInfo ||
                        !IsInterestingMember(member.Name))
                    {
                        continue;
                    }

                    object? value;
                    try
                    {
                        value = member switch
                        {
                            FieldInfo field => field.GetValue(null),
                            PropertyInfo property when property.GetMethod is not null =>
                                property.GetValue(null),
                            _ => null,
                        };
                    }
                    catch
                    {
                        continue;
                    }

                    ScanValue(value, cShopType, seen, ref visited, ref observed);
                    if (visited >= 20_000)
                    {
                        return observed;
                    }
                }
            }
        }

        return observed;
    }

    private static void ScanValue(
        object? value,
        Type cShopType,
        HashSet<int> seen,
        ref int visited,
        ref int observed)
    {
        if (value is null || visited >= 20_000)
        {
            return;
        }

        if (value is IEnumerable enumerable && value is not string)
        {
            try
            {
                foreach (var entry in enumerable)
                {
                    ScanValue(entry, cShopType, seen, ref visited, ref observed);
                    if (visited >= 20_000)
                    {
                        break;
                    }
                }
            }
            catch
            {
                // Some IL2CPP collections throw while their native backing data
                // is still being initialized; a later scheduled scan can retry.
            }

            return;
        }

        visited++;
        var type = value.GetType();
        if (!IsCShopValue(type, cShopType))
        {
            return;
        }

        var identity = RuntimeHelpers.GetHashCode(value);
        if (!seen.Add(identity))
        {
            return;
        }

        var itemId = ReadInt(
            value,
            "goods_item", "goodsItem", "item_id", "itemId", "itemID", "thing_id");
        var shopId = ReadInt(value, "shop_id", "shopId", "shopID");
        if (!itemId.HasValue || itemId.Value <= 0 || !shopId.HasValue)
        {
            return;
        }

        var before = ShopGoodsCatalog.All.Count;
        Observe(value, shopId.Value, itemId.Value);
        if (ShopGoodsCatalog.All.Count > before)
        {
            observed++;
        }
    }

    private static bool IsShopDataType(Type type, Type cShopType)
    {
        if (type == cShopType)
        {
            return true;
        }

        var name = type.Name;
        return name.Contains("shop", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("goods", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("config", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("data", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCShopValue(Type type, Type cShopType) =>
        type == cShopType ||
        type.Name.Equals(cShopType.Name, StringComparison.OrdinalIgnoreCase) ||
        type.GetField("goods_item", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is not null;

    private static bool IsInterestingMember(string name) =>
        name.Contains("shop", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("goods", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("config", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("list", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("data", StringComparison.OrdinalIgnoreCase);

    private static Type? FindType(string name)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var type in GetTypesSafe(assembly))
            {
                if (type.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return type;
                }
            }
        }

        return null;
    }

    private static IEnumerable<Type> GetTypesSafe(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type is not null)!;
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }

    private static TradeCurrencyKind ResolveCurrency(string? value, ShopKey shopKey)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (ShopCatalog.TryGet(shopKey, out var shop) && shop.Source == ShopSource.Synthetic)
            {
                return TradeCurrencyKind.Money;
            }

            return shopKey.Path switch
            {
                "maggie_shop" => TradeCurrencyKind.Relationship,
                "bigfoot_shop" or "fang_shop" or "blackclaw_shop" =>
                    TradeCurrencyKind.None,
                "roof_benefit" or "excess_benefit" => TradeCurrencyKind.Discipline,
                "vending_machine" or
                "weekly_supplies" or
                "vice_captain_shop" or
                "beth_doctor_shop" or
                "lunch_counter" or
                "big_bang_pizza" or
                "lottery" or
                "tv_shopping" or
                "church_goods" or
                "rocky_shop" => TradeCurrencyKind.Money,
                _ => TradeCurrencyKind.None,
            };
        }

        if (value.Contains("discipline", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("prestige", StringComparison.OrdinalIgnoreCase))
        {
            return TradeCurrencyKind.Discipline;
        }

        if (value.Contains("friend", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("affection", StringComparison.OrdinalIgnoreCase))
        {
            return TradeCurrencyKind.Relationship;
        }

        if (value.Contains("chip", StringComparison.OrdinalIgnoreCase))
        {
            return TradeCurrencyKind.Chips;
        }

        if (value.Contains("gang", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("contribution", StringComparison.OrdinalIgnoreCase))
        {
            return TradeCurrencyKind.GangContribution;
        }

        if (value.Contains("money", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("cash", StringComparison.OrdinalIgnoreCase))
        {
            return TradeCurrencyKind.Money;
        }

        return TradeCurrencyKind.None;
    }

    private static object? ReadObject(object? target, params string[] names) =>
        ReadMember(target, names);

    private static int? ReadInt(object? target, params string[] names)
    {
        var value = ReadMember(target, names);
        if (value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToInt32(value);
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadString(object? target, params string[] names)
    {
        var value = ReadMember(target, names);
        return value?.ToString();
    }

    private static object? ReadMember(object? target, IReadOnlyList<string> names)
    {
        if (target is null)
        {
            return null;
        }

        try
        {
            var type = target.GetType();
            foreach (var name in names)
            {
                var field = type.GetField(
                    name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                if (field is not null)
                {
                    return field.GetValue(target);
                }

                var property = type.GetProperty(
                    name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                if (property?.GetMethod is not null)
                {
                    return property.GetValue(target);
                }
            }
        }
        catch
        {
            // Runtime metadata is optional; a missing field must not break a shop purchase.
        }

        return null;
    }
}
