# 创造都市岛物语模组

Steam AppID：`2488340`。发布日期以 `definition.json` 的 `releaseDate` 为准；专用 DLL、游戏代码与元数据的 SHA256 也以该定义为准。

## 功能

金钱、点数、建筑数量和道具各有独立开关，四种点数共用一个开关。默认关闭，仅支持 **0／1／50** 倍：0 倍“不消耗”；1／50 倍按实际消费净反加。资金不足、解锁、放置条件和游戏上限仍然有效，正常获得不放大。卸载不会回滚已保存数值。

## 实现边界

| 功能 | 消费入口 | 归属与保护 |
| --- | --- | --- |
| 金钱 | `main.AppData.AddMoney(long,bool)`、`SetMoney(long)` | `GetMoney` 读取、`AddMoney` 补回，受 `MONEY_MAX` 限制 |
| 点数 | `game.Point.AddValue(int)` | ID 0—3 共用 `pointReverse`，未知 ID 跳过，只修改实际消费对象 |
| 建筑数量 | `game.ChipModel.AddHaveNum(int)` | 只补回该建筑库存，不解锁建筑或绕过放置条件 |
| 道具 | `game.Item.AddHaveNum(int)`、`Use(game.Citizen,int)` | 同对象嵌套仅结算一次，保留原方法返回结果 |

金钱绝对赋值排除 `Init`、`NewGame`、`LoadGame` 范围及新存档对象首次赋值。功能版不启用广域观察和集合轮询。插件在 Unity 主线程等待 `main.AppData.instance_`，保留 BepInEx 控制台；日志队列上限 500 条、每次处理最多输出 8 条。

源码采用 `GamePlugin`、`GameControl`，程序集 `KairoMods.DreamTownIsland` 与插件 GUID 保持稳定。正式签名计划为 `Source/control.json`。

## 构建与验证

维护者于 2026-09-15 确认当前内嵌模组已经过人工实际游戏测试，功能完全正常。该结论对应 `releaseDate=2026-09-14` 的现有载荷；本轮启动器更新未修改模组 DLL。后续修改仍按影响范围重新验证。

使用本游戏匹配的 x86 IL2CPP 依赖和已生成互操作程序集。共享组件经校验后解压到 `.Build`。在仓库根目录执行：

```powershell
.\Scripts\BuildGameMod.ps1 -GameFolder DreamTownIsland -GameDirectory <游戏目录> -RuntimeDirectory <已校验组件目录>
dotnet run --project Mods/ControlTests/ControlTests.csproj -c Release -- --dreamtown-only
```

构建脚本核对游戏指纹、发布日期、完整签名、读取器、上限、控制钩子及内嵌计划，然后更新自有 DLL 和哈希，不部署或启动游戏。后续变更由使用者在游戏内验证三档倍率、四类点数归属、道具嵌套、金钱消费、正常收入及保存加载。部署通过启动器事务安装，安装与更新直接尝试；若文件被占用，关闭目标游戏后重试。运行中的游戏需重启才能加载新模组。

共享安装、安全、日期管理及发布规范见 [贡献规范](../../../CONTRIBUTING.md)。
