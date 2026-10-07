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
        if (document.IsPhoneMod is null && document.IsShopMod is null &&
            document.IsTaskMod is null)
        {
            throw new InvalidDataException(
                "Manifest.json requires at least one boolean field: 'isPhoneMod', 'isShopMod', or 'isTaskMod'.");
        }
        var isPhoneMod = document.IsPhoneMod == true;
        var isShopMod = document.IsShopMod == true;
        var isTaskMod = document.IsTaskMod == true;
        if (!isPhoneMod && !isShopMod && !isTaskMod)
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
            IsJsonTaskMod = isTaskMod,
        };
        manifest.Validate();
        return manifest;
    }

    public void Initialize(ModContext context)
    {
        var conversations = new List<LoadedConversation>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var numbers = new HashSet<string>(StringComparer.Ordinal);
        var phones = PhoneApi.For(context);
        var tasks = TaskApi.For(context);
        var warnedMissingConditions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                var hasRoutes = document.Routes is not null || document.DefaultLines is not null;
                PhoneDialogueLine[] lines;
                Func<PhoneCallContext, IEnumerable<PhoneDialogueLine>>? lineFactory = null;
                if (hasRoutes)
                {
                    if (document.Lines is not null)
                    {
                        throw new InvalidDataException("Use either 'lines' or conditional 'routes' with 'defaultLines', not both.");
                    }
                    if (document.Routes is null || document.Routes.Length == 0)
                    {
                        throw new InvalidDataException("Conditional conversations require a nonempty 'routes' array.");
                    }
                    if (document.DefaultLines is null)
                    {
                        throw new InvalidDataException("Conditional conversations require 'defaultLines' as a fallback.");
                    }

                    var defaultLines = ConvertLines(document.DefaultLines, document.DisplayName, "defaultLines");
                    var routes = new List<LoadedRoute>();
                    var routeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var route in document.Routes)
                    {
                        if (route is null || string.IsNullOrWhiteSpace(route.Id))
                        {
                            throw new InvalidDataException("Each route requires a nonempty 'id'.");
                        }
                        var routeId = route.Id.Trim();
                        if (!routeIds.Add(routeId))
                        {
                            throw new InvalidDataException($"Duplicate route id '{routeId}'.");
                        }
                        if (route.When.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                        {
                            throw new InvalidDataException($"Route '{routeId}' requires a 'when' condition.");
                        }
                        if (route.Lines is null)
                        {
                            throw new InvalidDataException($"Route '{routeId}' requires a 'lines' array.");
                        }

                        var routeLines = ConvertLines(route.Lines, document.DisplayName, $"route '{routeId}'");
                        var condition = CompileCondition(route.When, context, tasks, phones, warnedMissingConditions);
                        routes.Add(new LoadedRoute(routeId, condition, routeLines));
                    }

                    lines = Array.Empty<PhoneDialogueLine>();
                    lineFactory = call =>
                    {
                        foreach (var route in routes)
                        {
                            if (route.Condition(call))
                            {
                                context.Logger.Debug(
                                    $"[JsonPhoneMod] Conversation '{key}' selected route '{route.Id}'.");
                                return route.Lines;
                            }
                        }

                        context.Logger.Debug(
                            $"[JsonPhoneMod] Conversation '{key}' selected its default route.");
                        return defaultLines;
                    };
                }
                else
                {
                    if (document.Lines is null)
                    {
                        throw new InvalidDataException("A conversation requires a 'lines' array or conditional 'routes'.");
                    }
                    lines = ConvertLines(document.Lines, document.DisplayName, "lines");
                }
                if (document.InteractionIconPath is not null)
                {
                    // Apply the same resource directory boundary as DLL Mods.
                    context.Resources.GetPath(document.InteractionIconPath);
                }
                conversations.Add(new LoadedConversation(path, key, document.Number,
                    document.DisplayName.Trim(), lines, lineFactory, document.InteractionIconPath,
                    document.AddToPhoneBook));
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
        foreach (var conversation in conversations)
        {
            var result = conversation.LineFactory is null
                ? phones.RegisterConversation(conversation.Key, conversation.Lines,
                    conversation.InteractionIconPath)
                : phones.RegisterDynamicConversation(conversation.Key, conversation.LineFactory,
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

    private static PhoneDialogueLine[] ConvertLines(
        LineDocument[] source,
        string displayName,
        string label)
    {
        var lines = source.Select(line => ConvertLine(line, displayName)).ToArray();
        if (PhoneDialogueGraph.Validate(lines) is { } error)
        {
            throw new InvalidDataException($"Invalid {label}: {error}");
        }

        return lines;
    }

    private static Func<PhoneCallContext, bool> CompileCondition(
        JsonElement element,
        ModContext context,
        TaskApi tasks,
        PhoneApi phones,
        HashSet<string> warnedMissingConditions)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("A route condition must be an object.");
        }

        var clauses = element.EnumerateObject().ToArray();
        if (clauses.Length != 1)
        {
            throw new InvalidDataException("A route condition must contain exactly one operator.");
        }

        var clause = clauses[0];
        switch (clause.Name.ToLowerInvariant())
        {
            case "all":
            case "any":
            {
                if (clause.Value.ValueKind != JsonValueKind.Array || clause.Value.GetArrayLength() == 0)
                {
                    throw new InvalidDataException($"Condition '{clause.Name}' requires a nonempty array.");
                }
                var children = clause.Value.EnumerateArray()
                    .Select(child => CompileCondition(child, context, tasks, phones, warnedMissingConditions))
                    .ToArray();
                return string.Equals(clause.Name, "all", StringComparison.OrdinalIgnoreCase)
                    ? call => children.All(child => child(call))
                    : call => children.Any(child => child(call));
            }
            case "not":
            {
                var child = CompileCondition(clause.Value, context, tasks, phones, warnedMissingConditions);
                return call => !child(call);
            }
            case "taskcompleted":
            {
                var taskId = ReadInt(clause.Value, "taskCompleted", minimum: 0);
                return _ => tasks.GetTasks(taskId).Any(task => task.IsComplete);
            }
            case "dayatleast":
            {
                var day = ReadInt(clause.Value, "dayAtLeast", minimum: 0);
                return call => (call.Time?.Day ?? int.MinValue) >= day;
            }
            case "dayatmost":
            {
                var day = ReadInt(clause.Value, "dayAtMost", minimum: 0);
                return call => (call.Time?.Day ?? int.MaxValue) <= day;
            }
            case "moneyatleast":
            {
                var money = ReadInt(clause.Value, "moneyAtLeast", minimum: 0);
                return _ => (GameContext.Player?.Money ?? -1) >= money;
            }
            case "timebetween":
            {
                if (clause.Value.ValueKind != JsonValueKind.Object ||
                    !TryGetProperty(clause.Value, "start", out var startElement) ||
                    !TryGetProperty(clause.Value, "end", out var endElement) ||
                    startElement.ValueKind != JsonValueKind.String || endElement.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidDataException("Condition 'timeBetween' requires string fields 'start' and 'end' in HH:mm format.");
                }
                var start = ParseClock(startElement.GetString()!, "timeBetween.start");
                var end = ParseClock(endElement.GetString()!, "timeBetween.end");
                return call =>
                {
                    if (call.Time is not { } time)
                    {
                        return false;
                    }
                    var current = time.Hour * 60 + time.Minute;
                    return start <= end
                        ? current >= start && current <= end
                        : current >= start || current <= end;
                };
            }
            case "itemcountatleast":
            {
                if (clause.Value.ValueKind != JsonValueKind.Object ||
                    !TryGetProperty(clause.Value, "item", out var itemElement) ||
                    itemElement.ValueKind != JsonValueKind.String ||
                    !ItemKey.TryParse(itemElement.GetString(), out var itemKey) ||
                    !TryGetProperty(clause.Value, "count", out var countElement) ||
                    !countElement.TryGetInt32(out var count) || count < 0)
                {
                    throw new InvalidDataException("Condition 'itemCountAtLeast' requires a namespaced 'item' and a nonnegative integer 'count'.");
                }
                return _ => (GameContext.Inventory?.GetCount(itemKey) ?? -1) >= count;
            }
            case "friendatleast":
            case "affectionatleast":
            {
                if (clause.Value.ValueKind != JsonValueKind.Object ||
                    !TryGetProperty(clause.Value, "characterId", out var characterElement) ||
                    !characterElement.TryGetInt32(out var characterId) ||
                    characterId < 0 ||
                    !TryGetProperty(clause.Value, "value", out var valueElement) ||
                    !valueElement.TryGetInt32(out var threshold))
                {
                    throw new InvalidDataException($"Condition '{clause.Name}' requires integer fields 'characterId' and 'value'.");
                }
                var useAffection = string.Equals(clause.Name, "affectionAtLeast", StringComparison.OrdinalIgnoreCase);
                return _ => GameContext.Relationships
                    .FirstOrDefault(relationship => relationship.CharacterId == characterId) is { } relationship &&
                    (useAffection ? relationship.Affection : relationship.Friend) >= threshold;
            }
            case "custom":
            {
                if (clause.Value.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(clause.Value.GetString()))
                {
                    throw new InvalidDataException("Condition 'custom' requires a nonempty condition key.");
                }
                var conditionKey = NormalizeConditionKey(clause.Value.GetString()!, context.Manifest);
                return call =>
                {
                    if (phones.TryEvaluateCondition(conditionKey, call, out var result))
                    {
                        return result;
                    }

                    if (warnedMissingConditions.Add(conditionKey))
                    {
                        context.Logger.Warning(
                            $"Phone JSON references condition '{conditionKey}', but it is unavailable. " +
                            "Check that the provider Mod is loaded and listed as a dependency.");
                    }
                    return false;
                };
            }
            default:
                throw new InvalidDataException($"Unknown phone condition operator '{clause.Name}'.");
        }
    }

    private static string NormalizeConditionKey(string key, ModManifest manifest)
    {
        var trimmed = key.Trim();
        if (!trimmed.Contains(':'))
        {
            return manifest.Id + ":" + trimmed;
        }

        var separator = trimmed.IndexOf(':');
        if (separator <= 0 || separator != trimmed.LastIndexOf(':') || separator == trimmed.Length - 1)
        {
            throw new InvalidDataException($"Invalid custom condition key '{key}'. Expected 'namespace:key'.");
        }

        var owner = trimmed[..separator];
        if (!string.Equals(owner, manifest.Id, StringComparison.OrdinalIgnoreCase) &&
            !manifest.Dependencies.Contains(owner, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Custom condition '{trimmed}' belongs to another Mod; add '{owner}' to Manifest.json dependencies.");
        }

        return trimmed;
    }

    private static int ReadInt(JsonElement value, string operatorName, int minimum = int.MinValue)
    {
        if (!value.TryGetInt32(out var result) || result < minimum)
        {
            throw new InvalidDataException(
                $"Condition '{operatorName}' requires an integer value of at least {minimum}.");
        }
        return result;
    }

    private static int ParseClock(string value, string fieldName)
    {
        if (value.Length != 5 || value[2] != ':' ||
            !int.TryParse(value[..2], out var hour) ||
            !int.TryParse(value[3..], out var minute) ||
            hour is < 0 or > 23 || minute is < 0 or > 59)
        {
            throw new InvalidDataException($"'{fieldName}' must use a valid 24-hour HH:mm value.");
        }
        return hour * 60 + minute;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
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
        string DisplayName, PhoneDialogueLine[] Lines,
        Func<PhoneCallContext, IEnumerable<PhoneDialogueLine>>? LineFactory,
        string? InteractionIconPath, bool AddToPhoneBook);

    private sealed record LoadedRoute(
        string Id,
        Func<PhoneCallContext, bool> Condition,
        PhoneDialogueLine[] Lines);

    private sealed class ManifestDocument
    {
        public ManifestDocument() { }
        public int SchemaVersion { get; init; } = 1;
        [JsonPropertyName("namespace")]
        public string? Namespace { get; init; }
        public bool? IsPhoneMod { get; init; }
        public bool? IsShopMod { get; init; }
        public bool? IsTaskMod { get; init; }
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
        public RouteDocument[]? Routes { get; init; }
        public LineDocument[]? DefaultLines { get; init; }
    }

    private sealed class RouteDocument
    {
        public RouteDocument() { }
        public string? Id { get; init; }
        public JsonElement When { get; init; }
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
