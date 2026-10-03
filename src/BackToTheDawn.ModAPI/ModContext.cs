using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BackToTheDawn.ModAPI;

/// <summary>
/// Describes the public identity and entry point of a Mod.
/// </summary>
public sealed record ModManifest(
    string Id,
    string Name,
    string Version,
    string EntryAssembly,
    string EntryType,
    string[] Dependencies)
{
    /// <summary>True for a data-only phone Mod discovered through Manifest.json.</summary>
    [JsonIgnore]
    public bool IsJsonPhoneMod { get; internal init; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static ModManifest Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A manifest path is required.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The mod manifest was not found.", path);
        }

        var json = File.ReadAllText(path);
        var manifest = JsonSerializer.Deserialize<ModManifest>(json, JsonOptions)
            ?? throw new InvalidDataException("The mod manifest is empty.");

        manifest.Validate();
        return manifest;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new InvalidDataException("Mod manifest field 'id' is required.");
        }

        if (Id is "." or ".." || Id.Contains('/') || Id.Contains('\\'))
        {
            throw new InvalidDataException(
                "Mod manifest field 'id' must be a single path-safe identifier.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidDataException("Mod manifest field 'name' is required.");
        }

        if (string.IsNullOrWhiteSpace(Version))
        {
            throw new InvalidDataException("Mod manifest field 'version' is required.");
        }

        if (!IsJsonPhoneMod && (string.IsNullOrWhiteSpace(EntryAssembly) ||
            !string.Equals(Path.GetFileName(EntryAssembly), EntryAssembly, StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "Mod manifest field 'entryAssembly' must contain only an assembly file name.");
        }

        if (!IsJsonPhoneMod && string.IsNullOrWhiteSpace(EntryType))
        {
            throw new InvalidDataException("Mod manifest field 'entryType' is required.");
        }

        if (Dependencies is null)
        {
            throw new InvalidDataException("Mod manifest field 'dependencies' must be an array.");
        }
    }
}

/// <summary>
/// A manifest that passed discovery and validation in the loader.
/// </summary>
public sealed record ModDescriptor(
    ModManifest Manifest,
    string DirectoryPath,
    string AssemblyPath)
{
    // AssemblyPath is empty for data-only JSON phone Mods.
    public string ResourceDirectory => Path.Combine(DirectoryPath, "resource");
}

/// <summary>
/// Read-only view of the manifests discovered by the loader.
/// </summary>
public static class ModRegistry
{
    private static IReadOnlyList<ModDescriptor> _discoveredMods = Array.Empty<ModDescriptor>();
    private static IReadOnlyList<ModRejectedEvent> _rejectedMods = Array.Empty<ModRejectedEvent>();

    public static bool IsReady { get; internal set; }

    public static IReadOnlyList<ModDescriptor> DiscoveredMods => _discoveredMods;

    public static IReadOnlyList<ModRejectedEvent> RejectedMods => _rejectedMods;

    internal static void SetSnapshot(
        IReadOnlyList<ModDescriptor> discoveredMods,
        IReadOnlyList<ModRejectedEvent> rejectedMods)
    {
        _discoveredMods = discoveredMods.ToArray();
        _rejectedMods = rejectedMods.ToArray();
        IsReady = true;
    }

    internal static void Clear()
    {
        _discoveredMods = Array.Empty<ModDescriptor>();
        _rejectedMods = Array.Empty<ModRejectedEvent>();
        IsReady = false;
    }
}

/// <summary>
/// Runtime information and services owned by one mod instance.
/// </summary>
public sealed class ModContext
{
    private ModContext(
        ModManifest manifest,
        string modDirectory,
        IModLogger logger,
        ModConfig config)
    {
        Manifest = manifest;
        ModDirectory = modDirectory;
        Logger = logger;
        Config = config;
        Resources = new ModResources(Path.Combine(modDirectory, "resource"));
    }

    public ModManifest Manifest { get; }

    public string ModDirectory { get; }

    public IModLogger Logger { get; }

    public ModConfig Config { get; }

    public ModResources Resources { get; }

    public static ModContext FromAssembly(
        Assembly entryAssembly,
        ModManifest manifest,
        IModLogger? logger = null,
        ModConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(entryAssembly);
        ArgumentNullException.ThrowIfNull(manifest);

        manifest.Validate();

        var assemblyDirectory = Path.GetDirectoryName(entryAssembly.Location);
        if (string.IsNullOrWhiteSpace(assemblyDirectory))
        {
            throw new InvalidOperationException(
                $"The entry assembly '{entryAssembly.FullName}' has no usable file location.");
        }

        return FromDirectory(assemblyDirectory, manifest, logger, config);
    }

    /// <summary>Creates a context for a Mod whose entry point is a directory, including JSON phone Mods.</summary>
    public static ModContext FromDirectory(
        string modDirectory,
        ModManifest manifest,
        IModLogger? logger = null,
        ModConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        manifest.Validate();
        if (string.IsNullOrWhiteSpace(modDirectory))
        {
            throw new ArgumentException("A Mod directory is required.", nameof(modDirectory));
        }
        var directory = Path.GetFullPath(modDirectory);
        return new ModContext(manifest, directory, logger ?? NullModLogger.Instance,
            config ?? new ModConfig(Path.Combine(directory, "config.json")));
    }
}

/// <summary>
/// Safe access to files in a mod's deployed resource directory.
/// </summary>
public sealed class ModResources
{
    private IModAssetBundleProvider? _assetBundleProvider;

