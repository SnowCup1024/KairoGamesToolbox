# 开罗游戏工具箱

Windows x64 桌面启动器，当前版本 **0.2.2**。支持 Steam 开罗游戏与手动添加的非 Steam 游戏；内置 63 款游戏的中英文名称和拼音索引。启动器、模组包和本地游戏文件分别管理，启动器发布 ZIP 仅包含一个 EXE。

## 0.2.2 更新

- “已安装”标题右侧新增“添加非 Steam 游戏”：选择目录，识别成功后保存并加入已安装分区。再次启动可恢复；重复添加同一游戏更新目录，不新增重复卡片。
- 搜索框增加顶部留白，支持中文、英文、无声调全拼、首字母和省略字符搜索。
- 详情页自动填写已保存的非 Steam 目录；未选择可用目录时，仅显示游戏介绍和目录选择卡片。
- 移除“打开 Mods 文件夹”和“存档与功能”卡片；第 03 区改为进入模组修改页面。
- 下载入口改为“下载最新 Mod 并选择”，仍未开放在线下载。本地 ZIP 导入到用户数据目录。
- 检测到模组后，安装按钮显示“更新 Mod”。根据安装清单检查并替换旧文件，移除新版清单不再包含的旧文件。
- 必要模组源码迁入 `Mods/Source`；外部 KairoMods 目录仍只保存本地测试材料，不纳入 Git。

## 游戏库与目录识别

Steam 路径首次启动时自动发现并保存；用户设置优先。保存路径时检查 `Steam.exe` 和 `steamapps`，统一分隔符并还原实际目录大小写。扫描支持多个 Steam 库；账号识别由 `SteamAccountService` 统一处理。

非 Steam 添加要求目录直接包含 `KairoGames.exe` 和 `KairoGames_Data/app.info`，读取公司与游戏名称，不执行 EXE，也不以文件夹名称猜测游戏。支持目录表的英文/中文标题、已验证的五个日文标题（铜锣烧店、便利店、食堂、百货商场2、口袋学院3）及已收录的 `steam_appid.txt`。已识别名称与 AppID 冲突时拒绝添加；未知日文标识且没有可用 AppID 时，需要补充识别表。

这是入库身份识别，不是补丁兼容性保证。安装模组时还必须匹配游戏代码与元数据的 SHA-256。

同一 AppID 同时有 Steam 和非 Steam 副本时合并为一张卡片；游戏库右键启动优先 Steam，详情页优先恢复已保存的非 Steam 目录，可切回 Steam。目录失效或身份发生变化时不会标为已安装；记录保留，可重新添加有效目录。当前每款游戏保存一个非 Steam 目录。

可选 Steam Web API Key 用于查询拥有状态和游玩记录；使用 Windows DPAPI CurrentUser 加密保存。编辑已保存密钥时清空重填，未保存离开恢复星号。非 Steam 副本本身没有独立的 Steam 游玩统计。

## 用户数据

所有运行时数据位于 `%LocalAppData%\KairoGamesToolbox`：

| 路径 | 用途 |
| --- | --- |
| `settings.json` | Steam 路径、主题、加密 API Key |
| `non-steam-games.json` | 非 Steam 游戏 AppID 与本地目录 |
| `Covers/<appid>.jpg` | 封面磁盘缓存 |
| `Mods/<英文无空格游戏名>/Alpha/` | Alpha 模组 ZIP |
| `Mods/<英文无空格游戏名>/Beta/` | Beta 模组 ZIP |
| `Mods/<英文无空格游戏名>/` | Stable 模组 ZIP |

不会自动迁移旧 EXE 同级 Mods 缓存；已有 ZIP 可以重新选择导入。源码仓库中的 `Mods/Source` 是开发源码，与用户缓存不是同一目录。运行时缓存不创建在发布 EXE 旁边。

封面首先读取并验证磁盘缓存，命中后不下载。缺失或损坏时，通过 Steam 商店元数据解析带 hash 的 600×900 library JPG；下载失败后，从用户保存或自动发现的 Steam 根目录 `appcache/librarycache/<appid>/library_600x900.jpg` 复制兜底。不使用固定盘符，不分发封面图片。

封面最多四路下载，复用内存缓存；全部来源失败显示占位图。31 天资源哈希复查属于后续计划，本版尚无过期检查。

## 模组安装与更新

在详情页选择目标目录，再选择工具箱格式的本地 ZIP。导入前校验清单、所有 payload 文件指纹和路径；导入成功按游戏和 Alpha/Beta/Stable 归档。选择 ZIP 不会修改游戏。

确认释放时，先检查游戏已退出，再核对清单 AppID、英文标识、`GameAssembly.dll` 和 `KairoGames_Data/il2cpp_data/Metadata/global-metadata.dat` 的指纹。版本不同、文件损坏或未知冲突时拒绝写入。不覆盖游戏代码、元数据和存档。

安装成功在游戏目录保存 `.kairomods-install.json`。更新只替换清单管理的文件：旧文件必须仍符合旧清单，或已经等于新版文件；被外部修改的文件拒绝覆盖。新版不再包含的旧文件也按旧指纹检查后移除。未列入清单的其他插件、配置和存档保留。

