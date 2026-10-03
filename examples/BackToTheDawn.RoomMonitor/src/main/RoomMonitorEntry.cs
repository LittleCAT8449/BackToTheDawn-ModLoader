using BackToTheDawn.ModAPI;

namespace BackToTheDawn.RoomMonitor;

public sealed class RoomMonitorEntry : IMod
{
    private const string CloneKey = "dev.backtothedawn.roommonitor:church_clone";
    private const string BatchProbeKey =
        "dev.backtothedawn.roommonitor.batch_probe:temporary";

    private readonly List<IDisposable> _subscriptions = new();
    private ModContext? _context;
    private bool _cloneAttempted;
    private bool _unregisterTested;
    private int _activeCloneId;

    public void Initialize(ModContext context)
    {
        _context = context;
        _subscriptions.Add(ModApi.Rooms.Subscribe(OnRoomChanged));
        _subscriptions.Add(ModApi.Events.Subscribe<GameplayReadyEvent>(_ => TryCreateChurchClone()));
        _subscriptions.Add(ModApi.Events.Subscribe<ArchiveLoadStartedEvent>(_ => _cloneAttempted = false));
        var current = ModApi.Rooms.Current;
        context.Logger.Info(
            current is null
                ? "Room Monitor initialized; waiting for gameplay."
                : $"Room Monitor initialized in {current.Id} ({current.Name}).");
    }

    public void Shutdown()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        _cloneAttempted = false;
        _unregisterTested = false;
        _activeCloneId = 0;
        _context?.Logger.Info("Room Monitor shut down.");
        _context = null;
    }

    private void TryCreateChurchClone()
    {
        if (_cloneAttempted)
        {
            return;
        }

        _cloneAttempted = true;
        var result = ModApi.Rooms.RegisterClone(
            CloneKey,
            "backtothedawn:church",
            "教堂副本");
        _context?.Logger.Info(
            $"Church clone registration: status={result.Status}, " +
            $"nativeId={result.NativeId}, message={result.Message}.");
        if (result.Succeeded)
        {
            _activeCloneId = result.NativeId;
            var duplicate = ModApi.Rooms.RegisterClone(
                CloneKey,
                "backtothedawn:church",
                "教堂副本");
            _context?.Logger.Info(
                $"Duplicate registration check: status={duplicate.Status}, " +
                $"expected={RoomRegistrationStatus.AlreadyRegistered}.");

            var isRegistered = ModApi.Rooms.IsRegistered(CloneKey);
            var found = ModApi.Rooms.TryGet(CloneKey, out var room);
            var rooms = ModApi.Rooms.GetRegisteredRooms();
            _context?.Logger.Info(
                $"Room query check: isRegistered={isRegistered}, found={found}, " +
                $"id={room?.NativeId}, listCount={rooms.Count}.");

            var batchProbe = ModApi.Rooms.RegisterClone(
                BatchProbeKey,
                "backtothedawn:church",
                "批量注销测试");
            var batchRemoved = ModApi.Rooms.UnregisterAll(
                "dev.backtothedawn.roommonitor.batch_probe");
            _context?.Logger.Info(
                $"Batch unregister check: registration={batchProbe.Status}, " +
                $"removed={batchRemoved}.");

            var entered = ModApi.Rooms.GoTo(CloneKey);
            _context?.Logger.Info($"Church clone transition requested: {entered}.");
        }
    }

    private void OnRoomChanged(RoomChangedEvent info)
    {
        var previous = info.Previous is null
            ? "<none>"
            : $"{info.Previous.Id} ({info.Previous.Name})";
        _context?.Logger.Info(
            $"Room changed: {previous} -> " +
            $"{info.Current.Id} ({info.Current.Name}).");

        if (_unregisterTested || info.Current.Id != _activeCloneId || _activeCloneId == 0)
        {
            return;
        }

        // Exercise explicit removal while the clone is active. The loader
        // should return to the base room before destroying the clone.
        _unregisterTested = true;
        var removed = ModApi.Rooms.Unregister(CloneKey);
        _context?.Logger.Info($"Active unregister check: removed={removed}.");
        var reRegistered = ModApi.Rooms.RegisterClone(
            CloneKey,
            "backtothedawn:church",
            "教堂副本");
        _context?.Logger.Info(
            $"Re-registration after unregister: status={reRegistered.Status}, " +
            $"nativeId={reRegistered.NativeId}.");
        if (reRegistered.Succeeded)
        {
            _activeCloneId = reRegistered.NativeId;
            ModApi.Rooms.GoTo(CloneKey);
        }
    }
}
