# The Tinkerer 简体中文汉化

[Steam 创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3811499593) · [原 Mod](https://steamcommunity.com/sharedfiles/filedetails/?id=3747571123) · [BaseLib](https://steamcommunity.com/sharedfiles/filedetails/?id=3737335127)

The Tinkerer（机械师）的非官方简体中文汉化补丁。原角色由 HantaFanta 制作，BaseLib 由 Alchyr 制作。本项目提供译稿和显示提示补丁，不包含原角色 DLL、美术或玩法资源。

## 使用

在 Steam 创意工坊订阅并启用 **The Tinkerer、BaseLib、本汉化补丁**。游戏语言选择“简体中文”，重新启动游戏即可。游戏会按依赖关系在原 Mod 之后加载汉化。

也可以将 `workshop/content` 中的四个文件放入游戏的 `mods/TheTinkererZhHans` 目录。工坊订阅和手动安装选择一种，避免重复加载。停用汉化并重启即可恢复英文。

## 汉化内容

- 8 个语言表、479 条词条，包括 96 张卡牌的名称和描述。
- 角色介绍、古神对话、机器人关键词、能力、遗物、药水和附魔。
- 动态数量词、抽牌和升级提示、能力动态描述、机器人悬浮提示。

核心术语：The Tinkerer＝机械师，Oil＝燃油，Bot＝机器人，Power＝充能。数值变量、升级高亮、能量图标、条件分支和富文本标签保留。补丁只在简体中文下生效，不修改原角色机制。

## 项目结构

| 目录 | 内容 |
| --- | --- |
| `src` | .NET 9 动态提示补丁源码 |
| `localization/zhs` | 简体中文译稿 |
| `localization/source-eng` | 当前原 Mod 的英文词条参考 |
| `workshop/content` | 已发布的 DLL、PCK、manifest 和 README |
| `workshop` | 工坊说明、原创封面和发布配置示例 |
| `tools` | 构建、封面生成和 Steamworks 上传工具 |
| `verification/validation.json` | v1.0.0 的词条、格式与加载校验结果 |

本机 Steam 账号配置、上传状态、运行日志和第三方游戏程序集不纳入 Git。

## 构建

需要 .NET 9 SDK，以及本机安装的《杀戮尖塔 2》。游戏程序集来自 `data_sts2_windows_x86_64`，不随仓库分发。

```powershell
dotnet build src/TheTinkererZhHans.csproj -c Release "/p:GameDirectory=D:/SteamLibrary/steamapps/common/Slay the Spire 2"
```

编译产物为 `src/bin/Release/net9.0/TheTinkererZhHans.dll`。`tools/build.ps1` 封装了默认路径的构建命令。构建不会自动覆盖已发布文件或上传创意工坊。

当前语言包位于 `workshop/content/TheTinkererZhHans.pck`。修改译稿后，使用 Godot 4.5 的 PCK 格式打包到 `TheTinkererMod/localization/zhs` 和 `TheTinkererZhHans/localization/zhs` 两个资源路径，再校验占位符与游戏加载。

工坊发布流程见 [workshop/README.md](workshop/README.md)。

## 兼容与验证

v1.0.0 针对 The Tinkerer 当前工坊版本（manifest `v1.0`）制作，已在游戏 `v0.111.0`、BaseLib `v3.4.7` 下验证。

- 全部479条词条通过键名、占位符、富文本标签和游戏 `LocValidator` 校验。
- 16个动态文本用例和 Harmony 提示补丁安装、移除检查通过。
- 原包与汉化包实际资源加载、中文语言表合并和补丁初始化通过。
- 工坊订阅下载的四个文件与上传文件 SHA-256 一致；工坊版本启动加载通过。

尚未进行完整战斗测试或逐张卡牌排版检查。原 Mod 更新后，新词条可能暂时回退为英文。原版迅捷机器人的语言表写“抽2张牌”，代码里的悬浮提示写“抽1张牌”；汉化保留原文数值。原版空故事文本和内部动作字段保持原样。
