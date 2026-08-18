namespace BackToTheDawn.ModAPI;

/// <summary>
/// Read-only relationship state for one loaded character. The game exposes
/// affection as a derived value for characters whose relationship has entered
/// affection mode; Friend remains available for the general NPC relationship
/// system.
/// </summary>
public sealed record CharacterRelationshipSnapshot(
    int CharacterId,
    string? CharacterName,
    int Friend,
    int Affection,
    int FriendLowerLimit,
    int FriendUpperLimit,
    bool IsAffectionMode,
    bool IsProtagonist,
    bool IsInteractive);

/// <summary>
/// Read-only relationship queries. Mods should use trade events for the
/// authoritative delta of a transaction and this API for the current value.
/// </summary>
public sealed class RelationshipApi
{
    internal RelationshipApi()
    {
    }

    public IReadOnlyList<CharacterRelationshipSnapshot> All =>
        GameContext.Relationships;

    public CharacterRelationshipSnapshot? Interactive =>
        GameContext.InteractiveRelationship;

    public CharacterRelationshipSnapshot? Protagonist =>
        GameContext.ProtagonistRelationship;

    /// <summary>
    /// Affection for the currently interactive character, when the game has
    /// one selected. This is convenient for a girlfriend UI such as Maggie's
    /// phone/mail flow; use <see cref="TryGet"/> for a stable character ID.
    /// </summary>
    public int? InteractiveAffection => Interactive?.Affection;

    public bool TryGetAffection(int characterId, out int affection)
    {
        if (TryGet(characterId, out var relationship))
        {
            affection = relationship.Affection;
            return true;
        }

        affection = 0;
        return false;
    }

    public bool TryGet(
        int characterId,
        out CharacterRelationshipSnapshot relationship)
    {
        foreach (var candidate in All)
        {
            if (candidate.CharacterId == characterId)
            {
                relationship = candidate;
                return true;
            }
        }

        relationship = null!;
        return false;
    }
}
