namespace BackToTheDawn.ModAPI;

/// <summary>A stable identifier for a task registered by a Mod.</summary>
public readonly record struct ModTaskKey
{
    public ModTaskKey(string @namespace, string path)
    {
        var normalized = new ShopKey(@namespace, path);
        Namespace = normalized.Namespace;
        Path = normalized.Path;
    }

    public string Namespace { get; }

    public string Path { get; }

    public override string ToString() => Namespace + ":" + Path;
}

/// <summary>Logical task-journal category. The Loader maps it to the game's UI task type.</summary>
public enum ModTaskCategory
{
    /// <summary>Prisoner tasks.</summary>
    Prisoner = 0,

    /// <summary>The original main story line.</summary>
    Mainline = 1,

    /// <summary>Prison guard captain tasks.</summary>
    PrisonGuardCaptain = 2,

    /// <summary>Prison guard mail room tasks.</summary>
    PrisonGuardMailRoom = 3,

    /// <summary>Barber shop tasks.</summary>
    BarberShop = 4,

    /// <summary>Da Jiao gang tasks.</summary>
    DaJiao = 5,

    /// <summary>Hei Zhua gang tasks.</summary>
    HeiZhua = 6,

    /// <summary>Jian Ya gang tasks.</summary>
    JianYa = 7,

    /// <summary>
    /// A general gang task displayed in the game's shared gang category.
    /// Use a faction-specific category when the task belongs to one gang.
    /// </summary>
    Gang = 8,

    /// <summary>Side quests.</summary>
    Side = 9,

    /// <summary>Prison escape tasks.</summary>
    Escape = 10,
}

/// <summary>A manually completed objective in a registered task.</summary>
public sealed record ModTaskObjective(string Id, string Description)
{
    /// <summary>Optional text shown after this objective is completed.</summary>
    public string? CompletedDescription { get; init; }
}

/// <summary>
/// Data needed to add a Mod-defined task to the game's native task journal.
/// The game has no generic task-offer hook, so a Mod accepts it through
/// <see cref="TaskApi.Accept"/> when its own conditions are met.
/// </summary>
public sealed record ModTaskDefinition(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<ModTaskObjective> Objectives)
{
    public ModTaskCategory Category { get; init; } = ModTaskCategory.Prisoner;

    public string? AcceptedDescription { get; init; }

    public string? CompletedDescription { get; init; }
}

public enum TaskMutationStatus
{
    Scheduled = 0,
    Applied = 1,
    AlreadyExists = 2,
    NotFound = 3,
    InvalidDefinition = 4,
    RuntimeUnavailable = 5,
    Failed = 6,
}

public sealed record TaskMutationResult(
    ModTaskKey Key,
    TaskMutationStatus Status,
    string Message)
{
    public bool Succeeded => Status is TaskMutationStatus.Scheduled or TaskMutationStatus.Applied;
}
