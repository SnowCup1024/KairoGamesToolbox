using System.Collections.Concurrent;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using KairoMods.Protocol;

namespace KairoMods.DreamTownIsland;

internal sealed class GameControl : IDisposable
{
    private static GameControl? current;
    private readonly Harmony harmony = new("snowcup.kairomods.dreamtownisland.control");
    private readonly ManualLogSource log;
    private readonly ControlState settings = new();
    private readonly ConcurrentQueue<string> messages = new();
    private readonly Dictionary<MethodBase, Resource> resources = new();
    private readonly string directory;
    private readonly string session = Guid.NewGuid().ToString("N");
    private bool attempted;
    private bool installed;
    private bool waiting;
    private string? setupError;
    private long nextCheck;
    private long sequence;
    private int queued;
    private int dropped;
    private int mainThread;
    private IntPtr moneySave;
    [ThreadStatic] private static int loadingDepth;
    [ThreadStatic] private static HashSet<string>? active;
    [ThreadStatic] private static bool refundInProgress;
    private sealed record Resource(string Feature, MethodInfo Reader, MethodInfo Add, PropertyInfo Maximum, PropertyInfo? Data);
    private sealed record Call(Resource Resource, object Owner, string Feature, string Key, FeatureState Setting,
        long Before, long Requested, string Identity);

    public GameControl(ManualLogSource log, string directory) { this.log = log; this.directory = directory; current = this; }
    public ModControlResponse Handle(ModControlRequest request)
    {
        string? error = setupError;
        if (request.Protocol != ControlProtocol.Version) error = "Unsupported protocol";
        else if (request.Action == "set")
        {
            if (error == null) error = settings.Configure(request.Features);
            if (error == null) log.LogInfo("ModControl confirmed | " + string.Join(",", settings.Snapshot().Select(p => $"{p.Key}={p.Value.Enabled}:{p.Value.Multiplier}x")));
        }
        else if (request.Action != "status") error = "Unknown command";
        return new(ControlProtocol.Version, 2488340, directory, session, setupError == null, settings.Snapshot(), error);
    }

    private static unsafe bool NativeReady()
    {
        var klass = IL2CPP.GetIl2CppClass("Assembly-CSharp.dll", "main", "AppData");
        if (klass == IntPtr.Zero) return false;
        var field = IL2CPP.GetIl2CppField(klass, "instance_");
        if (field == IntPtr.Zero) return false;
        IntPtr pointer = IntPtr.Zero;
        IL2CPP.il2cpp_field_static_get_value(field, &pointer);
        return pointer != IntPtr.Zero;
    }

    public void Pump()
    {
        mainThread = Environment.CurrentManagedThreadId;
        if (!attempted && Environment.TickCount64 >= nextCheck)
        {
            nextCheck = Environment.TickCount64 + 1000;
            try
            {
                if (NativeReady()) { attempted = true; Install(); }
                else if (!waiting) { waiting = true; log.LogInfo("Mod hooks waiting | native main.AppData singleton pending; launcher settings accepted"); }
            }
            catch (Exception ex)
            {
                attempted = true;
                installed = false;
                setupError = "Hook initialization failed; restart required.";
                harmony.UnpatchSelf();
                log.LogError("Mod setup failed | " + ex);
            }
        }
        for (int i = 0; i < 8 && messages.TryDequeue(out var line); i++)
        {
            Interlocked.Decrement(ref queued);
            log.LogInfo(line);
        }
        int lost = Interlocked.Exchange(ref dropped, 0);
        if (lost > 0) log.LogWarning("Mod log buffer full | dropped=" + lost);
    }

