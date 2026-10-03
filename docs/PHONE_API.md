# 电话 API

无需 DLL 的电话模组可使用 `Manifest.json` 和对话 JSON，格式与示例见 [JSON 电话模组](PHONE_JSON.md)。

电话功能由独立的 `BackToTheDawn.PhoneAPI.dll` 提供。Mod 项目需引用该 DLL，并通过 `PhoneApi.For(context)` 获取当前 Mod 的 API 实例。它可注册新对话和 5 位电话号码，也可覆盖游戏已有台词。自定义号码会出现在游戏电话簿中；拨打后优先使用游戏原生对话组件逐句显示台词，当前场景没有可用组件时回退到 Loader 对话面板。

```csharp
using BackToTheDawn.ModAPI;
using BackToTheDawn.PhoneAPI;

public sealed class PhoneMod : IMod
{
    private IDisposable? _lineSubscription;

    public void Initialize(ModContext context)
    {
        var phones = PhoneApi.For(context);
        _lineSubscription = phones.SubscribeLineDisplayed(line =>
        {
            if (!line.IsCustomConversation && line.StringKey is not null)
            {
                context.Logger.Info($"Native phone line key: {line.StringKey}");
            }
        });

        var conversation = phones.RegisterConversation(
            "repair-shop",
            new[]
            {
                new PhoneDialogueLine(
                    "修理店",
                    "喂，这里是修理店。",
                    PhoneDialogueSpeakerType.Caller),
                new PhoneDialogueLine(
                    "你",
                    "我想问一下今天的营业时间。",
                    PhoneDialogueSpeakerType.Player),
                new PhoneDialogueLine(
                    "修理店",
                    "晚上六点关门。",
                    PhoneDialogueSpeakerType.Caller),
            });

        if (conversation.Succeeded)
        {
            var number = phones.RegisterNumber(
                "48327",
                "街角修理店",
                "repair-shop");
            if (!number.Succeeded)
            {
                context.Logger.Warning(number.Message);
            }
        }
    }

    public void Shutdown() => _lineSubscription?.Dispose();
}
```

Conversation key 会自动加上 Mod 清单中的 `id` 前缀，例如 `dev.example.mod:repair-shop`。电话号码必须是五位数字，不能与其他 Mod 或游戏内号码重复。电话簿条目和对话只在本次游戏运行中生效，不会修改存档。

## 原生选项与分支

在某句台词的 `Options` 中填写选项。玩家完成这句台词后，Loader 调用游戏的 `CharacterTalk.ShowTalkOptionsList` 显示原生列表，等待玩家选择，再播放对应分支。等待选择时，继续对话的输入不会跳过菜单。取消原生列表会结束通话，并执行已有的挂断和操作恢复流程。

```csharp
var phones = PhoneApi.For(context);
phones.RegisterConversation("service", new[]
{
    new PhoneDialogueLine("修理店", "需要什么服务？")
    {
        Id = "menu",
        Options = new[]
        {
            new PhoneDialogueOption("repair", "修理收音机", "repair-question"),
            new PhoneDialogueOption("hours", "询问营业时间", "hours-answer"),
            new PhoneDialogueOption("hang-up", "挂断", EndCall: true),
        },
    },
    new PhoneDialogueLine("你", "请问能修理收音机吗？", PhoneDialogueSpeakerType.Player)
    {
        Id = "repair-question",
    },
    new PhoneDialogueLine("修理店", "可以，带来后我会检查零件和费用。")
    {
        EndCall = true,
    },
    new PhoneDialogueLine("修理店", "今天晚上六点关门。")
    {
        Id = "hours-answer",
        NextLineId = "menu", // 返回服务菜单，也可以改成 EndCall = true。
    },
});
phones.RegisterNumber("48328", "修理店服务", "service");
```

字段说明：

| 字段 | 作用 |
| --- | --- |
| 台词 `Id` | 分支目标标识，在当前对话内唯一，区分大小写；不需要跳转到的台词可以省略。 |
| 台词 `NextLineId` | 完成台词后的目标；省略则顺序播放下一句。也可跳回前面的台词。 |
| 台词 `EndCall` | 完成台词后挂断；不能同时设置目标或选项。 |
| 台词 `Options` | 完成这句台词后显示的选项列表。 |
| 选项 `Id` | 用于识别选择，同一句台词内必须唯一。 |
| 选项 `Text` | 原生列表显示的文字。 |
| 选项 `NextLineId` | 选择后的目标；省略则沿用所属台词的 `NextLineId` 或顺序进入下一句。 |
| 选项 `EndCall` | 选择后立即挂断；不能同时设置目标。 |

