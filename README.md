# Back To The Dawn Mod Loader

《动物迷城》（Back To The Dawn）的 Windows x64 模组加载器，基于 BepInEx 6 IL2CPP，为模组提供统一的注册接口和游戏事件监听。

## 主要功能

- **模组加载**：通过清单识别模组，处理依赖和初始化。
- **物品注册**：注册自定义物品，配置名称、描述和图标，并提供背包操作接口。
- **电话注册**：添加电话号码和对话，支持原生对话框、分支选项、自定义图片和自动挂断。
- **事件监听**：监听游戏生命周期、玩家状态、背包变化和交易等事件。

支持 C# 模组；电话模组也可以直接使用 JSON 编写，无需编译 DLL。项目仍在开发中。

## 使用与开发

模组放在游戏目录的 `BepInEx/mods/` 下，运行时需要安装 BepInEx 6 IL2CPP。编译源码需要 .NET 6 SDK 和对应的游戏 interop 程序集。

- [安装与分享说明](docs/DISTRIBUTION.md)
- [电话 API](docs/PHONE_API.md)
- [JSON 电话模组](docs/PHONE_JSON.md)
- [技术与 API 文档](docs/TECHNICAL.md)

源码位于 `src/`，模组示例位于 `examples/`，构建及打包脚本位于 `scripts/`。
