# 模组包规范（schemaVersion 1）

启动器安装包不包含本目录。模组独立发布，0.2.0 通过“选择本地 Mod ZIP”导入，GitHub 在线下载暂未实现。

## 目录

```
Mods/
  DoraemonDorayakiShopStory/
    Alpha/DoraemonDorayakiShopStory-0.0.3.zip
    Beta/
    （Stable 包最终直接放在本层）
```

游戏目录名由目录表英文名保留 ASCII 字母与数字生成，移除空格及标点。运行时缓存位于启动器实际 EXE 同级的 Mods，而非单文件临时解压目录。目录不可写时会显示错误，不回退到其他位置。

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

所有文件先检查再暂存，已有相同文件跳过，不同文件拒绝覆盖。普通安装异常会移除本次新增文件；不保证断电后的跨文件事务，不提供卸载。Steam 完整性验证通常不会删除新增模组文件。

## 首个 Alpha

AppID 2934180；观察插件 0.0.3，BepInEx 6.0.0-be.788（5b766a3），Unity IL2CPP x86。
进入存档后按 F8，日志出现 MoneyTrace ready 后观察 SubMoney/AddMoney；不提供金钱反加，也未接入工具箱实时控制。
首次运行加载器可能需要联网准备依赖。此包不分发商业游戏文件、互操作程序集或存档。

构建使用 Scripts/BuildModPackage.ps1，输入独立的加载器 ZIP、插件 DLL、用于计算指纹的游戏目录和许可证目录。不会编译或提交 KairoMods 开发项目。加载器来源：https://builds.bepinex.dev/projects/bepinex_be 。内附 BepInEx 和 .NET 许可证与 .NET 第三方声明。
