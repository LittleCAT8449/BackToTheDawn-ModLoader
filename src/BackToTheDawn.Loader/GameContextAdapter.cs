using BackToTheDawn.ModAPI;

namespace BackToTheDawn.Loader;

internal static class GameContextAdapter
{
    internal static bool IsGameplayReady { get; set; }
    private static GameTimeSnapshot? _lastPublishedTime;
    private static int? _lastPublishedMapId;
    private static PlayerSnapshot? _lastPublishedPlayer;
    private static readonly HashSet<string> ObservedPlayerHookSources = new();

    internal static GameStateSnapshot? Capture()
    {
        var process = GameProcess.singleton;
        if (process is null)
        {
            return null;
        }

        var mapId = MapManage.currentMap?.id ?? process.currentMapId;
        var mapName = GetMapName(mapId);

        return new GameStateSnapshot(
            IsGameplayReady,
            ArchiveData.GetGameArchiveDataId(),
            mapId,
            mapName,
            CaptureTime(),
            CapturePlayer());
    }

    internal static GameTimeSnapshot CaptureTime()
    {
        var time = TimeManage.nowTime;
        return new GameTimeSnapshot(
            time.day,
            time.wakeDay,
            time.hour,
            time.minute,
            time.GetTotalMinutes());
    }

    internal static int GetCurrentMapId() =>
        MapManage.currentMap?.id ?? GameProcess.singleton?.currentMapId ?? 0;

    internal static void InitializeEventBaselines()
    {
        _lastPublishedTime = CaptureTime();
        _lastPublishedMapId = GetCurrentMapId();
        _lastPublishedPlayer = CapturePlayer();
    }

    internal static void PublishTimeIfChanged()
    {
        var current = CaptureTime();
        var previous = _lastPublishedTime;
        _lastPublishedTime = current;

        if (previous is not null && previous != current)
        {
            GameEvents.RaiseTimeChanged(previous, current);
        }
    }

    internal static void PublishMapIfChanged(Map map)
    {
        var currentMapId = map.id;
        var previousMapId = _lastPublishedMapId ?? MapManage.previousMapId;
        _lastPublishedMapId = currentMapId;

        if (previousMapId != currentMapId)
        {
            GameEvents.RaiseMapChanged(
                previousMapId,
                currentMapId,
                GetMapName(currentMapId));
        }
    }

    internal static void PublishPlayerIfChanged(ThingPackage package, string source)
    {
        if (!IsGameplayReady || package is null)
        {
            return;
        }

        try
        {
            var protagonist = CharacterManage.protagonistAttribute;
            if (protagonist is null || package.cId != protagonist.id)
            {
                return;
            }

            if (ObservedPlayerHookSources.Add(source))
            {
                Plugin.Logger?.LogInfo(
                    $"[LifecycleHook] Player state source observed: {source} (cId={package.cId}).");
            }

            var current = CapturePlayer();
            var previous = _lastPublishedPlayer;
            _lastPublishedPlayer = current;

            if (previous is not null && current is not null && previous != current)
            {
                GameEvents.RaisePlayerStateChanged(previous, current, source);
            }
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError(
                $"[GameContext] Failed to publish player state after {source}: {exception}");
        }
    }

    internal static void PublishPlayerItemUsed(
        CharacterAttribute attribute,
        int itemId,
        int useCount)
    {
        if (!IsGameplayReady || attribute is null)
        {
            return;
        }

        try
        {
            var protagonist = CharacterManage.protagonistAttribute;
            if (protagonist is null || attribute.id != protagonist.id)
            {
                return;
            }

            var itemKey = ItemCatalog.ResolveOrCreateKey(itemId);
            GameEvents.RaisePlayerItemUsed(attribute.id, itemKey, useCount);
            GameEvents.RaisePlayerItemAction(
                attribute.id,
                itemKey,
                useCount,
                ItemActionKind.Use,
                succeeded: true,
                source: nameof(CharacterAttribute.UseItem));
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError(
                $"[GameContext] Failed to publish player item use for item {itemId}: {exception}");
        }
    }

    internal static void PublishPlayerItemAction(
        ThingPackage package,
        Thing? thing,
        ItemActionKind action,
        int count,
        bool succeeded,
        string source)
    {
        if (package is null)
        {
            return;
        }

        PublishPlayerItemAction(
            package.cId,
            thing?.id ?? 0,
            count,
            action,
            succeeded,
            source);
    }

    internal static void PublishPlayerItemAction(
        CharacterAttribute? attribute,
        Thing? thing,
        ItemActionKind action,
        int count,
        bool succeeded,
        string source,
        int rawOperationType = -1)
    {
        PublishPlayerItemAction(
            attribute?.id ?? 0,
            thing?.id ?? 0,
            count,
            action,
            succeeded,
            source,
            rawOperationType);
    }

    internal static void PublishPlayerItemAction(
        int characterId,
        int itemId,
        int count,
        ItemActionKind action,
        bool succeeded,
        string source,
        int rawOperationType = -1)
    {
        if (!IsGameplayReady || characterId == 0)
        {
            return;
        }

        try
        {
            var protagonist = CharacterManage.protagonistAttribute;
            if (protagonist is null || protagonist.id != characterId)
            {
                return;
            }

            var itemKey = itemId > 0
                ? ItemCatalog.ResolveOrCreateKey(itemId)
                : (ItemKey?)null;
            GameEvents.RaisePlayerItemAction(
                characterId,
                itemKey,
                count,
                action,
                succeeded,
                source,
                rawOperationType);
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError(
                $"[GameContext] Failed to publish item action {action} from {source}: {exception}");
        }
    }

    internal static string GetMapName(int mapId)
    {
        if (mapId == 0)
        {
            return string.Empty;
        }

        try
        {
            return MapManage.GetMapName(mapId) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    internal static void Reset()
    {
        IsGameplayReady = false;
        _lastPublishedTime = null;
        _lastPublishedMapId = null;
        _lastPublishedPlayer = null;
        ObservedPlayerHookSources.Clear();
        GameContext.Reset();
    }

    private static PlayerSnapshot? CapturePlayer()
    {
        var player = CharacterManage.protagonistAttribute;
        if (player is null)
        {
            return null;
        }

        return new PlayerSnapshot(
            player.id,
            player.health,
            player.healthMax,
            player.mentality,
            player.mentalityMax,
            player.satiety,
            player.satietyMax,
            player.energy,
            player.energyMax,
            player.focus,
            player.focusMax,
            player.money);
    }
}
