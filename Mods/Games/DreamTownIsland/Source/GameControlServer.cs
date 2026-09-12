using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KairoMods.Protocol;

namespace KairoMods.DreamTownIsland;

internal sealed class GameControlServer : IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentQueue<Pending> queue = new();
    private readonly string directory;
    private Task? listener;
    private sealed record Pending(ModControlRequest Request, TaskCompletionSource<ModControlResponse> Completion, long Expires);
    public GameControlServer(string directory) => this.directory = Path.GetFullPath(directory);

    public void Start(Action<Exception> report) => listener = Task.Run(async () =>
    {
        try { await Listen(lifetime.Token); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { report(ex); }
    });

    public void Pump(Func<ModControlRequest, ModControlResponse> handle)
    {
        for (int i = 0; i < 8 && queue.TryDequeue(out var pending); i++)
        {
            if (pending.Completion.Task.IsCompleted || Environment.TickCount64 >= pending.Expires)
            {
                pending.Completion.TrySetCanceled();
                continue;
            }
            try { pending.Completion.TrySetResult(handle(pending.Request)); }
            catch (Exception ex) { pending.Completion.TrySetException(ex); }
        }
    }

    private async Task Listen(CancellationToken token)
    {
        var normalized = directory.TrimEnd('\\', '/').ToUpperInvariant();
        var name = "KairoMods.2488340." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..24];
        while (!token.IsCancellationRequested)
        {
            using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.WaitForConnectionAsync(token);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(3));
            TaskCompletionSource<ModControlResponse>? completion = null;
            try
            {
                var data = new List<byte>();
                var one = new byte[1];
                while (true)
                {
                    if (await pipe.ReadAsync(one.AsMemory(), deadline.Token) == 0) throw new IOException("Disconnected");
                    if (one[0] == 10) break;
                    if (data.Count == 4096) throw new InvalidDataException("Request too long");
                    data.Add(one[0]);
                }
                var request = JsonSerializer.Deserialize<ModControlRequest>(data.ToArray()) ?? throw new InvalidDataException("Invalid request");
                // Only one pipe client, plus a hard bound if Unity stops pumping.
                if (queue.Count >= 8) throw new IOException("Game command queue full");
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                queue.Enqueue(new(request, completion, Environment.TickCount64 + 2000));
                var reply = await completion.Task.WaitAsync(deadline.Token);
                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(reply) + "\n");
                await pipe.WriteAsync(bytes.AsMemory(), deadline.Token);
                await pipe.FlushAsync(deadline.Token);
            }
            catch (Exception ex) when (ex is IOException or JsonException or OperationCanceledException) { }
            finally { completion?.TrySetCanceled(); }
        }
    }

    public void Dispose()
    {
        lifetime.Cancel();
        while (queue.TryDequeue(out var pending)) pending.Completion.TrySetCanceled();
        if (listener == null) lifetime.Dispose();
        else _ = listener.ContinueWith(_ => lifetime.Dispose(), TaskScheduler.Default);
    }
}
