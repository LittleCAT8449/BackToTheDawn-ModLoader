using System;
using HarmonyLib;

namespace BackToTheDawn.Loader;

/// <summary>
/// Low-volume diagnostics for map transitions. These patches only observe map IDs;
/// they do not alter the game's transition or loading behavior.
/// </summary>
internal static class RoomTransitionDiagnostics
{
    private static readonly BepInEx.Logging.ManualLogSource TraceLogger =
        BepInEx.Logging.Logger.CreateLogSource("BackToTheDawn.RoomTrace");

    internal static void LogRequest(string source, int mapId, string? details = null)
    {
        string path;
        string mapDictionaryState;

        try
        {
            object pathValue = MapManage.GetMapPathGOById(mapId);
            path = pathValue?.ToString() ?? "<null>";
        }
        catch (Exception exception)
        {
            path = $"<lookup failed: {exception.GetType().Name}>";
        }

        try
        {
            var dictionary = MapManage.mapDict;
            mapDictionaryState = dictionary == null
                ? "null"
                : dictionary.ContainsKey(mapId).ToString();
        }
        catch (Exception exception)
        {
            mapDictionaryState = $"<lookup failed: {exception.GetType().Name}>";
        }

        TraceLogger.LogInfo(
            $"[RoomTrace] {source}: mapId={mapId}, mapDictContains={mapDictionaryState}, resourcePath='{path}'{details}");
    }
}

[HarmonyPatch(typeof(MapManage), nameof(MapManage.GoToMap), new[] { typeof(int) })]
internal static class MapManageGoToMapTracePatch
{
    private static void Prefix(int __0)
    {
        RoomTransitionDiagnostics.LogRequest("GoToMap", __0);
    }
}

[HarmonyPatch(typeof(MapManage), nameof(MapManage.LoadMap), new[] { typeof(int) })]
internal static class MapManageLoadMapTracePatch
{
    private static void Prefix(int __0)
    {
        RoomTransitionDiagnostics.LogRequest("LoadMap", __0);
    }
}

[HarmonyPatch(typeof(MapManage), nameof(MapManage.LoadOneMap), new[] { typeof(int), typeof(bool) })]
internal static class MapManageLoadOneMapTracePatch
{
    private static void Prefix(int __0, bool __1)
    {
        RoomTransitionDiagnostics.LogRequest("LoadOneMap", __0, $", isOffsetPosition={__1}");
    }
}