    public ModResources(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("A resource root directory is required.", nameof(rootDirectory));
        }
        RootDirectory = Path.GetFullPath(rootDirectory);
    }

    public string RootDirectory { get; }

    public string GetPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("A relative resource path is required.", nameof(relativePath));
        }
        if (Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("Resource paths must be relative.", nameof(relativePath));
        }

        var fullPath = Path.GetFullPath(Path.Combine(RootDirectory, relativePath));
        var rootWithSeparator = RootDirectory.TrimEnd(Path.DirectorySeparatorChar) +
                                Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!fullPath.StartsWith(rootWithSeparator, comparison))
        {
            throw new ArgumentException(
                "The resource path must stay inside the mod resource directory.",
                nameof(relativePath));
        }

        EnsureNoReparsePoints(fullPath, relativePath);

        return fullPath;
    }

    public bool Exists(string relativePath) => File.Exists(GetPath(relativePath));

    public Stream OpenRead(string relativePath) => File.OpenRead(GetPath(relativePath));

    /// <summary>
    /// Loads and caches an AssetBundle from this Mod's resource directory.
    /// Bundle paths are always relative to that directory.
    /// </summary>
    public AssetBundleResult LoadBundle(string relativePath)
    {
        if (!TryResolvePath(relativePath, out var fullPath, out var failure))
        {
            return new AssetBundleResult(
                false,
                ResourceStatus.InvalidPath,
                relativePath ?? string.Empty,
                failure);
        }

        if (!File.Exists(fullPath))
        {
            return new AssetBundleResult(
                false,
                ResourceStatus.NotFound,
                relativePath,
                $"Resource file '{relativePath}' was not found.");
        }

        if (_assetBundleProvider is null)
        {
            return new AssetBundleResult(
                false,
                ResourceStatus.LoaderUnavailable,
                relativePath,
                "AssetBundle loading is only available from an initialized Mod context.");
        }

        return _assetBundleProvider.LoadBundle(fullPath, relativePath);
    }

    /// <summary>Loads an asset from a bundle previously loaded by this Mod.</summary>
    public AssetLoadResult<T> LoadAsset<T>(string bundlePath, string assetName)
    {
        if (string.IsNullOrWhiteSpace(assetName))
        {
            return new AssetLoadResult<T>(
                false,
                ResourceStatus.InvalidAssetName,
                bundlePath ?? string.Empty,
                assetName ?? string.Empty,
                default,
                "An asset name is required.");
        }

        if (!TryResolvePath(bundlePath, out var fullPath, out var failure))
        {
            return new AssetLoadResult<T>(
                false,
                ResourceStatus.InvalidPath,
                bundlePath ?? string.Empty,
                assetName,
                default,
                failure);
        }

        if (_assetBundleProvider is null)
        {
            return new AssetLoadResult<T>(
                false,
                ResourceStatus.LoaderUnavailable,
                bundlePath,
                assetName,
                default,
                "AssetBundle loading is only available from an initialized Mod context.");
        }

        var result = _assetBundleProvider.LoadAsset(
            fullPath,
            bundlePath,
            assetName,
            typeof(T));
        if (!result.Succeeded)
        {
            return new AssetLoadResult<T>(
                false,
                result.Status,
                bundlePath,
                assetName,
                default,
                result.Message);
        }

        if (result.Asset is T typedAsset)
        {
            return new AssetLoadResult<T>(
                true,
                ResourceStatus.Success,
                bundlePath,
                assetName,
                typedAsset,
                result.Message);
        }

        return new AssetLoadResult<T>(
            false,
            ResourceStatus.AssetTypeMismatch,
            bundlePath,
            assetName,
            default,
            $"Asset '{assetName}' is not compatible with requested type '{typeof(T).FullName}'.");
    }

    /// <summary>Unloads a bundle previously loaded by this Mod.</summary>
    public ResourceUnloadResult UnloadBundle(
        string relativePath,
        bool unloadAllObjects = false)
    {
        if (!TryResolvePath(relativePath, out var fullPath, out var failure))
        {
            return new ResourceUnloadResult(
                false,
                ResourceStatus.InvalidPath,
                relativePath ?? string.Empty,
                failure);
        }

        if (_assetBundleProvider is null)
        {
            return new ResourceUnloadResult(
                false,
                ResourceStatus.LoaderUnavailable,
                relativePath,
                "AssetBundle loading is only available from an initialized Mod context.");
        }

        return _assetBundleProvider.UnloadBundle(
            fullPath,
            relativePath,
            unloadAllObjects);
    }

    internal void AttachAssetBundleProvider(IModAssetBundleProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (_assetBundleProvider is not null)
        {
            throw new InvalidOperationException("An AssetBundle provider is already attached.");
        }

        _assetBundleProvider = provider;
    }

    internal void UnloadAllBundles() => _assetBundleProvider?.UnloadAllBundles();

    private bool TryResolvePath(
        string relativePath,
        out string fullPath,
        out string failure)
    {
        try
        {
            fullPath = GetPath(relativePath);
            failure = string.Empty;
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException or
            NotSupportedException)
        {
            fullPath = string.Empty;
            failure = exception.Message;
            return false;
        }
    }

    private void EnsureNoReparsePoints(string fullPath, string relativePath)
    {
        var currentPath = RootDirectory;
        CheckNotReparsePoint(currentPath, relativePath);

        var pathBelowRoot = Path.GetRelativePath(RootDirectory, fullPath);
        foreach (var segment in pathBelowRoot.Split(
                     new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);
            CheckNotReparsePoint(currentPath, relativePath);
        }
    }

    private static void CheckNotReparsePoint(string path, string relativePath)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new ArgumentException(
                    $"Resource path '{relativePath}' traverses a symbolic link or reparse point.",
                    nameof(relativePath));
            }
        }
        catch (FileNotFoundException)
        {
            // The final path may not exist yet. Missing files are reported by the operation.
        }
        catch (DirectoryNotFoundException)
        {
            // A missing parent means no existing descendant can be reached through it.
        }
    }
}
