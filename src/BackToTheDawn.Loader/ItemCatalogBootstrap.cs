using System.Text;
using BackToTheDawn.ModAPI;

namespace BackToTheDawn.Loader;

internal static class ItemCatalogBootstrap
{
    private const string GameNamespace = "backtothedawn";

    internal static void Initialize()
    {
        var bindings = new List<(ItemKey Key, int Id)>();
        foreach (var entry in ItemStaticNames.Entries)
        {
            bindings.Add((new ItemKey(GameNamespace, ToPath(entry.Name)), entry.Id));
        }

        var mappedIds = bindings.Select(binding => binding.Id).ToHashSet();
        var registeredKeys = bindings.Select(binding => binding.Key.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ItemReadableNames.Entries)
        {
            if (!mappedIds.Add(entry.Id))
            {
                throw new InvalidOperationException(
                    $"Readable item key '{entry.Path}' duplicates an existing ItemID mapping for {entry.Id}.");
            }

            var readableKey = new ItemKey(GameNamespace, entry.Path);
            var legacyKey = new ItemKey(GameNamespace, "item_" + entry.Id);
            if (!registeredKeys.Add(readableKey.ToString()) ||
                !registeredKeys.Add(legacyKey.ToString()))
            {
                throw new InvalidOperationException(
                    $"Readable item key or legacy alias for item ID {entry.Id} is already in use.");
            }

            bindings.Add((readableKey, entry.Id));
            bindings.Add((legacyKey, entry.Id));
        }

        ItemCatalog.InitializeStatic(bindings);
        var uniqueIds = bindings.Select(binding => binding.Id).Distinct().Count();
        Plugin.Logger?.LogInfo(
            $"[ItemCatalog] Static namespace registry ready: " +
            $"{bindings.Count} keys, {uniqueIds} unique IDs.");
    }

    private static string ToPath(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (char.IsUpper(character) && index > 0)
            {
                var previous = name[index - 1];
                var nextIsLower = index + 1 < name.Length && char.IsLower(name[index + 1]);
                if (char.IsLower(previous) || char.IsDigit(previous) || nextIsLower)
                {
                    builder.Append('_');
                }
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else
            {
                builder.Append('_');
            }
        }

        return builder.ToString().Trim('_');
    }
}