    private void Install()
    {
        var assembly = Assembly.Load("Assembly-CSharp");
        foreach (var (typeName, method, reader, maximum, feature) in new[] {
            ("main.AppData", "AddMoney", "GetMoney", "MONEY_MAX", "moneyReverse"),
            ("game.Point", "AddValue", "get_value_", "VALUE_MAX", "point"),
            ("game.ChipModel", "AddHaveNum", "get_haveNum_", "HAVE_NUM_MAX", "buildingReverse"),
            ("game.Item", "AddHaveNum", "get_haveNum_", "HAVE_NUM_MAX", "itemReverse") })
        {
            var type = assembly.GetType(typeName, true)!;
            var parameters = feature == "moneyReverse" ? new[] { typeof(long), typeof(bool) } : new[] { typeof(int) };
            var target = type.GetMethod(method, parameters) ?? throw new MissingMethodException(typeName, method);
            var getter = type.GetMethod(reader, Type.EmptyTypes) ?? throw new MissingMethodException(typeName, reader);
            var max = type.GetProperty(maximum) ?? throw new MissingMemberException(typeName, maximum);
            if (target.IsStatic || target.ReturnType != typeof(void) || getter.IsStatic
                || getter.ReturnType != parameters[0] || max.GetMethod?.IsStatic != true || max.PropertyType != parameters[0])
                throw new InvalidOperationException("Control signature mismatch: " + typeName);
            resources.Add(target, new(feature, getter, target, max, feature == "moneyReverse" ? null : type.GetProperty("data_")!));
        }
        var appType = assembly.GetType("main.AppData", true)!;
        var setMoney = appType.GetMethod("SetMoney", new[] { typeof(long) })!;
        resources.Add(setMoney, resources.Values.Single(r => r.Feature == "moneyReverse"));
        var loading = appType.GetMethods().Where(m => m.Name is "LoadGame" or "NewGame" or "Init").ToArray();
        var itemType = assembly.GetType("game.Item", true)!;
        var useItem = itemType.GetMethod("Use", new[] { assembly.GetType("game.Citizen", true)!, typeof(int) })
            ?? throw new MissingMethodException("game.Item", "Use");
        if (useItem.IsStatic || useItem.ReturnType != typeof(bool)) throw new InvalidOperationException("Item Use signature mismatch");
        resources.Add(useItem, resources.Values.Single(r => r.Feature == "itemReverse"));
        try
        {
            foreach (var target in resources.Keys)
                harmony.Patch(target, new HarmonyMethod(typeof(GameControl), target == useItem ? nameof(BeforeItemUse) : target == setMoney ? nameof(BeforeSetMoney) : nameof(Before)),
                    new HarmonyMethod(typeof(GameControl), nameof(After)), finalizer: new HarmonyMethod(typeof(GameControl), nameof(FinalizeCall)));
            foreach (var method in loading)
                harmony.Patch(method, new HarmonyMethod(typeof(GameControl), nameof(BeforeLoad)),
                    finalizer: new HarmonyMethod(typeof(GameControl), nameof(FinishLoad)));
            installed = true;
        }
        catch { harmony.UnpatchSelf(); throw; }
        log.LogInfo("Mod hooks ready | revision=control-3 | hooks=9 | features=4 | no snapshots | default OFF | actual consumption; load/new-game excluded");
    }

    private static string Clean(object? value) => (Convert.ToString(value) ?? "").Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ');
    private void Enqueue(string line)
    {
        if (Interlocked.Increment(ref queued) > 500) { Interlocked.Decrement(ref queued); Interlocked.Increment(ref dropped); return; }
        messages.Enqueue(line);
    }

    private static void Before(object __instance, MethodBase __originalMethod, object[] __args, out Call? __state)
        => Begin(__instance, __originalMethod, Convert.ToInt64(__args[0]), out __state);

    private static void BeforeItemUse(object __instance, MethodBase __originalMethod, int __1, out Call? __state)
        => Begin(__instance, __originalMethod, -(long)__1, out __state);

