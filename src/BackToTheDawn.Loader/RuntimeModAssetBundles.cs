using BackToTheDawn.ModAPI;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BackToTheDawn.Loader;

internal sealed class RuntimeModAssetBundles : IModAssetBundleProvider
{
    private readonly Dictionary<string, AssetBundle> _loadedBundles = new(
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);
    private readonly IModLogger _logger;
    private readonly int _mainThreadId;

    internal RuntimeModAssetBundles(IModLogger logger)
    {
        _logger = logger;
        _mainThreadId = Environment.CurrentManagedThreadId;
    }

    public AssetBundleResult LoadBundle(string fullPath, string relativePath)
    {
        if (!IsMainThread())
        {
            return new AssetBundleResult(
                false,
                ResourceStatus.WrongThread,
                relativePath,
                "AssetBundle operations must be called on the Unity main thread.");
        }

        if (_loadedBundles.TryGetValue(fullPath, out var cachedBundle) && cachedBundle is not null)
        {
            return new AssetBundleResult(
                true,
                ResourceStatus.AlreadyLoaded,
                relativePath,
                "The AssetBundle is already loaded and was returned from this Mod's cache.");
        }

        try
        {
            var bundle = AssetBundle.LoadFromFile(fullPath);
            if (bundle is null)
            {
                const string message = "Unity could not read this file as an AssetBundle.";
                _logger.Error($"AssetBundle load failed for '{relativePath}': {message}");
                return new AssetBundleResult(
                    false,
                    ResourceStatus.BundleLoadFailed,
                    relativePath,
                    message);
            }

            _loadedBundles[fullPath] = bundle;
            _logger.Info($"Loaded AssetBundle '{relativePath}'.");
            return new AssetBundleResult(
                true,
                ResourceStatus.Success,
                relativePath,
                "AssetBundle loaded successfully.");
        }
        catch (Exception exception)
        {
            _logger.Error(
                $"AssetBundle load failed for '{relativePath}': {exception.GetType().Name}: " +
                exception.Message);
            return new AssetBundleResult(
                false,
                ResourceStatus.BundleLoadFailed,
                relativePath,
                exception.Message);
        }
    }

    public AssetLoadResult<object> LoadAsset(
        string fullBundlePath,
        string relativeBundlePath,
        string assetName,
        Type assetType)
    {
        if (!IsMainThread())
        {
            return new AssetLoadResult<object>(
                false,
                ResourceStatus.WrongThread,
                relativeBundlePath,
                assetName,
                null,
                "AssetBundle operations must be called on the Unity main thread.");
        }

        if (!typeof(UnityEngine.Object).IsAssignableFrom(assetType))
        {
            return new AssetLoadResult<object>(
                false,
                ResourceStatus.InvalidAssetType,
                relativeBundlePath,
                assetName,
                null,
                $"Requested type '{assetType.FullName}' is not a UnityEngine.Object type.");
        }

        if (!_loadedBundles.TryGetValue(fullBundlePath, out var bundle) || bundle is null)
        {
            return new AssetLoadResult<object>(
                false,
                ResourceStatus.BundleNotLoaded,
                relativeBundlePath,
                assetName,
                null,
                $"Bundle '{relativeBundlePath}' must be loaded before requesting assets from it.");
        }

        try
        {
            var asset = bundle.LoadAsset(assetName, Il2CppType.From(assetType));
            if (asset is null)
            {
                _logger.Warning(
                    $"Asset '{assetName}' was not found in bundle '{relativeBundlePath}'.");
                return new AssetLoadResult<object>(
                    false,
                    ResourceStatus.AssetNotFound,
                    relativeBundlePath,
                    assetName,
                    null,
                    $"Asset '{assetName}' was not found in bundle '{relativeBundlePath}'.");
            }

            _logger.Info(
                $"Loaded asset '{assetName}' ({assetType.FullName}) from '{relativeBundlePath}'.");
            if (asset is GameObject prefab)
            {
                AssetBundlePrefabDiagnostics.Inspect(
                    prefab,
                    relativeBundlePath,
                    assetName,
                    _logger);
            }

            return new AssetLoadResult<object>(
                true,
                ResourceStatus.Success,
                relativeBundlePath,
                assetName,
                asset,
                "Asset loaded successfully.");
        }
        catch (Exception exception)
        {
            _logger.Error(
                $"Asset load failed for '{assetName}' in '{relativeBundlePath}': " +
                $"{exception.GetType().Name}: {exception.Message}");
            return new AssetLoadResult<object>(
                false,
                ResourceStatus.AssetLoadFailed,
                relativeBundlePath,
                assetName,
                null,
                exception.Message);
        }
    }

    public ResourceUnloadResult UnloadBundle(
        string fullPath,
        string relativePath,
        bool unloadAllObjects)
    {
        if (!IsMainThread())
        {
            return new ResourceUnloadResult(
                false,
                ResourceStatus.WrongThread,
                relativePath,
                "AssetBundle operations must be called on the Unity main thread.");
        }

        if (!_loadedBundles.TryGetValue(fullPath, out var bundle) || bundle is null)
        {
            return new ResourceUnloadResult(
                false,
                ResourceStatus.BundleNotLoaded,
                relativePath,
                $"Bundle '{relativePath}' is not loaded by this Mod.");
        }

        try
        {
            bundle.Unload(unloadAllObjects);
            _loadedBundles.Remove(fullPath);
            _logger.Info(
                $"Unloaded AssetBundle '{relativePath}' (unloadAllObjects={unloadAllObjects}).");
            return new ResourceUnloadResult(
                true,
                ResourceStatus.Success,
                relativePath,
                "AssetBundle unloaded successfully.");
        }
        catch (Exception exception)
        {
            _logger.Error(
                $"AssetBundle unload failed for '{relativePath}': " +
                $"{exception.GetType().Name}: {exception.Message}");
            return new ResourceUnloadResult(
                false,
                ResourceStatus.UnloadFailed,
                relativePath,
                exception.Message);
        }
    }

    public void UnloadAllBundles()
    {
        if (!IsMainThread())
        {
            _logger.Warning(
                "Skipped automatic AssetBundle cleanup because shutdown ran off the Unity main thread.");
            return;
        }

        foreach (var (path, bundle) in _loadedBundles.ToArray())
        {
            try
            {
                if (bundle is not null)
                {
                    // Preserve already-instantiated scene objects during Mod shutdown.
                    bundle.Unload(unloadAllLoadedObjects: false);
                }

                _loadedBundles.Remove(path);
            }
            catch (Exception exception)
            {
                _logger.Warning(
                    $"Automatic AssetBundle cleanup failed for '{Path.GetFileName(path)}': " +
                    exception.Message);
            }
        }
    }

    private bool IsMainThread() => Environment.CurrentManagedThreadId == _mainThreadId;
}
