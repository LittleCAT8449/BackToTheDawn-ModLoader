namespace BackToTheDawn.ModAPI;

/// <summary>
/// Stable, game-oriented lifecycle events exposed to mods.
/// </summary>
public static class GameEvents
{
    public static event Action<int>? StartupStepChanged;
    public static event Action<MainMenuEnteredEvent>? MainMenuEntered;
    public static event Action<ArchiveLoadEvent>? ArchiveLoadStarted;
    public static event Action<ArchiveLoadEvent>? ArchiveLoadInvocationReturned;
    public static event Action? GameplayReady;
    public static event Action<ItemCatalogReadyEvent>? ItemCatalogReady;
    public static event Action<ItemRuntimeReadyEvent>? ItemRuntimeReady;
    public static event Action<TimeChangedEvent>? TimeChanged;
    public static event Action<MapChangedEvent>? MapChanged;
    public static event Action<PlayerStateChangedEvent>? PlayerStateChanged;
    public static event Action<PlayerItemUsedEvent>? PlayerItemUsed;
    public static event Action<PlayerItemActionEvent>? PlayerItemAction;
    public static event Action<InventoryChangedEvent>? InventoryChanged;
    public static event Action<InventoryMovedEvent>? InventoryMoved;
    public static event Action<TradeDetectedEvent>? TradeDetected;
    public static event Action<ItemUseBeforeEvent>? ItemUseBefore;
    public static event Action<ItemUseAfterEvent>? ItemUseAfter;
    public static event Action<ModDiscoveredEvent>? ModDiscovered;
    public static event Action<ModRejectedEvent>? ModRejected;
    public static event Action<ModRegistryReadyEvent>? ModRegistryReady;
    public static event Action<ModInitializedEvent>? ModInitialized;
    public static event Action<ModInitializationFailedEvent>? ModInitializationFailed;
    public static event Action<ModShutdownEvent>? ModShutdown;

    /// <summary>
    /// Subscribes to one of the strongly typed game events and returns a handle
    /// that can be disposed during mod unload. This is the preferred API for
    /// mods because it keeps the event wiring and cleanup in one place.
    /// </summary>
    public static IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        where TEvent : IGameEvent
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (typeof(TEvent) == typeof(StartupStepChangedEvent))
        {
            Action<int> wrapper = step =>
                handler((TEvent)(object)new StartupStepChangedEvent(step));
            StartupStepChanged += wrapper;
            return new Subscription(() => StartupStepChanged -= wrapper);
        }

        if (typeof(TEvent) == typeof(MainMenuEnteredEvent))
        {
            Action<MainMenuEnteredEvent> wrapper = value =>
                handler((TEvent)(object)value);
            MainMenuEntered += wrapper;
            return new Subscription(() => MainMenuEntered -= wrapper);
        }

        if (typeof(TEvent) == typeof(ArchiveLoadStartedEvent))
        {
            Action<ArchiveLoadEvent> wrapper = value =>
                handler((TEvent)(object)new ArchiveLoadStartedEvent(value.ArchiveId));
            ArchiveLoadStarted += wrapper;
            return new Subscription(() => ArchiveLoadStarted -= wrapper);
        }

        if (typeof(TEvent) == typeof(ArchiveLoadInvocationReturnedEvent))
        {
            Action<ArchiveLoadEvent> wrapper = value =>
                handler((TEvent)(object)new ArchiveLoadInvocationReturnedEvent(value.ArchiveId));
            ArchiveLoadInvocationReturned += wrapper;
            return new Subscription(() => ArchiveLoadInvocationReturned -= wrapper);
        }

        if (typeof(TEvent) == typeof(GameplayReadyEvent))
        {
            Action wrapper = () => handler((TEvent)(object)new GameplayReadyEvent());
            GameplayReady += wrapper;
            return new Subscription(() => GameplayReady -= wrapper);
        }

