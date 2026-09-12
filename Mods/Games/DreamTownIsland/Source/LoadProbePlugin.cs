using BepInEx;
using BepInEx.Unity.IL2CPP;
using KairoMods.Protocol;

namespace KairoMods.DreamTownIsland;

[BepInPlugin("snowcup.kairomods.dreamtownisland", "KairoMods.DreamTownIsland", "1.0.4")]
public sealed class LoadProbePlugin : BasePlugin
{
    private GameControlServer? server;
    private GameControl? control;

    public override void Load()
    {
        var directory = Path.GetDirectoryName(Environment.ProcessPath)!;
        var identity = File.ReadAllLines(Path.Combine(directory, "KairoGames_Data", "app.info"));
        if (IntPtr.Size != 4 || identity.Length < 2 || identity[0].Trim() != "Kairosoft" || identity[1].Trim() != "創造タウンズ島")
            throw new InvalidOperationException("Dream Town Island x86 identity check failed.");
        control = new GameControl(Log, directory);
        server = new GameControlServer(directory);
        server.Start(error => Log.LogError("ModControl pipe failed | " + error.Message));
        ObservationListener.Pump = () => { control.Pump(); server.Pump(control.Handle); };
        AddComponent<ObservationListener>();
        Log.LogInfo("Mod loaded | appId=2488340 | version=1.0.4 | revision=control-3 | all 4 features OFF | launcher control only | no hotkeys");
    }

    public override bool Unload()
    {
        server?.Dispose();
        ObservationListener.Pump = null;
        control?.Dispose();
        return true;
    }
}

public sealed class ObservationListener : UnityEngine.MonoBehaviour
{
    internal static Action? Pump;
    public ObservationListener(IntPtr pointer) : base(pointer) { }
    public void Update() => Pump?.Invoke();
}
