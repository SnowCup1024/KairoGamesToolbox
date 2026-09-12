# 模组开发与协议

当前专用模组：哆啦A梦的铜锣烧店物语，v1.0 Beta-2（内部 1.0.2），Steam AppID `2934180`。目录规则和发布规则以根目录 [CONTRIBUTING.md](../CONTRIBUTING.md) 为准。

## 文件边界

自 v1.0.1 起进行的版本号规范调整，是为本版本 v1.0 Beta-2 做准备。本次确认的内外版本映射为最终规范，无意外不再修改；后续变更须有明确理由并获得维护者确认，禁止随版本迭代自行更换规则。

`Games/<英文无空格游戏名>/Source` 保存源码；`Payload` 仅保存已审核的自有 DLL；`definition.json` 保存版本、开发标记、游戏指纹、运行组件地址／SHA256、专用文件清单、四语言功能与日志规则。共有协议放在 `Shared`。商业游戏、生成的 interop、BepInEx core、dotnet 和完整 ZIP 不提交。

启动器内嵌专用载荷。安装时从官方源下载并缓存共享运行组件，组合成临时声明式安装包，复用现有指纹校验与事务安装；临时包安装后删除。缓存损坏时重新下载，下载指纹错误时拒绝安装。新游戏必须显式加入 Core 内嵌资源，并测试资源逻辑名称和指纹。

## 控制协议 2

同一 Windows 用户的命名管道，名字为 `KairoMods.<AppID>.<目录 SHA256 前24位>`。目录采用完整路径、去掉末尾分隔符、转大写后进行 UTF-8 SHA256。这是兼容保留的通信标识，不表示依赖旧 KairoMods 工作区。

请求和响应是 UTF-8、单行 JSON。请求由后台管道线程接收，最多 4096 字节；响应最多 16384 字节。客户端超时 4 秒，服务端连接处理超时 3 秒，排队命令有效期 2 秒。游戏状态命令仅在 Unity `Update` 中执行，过期命令不补执行。

```json
{"Protocol":2,"Action":"status","Features":null}
```

`set` 必须携带全部五项状态，名称与倍率均验证通过才整体替换。`FeatureState` 包含 `Enabled` 和 `Multiplier`，倍率只允许 1、2、5、20。响应包含 Protocol、AppId、Directory、Session、Ready、Features、Error；Session 是本次游戏进程的唯一标识。旧观察版协议 1 不作为可控制的新版模组使用。

`Ready` 表示可接收设置，不代表已加载存档。模组先启动控制服务，读取原生 AppData 的 instance_ 字段，等实际单例非空后再安装钩子，不能通过启动器命令强制初始化游戏静态构造函数。安装失败后报告错误并要求重启，不重复安装部分钩子。无 F8／F9 或其他独立控制热键。

仅在模组控制页可见且检测到目标插件时查询，连接前后均为 15 秒；手动刷新也遵循安装检查。页面离开取消请求，旧响应不得更新新页面。状态只在启动器进程内按游戏目录缓存，重连或游戏 Session 改变时恢复；新启动器首次连接发送默认全关闭设置。

## 五项修改

| 功能 | 观察／扣除入口 | 补回入口 | 余额范围 |
| --- | --- | --- | --- |
| 金钱 | `ui.AppData.SubMoney(long,bool)` | `AddMoney(long)` | 全局 long |
| F 点 | `ui.AppData.SubFPoint(long,bool)` | `AddFPoint(long)` | 全局 long |
| 未来币 | `ui.AppData.SubCoin(long,bool)` | `AddCoinPoint(long)` | 全局 long |
| 训练点 | `data.Character.SubHeartPoint(int)` | `AddHeartPoint(int)` | 对应角色 int |
| 道具 | `data.ItemData.SubStock(int)`、`data.Character.EquipItem(ItemData,int)` | `AddStock(int)` | 对应道具 int |

先运行原方法，读取实际扣除量 `spent = before - after`；开启时补回 `spent × (multiplier + 1)`，达到 `multiplier × spent` 的净增加。实际没有扣除则不补回，正常收入不放大；计算使用 decimal 检查并拒绝超出 long／int 范围。游戏原有购买条件不绕过。

同线程、同资源及同对象的嵌套入口只由最外层结算，避免赠送调用 SubStock 时重复补回；补回触发的 Add 方法不再被重复记录。Harmony finalizer 必须释放嵌套状态并保留原异常。void 与 long 方法使用不同 postfix，void 方法禁止声明 `__result`。仅发生反加时更新 long 返回结果。

本版不读取全局标量快照，不遍历角色或道具集合；只在实际入口执行时读取对应余额。反加尚需游戏内逐项验证，尤其 F 点和未来币的实际扣除场景。不要用编译成功代替真实游戏验证。

## 日志

模组记录最终 `ResourceResult`，包含时间、事件编号、资源 ID、before、after、delta。日志队列最多 1000 条，每帧最多输出 20 条；超限报告丢弃数量。补回引起的嵌套 Add 不重复作为玩家收入展示。

启动器增量读取 `BepInEx/LogOutput.log`，每秒最多读取 64KiB，等待完整 UTF-8 行并识别文件截断，界面最多 500 行。识别到的资源结果按 `definition.json` 的 `LogTemplates` 与功能 `LogNames` 翻译，例如“增加金钱 150”；其他诊断保留原文，不能伪装成成功事件。`Development=true` 时所有日志保留原文。

不通过嵌入外部控制台窗口实现日志界面。插件移除控制台日志监听器、分离 BepInEx 控制台并保存关闭设置，保留磁盘日志。首次旧配置可能在插件加载前短暂显示控制台，游戏窗口不隐藏。

## 构建

```powershell
.\Scripts\UpdateBundledMod.ps1 -TestGameDirectory .\.TestGames
dotnet run --project Mods/ControlTests/ControlTests.csproj -c Release
```

本地 `.TestGames` 需提供 BepInEx core 与匹配的已生成 interop，用于编译及只读签名校验。`BuildObserver.ps1` 只构建，明确传入 `-Deploy` 才会向测试游戏复制 DLL，且必须先关闭游戏；正式安装优先通过启动器执行。`MetadataInspect` 只读取元数据，不执行商业游戏程序集。

共享运行组件来源为 [BepInEx 官方构建站](https://builds.bepinex.dev/projects/bepinex_be)。当前固定 Windows x86 IL2CPP 788 包，SHA256 为 `D5954A5993EC39CD1133603D85BFF93875D30B6411B712CC13DCF03C8E08A4D3`。许可文本来自 BepInEx 对应提交及 .NET runtime v6.0.7，位于 `Runtime/Notices`，随安装复制到 `licenses/KairoMods`。

## v1.0 Beta-2 初始化修复

v1.0.1 的四类型 InitializedAndNoError 联合门槛可能因静态转发门面 S 或可选类型尚未初始化而阻止所有钩子安装，表现为只有启动日志而没有 Mod hooks ready。现在在 Unity Update 中读取原生 AppData.instance_，不调用托管游戏单例 getter，不在 Load 中强制初始化。单例存在后安装一次钩子，等待／就绪／异常均写入日志。此修改仍需实际游戏验证，未宣称解决此前偶发无响应。

UIElementsModule 的预加载 Warning 在此前成功的观察与反加日志中也出现；保留原始诊断，不凭此警告判断游戏钩子已失败或修复成功。

用户已确认五项反加实际效果全部正常，前一轮其余修复也已手动通过。本轮仅调整启动器布局、刷新与版本展示，模组源码和已验证载荷不再变更。
