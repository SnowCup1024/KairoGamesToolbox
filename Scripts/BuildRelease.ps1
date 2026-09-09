[CmdletBinding()]
param(
    [ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$')]
    [string]$Version = '0.1.3',
    [switch]$SkipLaunchCheck
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$windowsRoot = Join-Path $repositoryRoot 'Windows'
$solution = Join-Path $windowsRoot 'KairosoftGameToolbox.sln'
$project = Join-Path $windowsRoot 'Launcher\KairosoftGameToolbox.csproj'
$testProject = Join-Path $windowsRoot 'Test\LauncherSmokeTest.csproj'
$buildRoot = Join-Path $repositoryRoot '.Build'
$publishRoot = Join-Path $buildRoot 'Publish'
$packageRoot = Join-Path $buildRoot 'Package'
$releaseName = "KairosoftGameToolbox-v$Version-win-x64"
$publishDirectory = Join-Path $publishRoot "v$Version"
$stagingDirectory = Join-Path $publishRoot ".$releaseName-$PID"
$packagePath = Join-Path $packageRoot "$releaseName.zip"
$temporaryPackagePath = Join-Path $packageRoot ".$releaseName-$PID.zip"

function Assert-BuildChildPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $resolvedBuildRoot = [IO.Path]::GetFullPath($buildRoot).TrimEnd('\')
    $resolvedPath = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    if ($resolvedPath -eq $resolvedBuildRoot -or
        -not $resolvedPath.StartsWith($resolvedBuildRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝操作 .Build 外部或 .Build 根目录：$resolvedPath"
    }
}

function Remove-GeneratedPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    Assert-BuildChildPath -Path $Path
    for ($attempt = 1; $attempt -le 20; $attempt++) {
        if (-not (Test-Path -LiteralPath $Path)) {
            return
        }
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force
            return
        }
        catch {
            if ($attempt -eq 20) {
                throw
            }
            Start-Sleep -Milliseconds 250
        }
    }
}

function Invoke-DotNet {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments[0]) 失败，退出码 $LASTEXITCODE。"
    }
}

function Assert-X64Executable {
    param([Parameter(Mandatory = $true)][string]$Path)

    $stream = [IO.File]::OpenRead($Path)
    try {
        $reader = [IO.BinaryReader]::new($stream)
        if ($reader.ReadUInt16() -ne 0x5A4D) {
            throw '发布文件不是有效的 Windows PE 文件。'
        }
        $stream.Position = 0x3C
        $peOffset = $reader.ReadInt32()
        if ($peOffset -lt 0 -or $peOffset + 6 -gt $stream.Length) {
            throw '发布文件的 PE 头偏移无效。'
        }
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550) {
            throw '发布文件的 PE 标记无效。'
        }
        if ($reader.ReadUInt16() -ne 0x8664) {
            throw '发布文件不是 x64 可执行文件。'
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Test-ApplicationLaunch {
    param([Parameter(Mandatory = $true)][string]$Path)

    $process = Start-Process -FilePath $Path -WorkingDirectory (Split-Path -Parent $Path) -PassThru -WindowStyle Hidden
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(20)
        while ([DateTime]::UtcNow -lt $deadline) {
            Start-Sleep -Milliseconds 250
            $process.Refresh()
            if ($process.HasExited) {
                throw "启动器启动后退出，退出码 $($process.ExitCode)。"
            }
            if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
                return
            }
        }
        throw '启动器在 20 秒内没有创建主窗口。'
    }
    finally {
        if (-not $process.HasExited) {
            $process.Kill($true)
            if (-not $process.WaitForExit(10000)) {
                throw '无法在 10 秒内关闭启动验证进程。'
            }
        }
        $process.Dispose()
    }
}

foreach ($requiredPath in @($solution, $project, $testProject)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "缺少构建输入：$requiredPath"
    }
}

