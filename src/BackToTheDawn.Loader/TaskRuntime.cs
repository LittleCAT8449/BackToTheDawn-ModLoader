using System.Security.Cryptography;
using System.Text;
using BackToTheDawn.ModAPI;
using UnityEngine;

namespace BackToTheDawn.Loader;

/// <summary>Injects Mod task definitions into the game's task configuration.</summary>
internal static class TaskRuntime
{
    private const int FirstModTaskId = 1_000_000;
    private const int ModTaskIdRange = 1_000_000_000;
    // The game drops targets with unknown types and treats the resulting empty
    // task as completed. Use a valid but unreachable ArriveMap target so the
    // native journal creates and displays each objective; Mods complete it via
    // TaskApi.CompleteObjective when their own conditions are met.
    private const int ManualTargetType = (int)TaskTargetType.ArriveMap;
    private const string ManualTargetValue = "2147483647";

    private sealed class RegisteredTask(string ownerId, ModTaskKey key, ModTaskDefinition definition)
    {
        public string OwnerId { get; } = ownerId;
        public ModTaskKey Key { get; } = key;
        public ModTaskDefinition Definition { get; } = definition;
        public int NativeId { get; set; } = CreateNativeId(key);
        public bool Applied { get; set; }
        public c_task_main? MainRow { get; set; }
        public Il2CppSystem.Collections.Generic.Dictionary<int, c_task_target>? TargetDictionary { get; set; }
        public readonly List<c_task_target> TargetRows = new();
    }

    private sealed record PendingAcceptance(
        string TaskOwnerId,
        string RequesterId,
        ModTaskKey Key);

    private static readonly Dictionary<ModTaskKey, RegisteredTask> Registered = new();
    private static readonly List<PendingAcceptance> PendingAcceptances = new();

    internal static void InstallProviders()
    {
        TaskApi.RegisterProvider = Register;
        TaskApi.AcceptProvider = Accept;
        TaskApi.CompleteObjectiveProvider = CompleteObjective;
    }

    internal static void ClearProviders()
    {
        TaskApi.RegisterProvider = null;
        TaskApi.AcceptProvider = null;
        TaskApi.CompleteObjectiveProvider = null;
    }

    internal static void OnConfigInitialized()
    {
        ApplyPendingRegistrations();
        EnsureLocalization();
    }

    internal static void OnGameplayReady()
    {
        ApplyPendingRegistrations();
        EnsureLocalization();
    }

    internal static void Tick()
    {
        ApplyPendingRegistrations();
        EnsureLocalization();

        if (!GameContextAdapter.IsGameplayReady || PendingAcceptances.Count == 0)
        {
            return;
        }

        foreach (var pending in PendingAcceptances.ToArray())
        {
            if (!Registered.TryGetValue(pending.Key, out var registration))
            {
                PendingAcceptances.Remove(pending);
                continue;
            }

            if (!registration.Applied)
            {
                continue;
            }

            PendingAcceptances.Remove(pending);
            AcceptNative(registration);
        }
    }

    internal static void UnregisterMod(string ownerId)
    {
        PendingAcceptances.RemoveAll(entry =>
            entry.TaskOwnerId.Equals(ownerId, StringComparison.OrdinalIgnoreCase) ||
            entry.RequesterId.Equals(ownerId, StringComparison.OrdinalIgnoreCase));

        foreach (var registration in Registered.Values
                     .Where(entry => entry.OwnerId.Equals(ownerId, StringComparison.OrdinalIgnoreCase))
                     .ToArray())
        {
            if (HasTaskRecord(registration.NativeId))
            {
                Plugin.Logger?.LogWarning(
                    $"[Tasks] Keeping task configuration for {registration.Key} because a save " +
                    "contains a task record using it.");
                continue;
            }

            RemoveAppliedRegistration(registration);
            Registered.Remove(registration.Key);
        }
    }

    internal static void Reset()
    {
        // Leave injected rows in the current game session. Removing their config
        // while a save may still contain a task record would orphan that record.
        PendingAcceptances.Clear();
        Registered.Clear();
    }

    internal static bool IsManualObjectiveTask(int taskId) =>
        Registered.Values.Any(entry => entry.NativeId == taskId);

