# Dream Town Island 接入记录

当前启动器与模组：**v1.0 Beta 4**（内部 `1.0.4`），功能修订 `control-3`。

## 当前修订：control-3

- 维护者确认合并点数、建筑和道具正常；反馈 control-2 的正常金钱消费没有反加。最新日志只出现一次金钱正收入，未出现消费结果；因此不能认定所有消费都经过已挂钩的 AddMoney，也不能把 UIElementsModule 警告认定为原因。
- 保留 AddMoney，并补充 `main.AppData.SetMoney(long):void` 的下降结算。`Init`、`NewGame(int,bool)`、`LoadGame(int,Il2CppStructArray<byte>)` 同线程范围内不修改资源，新 save 对象的首次绝对赋值只建立基线。正数／相等赋值不反加，嵌套 AddMoney→SetMoney 只结算外层，返还继续使用 AddMoney。维护者于 2026-09-13 确认该修订及本轮其他修改全部正常；确认来自实际游玩反馈，不将其表述为逐入口调用轨迹证明。
- 恢复探针已使用的 `object[] __args` 参数读取方式；金钱消费日志同时保留请求值用于进一步定位。九个钩子（六个资源入口、三个加载保护），无轮询余额或广域探针。
- 四项统一使用 0／1／20 三档；0x 补回实际消耗，净变化为零，仍需要原本足够的余额／库存；1x 和 20x 为净反加。默认开关全部关闭，点数仍按实际对象分流。
- `development=false` 启用游戏定义的四语言最终结果翻译；零差值返还也记录结果。原始磁盘日志保留，未知诊断和错误不隐藏。
- 验收完成：维护者于 2026-09-13 明确确认所有修改完全正常。已完成标准 Release 构建、冒烟测试、控制协议／倍率测试、188 处本地化检查和运行组件组合包校验。桌面启动未自动执行，实际游戏及界面效果由维护者确认。最终产物位于 `.Build/Publish/v1.0-Beta-4` 和 `.Build/Package`，按本轮授权提交推送，不创建 GitHub Release。

以下为历史记录，验收结论以本节为准。

## 历史修订：control-2

- 菜单调整为四项：金钱反加、点数反加、建筑数量反加、道具反加，各支持 1／2／5／20 倍。
- 餐饮、服务、娱乐、文化的 ID 0—3 统一映射到 `pointReverse`，共用开关与倍率；每次仍只读取和补回触发消费的那个 Point 对象，不修改其他点数余额。未知 ID 跳过。
- 新增 `game.Item.AddHaveNum(int):void` 和 `game.Item.Use(game.Citizen,int itemNum):bool`；前者捕捉负数库存变化，后者捕捉使用前后实际消耗，补回统一调用对应 Item 的 AddHaveNum。两个入口共享同一对象交易键，嵌套只由最外层结算，补回调用不递归。bool 返回结果不修改，未扣除不补回，不钩住初始化用的 SetHaveNum。
- 已验证的金钱、建筑结算保持原有算法。总计五个钩子，无字段采样；保留道具 ID／名称和点数 ID／名称供诊断。
- 功能集合从六项变为四项，旧启动器与旧游戏进程需关闭后更新。新启动器使用四项默认关闭状态，旧四种点数的独立设置不会混入新协议集合。
- 五个完整签名及读取器、上限、void／bool postfix 检查通过；编译无警告／错误，30 项定向检查通过。实际道具效果和嵌套执行仍须游戏内确认。

测试启动器：`.Build/Publish/v1.0-Beta-4-control-2/KairosoftGameToolbox.exe`。退出旧启动器和游戏，在新构建中「更新 Mod」。预期日志为 `Mod hooks ready | revision=control-2 | hooks=5 | features=4`。

本轮集中测试：

1. 道具关闭时消耗正常；开启后使用道具，确认同一道具库存按倍率净增加，其他道具不变。例如 3 份使用 1 份，1x 后应为 4 份，2x 后应为 5 份，5x 后应为 8 份，20x 后应为 23 份（未触及游戏上限）。正常购买／获得不放大。
2. 确认菜单只有一个点数开关和倍率，能控制四种点数，实际消耗之外的其他点数不变化；原四种点数反加实效已确认，无需重新开展观察。
3. 正常保存并重新加载，确认道具状态和使用效果正常。汇报失败项及操作，Agent 读取日志定位；本轮未提交／推送。

