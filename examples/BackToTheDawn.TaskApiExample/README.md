# Task API Story Quest Examples

这个示例模组会注册并接取六条中文任务，分别展示主线、越狱、帮派、理发店、队长和支线分类。每条任务都有背景描述、三阶段目标、目标完成文本、接取文本和结案文本。打开游戏的任务日志即可查看。

它还在 `tasks/json-find-apple.json` 中定义了一条 JSON 任务。C# 入口在 `GameplayReady` 时接取它，并监听 `InventoryChangedEvent`；玩家获得苹果后，C# 会调用 `CompleteObjective` 完成 JSON 中的目标。这展示了同一个 C# 模组如何加载并使用 JSON 任务定义。

任务 ID 沿用此前的分类预览 ID，以便加载器识别同一组示例任务。旧存档中已经接取的记录由游戏保存；如果旧记录没有刷新出新阶段，使用一个新存档查看完整的三阶段版本。

六条叙事示例任务的目标仍由模组代码自行推进；JSON 示例任务则演示通过背包事件推进目标。JSON 本身只声明任务文本和目标，不会自动判断游戏条件。

任务注册和接取结果会写入 `BepInEx/LogOutput.log`。
