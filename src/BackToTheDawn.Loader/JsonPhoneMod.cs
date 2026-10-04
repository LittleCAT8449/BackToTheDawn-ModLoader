using System.Text.Json;
using System.Text.Json.Serialization;
using BackToTheDawn.ModAPI;
using BackToTheDawn.PhoneAPI;

namespace BackToTheDawn.Loader;

/// <summary>Adapts data-only phone Mods to the normal discovery and IMod lifecycle.</summary>
internal sealed class JsonPhoneMod : IMod
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    internal static ModManifest? LoadManifest(string path)
    {
        var document = JsonSerializer.Deserialize<ManifestDocument>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Manifest.json must contain an object.");
        if (document.IsPhoneMod is null && document.IsShopMod is null)
        {
            throw new InvalidDataException(
                "Manifest.json requires at least one boolean field: 'isPhoneMod' or 'isShopMod'.");
        }
        var isPhoneMod = document.IsPhoneMod == true;
        var isShopMod = document.IsShopMod == true;
        if (!isPhoneMod && !isShopMod)
        {
            Plugin.Logger?.LogInfo($"[JsonPhoneMod] Skipping inactive JSON Manifest.json: '{path}'.");
            return null;
        }
        if (document.SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported JSON Mod manifest schemaVersion {document.SchemaVersion}.");
        }
        var ownerId = document.Namespace?.Trim();
        if (string.IsNullOrWhiteSpace(ownerId) || !IsAsciiLetterOrDigit(ownerId[0]) ||
            ownerId.Any(character => !IsAsciiLetterOrDigit(character) && character is not '.' and not '_' and not '-'))
        {
            throw new InvalidDataException(
                "Manifest.json requires 'namespace': start with a letter or digit, then use letters, digits, '.', '_' or '-'.");
        }
        if (document.Dependencies is null || document.Dependencies.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException("Manifest dependencies must be an array of nonempty Mod IDs.");
        }
        var manifest = new ModManifest(ownerId, document.Name?.Trim() ?? ownerId,
            document.Version?.Trim() ?? "1.0.0", string.Empty, string.Empty, document.Dependencies)
        {
            IsJsonPhoneMod = isPhoneMod,
            IsJsonShopMod = isShopMod,
        };
        manifest.Validate();
        return manifest;
    }

    public void Initialize(ModContext context)
    {
        var conversations = new List<LoadedConversation>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var numbers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in EnumerateDialogueFiles(context.ModDirectory))
        {
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(path), DocumentOptions);
                if (json.RootElement.ValueKind != JsonValueKind.Object ||
                    !TryGetType(json.RootElement, out var type) ||
                    !string.Equals(type, "phoneConversation", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var document = json.RootElement.Deserialize<ConversationDocument>(JsonOptions)
                    ?? throw new InvalidDataException("A phone conversation must contain an object.");
                if (document.SchemaVersion != 1)
                {
                    throw new InvalidDataException($"Unsupported phone conversation schemaVersion {document.SchemaVersion}.");
                }
                if (string.IsNullOrWhiteSpace(document.Key))
                {
                    throw new InvalidDataException("A conversation requires 'key'.");
                }
                var key = document.Key.Trim();
                if (!key.Contains(':'))
                {
                    key = context.Manifest.Id + ":" + key;
                }
                else if (!key.StartsWith(context.Manifest.Id + ":", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("A conversation key must use this Mod's namespace.");
                }
                if (!keys.Add(key))
                {
                    throw new InvalidDataException($"Duplicate conversation key '{key}'.");
                }
                if (document.Number is null || document.Number.Length != 5 ||
                    document.Number.Any(character => character is < '0' or > '9'))
                {
                    throw new InvalidDataException("'number' must be a string containing exactly five digits.");
                }
                if (!numbers.Add(document.Number))
                {
                    throw new InvalidDataException($"Duplicate phone number '{document.Number}' within this Mod.");
                }
                if (string.IsNullOrWhiteSpace(document.DisplayName))
                {
                    throw new InvalidDataException("A conversation requires 'displayName'.");
                }
                if (document.Lines is null)
                {
                    throw new InvalidDataException("A conversation requires a 'lines' array.");
                }
                var lines = document.Lines.Select(line => ConvertLine(line, document.DisplayName)).ToArray();
                if (PhoneDialogueGraph.Validate(lines) is { } error)
                {
                    throw new InvalidDataException(error);
                }
                if (document.InteractionIconPath is not null)
                {
                    // Apply the same resource directory boundary as DLL Mods.
                    context.Resources.GetPath(document.InteractionIconPath);
                }
                conversations.Add(new LoadedConversation(path, key, document.Number,
                    document.DisplayName.Trim(), lines, document.InteractionIconPath, document.AddToPhoneBook));
            }
            catch (Exception exception)
            {
                throw new InvalidDataException($"Phone JSON '{path}': {exception.Message}", exception);
            }
        }
        if (conversations.Count == 0)
        {
            throw new InvalidDataException(
                "No phone dialogue JSON was found. Each dialogue needs 'type': 'phoneConversation'.");
        }

        // Parse and validate every file before registration. ModHost rolls back
        // this namespace if any runtime registration (including a duplicate number) fails.
        var phones = PhoneApi.For(context);
        foreach (var conversation in conversations)
        {
            var result = phones.RegisterConversation(conversation.Key, conversation.Lines,
                conversation.InteractionIconPath);
            if (!result.Succeeded)
            {
                throw new InvalidDataException($"Phone JSON '{conversation.Path}': {result.Message}");
            }
            var number = phones.RegisterNumber(conversation.Number, conversation.DisplayName,
                conversation.Key, conversation.AddToPhoneBook);
            if (!number.Succeeded)
            {
                throw new InvalidDataException($"Phone JSON '{conversation.Path}': {number.Message}");
            }
            context.Logger.Info(
                $"[JsonPhoneMod] Registered '{conversation.Number}' ({conversation.DisplayName}) " +
                $"from '{Path.GetRelativePath(context.ModDirectory, conversation.Path)}'.");
        }
        context.Logger.Info($"[JsonPhoneMod] Loaded {conversations.Count} phone conversation(s).");
    }

    public void Shutdown() { } // ModHost unregisters all phone data owned by this Mod.

    private static PhoneDialogueLine ConvertLine(LineDocument? line, string displayName)
    {
        if (line is null || line.Text is null || line.Options is null)
        {
            throw new InvalidDataException("Each line needs 'text'; a line or its options cannot be null.");
        }
        return new PhoneDialogueLine(
            line.Speaker ?? (line.SpeakerType == PhoneDialogueSpeakerType.Player ? "你" : displayName),
            line.Text, line.SpeakerType)
        {
            Id = line.Id,
            NextLineId = line.NextLineId,
            EndCall = line.EndCall,
            Options = line.Options.Select(option => option is null
                ? throw new InvalidDataException("A phone option cannot be null.")
                : new PhoneDialogueOption(option.Id ?? string.Empty, option.Text ?? string.Empty,
                    option.NextLineId, option.EndCall)).ToArray(),
        };
    }

    private static IEnumerable<string> EnumerateDialogueFiles(string root)
    {
        var directoryOptions = new EnumerationOptions
        {
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = false,
            MatchCasing = MatchCasing.CaseInsensitive,
        };
        foreach (var file in Directory.EnumerateFiles(root, "*.json", directoryOptions)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(file);
            if (!string.Equals(name, "Manifest.json", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(name, "mod.json", StringComparison.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
        foreach (var directory in Directory.EnumerateDirectories(root, "*", directoryOptions)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            // A nested Mod has its own namespace and must be loaded separately.
            if (File.Exists(Path.Combine(directory, "Manifest.json")) ||
                File.Exists(Path.Combine(directory, "mod.json")))
            {
                continue;
            }
            foreach (var file in EnumerateDialogueFiles(directory))
            {
                yield return file;
            }
        }
    }

    private static bool TryGetType(JsonElement root, out string? type)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, "type", StringComparison.OrdinalIgnoreCase))
            {
                type = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                return true;
            }
        }
        type = null;
        return false;
    }

    private static bool IsAsciiLetterOrDigit(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }

    private sealed record LoadedConversation(string Path, string Key, string Number,
        string DisplayName, PhoneDialogueLine[] Lines, string? InteractionIconPath, bool AddToPhoneBook);

    private sealed class ManifestDocument
    {
        public ManifestDocument() { }
        public int SchemaVersion { get; init; } = 1;
        [JsonPropertyName("namespace")]
        public string? Namespace { get; init; }
        public bool? IsPhoneMod { get; init; }
        public bool? IsShopMod { get; init; }
        public string? Name { get; init; }
        public string? Version { get; init; }
        public string[]? Dependencies { get; init; } = Array.Empty<string>();
    }

    private sealed class ConversationDocument
    {
        public ConversationDocument() { }
        public int SchemaVersion { get; init; } = 1;
        public string? Key { get; init; }
        public string? Number { get; init; }
        public string? DisplayName { get; init; }
        public bool AddToPhoneBook { get; init; } = true;
        public string? InteractionIconPath { get; init; }
        public LineDocument[]? Lines { get; init; }
    }

    private sealed class LineDocument
    {
        public LineDocument() { }
        public string? Id { get; init; }
        public string? Speaker { get; init; }
        public string? Text { get; init; }
        public PhoneDialogueSpeakerType SpeakerType { get; init; } = PhoneDialogueSpeakerType.Caller;
        public string? NextLineId { get; init; }
        public bool EndCall { get; init; }
        public OptionDocument[]? Options { get; init; } = Array.Empty<OptionDocument>();
    }

    private sealed class OptionDocument
    {
        public OptionDocument() { }
        public string? Id { get; init; }
        public string? Text { get; init; }
        public string? NextLineId { get; init; }
        public bool EndCall { get; init; }
    }
}