        if (typeof(TEvent) == typeof(ItemCatalogReadyEvent))
        {
            Action<ItemCatalogReadyEvent> wrapper = value =>
                handler((TEvent)(object)value);
            ItemCatalogReady += wrapper;
            return new Subscription(() => ItemCatalogReady -= wrapper);
        }

        if (typeof(TEvent) == typeof(ItemRuntimeReadyEvent))
        {
            Action<ItemRuntimeReadyEvent> wrapper = value =>
                handler((TEvent)(object)value);
            ItemRuntimeReady += wrapper;
            return new Subscription(() => ItemRuntimeReady -= wrapper);
        }

        if (typeof(TEvent) == typeof(TimeChangedEvent))
        {
            Action<TimeChangedEvent> wrapper = value =>
                handler((TEvent)(object)value);
            TimeChanged += wrapper;
            return new Subscription(() => TimeChanged -= wrapper);
        }

        if (typeof(TEvent) == typeof(MapChangedEvent))
        {
            Action<MapChangedEvent> wrapper = value =>
                handler((TEvent)(object)value);
            MapChanged += wrapper;
            return new Subscription(() => MapChanged -= wrapper);
        }

        if (typeof(TEvent) == typeof(PlayerStateChangedEvent))
        {
            Action<PlayerStateChangedEvent> wrapper = value =>
                handler((TEvent)(object)value);
            PlayerStateChanged += wrapper;
            return new Subscription(() => PlayerStateChanged -= wrapper);
        }

        if (typeof(TEvent) == typeof(PlayerItemUsedEvent))
        {
            Action<PlayerItemUsedEvent> wrapper = value =>
                handler((TEvent)(object)value);
            PlayerItemUsed += wrapper;
            return new Subscription(() => PlayerItemUsed -= wrapper);
        }

        if (typeof(TEvent) == typeof(PlayerItemActionEvent))
        {
            Action<PlayerItemActionEvent> wrapper = value =>
                handler((TEvent)(object)value);
            PlayerItemAction += wrapper;
            return new Subscription(() => PlayerItemAction -= wrapper);
        }

        if (typeof(TEvent) == typeof(InventoryChangedEvent))
        {
            Action<InventoryChangedEvent> wrapper = value =>
                handler((TEvent)(object)value);
            InventoryChanged += wrapper;
            return new Subscription(() => InventoryChanged -= wrapper);
        }

        if (typeof(TEvent) == typeof(InventoryMovedEvent))
        {
            Action<InventoryMovedEvent> wrapper = value =>
                handler((TEvent)(object)value);
            InventoryMoved += wrapper;
            return new Subscription(() => InventoryMoved -= wrapper);
        }

        if (typeof(TEvent) == typeof(TradeDetectedEvent))
        {
            Action<TradeDetectedEvent> wrapper = value =>
                handler((TEvent)(object)value);
            TradeDetected += wrapper;
            return new Subscription(() => TradeDetected -= wrapper);
        }

        if (typeof(TEvent) == typeof(ItemUseBeforeEvent))
        {
            Action<ItemUseBeforeEvent> wrapper = value =>
                handler((TEvent)(object)value);
            ItemUseBefore += wrapper;
            return new Subscription(() => ItemUseBefore -= wrapper);
        }

        if (typeof(TEvent) == typeof(ItemUseAfterEvent))
        {
            Action<ItemUseAfterEvent> wrapper = value =>
                handler((TEvent)(object)value);
            ItemUseAfter += wrapper;
            return new Subscription(() => ItemUseAfter -= wrapper);
        }

        if (typeof(TEvent) == typeof(ModDiscoveredEvent))
        {
            Action<ModDiscoveredEvent> wrapper = value =>
                handler((TEvent)(object)value);
            ModDiscovered += wrapper;
            return new Subscription(() => ModDiscovered -= wrapper);
        }

        if (typeof(TEvent) == typeof(ModRejectedEvent))
        {
            Action<ModRejectedEvent> wrapper = value =>
                handler((TEvent)(object)value);
            ModRejected += wrapper;
            return new Subscription(() => ModRejected -= wrapper);
        }

