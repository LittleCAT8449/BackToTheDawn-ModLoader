using System.Reflection;
using System.Text.Json;

namespace BackToTheDawn.ModAPI;

/// <summary>
/// Describes the public identity and entry assembly of a mod.
/// </summary>
public sealed record ModManifest(
    string Id,
    string Name,
    string Version,
    string EntryAssembly,
    string EntryType,
    string[] Dependencies)
{
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

        if (string.IsNullOrWhiteSpace(EntryAssembly) ||
            !string.Equals(Path.GetFileName(EntryAssembly), EntryAssembly, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Mod manifest field 'entryAssembly' must contain only an assembly file name.");
        }

        if (string.IsNullOrWhiteSpace(EntryType))
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

        return new ModContext(
            manifest,
            Path.GetFullPath(assemblyDirectory),
            logger ?? NullModLogger.Instance,
            config ?? new ModConfig(Path.Combine(assemblyDirectory, "config.json")));
    }
}

/// <summary>
/// Safe access to files in a mod's deployed resource directory.
/// </summary>
public sealed class ModResources
{
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

        return fullPath;
    }

    public bool Exists(string relativePath) => File.Exists(GetPath(relativePath));

    public Stream OpenRead(string relativePath) => File.OpenRead(GetPath(relativePath));
}
