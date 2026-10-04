# JSON 任务定义

任务 JSON 用来声明任务标题、描述、分类和目标。目标是否完成仍由 C# 模组根据游戏事件或剧情条件判断，再调用 `TaskApi` 推进。

## 放置位置

把 JSON 文件放在模组目录的 `tasks/` 中；加载器会递归扫描该目录下的 `.json` 文件。

```text
MyTaskMod/
  mod.json                 # C# 模组；或者使用下方的 Manifest.json
  MyTaskMod.dll
  tasks/
    repair-radio.json
```

如果模组使用 `mod.json` 并带有 C# 入口，任务会在 `IMod.Initialize` 之前注册，因此 C# 代码可以立刻接取或推进这些任务。如果模组只有 JSON，则在 `Manifest.json` 中设置 `isTaskMod: true`，并至少提供一个有效任务文件。

## 任务 JSON 格式

```json
{
  "type": "task",
  "schemaVersion": 1,
  "key": "repair-radio",
  "name": "修好收音机",
  "description": "找到缺少的零件，修好收音机。",
  "category": "Prisoner",
  "acceptedDescription": "先找齐零件，再检查收音机的线路。",
  "completedDescription": "收音机终于能正常工作了。",
  "objectives": [
    {
      "id": "find-parts",
      "description": "找到修理收音机需要的零件。",
      "completedDescription": "零件已经找齐。"
    },
    {
      "id": "repair",
      "description": "修好收音机。"
    }
  ]
}
```

- `type` 必须是 `task`，`schemaVersion` 当前必须为 `1`。
- `key` 是当前模组命名空间内的本地 ID，不要加 `namespace:` 前缀。可使用英文字母、数字、下划线、连字符和句点。
- `name`、`description` 和至少一个 `objectives` 项为必填；每个目标必须有唯一的 `id` 和 `description`。一个任务最多有 64 个目标。
- `category` 可选，默认为 `Prisoner`。可选值为 `Prisoner`、`Mainline`、`PrisonGuardCaptain`、`PrisonGuardMailRoom`、`BarberShop`、`DaJiao`、`HeiZhua`、`JianYa`、`Gang`、`Side`、`Escape`。
- `acceptedDescription`、`completedDescription` 和目标上的 `completedDescription` 均可省略。

JSON 任务注册后会出现在游戏原生任务日志中。加载器为目标创建可显示的原生目标行，但不会从目标描述中推断条件，也不会自动接取任务。

## 在同一个 C# 模组中使用

在 C# 模组目录里同时放置 `mod.json`、入口 DLL 和任务 JSON。调用原有短 ID API 即可：

```csharp
using BackToTheDawn.ModAPI;

public sealed class ModEntry : IMod
{
    private TaskApi? _tasks;

    public void Initialize(ModContext context)
    {
        _tasks = TaskApi.For(context);

        ModApi.Events.Subscribe<GameplayReadyEvent>(_ =>
            _tasks.Accept("repair-radio"));

        ModApi.Events.Subscribe<RadioPartsCollectedEvent>(_ =>
            _tasks.CompleteObjective("repair-radio", "find-parts"));
    }

    public void Shutdown() { }
}
```

`RadioPartsCollectedEvent` 只是示例中的模组自定义事件。实际模组应替换成自己监听的游戏事件，并保存、释放订阅句柄。可运行示例见 `examples/BackToTheDawn.TaskApiExample`：其中 C# 入口监听背包事件，获得苹果后完成 JSON 任务目标。

## C# 模组使用另一个 JSON 任务模组

JSON-only 任务模组的 `Manifest.json` 示例：

```json
{
  "schemaVersion": 1,
  "namespace": "example.sharedtasks",
  "isTaskMod": true,
  "name": "共享任务定义",
  "version": "1.0.0",
  "dependencies": ["dev.backtothedawn.loader"]
}
```

把任务 JSON 放在该目录的 `tasks/` 下。使用这些任务的 C# 模组应在自己的 `mod.json` 的 `dependencies` 数组中声明 `example.sharedtasks`。加载器会先初始化依赖；随后 C# 模组可以用完整的 `ModTaskKey` 接取任务和完成目标：

```csharp
var tasks = TaskApi.For(context);
var taskKey = new ModTaskKey("example.sharedtasks", "repair-radio");

tasks.Accept(taskKey);
tasks.CompleteObjective(taskKey, "find-parts");
```

跨模组调用只允许访问自己或 `dependencies` 中声明的模组命名空间。卸载任务定义所属的模组时，已经写入存档的原生任务记录会按 Task API 现有规则保留。
