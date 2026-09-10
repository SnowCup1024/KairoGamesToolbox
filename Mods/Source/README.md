# 模组包规范（schemaVersion 1）

启动器安装包不包含本目录。模组独立发布，0.2.2 通过“选择本地 Mod ZIP”导入，GitHub 在线下载暂未实现。

## 源码与产物目录

仓库 `Mods` 只跟踪 `Source`：KairoMods.Observer、ControlTests、MetadataInspect 及此说明。0.0.3 ZIP 已从当前版本删除，不再提交模组二进制包。

独立打包脚本输出到 `.Build/ModPackages/<英文无空格游戏名>/Alpha` 或 `Beta`，Stable 直接位于游戏名目录；这些生成物被 Git 忽略。用户导入缓存仍位于 `%LocalAppData%/KairoGamesToolbox/Mods`，与源码及构建输出分离。

## ZIP

根目录 `manifest.json`，安装文件位于 `payload/`。清单字段：

- `schemaVersion`: 1
- `appId`、`gameFolder`: 必须与当前所选游戏一致。
- `version`: 三段数字版本；`channel`: Alpha、Beta 或 Stable。
- `description`: 给用户显示的功能说明。
- `targets`: GameAssembly.dll 和 KairoGames_Data/il2cpp_data/Metadata/global-metadata.dat 的路径及 SHA-256。
- `files`: 每个 payload 文件的相对路径及 SHA-256。

两个目标指纹必须全部匹配才允许释放。因此不同游戏、更新后的版本、其他修改版即使 EXE 同名也可能被拒绝。指纹用于匹配及完整性检查，不是作者签名；只能安装可信来源的 ZIP。

当前允许释放 BepInEx/core、BepInEx/plugins、dotnet、licenses 下的文件，以及加载器入口 winhttp.dll、doorstop_config.ini、.doorstop_version、changelog.txt。不允许覆盖游戏代码、元数据或存档。拒绝路径越界、链接、重复条目、未声明文件和损坏内容。最大 4096 条目、512 MiB 解压总量。

首次安装的未知冲突拒绝覆盖。安装清单保存在游戏目录 .kairomods-install.json；更新时校验旧文件指纹，替换旧文件并移除清单中已淘汰的文件。先暂存和备份，异常时恢复；恢复失败保留暂存目录并报错。不保证断电后的跨文件事务，不提供卸载。Steam 完整性验证通常不会删除新增模组文件。

## 历史 Alpha 0.0.3（当前仓库已移除包）

AppID 2934180；观察插件 0.0.3，BepInEx 6.0.0-be.788（5b766a3），Unity IL2CPP x86。
进入存档后按 F8，日志出现 MoneyTrace ready 后观察 SubMoney/AddMoney；不提供金钱反加，也未接入工具箱实时控制。
首次运行加载器可能需要联网准备依赖。此包不分发商业游戏文件、互操作程序集或存档。

构建使用 Scripts/BuildModPackage.ps1，输入独立的加载器 ZIP、插件 DLL、用于计算指纹的游戏目录和许可证目录。模组源码已迁入 Mods/Source；构建使用 Scripts/BuildObserver.ps1，需显式传入本地 TestGameDirectory。外部 KairoMods 仍不提交。加载器来源：https://builds.bepinex.dev/projects/bepinex_be 。内附 BepInEx 和 .NET 许可证与 .NET 第三方声明。