    internal static string GetTaskTypeName(int taskId, int nativeCategory)
    {
        var registration = Registered.Values.FirstOrDefault(entry => entry.NativeId == taskId);
        if (registration is not null)
        {
            return registration.Definition.Category.ToString();
        }

        return nativeCategory switch
        {
            (int)TaskMainType.Prisoner => nameof(ModTaskCategory.Prisoner),
            (int)TaskMainType.Mainline => nameof(ModTaskCategory.Mainline),
            (int)TaskMainType.PrisonGuardCaptain => nameof(ModTaskCategory.PrisonGuardCaptain),
            (int)TaskMainType.PrisonGuardMailRoom => nameof(ModTaskCategory.PrisonGuardMailRoom),
            (int)TaskMainType.BarberShop => nameof(ModTaskCategory.BarberShop),
            (int)TaskMainType.DaJiao => nameof(ModTaskCategory.DaJiao),
            (int)TaskMainType.HeiZhua => nameof(ModTaskCategory.HeiZhua),
            (int)TaskMainType.JianYa => nameof(ModTaskCategory.JianYa),
            9 => nameof(ModTaskCategory.Escape),
            10 => nameof(ModTaskCategory.Mainline),
            _ => ((TaskMainType)nativeCategory).ToString(),
        };
    }

    private static TaskMutationResult Register(
        string ownerId,
        ModTaskKey key,
        ModTaskDefinition definition)
    {
        if (!string.Equals(ownerId, key.Namespace, StringComparison.OrdinalIgnoreCase))
        {
            return Result(key, TaskMutationStatus.InvalidDefinition,
                "The task key namespace does not match the owning Mod.");
        }

        if (Registered.ContainsKey(key))
        {
            return Result(key, TaskMutationStatus.AlreadyExists,
                $"Task '{key}' is already registered.");
        }

        var registration = new RegisteredTask(ownerId, key, definition);
        Registered.Add(key, registration);

        if (TryApply(registration))
        {
            Plugin.Logger?.LogInfo(
                $"[Tasks] Registered Mod task '{key}' as native task {registration.NativeId} " +
                $"with category {definition.Category} (task_type={registration.MainRow?.task_type}) " +
                $"and {definition.Objectives.Count} manual objective(s).");
            return Result(key, TaskMutationStatus.Applied,
                $"Task '{key}' was added to the native task configuration.");
        }

        Plugin.Logger?.LogInfo(
            $"[Tasks] Queued Mod task '{key}' for registration when the game's task tables load.");
        return Result(key, TaskMutationStatus.Scheduled,
            "Task registration is queued until the game's task tables are available.");
    }

    private static TaskMutationResult Accept(string ownerId, ModTaskKey key)
    {
        if (!TryGetRegistration(key, out var registration))
        {
            return Result(key, TaskMutationStatus.NotFound,
                $"Task '{key}' is not registered. Check that its owner Mod is loaded.");
        }

        if (HasTaskRecord(registration.NativeId))
        {
            return Result(key, TaskMutationStatus.AlreadyExists,
                $"Task '{key}' already exists in the task journal.");
        }

        if (PendingAcceptances.Any(entry => entry.Key == key))
        {
            return Result(key, TaskMutationStatus.Scheduled,
                $"Task '{key}' is already queued for acceptance.");
        }

        PendingAcceptances.Add(new PendingAcceptance(registration.OwnerId, ownerId, key));
        return Result(key, TaskMutationStatus.Scheduled,
            "Task acceptance is queued until gameplay and its task configuration are ready.");
    }

