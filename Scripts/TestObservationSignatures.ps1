[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GameDirectory,
    [Parameter(Mandatory)][string]$PlanPath,
    [string]$PluginDll
)
$ErrorActionPreference = 'Stop'
Add-Type -LiteralPath (Join-Path $GameDirectory 'BepInEx/core/Mono.Cecil.dll')
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameDirectory 'BepInEx/interop/Assembly-CSharp.dll'))
$plugin = $null
try {
    $plan = Get-Content -LiteralPath $PlanPath -Raw | ConvertFrom-Json
    foreach ($spec in $plan.hooks) {
        $type = $assembly.MainModule.GetType($spec.type.Replace('+','/'))
        if (-not $type) { throw "Missing type $($spec.type)" }
        $returnType = if ($spec.returns) { $spec.returns } else { 'System.Void' }
        $found = @($type.Methods | Where-Object {
            $_.Name -ceq $spec.method -and $_.IsStatic -eq [bool]$spec.static -and $_.ReturnType.FullName -ceq $returnType -and
            (($_.Parameters | ForEach-Object { $_.ParameterType.FullName.Replace('/','+') }) -join ',') -ceq ($spec.parameters -join ',')
        })
        if ($found.Count -ne 1) { throw "Signature mismatch: $($spec.type).$($spec.method)($($spec.parameters -join ','))" }
        if ($spec.reader) {
            $reader = @($type.Methods | Where-Object { $_.Name -ceq $spec.reader -and -not $_.IsStatic -and $_.Parameters.Count -eq 0 -and $_.ReturnType.FullName -in @('System.Int32','System.Int64') })
            if ($reader.Count -ne 1) { throw "Balance reader mismatch: $($spec.type).$($spec.reader)" }
        }
        if ($spec.maximum) {
            $max = @($type.Properties | Where-Object { $_.Name -ceq $spec.maximum -and $_.GetMethod.IsStatic -and $_.PropertyType.FullName -ceq $spec.parameters[0] })
            if ($max.Count -ne 1) { throw "Game limit mismatch: $($spec.type).$($spec.maximum)" }
        }
    }
    $app = $assembly.MainModule.GetType('main.AppData')
    foreach ($sample in $plan.samples) {
        $type = $assembly.MainModule.GetType($sample.type.Replace('+','/'))
        $list = @($app.Properties | Where-Object Name -CEQ $sample.list)
        if ($list.Count -ne 1 -or $list[0].PropertyType.FullName -cne "Il2CppSystem.Collections.Generic.List``1<$($sample.type)>") { throw "List mismatch: $($sample.list)" }
        foreach ($field in $sample.fields) {
            $property = @($type.Properties | Where-Object { $_.Name -ceq $field -and $_.PropertyType.FullName -in @('System.Int32','System.Int64','System.Boolean') -and -not $_.GetMethod.IsStatic })
            if ($property.Count -ne 1) { throw "Snapshot field mismatch: $($sample.type).$field" }
        }
    }
    if ($PluginDll) {
        $plugin = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $PluginDll).Path)
        $controller = if ($plan.controllerType) { $plan.controllerType } else { 'KairoMods.DreamTownIsland.GameObservation' }
        $observer = $plugin.MainModule.GetType($controller)
        $hookNames = if ($plan.controllerType) { @('Before','BeforeItemUse','BeforeSetMoney','BeforeLoad','FinishLoad','After','FinalizeCall') } else { @('Before','BeforeStatic','After','FinalizeCall') }
        foreach ($name in $hookNames) {
            $hook = @($observer.Methods | Where-Object Name -CEQ $name)
            if ($hook.Count -ne 1 -or @($hook[0].Parameters | Where-Object Name -CEQ '__result').Count -gt 0) { throw "Invalid observation hook: $name" }
        }
        $resource = if ($plan.controllerType) { 'ControlPlan.json' } else { 'ObservationPlan.json' }
        if (@($plugin.MainModule.Resources | Where-Object Name -CEQ $resource).Count -ne 1) { throw 'Missing embedded hook plan' }
    }
    Write-Output "PASS: $($plan.hooks.Count) exact target signatures, balance readers and $($plan.samples.Count) snapshot groups. No result mutation; runtime installation still requires game testing."
} finally { $assembly.Dispose(); if ($plugin) { $plugin.Dispose() } }
