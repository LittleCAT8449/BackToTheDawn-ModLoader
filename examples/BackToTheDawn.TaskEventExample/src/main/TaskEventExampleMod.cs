using BackToTheDawn.ModAPI;

namespace BackToTheDawn.TaskEventExample;

public sealed class TaskEventExampleMod : IMod
{
    private readonly List<IDisposable> _subscriptions = new();
    private ModContext? _context;

    public void Initialize(ModContext context)
    {
        _context = context;
        _subscriptions.Add(ModApi.Events.Subscribe<TaskAcceptedEvent>(OnTaskAccepted));
        _subscriptions.Add(ModApi.Events.Subscribe<TaskUpdatedEvent>(OnTaskUpdated));
        _subscriptions.Add(ModApi.Events.Subscribe<GameplayReadyEvent>(OnGameplayReady));

        context.Logger.Info(
            "Subscribed to TaskAcceptedEvent and TaskUpdatedEvent. " +
            "Accept or progress a task to see event details in LogOutput.log.");
    }

    public void Shutdown()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        _context?.Logger.Info("Task event example shut down.");
        _context = null;
    }

    private void OnTaskAccepted(TaskAcceptedEvent info) =>
        _context?.Logger.Info($"Task accepted: {FormatTask(info.Task)}");

    private void OnTaskUpdated(TaskUpdatedEvent info)
    {
        _context?.Logger.Info(
            $"Task updated (source={info.Source}): " +
            $"before=[{FormatTask(info.Previous)}] " +
            $"after=[{FormatTask(info.Current)}]");
    }

    private void OnGameplayReady(GameplayReadyEvent _)
    {
        var activeTasks = ModApi.Tasks.ActiveTasks;
        _context?.Logger.Info($"Gameplay ready; active task count={activeTasks.Count}.");

        foreach (var task in activeTasks)
        {
            _context?.Logger.Info($"Active task snapshot: {FormatTask(task)}");
        }
    }

    private static string FormatTask(TaskSnapshot task)
    {
        var targets = task.Targets.Count == 0
            ? "none"
            : string.Join(
                "; ",
                task.Targets.Select(target =>
                    $"{target.Index}:{target.Description} " +
                    $"({(target.IsCompleted ? "done" : "pending")})"));

        return $"id={task.Id}, name='{task.Name}', type={task.TaskType}, " +
               $"startDay={task.StartDay}, objectives={task.CompletedTargetCount}/" +
               $"{task.Targets.Count}, complete={task.IsComplete}, failed={task.IsFailed}, " +
               $"givenUp={task.IsGivenUp}, timedOut={task.IsTimedOut}, " +
               $"discontinued={task.IsDiscontinued}, targets=[{targets}]";
    }
}
