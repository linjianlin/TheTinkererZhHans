# 创意工坊发布

已发布项目：[The Tinkerer 简体中文汉化 | 机械师](https://steamcommunity.com/sharedfiles/filedetails/?id=3811499593)

- App ID：2868840
- 可见性：公开
- 原 Mod 依赖：3747571123
- BaseLib 依赖：3737335127
- 版本：v1.0.0

`content` 只包含经验证的 DLL、PCK、manifest 和 README 四个文件。`preview.png` 是原创封面；`description.bbcode.txt` 是工坊说明。上传工具分别提交默认 `english` 和简体中文 `schinese` 元数据，两者均显示此中文标题和说明。

已从工坊订阅、下载并逐文件核对 SHA-256，工坊版本的游戏加载也通过验证。

## 发布工具

需要 .NET 9 SDK、Steam 客户端、游戏授权，以及本机游戏目录中的 `Steamworks.NET.dll` 和 `steam_api64.dll`。第三方 DLL 不随仓库分发。

```powershell
dotnet build tools/WorkshopPublisher/WorkshopPublisher.csproj -c Release "/p:SteamSdkDir=D:/SteamLibrary/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64"
```

在项目根目录执行上述命令。工具支持 `--status`、`--inspect <id>`、`--validate <config>`、`--publish <config> --confirm` 和 `--install <config> --confirm`。

## 发布配置

复制 `workshop/publish.example.json` 为 `workshop/publish.json`，填入 `expectedSteamId`。配置文件、发布日志和上传状态只在本地保存，被 `.gitignore` 排除。

首次发布到自己的新项目时，`itemId` 保持 `null`。维护本仓库已发布项目的所有者应填写 `3811499593`；其他账号不能更新这个工坊项目。

```powershell
dotnet tools/WorkshopPublisher/bin/Release/net9.0/WorkshopPublisher.dll --status
dotnet tools/WorkshopPublisher/bin/Release/net9.0/WorkshopPublisher.dll --validate workshop/publish.json
dotnet tools/WorkshopPublisher/bin/Release/net9.0/WorkshopPublisher.dll --publish workshop/publish.json --confirm
```

发布前先修改、构建和验证补丁，将已验证文件放入 `workshop/content` 并更新版本说明。上传工具检查 DLL、PCK 的已测哈希，发布新版本前需同步校验值。

## 状态与恢复

创建项目后，工坊 ID 会立即写入 `tools/WorkshopPublisher/state.json`。后续更新保留该文件及 `publish.json` 中的同一 ID，避免重复创建。

上传超时或 I/O 失败时，远端结果可能未知，应先检查已保存的工坊 ID。工具不会自动重复创建结果不明的项目。Steam 返回需要接受工坊协议时，必须由账号所有者处理，然后继续更新已创建的项目。

当前发布 ID、日志和账号配置均只保存在发布者本机，不纳入公开仓库。
