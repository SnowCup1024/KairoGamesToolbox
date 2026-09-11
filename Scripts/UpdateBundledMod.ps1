[CmdletBinding()]
param([string]$TestGameDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) '.TestGames'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'BuildObserver.ps1') -TestGameDirectory $TestGameDirectory
$plugin = Join-Path $root '.Build/Output/KairoMods.Observer/Release/net6.0/KairoMods.Observer.dll'
& (Join-Path $PSScriptRoot 'TestObserverSignatures.ps1') -TestGameDirectory $TestGameDirectory -PluginDll $plugin
$gameRoot = Join-Path $root 'Mods/Games/DoraemonDorayakiShopStory'
$definitionFile = Join-Path $gameRoot 'definition.json'
$definition = Get-Content -LiteralPath $definitionFile -Raw | ConvertFrom-Json
$project = [xml](Get-Content -LiteralPath (Join-Path $gameRoot 'Source/KairoMods.Observer.csproj') -Raw)
$definition.version = [string]$project.Project.PropertyGroup.Version
if ($definition.version -notmatch '^\d+\.\d+\.[1-9]\d*$') { throw 'z must be greater than zero' }
foreach ($file in $definition.files) {
    if ($file.path -ne 'BepInEx/plugins/KairoMods.Observer/KairoMods.Observer.dll') { throw 'Unexpected bundled executable' }
    $destination = Join-Path (Join-Path $gameRoot 'Payload') $file.path
    New-Item -ItemType Directory -Force (Split-Path $destination) | Out-Null
    Copy-Item -LiteralPath $plugin -Destination $destination -Force
    $file.sha256 = (Get-FileHash -LiteralPath $destination).Hash
}
# A game fingerprint update requires deliberate review, never silently bless a new game build.
foreach ($target in $definition.targets) {
    if ((Get-FileHash -LiteralPath (Join-Path $TestGameDirectory $target.path)).Hash -ne $target.sha256) {
        throw "Game fingerprint changed: $($target.path). Review compatibility before updating definition.json."
    }
}
$definition | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $definitionFile -Encoding utf8
Write-Host 'Bundled game DLL and hash updated. No game launched, installed, or packaged.'