New-Item -ItemType Directory -Path $publishRoot, $packageRoot -Force | Out-Null
Get-ChildItem -LiteralPath $publishRoot -Directory -Force |
    Where-Object { $_.Name.StartsWith(".$releaseName-", [StringComparison]::Ordinal) } |
    ForEach-Object { Remove-GeneratedPath -Path $_.FullName }
Get-ChildItem -LiteralPath $packageRoot -File -Force |
    Where-Object {
        $_.Name.StartsWith(".$releaseName-", [StringComparison]::Ordinal) -and
        $_.Extension -eq '.zip'
    } |
    ForEach-Object { Remove-GeneratedPath -Path $_.FullName }
Remove-GeneratedPath -Path $stagingDirectory
Remove-GeneratedPath -Path $publishDirectory
Remove-GeneratedPath -Path $temporaryPackagePath
Remove-GeneratedPath -Path $packagePath

try {
    Invoke-DotNet -Arguments @('restore', $solution)
    Invoke-DotNet -Arguments @(
        'build', $solution,
        '--configuration', 'Release',
        '--no-restore',
        '--verbosity', 'minimal'
    )
    Invoke-DotNet -Arguments @(
        'run',
        '--project', $testProject,
        '--configuration', 'Release',
        '--no-build'
    )
    Invoke-DotNet -Arguments @(
        'publish', $project,
        '--configuration', 'Release',
        '--runtime', 'win-x64',
        '--self-contained', 'true',
        '--no-restore',
        '-p:Platform=x64',
        '-p:WindowsAppSDKSelfContained=true',
        '-p:PublishSingleFile=true',
        '-p:EnableMsixTooling=true',
        '-p:IncludeAllContentForSelfExtract=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:PublishReadyToRun=false',
        '-p:PublishTrimmed=false',
        '-p:DebugSymbols=false',
        '-p:DebugType=None',
        "-p:Version=$Version",
        "-p:AssemblyVersion=$Version",
        "-p:FileVersion=$Version",
        "-p:InformationalVersion=$Version",
        "-p:PublishDir=$stagingDirectory\"
    )

    $publishedFiles = @(Get-ChildItem -LiteralPath $stagingDirectory -Recurse -File -Force)
    $publishedDirectories = @(Get-ChildItem -LiteralPath $stagingDirectory -Recurse -Directory -Force)
    if ($publishedFiles.Count -ne 1 -or
        $publishedDirectories.Count -ne 0 -or
        $publishedFiles[0].Name -ne 'KairosoftGameToolbox.exe') {
        throw '发布目录必须只包含 KairosoftGameToolbox.exe。'
    }

    $application = $publishedFiles[0].FullName
    $versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($application)
    if ($versionInfo.FileVersion -notlike "$Version*") {
        throw "启动器文件版本为 $($versionInfo.FileVersion)，预期为 $Version。"
    }
    Assert-X64Executable -Path $application

    if (-not $SkipLaunchCheck) {
        Test-ApplicationLaunch -Path $application
    }

    Move-Item -LiteralPath $stagingDirectory -Destination $publishDirectory
    $application = Join-Path $publishDirectory 'KairosoftGameToolbox.exe'
    Compress-Archive -LiteralPath $application -DestinationPath $temporaryPackagePath -CompressionLevel Optimal

    $archive = [IO.Compression.ZipFile]::OpenRead($temporaryPackagePath)
    try {
        $entries = @($archive.Entries)
        if ($entries.Count -ne 1 -or $entries[0].FullName -ne 'KairosoftGameToolbox.exe') {
            throw 'ZIP 必须只包含 KairosoftGameToolbox.exe。'
        }
    }
    finally {
        $archive.Dispose()
    }

    Move-Item -LiteralPath $temporaryPackagePath -Destination $packagePath
    $hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
    Write-Host "发布完成：$packagePath"
    Write-Host "SHA256：$hash"
}
finally {
    Remove-GeneratedPath -Path $stagingDirectory
    Remove-GeneratedPath -Path $temporaryPackagePath
}
