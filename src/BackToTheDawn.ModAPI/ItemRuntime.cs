namespace BackToTheDawn.ModAPI;

/// <summary>
/// Describes the outcome of connecting a virtual Mod item to the current
/// process' game item table.
/// </summary>
public enum ItemInjectionStatus
{
    Injected = 0,
    AlreadyInjected = 1,
    InjectionDisabled = 2,
    RuntimeUnavailable = 3,
    TemplateUnavailable = 4,
    Failed = 5,
}

/// <summary>
/// Detailed result returned by the Loader's runtime item bridge.
/// </summary>
public sealed record ItemInjectionResult(
    Item Item,
    ItemInjectionStatus Status,
    int? RuntimeId,
    string Message)
{
    public bool Succeeded =>
        Status is ItemInjectionStatus.Injected or ItemInjectionStatus.AlreadyInjected;
}

/// <summary>
/// Raised once the live item table has been observed and the optional Mod
/// item injection pass has completed for the current game process.
/// </summary>
public sealed record ItemRuntimeReadyEvent(
    int CatalogCount,
    int InjectedCount,
    bool InjectionEnabled) : IGameEvent;
