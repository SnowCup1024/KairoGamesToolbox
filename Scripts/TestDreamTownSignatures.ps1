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
    if ($plan.controllerType -cne 'KairoMods.DreamTownIsland.GameControl') { throw '只支持都市岛正式控制计划。' }
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
    if ($PluginDll) {
        $plugin = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $PluginDll).Path)
        $controller = 'KairoMods.DreamTownIsland.GameControl'
        $controllerType = $plugin.MainModule.GetType($controller)
        $hookNames = @('Before','BeforeItemUse','BeforeSetMoney','BeforeLoad','FinishLoad','After','FinalizeCall')
        foreach ($name in $hookNames) {
            $hook = @($controllerType.Methods | Where-Object Name -CEQ $name)
            if ($hook.Count -ne 1 -or @($hook[0].Parameters | Where-Object Name -CEQ '__result').Count -gt 0) { throw "控制钩子签名无效： $name" }
        }
        $resource = 'ControlPlan.json'
        if (@($plugin.MainModule.Resources | Where-Object Name -CEQ $resource).Count -ne 1) { throw 'Missing embedded hook plan' }
    }
    Write-Output "通过：$($plan.hooks.Count) 个实际目标签名、余额读取器与上限；静态检查不代替游戏内验证。"
} finally { $assembly.Dispose(); if ($plugin) { $plugin.Dispose() } }
