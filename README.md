# 开罗游戏工具箱

Windows x64 启动器，当前版本 **v1.0 Beta 3**（内部版本 1.0.3）。管理 Steam 与非 Steam 开罗游戏，提供游戏专用 Mod 的安装、更新、删除与实时控制。

界面支持简体中文、繁体中文、英文、日文。内置 63 款游戏目录、Steam 译名资料及拼音搜索索引。语言在「设置 → 用户界面」中切换，即时重建页面；主题继续支持跟随系统、浅色和深色。

## v1.0 Beta 3

- 正式版对外名称增加 `Release` 后缀；测试版显示为 `v1.0 Beta 3`。关于和设置页点击版本号切换为 `v1.0.3`，再次点击恢复，不再附带许可证文字。
- 启动器启用单文件压缩，继续内置自身运行组件；游戏用共享组件仍按需下载。
- 本次未修改专用模组（内部版本保持 1.0.2），无需为启动器升级重新安装 Mod。
- 完成构建与检查后提交并推送，不创建 GitHub Release。

## v1.0 Beta-2 修复记录

五项反加和前一轮修复已由用户手动验证通过；本轮继续调整控件对齐和手动刷新，不发布 GitHub Release。

- 修复钩子等待条件导致全部控制未生效：观察实际 AppData 单例，不再要求所有原生类型标志同时初始化；就绪／等待／失败均有日志。
- 每项文字、开关与滑动条同一行；拖动结束后合并发送，避免来回跳动与禁用。
- 安装状态检查版本与文件指纹，完整的最新 Mod 禁止重复更新。
- 日志支持跨行选择与复制全部；API Key 掩码改为占位显示，点击／键盘进入时清空重填。
- 当前语言最长标签作为统一列宽，开关与倍率条纵向对齐，空间不足时切换单列。
- 手动刷新立即显示连接中，等待正在进行的检查后执行；失败有提示，总等待上限 6 秒，自动检查仍为 15 秒。
- 对外显示 v1.0 Beta-2，内部版本保持 1.0.2。


- 游戏详情分为「游戏目录与模组状态」「模组控制」两个页签。
- 非 Steam 路径旁可移除库记录，不删除游戏文件。
- 安装按钮自动判断「安装 Mod／更新 Mod」，进入详情页时检查本地安装标识。
- 游戏专用 DLL、功能定义与日志规则随启动器内嵌；通用运行组件首次安装时从固定官方地址下载并校验，后续复用缓存。
- 模组控制仅在对应页签可见时检查连接，连接前后均为 15 秒；断线后控件变灰并保留显示状态。
- 开关和倍率按游戏目录在启动器进程内记忆；重新连接后恢复。关闭启动器后清除这份记忆，新启动器首次连接会发送全关闭、1x 的设置。
- BepInEx 日志在页面内以彩色文本显示；资源结果按游戏定义翻译，开发模式保留原文。列表最多保留 500 行。
- 哆啦A梦模组支持金钱、F 点、未来币、训练点、道具五项反加，每项独立开关及 1x／2x／5x／20x 四档倍率，宽屏两列、窄屏一列。

## 使用

1. 启动工具箱。Steam 路径会自动检测，也可在设置中填写并验证。
2. 非 Steam 游戏点击「添加非 Steam 游戏」，选择直接包含 `KairoGames.exe` 的目录。依据 `KairoGames_Data/app.info` 与游戏目录识别身份，不以文件夹名猜测。
3. 进入游戏详情，在「游戏目录与模组状态」点击「安装 Mod」。首次安装需要联网下载共享运行组件；游戏代码及元数据指纹不匹配时拒绝安装。
4. 启动游戏，打开「模组控制」。连接后即可设置开关，无需按热键或确认进入存档。模组内部等待游戏 AppData 单例创建后才安装游戏钩子，设置可以提前选择。
5. 退出游戏后可更新或删除 Mod。安装、更新和删除均保留游戏原文件；更新与删除只处理已识别、指纹匹配的受管理文件。

反加按**实际扣除量**计算。例如实际扣除 10，选择 5x，最终相对于扣款前净增加 50。正常收入不放大；购买资格仍由游戏判断。训练点作用于发生消费的角色，道具作用于对应库存。所有选项默认关闭，模组不提供独立热键。