    private static TaskMutationResult CompleteObjective(
        string ownerId,
        ModTaskKey key,
        string objectiveId)
    {
        if (!TryGetRegistration(key, out var registration))
        {
            return Result(key, TaskMutationStatus.NotFound,
                $"Task '{key}' is not registered. Check that its owner Mod is loaded.");
        }

        var objectiveIndex = -1;
        for (var index = 0; index < registration.Definition.Objectives.Count; index++)
        {
            if (registration.Definition.Objectives[index].Id.Equals(
                    objectiveId, StringComparison.OrdinalIgnoreCase))
            {
                objectiveIndex = index + 1;
                break;
            }
        }

        if (objectiveIndex < 0)
        {
            return Result(key, TaskMutationStatus.NotFound,
                $"Objective '{objectiveId}' was not found in task '{key}'.");
        }

        if (!GameContextAdapter.IsGameplayReady)
        {
            return Result(key, TaskMutationStatus.RuntimeUnavailable,
                "A game must be running before a task objective can be completed.");
        }

        try
        {
            var detail = TaskManage.GetAcceptTask(registration.NativeId);
            if (detail is null)
            {
                return Result(key, TaskMutationStatus.NotFound,
                    $"Task '{key}' is not active in the task journal.");
            }

            if (TaskTargetManage.IsFinishTaskTarget(registration.NativeId, objectiveIndex))
            {
                return Result(key, TaskMutationStatus.AlreadyExists,
                    $"Objective '{objectiveId}' is already complete.");
            }

            TaskTargetManage.ForceFinishTaskTarget(
                registration.NativeId,
                objectiveIndex,
                isShowLogTips: true);

            if (!TaskTargetManage.IsFinishTaskTarget(registration.NativeId, objectiveIndex))
            {
                return Result(key, TaskMutationStatus.Failed,
                    $"The game did not complete objective '{objectiveId}'.");
            }

            Plugin.Logger?.LogInfo(
                $"[Tasks] Mod {ownerId} completed objective '{objectiveId}' " +
                $"for task '{key}' (native task {registration.NativeId}, target {objectiveIndex}).");
            return Result(key, TaskMutationStatus.Applied,
                $"Objective '{objectiveId}' was completed.");
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError(
                $"[Tasks] Completing objective '{objectiveId}' for '{key}' failed: {exception}");
            return Result(key, TaskMutationStatus.Failed, exception.Message);
        }
    }

    private static void ApplyPendingRegistrations()
    {
        foreach (var registration in Registered.Values.Where(entry => !entry.Applied).ToArray())
        {
            try
            {
                if (!TryApply(registration))
                {
                    continue;
                }

                Plugin.Logger?.LogInfo(
                    $"[Tasks] Registered Mod task '{registration.Key}' as native task " +
                    $"{registration.NativeId} with category {registration.Definition.Category} " +
                    $"(task_type={registration.MainRow?.task_type}) and " +
                    $"{registration.Definition.Objectives.Count} manual objective(s).");
            }
            catch (Exception exception)
            {
                Plugin.Logger?.LogError(
                    $"[Tasks] Registering Mod task '{registration.Key}' failed: {exception}");
                RemoveAppliedRegistration(registration);
                Registered.Remove(registration.Key);
            }
        }
    }

