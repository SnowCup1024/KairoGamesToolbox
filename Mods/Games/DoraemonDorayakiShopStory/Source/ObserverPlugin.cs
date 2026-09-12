using BepInEx;
using BepInEx.Unity.IL2CPP;
using KairoMods.Protocol;

namespace KairoMods.Observer;

[BepInPlugin("snowcup.kairomods.observer", "KairoMods.Doraemon", "1.0.2")]
public sealed class ObserverPlugin : BasePlugin
{
    private GameObservation observation = null!;
    private readonly ControlServer server = new();
    private readonly string session = Guid.NewGuid().ToString("N");
    private bool attempted;
    private long nextCheck;
    private string? setupError;
    private bool waitingLogged;

    public override void Load()
    {
        // The launcher renders the log. Detach only BepInEx's console, never the game window.
        try
        {
            var config = new BepInEx.Configuration.ConfigFile(Paths.BepInExConfigPath, false);
            config.Bind("Logging.Console", "Enabled", false).Value = false;
            config.Save();
            foreach (var listener in BepInEx.Logging.Logger.Listeners.OfType<BepInEx.Logging.ConsoleLogListener>().ToArray())
            {
                BepInEx.Logging.Logger.Listeners.Remove(listener);
                listener.Dispose();
            }
            if (ConsoleManager.ConsoleActive) ConsoleManager.DetachConsole();
        }
        catch (Exception ex) { Log.LogWarning("Console configuration: " + ex.Message); }
        observation = new GameObservation(Log);
        ActivationListener.Pump = Pump;
        AddComponent<ActivationListener>();
        server.Start();
        Log.LogInfo("KairoMods 1.0.2 | launcher control only | all features OFF | no hotkeys");
    }

    private void Pump()
    {
        server.Pump(HandleCommand);
        // Never force game static constructors from Load or a launcher command.
        if (!attempted && Environment.TickCount64 >= nextCheck)
        {
            nextCheck = Environment.TickCount64 + 1000;
            try
            {
                if (GameObservation.NativeTypesReady())
                {
                    attempted = true;
                    observation.Install();
                }
                else if (!waitingLogged)
                {
                    waitingLogged = true;
                    Log.LogInfo("Mod hooks waiting | AppData singleton not created yet | settings accepted, gameplay hooks pending");
                }
            }
            catch (Exception ex)
            {
                attempted = true;
                setupError = "Hook initialization failed; restart required.";
                Log.LogError(ex);
            }
        }
        observation.FlushLogs();
    }

    private ModControlResponse HandleCommand(ModControlRequest request)
    {
        string? error = setupError;
        if (request.Protocol != ControlProtocol.Version) error = "Unsupported protocol";
        else if (request.Action == "set")
        {
            if (error == null) error = observation.Configure(request.Features);
        }
        else if (request.Action != "status") error = "Unknown command";
        return new(ControlProtocol.Version, 2934180, ControlServer.GameDirectory, session,
            setupError == null, observation.Snapshot(), error);
    }
}

public sealed class ActivationListener : UnityEngine.MonoBehaviour
{
    internal static Action? Pump;
    public ActivationListener(IntPtr pointer) : base(pointer) { }
    public void Update() => Pump?.Invoke();
}
