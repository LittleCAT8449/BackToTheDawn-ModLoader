namespace BackToTheDawn.ModAPI;

/// <summary>
/// Task journal queries plus Mod-scoped task registration and progression.
/// </summary>
public sealed class TaskApi
{
    internal static Func<IReadOnlyList<TaskSnapshot>>? ActiveTasksProvider { get; set; }
    internal static Func<IReadOnlyList<TaskSnapshot>>? AllTasksProvider { get; set; }
    internal static Func<string, ModTaskKey, ModTaskDefinition, TaskMutationResult>?
        RegisterProvider { get; set; }
    internal static Func<string, ModTaskKey, TaskMutationResult>? AcceptProvider { get; set; }
    internal static Func<string, ModTaskKey, string, TaskMutationResult>?
        CompleteObjectiveProvider { get; set; }

    private readonly string? _ownerId;

    internal TaskApi()
    {
    }

    private TaskApi(string ownerId) => _ownerId = ownerId;

    /// <summary>Creates a task API scoped to the calling Mod's namespace.</summary>
    public static TaskApi For(ModContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new TaskApi(context.Manifest.Id);
    }

    /// <summary>Gets currently active tasks. The result is an immutable snapshot.</summary>
    public IReadOnlyList<TaskSnapshot> ActiveTasks => Read(ActiveTasksProvider);

    /// <summary>Gets all task records currently available in the task journal.</summary>
    public IReadOnlyList<TaskSnapshot> AllTasks => Read(AllTasksProvider);

    /// <summary>
    /// Gets every task record with this native task ID. A list is returned
    /// because repeatable tasks may have more than one record.
    /// </summary>
    public IReadOnlyList<TaskSnapshot> GetTasks(int taskId)
    {
        var matches = AllTasks.Where(task => task.Id == taskId).ToArray();
        return Array.AsReadOnly(matches);
    }

    /// <summary>Gets the first task record with this native task ID, if present.</summary>
    public bool TryGetTask(int taskId, out TaskSnapshot? task)
    {
        task = AllTasks.FirstOrDefault(candidate => candidate.Id == taskId);
        return task is not null;
    }

    /// <summary>
    /// Registers a task definition under this Mod's namespace. Task definitions
    /// are added to the game's task configuration when it becomes available.
    /// </summary>
    public TaskMutationResult Register(ModTaskDefinition definition)
    {
        EnsureOwner();
        ArgumentNullException.ThrowIfNull(definition);
        ValidateDefinition(definition);

        var key = new ModTaskKey(_ownerId!, definition.Id);
        var snapshot = definition with
        {
            Id = key.Path,
            Objectives = Array.AsReadOnly(definition.Objectives.ToArray())
        };
        return RegisterProvider?.Invoke(_ownerId!, key, snapshot)
               ?? Unavailable(key);
    }

    /// <summary>
    /// Accepts a task previously registered by this Mod. The task is accepted
    /// through the game's task manager and appears in the native task journal.
    /// </summary>
    public TaskMutationResult Accept(string taskId)
    {
        EnsureOwner();
        var key = new ModTaskKey(_ownerId!, taskId);
        return AcceptProvider?.Invoke(_ownerId!, key) ?? Unavailable(key);
    }

    /// <summary>
    /// Completes a manually controlled objective of an accepted task. The
    /// objective identifier is the local ID supplied in its definition.
    /// </summary>
    public TaskMutationResult CompleteObjective(string taskId, string objectiveId)
    {
        EnsureOwner();
        var key = new ModTaskKey(_ownerId!, taskId);
        if (string.IsNullOrWhiteSpace(objectiveId))
        {
            throw new ArgumentException("An objective ID is required.", nameof(objectiveId));
        }

        return CompleteObjectiveProvider?.Invoke(_ownerId!, key, objectiveId.Trim())
               ?? Unavailable(key);
    }

    internal static void Reset()
    {
        ActiveTasksProvider = null;
        AllTasksProvider = null;
        RegisterProvider = null;
        AcceptProvider = null;
        CompleteObjectiveProvider = null;
    }

    private void EnsureOwner()
    {
        if (_ownerId is null)
        {
            throw new InvalidOperationException(
                "Use TaskApi.For(context) to register or modify tasks.");
        }
    }

    private static TaskMutationResult Unavailable(ModTaskKey key) =>
        new(key, TaskMutationStatus.RuntimeUnavailable,
            "The Loader task runtime is unavailable.");

    private static void ValidateDefinition(ModTaskDefinition definition)
    {
        _ = new ModTaskKey("validation", definition.Id);
        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            throw new ArgumentException("A task name is required.", nameof(definition));
        }

        if (string.IsNullOrWhiteSpace(definition.Description))
        {
            throw new ArgumentException("A task description is required.", nameof(definition));
        }

        if (!Enum.IsDefined(definition.Category))
        {
            throw new ArgumentOutOfRangeException(nameof(definition),
                "The task category is not supported.");
        }

        if (definition.Objectives is null || definition.Objectives.Count == 0)
        {
            throw new ArgumentException("At least one task objective is required.", nameof(definition));
        }

        if (definition.Objectives.Count > 64)
        {
            throw new ArgumentException("A task cannot have more than 64 objectives.", nameof(definition));
        }

        var objectiveIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var objective in definition.Objectives)
        {
            ArgumentNullException.ThrowIfNull(objective);
            _ = new ModTaskKey("validation", objective.Id);
            if (string.IsNullOrWhiteSpace(objective.Description))
            {
                throw new ArgumentException(
                    $"Objective '{objective.Id}' needs a description.", nameof(definition));
            }

            if (!objectiveIds.Add(objective.Id.Trim()))
            {
                throw new ArgumentException(
                    $"Objective ID '{objective.Id}' is duplicated.", nameof(definition));
            }
        }
    }

    private static IReadOnlyList<TaskSnapshot> Read(
        Func<IReadOnlyList<TaskSnapshot>>? provider)
    {
        try
        {
            return provider?.Invoke() ?? Array.Empty<TaskSnapshot>();
        }
        catch
        {
            return Array.Empty<TaskSnapshot>();
        }
    }
}

/// <summary>A stable, detached snapshot of a task record.</summary>
public sealed record TaskSnapshot(
    int Id,
    string Name,
    string TaskType,
    int StartDay,
    bool IsComplete,
    bool IsToBeCompleted,
    bool IsFailed,
    bool IsGivenUp,
    bool IsTimedOut,
    bool IsDiscontinued,
    IReadOnlyList<TaskTargetSnapshot> Targets)
{
    public int CompletedTargetCount => Targets.Count(target => target.IsCompleted);
}

/// <summary>A detached snapshot of one objective within a task.</summary>
public sealed record TaskTargetSnapshot(
    int Index,
    string TargetType,
    string Description,
    bool IsCompleted,
    int FinishedAtGameTime);
