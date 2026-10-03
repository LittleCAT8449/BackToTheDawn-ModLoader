# Back To The Dawn Mod Loader 安装教程

适用于 Windows x64 的《动物迷城》（Back To The Dawn）。玩家下载预编译 ZIP 即可安装，不需要 Rider、PowerShell 或 .NET SDK。

**安装需要两部分：BepInEx 运行环境 + 本项目前置。当前 Release ZIP 只包含本项目前置，不包含 BepInEx。**

## 1. 找到游戏目录

先关闭游戏。在 Steam 库中右键《动物迷城》，选择“管理 → 浏览本地文件”。

打开的文件夹里应该能看到 `Back To The Dawn.exe`。下文的“游戏目录”都指这个文件夹。

## 2. 安装 BepInEx

如果已经安装兼容的 BepInEx 6 IL2CPP，可以跳到第 3 步。

1. 打开 [BepInEx 6.0.0-pre.2 官方下载页面](https://github.com/BepInEx/BepInEx/releases/tag/v6.0.0-pre.2)。
2. 展开 **Assets**，下载 `BepInEx-Unity.IL2CPP-win-x64-6.0.0-pre.2.zip`。
3. 将 ZIP 内的文件直接解压到游戏目录，让 `BepInEx` 文件夹与 `Back To The Dawn.exe` 位于同一层。

这里需要 **Unity IL2CPP / Windows x64** 版本；BepInEx 5、Mono 或 x86 包不适用于此前置。

当前本地实测环境为 `6.0.0-be.697`。官方说明支持这一代构建升级到 `6.0.0-pre.2`，但本项目前置尚未在全新 `pre.2` 环境中完成游戏内验证。

## 3. 安装本项目前置

1. 打开 [本项目 Release 页面](https://github.com/LittleCAT8449/BackToTheDawn-ModLoader/releases)。
2. 在 **Assets** 中下载名称以 `BackToTheDawn.ModLoader-` 开头的 ZIP。`Source code` 是源码，不是安装包；`.sha256` 是可选的下载校验文件。
3. 将 ZIP 内的文件直接解压到游戏目录，合并 `BepInEx` 文件夹。更新时覆盖前置的四个 DLL。

安装后的关键文件应位于：

```text
游戏目录/
├── Back To The Dawn.exe
└── BepInEx/
    ├── core/
    └── plugins/
        └── BackToTheDawn.Loader/
            ├── BackToTheDawn.Loader.dll
            ├── BackToTheDawn.ModAPI.dll
            ├── BackToTheDawn.PhoneAPI.dll
            └── BackToTheDawn.ShopAPI.dll
```

不要多套一层 ZIP 同名文件夹，也不要把文件放进下载的源码项目目录。

## 4. 启动并确认安装

从 Steam 正常启动游戏。第一次启动时，BepInEx 会生成游戏所需的文件，可能比平时慢。

进入主菜单后，打开游戏目录中的 `BepInEx/LogOutput.log`。看到类似下面这一行，表示前置加载成功：

```text
Back To The Dawn Mod Loader v0.1.0 loaded successfully.
```

版本号会随前置版本变化。前置负责加载模组，安装前置本身不会自动添加电话对话。

## 5. 安装模组

关闭游戏，把模组文件夹放到游戏目录的 `BepInEx/mods/` 中。没有 `mods` 文件夹时自行创建。

### C# 模组

```text
BepInEx/mods/YourMod/
├── mod.json
├── YourMod.dll
└── resource/
    └── 模组图片等资源
```

### JSON 电话模组

```text
BepInEx/mods/YourPhoneMod/
├── Manifest.json
├── dialogues/
│   └── conversation.json
└── resource/
    └── 模组图片等资源
```

`resource` 是否需要取决于模组。一个模组目录使用一种清单：C# 模组用 `mod.json`，JSON 电话模组用 `Manifest.json`。

重新启动游戏后生效。号码和玩法以模组作者的说明为准。

**体验包内示例：** 将安装包中的 `examples/BackToTheDawn.JsonPhoneMod` 整个文件夹复制到 `BepInEx/mods/`，重启游戏后在公用电话键盘拨打 `48329`。

## 常见问题

| 问题 | 检查方法 |
| --- | --- |
| 没有 `LogOutput.log` | 检查 BepInEx 是否解压到游戏 EXE 同级目录，是否选了 IL2CPP win-x64 包，并确认已经启动过游戏。 |
| 有日志，但没有前置加载成功的提示 | 检查四个前置 DLL 的位置，再查看日志中的错误。 |
| 前置成功，模组没有加载 | 检查模组是否位于游戏的 `BepInEx/mods/`，清单是否在模组文件夹第一层，文件名是否误写成 `.json.txt`。 |
| 电话没有自定义对话 | 确认模组已加载、号码正确，并查看日志中是否有号码冲突或 JSON 格式错误。 |
| 图片没有显示 | 检查图片是否位于模组的 `resource/`，文件名是否与对话配置完全一致。 |

需要反馈问题时，请附上 `BepInEx/LogOutput.log` 和模组目录截图。

## 更新前置

关闭游戏，将新版前置 ZIP 解压到同一游戏目录，覆盖 `BackToTheDawn.Loader` 文件夹中的四个 DLL，再重新启动。已有的 `BepInEx/mods/` 模组可以保留。

旧 DLL 备份放到 `plugins` 之外，避免 BepInEx 扫描到重复插件。

## 给模组作者

C# 项目目标框架使用 `net6.0`，引用前置提供的 `BackToTheDawn.ModAPI.dll`；电话功能还需引用 `BackToTheDawn.PhoneAPI.dll`，商店注册功能还需引用 `BackToTheDawn.ShopAPI.dll`。引用设置 `Private=false`，发布自己的模组 DLL、清单和资源即可。

- [电话 API](PHONE_API.md)
- [JSON 电话模组格式](PHONE_JSON.md)
- [商店注册 API](SHOP_API.md)
- [技术与公共 API](TECHNICAL.md)

开发工作区执行 `scripts/Pack-Release.ps1` 可生成前置分享包。默认构建 Release；`-SkipBuild` 可用已有 DLL 重新打包文档。安装包不包含游戏文件、存档、个人配置、日志或生成的 interop 文件。
