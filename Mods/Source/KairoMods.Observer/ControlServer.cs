using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KairoMods.Observer;

public sealed record ControlRequest(int Protocol, string Action, bool Enabled);
public sealed record ControlReply(int Protocol, uint AppId, string Directory, bool Ready, bool Enabled, string? Error);

public sealed class ControlServer
{
    public static readonly string GameDirectory = Path.GetDirectoryName(Environment.ProcessPath)!;
    private readonly ConcurrentQueue<Pending> queue = new();
    private sealed record Pending(ControlRequest Request, TaskCompletionSource<ControlReply> Completion, DateTime Deadline);

    public void Start() => _ = Task.Run(ListenAsync);

    // 唯一触碰游戏状态的入口，始终由 Unity Update 调用。
    public void Pump(Func<ControlRequest, ControlReply> handle)
    {
        while (queue.TryDequeue(out var pending))
        {
            if (DateTime.UtcNow > pending.Deadline || pending.Completion.Task.IsCompleted) continue;
            try { pending.Completion.TrySetResult(handle(pending.Request)); }
            catch (Exception ex) { pending.Completion.TrySetException(ex); }
        }
    }

    private async Task ListenAsync()
    {
        var normalized = Path.GetFullPath(GameDirectory).TrimEnd('\\','/').ToUpperInvariant();
        var name = "KairoMods.2934180." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..24];
        while (true)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync();
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var data = new List<byte>();
                var one = new byte[1];
                while (data.Count < 4096)
                {
                    if (await pipe.ReadAsync(one.AsMemory(), deadline.Token) == 0) throw new IOException("Disconnected");
                    if (one[0] == 10) break;
                    data.Add(one[0]);
                }
                if (data.Count == 4096) throw new InvalidDataException("Request too long");
                var request = JsonSerializer.Deserialize<ControlRequest>(data.ToArray()) ?? throw new InvalidDataException("Invalid request");
                var completion = new TaskCompletionSource<ControlReply>(TaskCreationOptions.RunContinuationsAsynchronously);
                var expires = DateTime.UtcNow.AddSeconds(2);
                queue.Enqueue(new Pending(request, completion, expires));
                ControlReply reply;
                try { reply = await completion.Task.WaitAsync(deadline.Token); }
                catch { completion.TrySetCanceled(); throw; }
                var response = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(reply) + "\n");
                await pipe.WriteAsync(response.AsMemory(), deadline.Token);
                await pipe.FlushAsync(deadline.Token);
            }
            catch { await Task.Delay(100); }
        }
    }
}
