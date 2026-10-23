#Requires -Version 7.2
param([Parameter(Mandatory)][string]$ReleaseDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$release = [IO.Path]::GetFullPath($ReleaseDirectory)
$setups = @(Get-ChildItem -LiteralPath $release -Filter '*-Setup.exe')
if ($setups.Count -ne 1) { throw '应有且仅有一个候选安装包。' }
$setup = $setups[0]
$payload = Join-Path $release ($setup.BaseName -replace '-Setup$', '')
$build = Get-Content (Join-Path $payload 'BUILD.json') -Raw | ConvertFrom-Json
if ($build.version -notmatch '^\d+\.\d+\.\d+(-rc\.\d+)?$' -or $setup.Name -ne "md2word-$($build.version)-win-x64-Setup.exe") { throw '版本或安装包名称不一致。' }
$verification = Get-Content (Join-Path $release 'verification.json') -Raw | ConvertFrom-Json
if (-not $verification.allPassed) { throw '候选便携验证未通过，暂不制作试用准备包。' }
$setupHash = (Get-FileHash $setup.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$expected = @((Get-Content (Join-Path $release 'SHA256SUMS.txt')) | Where-Object { $_ -eq "$setupHash  $($setup.Name)" })
if ($expected.Count -ne 1) { throw '安装包摘要与候选清单不一致。' }
$folder = Join-Path $root ('artifacts/trials/' + $build.version + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
if (Test-Path -LiteralPath $folder) { throw '试用准备目录已存在，请稍后重试。' }
$kit = Join-Path $folder 'md2word试用准备'
foreach ($name in @('', '样例/assets', '缺图练习/assets', '修复用图片', '验证人员')) { New-Item -ItemType Directory -Path (Join-Path $kit $name) -Force | Out-Null }
Copy-Item -LiteralPath $setup.FullName -Destination $kit
foreach ($pair in @(@('work-report.md','工作周报.md'), @('table.md','表格.md'), @('mixed-images.md','中英文与图片.md'))) {
    Copy-Item -LiteralPath (Join-Path $root ('examples/showcase/' + $pair[0])) -Destination (Join-Path $kit ('样例/' + $pair[1]))
}
Copy-Item -LiteralPath (Join-Path $root 'examples/showcase/assets/md2word-cover.png') -Destination (Join-Path $kit '样例/assets')
Copy-Item -LiteralPath (Join-Path $root 'tests/fixtures/assets/diagram.png') -Destination (Join-Path $kit '修复用图片')
'# 图片说明', '', '这是一份合成的缺图练习。修复图片后，下面的图片应嵌入生成的Word。', '', '![示意图](assets/diagram.png)' | Set-Content (Join-Path $kit '缺图练习/图片说明.md') -Encoding utf8
'请将修复用图片中的diagram.png复制到这里，再重试失败文件。' | Set-Content (Join-Path $kit '缺图练习/assets/请将图片放在这里.txt') -Encoding utf8
foreach ($file in @('请先阅读.txt','试用反馈.csv')) {
    Get-Content (Join-Path $root "release/trial/$file") -Raw | Set-Content (Join-Path $kit $file) -Encoding utf8BOM -NoNewline
}
Copy-Item -LiteralPath (Join-Path $root 'release/RELEASE-STATUS.md') -Destination (Join-Path $kit '发布状态.md')
Copy-Item -LiteralPath (Join-Path $root 'docs/目标电脑与用户试用.md') -Destination (Join-Path $kit '验证人员/目标电脑检查表.md')
Get-Content (Join-Path $root 'scripts/collect-trial-environment.ps1') -Raw | Set-Content (Join-Path $kit '验证人员/收集环境.ps1') -Encoding utf8BOM -NoNewline
[ordered]@{
    version = $build.version; buildId = $build.buildId; createdAt = (Get-Date).ToUniversalTime().ToString('o')
    installer = $setup.Name; installerSha256 = $setupHash; signing = $build.signing
    publicDistributionReady = $false; purpose = 'local-review-preparation'; pendingChecks = $build.pendingChecks
    candidateChecks = [ordered]@{ portablePassed = [bool]$verification.allPassed; portableCount = $verification.checks.Count }
    actualUserTrials = 0; cleanWindows10Verified = $false; cleanWindows11Verified = $false
} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $kit 'TRIAL.json') -Encoding utf8
$files = @(Get-ChildItem -LiteralPath $kit -Recurse -File | Sort-Object FullName | ForEach-Object {
    [ordered]@{ path = [IO.Path]::GetRelativePath($kit, $_.FullName).Replace('\','/'); sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$files | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $kit 'FILES.json') -Encoding utf8
$zip = Join-Path $folder ("md2word-$($build.version)-试用准备.zip")
Compress-Archive -LiteralPath $kit -DestinationPath $zip
((Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($zip)) | Set-Content (Join-Path $folder 'SHA256SUMS.txt') -Encoding utf8
Write-Output "试用准备包已生成，尚未对外分发：$folder"
