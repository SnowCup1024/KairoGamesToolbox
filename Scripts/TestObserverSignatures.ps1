[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$TestGameDirectory,
    [Parameter(Mandatory)][string]$PluginDll
)
$ErrorActionPreference = 'Stop'
Add-Type -LiteralPath (Join-Path $TestGameDirectory 'BepInEx/core/Mono.Cecil.dll')
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $TestGameDirectory 'BepInEx/interop/Assembly-CSharp.dll'))
$plugin = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $PluginDll).Path)
try {
    $observer = $plugin.MainModule.GetType('KairoMods.Observer.GameObservation')
    foreach ($name in @('AfterVoid')) {
        $hook = @($observer.Methods | Where-Object Name -EQ $name)
        if ($hook.Count -ne 1 -or @($hook[0].Parameters | Where-Object Name -EQ '__result').Count -ne 0) {
            throw "$name must support void methods without requesting __result"
        }
    }
    $targets = @(
        @('ui.AppData','AddMoney','System.Int64','System.Int64'),
        @('ui.AppData','SubMoney','System.Int64','System.Int64,System.Boolean'),
        @('ui.AppData','AddFPoint','System.Int64','System.Int64'),
        @('ui.AppData','SubFPoint','System.Int64','System.Int64,System.Boolean'),
        @('ui.AppData','AddCoinPoint','System.Int64','System.Int64'),
        @('ui.AppData','SubCoin','System.Int64','System.Int64,System.Boolean'),
        @('data.Character','AddHeartPoint','System.Void','System.Int32'),
        @('data.Character','SubHeartPoint','System.Void','System.Int32'),
        @('data.ItemData','AddStock','System.Void','System.Int32'),
        @('data.ItemData','SubStock','System.Void','System.Int32'),
        @('data.Character','EquipItem','System.Void','data.ItemData,System.Int32')
    )
    foreach ($target in $targets) {
        $methods = @($game.MainModule.GetType($target[0]).Methods | Where-Object {
            $_.Name -eq $target[1] -and $_.ReturnType.FullName -eq $target[2] -and
            (($_.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ',') -eq $target[3] -and -not $_.IsStatic
        })
        if ($methods.Count -ne 1) { throw "Signature mismatch: $($target -join ' ')" }
    }
    $longHook = @($observer.Methods | Where-Object Name -EQ 'AfterLong')
    if ($longHook.Count -ne 1 -or @($longHook[0].Parameters | Where-Object { $_.Name -eq '__result' -and $_.ParameterType.FullName -eq 'System.Int64&' }).Count -ne 1) { throw 'Long result hook mismatch' }
    Write-Output 'PASS: 11 game target signatures; void and long postfixes are separate.'
    Write-Output 'Static metadata check only; IL2CPP runtime hook installation still requires manual testing.'
} finally {
    $game.Dispose()
    $plugin.Dispose()
}
