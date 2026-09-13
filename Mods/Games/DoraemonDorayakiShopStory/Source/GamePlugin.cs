using BepInEx;
using BepInEx.Unity.IL2CPP;
using KairoMods.Protocol;

namespace KairoMods.DoraemonDorayakiShopStory;

[BepInPlugin("snowcup.kairomods.observer", "KairoMods.Doraemon", "2026.9.14")]
public sealed class GamePlugin : BasePlugin
{
    private GameControl control = null!;
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
        control = new GameControl(Log);
        GameUpdateListener.Pump = Pump;
        AddComponent<GameUpdateListener>();
        server.Start();
        Log.LogInfo("KairoMods 2026-09-14 | launcher control only | all features OFF | no hotkeys");
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
                if (GameControl.NativeTypesReady())
                {
                    attempted = true;
                    control.Install();
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
        control.FlushLogs();
    }

    private ModControlResponse HandleCommand(ModControlRequest request)
    {
        string? error = setupError;
        if (request.Protocol != ControlProtocol.Version) error = "Unsupported protocol";
        else if (request.Action == "set")
        {
            if (error == null) error = control.Configure(request.Features);
        }
        else if (request.Action != "status") error = "Unknown command";
        return new(ControlProtocol.Version, 2934180, ControlServer.GameDirectory, session,
            setupError == null, control.Snapshot(), error);
    }
}

public sealed class GameUpdateListener : UnityEngine.MonoBehaviour
{
    internal static Action? Pump;
    public GameUpdateListener(IntPtr pointer) : base(pointer) { }
    public void Update() => Pump?.Invoke();
}
