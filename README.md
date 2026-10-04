# Back To The Dawn Mod Loader

## 项目概述

本项目是面向《动物迷城》（Back To The Dawn）的 Windows x64 模组加载器，基于 BepInEx 6 IL2CPP。加载器负责发现模组、处理依赖并初始化；模组作者可以用 C# 编写模组，也可以用 JSON 编写电话对话、商店目录和任务定义。

项目通过独立 API 提供常用扩展能力：ModAPI 用于注册物品、操作背包、注册任务和监听游戏事件；PhoneAPI 用于注册电话号码与对话；ShopAPI 用于注册商店、调整商品并打开游戏原生商店界面。

## 文档

- [安装说明](docs/DISTRIBUTION.md)
- [API 参考：ModAPI、PhoneAPI、ShopAPI 与 JSON](docs/API_REFERENCE.md)
- [技术文档](docs/TECHNICAL.md)
