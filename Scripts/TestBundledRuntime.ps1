[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$definition = Get-Content -LiteralPath (Join-Path $root 'Mods/Games/DoraemonDorayakiShopStory/definition.json') -Raw | ConvertFrom-Json
$directory = Join-Path $root '.Build/Dependencies'
New-Item -ItemType Directory -Force $directory | Out-Null
$path = Join-Path $directory ($definition.runtime.id + '.zip')
if (-not (Test-Path -LiteralPath $path)) { Invoke-WebRequest -Uri $definition.runtime.url -OutFile $path -TimeoutSec 180 }
if ((Get-FileHash -LiteralPath $path).Hash -ne $definition.runtime.sha256) { throw 'Runtime SHA256 mismatch' }
$archive = [IO.Compression.ZipFile]::OpenRead($path)
try {
    if (-not $archive.GetEntry('winhttp.dll') -or -not $archive.GetEntry('dotnet/coreclr.dll') -or -not $archive.GetEntry('BepInEx/core/BepInEx.Unity.IL2CPP.dll')) { throw 'Runtime layout mismatch' }
    foreach ($entry in $archive.Entries) {
        if ($entry.FullName -match '(^/|\.\.|:|\\)' -or $entry.FullName -match '(GameAssembly\.dll|global-metadata\.dat|KairoGames\.exe)$') { throw 'Unexpected runtime entry' }
    }
    Write-Host "PASS: official runtime download, SHA256 and $($archive.Entries.Count) entries verified."
} finally { $archive.Dispose() }
dotnet run --project (Join-Path $root 'Mods/ControlTests/ControlTests.csproj') -c Release -- --verify-runtime $path
if ($LASTEXITCODE -ne 0) { throw 'Runtime composition test failed' }
