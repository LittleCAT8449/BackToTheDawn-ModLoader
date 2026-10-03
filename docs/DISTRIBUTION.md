# Back To The Dawn Mod Loader 安装与分享

## 给玩家安装

支持 Windows x64 的《动物迷城》Unity IL2CPP 版。普通玩家使用预编译安装包即可，不需要安装 .NET SDK 或编译源码。

1. 退出游戏。
2. 安装 BepInEx 6 Unity IL2CPP Windows x64 运行环境。已经安装兼容运行环境的玩家可以继续下一步。
   - 官方发布页：https://github.com/BepInEx/BepInEx/releases/tag/v6.0.0-pre.2
   - 选择 `BepInEx-Unity.IL2CPP-win-x64-6.0.0-pre.2.zip`，解压到 `Back To The Dawn.exe` 所在目录。
   - 当前本地实测运行环境的日志版本为 `6.0.0-be.697`。官方发布说明将这一代构建列为可以升级到 `6.0.0-pre.2` 的版本；本分享包尚未在全新 `pre.2` 安装环境中做游戏内验证。
3. 将本前置包解压到同一游戏目录，合并 `BepInEx` 文件夹。安装完成后的核心文件位置如下：

   ```text
   游戏目录/
     Back To The Dawn.exe
     BepInEx/
       plugins/
         BackToTheDawn.Loader/
           BackToTheDawn.Loader.dll
           BackToTheDawn.ModAPI.dll
           BackToTheDawn.PhoneAPI.dll
   ```

4. 从 Steam 启动游戏。首次启动 BepInEx 会生成该游戏版本的 interop 文件，启动时间可能较长。
5. 打开 `BepInEx/LogOutput.log`，找到 `Back To The Dawn Mod Loader v0.1.0 loaded successfully.` 即表示前置已加载。
6. 把需要使用的 Mod 文件夹放进 `BepInEx/mods/`，随后重新启动游戏。

Mod 的目录布局：

```text
BepInEx/mods/YourMod/
  mod.json
  YourMod.dll
  resource/
    图片和其他素材
```

当前的 `48327` 测试电话由 `BackToTheDawn.ExampleMod` 注册。分享这个示例时，另行提供该 Mod 的 DLL、`mod.json` 和 `resource` 目录。

JSON 电话模组只需要 `Manifest.json`、对话 JSON 和可选的 `resource` 目录，无需 Mod DLL。分享包内的 `examples/BackToTheDawn.JsonPhoneMod/` 可复制到 `BepInEx/mods/`，启用示例号码 `48329`。格式见 `docs/PHONE_JSON.md`。

## 更新前置

退出游戏后更新 `BepInEx/plugins/BackToTheDawn.Loader/` 中的三个 DLL。旧版本备份应放在 `plugins` 之外，或改成 `.dll.bak` 等不以 `.dll` 结尾的文件名，避免 BepInEx 扫描到重复插件。

## 给 Mod 作者使用

从前置目录引用 `BackToTheDawn.ModAPI.dll`；使用电话功能时还要引用 `BackToTheDawn.PhoneAPI.dll`。二者都由前置提供，在 Mod 项目中设置 `Private=false`：

```xml
<ItemGroup>
  <Reference Include="BackToTheDawn.ModAPI">
    <HintPath>你的前置目录/BackToTheDawn.ModAPI.dll</HintPath>
    <Private>false</Private>
  </Reference>
  <Reference Include="BackToTheDawn.PhoneAPI">
    <HintPath>你的前置目录/BackToTheDawn.PhoneAPI.dll</HintPath>
    <Private>false</Private>
  </Reference>
</ItemGroup>
```

项目目标框架使用 `net6.0`。把自己的 Mod DLL、清单和资源一起提供给玩家。`mod.json` 中声明对前置的依赖：

```json
{
  "id": "yourname.yourmod",
  "name": "Your Mod",
  "version": "1.0.0",
  "entryAssembly": "YourMod.dll",
  "entryType": "YourMod.ModEntry",
  "dependencies": ["dev.backtothedawn.loader"]
}
```

电话 API 用法见包内 `docs/PHONE_API.md`，公共 API 说明见 `docs/TECHNICAL.md`。

## 在开发工作区生成分享包

```powershell
.\scripts\Pack-Release.ps1
```

脚本默认构建 Release 版本，并在工作区 `dist` 下生成带时间戳的 ZIP。包内包含三个前置 DLL、这份安装说明、API 文档和文件校验信息。BepInEx 运行环境由接收者按上面的步骤安装，游戏文件、存档、个人配置、日志和生成的 interop 文件不参与打包。
