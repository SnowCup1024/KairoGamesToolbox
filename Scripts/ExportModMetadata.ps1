[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GameDirectory,
    [Parameter(Mandatory)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
# Cecil reads metadata only; it does not load or execute game types.
Add-Type -LiteralPath (Join-Path $GameDirectory 'BepInEx/core/Mono.Cecil.dll')
$assemblyPath = Join-Path $GameDirectory 'BepInEx/interop/Assembly-CSharp.dll'
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($assemblyPath)
function Read-Type($type) {
    [ordered]@{
        name = $type.FullName
        fields = @($type.Fields | Where-Object { $_.Name -notlike 'Native*Ptr*' } | ForEach-Object {
            @{ name = $_.Name; type = $_.FieldType.FullName; static = $_.IsStatic }
        })
        properties = @($type.Properties | ForEach-Object {
            @{ name = $_.Name; type = $_.PropertyType.FullName; static = ($_.GetMethod -and $_.GetMethod.IsStatic) }
        })
        methods = @($type.Methods | Where-Object { -not $_.IsConstructor } | ForEach-Object {
            @{ name = $_.Name; signature = $_.FullName; returns = $_.ReturnType.FullName; static = $_.IsStatic;
                parameters = @($_.Parameters | ForEach-Object { @{ name = $_.Name; type = $_.ParameterType.FullName } }) }
        })
    }
    foreach ($nested in $type.NestedTypes) { Read-Type $nested }
}
try {
    $report = [ordered]@{
        assemblySha256 = (Get-FileHash -LiteralPath $assemblyPath).Hash
        types = @($assembly.MainModule.Types | ForEach-Object { Read-Type $_ })
    }
    $output = [IO.Path]::GetFullPath($OutputPath)
    $build = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../.Build')) + [IO.Path]::DirectorySeparatorChar
    if (-not $output.StartsWith($build, [StringComparison]::OrdinalIgnoreCase)) { throw 'Metadata reports belong in .Build.' }
    New-Item -ItemType Directory -Force (Split-Path $output) | Out-Null
    $report | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath $output -Encoding utf8
    Write-Output "Exported $($report.types.Count) types to $output; no game code executed."
} finally { $assembly.Dispose() }
