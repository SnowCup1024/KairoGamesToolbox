using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KairosoftGameToolbox.Services;

public sealed record ModControlReply(int Protocol, uint AppId, string Directory, bool Ready, bool Enabled, string? Error);

public static class ModControlClient
{
    public static string PipeName(uint appId, string directory)
    {
        var normalized = Path.GetFullPath(directory).TrimEnd('\\', '/').ToUpperInvariant();
        return $"KairoMods.{appId}." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..24];
    }

    public static async Task<ModControlReply> SendAsync(uint appId, string directory, string action,
        bool enabled = false, CancellationToken cancellationToken = default)
    {
        if (action is not ("status" or "activate" or "set")) throw new ArgumentException("未知模组命令。");
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
            if (await pipe.ReadAsync(one, deadline.Token) == 0) throw new IOException("模组连接已断开。");
            if (one[0] == 10) break;
            bytes.Add(one[0]);
        }
        if (bytes.Count == 4096) throw new InvalidDataException("模组响应过长。");
        var reply = JsonSerializer.Deserialize<ModControlReply>(bytes.ToArray()) ?? throw new InvalidDataException("模组响应无效。");
        if (reply.Protocol != 1 || reply.AppId != appId || !string.Equals(Path.GetFullPath(reply.Directory).TrimEnd('\\','/'), Path.GetFullPath(directory).TrimEnd('\\','/'), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("响应来自不匹配的游戏或协议。");
        return reply;
    }
}
