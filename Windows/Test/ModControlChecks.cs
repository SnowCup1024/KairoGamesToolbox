using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using KairosoftGameToolbox.Services;
using KairoMods.Protocol;

static class ModControlChecks
{
    public static async Task RunAsync(Action<string, bool> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "KairoControl-" + Guid.NewGuid());
        var pipeName = ModControlClient.PipeName(123, directory);
        check("控制连接按目录区分游戏副本", pipeName != ModControlClient.PipeName(123, directory + "other"));
        check("控制连接忽略路径大小写与结尾分隔符", pipeName == ModControlClient.PipeName(123, directory.ToUpperInvariant() + Path.DirectorySeparatorChar));
        async Task Exchange(string action, string response, Action<ModControlReply>? validate, bool reject = false)
        {
            using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var serving = Task.Run(async () =>
            {
                await server.WaitForConnectionAsync(timeout.Token);
                using var reader = new StreamReader(server, Encoding.UTF8, false, 1024, true);
                var line = await reader.ReadLineAsync(timeout.Token);
                using var request = JsonDocument.Parse(line!);
                if (request.RootElement.GetProperty("Action").GetString() != action) throw new Exception("Wrong command");
                if (action == "set" && !request.RootElement.GetProperty("Enabled").GetBoolean()) throw new Exception("Wrong state");
                await server.WriteAsync(Encoding.UTF8.GetBytes(response + "\n"), timeout.Token);
                await server.FlushAsync(timeout.Token);
            });
            try
            {
                var reply = await ModControlClient.SendAsync(123, directory, action, true, timeout.Token);
                if (reject) check("拒绝不匹配游戏的响应", false);
                else validate!(reply);
            }
            catch (InvalidDataException) when (reject) { check("拒绝不匹配游戏的响应", true); }
            await serving;
        }
        await Exchange("status", JsonSerializer.Serialize(new ModControlReply(1,123,directory,false,false,null)), r => check("未就绪状态不误报开启", !r.Ready && !r.Enabled));
        await Exchange("set", JsonSerializer.Serialize(new ModControlReply(1,123,directory,true,true,null)), r => check("开关命令携带目标状态并返回确认", r.Ready && r.Enabled));
        await Exchange("activate", JsonSerializer.Serialize(new ModControlReply(1,123,directory,false,false,"not ready")), r => check("保留游戏端启用错误", r.Error == "not ready"));
        await Exchange("status", JsonSerializer.Serialize(new ModControlReply(1,456,directory,true,true,null)), null, true);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        try { await ModControlClient.SendAsync(123,directory,"status",cancellationToken:cancel.Token); check("离线控制请求可取消",false); }
        catch (OperationCanceledException) { check("离线控制请求可取消",true); }
        var features = BundledModService.ForGame(2934180)!.Features.ToDictionary(f => f.Id, _ => new FeatureState());
        var valid = new ModControlResponse(2, 2934180, directory, "session", true, features);
        async Task RejectV2(string name, ModControlResponse response)
        {
            using var server = new NamedPipeServerStream(ModControlClient.PipeName(2934180, directory), PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var serving = Task.Run(async () =>
            {
                await server.WaitForConnectionAsync(timeout.Token);
                using var reader = new StreamReader(server, Encoding.UTF8, false, 1024, true);
                await reader.ReadLineAsync(timeout.Token);
                await server.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response) + "\n"), timeout.Token);
                await server.FlushAsync(timeout.Token);
            });
            try { await ModControlClient.SendFeaturesAsync(2934180, directory, cancellationToken: timeout.Token); check(name, false); }
            catch (InvalidDataException) { check(name, true); }
            await serving;
        }
        await RejectV2("协议2拒绝旧协议响应", valid with { Protocol = 1 });
        await RejectV2("协议2拒绝其他游戏目录", valid with { Directory = directory + "other" });
        await RejectV2("协议2拒绝缺少游戏会话", valid with { Session = "" });
        await RejectV2("协议2拒绝缺少功能状态", valid with { Features = new() });
        await RejectV2("协议2拒绝非法倍率", valid with { Features = new(features) { ["moneyReverse"] = new(true, 999) } });
    }
}