删除 Mod 恢复原游戏的启动方式，**不会撤销已经写进存档的金钱、道具等变化**。请使用备份存档测试。运行时生成的日志、配置、互操作缓存以及不属于安装清单的文件会保留，避免误删其他内容。

启动器关闭不会强制关闭游戏或撤销正在游戏内生效的设置。退出游戏后模组内存状态消失；新启动器进程不读取上一进程的开关记录。

## 启动器包体研究（Beta 3）

联网下载的是游戏侧共享组件；启动器自身仍自包含 .NET 与 WinUI，保证没有预装这些运行库也能使用单个 EXE。发布输入中，`Microsoft.Windows.SDK.NET.dll` 约 26.34 MB、`Microsoft.ui.xaml.dll` 约 15.09 MB、`System.Private.CoreLib.dll` 约 13.17 MB；自有模组 DLL 仅 32 KB，并不是大包的主要来源。

同一 Windows x64 环境实测（MB 为十进制）：

| 构建 | EXE | ZIP |
| --- | ---: | ---: |
| Beta 2，无单文件压缩 | 161.47 MB | 62.25 MB |
| Beta 3，启用单文件压缩 | 66.61 MB | 60.94 MB |

EXE 减少约 58.7%，ZIP 减少约 2.1%。单文件内部使用压缩后，外层 ZIP 的额外压缩空间有限；这不意味着安装缓存或内存占用同比减少。首次解包会增加解压工作，启动速度仍需手动确认。使用 .NET 官方 `EnableCompressionInSingleFile`，保留全部运行依赖和原有单文件自解压设置，未开启 IL 裁剪。

也试验了 `PublishTrimmed=true`、`TrimMode=partial` 配合压缩，EXE 约 34.42 MB；但有 IL2026 警告涉及反射 JSON 的配置、安装清单和管道通信，因此它只是研究结果，不作为交付包。下一步若要显著降低 ZIP，需要先迁移 JSON 源生成并验证裁剪后的实际发布产物，或另行设计需要安装／下载启动器运行库的轻量分发方式；后者改变单 EXE 即用体验，并且只是把运行组件转移到首次下载，不会消除它们。

参考：[.NET 单文件压缩及启动开销](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)、[.NET 裁剪与兼容性](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trim-self-contained)。本次 183 项启动器检查和本地化检查通过；未自动运行图形界面，不将普通测试通过当作裁剪安全证明。

## 模组与共享组件

旧完整 ZIP 超过 30MB，主要体积来自 BepInEx、.NET 运行时和互操作支持库；专用模组 DLL 远小于它们。共享组件可按**平台、架构、运行时及固定版本**缓存复用，不能假定所有 Unity 游戏均使用同一个包。

本版哆啦A梦适配 Windows x86、Unity 2021.3.35f1、IL2CPP，使用 BepInEx 6.0.0-be.788。固定下载地址与 SHA256 在游戏 `definition.json` 中；只从官方 HTTPS 源下载，失败不安装，已有合法缓存可离线复用。首次生成 IL2CPP 互操作文件仍可能由 BepInEx 获取其所需资料，并耗时较长。

专用模组随启动器更新，不再要求用户选择 ZIP。后续修改专用模组时，需要重新构建 DLL、更新内嵌指纹并发布新版启动器。旧 ZIP 工具只保留为离线开发辅助，生成物不提交 Git。

模组加载时关闭 BepInEx 控制台并保存配置。旧安装首次启动可能在插件加载前短暂出现控制台；后续启动采用关闭配置。游戏窗口始终正常显示。

## 数据与目录

用户数据统一保存在 `%LocalAppData%\KairoGamesToolbox`：

| 路径 | 内容 |
| --- | --- |
| `settings.json` | 语言、主题、Steam 路径、当前 Windows 用户 DPAPI 加密的 API Key |
| `non-steam-games.json` | 手动添加的游戏目录 |
| `Covers` | 按 AppID 缓存的封面 |
| `Mods/Runtime` | 经 SHA256 校验的共享运行组件 ZIP |

Steam API Key 是可选项，仅用于拥有状态、游玩时长及最近游玩时间；缺少它不影响启动。再次编辑已保存 Key 时清空重填，不保存离开会恢复掩码。

