namespace BackToTheDawn.ModAPI;

/// <summary>
/// Stable namespaced shop identifier, for example
/// <c>backtothedawn:vending_machine</c>.
/// </summary>
public readonly record struct ShopKey
{
    public ShopKey(string @namespace, string path)
    {
        Namespace = NormalizePart(@namespace, nameof(@namespace));
        Path = NormalizePart(path, nameof(path));
    }

    public string Namespace { get; }

    public string Path { get; }

    public override string ToString() => Namespace + ":" + Path;

    public static bool TryParse(string? value, out ShopKey key)
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
            key = new ShopKey(value[..separator], value[(separator + 1)..]);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string NormalizePart(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A shop key component is required.", parameterName);
        }

        var normalized = value.Trim().ToLowerInvariant();
        foreach (var character in normalized)
        {
            var valid = ((character >= 'a' && character <= 'z') ||
                         (character >= '0' && character <= '9')) ||
                        character is '_' or '-' or '.';
            if (!valid)
            {
                throw new ArgumentException(
                    $"Shop key component '{value}' contains an invalid character.",
                    parameterName);
            }
        }

        return normalized;
    }
}

public enum ShopSource
{
    NativeShopConfig = 0,
    SemanticHook = 1,
    Synthetic = 2,
}

/// <summary>
/// Public shop metadata. A descriptor can represent several native IDs when
/// the game uses multiple configurations for one logical shop.
/// </summary>
public sealed record ShopDescriptor(
    ShopKey Key,
    string DisplayName,
    IReadOnlyList<int> NativeShopIds,
    ShopSource Source,
    string? LocalizationKey = null);

/// <summary>
/// Read-only namespaced shop catalog. Native numeric IDs are only lookup
/// metadata; Mods should normally compare <see cref="ShopDescriptor.Key"/>.
/// </summary>
public static class ShopCatalog
{
    private static readonly object SyncRoot = new();
    private static Dictionary<string, ShopDescriptor> _descriptors =
        new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<int, ShopDescriptor> _descriptorsByNativeId = new();
    private static IReadOnlyList<ShopDescriptor> _all = Array.Empty<ShopDescriptor>();

    public static bool IsAvailable { get; private set; }

    public static IReadOnlyList<ShopDescriptor> All => _all;

    public static bool TryGet(ShopKey key, out ShopDescriptor descriptor) =>
        TryGet(key.ToString(), out descriptor);

    public static bool TryGet(string? key, out ShopDescriptor descriptor)
    {
        descriptor = null!;
        if (!ShopKey.TryParse(key, out var shopKey))
        {
            return false;
        }

        lock (SyncRoot)
        {
            return _descriptors.TryGetValue(shopKey.ToString(), out descriptor!);
        }
    }

    public static bool TryGetByNativeId(int nativeShopId, out ShopDescriptor descriptor)
    {
        lock (SyncRoot)
        {
            return _descriptorsByNativeId.TryGetValue(nativeShopId, out descriptor!);
        }
    }

    internal static void InitializeStatic(IEnumerable<ShopDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        var byKey = new Dictionary<string, ShopDescriptor>(StringComparer.OrdinalIgnoreCase);
        var byNativeId = new Dictionary<int, ShopDescriptor>();
        foreach (var descriptor in descriptors)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            var key = descriptor.Key.ToString();
            if (byKey.ContainsKey(key))
            {
                throw new InvalidOperationException($"Duplicate shop key '{key}'.");
            }

            var nativeIds = descriptor.NativeShopIds
                .Distinct()
                .OrderBy(id => id)
                .ToArray();
            var normalized = descriptor with
            {
                NativeShopIds = Array.AsReadOnly(nativeIds),
            };
            byKey.Add(key, normalized);

            foreach (var nativeId in nativeIds)
            {
                if (byNativeId.TryGetValue(nativeId, out var existing) &&
                    existing.Key != normalized.Key)
                {
                    throw new InvalidOperationException(
                        $"Native shop ID {nativeId} is mapped to both " +
                        $"'{existing.Key}' and '{normalized.Key}'.");
                }

                byNativeId[nativeId] = normalized;
            }
        }

        lock (SyncRoot)
        {
            _descriptors = byKey;
            _descriptorsByNativeId = byNativeId;
            _all = byKey.Values.OrderBy(value => value.Key.ToString()).ToArray();
            IsAvailable = true;
        }
    }

    internal static void Reset()
    {
        lock (SyncRoot)
        {
            _descriptors = new Dictionary<string, ShopDescriptor>(StringComparer.OrdinalIgnoreCase);
            _descriptorsByNativeId = new Dictionary<int, ShopDescriptor>();
            _all = Array.Empty<ShopDescriptor>();
            IsAvailable = false;
        }
    }
}
