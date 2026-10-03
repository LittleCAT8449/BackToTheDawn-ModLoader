using System.Diagnostics;
using System.Reflection;
using BackToTheDawn.ModAPI;
using BepInEx.Logging;

namespace BackToTheDawn.Loader;

internal sealed class ModHost
{
    private const string LoaderModId = Plugin.PluginGuid;
    private readonly List<LoadedMod> _loadedMods = new();

    public static ModHost? Current { get; set; }

    public int ActiveCount => _loadedMods.Count;

    public int FailedCount { get; private set; }

    public void Initialize(IReadOnlyList<ModDescriptor> descriptors)
    {
        FailedCount = 0;
        Plugin.Logger?.LogInfo(
            $"[ModHost] Beginning initialization of {descriptors.Count} discovered Mod(s).");
        var remaining = descriptors.ToDictionary(
            descriptor => descriptor.Manifest.Id,
            StringComparer.OrdinalIgnoreCase);
        var initializedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (remaining.Count > 0)
        {
            var ready = remaining.Values
                .Where(descriptor => descriptor.Manifest.Dependencies.All(dependency =>
                    string.Equals(dependency, LoaderModId, StringComparison.OrdinalIgnoreCase) ||
                    !remaining.ContainsKey(dependency)))
                .OrderBy(descriptor => descriptor.Manifest.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (ready.Length == 0)
            {
                foreach (var descriptor in remaining.Values.ToArray())
                {
                    remaining.Remove(descriptor.Manifest.Id);
                    Fail(
                        descriptor,
                        "The Mod dependency graph contains a cycle or unresolved dependency.");
                }

                break;
            }

            foreach (var descriptor in ready)
            {
                remaining.Remove(descriptor.Manifest.Id);
                var failedDependencies = descriptor.Manifest.Dependencies
                    .Where(dependency =>
                        !string.Equals(dependency, LoaderModId, StringComparison.OrdinalIgnoreCase) &&
                        !initializedIds.Contains(dependency))
                    .ToArray();

                if (failedDependencies.Length > 0)
                {
                    Fail(
                        descriptor,
                        $"A dependency failed to initialize: {string.Join(", ", failedDependencies)}.");
                    continue;
                }

                if (InitializeOne(descriptor))
                {
                    initializedIds.Add(descriptor.Manifest.Id);
                }
            }
        }
    }

    public void Shutdown()
    {
        for (var index = _loadedMods.Count - 1; index >= 0; index--)
        {
            var loadedMod = _loadedMods[index];
            try
            {
                loadedMod.Instance.Shutdown();
                GameEvents.RaiseModShutdown(loadedMod.Descriptor);
                Plugin.Logger?.LogInfo(
                    $"[ModHost] Shut down {loadedMod.Descriptor.Manifest.Id}.");
            }
            catch (Exception exception)
            {
                Plugin.Logger?.LogError(
                    $"[ModHost] Shutdown failed for {loadedMod.Descriptor.Manifest.Id}: " +
                    Unwrap(exception));
            }
            finally
            {
                PhoneRuntime.UnregisterMod(loadedMod.Descriptor.Manifest.Id);
                loadedMod.Context.Resources.UnloadAllBundles();
            }
        }

        _loadedMods.Clear();
    }

    private bool InitializeOne(ModDescriptor descriptor)
    {
        IMod? instance = null;
        ModContext? context = null;
        var stopwatch = Stopwatch.StartNew();
        Plugin.Logger?.LogInfo(
            $"[ModHost] Initializing {descriptor.Manifest.Id} " +
            $"(depends: {string.Join(", ", descriptor.Manifest.Dependencies)}).");
        try
        {
            Assembly? assembly = null;
            if (descriptor.Manifest.IsJsonPhoneMod)
            {
                instance = new JsonPhoneMod();
            }
            else
            {
                assembly = Assembly.LoadFrom(descriptor.AssemblyPath);
                var entryType = assembly.GetType(descriptor.Manifest.EntryType, throwOnError: true)
                    ?? throw new InvalidOperationException(
                        $"Entry type '{descriptor.Manifest.EntryType}' was not found.");

                if (!typeof(IMod).IsAssignableFrom(entryType) || entryType.IsAbstract)
                {
                    throw new InvalidOperationException(
                        $"Entry type '{descriptor.Manifest.EntryType}' must be a concrete IMod.");
                }

                instance = Activator.CreateInstance(entryType) as IMod
                    ?? throw new InvalidOperationException(
                        $"Entry type '{descriptor.Manifest.EntryType}' must have a public parameterless constructor.");
            }

            var logger = new BepInExModLogger(
                Plugin.Logger ?? throw new InvalidOperationException("Loader logger is unavailable."),
                descriptor.Manifest.Id);
            var configPath = Path.Combine(
                BepInEx.Paths.ConfigPath,
                "mods",
                descriptor.Manifest.Id + ".json");
            var config = new ModConfig(configPath);
            if (config.LoadError is not null)
            {
                logger.Warning(
                    $"Configuration could not be loaded from '{config.FilePath}': {config.LoadError}. " +
                    "Defaults will be used.");
            }

            context = descriptor.Manifest.IsJsonPhoneMod
                ? ModContext.FromDirectory(descriptor.DirectoryPath, descriptor.Manifest, logger, config)
                : ModContext.FromAssembly(assembly!, descriptor.Manifest, logger, config);
            context.Resources.AttachAssetBundleProvider(new RuntimeModAssetBundles(logger));
            instance.Initialize(context);

            _loadedMods.Add(new LoadedMod(descriptor, instance, context));
            Plugin.Logger?.LogInfo(
                $"[ModHost] Initialized {descriptor.Manifest.Id} " +
                $"using {(descriptor.Manifest.IsJsonPhoneMod ? "JSON phone data" : descriptor.Manifest.EntryType)} " +
                $"in {stopwatch.ElapsedMilliseconds} ms.");
            GameEvents.RaiseModInitialized(descriptor,
                descriptor.Manifest.IsJsonPhoneMod ? nameof(JsonPhoneMod) : descriptor.Manifest.EntryType);
            return true;
        }
        catch (Exception exception)
        {
            if (instance is not null)
            {
                try
                {
                    instance.Shutdown();
                }
                catch (Exception shutdownException)
                {
                    Plugin.Logger?.LogError(
                        $"[ModHost] Cleanup failed after initialization failure for " +
                        $"{descriptor.Manifest.Id}: {Unwrap(shutdownException)}");
                }
            }

            PhoneRuntime.UnregisterMod(descriptor.Manifest.Id);
            context?.Resources.UnloadAllBundles();

            Fail(descriptor, Unwrap(exception).Message);
            return false;
        }
    }

    private void Fail(ModDescriptor descriptor, string reason)
    {
        FailedCount++;
        Plugin.Logger?.LogError(
            $"[ModHost] Initialization failed for {descriptor.Manifest.Id}: {reason}");
        GameEvents.RaiseModInitializationFailed(
            descriptor.DirectoryPath,
            descriptor.Manifest.Id,
            reason);
    }

    private static Exception Unwrap(Exception exception)
    {
        while (exception is TargetInvocationException invocation)
        {
            var innerException = invocation.InnerException;
            if (innerException is null)
            {
                break;
            }

            exception = innerException;
        }

        return exception;
    }

    private sealed record LoadedMod(
        ModDescriptor Descriptor,
        IMod Instance,
        ModContext Context);
}

internal sealed class BepInExModLogger : IModLogger
{
    private readonly ManualLogSource _logger;
    private readonly string _modId;

    public BepInExModLogger(ManualLogSource logger, string modId)
    {
        _logger = logger;
        _modId = modId;
    }

    public void Info(string message) => _logger.LogInfo($"[{_modId}] {message}");

    public void Warning(string message) => _logger.LogWarning($"[{_modId}] {message}");

    public void Error(string message) => _logger.LogError($"[{_modId}] {message}");
}
