#Requires -Version 7.2
param([switch]$DirectNetwork, [switch]$SkipTests, [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$TestReport = 'latest')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { throw '请先运行scripts/bootstrap.ps1准备项目SDK。' }
$env:DOTNET_ROOT = Split-Path -Parent $dotnet
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\cli-home'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$proxyBackup = @{}
foreach ($proxyName in @('HTTP_PROXY', 'HTTPS_PROXY', 'ALL_PROXY')) {
    $proxyBackup[$proxyName] = [Environment]::GetEnvironmentVariable($proxyName, 'Process')
    if ($DirectNetwork) { [Environment]::SetEnvironmentVariable($proxyName, $null, 'Process') }
}
Push-Location $projectRoot
try {
    & $dotnet restore 'Md2Word.slnx' --locked-mode -m:1
    if ($LASTEXITCODE -ne 0) { throw '依赖还原失败。' }
    & $dotnet build 'Md2Word.slnx' --no-restore -c Release -m:1
    if ($LASTEXITCODE -ne 0) { throw '工程构建失败。' }
    if (-not $SkipTests) {
        & $dotnet test 'tests/Md2Word.Tests/Md2Word.Tests.csproj' --no-build -c Release --logger "trx;LogFileName=$TestReport.trx" --results-directory 'artifacts/test-results'
        if ($LASTEXITCODE -ne 0) { throw '测试未通过。' }
        & $dotnet test 'tests/Md2Word.Desktop.Tests/Md2Word.Desktop.Tests.csproj' --no-build -c Release --logger "trx;LogFileName=$TestReport-desktop.trx" --results-directory 'artifacts/test-results'
        if ($LASTEXITCODE -ne 0) { throw '桌面测试未通过。' }
        & (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File (Join-Path $PSScriptRoot 'verify-startup.ps1') -ReportPath "artifacts/test-results/$TestReport-startup.json"
        if ($LASTEXITCODE -ne 0) { throw '桌面启动检查未通过。' }
    }
}
finally {
    Pop-Location
    foreach ($proxyName in $proxyBackup.Keys) { [Environment]::SetEnvironmentVariable($proxyName, $proxyBackup[$proxyName], 'Process') }
}
