param(
    [Parameter(Mandatory)][string[]]$InputFiles,
    [Parameter(Mandatory)][ValidateSet('cn-general','original')][string]$Template,
    [string]$OutputDirectory,
    [switch]$KeepHeadingLevels
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$cli = Join-Path $projectRoot 'src\Md2Word.Cli\bin\Release\net10.0\Md2Word.Cli.dll'
if (-not (Test-Path -LiteralPath $cli)) { throw '请先运行scripts/build.ps1构建工程。' }
$env:DOTNET_ROOT = Split-Path -Parent $dotnet
$cliArguments = @($cli) + $InputFiles + @('--template', $Template, '--pandoc', (Join-Path $projectRoot 'pandoc-3.10\pandoc.exe'))
if ($OutputDirectory) { $cliArguments += @('--output', $OutputDirectory) }
if ($KeepHeadingLevels) { $cliArguments += '--keep-heading-levels' }
& $dotnet @cliArguments
exit $LASTEXITCODE
