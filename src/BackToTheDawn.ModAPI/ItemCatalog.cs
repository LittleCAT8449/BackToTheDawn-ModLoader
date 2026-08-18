namespace BackToTheDawn.ModAPI;

/// <summary>
/// A stable namespaced item identifier, for example
/// <c>backtothedawn:apple</c>.
/// </summary>
public readonly record struct ItemKey
{
    public ItemKey(string @namespace, string path)
    {
        Namespace = NormalizePart(@namespace, nameof(@namespace), allowSlash: false);
        Path = NormalizePart(path, nameof(path), allowSlash: true);
    }

    public string Namespace { get; }

    public string Path { get; }

    public override string ToString() => Namespace + ":" + Path;

    public static bool TryParse(string? value, out ItemKey key)
    {
        key = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var separator = value.IndexOf(':');
        if (separator <= 0 || separator != value.LastIndexOf(':') || separator == value.Length - 1)
        {
            return false;
        }

        try
        {
            key = new ItemKey(value[..separator], value[(separator + 1)..]);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string NormalizePart(
        string value,
        string parameterName,
        bool allowSlash)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("An item key component is required.", parameterName);
        }

        var normalized = value.Trim().ToLowerInvariant();
        foreach (var character in normalized)
        {
            var valid = ((character >= 'a' && character <= 'z') ||
                         (character >= '0' && character <= '9')) ||
                        
                        character is '_' or '-' or '.' ||
                        (allowSlash && character == '/');
            if (!valid)
            {
                throw new ArgumentException(
                    $"Item key component '{value}' contains an invalid character.",
                    parameterName);
            }
        }

        return normalized;
    }
}

/// <summary>
/// Public item information. Numeric game IDs are intentionally not part of
/// this object; use <see cref="ItemIdResolver"/> when a low-level ID is
/// explicitly required.
/// </summary>
public sealed record ItemDefinition(
    ItemKey Key,
    string DisplayName,
    string ItemType,
    string ItemType2,
    string BackgroundDescription,
    bool IsEquipment,
    bool IsWeapon,
    int MaxStack,
    int MaxUse,
    string ParameterA,
    string ParameterB,
    bool OccupiesFullGrid = false,
    ItemResources? Resources = null);

