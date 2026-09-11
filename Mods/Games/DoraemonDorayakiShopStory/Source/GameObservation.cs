using System.Collections.Concurrent;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using KairoMods.Protocol;

namespace KairoMods.Observer;

internal sealed class GameObservation
{
    private static GameObservation current = null!;
    private readonly ManualLogSource log;
    private readonly Harmony harmony = new("snowcup.kairomods.observer");
    private readonly Dictionary<MethodBase, Resource> resources = new();
    private readonly ConcurrentQueue<string> messages = new();
    private Dictionary<string, FeatureState> features = new[] { "moneyReverse", "fPointReverse", "coinReverse", "trainingReverse", "itemReverse" }
        .ToDictionary(id => id, _ => new FeatureState());
    [ThreadStatic] private static HashSet<string>? transactions;
    [ThreadStatic] private static bool refundInProgress;
    private bool installed;
    private long sequence;
    private int dropped;
    private sealed record Resource(string Id, MethodInfo Reader, MethodInfo Add, bool IsInt);
    private sealed class Call
    {
        public Resource Resource = null!;
        public object Owner = null!;
        public string Key = "";
        public FeatureState Setting = new();
        public long Before;
        public long After;
        public bool Deduction;
        public bool Complete;
        public bool Reversed;
    }

    public GameObservation(ManualLogSource logger) { log = logger; current = this; }

    public Dictionary<string, FeatureState> Snapshot() => new(features);

    public string? Configure(Dictionary<string, FeatureState>? desired)
    {
        if (desired == null || desired.Count != features.Count || desired.Any(p => !features.ContainsKey(p.Key)
            || p.Value == null || !ControlProtocol.ValidMultiplier(p.Value.Multiplier))) return "Invalid feature settings";
        features = new(desired);
        return null;
    }

    public static unsafe bool NativeTypesReady()
    {
        // Read native metadata flags, without running the generated managed .cctor.
        foreach (var (ns, name) in new[] { ("", "S"), ("ui", "AppData"), ("data", "Character"), ("data", "ItemData") })
        {
            var pointer = IL2CPP.GetIl2CppClass("Assembly-CSharp.dll", ns, name);
            if (pointer == IntPtr.Zero || !UnityVersionHandler.Wrap((Il2CppClass*)pointer).InitializedAndNoError) return false;
        }
        return true;
    }

    public void Install()
    {
        var assembly = Assembly.Load("Assembly-CSharp");
        var state = assembly.GetType("S", true)!;
        var app = assembly.GetType("ui.AppData", true)!;
        foreach (var (id, field, add, sub) in new[] {
            ("moneyReverse", "Money", "AddMoney", "SubMoney"),
            ("fPointReverse", "FPoint", "AddFPoint", "SubFPoint"),
            ("coinReverse", "CoinPoint", "AddCoinPoint", "SubCoin") })
        {
            var reader = state.GetMethod("get_" + field)!;
            var increase = app.GetMethod(add, new[] { typeof(long) })!;
            var decrease = app.GetMethod(sub, new[] { typeof(long), typeof(bool) })!;
            if (increase == null || decrease == null || reader == null || increase.ReturnType != typeof(long) || decrease.ReturnType != typeof(long))
                throw new MissingMethodException(id);
            var resource = new Resource(id, reader, increase, false);
            resources.Add(increase, resource); resources.Add(decrease, resource);
        }
        foreach (var (id, typeName, getter, add, sub) in new[] {
            ("trainingReverse", "data.Character", "get_HeartPoint", "AddHeartPoint", "SubHeartPoint"),
            ("itemReverse", "data.ItemData", "GetStock", "AddStock", "SubStock") })
        {
            var type = assembly.GetType(typeName, true)!;
            var increase = type.GetMethod(add, new[] { typeof(int) })!;
            var decrease = type.GetMethod(sub, new[] { typeof(int) })!;
            var reader = type.GetMethod(getter, Type.EmptyTypes)!;
            if (increase == null || decrease == null || reader == null || increase.ReturnType != typeof(void) || decrease.ReturnType != typeof(void))
                throw new MissingMethodException(id);
            var resource = new Resource(id, reader, increase, true);
            resources.Add(increase, resource); resources.Add(decrease, resource);
        }
        var item = assembly.GetType("data.ItemData", true)!;
        var equip = assembly.GetType("data.Character", true)!.GetMethod("EquipItem", new[] { item, typeof(int) })!;
        if (equip == null || equip.ReturnType != typeof(void)) throw new MissingMethodException("EquipItem");
        try
        {
            foreach (var target in resources.Keys.Cast<MethodInfo>())
                harmony.Patch(target, prefix: Hook(nameof(Before)),
                    postfix: Hook(target.ReturnType == typeof(void) ? nameof(AfterVoid) : nameof(AfterLong)), finalizer: Hook(nameof(FinalizeCall)));
            harmony.Patch(equip, prefix: Hook(nameof(BeforeEquip)), postfix: Hook(nameof(AfterVoid)), finalizer: Hook(nameof(FinalizeCall)));
            installed = true;
            log.LogInfo("Mod hooks ready | Money, FPoint, CoinPoint, TrainingPoint, Items | no scalar polling");
        }
        catch { harmony.UnpatchSelf(); throw; }
    }

