using BackToTheDawn.ModAPI;
using UnityEngine;

namespace BackToTheDawn.Loader;

/// <summary>
/// Detects task changes from journal snapshots. This avoids patching the
/// IL2CPP TaskDetail constructor, which is unreliable in the current backend.
/// </summary>
internal static class TaskEventMonitor
{
    private const float PollIntervalSeconds = 0.2f;
    private static Dictionary<TaskOccurrenceKey, TaskSnapshot> _previous = new();
    private static float _nextPollTime;
    private static bool _hasBaseline;

    internal static void BeginSession()
    {
        _hasBaseline = false;
        _previous.Clear();
        _nextPollTime = 0f;

        if (!GameContextAdapter.TryCaptureAllTasks(out var tasks))
        {
            return;
        }

        _previous = IndexTasks(tasks);
        _hasBaseline = true;
        Plugin.Logger?.LogInfo(
            $"[Tasks] Task event monitor baseline captured: {_previous.Count} task record(s).");
    }

    internal static void Reset()
    {
        _previous.Clear();
        _hasBaseline = false;
        _nextPollTime = 0f;
    }

    internal static void Tick()
    {
        if (!GameContextAdapter.IsGameplayReady)
        {
            Reset();
            return;
        }

        if (Time.unscaledTime < _nextPollTime)
        {
            return;
        }

        _nextPollTime = Time.unscaledTime + PollIntervalSeconds;
        if (!GameContextAdapter.TryCaptureAllTasks(out var tasks))
        {
            return;
        }

        var current = IndexTasks(tasks);
        if (!_hasBaseline)
        {
            _previous = current;
            _hasBaseline = true;
            Plugin.Logger?.LogInfo(
                $"[Tasks] Task event monitor baseline captured: {_previous.Count} task record(s).");
            return;
        }

        foreach (var (key, task) in current)
        {
            if (!_previous.TryGetValue(key, out var previous))
            {
                if (IsActiveTask(task))
                {
                    GameEvents.RaiseTaskAccepted(task);
                }

                continue;
            }

            if (!AreEqual(previous, task))
            {
                GameEvents.RaiseTaskUpdated(previous, task, GetUpdateSource(previous, task));
            }
        }

        _previous = current;
    }

    private static Dictionary<TaskOccurrenceKey, TaskSnapshot> IndexTasks(
        IReadOnlyList<TaskSnapshot> tasks)
    {
        var occurrences = new Dictionary<TaskIdentity, int>();
        var indexed = new Dictionary<TaskOccurrenceKey, TaskSnapshot>();

        foreach (var task in tasks)
        {
            var identity = new TaskIdentity(task.Id, task.StartDay, task.TaskType);
            occurrences.TryGetValue(identity, out var occurrence);
            occurrences[identity] = occurrence + 1;
            indexed[new TaskOccurrenceKey(identity, occurrence)] = task;
        }

        return indexed;
    }

    private static bool AreEqual(TaskSnapshot left, TaskSnapshot right) =>
        left.Id == right.Id &&
        left.Name == right.Name &&
        left.TaskType == right.TaskType &&
        left.StartDay == right.StartDay &&
        left.IsComplete == right.IsComplete &&
        left.IsToBeCompleted == right.IsToBeCompleted &&
        left.IsFailed == right.IsFailed &&
        left.IsGivenUp == right.IsGivenUp &&
        left.IsTimedOut == right.IsTimedOut &&
        left.IsDiscontinued == right.IsDiscontinued &&
        left.Targets.SequenceEqual(right.Targets);

    private static bool IsActiveTask(TaskSnapshot task) =>
        !task.IsComplete && !task.IsFailed && !task.IsGivenUp &&
        !task.IsTimedOut && !task.IsDiscontinued;

    private static string GetUpdateSource(TaskSnapshot previous, TaskSnapshot current)
    {
        if (!previous.IsGivenUp && current.IsGivenUp)
        {
            return "give-up";
        }

        if (!previous.IsFailed && current.IsFailed)
        {
            return "failed";
        }

        if (!previous.IsTimedOut && current.IsTimedOut)
        {
            return "timed-out";
        }

        if (!previous.IsDiscontinued && current.IsDiscontinued)
        {
            return "discontinued";
        }

        if (!previous.IsComplete && current.IsComplete)
        {
            return "completed";
        }

        if (!previous.Targets.SequenceEqual(current.Targets))
        {
            return "target-progress";
        }

        return "state-change";
    }

    private readonly record struct TaskIdentity(int Id, int StartDay, string TaskType);

    private readonly record struct TaskOccurrenceKey(TaskIdentity Identity, int Occurrence);
}

internal sealed class TaskEventMonitorRunner : MonoBehaviour
{
    public TaskEventMonitorRunner(IntPtr pointer) : base(pointer)
    {
    }

    private void Update()
    {
        TaskRuntime.Tick();
        TaskEventMonitor.Tick();
    }
}
