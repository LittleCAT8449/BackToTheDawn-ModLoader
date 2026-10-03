using BackToTheDawn.ModAPI;
using System.Diagnostics;

var root = Path.Combine(Path.GetTempPath(), "btd-resource-tests-" + Guid.NewGuid().ToString("N"));
var outside = root + "-outside";
var assertions = 0;
Directory.CreateDirectory(root);
Directory.CreateDirectory(outside);

try
{
    var resourcesRoot = Path.Combine(root, "resource");
    Directory.CreateDirectory(resourcesRoot);
    File.WriteAllText(Path.Combine(resourcesRoot, "fixture.bundle"), "fixture");
    File.WriteAllText(Path.Combine(outside, "outside.bundle"), "outside");

    var resources = new ModResources(resourcesRoot);
    var provider = new FakeAssetBundleProvider();
    resources.AttachAssetBundleProvider(provider);
    var unavailableResources = new ModResources(resourcesRoot);

    Check(resources.Exists("fixture.bundle"), "resource Exists resolves files under the root");
    using (var stream = resources.OpenRead("fixture.bundle"))
    using (var reader = new StreamReader(stream))
    {
        Check(reader.ReadToEnd() == "fixture", "resource OpenRead resolves files under the root");
    }

    ExpectInvalidPath(
        resources,
        Path.GetRelativePath(resourcesRoot, Path.Combine(outside, "outside.bundle")));
    ExpectInvalidPath(resources, Path.Combine(outside, "outside.bundle"));

    var missing = resources.LoadBundle("missing.bundle");
    Check(missing.Status == ResourceStatus.NotFound, "missing bundles return NotFound");
    Check(provider.LoadCalls == 0, "missing bundles never reach the bundle provider");

    var unavailable = unavailableResources.LoadBundle("fixture.bundle");
    Check(unavailable.Status == ResourceStatus.LoaderUnavailable,
        "resources outside an initialized Mod context report LoaderUnavailable");

    var assetBeforeLoad = resources.LoadAsset<ProbeAsset>("fixture.bundle", "Probe");
    Check(assetBeforeLoad.Status == ResourceStatus.BundleNotLoaded,
        "assets cannot be read before their bundle is loaded");

    var load = resources.LoadBundle("fixture.bundle");
    Check(load.Succeeded && load.Status == ResourceStatus.Success, "bundle load result is forwarded");
    Check(provider.LastResolvedPath == Path.GetFullPath(Path.Combine(resourcesRoot, "fixture.bundle")),
        "provider receives the canonical in-root path");

    var cached = resources.LoadBundle("fixture.bundle");
    Check(cached.Succeeded && cached.Status == ResourceStatus.AlreadyLoaded,
        "repeated bundle loads report a cached success");

    var asset = resources.LoadAsset<ProbeAsset>("fixture.bundle", "Probe");
    Check(asset.Succeeded && asset.Asset?.Name == "Probe", "typed assets are returned to the caller");

    var mismatch = resources.LoadAsset<string>("fixture.bundle", "Probe");
    Check(mismatch.Status == ResourceStatus.AssetTypeMismatch, "wrong asset types fail explicitly");

    var unload = resources.UnloadBundle("fixture.bundle", unloadAllObjects: true);
    Check(unload.Succeeded && provider.LastUnloadAllObjects,
        "unload choice is forwarded to the provider");
    Check(resources.LoadAsset<ProbeAsset>("fixture.bundle", "Probe").Status == ResourceStatus.BundleNotLoaded,
        "assets cannot be read after their bundle is unloaded");

    resources.LoadBundle("fixture.bundle");
    resources.UnloadAllBundles();
    Check(provider.UnloadAllCalls == 1, "Mod shutdown cleanup reaches the bundle provider");

    TestSymlinkEscape(resources, resourcesRoot, outside);

    Console.WriteLine($"Resource API tests passed ({assertions} assertions).");
}
finally
{
    if (Directory.Exists(root))
    {
        Directory.Delete(root, recursive: true);
    }

    if (Directory.Exists(outside))
    {
        Directory.Delete(outside, recursive: true);
    }
}

void Check(bool condition, string description)
{
    if (!condition)
    {
        throw new InvalidOperationException("FAILED: " + description);
    }

    assertions++;
    Console.WriteLine("PASS: " + description);
}

void ExpectInvalidPath(ModResources resources, string path)
{
    var result = resources.LoadBundle(path);
    Check(result.Status == ResourceStatus.InvalidPath, $"unsafe path rejected: {path}");
}

void TestSymlinkEscape(ModResources resources, string resourcesRoot, string outside)
{
    var linkPath = Path.Combine(resourcesRoot, "linked");
    try
    {
        if (OperatingSystem.IsWindows())
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                Arguments = $"/c mklink /J \"{linkPath}\" \"{outside}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process is null || !process.WaitForExit(5000) || process.ExitCode != 0)
            {
                Console.WriteLine("SKIP: could not create a temporary directory junction.");
                return;
            }
        }
        else
        {
            Directory.CreateSymbolicLink(linkPath, outside);
        }

        var result = resources.LoadBundle("linked/outside.bundle");
        Check(result.Status == ResourceStatus.InvalidPath, "reparse-point escape is rejected");
    }
    catch (Exception exception) when (
        exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
    {
        Console.WriteLine("SKIP: reparse-point test unavailable on this host: " + exception.Message);
    }
    finally
    {
        if (Directory.Exists(linkPath))
        {
            Directory.Delete(linkPath, recursive: false);
        }
    }
}

sealed class ProbeAsset
{
    public string Name => "Probe";
}

sealed class FakeAssetBundleProvider : IModAssetBundleProvider
{
    private readonly HashSet<string> _loadedBundles = new(StringComparer.OrdinalIgnoreCase);

    public int LoadCalls { get; private set; }

    public int UnloadAllCalls { get; private set; }

    public string? LastResolvedPath { get; private set; }

    public bool LastUnloadAllObjects { get; private set; }

    public AssetBundleResult LoadBundle(string fullPath, string relativePath)
    {
        LoadCalls++;
        LastResolvedPath = fullPath;
        if (!_loadedBundles.Add(fullPath))
        {
            return new AssetBundleResult(
                true,
                ResourceStatus.AlreadyLoaded,
                relativePath,
                "cached");
        }

        return new AssetBundleResult(true, ResourceStatus.Success, relativePath, "loaded");
    }

    public AssetLoadResult<object> LoadAsset(
        string fullBundlePath,
        string relativeBundlePath,
        string assetName,
        Type assetType)
    {
        if (!_loadedBundles.Contains(fullBundlePath))
        {
            return new AssetLoadResult<object>(
                false,
                ResourceStatus.BundleNotLoaded,
                relativeBundlePath,
                assetName,
                null,
                "not loaded");
        }

        return new AssetLoadResult<object>(
            true,
            ResourceStatus.Success,
            relativeBundlePath,
            assetName,
            new ProbeAsset(),
            "loaded");
    }

    public ResourceUnloadResult UnloadBundle(
        string fullPath,
        string relativePath,
        bool unloadAllObjects)
    {
        LastUnloadAllObjects = unloadAllObjects;
        if (!_loadedBundles.Remove(fullPath))
        {
            return new ResourceUnloadResult(
                false,
                ResourceStatus.BundleNotLoaded,
                relativePath,
                "not loaded");
        }

        return new ResourceUnloadResult(true, ResourceStatus.Success, relativePath, "unloaded");
    }

    public void UnloadAllBundles()
    {
        UnloadAllCalls++;
        _loadedBundles.Clear();
    }
}
