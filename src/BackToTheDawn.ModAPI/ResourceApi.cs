namespace BackToTheDawn.ModAPI;

/// <summary>Describes the result of a mod resource operation.</summary>
public enum ResourceStatus
{
    Success,
    AlreadyLoaded,
    InvalidPath,
    NotFound,
    LoaderUnavailable,
    BundleLoadFailed,
    BundleNotLoaded,
    WrongThread,
    InvalidAssetName,
    InvalidAssetType,
    AssetNotFound,
    AssetLoadFailed,
    AssetTypeMismatch,
    UnloadFailed
}

/// <summary>Result returned when loading an AssetBundle.</summary>
public sealed record AssetBundleResult(
    bool Succeeded,
    ResourceStatus Status,
    string Path,
    string Message);

/// <summary>Result returned when loading a typed asset from a bundle.</summary>
public sealed record AssetLoadResult<T>(
    bool Succeeded,
    ResourceStatus Status,
    string BundlePath,
    string AssetName,
    T? Asset,
    string Message);

/// <summary>Result returned when unloading an AssetBundle.</summary>
public sealed record ResourceUnloadResult(
    bool Succeeded,
    ResourceStatus Status,
    string Path,
    string Message);

internal interface IModAssetBundleProvider
{
    AssetBundleResult LoadBundle(string fullPath, string relativePath);

    AssetLoadResult<object> LoadAsset(
        string fullBundlePath,
        string relativeBundlePath,
        string assetName,
        Type assetType);

    ResourceUnloadResult UnloadBundle(
        string fullPath,
        string relativePath,
        bool unloadAllObjects);

    void UnloadAllBundles();
}
