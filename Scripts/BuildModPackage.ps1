[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$LoaderZip,
    [Parameter(Mandatory)][string]$PluginDll,
    [Parameter(Mandatory)][string]$GameDirectory,
    [Parameter(Mandatory)][string]$LicenseDirectory,
    [uint32]$AppId = 2934180,
    [ValidatePattern('^[A-Za-z0-9]+$')][string]$GameFolder = 'DoraemonDorayakiShopStory',
    [ValidateSet('Alpha','Beta','Stable')][string]$Channel = 'Alpha',
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.0.3'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$gameMods = Join-Path $root "Mods/$GameFolder"
New-Item -ItemType Directory -Force "$gameMods/Alpha", "$gameMods/Beta" | Out-Null
$destination = if ($Channel -eq 'Stable') { $gameMods } else { Join-Path $gameMods $Channel }
$archivePath = Join-Path $destination "$GameFolder-$Version.zip"
if (Test-Path -LiteralPath $archivePath) { throw '目标包已存在；请使用新版本或先人工移走旧包。' }
$targets = @('GameAssembly.dll', 'KairoGames_Data/il2cpp_data/Metadata/global-metadata.dat') | ForEach-Object {
    @{ path = $_; sha256 = (Get-FileHash -LiteralPath (Join-Path $GameDirectory $_)).Hash }
}
$files = [Collections.Generic.List[object]]::new()
$loader = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $LoaderZip))
$zip = [IO.Compression.ZipFile]::Open($archivePath, [IO.Compression.ZipArchiveMode]::Create)
function Add-Payload([string]$Name, [IO.Stream]$Source) {
    $buffer = [IO.MemoryStream]::new()
    try {
        $Source.CopyTo($buffer)
        $bytes = $buffer.ToArray()
        $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
        $entry = $zip.CreateEntry("payload/$Name", [IO.Compression.CompressionLevel]::Optimal)
        $output = $entry.Open()
        try { $output.Write($bytes, 0, $bytes.Length) } finally { $output.Dispose() }
        $files.Add(@{ path = $Name; sha256 = $hash })
    } finally { $buffer.Dispose() }
}
try {
    foreach ($entry in $loader.Entries) {
        if ($entry.FullName.EndsWith('/')) { continue }
        $name = $entry.FullName
        if ($name -notmatch '^(BepInEx/core/|dotnet/)' -and $name -notin @('winhttp.dll','doorstop_config.ini','.doorstop_version','changelog.txt')) {
            throw "加载器归档含非预期文件：$name"
        }
        $inputStream = $entry.Open()
        try { Add-Payload $name $inputStream } finally { $inputStream.Dispose() }
    }
    $inputStream = [IO.File]::OpenRead((Resolve-Path -LiteralPath $PluginDll))
    try { Add-Payload 'BepInEx/plugins/KairoMods.Observer/KairoMods.Observer.dll' $inputStream } finally { $inputStream.Dispose() }
    foreach ($license in Get-ChildItem -LiteralPath $LicenseDirectory -File) {
        $inputStream = $license.OpenRead()
        try { Add-Payload "licenses/KairoMods/$($license.Name)" $inputStream } finally { $inputStream.Dispose() }
    }
    $manifest = @{
        schemaVersion = 1; appId = $AppId; gameFolder = $GameFolder; version = $Version; channel = $Channel
        description = 'Alpha 金钱观察模组。进入存档后按 F8 启用观察；仅记录消费和收入，不提供金钱反加或启动器实时开关。首次启动加载器可能需要联网生成互操作文件。'
        targets = @($targets); files = @($files.ToArray())
    }
    $writer = [IO.StreamWriter]::new($zip.CreateEntry('manifest.json').Open(), [Text.UTF8Encoding]::new($false))
    try { $writer.Write(($manifest | ConvertTo-Json -Depth 8)) } finally { $writer.Dispose() }
} finally { $zip.Dispose(); $loader.Dispose() }
Write-Host "模组包：$archivePath"
Get-FileHash -LiteralPath $archivePath
