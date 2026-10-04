using System.Text.Json;
using BackToTheDawn.ModAPI;
using BackToTheDawn.ShopAPI;
using ShopRegistrationApi = BackToTheDawn.ShopAPI.ShopApi;

namespace BackToTheDawn.Loader;

/// <summary>Loads data-only shop registrations from a JSON Mod directory.</summary>
internal sealed class JsonShopMod : IMod
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public void Initialize(ModContext context)
    {
        var shops = new List<LoadedShop>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in EnumerateJsonFiles(context.ModDirectory))
        {
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(path), DocumentOptions);
                if (json.RootElement.ValueKind != JsonValueKind.Object ||
                    !TryGetType(json.RootElement, out var type) ||
                    !string.Equals(type, "shop", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var document = json.RootElement.Deserialize<ShopDocument>(JsonOptions)
                    ?? throw new InvalidDataException("A shop definition must contain an object.");
                if (document.SchemaVersion != 1)
                {
                    throw new InvalidDataException(
                        $"Unsupported shop schemaVersion {document.SchemaVersion}.");
                }
                if (string.IsNullOrWhiteSpace(document.Key) || document.Key.Contains(':'))
                {
                    throw new InvalidDataException(
                        "A shop requires a local 'key' without a namespace prefix.");
                }
                var key = new ShopKey(context.Manifest.Id, document.Key.Trim()).Path;
                if (!keys.Add(key))
                {
                    throw new InvalidDataException($"Duplicate shop key '{key}' within this Mod.");
                }
                if (string.IsNullOrWhiteSpace(document.DisplayName))
                {
                    throw new InvalidDataException("A shop requires 'displayName'.");
                }
                if (document.Offers is null || document.Offers.Length == 0)
                {
                    throw new InvalidDataException("A shop requires a nonempty 'offers' array.");
                }

                var offers = document.Offers.Select(offer => ConvertOffer(offer)).ToArray();
                if (offers.Any(offer => offer.Price < 0 || offer.Stock < 0))
                {
                    throw new InvalidDataException("Shop offer prices and stock must be zero or greater.");
                }
                if (offers.Select(offer => offer.ItemKey.ToString())
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() != offers.Length)
                {
                    throw new InvalidDataException("An item can only appear once in one shop definition.");
                }

                shops.Add(new LoadedShop(path, key, document.DisplayName.Trim(), offers));
            }
            catch (Exception exception)
            {
                throw new InvalidDataException($"Shop JSON '{path}': {exception.Message}", exception);
            }
        }

        if (shops.Count == 0)
        {
            throw new InvalidDataException(
                "No shop JSON was found. Each shop definition needs 'type': 'shop'.");
        }

        // Parse and validate every file before mutating the runtime shop registry.
        // ModHost unregisters this owner if any registration fails.
        var api = ShopRegistrationApi.For(context);
        foreach (var shop in shops)
        {
            var result = api.RegisterShop(shop.Key, shop.DisplayName, shop.Offers);
            if (!result.Succeeded)
            {
                throw new InvalidDataException($"Shop JSON '{shop.Path}': {result.Message}");
            }

            context.Logger.Info(
                $"[JsonShopMod] Registered shop '{result.ShopKey}' ({shop.DisplayName}) from " +
                $"'{Path.GetRelativePath(context.ModDirectory, shop.Path)}'.");
        }

        context.Logger.Info($"[JsonShopMod] Loaded {shops.Count} shop definition(s).");
    }

    public void Shutdown() { } // ModHost unregisters all shop data owned by this Mod.

    private static ShopOffer ConvertOffer(ShopOfferDocument? offer)
    {
        if (offer is null)
        {
            throw new InvalidDataException("A shop offer cannot be null.");
        }
        if (!ItemKey.TryParse(offer.Item, out var itemKey))
        {
            throw new InvalidDataException(
                $"Shop offer item '{offer.Item}' must use a namespaced key such as 'backtothedawn:apple'.");
        }
        if (offer.Price is null)
        {
            throw new InvalidDataException($"Shop offer '{offer.Item}' requires an integer 'price'.");
        }

        return new ShopOffer(itemKey, offer.Price.Value, offer.Stock ?? int.MaxValue);
    }

    private static IEnumerable<string> EnumerateJsonFiles(string root)
    {
        var options = new EnumerationOptions
        {
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = false,
            MatchCasing = MatchCasing.CaseInsensitive,
        };

        foreach (var file in Directory.EnumerateFiles(root, "*.json", options)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(file);
            if (!string.Equals(name, "Manifest.json", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(name, "mod.json", StringComparison.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }

        foreach (var directory in Directory.EnumerateDirectories(root, "*", options)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            // A nested Mod has its own namespace and is discovered independently.
            if (File.Exists(Path.Combine(directory, "Manifest.json")) ||
                File.Exists(Path.Combine(directory, "mod.json")))
            {
                continue;
            }

            foreach (var file in EnumerateJsonFiles(directory))
            {
                yield return file;
            }
        }
    }

    private static bool TryGetType(JsonElement root, out string? type)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, "type", StringComparison.OrdinalIgnoreCase))
            {
                type = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()
                    : null;
                return true;
            }
        }

        type = null;
        return false;
    }

    private sealed record LoadedShop(string Path, string Key, string DisplayName, ShopOffer[] Offers);

    private sealed class ShopDocument
    {
        public int SchemaVersion { get; init; } = 1;
        public string? Key { get; init; }
        public string? DisplayName { get; init; }
        public ShopOfferDocument[]? Offers { get; init; }
    }

    private sealed class ShopOfferDocument
    {
        public string? Item { get; init; }
        public int? Price { get; init; }
        public int? Stock { get; init; }
    }
}
