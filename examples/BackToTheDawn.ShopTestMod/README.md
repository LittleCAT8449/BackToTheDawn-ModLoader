# 商店 API 测试模组

这是一个可以直接构建的商店 API 示例。模组注册 `test_market` 新商店，加入苹果和止痛药，然后在进入游戏时自动请求打开原生商店界面。

## 构建

在仓库根目录运行：

```powershell
dotnet build examples/BackToTheDawn.ShopTestMod/BackToTheDawn.ShopTestMod.csproj --configuration Release
```

构建后将 `bin/Release/net6.0` 中的 `mod.json` 和 `BackToTheDawn.ShopTestMod.dll` 复制到：

```text
BepInEx/mods/ShopTestMod
```

目标文件夹中应包含 `mod.json` 和 `BackToTheDawn.ShopTestMod.dll`。`.pdb`、`.deps.json`、Loader、ModAPI 和 ShopAPI DLL 无需复制；它们由前置安装在 `BepInEx/plugins/BackToTheDawn.Loader` 中。

进入存档后会自动打开“Mod 商店测试”。可以购买苹果（25）和止痛药（80）来验证原生购买界面。初始化、注册和打开结果会记录在 `BepInEx/LogOutput.log`。
