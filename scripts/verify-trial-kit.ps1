#Requires -Version 7.2
param([Parameter(Mandatory)][string]$KitDirectory, [Parameter(Mandatory)][string]$ReleaseDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$kit = [IO.Path]::GetFullPath($KitDirectory)
$release = [IO.Path]::GetFullPath($ReleaseDirectory)
$manifest = Get-Content (Join-Path $kit 'TRIAL.json') -Raw | ConvertFrom-Json
$payload = Join-Path $release ("md2word-$($manifest.version)-win-x64")
$work = Join-Path $root ('artifacts/trial-checks/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work -Force | Out-Null
$checks = [Collections.Generic.List[object]]::new()
$completed = $false
$collectorCheck = [ordered]@{ windowsPowerShell51Syntax = 'not-checked'; windowsPowerShell51Execution = 'not-checked'; actualRuntime = $null }
function Check([string]$Name, [bool]$Passed) {
    $checks.Add([ordered]@{ name = $Name; passed = $Passed })
    if (-not $Passed) { throw "试用准备检查失败：$Name" }
}
function Convert-Sample([string]$InputFile) {
    $info = [Diagnostics.ProcessStartInfo]::new((Join-Path $payload 'Md2Word.Cli.exe'))
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $info.RedirectStandardError = $true
    foreach ($arg in @($InputFile,'--template','cn-general','--output',(Join-Path $work '结果'))) { $info.ArgumentList.Add($arg) }
    $process = [Diagnostics.Process]::Start($info)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(45000)) { $process.Kill($true); throw '本次创建的CLI检查超时。' }
        $process.WaitForExit()
        $null = $stdout.GetAwaiter().GetResult()
        $null = $stderr.GetAwaiter().GetResult()
        return $process.ExitCode
    } finally { $process.Dispose() }
}
function Has-Media([string]$Document, [string]$ExpectedImage) {
    $zip = [IO.Compression.ZipFile]::OpenRead($Document)
    try {
        $expectedHash = (Get-FileHash $ExpectedImage -Algorithm SHA256).Hash
        foreach ($entry in $zip.Entries | Where-Object FullName -Like 'word/media/*') {
            $stream = $entry.Open()
            try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
            finally { $stream.Dispose() }
            if ($hash -eq $expectedHash) { return $true }
        }
        return $false
    } finally { $zip.Dispose() }
}
try {
    $kitName = Split-Path $kit -Leaf
    $bundle = Split-Path $kit -Parent
    $archives = @(Get-ChildItem -LiteralPath $bundle -Filter '*-试用准备.zip')
    Check '试用ZIP唯一且存在' ($archives.Count -eq 1)
    $archiveHash = (Get-FileHash $archives[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    Check '试用ZIP摘要一致' (@((Get-Content (Join-Path $bundle 'SHA256SUMS.txt')) | Where-Object { $_ -eq "$archiveHash  $($archives[0].Name)" }).Count -eq 1)
    $extracted = Join-Path $work '解压核对'
    Expand-Archive -LiteralPath $archives[0].FullName -DestinationPath $extracted
    $kit = Join-Path $extracted $kitName
    Check '实际ZIP可完整解压' (Test-Path (Join-Path $kit 'FILES.json'))
    $files = @(Get-Content (Join-Path $kit 'FILES.json') -Raw | ConvertFrom-Json)
    foreach ($file in $files) {
        $path = [IO.Path]::GetFullPath((Join-Path $kit $file.path))
        if (-not $path.StartsWith($kit.TrimEnd('\') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw '试用清单路径超出试用目录。' }
        Check ("文件完整：$($file.path)") ((Test-Path -LiteralPath $path -PathType Leaf) -and (Get-FileHash $path -Algorithm SHA256).Hash -eq $file.sha256)
    }
    Check '试用材料明确保留未验证状态' (-not $manifest.publicDistributionReady -and $manifest.actualUserTrials -eq 0 -and -not $manifest.cleanWindows10Verified -and -not $manifest.cleanWindows11Verified)
    Check '安装包与候选摘要一致' ((Get-FileHash (Join-Path $kit $manifest.installer) -Algorithm SHA256).Hash -eq $manifest.installerSha256)
    $feedback = @(Import-Csv -LiteralPath (Join-Path $kit '试用反馈.csv') -Encoding UTF8)
    Check '三份反馈均为未执行' ($feedback.Count -eq 3 -and @($feedback | Where-Object { $_.独立安装 -ne '待执行' -or $_.排版检查 -ne '待执行' }).Count -eq 0)
    Copy-Item -LiteralPath (Join-Path $kit '样例'),(Join-Path $kit '缺图练习'),(Join-Path $kit '修复用图片') -Destination $work -Recurse
    $originalHashes = @{}
    foreach ($file in Get-ChildItem $work -Filter '*.md' -Recurse -File) { $originalHashes[$file.FullName] = (Get-FileHash $file.FullName -Algorithm SHA256).Hash }
    foreach ($name in @('工作周报','表格','中英文与图片')) {
        Check ("候选CLI转换：$name") ((Convert-Sample (Join-Path $work "样例/$name.md")) -eq 0)
        Check ("生成DOCX：$name") (Test-Path (Join-Path $work "结果/$name.docx"))
    }
    Check '用户提供的介绍图原样嵌入' (Has-Media (Join-Path $work '结果/中英文与图片.docx') (Join-Path $work '样例/assets/md2word-cover.png'))
    $missing = Join-Path $work '缺图练习/图片说明.md'
    Check '缺图练习确实失败' ((Convert-Sample $missing) -eq 1)
    Check '缺图失败没有半成品' (-not (Test-Path (Join-Path $work '结果/图片说明.docx')))
    Copy-Item -LiteralPath (Join-Path $work '修复用图片/diagram.png') -Destination (Join-Path $work '缺图练习/assets')
    Check '按说明补回图片后可转换' ((Convert-Sample $missing) -eq 0)
    Check '修复图片原样嵌入' (Has-Media (Join-Path $work '结果/图片说明.docx') (Join-Path $work '修复用图片/diagram.png'))
    $firstResult = (Get-FileHash (Join-Path $work '结果/工作周报.docx') -Algorithm SHA256).Hash
    Check '再次转换另存新文件' ((Convert-Sample (Join-Path $work '样例/工作周报.md')) -eq 0 -and (Test-Path (Join-Path $work '结果/工作周报（2）.docx')))
    Check '旧结果摘要保持' ((Get-FileHash (Join-Path $work '结果/工作周报.docx') -Algorithm SHA256).Hash -eq $firstResult)
    Check '全部Markdown源文件未改动' (@($originalHashes.Keys | Where-Object { (Get-FileHash $_ -Algorithm SHA256).Hash -ne $originalHashes[$_] }).Count -eq 0)
    $snapshot = Join-Path $work 'environment.json'
    $collector = Join-Path $kit '验证人员/收集环境.ps1'
    $parseScript = @'
$parseErrors = $null
$null = [System.Management.Automation.Language.Parser]::ParseFile('__SOURCE_PATH__', [ref]$null, [ref]$parseErrors)
if ($parseErrors.Count -gt 0) { $parseErrors | Out-String | Write-Output; exit 1 }
'@
    & powershell.exe -NoProfile -Command ($parseScript.Replace('__SOURCE_PATH__', $collector.Replace("'", "''")))
    Check 'Windows PowerShell5.1语法解析' ($LASTEXITCODE -eq 0)
    $collectorCheck.windowsPowerShell51Syntax = 'passed'
    $policy = (& powershell.exe -NoProfile -Command 'Get-ExecutionPolicy' | Out-String).Trim()
    if ($policy -in @('Restricted', 'AllSigned')) {
        $collectorCheck.windowsPowerShell51Execution = "not-run-policy-$policy"
        # 只在已有开发宿主中验证输出；不绕过或更改目标系统执行策略。
        & $collector -OutputPath $snapshot
        $collectorCheck.actualRuntime = "PowerShell $($PSVersionTable.PSVersion)"
    } else {
        & powershell.exe -NoProfile -File $collector -OutputPath $snapshot
        Check 'Windows PowerShell5.1实际运行' ($LASTEXITCODE -eq 0)
        $collectorCheck.windowsPowerShell51Execution = 'passed'
        $collectorCheck.actualRuntime = 'Windows PowerShell5.1'
    }
    Check '环境记录已实际生成' (Test-Path $snapshot)
    $environment = Get-Content $snapshot -Raw -Encoding UTF8 | ConvertFrom-Json
    Check '环境记录不冒充人工验收' (-not $environment.cleanEnvironmentVerified -and -not $environment.networkDisconnectionVerified -and @($environment.manualResults.psobject.Properties | Where-Object { $null -ne $_.Value }).Count -eq 0)
    $text = Get-Content $snapshot -Raw -Encoding UTF8
    Check '环境记录未包含用户路径和机器名' (-not $text.Contains($env:USERPROFILE) -and -not $text.Contains($env:COMPUTERNAME))
    $completed = $true
} finally {
    [ordered]@{ checkedAt = (Get-Date).ToUniversalTime().ToString('o'); version = $manifest.version; buildId = $manifest.buildId; checks = $checks.ToArray(); allPassed = $completed; environmentCollector = $collectorCheck; userTrialPerformed = $false; cleanMachineVerified = $false; wordLayoutReviewed = $false } | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $work 'verification.json') -Encoding utf8
}
Write-Output "试用材料本机检查通过，共$($checks.Count)项：$work"
