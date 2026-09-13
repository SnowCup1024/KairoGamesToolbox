[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$TestGameDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$gameRoot = Join-Path $root 'Mods/Games/DoraemonDorayakiShopStory'
$definitionFile = Join-Path $gameRoot 'definition.json'
$definition = Get-Content -LiteralPath $definitionFile -Raw | ConvertFrom-Json
$project = [xml](Get-Content -LiteralPath (Join-Path $gameRoot 'Source/KairoMods.DoraemonDorayakiShopStory.csproj') -Raw)
$releaseDate = [string]$project.Project.PropertyGroup.ModReleaseDate
$parsedDate = [datetime]::MinValue
if (-not [datetime]::TryParseExact($releaseDate, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$parsedDate) -or $releaseDate -cne $definition.releaseDate) { throw '模组项目与定义的发布日期必须一致且为有效的 YYYY-MM-DD。' }
# 在编译或替换载荷前核对游戏指纹，不自动接受未知版本。
foreach ($target in $definition.targets) {
    if ((Get-FileHash -LiteralPath (Join-Path $TestGameDirectory $target.path)).Hash -ne $target.sha256) {
        throw "游戏指纹变化，需先核对兼容性：$($target.path)"
    }
}
if (@($definition.files).Count -ne 1 -or $definition.files[0].path -cne 'BepInEx/plugins/KairoMods.Observer/KairoMods.Observer.dll') { throw '专用载荷清单不匹配。' }
& (Join-Path $PSScriptRoot 'BuildDoraemonMod.ps1') -TestGameDirectory $TestGameDirectory
$plugin = Join-Path $root '.Build/Output/KairoMods.DoraemonDorayakiShopStory/Release/net6.0/KairoMods.Observer.dll'
& (Join-Path $PSScriptRoot 'TestDoraemonSignatures.ps1') -TestGameDirectory $TestGameDirectory -PluginDll $plugin
$file = $definition.files[0]
$destination = Join-Path (Join-Path $gameRoot 'Payload') $file.path
New-Item -ItemType Directory -Force (Split-Path $destination) | Out-Null
Copy-Item -LiteralPath $plugin -Destination $destination -Force
$file.sha256 = (Get-FileHash -LiteralPath $destination).Hash
$definition | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $definitionFile -Encoding utf8
Write-Host '哆啦A梦专用 DLL 与哈希已更新；未启动游戏或部署。'
