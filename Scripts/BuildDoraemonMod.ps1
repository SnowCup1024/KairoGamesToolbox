[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$TestGameDirectory)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$testRoot = (Resolve-Path -LiteralPath $TestGameDirectory).Path
if (-not (Test-Path -LiteralPath (Join-Path $testRoot 'KairoGames.exe'))) { throw '测试目录缺少 KairoGames.exe。' }
dotnet build (Join-Path $repositoryRoot 'Mods/Games/DoraemonDorayakiShopStory/Source/KairoMods.DoraemonDorayakiShopStory.csproj') -c Release "-p:TestGameDirectory=$testRoot"
if ($LASTEXITCODE -ne 0) { throw '模组构建失败。' }
Write-Host '哆啦A梦模组构建完成；部署使用启动器事务安装。'
