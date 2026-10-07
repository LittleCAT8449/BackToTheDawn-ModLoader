using BackToTheDawn.ModAPI;
using UnityEngine;

namespace BackToTheDawn.Loader;

internal static class RoomCloneRuntime
{
    private sealed record CloneEntry(
        string Key,
        int NativeId,
        int BaseRoomId,
        string Name,
        GameObject GameObject);

    private static readonly Dictionary<string, CloneEntry> Clones =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, int> BuiltInRooms =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["backtothedawn:church"] = 14,
            ["church"] = 14,
        };
    private static int _nextNativeId = 9000;

    internal static RoomRegistrationResult Register(
        string key,
        string baseRoom,
        string? displayName)
    {
        if (Clones.ContainsKey(key))
        {
            var existing = Clones[key];
            return new RoomRegistrationResult(
                key,
                existing.NativeId,
                RoomRegistrationStatus.AlreadyRegistered,
                "The room key is already registered.");
        }

        if (!BuiltInRooms.TryGetValue(baseRoom, out var baseId))
        {
            return new RoomRegistrationResult(
                key,
                0,
                RoomRegistrationStatus.InvalidBaseRoom,
                $"Unsupported base room '{baseRoom}'. The POC currently supports church.");
        }

        try
        {
            var baseObject = MapManage.GetMapGOById(baseId);
            if (baseObject is null)
            {
                MapManage.LoadMap(baseId);
                baseObject = MapManage.GetMapGOById(baseId);
            }
            if (baseObject is null)
            {
                return new RoomRegistrationResult(
                    key,
                    0,
                    RoomRegistrationStatus.GameObjectUnavailable,
                    $"The base room GameObject for map {baseId} is not loaded.");
            }

            var baseMap = baseObject.GetComponent<Map>();
            if (baseMap is null)
            {
                return new RoomRegistrationResult(
                    key,
                    0,
                    RoomRegistrationStatus.MapComponentUnavailable,
                    "The base room GameObject has no Map component.");
            }

            var nativeId = _nextNativeId++;
            var name = string.IsNullOrWhiteSpace(displayName)
                ? key
                : displayName;
            var clone = UnityEngine.Object.Instantiate(baseObject);
            clone.name = "BackToTheDawn.RoomClone." + key.Replace(':', '.');
            clone.transform.SetParent(baseObject.transform.parent, false);
            clone.transform.localPosition = baseObject.transform.localPosition +
                new Vector3(1000f + nativeId, 0f, 0f);
            clone.transform.localRotation = baseObject.transform.localRotation;
            clone.transform.localScale = baseObject.transform.localScale;
            clone.SetActive(false);

            var map = clone.GetComponent<Map>();
            if (map is null)
            {
                UnityEngine.Object.Destroy(clone);
                return new RoomRegistrationResult(
                    key,
                    0,
                    RoomRegistrationStatus.MapComponentUnavailable,
                    "The cloned GameObject has no Map component.");
            }

            map.id = nativeId;
            MapManage.mapDict.Add(nativeId, map);

            var entry = new CloneEntry(key, nativeId, baseId, name!, clone);
            Clones[key] = entry;
            Plugin.Logger?.LogInfo(
                $"[RoomRuntime] Registered clone '{key}' from map {baseId} as {nativeId}.");
            return new RoomRegistrationResult(
                key,
                nativeId,
                RoomRegistrationStatus.Succeeded,
                $"Cloned map {baseId} into runtime room {nativeId}.");
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError($"[RoomRuntime] Failed to register '{key}': {exception}");
            return new RoomRegistrationResult(
                key,
                0,
                RoomRegistrationStatus.RegistrationFailed,
                exception.Message);
        }
    }

    internal static bool GoTo(string key)
    {
        if (!Clones.TryGetValue(key, out var entry))
        {
            return false;
        }

        RoomTransitionDiagnostics.LogRequest(
            "RoomApi.GoTo rejected",
            entry.NativeId,
            ", reason=runtime clone has no native map resource or transition initialization");
        Plugin.Logger?.LogWarning(
            $"[RoomRuntime] GoTo rejected for runtime clone '{key}' ({entry.NativeId}). " +
            "Cloned GameObjects are not resource-backed maps and cannot safely use the native transition flow yet.");
        return false;
    }

    internal static bool IsRegistered(string key) =>
        !string.IsNullOrWhiteSpace(key) && Clones.ContainsKey(key.Trim());

    internal static RoomInfo? Get(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || !Clones.TryGetValue(key.Trim(), out var entry))
        {
            return null;
        }

        return ToRoomInfo(entry);
    }

    internal static IReadOnlyList<RoomInfo> GetAll() =>
        Clones.Values.Select(ToRoomInfo).ToArray();

    internal static bool Unregister(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || !Clones.TryGetValue(key.Trim(), out var entry))
        {
            return false;
        }

        try
        {
            if (GameContext.Current?.MapId == entry.NativeId)
            {
                MapManage.GoToMap(entry.BaseRoomId);
            }

            RemoveMapRegistration(entry.NativeId);
            if (entry.GameObject is not null)
            {
                UnityEngine.Object.Destroy(entry.GameObject);
            }

            Clones.Remove(entry.Key);
            Plugin.Logger?.LogInfo(
                $"[RoomRuntime] Unregistered clone '{entry.Key}' ({entry.NativeId}).");
            return true;
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError(
                $"[RoomRuntime] Failed to unregister '{entry.Key}': {exception}");
            return false;
        }
    }

    internal static int UnregisterAll(string modId)
    {
        if (string.IsNullOrWhiteSpace(modId))
        {
            return 0;
        }

        var prefix = modId.Trim() + ":";
        var keys = Clones.Keys
            .Where(key => key.Equals(modId.Trim(), StringComparison.OrdinalIgnoreCase) ||
                          key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var removed = 0;
        foreach (var key in keys)
        {
            if (Unregister(key))
            {
                removed++;
            }
        }

        return removed;
    }

    internal static bool TryGetName(int mapId, out string name)
    {
        foreach (var entry in Clones.Values)
        {
            if (entry.NativeId == mapId)
            {
                name = entry.Name;
                return true;
            }
        }

        name = string.Empty;
        return false;
    }

    internal static void Reset()
    {
        foreach (var entry in Clones.Values)
        {
            RemoveMapRegistration(entry.NativeId);

            if (entry.GameObject is not null)
            {
                UnityEngine.Object.Destroy(entry.GameObject);
            }
        }

        Clones.Clear();
        _nextNativeId = 9000;
    }

    private static RoomInfo ToRoomInfo(CloneEntry entry) =>
        new(entry.Key, entry.NativeId, entry.Name, RoomSource.Clone, entry.GameObject is not null);

    private static void RemoveMapRegistration(int nativeId)
    {
        try
        {
            // Runtime clones are not resource-backed maps. Remove them
            // before MapManage.LoadWholeMap enumerates native maps.
            MapManage.mapDict.Remove(nativeId);
            // Defensive cleanup for builds that may have received the old
            // prototype's wholeList entry during this process.
            MapManage.wholeList.Remove(nativeId);
        }
        catch (Exception exception)
        {
            Plugin.DebugLog(
                $"[RoomRuntime] Failed to remove clone map {nativeId}: {exception.Message}");
        }
    }
}