        if (typeof(TEvent) == typeof(ModRegistryReadyEvent))
        {
            Action<ModRegistryReadyEvent> wrapper = value =>
                handler((TEvent)(object)value);
            ModRegistryReady += wrapper;
            return new Subscription(() => ModRegistryReady -= wrapper);
        }

        if (typeof(TEvent) == typeof(ModInitializedEvent))
        {
            Action<ModInitializedEvent> wrapper = value =>
                handler((TEvent)(object)value);
            ModInitialized += wrapper;
            return new Subscription(() => ModInitialized -= wrapper);
        }

        if (typeof(TEvent) == typeof(ModInitializationFailedEvent))
        {
            Action<ModInitializationFailedEvent> wrapper = value =>
                handler((TEvent)(object)value);
            ModInitializationFailed += wrapper;
            return new Subscription(() => ModInitializationFailed -= wrapper);
        }

        if (typeof(TEvent) == typeof(ModShutdownEvent))
        {
            Action<ModShutdownEvent> wrapper = value =>
                handler((TEvent)(object)value);
            ModShutdown += wrapper;
            return new Subscription(() => ModShutdown -= wrapper);
        }

        throw new NotSupportedException(
            $"The event type '{typeof(TEvent).FullName}' is not registered with GameEvents.");
    }

    internal static Action<string>? DiagnosticLog { get; set; }
    internal static Action<string>? ErrorLog { get; set; }

    internal static void RaiseStartupStepChanged(int step) =>
        Raise(StartupStepChanged, step, nameof(StartupStepChanged));

    internal static void RaiseMainMenuEntered(bool showsInputSelection, int immediateArchiveId) =>
        Raise(
            MainMenuEntered,
            new MainMenuEnteredEvent(showsInputSelection, immediateArchiveId),
            nameof(MainMenuEntered));

    internal static void RaiseArchiveLoadStarted(int archiveId) =>
        Raise(ArchiveLoadStarted, new ArchiveLoadEvent(archiveId), nameof(ArchiveLoadStarted));

    internal static void RaiseArchiveLoadInvocationReturned(int archiveId) =>
        Raise(
            ArchiveLoadInvocationReturned,
            new ArchiveLoadEvent(archiveId),
            nameof(ArchiveLoadInvocationReturned));

    internal static void RaiseGameplayReady() => Raise(GameplayReady, nameof(GameplayReady));

    internal static void RaiseItemCatalogReady(int count) =>
        Raise(ItemCatalogReady, new ItemCatalogReadyEvent(count), nameof(ItemCatalogReady));

    internal static void RaiseItemRuntimeReady(
        int catalogCount,
        int injectedCount,
        bool injectionEnabled) =>
        Raise(
            ItemRuntimeReady,
            new ItemRuntimeReadyEvent(catalogCount, injectedCount, injectionEnabled),
            nameof(ItemRuntimeReady));

    internal static void RaiseTimeChanged(GameTimeSnapshot previous, GameTimeSnapshot current) =>
        Raise(TimeChanged, new TimeChangedEvent(previous, current), nameof(TimeChanged));

    internal static void RaiseMapChanged(int previousMapId, int currentMapId, string currentMapName) =>
        Raise(
            MapChanged,
            new MapChangedEvent(previousMapId, currentMapId, currentMapName),
            nameof(MapChanged));

    internal static void RaisePlayerStateChanged(
        PlayerSnapshot previous,
        PlayerSnapshot current,
        string source) =>
        Raise(
            PlayerStateChanged,
            new PlayerStateChangedEvent(previous, current, source),
            nameof(PlayerStateChanged));

    internal static void RaisePlayerItemUsed(
        int characterId,
        ItemKey itemKey,
        int useCount) =>
        Raise(
            PlayerItemUsed,
            new PlayerItemUsedEvent(characterId, itemKey, useCount),
            nameof(PlayerItemUsed));

    internal static void RaisePlayerItemAction(
        int characterId,
        ItemKey? itemKey,
        int count,
        ItemActionKind action,
        bool succeeded,
        string source,
        int rawOperationType = -1) =>
        Raise(
            PlayerItemAction,
            new PlayerItemActionEvent(
                characterId,
                itemKey,
                count,
                action,
                succeeded,
                source,
                rawOperationType),
            nameof(PlayerItemAction));

    internal static void RaiseInventoryChanged(
        int characterId,
        ItemKey itemKey,
        int delta,
        int totalCount,
        InventoryChangeKind change,
        bool succeeded,
        string source,
        string reason) =>
        Raise(
            InventoryChanged,
            new InventoryChangedEvent(
                characterId,
                itemKey,
                delta,
                totalCount,
                change,
                succeeded,
                source,
                reason),
            nameof(InventoryChanged));

    internal static void RaiseInventoryMoved(
        int characterId,
        ItemKey itemKey,
        int count,
        InventoryLocation from,
        InventoryLocation to,
        bool succeeded,
        string source) =>
        Raise(
            InventoryMoved,
            new InventoryMovedEvent(
                characterId,
                itemKey,
                count,
                from,
                to,
                succeeded,
                source),
            nameof(InventoryMoved));

    internal static void RaiseTradeDetected(TradeDetectedEvent value) =>
        Raise(TradeDetected, value, nameof(TradeDetected));

    internal static ItemUseBeforeEvent RaiseItemUseBefore(ItemUseContext context)
    {
        var value = new ItemUseBeforeEvent(context);
        Raise(ItemUseBefore, value, nameof(ItemUseBefore));
        return value;
    }

    internal static void RaiseItemUseAfter(
        ItemUseContext context,
        ItemUseResult result) =>
        Raise(
            ItemUseAfter,
            new ItemUseAfterEvent(context, result),
            nameof(ItemUseAfter));

    internal static void RaiseModDiscovered(ModDescriptor mod) =>
        Raise(ModDiscovered, new ModDiscoveredEvent(mod), nameof(ModDiscovered));

    internal static void RaiseModRejected(string directoryPath, string? modId, string reason) =>
        Raise(
            ModRejected,
            new ModRejectedEvent(directoryPath, modId, reason),
            nameof(ModRejected));

    internal static void RaiseModRegistryReady(
        IReadOnlyList<ModDescriptor> discoveredMods,
        IReadOnlyList<ModRejectedEvent> rejectedMods) =>
        Raise(
            ModRegistryReady,
            new ModRegistryReadyEvent(discoveredMods.ToArray(), rejectedMods.ToArray()),
            nameof(ModRegistryReady));

    internal static void RaiseModInitialized(ModDescriptor mod, string entryType) =>
        Raise(ModInitialized, new ModInitializedEvent(mod, entryType), nameof(ModInitialized));

    internal static void RaiseModInitializationFailed(
        string directoryPath,
        string? modId,
        string reason) =>
        Raise(
            ModInitializationFailed,
            new ModInitializationFailedEvent(directoryPath, modId, reason),
            nameof(ModInitializationFailed));

    internal static void RaiseModShutdown(ModDescriptor mod) =>
        Raise(ModShutdown, new ModShutdownEvent(mod), nameof(ModShutdown));

    internal static void ClearSubscribers()
    {
        StartupStepChanged = null;
        MainMenuEntered = null;
        ArchiveLoadStarted = null;
        ArchiveLoadInvocationReturned = null;
        GameplayReady = null;
        ItemCatalogReady = null;
        ItemRuntimeReady = null;
        TimeChanged = null;
        MapChanged = null;
        PlayerStateChanged = null;
        PlayerItemUsed = null;
        PlayerItemAction = null;
        InventoryChanged = null;
        InventoryMoved = null;
        TradeDetected = null;
        ItemUseBefore = null;
        ItemUseAfter = null;
        ModDiscovered = null;
        ModRejected = null;
        ModRegistryReady = null;
        ModInitialized = null;
        ModInitializationFailed = null;
        ModShutdown = null;
        DiagnosticLog = null;
        ErrorLog = null;
    }

    private static void Raise<T>(Action<T>? handlers, T value, string eventName)
    {
        DiagnosticLog?.Invoke($"{eventName}: {value}");
        if (handlers is null)
        {
            return;
        }

        foreach (var subscriber in handlers.GetInvocationList().Cast<Action<T>>())
        {
            try
            {
                subscriber(value);
            }
            catch (Exception exception)
            {
                ErrorLog?.Invoke(
                    $"Subscriber '{subscriber.Method.DeclaringType?.FullName}.{subscriber.Method.Name}' " +
                    $"failed while handling {eventName}: {exception}");
            }
        }
    }

    private static void Raise(Action? handlers, string eventName)
    {
        DiagnosticLog?.Invoke(eventName);
        if (handlers is null)
        {
            return;
        }

        foreach (var subscriber in handlers.GetInvocationList().Cast<Action>())
        {
            try
            {
                subscriber();
            }
            catch (Exception exception)
            {
                ErrorLog?.Invoke(
                    $"Subscriber '{subscriber.Method.DeclaringType?.FullName}.{subscriber.Method.Name}' " +
                    $"failed while handling {eventName}: {exception}");
            }
        }
    }

    private sealed class Subscription : IDisposable
    {
        private Action? _dispose;

        public Subscription(Action dispose) => _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}

