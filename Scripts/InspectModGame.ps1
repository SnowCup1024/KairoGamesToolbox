[CmdletBinding()]
param([Parameter(Mandatory)][string]$GameDirectory)
$ErrorActionPreference = 'Stop'
$gameRoot = (Resolve-Path -LiteralPath $GameDirectory).Path
function Read-Machine([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        if ($reader.ReadUInt16() -ne 0x5A4D) { throw "Not a PE file: $Path" }
        $stream.Position = 0x3C
        $offset = $reader.ReadInt32()
        if ($offset -lt 0 -or $offset -gt $stream.Length - 6) { throw 'Invalid PE offset' }
        $stream.Position = $offset
        if ($reader.ReadUInt32() -ne 0x4550) { throw 'Invalid PE signature' }
        switch ($reader.ReadUInt16()) {
            0x014c { 'x86' }
            0x8664 { 'x64' }
            default { throw 'Unsupported PE machine' }
        }
    } finally { $reader.Dispose() }
}
$exe = Join-Path $gameRoot 'KairoGames.exe'
$assembly = Join-Path $gameRoot 'GameAssembly.dll'
$metadata = Join-Path $gameRoot 'KairoGames_Data/il2cpp_data/Metadata/global-metadata.dat'
$info = @(Get-Content -LiteralPath (Join-Path $gameRoot 'KairoGames_Data/app.info'))
if ($info.Count -lt 2 -or $info[0].Trim() -ne 'Kairosoft') { throw 'Unrecognized app.info publisher' }
$architecture = Read-Machine $exe
if (-not (Test-Path -LiteralPath $assembly) -or -not (Test-Path -LiteralPath $metadata)) {
    throw 'This inspector supports IL2CPP only; inspect Mono separately.'
}
if ((Read-Machine $assembly) -ne $architecture) { throw 'EXE and GameAssembly architectures differ' }
$stream = [IO.File]::OpenRead($metadata)
$reader = [IO.BinaryReader]::new($stream)
try {
    if ($reader.ReadUInt32() -ne [Convert]::ToUInt32('FAB11BAF', 16)) { throw 'Invalid IL2CPP metadata header' }
    $metadataVersion = $reader.ReadInt32()
} finally { $reader.Dispose() }
[ordered]@{
    product = $info[1].Trim()
    runtime = 'IL2CPP'
    architecture = $architecture
    unity = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $gameRoot 'UnityPlayer.dll')).ProductVersion
    metadataVersion = $metadataVersion
    targets = @('GameAssembly.dll', 'KairoGames_Data/il2cpp_data/Metadata/global-metadata.dat') | ForEach-Object {
        @{ path = $_; sha256 = (Get-FileHash -LiteralPath (Join-Path $gameRoot $_) -Algorithm SHA256).Hash }
    }
    interopAvailable = Test-Path -LiteralPath (Join-Path $gameRoot 'BepInEx/interop/Assembly-CSharp.dll')
} | ConvertTo-Json -Depth 5
