# JSON 电话模组

安装前置后，可以用 JSON 注册电话，无需编写或编译 DLL。JSON 模组复用 PhoneAPI 的电话运行时，支持电话簿、原生电话与玩家对话框、原生选项、分支跳转、图片替换和挂断。

## 目录

将一个模组文件夹放在 `BepInEx/mods/` 的直接子目录：

```text
BepInEx/mods/MyPhoneMod/
  Manifest.json
  dialogues/
    shop/
      repair.json
  resource/
    icons/
      shop.png
```

加载器发现根目录的 `Manifest.json` 后，递归读取这个模组目录及所有子目录中的 JSON。只有 `type` 为 `phoneConversation` 的对象会作为对话加载；`Manifest.json`、`mod.json` 和其他类型的 JSON 不注册为对话。扫描不会跟随目录链接，也不会进入含有独立模组清单的子目录；独立模组应分别放在 `BepInEx/mods/` 下。

一个模组可以有多个对话 JSON，每个文件注册一个号码。修改文件后重新启动游戏生效。

## Manifest.json

```json
{
  "schemaVersion": 1,
  "namespace": "yourname.phonepack",
  "name": "我的电话模组",
  "version": "1.0.0",
  "isPhoneMod": true,
  "dependencies": ["dev.backtothedawn.loader"]
}
```

| 字段 | 说明 |
| --- | --- |
| `namespace` | 必填，模组的唯一身份及电话对话命名空间。以英文字母或数字开头，只使用英文字母、数字、点、下划线和短横线。与其他 JSON 或 DLL 模组的 ID 不能重复。 |
| `isPhoneMod` | 必填布尔值，`true` 启用电话 JSON 扫描；`false` 跳过这个清单。 |
| `name` | 模组名称，省略时使用命名空间。 |
| `version` | 模组版本，省略时使用 `1.0.0`。 |
| `dependencies` | 依赖的 Mod ID 数组，省略时为空。复用已有的依赖排序和检查。 |
| `schemaVersion` | 格式版本，当前为 `1`，省略也使用 `1`。 |

DLL 模组继续使用已有的 `mod.json`。一个目录只能选择其中一种清单。

## 对话 JSON

```json
{
  "type": "phoneConversation",
  "key": "shop",
  "number": "48328",
  "displayName": "便利店",
  "lines": [
    {
      "id": "menu",
      "speakerType": "Caller",
      "text": "需要什么服务？",
      "options": [
        { "id": "hours", "text": "询问营业时间", "nextLineId": "hours" },
        { "id": "hang-up", "text": "挂断", "endCall": true }
      ]
    },
    {
      "id": "hours",
      "speakerType": "Caller",
      "text": "晚上六点关门。",
      "endCall": true
    }
  ]
}
```

### 文件字段

| 字段 | 说明 |
| --- | --- |
| `type` | 必须为 `phoneConversation` 才会被识别为对话。 |
| `key` | 必填对话标识。例如 `shop` 会变成 `yourname.phonepack:shop`。也可填写完整的当前模组命名空间 key。 |
| `number` | 必填五位数字字符串，例如 `"48328"`；不能写成数值。不得与游戏或其他模组号码重复。 |
| `displayName` | 必填，电话簿及后备面板显示的联系人名称。 |
| `lines` | 必填且至少包含一句台词。 |
| `addToPhoneBook` | 默认 `true`。设为 `false` 时仍可通过键盘拨号。 |
| `interactionIconPath` | 可选，左侧电话图片的路径，相对于当前模组的 `resource` 目录，如 `icons/shop.png`。 |
| `schemaVersion` | 当前为 `1`，省略也使用 `1`。 |

### 台词与选项

- 台词 `text` 必填，原生对话框只显示这段文字。
- `speakerType` 为 `Caller` 或 `Player`，省略时使用 `Caller`。
- `speaker` 可选，是说话者信息；省略时电话方使用联系人名称，玩家使用“你”。它不会加到原生台词前面。
- 台词 `id` 是分支目标，在同一个文件中必须唯一，区分大小写。
- `nextLineId` 跳到指定台词；省略时进入下一句，到末尾自动挂断。可以跳回前面的台词。
- `endCall: true` 表示完成台词后挂断，不能同时填写 `nextLineId` 或非空 `options`。
- `options` 在当前台词完成后显示原生选项列表，省略时没有选项。
- 每个选项需要同一句台词内唯一的 `id` 和非空 `text`。`nextLineId` 指定选择后的目标；省略时沿用所属台词的跳转或顺序下一句。`endCall: true` 会直接挂断，不能同时填写目标。

取消原生选项列表会挂断。成功开始一通电话才计入一次每日拨打次数；选择和返回菜单不会重复扣次。

## 示例与错误日志

工作区的 `examples/BackToTheDawn.JsonPhoneMod/` 是完整示例。复制到 `BepInEx/mods/` 后，启动游戏拨打 `48329`；该示例提供“修理收音机 / 询问营业时间 / 挂断”，询问营业时间后返回菜单。

清单、对话标识、号码或分支定义有误时，`BepInEx/LogOutput.log` 会记录文件或模组及原因。扫描到的 JSON 必须语法正确。当前模组全部对话先解析校验，再开始注册；注册时遇到号码冲突等错误会撤销这个模组本轮注册的电话，其他模组继续加载。

JSON 模式描述对话和跳转；需要选择回调中执行 C# 逻辑时，使用 DLL 模组和 `SubscribeOptionSelected`。C# 用法见 `PHONE_API.md`。
