[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9]+$')][string]$GameFolder,
    [Parameter(Mandatory)][string]$GameDirectory,
    [Parameter(Mandatory)][string]$RuntimeDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$gameRoot = Join-Path $root "Mods/Games/$GameFolder"
$definitionFile = Join-Path $gameRoot 'definition.json'
$definition = Get-Content -LiteralPath $definitionFile -Raw | ConvertFrom-Json
if ($definition.gameFolder -cne $GameFolder) { throw 'Game folder mismatch' }
# Reject changed commercial files before building or replacing a bundled payload.
foreach ($target in $definition.targets) {
    if ($target.path -notin @('GameAssembly.dll', 'KairoGames_Data/il2cpp_data/Metadata/global-metadata.dat')) { throw 'Unexpected target' }
    if ((Get-FileHash -LiteralPath (Join-Path $GameDirectory $target.path)).Hash -ne $target.sha256) { throw "Game fingerprint changed: $($target.path)" }
}
$projects = @(Get-ChildItem -LiteralPath (Join-Path $gameRoot 'Source') -Filter '*.csproj')
if ($projects.Count -ne 1) { throw 'Expected exactly one plugin project' }
$projectFile = $projects[0]
$project = [xml](Get-Content -LiteralPath $projectFile.FullName -Raw)
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.[1-9]\d*$' -or $version -ne $definition.version) { throw 'Project and definition versions must match; z must be positive' }
$assemblyName = $projectFile.BaseName
if ($assemblyName -notmatch '^KairoMods\.[A-Za-z0-9]+$') { throw 'Unsupported assembly name' }
$relativePayload = "BepInEx/plugins/$assemblyName/$assemblyName.dll"
if (@($definition.files).Count -ne 1 -or $definition.files[0].path -cne $relativePayload) { throw 'Expected one game-specific plugin payload' }
$runtimeRoot = (Resolve-Path -LiteralPath $RuntimeDirectory).Path
$observationPlan = Join-Path $projectFile.DirectoryName 'observation.json'
if (Test-Path -LiteralPath (Join-Path $projectFile.DirectoryName 'control.json')) { $observationPlan = Join-Path $projectFile.DirectoryName 'control.json' }
if (Test-Path -LiteralPath $observationPlan) {
    & (Join-Path $PSScriptRoot 'TestObservationSignatures.ps1') -GameDirectory $GameDirectory -PlanPath $observationPlan
}
dotnet build $projectFile.FullName -c Release "-p:RuntimeDirectory=$runtimeRoot" "-p:GameDirectory=$GameDirectory"
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed' }
$output = Join-Path $root ".Build/Output/$assemblyName/Release/net6.0/$assemblyName.dll"
if (Test-Path -LiteralPath $observationPlan) {
    & (Join-Path $PSScriptRoot 'TestObservationSignatures.ps1') -GameDirectory $GameDirectory -PlanPath $observationPlan -PluginDll $output
}
$destination = Join-Path (Join-Path $gameRoot 'Payload') $relativePayload
New-Item -ItemType Directory -Force (Split-Path $destination) | Out-Null
Copy-Item -LiteralPath $output -Destination $destination -Force
$definition.files[0].sha256 = (Get-FileHash -LiteralPath $destination).Hash
$definition | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $definitionFile -Encoding utf8
Write-Host "Updated $GameFolder payload and hash; did not deploy, launch, or validate gameplay hooks."
