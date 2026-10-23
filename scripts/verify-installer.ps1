#Requires -Version 7.2
param([Parameter(Mandatory)][string]$ReleaseDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$releaseRoot = [IO.Path]::GetFullPath($ReleaseDirectory)
$setup = @(Get-ChildItem -LiteralPath $releaseRoot -Filter '*-Setup.exe')
if ($setup.Count -ne 1) { throw '应有且仅有一个安装包。' }
$registry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{A73DF38C-55D1-48F8-B8C9-5A5D3D50C28B}_is1'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'md2word'
if ((Test-Path $registry) -or (Test-Path $shortcut)) { throw '当前用户已安装md2word或存在同名开始菜单目录，停止检查以保护现有安装。' }
$work = Join-Path $root ('artifacts/installer-checks/' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$app = Join-Path $work '安装 程序'
$output = Join-Path $work '用户 文档'
$settingsDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'md2word'
$settingsFile = Join-Path $settingsDirectory 'settings.json'
$hadSettings = Test-Path -LiteralPath $settingsFile
$savedSettings = if ($hadSettings) { [IO.File]::ReadAllBytes($settingsFile) } else { $null }
$records = [Collections.Generic.List[object]]::new()
New-Item -ItemType Directory -Path $work -Force | Out-Null
$runningApp = $null
$finished = $false
$installedBytes = 0
function Check([string]$Name, [bool]$Passed, $Detail) {
    $records.Add([ordered]@{ name = $Name; passed = $Passed; detail = $Detail })
    if (-not $Passed) { throw "安装检查失败：$Name" }
}
function Run-Hidden([string]$Executable, [string[]]$Arguments, [int]$Seconds = 90) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.Environment['DOTNET_ROOT'] = Join-Path $work '不存在的运行时'
    $start.Environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (-not $process.WaitForExit($Seconds * 1000)) { $process.Kill($true); throw '本检查启动的进程超时。' }
        return $process.ExitCode
    } finally { $process.Dispose() }
}
function Install([string]$Log) {
    Run-Hidden $setup[0].FullName @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/CURRENTUSER', "/DIR=$app", "/LOG=$work/$Log")
}
function Get-TestUninstaller {
    if (-not (Test-Path $registry)) { return $null }
    $path = [IO.Path]::GetFullPath((Get-ItemProperty $registry).UninstallString.Trim('"'))
    if (-not $path.StartsWith($app + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($path) -notmatch '^unins\d+\.exe$') { throw '卸载路径不属于本检查安装。' }
    return $path
}
try {
    Check '候选包未签名状态如实记录' ((Get-AuthenticodeSignature $setup[0].FullName).Status -eq 'NotSigned') '当前候选不声称有代码签名。'
    Check '当前用户首次安装' ((Install 'install.log') -eq 0) $app
    Check '注册为当前用户安装' (Test-Path $registry) 'HKCU卸载记录'
    Check '创建开始菜单入口' (Test-Path (Join-Path $shortcut 'Markdown转Word.lnk')) $shortcut
    Check '安装模式不含便携标记' (-not (Test-Path (Join-Path $app 'portable.flag'))) '设置使用LocalAppData'
    $installedBytes = [long]((Get-ChildItem -LiteralPath $app -Recurse -File | Measure-Object Length -Sum).Sum)
    $manifest = Get-Content (Join-Path $app 'FILES.json') -Raw | ConvertFrom-Json
    $mismatches = @($manifest | Where-Object { -not (Test-Path (Join-Path $app $_.path)) -or (Get-FileHash (Join-Path $app $_.path) -Algorithm SHA256).Hash -ne $_.sha256 })
    Check '安装文件摘要与发布目录一致' ($mismatches.Count -eq 0) @($mismatches.path)
    New-Item -ItemType Directory -Path $settingsDirectory -Force | Out-Null
    $settings = [ordered]@{ Template = 'CnGeneral'; UseFirstHeadingAsTitle = $false; UseCustomOutput = $true; OutputDirectory = $output }
    $settings | ConvertTo-Json | Set-Content -LiteralPath $settingsFile -Encoding utf8
    # 隐藏探测窗口应返回3；这验证可启动，不冒充人工窗口交互。
    Check '安装后使用包内运行时启动' ((Run-Hidden (Join-Path $app 'Md2Word.Desktop.exe') @('--smoke-test')) -eq 3) '精简运行时路径，隐藏窗口探测。'
    Check '标题及输出设置保持' ((Get-Content $settingsFile -Raw | ConvertFrom-Json).UseFirstHeadingAsTitle -eq $false) $null
    $before = Get-FileHash $settingsFile -Algorithm SHA256
    Check '相同版本覆盖安装' ((Install 'reinstall.log') -eq 0) $null
    Check '覆盖安装保留设置' ((Get-FileHash $settingsFile -Algorithm SHA256).Hash -eq $before.Hash) $null
    $source = Join-Path $work '工作 示例.md'
    Copy-Item -LiteralPath (Join-Path $root 'examples/showcase/work-report.md') -Destination $source
    $sourceHash = (Get-FileHash $source -Algorithm SHA256).Hash
    Check '安装后真实转换' ((Run-Hidden (Join-Path $app 'Md2Word.Cli.exe') @($source, '--template', 'cn-general', '--output', $output)) -eq 0) $null
    $document = Join-Path $output '工作 示例.docx'
    Check '生成可读取的DOCX容器' (Test-Path $document) $document
    $zip = [IO.Compression.ZipFile]::OpenRead($document)
    try {
        $reader = [IO.StreamReader]::new($zip.GetEntry('word/document.xml').Open())
        try { [xml]$documentXml = $reader.ReadToEnd() } finally { $reader.Dispose() }
        Check '文档正文与表格存在' ($documentXml.DocumentElement.InnerText.Contains('协作文档库试运行周报') -and $documentXml.SelectNodes('//*[local-name()="tbl"]').Count -gt 0) '结构检查不等于Word排版验收。'
    } finally { $zip.Dispose() }
    $documentHash = (Get-FileHash $document -Algorithm SHA256).Hash
    $insideDocument = Join-Path $app '用户自行保存.docx'
    Copy-Item -LiteralPath $document -Destination $insideDocument
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $app 'Md2Word.Desktop.exe'))
    $start.UseShellExecute = $false
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $runningApp = [Diagnostics.Process]::Start($start)
    if (-not $runningApp.WaitForInputIdle(15000)) { throw '运行实例未就绪。' }
    $mutex = [Threading.Mutex]::OpenExisting('Local\md2word-desktop-running')
    try { Check '程序运行时阻止覆盖安装' ((Install 'running-block.log') -ne 0) '未强制关闭应用。' } finally { $mutex.Dispose() }
    [void]$runningApp.CloseMainWindow()
    # 隐藏窗口没有可供CloseMainWindow定位的主窗口；仅结束本检查启动的空闲探测实例。
    if (-not $runningApp.WaitForExit(1000)) { $runningApp.Kill(); $runningApp.WaitForExit() }
    $runningApp.Dispose(); $runningApp = $null
    Check '卸载成功' ((Run-Hidden (Get-TestUninstaller) @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=$work/uninstall.log")) -eq 0) $null
    Check '卸载移除程序与菜单' (-not (Test-Path (Join-Path $app 'Md2Word.Desktop.exe')) -and -not (Test-Path $shortcut) -and -not (Test-Path $registry)) $null
    Check '卸载保留用户设置' ((Get-Content $settingsFile -Raw | ConvertFrom-Json).OutputDirectory -eq $output) $null
    Check '卸载保留源文件和结果' ((Get-FileHash $source -Algorithm SHA256).Hash -eq $sourceHash -and (Get-FileHash $document -Algorithm SHA256).Hash -eq $documentHash) $null
    Check '卸载保留安装目录内的用户文档' ((Get-FileHash $insideDocument -Algorithm SHA256).Hash -eq $documentHash) $null
    Check '卸载后重新安装' ((Install 'after-uninstall.log') -eq 0) $null
    Check '重新安装保留设置' ((Get-Content $settingsFile -Raw | ConvertFrom-Json).OutputDirectory -eq $output) $null
    Check '检查完成后卸载测试实例' ((Run-Hidden (Get-TestUninstaller) @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')) -eq 0) $null
    $finished = $true
} finally {
    if ($runningApp -and -not $runningApp.HasExited) { [void]$runningApp.CloseMainWindow(); if (-not $runningApp.WaitForExit(10000)) { $runningApp.Kill($true) } }
    $uninstaller = Get-TestUninstaller
    if ($uninstaller -and (Test-Path -LiteralPath $uninstaller)) { [void](Run-Hidden $uninstaller @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')) }
    if ($hadSettings) { [IO.File]::WriteAllBytes($settingsFile, $savedSettings) } elseif (Test-Path $settingsFile) { Remove-Item -LiteralPath $settingsFile }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $isAdmin = ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    [ordered]@{ checkedAt = (Get-Date).ToUniversalTime().ToString('o'); installerSha256 = (Get-FileHash $setup[0].FullName -Algorithm SHA256).Hash; verificationScriptSha256 = (Get-FileHash $PSCommandPath -Algorithm SHA256).Hash; work = $work; allPassed = $finished; installedBytes = $installedBytes; elevatedAccount = $isAdmin; checks = $records; cleanMachineVerified = $false; actualNetworkDisconnected = $false; windows11Verified = $false; olderVersionUpgradeVerified = $false; manualInteractionVerified = $false } | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $releaseRoot 'installer-verification.json') -Encoding utf8
}
Write-Output "本机安装检查通过：$releaseRoot"