    private static bool TryApply(RegisteredTask registration)
    {
        if (!TryGetTaskTables(out var taskMainRows, out var taskMainById,
                out var taskTargetRows, out var targetsByTask))
        {
            return false;
        }

        if (registration.Applied && IsStillApplied(registration, taskMainById, targetsByTask))
        {
            return true;
        }

        registration.Applied = false;
        if (taskMainById.ContainsKey(registration.NativeId) ||
            targetsByTask.ContainsKey(registration.NativeId))
        {
            registration.NativeId = FindFreeNativeId(registration.Key, taskMainById, targetsByTask);
        }

        var nextOrder = 0;
        for (var index = 0; index < taskMainRows.Count; index++)
        {
            var row = taskMainRows[index];
            if (row is not null)
            {
                nextOrder = Math.Max(nextOrder, row.task_order);
            }
        }

        nextOrder++;
        var nameKey = LocalizationKey(registration.Key, "name");
        var descriptionKey = LocalizationKey(registration.Key, "description");
        var acceptedKey = LocalizationKey(registration.Key, "accepted");
        var completedKey = LocalizationKey(registration.Key, "completed");
        var mainRow = new c_task_main
        {
            task_id = registration.NativeId,
            task_type = (int)ToNativeCategory(registration.Definition.Category),
            task_release_date = 0,
            task_order = nextOrder,
            task_action_id = string.Empty,
            task_deliver_NPC = 0,
            task_limit_time = 0,
            task_pic = string.Empty,
            task_reward = string.Empty,
            task_reward_2 = string.Empty,
            lead_limit = 0,
            L_task_name = nameKey,
            L_task_infor_des = descriptionKey,
            L_task_accept_des = acceptedKey,
            L_task_deliver_des = completedKey,
        };

        var targetDictionary = new Il2CppSystem.Collections.Generic.Dictionary<int, c_task_target>();
        var targetRows = new List<c_task_target>();
        var addedToMainList = false;
        var addedToMainDictionary = false;
        var addedToTargetDictionary = false;
        try
        {
            taskMainRows.Add(mainRow);
            addedToMainList = true;
            taskMainById.Add(registration.NativeId, mainRow);
            addedToMainDictionary = true;

            for (var index = 0; index < registration.Definition.Objectives.Count; index++)
            {
                var objective = registration.Definition.Objectives[index];
                var targetId = index + 1;
                var row = new c_task_target
                {
                    task_id = registration.NativeId,
                    task_target_order = targetId,
                    task_target_type = ManualTargetType,
                    task_target = ManualTargetValue,
                    L_target_des = LocalizationKey(registration.Key, "objective", objective.Id),
                    L_target_complete_des = LocalizationKey(
                        registration.Key, "objectiveComplete", objective.Id),
                    L_target_other_des = string.Empty,
                    get_item = string.Empty,
                    target_tips = string.Empty,
                    target_tips_limit = string.Empty,
                    father_task_target_order = "0",
                    clue_tips = string.Empty,
                };
                targetRows.Add(row);
                targetDictionary.Add(targetId, row);
                taskTargetRows.Add(row);
            }

            targetsByTask.Add(registration.NativeId, targetDictionary);
            addedToTargetDictionary = true;

            registration.MainRow = mainRow;
            registration.TargetDictionary = targetDictionary;
            registration.TargetRows.AddRange(targetRows);
            registration.Applied = true;
            EnsureLocalization(registration);
            return true;
        }
        catch
        {
            foreach (var row in targetRows)
            {
                taskTargetRows.Remove(row);
            }

            if (addedToTargetDictionary)
            {
                targetsByTask.Remove(registration.NativeId);
            }

            if (addedToMainDictionary)
            {
                taskMainById.Remove(registration.NativeId);
            }

            if (addedToMainList)
            {
                taskMainRows.Remove(mainRow);
            }

            throw;
        }
    }

    private static bool TryGetTaskTables(
        out Il2CppSystem.Collections.Generic.List<c_task_main> taskMainRows,
        out Il2CppSystem.Collections.Generic.Dictionary<int, c_task_main> taskMainById,
        out Il2CppSystem.Collections.Generic.List<c_task_target> taskTargetRows,
        out Il2CppSystem.Collections.Generic.Dictionary<
            int, Il2CppSystem.Collections.Generic.Dictionary<int, c_task_target>> targetsByTask)
    {
        taskMainRows = null!;
        taskMainById = null!;
        taskTargetRows = null!;
        targetsByTask = null!;
        try
        {
            var config = ConfigData.singleton;
            taskMainRows = config?.task_main!;
            taskTargetRows = config?.task_target!;
            taskMainById = ConfigData.dict_task_main!;
            targetsByTask = TaskTargetManage.DictConfig!;
            return taskMainRows is not null && taskMainById is not null &&
                   taskTargetRows is not null && targetsByTask is not null;
        }
        catch (Exception exception)
        {
            Plugin.DebugLog(
                $"[Tasks] The game's task configuration is not ready yet: {exception.Message}");
            return false;
        }
    }

    private static bool IsStillApplied(
        RegisteredTask registration,
        Il2CppSystem.Collections.Generic.Dictionary<int, c_task_main> taskMainById,
        Il2CppSystem.Collections.Generic.Dictionary<
            int, Il2CppSystem.Collections.Generic.Dictionary<int, c_task_target>> targetsByTask)
    {
        if (!taskMainById.TryGetValue(registration.NativeId, out var mainRow) ||
            mainRow is null || mainRow.L_task_name != LocalizationKey(registration.Key, "name"))
        {
            return false;
        }

        if (!targetsByTask.TryGetValue(registration.NativeId, out var targetDictionary) ||
            targetDictionary is null || targetDictionary.Count != registration.Definition.Objectives.Count)
        {
            return false;
        }

        registration.MainRow = mainRow;
        registration.TargetDictionary = targetDictionary;
        registration.Applied = true;
        return true;
    }