/// <summary>
/// One declared effect applied when an item is used. The effect key is
/// namespaced; the original numeric effect ID is available only through
/// <see cref="EffectIdResolver"/>.
/// </summary>
public sealed record ItemEffectDefinition(
    ItemKey Key,
    string DisplayName,
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

/// <summary>
/// Read-only access to the game's namespaced item registry.
/// </summary>
public static class ItemCatalog
{
    private static readonly object SyncRoot = new();
    private static Dictionary<string, ItemDefinition> _definitions =
        new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, int> _idsByKey =
        new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<int, List<ItemKey>> _keysById = new();
    private static IReadOnlyList<ItemDefinition> _all = Array.Empty<ItemDefinition>();
    private static Dictionary<string, List<ItemEffectDefinition>> _effectsByItem =
        new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, int> _effectIdsByKey =
        new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<int, ItemKey> _effectKeysById = new();

    /// <summary>
    /// True once the static namespaced registry has been initialized.
    /// </summary>
    public static bool IsAvailable { get; private set; }

    /// <summary>
    /// True after the live ConfigData item table has enriched the registry.
    /// </summary>
    public static bool IsRuntimeReady { get; private set; }

    /// <summary>
    /// Returns item definitions without exposing numeric IDs.
    /// </summary>
    public static IReadOnlyList<ItemDefinition> All => _all;

    /// <summary>
    /// Registers a Mod-owned catalog entry. This creates a namespaced
    /// definition for Mod APIs; it does not mutate the game's c_item table or
    /// assign a numeric game ID.
    ///
    /// Prefer <see cref="ItemRegistry.Register(Item)"/> with a custom
    /// <see cref="Item"/> subclass for new Mod items.
    /// </summary>
    public static bool TryRegister(ItemDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Key.Namespace.Equals("backtothedawn", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        lock (SyncRoot)
        {
            var key = definition.Key.ToString();
            if (_definitions.ContainsKey(key))
            {
                return false;
            }

            _definitions[key] = definition;
            RebuildAll();
            IsAvailable = true;
            return true;
        }
    }

    internal static bool TryUnregister(ItemKey key)
    {
        if (key.Namespace.Equals("backtothedawn", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        lock (SyncRoot)
        {
            var keyText = key.ToString();
            if (_idsByKey.ContainsKey(keyText))
            {
                return false;
            }

            if (!_definitions.Remove(keyText))
            {
                return false;
            }

            _idsByKey.Remove(keyText);
            _effectsByItem.Remove(keyText);
            RebuildAll();
            IsAvailable = _definitions.Count > 0;
            return true;
        }
    }

    public static bool TryGet(string key, out ItemDefinition definition)
    {
        if (!ItemKey.TryParse(key, out var itemKey))
        {
            definition = null!;
            return false;
        }

        return TryGet(itemKey, out definition);
    }

    public static bool TryGet(ItemKey key, out ItemDefinition definition)
    {
        lock (SyncRoot)
        {
            return _definitions.TryGetValue(key.ToString(), out definition!);
        }
    }

    /// <summary>
    /// Returns the declared use effects for an item. The returned list is
    /// empty when the runtime effect table is not available or the item has
    /// no configured use effects.
    /// </summary>
    public static IReadOnlyList<ItemEffectDefinition> GetEffects(string key)
    {
        if (!ItemKey.TryParse(key, out var itemKey))
        {
            return Array.Empty<ItemEffectDefinition>();
        }

        return GetEffects(itemKey);
    }

    public static IReadOnlyList<ItemEffectDefinition> GetEffects(ItemKey key)
    {
        lock (SyncRoot)
        {
            return _effectsByItem.TryGetValue(key.ToString(), out var effects)
                ? effects.ToArray()
                : Array.Empty<ItemEffectDefinition>();
        }
    }

    internal static void InitializeStatic(IEnumerable<(ItemKey Key, int Id)> bindings)
    {
        lock (SyncRoot)
        {
            _definitions = new Dictionary<string, ItemDefinition>(StringComparer.OrdinalIgnoreCase);
            _idsByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _keysById = new Dictionary<int, List<ItemKey>>();
            _effectsByItem = new Dictionary<string, List<ItemEffectDefinition>>(
                StringComparer.OrdinalIgnoreCase);
            _effectIdsByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _effectKeysById = new Dictionary<int, ItemKey>();

            foreach (var binding in bindings)
            {
                var keyText = binding.Key.ToString();
                if (!_idsByKey.ContainsKey(keyText))
                {
                    _idsByKey[keyText] = binding.Id;
                }

                if (!_keysById.TryGetValue(binding.Id, out var keys))
                {
                    keys = new List<ItemKey>();
                    _keysById[binding.Id] = keys;
                }

                if (!keys.Contains(binding.Key))
                {
                    keys.Add(binding.Key);
                }

                if (!_definitions.ContainsKey(keyText))
                {
                    _definitions[keyText] = CreatePlaceholder(binding.Key);
                }
            }

            RebuildAll();
            IsAvailable = _definitions.Count > 0;
            IsRuntimeReady = false;
        }
    }

    internal static ItemKey ResolveOrCreateKey(int id)
    {
        lock (SyncRoot)
        {
            if (_keysById.TryGetValue(id, out var existing) && existing.Count > 0)
            {
                return existing[0];
            }

            var key = new ItemKey("backtothedawn", "item_" + id);
            _idsByKey[key.ToString()] = id;
            _keysById[id] = new List<ItemKey> { key };
            _definitions[key.ToString()] = CreatePlaceholder(key);
            RebuildAll();
            IsAvailable = true;
            return key;
        }
    }

    internal static void PublishRuntimeEntry(
        int id,
        ItemDefinition definition)
    {
        lock (SyncRoot)
        {
            var key = ResolveOrCreateKey(id);
            if (!_keysById.TryGetValue(id, out var keys))
            {
                keys = new List<ItemKey> { key };
                _keysById[id] = keys;
            }

            foreach (var alias in keys)
            {
                _definitions[alias.ToString()] = definition with { Key = alias };
            }

            RebuildAll();
            IsAvailable = true;
        }
    }

    internal static void MarkRuntimeReady() => IsRuntimeReady = true;

    internal static void BeginRuntimeEffects()
    {
        lock (SyncRoot)
        {
            _effectsByItem = new Dictionary<string, List<ItemEffectDefinition>>(
                StringComparer.OrdinalIgnoreCase);
            _effectIdsByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _effectKeysById = new Dictionary<int, ItemKey>();
        }
    }

    internal static void PublishRuntimeEffect(
        ItemKey itemKey,
        int effectId,
        ItemEffectDefinition effect)
    {
        lock (SyncRoot)
        {
            var effectKeyText = effect.Key.ToString();
            _effectIdsByKey[effectKeyText] = effectId;
            _effectKeysById[effectId] = effect.Key;

            var itemKeys = _idsByKey.TryGetValue(itemKey.ToString(), out var itemId) &&
                            _keysById.TryGetValue(itemId, out var aliases)
                ? aliases
                : new List<ItemKey> { itemKey };
            foreach (var alias in itemKeys)
            {
                if (!_effectsByItem.TryGetValue(alias.ToString(), out var effects))
                {
                    effects = new List<ItemEffectDefinition>();
                    _effectsByItem[alias.ToString()] = effects;
                }

                effects.Add(effect);
            }
        }
    }

    internal static bool TryGetId(ItemKey key, out int id)
    {
        lock (SyncRoot)
        {
            return _idsByKey.TryGetValue(key.ToString(), out id);
        }
    }

    internal static void BindRuntimeId(ItemKey key, int id)
    {
        lock (SyncRoot)
        {
            var keyText = key.ToString();
            if (!_definitions.ContainsKey(keyText))
            {
                return;
            }

            _idsByKey[keyText] = id;
            if (!_keysById.TryGetValue(id, out var keys))
            {
                keys = new List<ItemKey>();
                _keysById[id] = keys;
            }

            if (!keys.Contains(key))
            {
                keys.Add(key);
            }
        }
    }

    internal static bool TryGetKey(int id, out ItemKey key)
    {
        lock (SyncRoot)
        {
            if (_keysById.TryGetValue(id, out var keys) && keys.Count > 0)
            {
                key = keys[0];
                return true;
            }
        }

        key = default;
        return false;
    }

    internal static bool TryGetEffectId(ItemKey key, out int id)
    {
        lock (SyncRoot)
        {
            return _effectIdsByKey.TryGetValue(key.ToString(), out id);
        }
    }

    internal static bool TryGetEffectKey(int id, out ItemKey key)
    {
        lock (SyncRoot)
        {
            return _effectKeysById.TryGetValue(id, out key);
        }
    }

    internal static void Reset()
    {
        lock (SyncRoot)
        {
            _definitions = new Dictionary<string, ItemDefinition>(StringComparer.OrdinalIgnoreCase);
            _idsByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _keysById = new Dictionary<int, List<ItemKey>>();
            _all = Array.Empty<ItemDefinition>();
            _effectsByItem = new Dictionary<string, List<ItemEffectDefinition>>(
                StringComparer.OrdinalIgnoreCase);
            _effectIdsByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _effectKeysById = new Dictionary<int, ItemKey>();
            IsAvailable = false;
            IsRuntimeReady = false;
        }
    }

    private static ItemDefinition CreatePlaceholder(ItemKey key) =>
        new(key, string.Empty, string.Empty, string.Empty, string.Empty, false, false, 0, 0, string.Empty, string.Empty);

    private static void RebuildAll()
    {
        _all = _definitions.Values
            .OrderBy(definition => definition.Key.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

/// <summary>
/// Explicit low-level bridge for code that must call a game method requiring
/// the original integer item ID. Normal item reads should use ItemCatalog.
/// </summary>
public static class ItemIdResolver
{
    public static bool TryGetId(ItemKey key, out int id) => ItemCatalog.TryGetId(key, out id);

    public static bool TryGetId(string key, out int id)
    {
        if (!ItemKey.TryParse(key, out var itemKey))
        {
            id = 0;
            return false;
        }

        return TryGetId(itemKey, out id);
    }

    public static int GetId(ItemKey key)
    {
        if (TryGetId(key, out var id))
        {
            return id;
        }

        throw new KeyNotFoundException($"The item key '{key}' is not registered.");
    }

    public static int GetId(string key)
    {
        if (!ItemKey.TryParse(key, out var itemKey))
        {
            throw new FormatException($"Invalid item key '{key}'.");
        }

        return GetId(itemKey);
    }

    public static bool TryGetKey(int id, out ItemKey key) => ItemCatalog.TryGetKey(id, out key);
}

/// <summary>
/// Explicit low-level bridge for code that must call a game method requiring
/// the original integer effect ID. Normal effect reads should use
/// ItemCatalog.GetEffects().
/// </summary>
public static class EffectIdResolver
{
    public static bool TryGetId(ItemKey key, out int id) => ItemCatalog.TryGetEffectId(key, out id);

    public static bool TryGetId(string key, out int id)
    {
        if (!ItemKey.TryParse(key, out var effectKey))
        {
            id = 0;
            return false;
        }

        return TryGetId(effectKey, out id);
    }

    public static int GetId(ItemKey key)
    {
        if (TryGetId(key, out var id))
        {
            return id;
        }

        throw new KeyNotFoundException($"The effect key '{key}' is not registered.");
    }

    public static int GetId(string key)
    {
        if (!ItemKey.TryParse(key, out var effectKey))
        {
            throw new FormatException($"Invalid effect key '{key}'.");
        }

        return GetId(effectKey);
    }

    public static bool TryGetKey(int id, out ItemKey key) => ItemCatalog.TryGetEffectKey(id, out key);
}

/// <summary>
/// Raised after the live ConfigData item table has been loaded.
/// </summary>
public sealed record ItemCatalogReadyEvent(int Count) : IGameEvent;