| 仓库路径 | 内容 |
| --- | --- |
| `Windows/Core` | 独立于 WinUI 的模型、识别、安装事务、通信、本地化与缓存 |
| `Windows/Launcher/Source` | WinUI 界面、主题、动画和页面生命周期 |
| `Windows/Launcher/Data` | 游戏目录和图标 |
| `Windows/Test` | 无头测试 |
| `Mods/Games/DoraemonDorayakiShopStory/Source` | 游戏专用模组源码 |
| `Mods/Games/DoraemonDorayakiShopStory/Payload` | 允许提交的自有专用模组 DLL |
| `Mods/Games/DoraemonDorayakiShopStory/definition.json` | 游戏指纹、运行组件、四语言功能与日志规则 |
| `Mods/Shared` | 启动器与模组共同使用的协议源码 |
| `Mods/Runtime/Notices` | 共享组件许可声明，无运行组件二进制 |
| `Mods/ControlTests`、`Mods/MetadataInspect` | 协议／倍率测试及只读元数据工具 |
| `.TestGames` | 本地合法游戏测试副本、BepInEx 与 interop；绝不提交 |
| `.Build` | 所有构建、验证、下载及发布输出；绝不提交 |

外部 `KairoMods` 工作区已不再是构建依赖；不需要删除那里的旧包或备份才能使用本项目。

## 构建与验证

需要 Windows、`global.json` 指定的 .NET SDK 10.0.400（允许最新补丁，当前验证 10.0.401）。启动器与 Core 使用 .NET 8，游戏插件使用 .NET 6，以匹配现有 BepInEx。

```powershell
dotnet build Windows/KairosoftGameToolbox.sln --configuration Debug
dotnet run --project Windows/Test/LauncherSmokeTest.csproj
dotnet run --project Mods/ControlTests/ControlTests.csproj -c Release
.\Scripts\TestLocalization.ps1
.\Scripts\TestBundledRuntime.ps1
.\Scripts\BuildRelease.ps1 -Version 1.0.3 -SkipLaunchCheck
```

普通启动器构建使用已提交的专用 DLL，不需要商业游戏。修改模组源码后执行：

```powershell
.\Scripts\UpdateBundledMod.ps1 -TestGameDirectory .\.TestGames
```

该脚本编译插件、检查 11 个实际目标签名、核对游戏指纹、更新自有 DLL 及清单，不启动或部署游戏。游戏版本变化时必须人工检查兼容性，不自动更新目标指纹。

发布输出：`.Build/Publish/v1.0-Beta-3/KairosoftGameToolbox.exe` 与 `.Build/Package/KairosoftGameToolbox-v1.0-Beta-3-win-x64.zip`。ZIP 只有一个 EXE，专用 DLL 内嵌其中，共享运行组件不内嵌。

本轮不使用 computer use，不自动启动游戏。`-SkipLaunchCheck` 表示窗口和游戏内效果由使用者手动验证；自动测试与静态签名检查不能证明 IL2CPP 钩子运行稳定。历史观察版偶发无响应尚无确定根因，本版取消全局数值轮询与热键并限制日志队列，仍需实际过夜、训练、赠送和重连测试。

## 版本与贡献

自 v1.0.1 起进行的版本号规范调整，是为 v1.0 Beta-2 做准备；v1.0 Beta 3 经维护者确认，为正式版对外名称补充 Release 后缀，并将测试版名称改为 Beta 空格序号，内部编号规则不变。此次内外版本映射为最终规范，无意外不再修改；后续变更须有明确理由并获得维护者确认，禁止随版本迭代自行更换规则。

`v0.0.1`—`v0.2.4` 是早期测试阶段。对外：测试版 `v2.3 Beta z`，正式版 `v2.3 Release`；内部：测试版 `2.3.z`，正式版使用最后一次测试的 z 加 1，例如 Beta 4 为 `2.3.4`，随后正式版为 `2.3.5`。z 不允许为 0。Beta 不创建 GitHub Releases，正式版以 GitHub Releases 发布为准。文件名中的空格替换为连字符。

完整规范、技术约束、测试与提交规则见 [CONTRIBUTING.md](CONTRIBUTING.md)，模组协议见 [Mods/README.md](Mods/README.md)。项目采用 [GPL-3.0](LICENSE)；第三方组件保留各自许可。
