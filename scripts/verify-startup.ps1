#Requires -Version 7.2
param([string]$ReportPath = 'artifacts/test-results/startup.json')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$application = Join-Path $projectRoot 'src/Md2Word.Desktop/bin/Release/net10.0-windows/Md2Word.Desktop.exe'
$engine = Join-Path $projectRoot 'pandoc-3.10/pandoc.exe'
$env:DOTNET_ROOT = Join-Path $projectRoot '.tools/dotnet'
$records = @()
foreach ($style in @('Normal', 'Hidden')) {
    $process = Start-Process -FilePath $application -ArgumentList @('--smoke-test', '--pandoc', ('"' + $engine + '"')) -WindowStyle $style -PassThru
    if (-not $process.WaitForExit(15000)) {
        # 仅结束本检查刚刚启动的探测进程。
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        throw "启动检查超时：$style。"
    }
    $process.Refresh()
    $expected = if ($style -eq 'Normal') { 0 } else { 3 }
    $records += [pscustomobject]@{ windowStyle = $style; exitCode = $process.ExitCode; expectedExitCode = $expected; passed = ($process.ExitCode -eq $expected) }
}
& (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File (Join-Path $PSScriptRoot 'start.ps1') -SmokeTest
$launcherExit = $LASTEXITCODE
$reportFile = if ([IO.Path]::IsPathRooted($ReportPath)) { [IO.Path]::GetFullPath($ReportPath) } else { [IO.Path]::GetFullPath((Join-Path $projectRoot $ReportPath)) }
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($reportFile)) -Force | Out-Null
[pscustomobject]@{ probes = $records; launcherExitCode = $launcherExit; applicationSha256 = (Get-FileHash -LiteralPath $application -Algorithm SHA256).Hash } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $reportFile -Encoding UTF8
if (@($records | Where-Object { -not $_.passed }).Count -gt 0 -or $launcherExit -ne 0) { throw '窗口可见性或启动入口检查未通过，请查看启动检查报告。' }
Write-Output '窗口可见性与启动入口检查通过。'
