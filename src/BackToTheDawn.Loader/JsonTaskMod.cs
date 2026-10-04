using System.Text.Json;
using BackToTheDawn.ModAPI;

namespace BackToTheDawn.Loader;

/// <summary>
/// Loads task definitions from a Mod's tasks directory. For C# Mods this is
/// called before IMod.Initialize, so the entry point can accept and progress
/// the JSON-defined tasks through TaskApi.
/// </summary>
internal sealed class JsonTaskMod
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public void Initialize(ModContext context, bool required = false)
    {
        var definitions = new List<LoadedTask>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in EnumerateTaskFiles(context.ModDirectory))
        {
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(path), DocumentOptions);
                if (json.RootElement.ValueKind != JsonValueKind.Object ||
                    !TryGetType(json.RootElement, out var type) ||
                    !string.Equals(type, "task", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var document = json.RootElement.Deserialize<TaskDocument>(JsonOptions)
                    ?? throw new InvalidDataException("A task definition must contain an object.");
                if (document.SchemaVersion != 1)
                {
                    throw new InvalidDataException(
                        $"Unsupported task schemaVersion {document.SchemaVersion}.");
                }

                if (string.IsNullOrWhiteSpace(document.Key) || document.Key.Contains(':'))
                {
                    throw new InvalidDataException(
                        "A task requires a local 'key' without a namespace prefix.");
                }

                if (string.IsNullOrWhiteSpace(document.Name))
                {
                    throw new InvalidDataException("A task requires 'name'.");
                }

                if (string.IsNullOrWhiteSpace(document.Description))
                {
                    throw new InvalidDataException("A task requires 'description'.");
                }

                var key = new ModTaskKey(context.Manifest.Id, document.Key.Trim());
                if (!keys.Add(key.Path))
                {
                    throw new InvalidDataException($"Duplicate task key '{key.Path}' within this Mod.");
                }

                var category = ParseCategory(document.Category);
                if (document.Objectives is null || document.Objectives.Length == 0)
                {
                    throw new InvalidDataException("A task requires a nonempty 'objectives' array.");
                }

                if (document.Objectives.Length > 64)
                {
                    throw new InvalidDataException("A task cannot have more than 64 objectives.");
                }

                var objectiveIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var objectives = document.Objectives.Select(objective =>
                {
                    if (objective is null)
                    {
                        throw new InvalidDataException("A task objective cannot be null.");
                    }

                    if (string.IsNullOrWhiteSpace(objective.Id))
                    {
                        throw new InvalidDataException("Each objective requires an 'id'.");
                    }

                    var objectiveId = new ModTaskKey("validation", objective.Id.Trim()).Path;
                    if (!objectiveIds.Add(objectiveId))
                    {
                        throw new InvalidDataException(
                            $"Duplicate objective id '{objectiveId}' in task '{key.Path}'.");
                    }

                    if (string.IsNullOrWhiteSpace(objective.Description))
                    {
                        throw new InvalidDataException(
                            $"Objective '{objectiveId}' requires 'description'.");
                    }

                    return new ModTaskObjective(objectiveId, objective.Description.Trim())
                    {
                        CompletedDescription = NormalizeOptional(objective.CompletedDescription),
                    };
                }).ToArray();

                var definition = new ModTaskDefinition(
                    key.Path,
                    document.Name.Trim(),
                    document.Description.Trim(),
                    Array.AsReadOnly(objectives))
                {
                    Category = category,
                    AcceptedDescription = NormalizeOptional(document.AcceptedDescription),
                    CompletedDescription = NormalizeOptional(document.CompletedDescription),
                };

                definitions.Add(new LoadedTask(path, key.Path, definition));
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    $"Task JSON '{path}': {exception.Message}", exception);
            }
        }

        if (definitions.Count == 0)
        {
            if (required)
            {
                throw new InvalidDataException(
                    "No task JSON was found. Each task definition needs 'type': 'task'.");
            }

            return;
        }

        // Validate every file before registering any definition. ModHost removes
        // all task registrations owned by this Mod if a later step fails.
        var tasks = TaskApi.For(context);
        foreach (var task in definitions)
        {
            var result = tasks.Register(task.Definition);
            if (!result.Succeeded)
            {
                throw new InvalidDataException($"Task JSON '{task.Path}': {result.Message}");
            }

            context.Logger.Info(
                $"[JsonTaskMod] Registered task '{result.Key}' ({task.Definition.Name}) from " +
                $"'{Path.GetRelativePath(context.ModDirectory, task.Path)}' ({result.Status}).");
        }

        context.Logger.Info($"[JsonTaskMod] Loaded {definitions.Count} task definition(s).");
    }

    public void Shutdown() { } // ModHost unregisters every task owned by this Mod.

    private static ModTaskCategory ParseCategory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ModTaskCategory.Prisoner;
        }

        if (Enum.TryParse<ModTaskCategory>(value.Trim(), ignoreCase: true, out var category) &&
            Enum.IsDefined(category))
        {
            return category;
        }

        throw new InvalidDataException(
            $"Unsupported task category '{value}'. Use a ModTaskCategory name.");
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IEnumerable<string> EnumerateTaskFiles(string modDirectory)
    {
        var taskDirectory = Path.Combine(modDirectory, "tasks");
        if (!Directory.Exists(taskDirectory))
        {
            return Array.Empty<string>();
        }

        var options = new EnumerationOptions
        {
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = false,
            MatchCasing = MatchCasing.CaseInsensitive,
            RecurseSubdirectories = true,
        };

        return Directory.EnumerateFiles(taskDirectory, "*.json", options)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryGetType(JsonElement root, out string? type)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, "type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            type = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()
                : null;
            return true;
        }

        type = null;
        return false;
    }

    private sealed record LoadedTask(string Path, string Key, ModTaskDefinition Definition);

    private sealed class TaskDocument
    {
        public int SchemaVersion { get; init; } = 1;
        public string? Key { get; init; }
        public string? Name { get; init; }
        public string? Description { get; init; }
        public string? Category { get; init; }
        public string? AcceptedDescription { get; init; }
        public string? CompletedDescription { get; init; }
        public TaskObjectiveDocument?[]? Objectives { get; init; }
    }

    private sealed class TaskObjectiveDocument
    {
        public string? Id { get; init; }
        public string? Description { get; init; }
        public string? CompletedDescription { get; init; }
    }
}