    private static int FindFreeNativeId(
        ModTaskKey key,
        Il2CppSystem.Collections.Generic.Dictionary<int, c_task_main> taskMainById,
        Il2CppSystem.Collections.Generic.Dictionary<
            int, Il2CppSystem.Collections.Generic.Dictionary<int, c_task_target>> targetsByTask)
    {
        var candidate = CreateNativeId(key);
        for (var attempt = 0; attempt < 10_000; attempt++)
        {
            var usedByOtherRegistration = Registered.Values.Any(entry =>
                entry.Key != key && entry.NativeId == candidate);
            if (!usedByOtherRegistration && !taskMainById.ContainsKey(candidate) &&
                !targetsByTask.ContainsKey(candidate))
            {
                return candidate;
            }

            candidate++;
        }

        throw new InvalidOperationException($"Could not allocate a native task ID for '{key}'.");
    }

    private static int CreateNativeId(ModTaskKey key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key.ToString()));
        var hash = BitConverter.ToUInt32(bytes, 0);
        return FirstModTaskId + (int)(hash % ModTaskIdRange);
    }

    private static void AcceptNative(RegisteredTask registration)
    {
        try
        {
            if (HasTaskRecord(registration.NativeId))
            {
                return;
            }

            TaskManage.AcceptTask(registration.NativeId);
            var accepted = TaskManage.GetAcceptTask(registration.NativeId);
            if (accepted is null)
            {
                Plugin.Logger?.LogWarning(
                    $"[Tasks] Game did not accept Mod task '{registration.Key}' " +
                    $"(native task {registration.NativeId}). Check its category and native task limits.");
                return;
            }

            Plugin.Logger?.LogInfo(
                $"[Tasks] Accepted Mod task '{registration.Key}' " +
                $"(native task {registration.NativeId}).");
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError(
                $"[Tasks] Accepting Mod task '{registration.Key}' failed: {exception}");
        }
    }

    private static bool HasTaskRecord(int nativeId)
    {
        try
        {
            var active = TaskManage.GetAllAcceptTaskList();
            if (active is not null)
            {
                for (var index = 0; index < active.Count; index++)
                {
                    if (active[index]?.taskId == nativeId)
                    {
                        return true;
                    }
                }
            }

            var history = TaskManage.GetAllAcceptHistoryTaskList();
            if (history is not null)
            {
                for (var index = 0; index < history.Count; index++)
                {
                    if (history[index]?.taskId == nativeId)
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool TryGetRegistration(
        ModTaskKey key,
        out RegisteredTask registration)
    {
        if (Registered.TryGetValue(key, out registration!) &&
            key.Namespace.Equals(registration.OwnerId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        registration = null!;
        return false;
    }

    private static void EnsureLocalization()
    {
        foreach (var registration in Registered.Values)
        {
            EnsureLocalization(registration);
        }
    }

    private static void EnsureLocalization(RegisteredTask registration)
    {
        AddLanguage(LocalizationKey(registration.Key, "name"), registration.Definition.Name);
        AddLanguage(LocalizationKey(registration.Key, "description"), registration.Definition.Description);
        AddLanguage(LocalizationKey(registration.Key, "accepted"),
            registration.Definition.AcceptedDescription ?? registration.Definition.Description);
        AddLanguage(LocalizationKey(registration.Key, "completed"),
            registration.Definition.CompletedDescription ?? registration.Definition.Description);

        foreach (var objective in registration.Definition.Objectives)
        {
            AddLanguage(LocalizationKey(registration.Key, "objective", objective.Id), objective.Description);
            AddLanguage(LocalizationKey(registration.Key, "objectiveComplete", objective.Id),
                objective.CompletedDescription ?? objective.Description);
        }
    }

    private static void AddLanguage(string key, string value)
    {
        try
        {
            var dictionary = LanguageData.dict_Data;
            if (dictionary is null || dictionary.ContainsKey(key))
            {
                return;
            }

            var entry = new c_languagedata { ID = key, v = value };
            dictionary.Add(key, entry);
            LanguageData.singleton?.Data?.Add(entry);
        }
        catch (Exception exception)
        {
            Plugin.DebugLog(
                $"[Tasks] Adding localization key '{key}' will be retried: {exception.Message}");
        }
    }

    private static void RemoveAppliedRegistration(RegisteredTask registration)
    {
        if (!registration.Applied)
        {
            return;
        }

        try
        {
            var config = ConfigData.singleton;
            if (registration.MainRow is not null)
            {
                config?.task_main?.Remove(registration.MainRow);
            }

            if (registration.TargetDictionary is not null)
            {
                foreach (var row in registration.TargetRows)
                {
                    config?.task_target?.Remove(row);
                }

                TaskTargetManage.DictConfig?.Remove(registration.NativeId);
            }

            if (ConfigData.dict_task_main?.TryGetValue(registration.NativeId, out var mainRow) == true &&
                registration.MainRow is not null && mainRow.L_task_name == registration.MainRow.L_task_name)
            {
                ConfigData.dict_task_main.Remove(registration.NativeId);
            }
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning(
                $"[Tasks] Removing task configuration for '{registration.Key}' failed: {exception.Message}");
        }

        registration.Applied = false;
        registration.MainRow = null;
        registration.TargetDictionary = null;
        registration.TargetRows.Clear();
    }

    private static ModTaskCategoryNative ToNativeCategory(ModTaskCategory category) => category switch
    {
        ModTaskCategory.Prisoner => ModTaskCategoryNative.Prisoner,
        ModTaskCategory.Mainline => ModTaskCategoryNative.Mainline,
        ModTaskCategory.PrisonGuardCaptain => ModTaskCategoryNative.PrisonGuardCaptain,
        ModTaskCategory.PrisonGuardMailRoom => ModTaskCategoryNative.PrisonGuardMailRoom,
        ModTaskCategory.BarberShop => ModTaskCategoryNative.BarberShop,
        ModTaskCategory.DaJiao => ModTaskCategoryNative.DaJiao,
        ModTaskCategory.HeiZhua => ModTaskCategoryNative.HeiZhua,
        ModTaskCategory.JianYa => ModTaskCategoryNative.JianYa,
        ModTaskCategory.Gang => ModTaskCategoryNative.Gang,
        ModTaskCategory.Side => ModTaskCategoryNative.Side,
        ModTaskCategory.Escape => ModTaskCategoryNative.Escape,
        _ => ModTaskCategoryNative.Prisoner,
    };

    private static string LocalizationKey(ModTaskKey key, string value, string? objectiveId = null) =>
        objectiveId is null
            ? $"modtask.{key.Namespace}.{key.Path}.{value}"
            : $"modtask.{key.Namespace}.{key.Path}.{value}.{objectiveId}";

    private static TaskMutationResult Result(
        ModTaskKey key,
        TaskMutationStatus status,
        string message) => new(key, status, message);

    // Keep references to native enum values out of the public API assembly.
    private enum ModTaskCategoryNative
    {
        Prisoner = (int)TaskMainType.Prisoner,
        Mainline = (int)TaskMainType.Mainline,
        PrisonGuardCaptain = (int)TaskMainType.PrisonGuardCaptain,
        PrisonGuardMailRoom = (int)TaskMainType.PrisonGuardMailRoom,
        // Vanilla side quests use the mail-room native type in their task rows;
        // both categories therefore share the game's actual side-quest title object.
        Side = (int)TaskMainType.PrisonGuardMailRoom,
        BarberShop = (int)TaskMainType.BarberShop,
        DaJiao = (int)TaskMainType.DaJiao,
        HeiZhua = (int)TaskMainType.HeiZhua,
        JianYa = (int)TaskMainType.JianYa,
        Gang = (int)TaskMainType.DaJiao,
        // Values 9 and 10 are used by the UI even though the generated enum
        // does not name them. GetTitleByTaskType groups types 3-5 together,
        // maps type 9 to the escape title, and type 10 to the legacy mainline title.
        // Type 0 has a separate title object with the same localized text as the
        // native side-quest category, so Side intentionally uses native type 8.
        // Public categories are logical values, not direct copies of these native IDs.
        Escape = 9,
    }
}
