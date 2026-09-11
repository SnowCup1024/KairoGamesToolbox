using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KairoMods.Protocol;

namespace KairosoftGameToolbox.Services;

public sealed record ModControlReply(int Protocol, uint AppId, string Directory, bool Ready, bool Enabled, string? Error);

public static class ModControlClient
{
    public static async Task<ModControlResponse> SendFeaturesAsync(uint appId, string directory,
        Dictionary<string, FeatureState>? features = null, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(4));
        using var pipe = new NamedPipeClientStream(".", PipeName(appId, directory), PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(deadline.Token);
        var request = new ModControlRequest(ControlProtocol.Version, features == null ? "status" : "set", features);
        await pipe.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request) + "\n"), deadline.Token);
        await pipe.FlushAsync(deadline.Token);
        var bytes = new List<byte>(); var one = new byte[1];
        while (bytes.Count < ControlProtocol.MaxMessageBytes)
        {
            if (await pipe.ReadAsync(one, deadline.Token) == 0) throw new IOException(L.T("模组连接已断开。"));
            if (one[0] == 10) break;
            bytes.Add(one[0]);
        }
        if (bytes.Count == ControlProtocol.MaxMessageBytes) throw new InvalidDataException(L.T("模组响应过长。"));
        var reply = JsonSerializer.Deserialize<ModControlResponse>(bytes.ToArray()) ?? throw new InvalidDataException(L.T("模组响应无效。"));
        var definition = BundledModService.ForGame(appId) ?? throw new InvalidDataException("Unknown game");
        if (reply.Protocol != ControlProtocol.Version || reply.AppId != appId || string.IsNullOrEmpty(reply.Session)
            || !string.Equals(Path.GetFullPath(reply.Directory).TrimEnd('\\', '/'), Path.GetFullPath(directory).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)
            || reply.Features == null || !reply.Features.Keys.ToHashSet().SetEquals(definition.Features.Select(f => f.Id))
            || reply.Features.Values.Any(f => f == null || !ControlProtocol.ValidMultiplier(f.Multiplier)))
            throw new InvalidDataException(L.T("响应来自不匹配的游戏或协议。"));
        return reply;
    }
    public static string PipeName(uint appId, string directory)
    {
        var normalized = Path.GetFullPath(directory).TrimEnd('\\', '/').ToUpperInvariant();
        return $"KairoMods.{appId}." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..24];
    }

    public static async Task<ModControlReply> SendAsync(uint appId, string directory, string action,
        bool enabled = false, CancellationToken cancellationToken = default)
    {
        if (action is not ("status" or "activate" or "set")) throw new ArgumentException(L.T("未知模组命令。"));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(4));
        using var pipe = new NamedPipeClientStream(".", PipeName(appId, directory), PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(deadline.Token);
        var request = JsonSerializer.Serialize(new { Protocol = 1, Action = action, Enabled = enabled }) + "\n";
        await pipe.WriteAsync(Encoding.UTF8.GetBytes(request), deadline.Token);
        await pipe.FlushAsync(deadline.Token);
        var bytes = new List<byte>();
        var one = new byte[1];
        while (bytes.Count < 4096)
        {
            if (await pipe.ReadAsync(one, deadline.Token) == 0) throw new IOException(L.T("模组连接已断开。"));
            if (one[0] == 10) break;
            bytes.Add(one[0]);
        }
        if (bytes.Count == 4096) throw new InvalidDataException(L.T("模组响应过长。"));
        var reply = JsonSerializer.Deserialize<ModControlReply>(bytes.ToArray()) ?? throw new InvalidDataException(L.T("模组响应无效。"));
        if (reply.Protocol != 1 || reply.AppId != appId || !string.Equals(Path.GetFullPath(reply.Directory).TrimEnd('\\','/'), Path.GetFullPath(directory).TrimEnd('\\','/'), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(L.T("响应来自不匹配的游戏或协议。"));
        return reply;
    }
}