public interface IGameEvent
{
}

public sealed record StartupStepChangedEvent(int Step) : IGameEvent;

public sealed record MainMenuEnteredEvent(bool ShowsInputSelection, int ImmediateArchiveId) : IGameEvent;

public sealed record ArchiveLoadEvent(int ArchiveId);

public sealed record ArchiveLoadStartedEvent(int ArchiveId) : IGameEvent;

public sealed record ArchiveLoadInvocationReturnedEvent(int ArchiveId) : IGameEvent;

public sealed record GameplayReadyEvent : IGameEvent;

public sealed record TimeChangedEvent(GameTimeSnapshot Previous, GameTimeSnapshot Current) : IGameEvent;

public sealed record MapChangedEvent(int PreviousMapId, int CurrentMapId, string CurrentMapName) : IGameEvent;

public sealed record PlayerStateChangedEvent(
    PlayerSnapshot Previous,
    PlayerSnapshot Current,
    string Source) : IGameEvent;

public sealed record PlayerItemUsedEvent(
    int CharacterId,
    ItemKey ItemKey,
    int UseCount) : IGameEvent;

public enum ItemActionKind
{
    Unknown = 0,
    Use = 1,
    Arrange = 2,
    Destroy = 3,
    Equip = 4,
    Unequip = 5,
    Move = 6,
    OperationSelected = 7,
}

/// <summary>
/// Unified observation of a player item operation. ItemKey is null when an
/// operation applies to the whole pocket (for example Arrange), and
/// RawOperationType is populated for the low-level UI fallback hook.
/// </summary>
public sealed record PlayerItemActionEvent(
    int CharacterId,
    ItemKey? ItemKey,
    int Count,
    ItemActionKind Action,
    bool Succeeded,
    string Source,
    int RawOperationType = -1) : IGameEvent;

public sealed record ModDiscoveredEvent(ModDescriptor Mod) : IGameEvent;

public sealed record ModRejectedEvent(string DirectoryPath, string? ModId, string Reason) : IGameEvent;

public sealed record ModRegistryReadyEvent(
    ModDescriptor[] Mods,
    ModRejectedEvent[] Rejected) : IGameEvent;

public sealed record ModInitializedEvent(ModDescriptor Mod, string EntryType) : IGameEvent;

public sealed record ModInitializationFailedEvent(
    string DirectoryPath,
    string? ModId,
    string Reason) : IGameEvent;

public sealed record ModShutdownEvent(ModDescriptor Mod) : IGameEvent;
