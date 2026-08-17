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

        ItemCatalog.InitializeStatic(bindings);
        var uniqueIds = bindings.Select(binding => binding.Id).Distinct().Count();
        Plugin.Logger?.LogInfo(
            $"[ItemCatalog] Static namespace registry ready: " +
            $"{bindings.Count} names, {uniqueIds} unique IDs.");
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