未设置选项和分支的旧对话继续按顺序播放，到最后一句结束时挂断。注册时会检查重复标识、缺失目标和相互冲突的字段，错误定义会抛出 `ArgumentException`。选择和分支跳转不会重复扣除每日拨打次数。

可订阅选项选择事件，执行 Mod 自己的逻辑；订阅会观察所有 Mod 的电话选项，按 `ConversationKey` 筛选。此回调不替代配置的跳转：

```csharp
// 保存 subscription，并在 Mod.Shutdown 中 Dispose。
var subscription = phones.SubscribeOptionSelected(choice =>
{
    if (choice.ConversationKey == context.Manifest.Id + ":service")
    {
        context.Logger.Info($"选择：{choice.OptionId}，来自台词：{choice.LineId}");
    }
});
```

当前无法取得原生选项组件时，会显示 Loader 的后备选项按钮。`48327` 示例号码提供“修理收音机 / 询问营业时间 / 挂断”三个分支。

## 覆盖游戏台词

先订阅 `PhoneLineDisplayedEvent`，拨打原有电话并记录 `StringKey`，再按完整 key 覆盖文本：

```csharp
var phones = PhoneApi.For(context);
var result = phones.OverrideLine(
    "native_dialogue_key",
    "替换后的台词");
```

文本覆盖针对这个对话 key 的每次显示。它只替换文字，不会改动原对话的选项、剧情动作、条件、关系变化或通话流程。多个 Mod 覆盖同一 key 时，最后注册的文本生效；卸载该 Mod 后会回退到仍注册的覆盖内容。

## 修改通话中的电话图片

`RegisterConversation` 的 `interactionIconPath` 可指定通话时左侧方框中电话图片或联系人图片所用的 PNG。路径相对于 Mod 的 `resource` 目录；留空时使用游戏原图。运行时替换 `TalkPhone` 中正在显示的联系人图片，并在原生图片动画刷新后维持自定义贴图。挂断或 Mod 卸载时恢复原图。该参数沿用已有名称。

替换只作用于当前面板的 `icon` 层或 NPC 肖像层，游戏的背景、阴影和边框继续正常显示。PNG 按原比例在原图标区域居中显示，使用像素采样；挂断时一并恢复原生图片的显示模式。

```csharp
var conversation = phones.RegisterConversation(
    "repair-shop",
    lines,
    interactionIconPath: "icons/repair-shop.png");
```

将图片放在 Mod 目录的 `resource/icons/repair-shop.png`。`debug_token.png` 可用于快速确认替换是否生效；它是方形瓶子图标，不适合作为最终电话图标。

## 当前范围

- 新电话对话支持原生选项、按台词标识跳转和主动挂断；目前没有内置条件表达式、语音或原版剧情动作执行接口。
- 新号码通过游戏电话输入界面拨打，通话优先使用游戏原生对话框。成功开始一通自定义电话时，通过 `StoragePhoneInfo.AddCallTimes()` 计入原生每日拨打次数；重复回调不重复扣次。费用由进入电话界面的原生流程处理，Loader 不额外扣费。
- `PhoneDialogueSpeakerType.Caller` 使用电话样式；`PhoneDialogueSpeakerType.Player` 使用玩家普通对话样式和玩家角色身份。两种原生对话框均直接显示 `Text`，不会自动添加说话者名称前缀。`Speaker` 保留为说话者信息，并供 Loader 后备面板单独显示；旧的双参数 `PhoneDialogueLine(speaker, text)` 保持电话方样式。
- 如果当前场景无法取得游戏对话组件，则显示 Loader 后备面板。
- 原版电话对话仍由游戏执行；`OverrideLine` 只改变指定 `TalkString.stringKey` 的显示文本。
- `PhoneLineDisplayedEvent` 可观察原版电话台词和 Mod 对话，适合发现需要覆盖的原版 key。
