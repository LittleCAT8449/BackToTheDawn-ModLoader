namespace BackToTheDawn.ModAPI;

/// <summary>
/// Read-only access to the latest game state exposed by the loader.
/// </summary>
public static class GameContext
{
    internal static Func<GameStateSnapshot?>? SnapshotProvider { get; set; }

    public static GameStateSnapshot? Current
    {
        get
        {
            try
            {
                return SnapshotProvider?.Invoke();
            }
            catch
            {
                return null;
            }
        }
    }

    public static bool IsGameplayReady => Current?.IsGameplayReady ?? false;
    public static int ArchiveId => Current?.ArchiveId ?? 0;
    public static int MapId => Current?.MapId ?? 0;
    public static GameTimeSnapshot? Time => Current?.Time;
    public static PlayerSnapshot? Player => Current?.Player;

    public static bool TryGetSnapshot(out GameStateSnapshot? snapshot)
    {
        snapshot = Current;
        return snapshot is not null;
    }

    internal static void Reset() => SnapshotProvider = null;
}

public sealed record GameStateSnapshot(
    bool IsGameplayReady,
    int ArchiveId,
    int MapId,
    string MapName,
    GameTimeSnapshot Time,
    PlayerSnapshot? Player);

public sealed record GameTimeSnapshot(
    int Day,
    int WakeDay,
    int Hour,
    int Minute,
    int TotalMinutes);

public sealed record PlayerSnapshot(
    int CharacterId,
    int Health,
    int MaxHealth,
    int Mentality,
    int MaxMentality,
    int Satiety,
    int MaxSatiety,
    int Energy,
    int MaxEnergy,
    int Focus,
    int MaxFocus,
    int Money);
