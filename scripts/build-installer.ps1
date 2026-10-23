#Requires -Version 7.2
param(
    [Parameter(Mandatory)][string]$PayloadDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$AppVersion,
    [string]$AppFileVersion
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
[xml]$versions = Get-Content (Join-Path $root 'release/version.props') -Raw
if (-not $AppVersion) { $AppVersion = $versions.Project.PropertyGroup.Version }
if (-not $AppFileVersion) { $AppFileVersion = $versions.Project.PropertyGroup.FileVersion }
if ($AppVersion -notmatch '^\d+\.\d+\.\d+(-rc\.\d+)?$' -or $AppFileVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw '安装包版本格式无效。' }
$compiler = Join-Path $root '.tools/inno/ISCC.exe'
if (-not (Test-Path $compiler)) { throw '请先运行scripts/bootstrap-inno.ps1。' }
$actualVersion = & $compiler --version
$expected = (Get-Content (Join-Path $root 'release/inno.json') -Raw | ConvertFrom-Json).version
if (($actualVersion -join "`n").Trim() -ne $expected) { throw '安装包编译工具版本不符。' }
$payload = [IO.Path]::GetFullPath($PayloadDirectory)
if (Test-Path (Join-Path $payload 'portable.flag')) { throw '安装目录不能包含portable.flag，请从未加便携标记的发布目录构建。' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
& $compiler --no-ide-signtools "--define=AppSource=$payload" "--define=AppVersion=$AppVersion" "--define=AppFileVersion=$AppFileVersion" "--output-dir=$([IO.Path]::GetFullPath($OutputDirectory))" (Join-Path $root 'release/md2word.iss') *> (Join-Path $OutputDirectory 'installer-build.log')
if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $OutputDirectory 'installer-build.log') -Tail 25; throw '安装包编译失败。' }
Write-Output (Join-Path $OutputDirectory "md2word-$AppVersion-win-x64-Setup.exe")