    private static void BeforeLoad() => loadingDepth++;
    private static Exception? FinishLoad(Exception? __exception)
    {
        loadingDepth = Math.Max(0, loadingDepth - 1);
        if (current != null) current.moneySave = IntPtr.Zero;
        return __exception;
    }
    private static void BeforeSetMoney(object __instance, MethodBase __originalMethod, long __0, out Call? __state)
    {
        Begin(__instance, __originalMethod, -1, out __state, absolute: true);
        if (__state != null && __0 >= __state.Before)
        {
            active?.Remove(__state.Key);
            __state = null;
        }
    }

    private static void Begin(object __instance, MethodBase __originalMethod, long requested, out Call? __state, bool absolute = false)
    {
        __state = null;
        var self = current;
        if (self == null || !self.installed || loadingDepth > 0 || refundInProgress || Environment.CurrentManagedThreadId != self.mainThread) return;
        string? key = null;
        try
        {
            var resource = self.resources[__originalMethod];
            if (resource.Feature == "moneyReverse")
            {
                var save = __instance.GetType().GetProperty("save_")?.GetValue(__instance) as Il2CppObjectBase;
                if (save == null) return;
                bool changed = self.moneySave != save.Pointer;
                self.moneySave = save.Pointer;
                // First absolute assignment for a new save is a baseline, never a purchase.
                if (absolute && changed) return;
            }
            var data = resource.Data?.GetValue(__instance);
            var id = data?.GetType().GetProperty("id_")?.GetValue(data);
            string? feature = resource.Feature == "point" ? (id == null ? null : ControlState.PointFeature(Convert.ToInt32(id))) : resource.Feature;
            if (feature == null) return;
            key = feature + ":" + ((Il2CppObjectBase)__instance).Pointer.ToString("X");
            if (!(active ??= new()).Add(key)) return;
            long before = Convert.ToInt64(resource.Reader.Invoke(__instance, null));
            var name = data?.GetType().GetProperty("name_")?.GetValue(data);
            __state = new(resource, __instance, feature, key, self.settings.Get(feature), before, requested, $"id={Clean(id)},name={Clean(name)}");
        }
        catch (Exception ex)
        {
            if (key != null) active?.Remove(key);
            self.Enqueue("Mod read failed | " + Clean(ex.GetBaseException().Message));
        }
    }

    private static void After(Call? __state)
    {
        var self = current;
        if (self == null || __state == null) return;
        var call = __state;
        try
        {
            long after = Convert.ToInt64(call.Resource.Reader.Invoke(call.Owner, null));
            long refund = 0;
            if (call.Setting.Enabled && call.Requested < 0)
            {
                long maximum = Convert.ToInt64(call.Resource.Maximum.GetValue(null));
                refund = ControlState.Refund(call.Before, after, call.Requested, call.Setting, maximum);
                if (refund > 0)
                {
                    refundInProgress = true;
                    try
                    {
                        var args = call.Feature == "moneyReverse" ? new object[] { refund, false } : new object[] { checked((int)refund) };
                        call.Resource.Add.Invoke(call.Owner, args);
                    }
                    finally { refundInProgress = false; }
                    after = Convert.ToInt64(call.Resource.Reader.Invoke(call.Owner, null));
                }
                else if (call.Before > after) self.Enqueue("Mod reversal skipped | resource=" + call.Feature + " | game limit or unsupported balance");
            }
            if (after != call.Before || refund > 0)
                self.Enqueue(FormattableString.Invariant($"ResourceResult | time={DateTimeOffset.Now:O} | id={++self.sequence} | resource={call.Feature} | owner={call.Identity} | before={call.Before} | after={after} | delta={(decimal)after - call.Before} | refund={refund} | requested={call.Requested} | method={call.Resource.Add.Name}"));
        }
        catch (Exception ex) { self.Enqueue("Mod operation failed | " + Clean(ex.GetBaseException().Message)); }
    }

    private static Exception? FinalizeCall(Exception? __exception, Call? __state)
    {
        if (__state != null) active?.Remove(__state.Key);
        return __exception;
    }
    public void Dispose() { installed = false; harmony.UnpatchSelf(); current = null; }
}
