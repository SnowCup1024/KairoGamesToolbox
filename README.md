# 开罗游戏工具箱

面向 Steam 开罗游戏的 Windows x64 桌面启动器，当前版本为 `0.1.2`。

## 当前功能

- 内置 63 款游戏的中英文目录；封面从 Steam CDN 联网获取，不随源码或安装包分发。
- 自动识别 Steam 路径、多游戏库和已安装的开罗游戏。
- 中英文搜索、分区排序、启动游戏、打开安装入口和商店页面。
- 定位并打开游戏存档目录。
- 可选 Steam Web API 查询拥有状态、游玩时长和最近游玩时间。
- 支持跟随系统、浅色与深色主题；API Key 使用 Windows DPAPI 加密保存。

“应用 Mod 补丁”入口会显示二级风险提示；当前版本没有可应用的补丁，不会修改游戏文件。

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
.\Scripts\BuildRelease.ps1 -Version 0.1.2
```

发布脚本执行构建、冒烟测试、单文件发布、主窗口启动检查及 ZIP 打包。产物位于 `.Build/Package/`；只有无法检查桌面启动时才使用 `-SkipLaunchCheck`。

## 目录

- `Windows/Core/`：独立于 WinUI 的模型与服务。
- `Windows/Launcher/Source/`：窗口、页面、控件与界面服务。
- `Windows/Launcher/Data/`：游戏目录与图标。
- `Windows/Test/`：不依赖 Steam 或网络的合成夹具冒烟测试。
- `Scripts/`：发布脚本。
- `.Build/`：构建产物，不提交。
- `.TestGames/`：可选本地游戏测试副本与存档，不提交。

## 联网封面

启动器通过 Steam 的 `IStoreBrowseService/GetItems` 获取 `asset_url_format` 和 `library_capsule_2x`，保留资源独立的 hash 路径，再从 Steam CDN 下载 JPG。普通 `library_capsule` 可能仅为 300×450；CDN 请求仍使用 600×900 资源；Steam 本地缓存兜底也接受 300×450 的 library 封面。

配置保存在 `%LocalAppData%\KairoGamesToolbox\settings.json`，封面保存在 `%LocalAppData%\KairoGamesToolbox\Covers\<appid>.jpg`。首次启动在创建主窗口前自动识别 Steam 并保存配置；已保存的用户路径优先。路径统一分隔符，并还原磁盘目录的实际大小写。不读取或迁移旧应用目录。

每次启动优先读取并验证 Covers 磁盘缓存，命中后不联网。缺失或损坏时才查询 Steam CDN，下载成功后原子写入缓存。下载失败（包括无资源、超时或图片损坏）后，尝试从 Steam 根目录的 `appcache\librarycache\<appid>\library_600x900.jpg` 读取并复制到 Covers；只使用用户设置或注册表自动识别的 Steam 路径，不使用固定盘符。不会修改 Steam 源图片。

封面查询不需要 Steam Web API Key，最多同时下载四款游戏的资源。磁盘写入失败不影响当前显示；所有来源均失败时显示占位图，30 秒后再次刷新可重试。切页复用内存缓存，重新打开应用复用磁盘缓存。

## 0.1.2 变更

- 所有游戏弹窗（包括未安装及未拥有的游戏）左上角提供三点按钮，悬浮提示“将 Mod 补丁应用于非 Steam 下载版本”。
- 点击后先确认备份警告，选择“我知道了”再选择游戏 EXE，随后进入共用的补丁提示流程；取消会结束操作。
- 所选 EXE 仅作为本次补丁目标，不会启动文件或覆盖 Steam 安装路径。当前尚未提供实际补丁，也不验证所选 EXE 是否对应当前游戏。
- Steam 提示改为“工具箱无法实现文件回滚，如需恢复请到 Steam 运行‘验证游戏文件的完整性’”；非 Steam 目标提示从自行备份恢复。

## 0.1.1 变更

- 保留 `LibraryPage.xaml.cs`，本次不进行页面拆分。
- 移除旧项目配置迁移、旧明文 Key 和旧主题字段迁移。
- 移除打包封面，使用 Steam 元数据提供的联网 library JPG。
- 将 Mod 入口改为“应用 Mod 补丁”，显示不可逆风险与 Steam 文件验证指引。
- 发布脚本支持补丁版本号；本地代理指令文件不纳入 Git 跟踪。

## 后续开发

已确定下一个里程碑为“启动器稳定性与可测试性完善”：优先完善安装与账号识别、异常和设置保存反馈，并补充对应测试。

封面缓存后续增加 31 天检查：重新查询资源哈希，相同则保留图片，不同则下载更新；检查失败保留可用缓存。0.1.2 不加入过期检查。

后续版本的 Mod 采用直接修改游戏原文件的补丁形式，具体补丁制作、应用与恢复机制待设计。当前阶段不实现该功能；启动器的其他功能想法另行讨论。

## 许可证

见 [LICENSE](LICENSE)。
