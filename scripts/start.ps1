#Requires -Version 7.2
param([switch]$SmokeTest)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$application = Join-Path $projectRoot 'src/Md2Word.Desktop/bin/Release/net10.0-windows/Md2Word.Desktop.exe'
if (-not (Test-Path -LiteralPath $application)) { throw '请先运行scripts/build.ps1构建工程。' }
$env:DOTNET_ROOT = Join-Path $projectRoot '.tools/dotnet'
$engine = Join-Path $projectRoot 'pandoc-3.10/pandoc.exe'
$arguments = @('--pandoc', ('"' + $engine + '"'))
if ($SmokeTest) { $arguments += '--smoke-test' }
# 此进程是用户要操作的主窗口，必须正常显示。
$process = Start-Process -FilePath $application -ArgumentList $arguments -WorkingDirectory $projectRoot -WindowStyle Normal -PassThru
if ($SmokeTest) {
    if (-not $process.WaitForExit(15000)) { throw '桌面启动检查超时。' }
    $process.Refresh()
    if ($process.ExitCode -ne 0) { throw "桌面启动检查失败，退出码$($process.ExitCode)。退出码3表示窗口未正常显示。" }
}
else {
    $startup = [Diagnostics.Stopwatch]::StartNew()
    do {
        $process.Refresh()
        if ($process.HasExited) { throw "程序启动后已退出，退出码$($process.ExitCode)。" }
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 100
    } while ($startup.ElapsedMilliseconds -lt 15000)
    if ($process.MainWindowHandle -eq [IntPtr]::Zero) { throw '程序已启动，但未找到可见窗口，请记录此提示以便排查。' }
}