    private static HarmonyMethod Hook(string name) => new(typeof(GameObservation), name);
    private static long Read(Resource resource, object owner) => Convert.ToInt64(resource.Reader.Invoke(resource.Reader.IsStatic ? null : owner, null));

    private static Call? Begin(Resource resource, object owner, bool deduction)
    {
        if (!current.installed || refundInProgress) return null;
        string key = resource.Id + ":" + (resource.Reader.IsStatic ? "global" : ((Il2CppObjectBase)owner).Pointer.ToString("X"));
        transactions ??= new();
        if (!transactions.Add(key)) return null; // EquipItem may call SubStock internally.
        try { return new Call { Resource = resource, Owner = owner, Key = key, Setting = current.features[resource.Id], Before = Read(resource, owner), Deduction = deduction }; }
        catch { transactions.Remove(key); throw; }
    }

    private static void Before(object __instance, MethodBase __originalMethod, out Call? __state)
    {
        __state = null;
        try { __state = Begin(current.resources[__originalMethod], __instance, __originalMethod.Name.StartsWith("Sub", StringComparison.Ordinal)); }
        catch (Exception ex) { current.Enqueue("Hook read failed | " + ex.GetBaseException().Message); }
    }

    private static void BeforeEquip(object __0, out Call? __state)
    {
        __state = null;
        if (__0 == null) return;
        try { __state = Begin(current.resources.Values.First(r => r.Id == "itemReverse"), __0, true); }
        catch (Exception ex) { current.Enqueue("Item read failed | " + ex.GetBaseException().Message); }
    }

    private static void Complete(Call? call)
    {
        if (call == null || call.Complete) return;
        try
        {
            call.After = Read(call.Resource, call.Owner);
            if (call.Deduction && call.Setting.Enabled)
            {
                long refund = MoneyReversal.Refund(call.Before, call.After, call.Setting.Multiplier, call.Resource.IsInt ? int.MaxValue : long.MaxValue);
                if (refund > 0)
                {
                    refundInProgress = true;
                    try
                    {
                        object amount = call.Resource.IsInt ? (object)(int)refund : refund;
                        call.Resource.Add.Invoke(call.Owner, new[] { amount });
                    }
                    finally { refundInProgress = false; }
                    call.After = Read(call.Resource, call.Owner);
                    call.Reversed = true;
                }
            }
            current.Enqueue($"ResourceResult | time={DateTimeOffset.Now:O} | id={Interlocked.Increment(ref current.sequence)} | resource={call.Resource.Id} | before={call.Before} | after={call.After} | delta={(decimal)call.After - call.Before}");
            call.Complete = true;
        }
        catch (Exception ex) { current.Enqueue("Hook operation failed | " + ex.GetBaseException().Message); }
    }

    private static void AfterVoid(Call? __state) => Complete(__state);
    private static void AfterLong(Call? __state, ref long __result)
    {
        Complete(__state);
        if (__state?.Complete == true && __state.Reversed) __result = __state.After;
    }
    private static Exception? FinalizeCall(Exception? __exception, Call? __state)
    {
        if (__state != null) transactions?.Remove(__state.Key);
        return __exception; // Preserve the game's exception; never silently swallow it.
    }
    private void Enqueue(string message)
    {
        if (messages.Count >= 1000) { Interlocked.Increment(ref dropped); return; }
        messages.Enqueue(message);
    }
    public void FlushLogs()
    {
        for (int i = 0; i < 20 && messages.TryDequeue(out var line); i++) log.LogInfo(line);
        int lost = Interlocked.Exchange(ref dropped, 0);
        if (lost > 0) log.LogWarning($"Log buffer full | dropped={lost}");
    }
}
