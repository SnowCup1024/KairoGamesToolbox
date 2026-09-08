# 开罗游戏工具箱

面向 Steam 开罗游戏的 Windows x64 桌面启动器，当前版本为 `0.1.0`。

## 当前功能

- 内置 63 款游戏的中英文目录与本地封面。
- 自动识别 Steam 路径、多游戏库和已安装的开罗游戏。
- 中英文搜索、分区排序、启动游戏、打开安装入口和商店页面。
- 定位并打开游戏存档目录。
- 可选 Steam Web API 查询拥有状态、游玩时长和最近游玩时间。
- 支持跟随系统、浅色与深色主题；API Key 使用 Windows DPAPI 加密保存。

当前 Mod 按钮仅为占位入口，尚未实现补丁功能。

## 开发环境

在 Windows 上安装 `global.json` 指定的 .NET SDK 10.0.400（允许最新补丁版本）。Core 面向 .NET 8，界面使用 WinUI，面向 Windows x64。

在仓库根目录运行：

```powershell
dotnet build Windows/KairosoftGameToolbox.sln --configuration Debug
dotnet run --project Windows/Test/LauncherSmokeTest.csproj
.\RunLauncher.bat
```

发布打包：

```powershell
.\Scripts\BuildRelease.ps1 -Version 0.1.0
```

发布脚本执行构建、冒烟测试、单文件发布、主窗口启动检查及 ZIP 打包。产物位于 `.Build/Package/`；只有无法检查桌面启动时才使用 `-SkipLaunchCheck`。

## 目录

- `Windows/Core/`：独立于 WinUI 的模型与服务。
- `Windows/Launcher/Source/`：窗口、页面、控件与界面服务。
- `Windows/Launcher/Data/`：游戏目录、封面与图标。
- `Windows/Test/`：不依赖 Steam 或网络的合成夹具冒烟测试。
- `Scripts/`：发布脚本。
- `.Build/`：构建产物，不提交。
- `.TestGames/`：可选本地游戏测试副本与存档，不提交。

## 后续开发

已确定下一个里程碑为“启动器稳定性与可测试性完善”：优先完善安装与账号识别、异常和设置保存反馈，并补充对应测试。

后续版本的 Mod 采用直接修改游戏原文件的补丁形式，具体补丁制作、应用与恢复机制待设计。当前阶段不实现该功能；启动器的其他功能想法另行讨论。

## 许可证

见 [LICENSE](LICENSE)。
