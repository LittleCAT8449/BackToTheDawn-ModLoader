using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using BackToTheDawn.ModAPI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BackToTheDawn.Loader;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BasePlugin
{
    public const string PluginGuid = "dev.backtothedawn.loader";
    public const string PluginName = "Back To The Dawn Mod Loader";
    public const string PluginVersion = "0.1.0";

    private ConfigEntry<bool>? _enabled;
    private ConfigEntry<bool>? _showOverlay;
    private ConfigEntry<bool>? _showConsole;
    private ConfigEntry<bool>? _enableRuntimeProbe;
    private ConfigEntry<bool>? _enableLifecycleHooks;
    private System.Action<Scene, LoadSceneMode>? _sceneLoadedHandler;
    private Harmony? _harmony;
    private ModRegistryRunner? _modRegistryRunner;
    private ModHost? _modHost;
    private LoaderConsole? _loaderConsole;

    internal static ManualLogSource? Logger { get; private set; }

    public override void Load()
    {
        Logger = Log;
        GameEvents.DiagnosticLog = message => Log.LogInfo($"[GameEvents] {message}");
        GameEvents.ErrorLog = message => Log.LogError($"[GameEvents] {message}");
        GameContext.SnapshotProvider = GameContextAdapter.Capture;

        _enabled = Config.Bind(
            "General",
            "Enabled",
            true,
            "Whether the Back To The Dawn mod loader prototype is enabled.");

        _showOverlay = Config.Bind(
            "Interface",
            "ShowStatusOverlay",
            true,
            "Show a small mod-loader status panel in the top-left corner.");

        _showConsole = Config.Bind(
            "Interface",
            "ShowConsole",
            true,
            "Enable the in-game loader console (toggle with F8).");

        _enableRuntimeProbe = Config.Bind(
            "Diagnostics",
            "EnableRuntimeProbe",
            true,
            "Scan the loaded scene once and log likely game manager and UI objects.");

        _enableLifecycleHooks = Config.Bind(
            "Diagnostics",
            "EnableLifecycleHooks",
            true,
            "Log key main-menu and saved-game loading lifecycle calls through HarmonyX.");

        if (!_enabled.Value)
        {
            Log.LogWarning($"{PluginName} is disabled in its configuration file.");
            return;
        }

        ItemCatalogBootstrap.Initialize();

        Log.LogInfo("==================================================");
        Log.LogInfo($"{PluginName} v{PluginVersion} loaded successfully.");
        Log.LogInfo("IL2CPP injection works; the prototype is ready for game hooks.");
        Log.LogInfo("==================================================");

        _sceneLoadedHandler = OnSceneLoaded;
        SceneManager.sceneLoaded += _sceneLoadedHandler;

        if (_enableRuntimeProbe.Value)
        {
            AddComponent<RuntimeObjectProbe>();
        }

        if (_enableLifecycleHooks.Value)
        {
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo("Game lifecycle Harmony hooks installed.");
        }

        _modHost = new ModHost();
        ModHost.Current = _modHost;
        _modRegistryRunner = AddComponent<ModRegistryRunner>();
        Log.LogInfo("Mod manifest registry scheduled for the first Unity frame.");

        var activeScene = SceneManager.GetActiveScene();
        LoaderOverlay.CurrentScene = activeScene.name;
        Log.LogInfo($"Initial scene: '{activeScene.name}' (build index {activeScene.buildIndex}).");

        if (_showOverlay.Value)
        {
            AddComponent<LoaderOverlay>();
            Log.LogInfo("Status overlay attached to the Unity runtime.");
        }

        if (_showConsole.Value)
        {
            _loaderConsole = AddComponent<LoaderConsole>();
            Log.LogInfo("Loader console attached to the Unity runtime (toggle with F8).");
        }
    }

    public override bool Unload()
    {
        _harmony?.UnpatchSelf();
        _harmony = null;
        if (_modRegistryRunner is not null)
        {
            UnityEngine.Object.Destroy(_modRegistryRunner);
            _modRegistryRunner = null;
        }

        if (_loaderConsole is not null)
        {
            UnityEngine.Object.Destroy(_loaderConsole);
            _loaderConsole = null;
        }

        _modHost?.Shutdown();
        ModHost.Current = null;
        _modHost = null;
        ModRegistry.Clear();
        GameEvents.ClearSubscribers();
        GameContextAdapter.Reset();
        RuntimeItemCatalog.Reset();
        ItemCatalog.Reset();

        if (_sceneLoadedHandler is not null)
        {
            SceneManager.sceneLoaded -= _sceneLoadedHandler;
            _sceneLoadedHandler = null;
        }

        Log.LogInfo($"{PluginName} unloaded.");
        Logger = null;
        return true;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        LoaderOverlay.CurrentScene = scene.name;
        Log.LogInfo(
            $"Scene loaded: '{scene.name}' (build index {scene.buildIndex}, mode {mode}).");

        if (_enableRuntimeProbe?.Value == true)
        {
            RuntimeObjectProbe.ScheduleScan(scene.name);
        }
    }
}