## 上一修订：control-1（历史开发记录，验证结论以上文为准）

## 当前功能与真实证据

维护者完成短时采样并明确要求直接推进至集中验收，因此本轮不再增加独立的中长时间观察等待；缺失的实际消费证据集中在本次验收补齐。

| 功能 | 已采集的证据 | 验收时仍须确认 |
| --- | --- | --- |
| 金钱反加 | AddMoney(-10,false)：534→524；AddMoney(-20,false)：524→504 | 开关及四档倍率后的真实净增加、收入不放大 |
| 餐饮点反加 | game.Point 数据 ID 0，名称「餐饮」，余额 10 | 实际扣除入口和反加效果，尚无负数调用日志 |
| 服务点反加 | ID 1，名称「服务」，余额 10 | 同上 |
| 娱乐点反加 | ID 2，名称「娱乐」，余额 10 | 同上 |
| 文化点反加 | ID 3，名称「文化」；AddValue(5)：10→15 | 正常获得路径已见，负数消费与反加待验收 |
| 建筑数量反加 | 市政厅 ID 5000；AddHaveNum(-1)：1→0 | 同一建筑库存反加、其他建筑不变、放置结果正常 |

四种资源是**点数**，不是称号或徽章。中文名称来自本次游戏自身日志；英文 Food／Hospitality／Entertainment／Culture 与 [Kairosoft Wiki 的本游戏周目页](https://kairosoft.wiki.gg/wiki/Endgame_%28Dream_Town_Island%29)一致。[攻略的 Points 与 Titles 章节](https://www.levelwinner.com/dream-town-island-beginners-guide-tips-tricks-strategies/)分别说明了点数消耗与称号奖励；资料核对日期为 2026-09-12。繁体与日文功能按钮为工具箱译文，不冒充已核对的 Steam 游戏内官方措辞。

本次日志中有 134 条 StateTrace、60 条 ProbeFrequency、51 条 ProbeCoverageGap，实际 ResourceTrace 20 条、EventTrace 4 条，ProbeReadError 为 0。主要噪声是首次基线和轮转覆盖提示，不能据此称实际消费入口高频。功能版不再安装广域观察钩子、不运行集合采样；只启用下列三个已核对签名的 void 入口：

- `main.AppData.AddMoney(long,bool)`，读取 GetMoney 与 MONEY_MAX。
- `game.Point.AddValue(int)`，读取 value_／VALUE_MAX，以 data_.id_ 在四种功能间分流；未知 ID 不修改。
- `game.ChipModel.AddHaveNum(int)`，读取 haveNum_／HAVE_NUM_MAX，按实际对象补回库存。

所有功能默认关闭、只有启动器控制、无热键。仅负数 Add 调用且确实扣除时补回 `实际扣除 × (倍率+1)`，实现净增加 1／2／5／20 倍；正数获得不放大。只处理外层同对象交易，补回期间禁止递归反加；不拦截 SetMoney／SetHaveNum 等初始化及绝对赋值入口。超过游戏上限、负库存哨兵或异常状态时不补回，并记录诊断。不会绕过资金不足、零建筑库存、建筑解锁、地形与放置条件。

游戏主线程处理完整设置命令，后台管道只排队；队列有界、命令过期不补执行。连接即可配置，不要求先进入存档；钩子内部等待真实单例。开发日志保留 ResourceResult 的原始资源 ID、对象、实际变化与补回数量，便于核对；四语言功能与日志模板已内嵌于定义。

## 集中验收构建与清单

测试启动器：`.Build/Publish/v1.0-Beta-4-control-1/KairosoftGameToolbox.exe`。关闭旧启动器与游戏，在此构建中「更新 Mod」后正常启动。应看到 `Mod hooks ready | revision=control-1 | hooks=3 | features=6`，并能在模组控制页操作六项开关与倍率。

1. 默认关闭时正常消费；分别开启金钱与四种点数，各完成一次实际消费。四种点数之前没有扣除证据，是本次重点。
2. 依次核对 1x／2x／5x／20x：例如原余额 100、消费 10，在未触及上限时应分别为 110／120／150／300。获得奖励／收入仍按原数量增加。
3. 对库存充足且可放置的建筑开启反加，放置一份后该建筑剩余库存应按倍率净增加，另一建筑库存不变。关闭后恢复消耗；零库存不自动生成第一份。
4. 切换点数开关验证互不串改；关闭游戏观察断线变灰、重新连接恢复本次启动器缓存；退出启动器后重新打开，默认关闭设置由启动器恢复。
5. 普通游玩覆盖一次结算／过夜及保存、退出、重新加载，报告异常和失败功能。保存数值随游戏正常保存，关闭开关不撤销既有变化。

构建前游戏指纹、三个完整签名、读取器与游戏上限、内嵌控制计划和 DLL 哈希均通过。插件编译 0 警告／0 错误。`dotnet run --project Mods/ControlTests/ControlTests.csproj -c Release -- --dreamtown-only` 通过 25 项定向检查，覆盖实际扣除计算、上限、四种点数隔离、设置原子性、四语言定义、载荷、真实管道与过期命令。静态／夹具检查不代替 IL2CPP 实际结算。

本轮仅直接 publish 测试启动器；未进行最终交付的整套标准发布流程，未提交／推送。维护者确认后再进入步骤 8。

## 初版观察阶段记录（observe-1，历史）

## 已有证据（2026-09-12）

| 项目 | 证据 |
| --- | --- |
| 游戏 | Dream Town Island／创造都市岛物语／創造タウンズ島，沿用已有 Steam 目录译名 |
| Steam AppID／buildid | `2488340`／`14607284` |
| app.info | `Kairosoft`、`創造タウンズ島` |
| 架构／后端 | EXE 与 GameAssembly 为 x86；IL2CPP metadata 29 |
| Unity | `2021.3.11f1 (0a5ca18544bf)`，UnityPlayer 文件版本 |
| 游戏指纹 | 两份 SHA256 位于 `definition.json`；本轮构建前重新核验通过 |
| 运行组件 | 固定 SHA256 的 BepInEx IL2CPP Windows x86 be.788 |
| 实际加载证据 | 读取现有 LogOutput.log，发现 interop 生成完成、`LoadProbe ready ... hooks=0`、Chainloader startup complete |
| 尚不能证明 | 上述日志不是当前观察钩子、首页／存档可用或长期稳定性的验证 |

当前使用维护者指定的 Steam 目录。不依赖旧 KairoMods 或哆啦A梦副本，不读取存档文件，不在本文保存个人路径或账户信息。

## 全面静态清单与分类

`ExportModMetadata.ps1` 用 Mono.Cecil 只读导出本游戏 1,654 个类型，包含嵌套类型、方法完整签名、static/instance、返回值、参数、字段和属性。完整结果位于忽略目录 `.Build/Metadata/DreamTownIsland.json`，不提交商业元数据报告。离线过程没有执行游戏程序集。

| 类别 | 候选及观察方式 | 风险／缺口 |
| --- | --- | --- |
| 城镇金钱 | `main.AppData.AddMoney(long,bool)`、`SetMoney(long)`，均 instance void；`GetMoney():long` 读前后值，另采样余额 | 与旧游戏 `SubMoney` 不同；Add 的负数是否代表真实扣款须看实际日志 |
| 点数 | `game.Point.AddValue(int)`、`AddMonthlyValue(int)`，instance void；`value_`／`monthlyValue_`；`data_.id_`／`name_` 标识种类 | PointData.ID 有四个枚举成员，不能据此声称四种已确认可消耗资源；译名和用途待采样 |
| 道具库存 | `game.Item.AddHaveNum(int)`、`SetHaveNum(int)`，void；`Use(Citizen,int):bool`；`haveNum_` | Use 的完成不代表使用成功；记录实际差值，嵌套入口用 parent 区分 |
| 建筑库存与发展 | `game.ChipModel` 的库存、经验、RankUp；采样数量、经验、建造次数、访问价格、销售额和解锁状态 | 不把经验、销售额或解锁状态自动归为可消耗资源 |
| 居民 | AddMoney、AddHobbyExp、AddHappiness、AddTmpHappiness、两种 AddParamValue、AddProduct | 钱／经验／幸福度读前后值；参数与产品入口只记录对象与参数，不声称已读余额；未遍历全部居民、好友和产品集合 |
| 股份及企业 | `game.Owner.StockPrice.AddHaveStockNum(int)` 及 `haveStockNum_`；Owner 合作度、销售与交易计数 | 股份按公司实例标识，实际购买出售和补回语义待证实 |
| 事件与发展 | Fever、Trophy、Event.AddExeNum、建造完成、新居民、任务、城市活动；Map 月／年结算 | Map 两个结算入口为 static，用不绑定 __instance 的独立 prefix；回调完成不代表游戏业务成功 |
| 内部状态与显示 | 计时、帧数、随机、渲染、寻路、音效、通用 UI／序列化 | 清单保留，初版不挂钩、不逐帧记录；存档 blob 和任意深层对象没有解析 |

精确观察计划在 `Source/observation.json`，目前 30 个钩子、5 组集合采样。资源／事件候选英文 ID 仅供诊断，尚未通过 Wiki 或开发者确认业务名称；正式功能阶段再核对来源。

## 探针行为与边界

- 自动等待 `main.AppData.instance_` 原生单例，不调用 GetInstance 强制初始化。Unity Update 负责安装、采样与日志输出；观察无需热键或手动激活。
- 仅调用选定读取器，不调用 setter／Add 补回，不修改参数、返回值或存档。所有 postfix 均不绑定 `__result`，支持 void 与 bool 原方法；安装异常撤销部分钩子，保留原游戏异常，不无限重试。
- 资源日志记录时间、id、parent、对象地址／数据 ID／名称、参数、before／after／delta。事件日志的 `completed=true` 仅表示方法正常返回，不能当作成功扣除或业务成功。
- 每秒采样城镇余额及轮换的一组集合；每组最多 32 个对象，约 8ms 预算（在对象之间检查，并非原生读取耗时的硬上限）。5 组轮转，大集合继续从上次位置扫描；不是所有字段每秒完整采样。
- 全局快照缓存最多 4,096 个字段键；超限明确报告。只保留字符串基线，不长期持有原生游戏对象。快照可能重复反映钩子变化，不是额外交易；同一采样周期的中间变化会漏失。
- 每个方法／采样类别每 10 秒输出最多 8 条明细，同时报告窗口总数、隐藏数和累计数；不会永久删除高频资源入口。队列最多 500 行，每 100ms 最多输出 4 行，过载报告丢弃数量。
- 工作者线程触发的钩子只计数并标记跳过，不从该线程读取游戏对象。纯空状态 IPC 仍不执行游戏操作；连接成功不等于观察已就绪。
- 启动日志区分 `Observation probe loaded` 与 `GameTrace ready | revision=observe-1`。保留开发控制台与原始日志，不翻译成未经确认的功能结果。

## 构建与检查

将实际游戏目录和已校验运行组件目录传给脚本：

```powershell
.\Scripts\ExportModMetadata.ps1 -GameDirectory $gameDirectory -OutputPath .Build/Metadata/DreamTownIsland.json
.\Scripts\BuildGameMod.ps1 -GameFolder DreamTownIsland -GameDirectory $gameDirectory -RuntimeDirectory $runtimeDirectory
dotnet run --project Mods/ControlTests/ControlTests.csproj -c Release -- --observation-only
```

`BuildGameMod.ps1` 构建前核对游戏 SHA256、精确钩子签名、返回值、读取器、集合字段；构建后检查 postfix 和内嵌观察计划，再更新自有 DLL 与清单哈希。同版探针修订也靠载荷指纹识别更新，不以版本字符串代替完整性判断。

本轮检查：30 个签名与 5 组字段通过，插件编译无警告／错误；限频明细、隐藏计数、窗口恢复、过载诊断和内嵌 SHA256 定向检查通过。测试启动器采用直接 publish，没有运行整套标准发布流程或自动游戏测试。

## 初版短时采样流程（已完成）

1. 退出游戏及旧启动器，打开 `.Build/Publish/v1.0-Beta-4-observe-1/KairosoftGameToolbox.exe`。
2. 在本游戏详情页点击「更新 Mod」，再正常启动游戏并进入存档。
3. 应出现 `GameTrace ready | revision=observe-1 | hooks=30`。正常游玩约一分钟，尽量进行一次建造或购买及一次已有资源／道具操作，不必为不存在的功能专门解锁内容。
4. 正常退出游戏，报告「已完成」及主要操作／异常即可。Agent 直接读取指定游戏的 `BepInEx/LogOutput.log`，无需逐行粘贴。若报错或无响应，报告阶段并保留日志，不反复重启覆盖证据。

这轮采样已完成；后续按维护者要求直接推进到本文件开头的集中验收。历史探针源码保留作开发参考，但不编译进当前功能 DLL。
