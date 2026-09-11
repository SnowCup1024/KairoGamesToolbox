[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$TestGameDirectory, [switch]$Deploy)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$testRoot = (Resolve-Path -LiteralPath $TestGameDirectory).Path
if (-not (Test-Path -LiteralPath (Join-Path $testRoot 'KairoGames.exe'))) { throw '测试目录缺少 KairoGames.exe。' }
if ($Deploy -and (Get-Process KairoGames -ErrorAction SilentlyContinue)) { throw '请退出游戏后再部署。' }
dotnet build (Join-Path $repositoryRoot 'Mods/Games/DoraemonDorayakiShopStory/Source/KairoMods.Observer.csproj') -c Release "-p:TestGameDirectory=$testRoot"
if ($LASTEXITCODE -ne 0) { throw '模组构建失败。' }
if ($Deploy) {
    $destination = Join-Path $testRoot 'BepInEx/plugins/KairoMods.Observer'
    New-Item -ItemType Directory -Force $destination | Out-Null
    Copy-Item -LiteralPath (Join-Path $repositoryRoot '.Build/Output/KairoMods.Observer/Release/net6.0/KairoMods.Observer.dll') -Destination $destination -Force
}
Write-Host '构建完成；未打包，也未启动游戏。'
