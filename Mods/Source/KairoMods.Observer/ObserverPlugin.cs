using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace KairoMods.Observer;

[BepInPlugin("snowcup.kairomods.observer", "KairoMods.Observer", "0.0.4")]
public sealed class ObserverPlugin : BasePlugin
{
    private static ManualLogSource traceLog = null!;
    private static MethodInfo readMoney = null!;
    private static long sequence;
    private readonly Harmony harmony = new("snowcup.kairomods.observer");

    private bool attempted;
    private bool ready;
    private string? setupError;
    private static bool reverseEnabled;
    private static MethodInfo addMoney = null!;
    private ControlServer? server;

    public override void Load()
    {
        traceLog = Log;
        Log.LogInfo("[KairoMods.Observer] Loaded | version=0.0.4 | mode=money-control-test");
        ActivationListener.Activate = InstallObservation;
        ActivationListener.Pump = () => server?.Pump(HandleCommand);
        AddComponent<ActivationListener>();
        UnityEngine.Application.runInBackground = true;
        server = new ControlServer();
        server.Start();
        Log.LogInfo("ModControl listening | default money reversal OFF");
        Log.LogInfo("MoneyTrace waiting | load a save, then press F8 once | no money hooks installed yet");
    }

    private ControlReply HandleCommand(ControlRequest request)
    {
        if (request.Protocol != 1) return Reply("Unsupported protocol");
        switch (request.Action)
        {
            case "activate": InstallObservation(); break;
            case "set":
                if (!ready) return Reply("请先进入存档并启用控制。");
                reverseEnabled = request.Enabled;
                Log.LogInfo($"MoneyReverse confirmed: {reverseEnabled}");
                break;
            case "status": break;
            default: return Reply("Unknown command");
        }
        return Reply(setupError);
    }

    private ControlReply Reply(string? error) => new(1, 2934180, ControlServer.GameDirectory, ready, reverseEnabled, error);

    private void InstallObservation()
    {
        if (attempted) return;
        attempted = true;
        Log.LogInfo("MoneyTrace activation requested after user entered save");
        try
        {
            var game = Assembly.Load("Assembly-CSharp");
            var state = game.GetType("S", throwOnError: true)!;
            var app = game.GetType("ui.AppData", throwOnError: true)!;
            readMoney = state.GetMethod("get_Money", BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMethodException("S.get_Money");
            var sub = app.GetMethod("SubMoney", new[] { typeof(long), typeof(bool) })
                ?? throw new MissingMethodException("ui.AppData.SubMoney");
            var add = app.GetMethod("AddMoney", new[] { typeof(long) })
                ?? throw new MissingMethodException("ui.AppData.AddMoney");
            addMoney = add;
            foreach (var target in new[] { sub, add })
                harmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(ObserverPlugin), nameof(BeforeMoney)),
                    postfix: new HarmonyMethod(typeof(ObserverPlugin), nameof(AfterMoney)));
            ready = true;
            Log.LogInfo("MoneyTrace ready | observing SubMoney and AddMoney | original methods run unchanged");
        }
        catch (Exception ex)
        {
            setupError = "钩子安装失败，请退出并重启游戏；不要继续消费测试。";
            Log.LogError($"MoneyTrace setup failed; restart game before retrying: {ex}");
            try { harmony.UnpatchSelf(); }
            catch (Exception cleanup) { Log.LogError($"MoneyTrace cleanup also failed: {cleanup}"); }
        }
    }

    public sealed class Observation
    {
        public long Id;
        public long? Before;
        public bool Reverse;
    }

    private static long? ReadBalance()
    {
        try { return (long)readMoney.Invoke(null, null)!; }
        catch (Exception ex)
        {
            traceLog.LogWarning($"MoneyTrace balance read failed: {ex.GetBaseException().Message}");
            return null;
        }
    }

    private static void BeforeMoney(MethodBase __originalMethod, long __0, out Observation __state)
    {
        __state = new Observation { Id = Interlocked.Increment(ref sequence) };
        try
        {
            __state.Before = ReadBalance();
            __state.Reverse = reverseEnabled && __originalMethod.Name == "SubMoney" && __0 > 0;
            traceLog.LogInfo($"MoneyTrace #{__state.Id} BEGIN | time={DateTimeOffset.Now:O} | method={__originalMethod.Name} | amount={__0} | before={__state.Before?.ToString() ?? "unknown"}");
        }
        catch (Exception ex) { traceLog.LogWarning($"MoneyTrace logging failed: {ex.Message}"); }
    }

    private static void AfterMoney(object __instance, MethodBase __originalMethod, ref long __result, Observation __state)
    {
        try
        {
            var after = ReadBalance();
            if (__state.Reverse && after.HasValue && __state.Before.HasValue)
            {
                var refund = MoneyReversal.Refund(__state.Before.Value, after.Value);
                if (refund > 0)
                {
                    addMoney.Invoke(__instance, new object[] { refund });
                    after = ReadBalance();
                    if (after.HasValue) __result = after.Value;
                    traceLog.LogInfo($"MoneyReverse #{__state.Id} refund={refund} | after={after}");
                }
            }
            var delta = after.HasValue && __state.Before.HasValue
                ? ((decimal)after.Value - __state.Before.Value).ToString() : "unknown";
            traceLog.LogInfo($"MoneyTrace #{__state.Id} END | method={__originalMethod.Name} | after={after?.ToString() ?? "unknown"} | delta={delta} | result={__result}");
        }
        catch (Exception ex) { traceLog.LogWarning($"MoneyTrace logging failed: {ex.Message}"); }
    }
}

public sealed class ActivationListener : UnityEngine.MonoBehaviour
{
    internal static Action? Activate;
    internal static Action? Pump;
    private bool requested;

    public ActivationListener(IntPtr pointer) : base(pointer) { }

    public void Update()
    {
        Pump?.Invoke();
        if (requested || !UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F8)) return;
        requested = true;
        Activate?.Invoke();
    }
}
