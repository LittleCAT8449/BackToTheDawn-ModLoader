using BackToTheDawn.ModAPI;
using UnityEngine;

namespace BackToTheDawn.AssetBundleProbe;

public sealed class AssetBundleProbeEntry : IMod
{
    private const string BundlePath = "probe/test.bundle";
    private const string PrefabName = "ResourceProbePrefab";
    private ModContext? _context;

    public void Initialize(ModContext context)
    {
        _context = context;

        var traversal = context.Resources.LoadBundle("../../mod.json");
        context.Logger.Info(
            $"Path traversal probe: status={traversal.Status}, rejected=" +
            $"{traversal.Status == ResourceStatus.InvalidPath}.");

        var loaded = context.Resources.LoadBundle(BundlePath);
        context.Logger.Info(
            $"Bundle probe: status={loaded.Status}, succeeded={loaded.Succeeded}, " +
            $"message={loaded.Message}");
        if (!loaded.Succeeded)
        {
            var assetBeforeBundle = context.Resources.LoadAsset<GameObject>(BundlePath, PrefabName);
            context.Logger.Info(
                $"Prefab-before-bundle probe: status={assetBeforeBundle.Status}.");
            return;
        }

        var cached = context.Resources.LoadBundle(BundlePath);
        context.Logger.Info(
            $"Bundle cache probe: status={cached.Status}, succeeded={cached.Succeeded}.");

        var prefab = context.Resources.LoadAsset<GameObject>(BundlePath, PrefabName);
        context.Logger.Info(
            $"Prefab probe: status={prefab.Status}, succeeded={prefab.Succeeded}, " +
            $"name={prefab.Asset?.name ?? "<none>"}.");

        var unloaded = context.Resources.UnloadBundle(BundlePath, unloadAllObjects: false);
        context.Logger.Info(
            $"Bundle unload probe: status={unloaded.Status}, succeeded={unloaded.Succeeded}.");
    }

    public void Shutdown()
    {
        _context?.Logger.Info("AssetBundle probe shut down.");
        _context = null;
    }
}
