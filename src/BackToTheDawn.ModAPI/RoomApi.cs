namespace BackToTheDawn.ModAPI;

/// <summary>Stable room/location API backed by the game's map focus hook.</summary>
public sealed class RoomApi
{
    internal static Func<string, string, string?, RoomRegistrationResult>? CloneProvider { get; set; }
    internal static Func<string, bool>? GoToProvider { get; set; }
    internal static Func<string, bool>? IsRegisteredProvider { get; set; }
    internal static Func<string, RoomInfo?>? GetProvider { get; set; }
    internal static Func<IReadOnlyList<RoomInfo>>? ListProvider { get; set; }
    internal static Func<string, bool>? UnregisterProvider { get; set; }
    internal static Func<string, int>? UnregisterAllProvider { get; set; }

    internal RoomApi()
    {
    }

    /// <summary>Returns the latest focused room, or null before gameplay is ready.</summary>
    public RoomSnapshot? Current
    {
        get
        {
            var state = GameContext.Current;
            return state is null || !state.IsGameplayReady
                ? null
                : new RoomSnapshot(state.MapId, state.MapName);
        }
    }

    /// <summary>Subscribes to room focus changes.</summary>
    public IDisposable Subscribe(Action<RoomChangedEvent> handler) =>
        GameEvents.Subscribe(handler);

    /// <summary>
    /// Registers a runtime clone of an existing room. This is experimental and
    /// does not make the clone persistent in save data.
    /// </summary>
    public RoomRegistrationResult RegisterClone(
        string key,
        string baseRoom,
        string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Room key cannot be empty.", nameof(key));
        }

        if (string.IsNullOrWhiteSpace(baseRoom))
        {
            throw new ArgumentException("Base room key cannot be empty.", nameof(baseRoom));
        }

        return CloneProvider?.Invoke(key.Trim(), baseRoom.Trim(), displayName?.Trim())
            ?? RoomRegistrationResult.Unavailable(key.Trim());
    }

    /// <summary>Requests a transition to a registered room key.</summary>
    public bool GoTo(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        return GoToProvider?.Invoke(key.Trim()) == true;
    }

    /// <summary>Returns whether a room key is registered in the current runtime.</summary>
    public bool IsRegistered(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        return IsRegisteredProvider?.Invoke(key.Trim()) == true;
    }

    /// <summary>Looks up a registered room without exposing native map IDs by default.</summary>
    public bool TryGet(string key, out RoomInfo room)
    {
        room = null!;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var value = GetProvider?.Invoke(key.Trim());
        if (value is null)
        {
            return false;
        }

        room = value;
        return true;
    }

    /// <summary>Returns a snapshot of rooms registered through the loader.</summary>
    public IReadOnlyList<RoomInfo> GetRegisteredRooms() =>
        ListProvider?.Invoke() ?? Array.Empty<RoomInfo>();

    /// <summary>Removes one runtime room. Native game rooms cannot be removed.</summary>
    public bool Unregister(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        return UnregisterProvider?.Invoke(key.Trim()) == true;
    }

    /// <summary>
    /// Removes all runtime rooms owned by a mod namespace. The return value is
    /// the number of rooms successfully removed.
    /// </summary>
    public int UnregisterAll(string modId)
    {
        if (string.IsNullOrWhiteSpace(modId))
        {
            return 0;
        }

        return UnregisterAllProvider?.Invoke(modId.Trim()) ?? 0;
    }
}

public sealed record RoomSnapshot(int Id, string Name);

public sealed record RoomChangedEvent(
    RoomSnapshot? Previous,
    RoomSnapshot Current) : IGameEvent;

public sealed record RoomInfo(
    string Key,
    int NativeId,
    string Name,
    RoomSource Source,
    bool IsLoaded);

public enum RoomSource
{
    Native,
    Clone,
    AssetBundle
}

public enum RoomRegistrationStatus
{
    Succeeded,
    AlreadyRegistered,
    InvalidBaseRoom,
    GameObjectUnavailable,
    MapComponentUnavailable,
    RegistrationFailed,
    Unavailable,
}

public sealed record RoomRegistrationResult(
    string Key,
    int NativeId,
    RoomRegistrationStatus Status,
    string Message)
{
    public bool Succeeded => Status == RoomRegistrationStatus.Succeeded;

    internal static RoomRegistrationResult Unavailable(string key) =>
        new(key, 0, RoomRegistrationStatus.Unavailable,
            "The loader room runtime is not available.");
}
