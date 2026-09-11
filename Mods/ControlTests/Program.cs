using KairoMods.Observer;
using KairoMods.Protocol;
using KairosoftGameToolbox.Services;

int failures = 0;
void Check(string text, bool success) { Console.WriteLine($"{(success ? "PASS" : "FAIL")} {text}"); if (!success) failures++; }
foreach (int multiplier in new[] { 1, 2, 5, 20 })
{
    long refund = MoneyReversal.Refund(1000, 990, multiplier);
    Check($"{multiplier}x 实扣10净增{10 * multiplier}", 990 + refund == 1000 + 10 * multiplier);
    Check($"{multiplier}x 正常收入不放大", MoneyReversal.Refund(100, 110, multiplier) == 0);
    Check($"{multiplier}x 未扣款不补回", MoneyReversal.Refund(100, 100, multiplier) == 0);
}
Check("拒绝非法倍率", MoneyReversal.Refund(100, 90, 3) == 0);
Check("long 上限拒绝溢出", MoneyReversal.Refund(long.MaxValue, long.MaxValue - 1) == 0);
Check("训练点和道具 int 上限拒绝溢出", MoneyReversal.Refund(int.MaxValue, int.MaxValue - 1, 20, int.MaxValue) == 0);
Check("long 极端差值拒绝溢出", MoneyReversal.Refund(long.MaxValue, long.MinValue) == 0);

var server = new ControlServer(); server.Start();
var states = BundledModService.ForGame(2934180)!.Features.ToDictionary(f => f.Id, _ => new FeatureState());
int mutations = 0;
ModControlResponse Handle(ModControlRequest request)
{
    if (request.Action == "set") { states = new(request.Features!); mutations++; }
    return new(2, 2934180, ControlServer.GameDirectory, "test-session", true, new(states));
}
async Task<ModControlResponse> Pump(Task<ModControlResponse> task)
{
    while (!task.IsCompleted) { server.Pump(Handle); await Task.Delay(10); }
    return await task;
}
var initial = await Pump(ModControlClient.SendFeaturesAsync(2934180, ControlServer.GameDirectory));
Check("协议2连接后即可控制且五项默认关闭", initial.Ready && initial.Features.Count == 5 && initial.Features.Values.All(f => !f.Enabled && f.Multiplier == 1));
var desired = new Dictionary<string, FeatureState>(states) { ["trainingReverse"] = new(true, 20), ["itemReverse"] = new(true, 5) };
var reply = await Pump(ModControlClient.SendFeaturesAsync(2934180, ControlServer.GameDirectory, desired));
Check("一次完整命令独立设置训练点与道具倍率", reply.Features["trainingReverse"] == new FeatureState(true, 20) && reply.Features["itemReverse"] == new FeatureState(true, 5) && !reply.Features["moneyReverse"].Enabled);
int previousMutations = mutations;
var expired = ModControlClient.SendFeaturesAsync(2934180, ControlServer.GameDirectory, states);
try { await expired; } catch (IOException) { } catch (OperationCanceledException) { }
await Task.Delay(100); server.Pump(Handle);
Check("过期命令不会在下一帧延迟修改", mutations == previousMutations);
if (args.Length == 2 && args[0] == "--verify-runtime")
{
    string runtime = Path.GetFullPath(args[1]);
    string package = Path.Combine(Path.GetDirectoryName(runtime)!, "bundle-test-" + Guid.NewGuid().ToString("N") + ".zip");
    try
    {
        var definition = BundledModService.ForGame(2934180)!;
        BundledModService.Compose(package, runtime, definition);
        var manifest = ModPackageService.Read(package, 2934180, definition.GameFolder);
        Check("实际运行组件与内嵌模组组合后所有文件通过安装器校验", manifest.Files.Count > 200 && manifest.Files.Any(f => f.Path == "winhttp.dll") && manifest.Files.Any(f => f.Path.EndsWith("KairoMods.Observer.dll")));
        Check("组合包包含 BepInEx 与 .NET 许可", manifest.Files.Count(f => f.Path.StartsWith("licenses/KairoMods/")) == 3);
        Check("组合包不包含商业游戏文件或互操作程序集", manifest.Files.All(f => !f.Path.Contains("interop/") && !f.Path.EndsWith("GameAssembly.dll") && !f.Path.EndsWith("global-metadata.dat")));
    }
    finally { if (File.Exists(package)) File.Delete(package); }
}
return failures;
