using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KairoMods.Protocol;

namespace KairoMods.DreamTownIsland;

// A bootstrap probe: never dispatch gameplay actions on this background thread.
internal sealed class LoadProbeServer : IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly string directory;
    private readonly string session = Guid.NewGuid().ToString("N");
    private Task? listener;

    public LoadProbeServer(string directory) => this.directory = Path.GetFullPath(directory);

    public void Start(Action<Exception> report)
    {
        listener = Task.Run(async () =>
        {
            try { await ListenAsync(lifetime.Token); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception ex) { report(ex); }
        });
    }

    internal static string? Validate(ModControlRequest request) => request.Protocol != ControlProtocol.Version
        ? "Unsupported protocol"
        : request.Action == "status" ? null
        : request.Action == "set" && request.Features is { Count: 0 } ? null
        : "Load-only probe has no gameplay features";

    private async Task ListenAsync(CancellationToken token)
    {
        var normalized = directory.TrimEnd('\\', '/').ToUpperInvariant();
        var pipeName = "KairoMods.2488340." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..24];
        while (!token.IsCancellationRequested)
        {
            using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.WaitForConnectionAsync(token);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                var bytes = new List<byte>();
                var one = new byte[1];
                while (true)
                {
                    if (await pipe.ReadAsync(one.AsMemory(), deadline.Token) == 0) throw new IOException("Disconnected");
                    if (one[0] == 10) break;
                    if (bytes.Count == 4096) throw new InvalidDataException("Request too long");
                    bytes.Add(one[0]);
                }
                var request = JsonSerializer.Deserialize<ModControlRequest>(bytes.ToArray()) ?? throw new InvalidDataException("Invalid request");
                var error = Validate(request);
                var reply = new ModControlResponse(ControlProtocol.Version, 2488340, directory, session, true, new(), error);
                var response = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(reply) + "\n");
                await pipe.WriteAsync(response.AsMemory(), deadline.Token);
                await pipe.FlushAsync(deadline.Token);
            }
            catch (Exception ex) when (ex is IOException or JsonException or OperationCanceledException)
            {
                // Bad/expired requests have no side effects; accept the next client.
            }
        }
    }

    public void Dispose()
    {
        lifetime.Cancel();
        if (listener != null) _ = listener.ContinueWith(_ => lifetime.Dispose(), TaskScheduler.Default);
        else lifetime.Dispose();
    }
}