public sealed class RuntimeObjectProbe : MonoBehaviour
{
    private const int DelayFrames = 120;
    private const int MaxVisitedObjects = 20_000;
    private const int MaxLoggedMatches = 250;

    private static readonly string[] Keywords =
    {
        "manager", "menu", "save", "state", "controller", "canvas", "ui", "system"
    };

    private static int _framesUntilScan = -1;
    private static string _scheduledScene = string.Empty;

    public RuntimeObjectProbe(IntPtr pointer) : base(pointer)
    {
    }

    public static void ScheduleScan(string sceneName)
    {
        _scheduledScene = sceneName;
        _framesUntilScan = DelayFrames;
        Plugin.Logger?.LogInfo(
            $"Runtime object probe scheduled for scene '{sceneName}' in {DelayFrames} frames.");
    }

    private void Update()
    {
        if (_framesUntilScan < 0)
        {
            return;
        }

        if (_framesUntilScan-- > 0)
        {
            return;
        }

        _framesUntilScan = -1;
        ScanActiveScene();
    }

    private static void ScanActiveScene()
    {
        var scene = SceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects();
        var visited = 0;
        var matched = 0;

        Plugin.Logger?.LogInfo(
            $"Runtime object probe started for '{_scheduledScene}': {roots.Length} root objects.");

        foreach (var root in roots)
        {
            Visit(root.transform, root.name, ref visited, ref matched);
            if (visited >= MaxVisitedObjects || matched >= MaxLoggedMatches)
            {
                break;
            }
        }

        Plugin.Logger?.LogInfo(
            $"Runtime object probe finished: visited {visited} objects, logged {matched} candidates.");
    }

    private static void Visit(Transform transform, string path, ref int visited, ref int matched)
    {
        if (visited >= MaxVisitedObjects || matched >= MaxLoggedMatches)
        {
            return;
        }

        visited++;
        var gameObject = transform.gameObject;
        var components = gameObject.GetComponents<Component>();
        var componentNames = new List<string>(components.Length);

        foreach (var component in components)
        {
            if (component is null)
            {
                continue;
            }

            componentNames.Add(component.GetIl2CppType().FullName);
        }

        var searchable = $"{path} {string.Join(" ", componentNames)}".ToLowerInvariant();
        if (Keywords.Any(searchable.Contains))
        {
            Plugin.Logger?.LogInfo(
                $"[RuntimeProbe] {path} | Active={gameObject.activeInHierarchy} | " +
                $"Components=[{string.Join(", ", componentNames)}]");
            matched++;
        }

        for (var index = 0; index < transform.childCount; index++)
        {
            var child = transform.GetChild(index);
            Visit(child, $"{path}/{child.name}", ref visited, ref matched);
            if (visited >= MaxVisitedObjects || matched >= MaxLoggedMatches)
            {
                break;
            }
        }
    }
}

public sealed class LoaderOverlay : MonoBehaviour
{
    public static string CurrentScene { get; set; } = "<initializing>";

    public LoaderOverlay(IntPtr pointer) : base(pointer)
    {
    }

    private void OnGUI()
    {
        var modStatus = ModRegistry.IsReady
            ? $"Mods: {ModRegistry.DiscoveredMods.Count} valid / " +
              $"{ModRegistry.RejectedMods.Count} rejected / " +
              $"{(ModHost.Current?.ActiveCount ?? 0)} active / " +
              $"{(ModHost.Current?.FailedCount ?? 0)} failed"
            : "Mods: scanning...";
        GUI.Box(
            new Rect(16f, 16f, 460f, 92f),
            $"{Plugin.PluginName}  v{Plugin.PluginVersion}\n运行正常\n" +
            $"场景: {CurrentScene}\n{modStatus}");
    }
}
