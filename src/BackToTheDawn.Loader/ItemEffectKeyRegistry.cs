using System.Security.Cryptography;
using System.Text;
using BackToTheDawn.ModAPI;

namespace BackToTheDawn.Loader;

/// <summary>
/// Maps the small set of common effects to readable keys and gives every
/// other description a deterministic, non-numeric fallback key.
/// </summary>
internal static class ItemEffectKeyRegistry
{
    private const string GameNamespace = "backtothedawn";

    private static readonly IReadOnlyDictionary<int, string> KnownPaths =
        new Dictionary<int, string>
        {
            [1] = "life",
            [2] = "strength",
            [3] = "dexterity",
            [4] = "intelligence",
            [5] = "energy",
            [6] = "health",
            [7] = "mentality",
            [8] = "satiety",
            [9] = "performance",
            [10] = "prestige",
            [11] = "money",
            [12] = "relationship",
            [13] = "gang_relationship",
            [14] = "action_energy_cost",
            [15] = "charm",
            [18] = "sleep_energy_recovery",
            [20] = "good_dream_chance",
            [21] = "combat_critical_chance",
            [22] = "combat_evasion_chance",
            [23] = "combat_extra_dice_a",
            [24] = "action_time_cost",
            [29] = "no_dream",
            [31] = "alcohol",
            [32] = "sleep_health_recovery",
            [56] = "weak_health_threshold"
        };

    internal static ItemKey Create(int id, string? description)
    {
        if (KnownPaths.TryGetValue(id, out var knownPath))
        {
            return new ItemKey(GameNamespace, knownPath);
        }

        var normalizedDescription = (description ?? string.Empty).Trim();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedDescription));
        var hash = Convert.ToHexString(bytes)[..10].ToLowerInvariant();
        return new ItemKey(GameNamespace, "effect_" + hash);
    }
}
