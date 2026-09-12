using System.Diagnostics;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;

namespace KairoMods.DreamTownIsland;

internal sealed class GameObservation : IDisposable
{
    private static GameObservation? current;
    private readonly Harmony harmony = new("snowcup.kairomods.dreamtownisland.observation");
    private readonly ManualLogSource log;
    private readonly ObservationLog output = new();
    private readonly Dictionary<MethodBase, Target> targets = new();
    private readonly Dictionary<(Type, string), PropertyInfo?> properties = new();
    private readonly Dictionary<string, string> baselines = new();
    private readonly List<Sampler> samplers = new();
    private Type appType = null!;
    private MethodInfo moneyReader = null!;
    private IntPtr appPointer;
    private bool installed;
    private bool attempted;
    private bool waitingLogged;
    private long nextCheck;
    private long nextSample;
    private long sequence;
    private int sampleGroup;
    private int mainThread;
    [ThreadStatic] private static Stack<long>? callStack;

    private sealed record Target(HookSpec Spec, MethodInfo? Reader);
    private sealed class Sampler
    {
        public SampleSpec Spec = null!;
        public PropertyInfo List = null!;
        public PropertyInfo Count = null!;
        public PropertyInfo Item = null!;
        public PropertyInfo[] Fields = Array.Empty<PropertyInfo>();
        public int Cursor;
    }
    private sealed class Call
    {
        public Target Target = null!;
        public object? Owner;
        public long Id;
        public long Parent;
        public string Identity = "";
        public string Args = "";
        public long? Before;
        public bool Completed;
    }

    public GameObservation(ManualLogSource log) { this.log = log; current = this; }

    private static unsafe IntPtr NativeInstance()
    {
        var klass = IL2CPP.GetIl2CppClass("Assembly-CSharp.dll", "main", "AppData");
        if (klass == IntPtr.Zero) return IntPtr.Zero;
        var field = IL2CPP.GetIl2CppField(klass, "instance_");
        if (field == IntPtr.Zero) return IntPtr.Zero;
        IntPtr pointer = IntPtr.Zero;
        IL2CPP.il2cpp_field_static_get_value(field, &pointer);
        return pointer;
    }

    public void Pump()
    {
        mainThread = Environment.CurrentManagedThreadId;
        var now = Environment.TickCount64;
        if (!attempted && now >= nextCheck)
        {
            nextCheck = now + 1000;
            try
            {
                if (NativeInstance() != IntPtr.Zero) { attempted = true; Install(); }
                else if (!waitingLogged)
                {
                    waitingLogged = true;
                    log.LogInfo("GameTrace waiting | native main.AppData singleton not created | no forced game initialization");
                }
            }
            catch (Exception ex)
            {
                attempted = true;
                installed = false;
                harmony.UnpatchSelf();
                log.LogError("GameTrace setup failed | restart required | " + ex);
            }
        }
        if (installed && now >= nextSample)
        {
            nextSample = now + 1000;
            try { Sample(); }
            catch (Exception ex) { Fault("sampling", ex); nextSample = now + 5000; }
        }
        output.Flush(line => log.LogInfo(line), line => log.LogWarning(line));
    }

