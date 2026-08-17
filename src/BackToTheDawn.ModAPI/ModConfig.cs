using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BackToTheDawn.ModAPI;

/// <summary>
/// Small JSON-backed configuration store owned by one Mod.
/// </summary>
public sealed class ModConfig
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private readonly object _sync = new();
    private JsonObject _values = new();

    public ModConfig(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("A configuration file path is required.", nameof(filePath));
        }

        FilePath = Path.GetFullPath(filePath);
        Reload();
    }

    public string FilePath { get; }

    /// <summary>
    /// Contains the most recent load error, if a file was missing or malformed.
    /// A missing file is normal and does not set this property.
    /// </summary>
    public string? LoadError { get; private set; }

    public bool IsDirty
    {
        get
        {
            lock (_sync)
            {
                return _isDirty;
            }
        }
    }

    private bool _isDirty;

    /// <summary>
    /// Reads a typed value. If the key is absent, the default is added in memory and
    /// will be persisted by the next <see cref="Save"/> call.
    /// </summary>
    public T Get<T>(string key, T defaultValue)
    {
        ValidateKey(key);

        lock (_sync)
        {
            if (!_values.TryGetPropertyValue(key, out var node))
            {
                _values[key] = JsonSerializer.SerializeToNode(defaultValue, SerializerOptions);
                _isDirty = true;
                return defaultValue;
            }

            try
            {
                return Deserialize<T>(node) ?? defaultValue;
            }
            catch (JsonException)
            {
                return defaultValue;
            }
            catch (NotSupportedException)
            {
                return defaultValue;
            }
        }
    }

    public bool TryGet<T>(string key, out T value)
    {
        ValidateKey(key);

        lock (_sync)
        {
            if (!_values.TryGetPropertyValue(key, out var node))
            {
                value = default!;
                return false;
            }

            try
            {
                value = Deserialize<T>(node)!;
                return true;
            }
            catch (JsonException)
            {
                value = default!;
                return false;
            }
            catch (NotSupportedException)
            {
                value = default!;
                return false;
            }
        }
    }

    public void Set<T>(string key, T value)
    {
        ValidateKey(key);

        lock (_sync)
        {
            _values[key] = JsonSerializer.SerializeToNode(value, SerializerOptions);
            _isDirty = true;
        }
    }

    public bool Remove(string key)
    {
        ValidateKey(key);

        lock (_sync)
        {
            if (!_values.Remove(key))
            {
                return false;
            }

            _isDirty = true;
            return true;
        }
    }

    /// <summary>
    /// Reloads the file from disk. In-memory changes that were not saved are discarded.
    /// </summary>
    public void Reload()
    {
        lock (_sync)
        {
            _values = new JsonObject();
            LoadError = null;
            _isDirty = false;

            if (!File.Exists(FilePath))
            {
                return;
            }

            try
            {
                var json = File.ReadAllText(FilePath);
                var node = JsonNode.Parse(json, documentOptions: DocumentOptions);
                if (node is not JsonObject values)
                {
                    throw new InvalidDataException("The configuration root must be a JSON object.");
                }

                _values = values;
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is JsonException ||
                exception is InvalidDataException)
            {
                LoadError = exception.Message;
            }
        }
    }

    /// <summary>
    /// Writes the current values, creating the parent directory when needed.
    /// </summary>
    public void Save()
    {
        lock (_sync)
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException("The configuration path has no parent directory.");
            }

            Directory.CreateDirectory(directory);
            var temporaryPath = FilePath + ".tmp";
            var json = _values.ToJsonString(SerializerOptions);
            File.WriteAllText(temporaryPath, json + Environment.NewLine, new UTF8Encoding(false));
            File.Move(temporaryPath, FilePath, overwrite: true);
            _isDirty = false;
            LoadError = null;
        }
    }

    private static T? Deserialize<T>(JsonNode? node)
    {
        if (node is null)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(node.ToJsonString(), SerializerOptions);
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("A configuration key is required.", nameof(key));
        }
    }
}
