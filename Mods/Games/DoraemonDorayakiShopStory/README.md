# 哆啦A梦的铜锣烧店物语模组

Steam AppID：`2934180`。发布日期以 `definition.json` 的 `releaseDate` 为准；专用 DLL、游戏代码与元数据的 SHA256 也以该定义为准。

## 功能

金钱、F 点、未来币、训练点和道具各有独立开关，默认关闭。所有功能仅支持 **0／1／50** 倍：0 倍“不消耗”；1／50 倍将实际扣除量转换为相应净增加，正常获得不放大，原游戏条件与上限不变。训练点只影响实际消费的角色，道具只影响对应库存。卸载不会回滚已写入存档的数值。

## 实现边界

| 功能 | 消费入口 | 补回入口 |
| --- | --- | --- |
| 金钱 | `ui.AppData.SubMoney(long,bool)` | `AddMoney(long)` |
| F 点 | `ui.AppData.SubFPoint(long,bool)` | `AddFPoint(long)` |
| 未来币 | `ui.AppData.SubCoin(long,bool)` | `AddCoinPoint(long)` |
| 训练点 | `data.Character.SubHeartPoint(int)` | `AddHeartPoint(int)` |
| 道具 | `data.ItemData.SubStock(int)`、`data.Character.EquipItem(ItemData,int)` | `AddStock(int)` |

同对象嵌套仅结算一次；保留原异常，防止溢出。金钱类使用 long，角色和库存使用 int。不遍历全局快照，只在真实入口读取余额。插件等待 `ui.AppData.instance_`，不强制初始化静态门面。

源码采用 `GamePlugin`、`GameControl`，命名空间为 `KairoMods.DoraemonDorayakiShopStory`。已发布程序集 `KairoMods.Observer`、插件 GUID `snowcup.kairomods.observer` 和安装路径保留兼容。插件关闭 BepInEx 控制台并保留磁盘日志，队列上限 1000 条、每帧最多输出 20 条。

## 构建与验证

使用本游戏匹配的 x86 IL2CPP 运行组件及已生成互操作程序集；不能以其他游戏的程序集代替。脚本从游戏目录读取依赖，核对游戏指纹与发布日期后编译、检查签名并更新自有载荷及哈希：

```powershell
.\Scripts\UpdateDoraemonMod.ps1 -TestGameDirectory <游戏目录>
```

`BuildDoraemonMod.ps1` 仅构建，`TestDoraemonSignatures.ps1` 检查本游戏签名。离线控制测试使用 `Mods/ControlTests/ControlTests.csproj`，不证明 IL2CPP 实际运行成功。游戏内由使用者验证各开关、三档倍率、正常收入、赠送嵌套、角色归属及保存加载。部署通过启动器事务安装，安装与更新直接尝试；若文件被占用，关闭目标游戏后重试。运行中的游戏需重启才能加载新模组。

共享安装、安全、日期管理及发布规范见 [贡献规范](../../../CONTRIBUTING.md)。
