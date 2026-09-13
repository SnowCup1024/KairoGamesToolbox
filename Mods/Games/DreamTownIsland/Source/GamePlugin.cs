using BepInEx;
using BepInEx.Unity.IL2CPP;
using KairoMods.Protocol;

namespace KairoMods.DreamTownIsland;

[BepInPlugin("snowcup.kairomods.dreamtownisland", "KairoMods.DreamTownIsland", "2026.9.14")]
public sealed class GamePlugin : BasePlugin
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
        GameUpdateListener.Pump = () => { control.Pump(); server.Pump(control.Handle); };
        AddComponent<GameUpdateListener>();
        Log.LogInfo("Mod loaded | appId=2488340 | date=2026-09-14 | all 4 features OFF | launcher control only | no hotkeys");
    }

    public override bool Unload()
    {
        server?.Dispose();
        GameUpdateListener.Pump = null;
        control?.Dispose();
        return true;
    }
}

public sealed class GameUpdateListener : UnityEngine.MonoBehaviour
{
    internal static Action? Pump;
    public GameUpdateListener(IntPtr pointer) : base(pointer) { }
    public void Update() => Pump?.Invoke();
}
