using BackToTheDawn.ModAPI;

namespace BackToTheDawn.Loader;

internal static class ModRegistryScanner
{
    private const string LoaderModId = Plugin.PluginGuid;

    public static void Scan(IEnumerable<string> roots)
    {
        var discovered = new List<ModDescriptor>();
        var rejected = new List<ModRejectedEvent>();

        var candidatesById = new Dictionary<string, ModDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            Plugin.Logger?.LogInfo($"[ModRegistry] Scanning '{root}'.");
            foreach (var manifestPath in EnumerateManifestPaths(root))
            {
                var directoryPath = Path.GetDirectoryName(manifestPath) ?? root;
                ModManifest? manifest = null;

                try
                {
                    manifest = ModManifest.Load(manifestPath);
                    var assemblyPath = Path.Combine(directoryPath, manifest.EntryAssembly);
                    if (!File.Exists(assemblyPath))
                    {
                        Reject(
                            rejected,
                            directoryPath,
                            manifest.Id,
                            $"Entry assembly '{manifest.EntryAssembly}' was not found.");
                        continue;
                    }

                    var descriptor = new ModDescriptor(
                        manifest,
                        Path.GetFullPath(directoryPath),
                        Path.GetFullPath(assemblyPath));

                    if (!candidatesById.TryAdd(manifest.Id, descriptor))
                    {
                        Reject(
                            rejected,
                            directoryPath,
                            manifest.Id,
                            $"Duplicate mod id '{manifest.Id}'.");
                        continue;
                    }

                    Plugin.Logger?.LogInfo(
                        $"[ModRegistry] Manifest discovered: {manifest.Id} v{manifest.Version} " +
                        $"({manifest.EntryAssembly}).");
                }
                catch (Exception exception)
                {
                    Reject(
                        rejected,
                        directoryPath,
                        manifest?.Id,
                        exception.Message);
                }
            }
        }

        var accepted = candidatesById;
        var removedDependency = true;
        while (removedDependency)
        {
            removedDependency = false;
            foreach (var descriptor in accepted.Values.ToArray())
            {
                var missingDependencies = descriptor.Manifest.Dependencies
                    .Where(dependency =>
                        !string.Equals(dependency, LoaderModId, StringComparison.OrdinalIgnoreCase) &&
                        !accepted.ContainsKey(dependency))
                    .ToArray();

                if (missingDependencies.Length == 0)
                {
                    continue;
                }

                accepted.Remove(descriptor.Manifest.Id);
                Reject(
                    rejected,
                    descriptor.DirectoryPath,
                    descriptor.Manifest.Id,
                    $"Missing dependency: {string.Join(", ", missingDependencies)}.");
                removedDependency = true;
            }
        }

        discovered.AddRange(accepted.Values.OrderBy(value => value.Manifest.Id));
        Publish(discovered, rejected);
    }

    private static IEnumerable<string> EnumerateManifestPaths(string pluginRoot)
    {
        var rootManifest = Path.Combine(pluginRoot, "mod.json");
        if (File.Exists(rootManifest))
        {
            yield return rootManifest;
        }

        foreach (var directory in Directory.EnumerateDirectories(pluginRoot).OrderBy(path => path))
        {
            var manifestPath = Path.Combine(directory, "mod.json");
            if (File.Exists(manifestPath))
            {
                yield return manifestPath;
            }
        }
    }

    private static void Publish(
        IReadOnlyList<ModDescriptor> discovered,
        IReadOnlyList<ModRejectedEvent> rejected)
    {
        ModRegistry.SetSnapshot(discovered, rejected);

        foreach (var descriptor in discovered)
        {
            GameEvents.RaiseModDiscovered(descriptor);
        }

        foreach (var rejection in rejected)
        {
            Plugin.Logger?.LogWarning(
                $"[ModRegistry] Rejected '{rejection.DirectoryPath}': {rejection.Reason}");
            GameEvents.RaiseModRejected(
                rejection.DirectoryPath,
                rejection.ModId,
                rejection.Reason);
        }

        Plugin.Logger?.LogInfo(
            $"[ModRegistry] Scan complete: {discovered.Count} valid, " +
            $"{rejected.Count} rejected.");
    }

    private static void Reject(
        ICollection<ModRejectedEvent> rejected,
        string directoryPath,
        string? modId,
        string reason)
    {
        rejected.Add(new ModRejectedEvent(Path.GetFullPath(directoryPath), modId, reason));
    }
}

public sealed class ModRegistryRunner : UnityEngine.MonoBehaviour
{
    private bool _scanned;

    public ModRegistryRunner(IntPtr pointer) : base(pointer)
    {
    }

    private void Update()
    {
        if (_scanned)
        {
            return;
        }

        _scanned = true;
        var modsRoot = Path.Combine(BepInEx.Paths.BepInExRootPath, "mods");
        ModRegistryScanner.Scan(new[] { modsRoot, BepInEx.Paths.PluginPath });
        ModHost.Current?.Initialize(ModRegistry.DiscoveredMods);
        GameEvents.RaiseModRegistryReady(
            ModRegistry.DiscoveredMods,
            ModRegistry.RejectedMods);
        UnityEngine.Object.Destroy(this);
    }
}