旧版无安装记录时，支持识别已有铜锣烧店 0.0.3 包及已验证的本地 0.0.4 插件指纹。仅有 BepInEx 文件夹不足以授权覆盖未知安装。更新按钮表示检测到安装痕迹，最终是否支持更新仍以完整校验结果为准。

文件先暂存和备份，再写入；普通异常会撤销本次变化。恢复失败时保留 `.kairomods-*` 暂存目录并报告位置。跨文件操作无法保证断电原子性，暂不提供手动回滚或卸载；操作前仍需备份。Steam 验证通常不会删除新增模组文件。指纹用于匹配和完整性，不等于作者签名，请只选择可信来源的包。

完整 ZIP 格式见 [Mods/README.md](Mods/README.md)。目前仓库独立包仍是 0.0.3 观察版；0.0.4 源码支持金钱反加，尚未打包发布。

## 实时控制原理

详情页第 03 区进入修改页面。启动游戏并进入存档后，点击“我已进入存档 · 启用控制”，再操作金钱反加开关。不要在主菜单激活游戏方法。

启动器与模组通过本机命名管道通信：管道名由 AppID 和规范化目录哈希组成，只允许当前用户访问。命令有超时，回复验证身份；游戏端将请求排队，由 Unity Update 执行，避免后台线程直接调用游戏方法。界面收到确认才更新开关，断线显示未知，重新连接读取游戏端状态。

插件观察 `SubMoney` 和 `AddMoney`。反加打开时，原扣款照常执行，然后对实际扣款补回两倍，净效果为增加同额金钱；普通收入不变。原游戏的购买条件和消费统计仍可能执行。新进程默认关闭，退出启动器或离开页面不会撤销开关，关闭也不会回滚已有余额或存档。

0.0.4 已由用户实测：正常扣款、打开后扣款反加、关闭后恢复扣款、开启和关闭时正常收入均符合预期。该结论限于铜锣烧店的已测游戏版本和操作，不代表所有游戏或所有扣款路径。

## 开发与验证

Windows 上安装 `global.json` 指定的 .NET SDK 10.0.400（允许最新补丁）。Core 为 .NET 8，WinUI 启动器为 Windows x64；测试模组为 .NET 6，配合 BepInEx IL2CPP x86。

```powershell
dotnet build Windows/KairosoftGameToolbox.sln --configuration Debug
dotnet run --project Windows/Test/LauncherSmokeTest.csproj
.\RunLauncher.bat
.\Scripts\BuildRelease.ps1 -Version 0.2.2 -SkipLaunchCheck
```

本轮不使用 computer use，不自动或隐藏启动游戏。构建后由用户手动验证界面，因此跳过发布脚本的窗口启动检查。发布输出在 `.Build/Publish/v0.2.2` 和 `.Build/Package`，保留的 v0.2.1 EXE 可用于对照。

模组源码独立构建，不加入启动器解决方案，不自动部署或打包：

```powershell
.\Scripts\BuildObserver.ps1 -TestGameDirectory 'E:\Aproject\KairoMods\.TestGames'
dotnet run --project Mods/Source/ControlTests -c Release
```

仅明确需要部署时添加 `-Deploy`，并先退出游戏。测试目录需要已有 BepInEx core 与生成的 interop DLL；通过 `TestGameDirectory` 参数提供，不提交这些依赖。开发辅助工具 `Mods/Source/MetadataInspect` 同样需要此参数。

| 目录 | 职责 |
| --- | --- |
| `Windows/Core` | 无 WinUI 依赖的扫描、识别、缓存、包安装、通信服务 |
| `Windows/Launcher/Source` | 窗口、卡片、游戏库与详情/控制页面 |
| `Windows/Launcher/Data` | 63 款游戏目录与应用图标 |
| `Windows/Test` | 无 Steam、无网络的合成文件和管道测试 |
| `Mods/Source` | 观察/控制插件、通信测试、元数据检查辅助源码 |
| `Scripts` | 启动器发布、模组构建和独立包制作脚本 |
| `.Build` | 所有构建与测试输出，不提交 |

Git 忽略 AGENTS.md、构建输出、本地 KairoMods、游戏副本、存档备份、下载依赖与 interop。发布包不包含模组源码或二进制。模组包由独立脚本明确制作，不随启动器发布自动更新。

本轮验证：128 项启动器测试、9 项模组计算/通信测试通过；旧版 0.0.3 与本地 0.0.4 无记录安装的识别更新在隔离副本中通过。Release 发布成功；界面与真实游戏行为待手动验收。

手动验收重点：添加非 Steam 后重启仍存在；目录不匹配有提示；未安装游戏详情只显示前两张卡片；选择有效目录后显示模组卡片；选择旧安装对应的新 ZIP 后按钮显示更新且游戏能正常启动；进入修改页面后实时开关仍可用。

## 后续计划

GitHub 模组索引、在线下载和版本选择；更多游戏身份标识；模组功能与游戏版本适配；31 天封面哈希复查；进一步完善异常恢复和启动器可测试性。

## 许可证

见 [LICENSE](LICENSE)。游戏、存档和生成的互操作程序集不随项目分发；独立模组包保留相应加载器与运行库许可证。