    private void Install()
    {
        var assembly = Assembly.Load("Assembly-CSharp");
        var plan = ObservationPlan.Load();
        appType = assembly.GetType("main.AppData", true)!;
        moneyReader = appType.GetMethod("GetMoney", Type.EmptyTypes)!;
        foreach (var spec in plan.Hooks)
        {
            var type = assembly.GetType(spec.Type, true)!;
            var args = spec.Parameters.Select(n => System.Type.GetType(n) ?? assembly.GetType(n, true)!).ToArray();
            var method = type.GetMethod(spec.Method, args) ?? throw new MissingMethodException(spec.Type, spec.Method);
            if (method.IsStatic != spec.Static || method.ReturnType.FullName != spec.Returns) throw new InvalidOperationException("Signature mismatch: " + method);
            var reader = spec.Reader == null ? null : type.GetMethod(spec.Reader, Type.EmptyTypes)
                ?? throw new MissingMethodException(spec.Type, spec.Reader);
            if (reader != null && (reader.IsStatic || (reader.ReturnType != typeof(int) && reader.ReturnType != typeof(long))))
                throw new InvalidOperationException("Invalid balance reader: " + reader);
            targets.Add(method, new(spec, reader));
        }
        foreach (var spec in plan.Samples)
        {
            var list = appType.GetProperty(spec.List)!;
            var type = assembly.GetType(spec.Type, true)!;
            samplers.Add(new() { Spec = spec, List = list, Count = list.PropertyType.GetProperty("Count")!,
                Item = list.PropertyType.GetProperty("Item")!, Fields = spec.Fields.Select(n => type.GetProperty(n)!).ToArray() });
        }
        try
        {
            foreach (var method in targets.Keys)
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(GameObservation), method.IsStatic ? nameof(BeforeStatic) : nameof(Before)),
                    postfix: new HarmonyMethod(typeof(GameObservation), nameof(After)),
                    finalizer: new HarmonyMethod(typeof(GameObservation), nameof(FinalizeCall)));
            installed = true;
        }
        catch { harmony.UnpatchSelf(); throw; }
        log.LogInfo($"GameTrace ready | revision=observe-1 | hooks={targets.Count} | sampleGroups={samplers.Count} | READ ONLY | no hotkeys");
        log.LogInfo("GameTrace coverage | " + string.Join(",", targets.Values.Select(t => t.Spec.Type + "." + t.Spec.Method)));
        log.LogInfo("GameTrace limits | sample=1 group/second,32 objects,8ms budget | detail=8/key/10s | queue=500 | nested calls have parent IDs; snapshots are not extra transactions");
        log.LogInfo("GameTrace gaps | no exhaustive UI/render/timer hooks; no serialized blobs; citizen fields observed on selected calls only; collection sampling can miss intermediate changes");
    }

    private object? Property(object obj, string name)
    {
        var key = (obj.GetType(), name);
        if (!properties.TryGetValue(key, out var property)) properties[key] = property = key.Item1.GetProperty(name);
        return property?.GetValue(obj);
    }

    private static string Clean(object? value)
    {
        var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "null";
        return text.Replace('\r', ' ').Replace('\n', ' ').Replace('|', '/')[..Math.Min(text.Length, 100)];
    }

    private string Identify(object? obj)
    {
        if (obj == null) return "null";
        if (obj is not Il2CppObjectBase native) return Clean(obj);
        var type = obj.GetType().FullName;
        var data = Property(obj, "data_");
        var id = Property(data ?? obj, "id_");
        var name = Property(data ?? obj, "name_");
        if (type == "game.Citizen") name = Clean(Property(obj, "familyName_")) + Clean(Property(obj, "lastName_"));
        return $"{type}@{native.Pointer:X}[id={Clean(id)},name={Clean(name)}]";
    }

    private static void Before(object __instance, MethodBase __originalMethod, object[] __args, out Call? __state)
        => Begin(__instance, __originalMethod, __args, out __state);

    private static void BeforeStatic(MethodBase __originalMethod, object[] __args, out Call? __state)
        => Begin(null, __originalMethod, __args, out __state);

    private static void Begin(object? __instance, MethodBase __originalMethod, object[] __args, out Call? __state)
    {
        __state = null;
        var self = current;
        if (self == null || !self.installed) return;
        // No game-object inspection from incidental worker-thread calls.
        if (Environment.CurrentManagedThreadId != self.mainThread)
        {
            self.output.Record("worker:" + __originalMethod.Name, () => "ProbeSkipped | worker-thread call | " + __originalMethod.Name);
            return;
        }
        try
        {
            var target = self.targets[__originalMethod];
            if (target.Spec.Type == "main.AppData" && self.Property(__instance!, "save_") == null) return;
            var call = new Call { Target = target, Owner = __instance, Id = ++self.sequence,
                Parent = callStack?.Count > 0 ? callStack.Peek() : 0, Identity = self.Identify(__instance),
                Args = string.Join(",", __args.Select(self.Identify)),
                Before = target.Reader == null ? null : Convert.ToInt64(target.Reader.Invoke(__instance, null)) };
            (callStack ??= new()).Push(call.Id);
            __state = call;
        }
        catch (Exception ex) { self.Fault("prefix", ex); }
    }

    // Omitting __result works for both void and bool; the original result is never changed.
    private static void After(Call? __state)
    {
        var self = current;
        if (self == null || __state == null) return;
        try
        {
            var call = __state;
            long? after = call.Target.Reader == null ? null : Convert.ToInt64(call.Target.Reader.Invoke(call.Owner, null));
            call.Completed = true;
            self.output.Record(call.Target.Spec.Type + "." + call.Target.Spec.Method, () =>
                $"{(after.HasValue ? "ResourceTrace" : "EventTrace")} | time={DateTimeOffset.Now:O} | id={call.Id} | parent={call.Parent} | resource={call.Target.Spec.Resource} | method={call.Target.Spec.Type}.{call.Target.Spec.Method} | owner={call.Identity} | args={call.Args} | before={call.Before} | after={after} | delta={(after.HasValue ? ((decimal)after.Value - call.Before!.Value).ToString() : "unknown")} | completed=true");
        }
        catch (Exception ex) { self.Fault("postfix", ex); }
    }

    private static Exception? FinalizeCall(Exception? __exception, Call? __state)
    {
        if (__state != null)
        {
            if (callStack?.Count > 0 && callStack.Peek() == __state.Id) callStack.Pop();
            else callStack?.Clear();
            if (__exception != null) current?.Fault("original:" + __state.Target.Spec.Method, __exception);
        }
        return __exception;
    }

    private void Fault(string stage, Exception ex) => output.Record("fault:" + stage,
        () => "ProbeReadError | stage=" + stage + " | " + Clean(ex.GetBaseException().Message));

    private void Snapshot(string key, object? value, string group)
    {
        var text = Clean(value);
        bool known = baselines.TryGetValue(key, out var before);
        if (known && text == before) return;
        if (!known && baselines.Count >= 4096)
        {
            output.Record("snapshotLimit", () => "ProbeCoverageGap | baseline limit=4096; new objects skipped until game singleton changes");
            return;
        }
        baselines[key] = text;
        output.Record("snapshot:" + group, () => $"StateTrace | time={DateTimeOffset.Now:O} | field={key} | {(known ? "before=" + before + " | after=" + text : "baseline=" + text)} | sampled=true");
    }

    private void Sample()
    {
        var pointer = NativeInstance();
        if (pointer == IntPtr.Zero) return;
        if (pointer != appPointer)
        {
            appPointer = pointer;
            baselines.Clear();
            foreach (var sampler in samplers) sampler.Cursor = 0;
        }
        var app = Activator.CreateInstance(appType, pointer)!;
        if (Property(app, "save_") == null) return;
        var watch = Stopwatch.StartNew();
        Snapshot("townMoney", moneyReader.Invoke(app, null), "townMoney");
        var group = samplers[sampleGroup++ % samplers.Count];
        var list = group.List.GetValue(app);
        if (list == null) return;
        int count = Convert.ToInt32(group.Count.GetValue(list));
        if (count <= 0) { group.Cursor = 0; return; }
        Snapshot(group.Spec.List + ".Count", count, "collectionCounts");
        int read = 0;
        while (read < Math.Min(count, 32) && watch.ElapsedMilliseconds < 8)
        {
            group.Cursor %= count;
            var item = group.Item.GetValue(list, new object[] { group.Cursor++ });
            read++;
            if (item == null) continue;
            var identity = Identify(item);
            foreach (var field in group.Fields) Snapshot(identity + "." + field.Name, field.GetValue(item), group.Spec.Type);
        }
        if (read < count) output.Record("sampleBudget:" + group.Spec.List,
            () => $"ProbeCoverageGap | list={group.Spec.List} | sampled={read}/{count} | rotating=true | elapsedMs={watch.ElapsedMilliseconds}");
    }

    public void Dispose()
    {
        installed = false;
        harmony.UnpatchSelf();
        current = null;
    }
}
