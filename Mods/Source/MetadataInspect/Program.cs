using Mono.Cecil;
using var a=AssemblyDefinition.ReadAssembly(args[0]);
foreach(var t in a.MainModule.Types.Where(t=>t.FullName=="ui.AppData" || t.FullName=="S"))
foreach(var m in t.Methods.Where(m=>m.Name.Contains("Money")))
Console.WriteLine($"{m.FullName} static={m.IsStatic} params={string.Join(", ",m.Parameters.Select(p=>$"{p.Name}:{p.ParameterType}"))}");
