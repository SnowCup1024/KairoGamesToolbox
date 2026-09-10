# 开罗游戏工具箱

面向 Steam 开罗游戏的 Windows x64 桌面启动器，当前版本为 `0.2.0`。

## 当前功能

- 内置 63 款游戏的中英文目录；封面从 Steam CDN 联网获取，不随源码或安装包分发。
- 自动识别 Steam 路径、多游戏库和已安装的开罗游戏。
- 中英文搜索、分区排序、启动游戏、打开安装入口和商店页面。
- 定位并打开游戏存档目录。
- 可选 Steam Web API 查询拥有状态、游玩时长和最近游玩时间。
- 支持跟随系统、浅色与深色主题；API Key 使用 Windows DPAPI 加密保存。

游戏卡片进入“Mod 与存档修改”详情页，支持本地模组 ZIP 校验与释放。模组独立发布，GitHub 在线下载暂未开放。

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
.\Scripts\BuildRelease.ps1 -Version 0.2.0
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

## 0.2.0

- 左键游戏卡片及右键“Mod 与存档修改”进入独立详情页，替代启动弹窗；返回保留游戏库搜索和列表。
- 详情页提供启动游戏、Steam/非 Steam 目标目录、本地 ZIP 导入与释放、存档目录入口；在线下载和存档编辑暂未开放。
- 释放前匹配所选 AppID、英文标识、GameAssembly.dll 与元数据 SHA-256；文件不匹配时不安装。已有不同文件拒绝覆盖。
- 模组保存在实际启动器 EXE 同级 Mods/英文无空格游戏名/Alpha 或 Beta；Stable 在游戏名目录。仓库中的独立 ZIP 不会被打进启动器发布包。
- 首个 Alpha 仅含加载器和金钱观察插件，进入存档后按 F8 启用日志；没有金钱反加和工具箱实时开关。
- KairoMods 是独立本地开发目录，不提交；详细模组协议见 [Mods/README.md](Mods/README.md)。
- 不使用电脑插件，不隐藏窗口启动；本次构建使用 SkipLaunchCheck，界面与真实游戏验证由用户进行。

## 0.1.3 热补丁 1

- 统一非 Steam 确认按钮与取消按钮的背景绘制范围，修正红色按钮的视觉内缩。
- 63 款目录游戏支持无声调全拼、首字母及省略字符的模糊搜索，例如 `meishimeng`、`msm`、`koudai3`；保留中文和英文搜索。
- 目录使用 `name_pinyin` 保存按音节分隔的拼音，明确多音字读音；新增游戏时同步填写。搜索完全离线，无额外运行依赖。
- 本对话不使用 computer use，构建与自动测试后由用户手动验证界面。

## 0.1.3 变更

- 游戏弹窗右上角三点入口改为无边框按钮。
- 非 Steam 风险确认改为“我已知晓并确认”，使用鲜红色；取消保留默认焦点，Steam 提示按钮保持不变。
- 确认风险后选择直接包含 `KairoGames.exe` 的文件夹，验证通过后进入共用补丁提示。当前仅做入口文件检查，具体游戏及版本匹配留待补丁实现；不会启动或修改游戏。
- 已保存的 API Key 再次编辑时清空重填，不显示旧密钥；未保存即离开会丢弃草稿并恢复星号。
- Steam 路径保存前检查 `Steam.exe` 和 `steamapps`，无效路径提示并保留原配置。
- 合并账号识别到 Core 的 `SteamAccountService`，兼容 `MostRecent` 字段大小写。存档定位与游玩记录共用识别结果。
- 初步拆分 `LibraryPage.Dialogs.cs` 承担启动弹窗、补丁警告及文件夹选择；页面扫描、列表及事件入口留在 `LibraryPage.xaml.cs`。

## 0.1.1 变更

- 保留 `LibraryPage.xaml.cs`，本次不进行页面拆分。
- 移除旧项目配置迁移、旧明文 Key 和旧主题字段迁移。
- 移除打包封面，使用 Steam 元数据提供的联网 library JPG。
- 将 Mod 入口改为“应用 Mod 补丁”，显示不可逆风险与 Steam 文件验证指引。
- 发布脚本支持补丁版本号；本地代理指令文件不纳入 Git 跟踪。

## 后续开发

已确定下一个里程碑为“启动器稳定性与可测试性完善”：优先完善安装与账号识别、异常和设置保存反馈，并补充对应测试。

封面缓存后续增加 31 天检查：重新查询资源哈希，相同则保留图片，不同则下载更新；检查失败保留可用缓存。0.1.3 不加入过期检查。

Mod 路线调整为独立运行时模块：工具箱负责包管理，游戏内模块负责功能。0.2.0 先实现匹配和本地安装；后续开发 GitHub 下载、版本管理以及工具箱与游戏的实时通信。静态资源补丁另行设计。

## 许可证

见 [LICENSE](LICENSE)。
