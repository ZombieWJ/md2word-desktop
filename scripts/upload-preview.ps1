#Requires -Version 7.2
param([Parameter(Mandatory)][string]$ReleaseDirectory)
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:GITHUB_EVENT_NAME -ne 'workflow_dispatch' -or $env:GITHUB_REPOSITORY -ne 'ZombieWJ/md2word-desktop' -or $env:GITHUB_REF -ne 'refs/heads/main' -or -not $env:GH_TOKEN) { throw 'Only an upstream manual preview workflow may upload.' }
$releaseRoot = [IO.Path]::GetFullPath($ReleaseDirectory)
$manifest = Get-Content (Join-Path $releaseRoot 'source-materials/BUILD.json') -Raw | ConvertFrom-Json
$version = [string]$manifest.version
if ($version -notmatch '^\d+\.\d+\.\d+-rc\.\d+$' -or -not $manifest.previewDownloadReady -or $manifest.publicReleaseReady -or $manifest.sourceHasLocalChanges -or $manifest.sourceBaseCommit -ne $env:GITHUB_SHA) { throw 'Preview must match this clean candidate revision.' }
$checks = Get-Content (Join-Path $releaseRoot 'verification.json') -Raw | ConvertFrom-Json
if (-not $checks.allPassed -or $checks.checks.Count -eq 0 -or @($checks.checks | Where-Object { -not $_.passed }).Count) { throw 'Packaged conversion checks have not passed.' }
$tests = foreach ($path in Get-ChildItem artifacts/test-results -Filter 'preview-*.trx') {
    [xml]$xml = Get-Content -LiteralPath $path.FullName -Raw
    $count = $xml.TestRun.ResultSummary.Counters
    if (-not $count -or [int]$count.failed -ne 0 -or [int]$count.passed -le 0 -or [int]$count.passed -ne [int]$count.total) { throw 'Preview tests did not all pass.' }
    [ordered]@{ report = $path.Name; passed = [int]$count.passed; total = [int]$count.total; failed = [int]$count.failed }
}
if (@($tests).Count -ne 2) { throw 'Missing core or desktop test report.' }
$runUrl = "https://github.com/$($env:GITHUB_REPOSITORY)/actions/runs/$($env:GITHUB_RUN_ID)"
$summary = [ordered]@{ version = $version; sourceCommit = $manifest.sourceBaseCommit; workflow = $runUrl; tests = @($tests); packagedChecks = @($checks.checks | ForEach-Object { [ordered]@{ name = $_.name; passed = $_.passed } }); os = $checks.os; networkCondition = $checks.networkCondition; cleanMachineVerified = $false; windows11Verified = $false; wordLayoutRechecked = $false; signing = 'unsigned'; pendingChecks = @($manifest.pendingChecks); deferredChecks = @($manifest.deferredChecks) }
$summary | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $releaseRoot 'VERIFICATION.json') -Encoding utf8
$sourceBundle = Join-Path $releaseRoot "md2word-$version-source-materials.zip"
Compress-Archive -Path (Join-Path $releaseRoot 'source-materials/*') -DestinationPath $sourceBundle
$upload = Join-Path $releaseRoot 'upload'
New-Item -ItemType Directory -Path $upload -Force | Out-Null
$names = @("md2word-$version-win-x64-Setup.exe", "md2word-$version-source-materials.zip", 'SIZE.md', 'VERIFICATION.json')
$hashes = @{}
foreach ($name in $names) { $hashes[$name] = (Get-FileHash -LiteralPath (Join-Path $releaseRoot $name) -Algorithm SHA256).Hash.ToLowerInvariant() }
$names | ForEach-Object { $hashes[$_] + '  ' + $_ } | Set-Content (Join-Path $upload 'SHA256SUMS.txt') -Encoding utf8
$names += 'SHA256SUMS.txt'
$hashes['SHA256SUMS.txt'] = (Get-FileHash -LiteralPath (Join-Path $upload 'SHA256SUMS.txt') -Algorithm SHA256).Hash.ToLowerInvariant()
$reviewBinary = 'LOCAL-REVIEW-pandoc.zip'
Copy-Item -LiteralPath '.tools/pandoc-bundle/pandoc-3.10-md2word-win-x64.zip' -Destination (Join-Path $releaseRoot $reviewBinary)
$names += $reviewBinary
$hashes[$reviewBinary] = (Get-FileHash -LiteralPath (Join-Path $releaseRoot $reviewBinary) -Algorithm SHA256).Hash.ToLowerInvariant()
$repo = '/repos/' + $env:GITHUB_REPOSITORY
$headers = @{ Authorization = 'Bearer ' + $env:GH_TOKEN; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28'; 'Cache-Control' = 'no-cache' }
function Api([string]$Method, [string]$Path, $Body = $null) {
    $options = @{ Uri = "https://api.github.com$Path"; Method = $Method; Headers = $headers; TimeoutSec = 90 }
    if ($null -ne $Body) { $options.Body = $Body | ConvertTo-Json -Depth 12 -Compress; $options.ContentType = 'application/json' }
    Invoke-RestMethod @options
}
try {
    $identity = Api GET $repo
    if ($identity.id -ne 1430541411) { throw 'Unexpected upstream repository identity.' }
    $all = Api GET "$repo/releases?per_page=100"
    $existing = @($all | Where-Object { $_.tag_name -eq "v$version" -or $_.name -eq "v$version — Windows预览版" })
    if ($existing.Count -gt 1) { throw 'Duplicate preview drafts; review before retrying.' }
    if ($existing.Count) {
        $draft = $existing[0]
        if (-not $draft.draft -or -not $draft.prerelease -or $draft.target_commitish -ne $manifest.sourceBaseCommit) { throw 'Existing release is not this revision unpublished draft.' }
    } else {
        $body = @"
Windows x64离线安装预览版。下载Setup.exe安装后，从开始菜单打开“Markdown转Word”；转换无需联网，也无需预装SDK、Pandoc或Word。便携ZIP不上传。应用未签名。

本版使用从归档依赖源码构建的Pandoc3.10，保留其GPL及各组件许可。source-materials.zip包含自有源码、Pandoc完整对应源码、实际依赖修订、GHC及Windows配套源码和构建记录；普通用户不需要下载源码材料。

当前工作流的核心、桌面及包内转换检查已通过，详见VERIFICATION.json和构建记录。尚未取得干净Windows10/11、实际断网及完整系统交互的全部记录；不是所有Windows及Word版本的兼容性承诺。维护者已确认此前安装、转换和卸载，用户试用按要求延期。

本草稿等待本机复核，不会由工作流自动发布。
源码提交：$($manifest.sourceBaseCommit)
构建与检查：$runUrl
"@
        $draft = Api POST "$repo/releases" @{ tag_name = "v$version"; target_commitish = $manifest.sourceBaseCommit; name = "v$version — Windows预览版"; body = $body; draft = $true; prerelease = $true }
    }
    $assets = Api GET "$repo/releases/$($draft.id)/assets?per_page=100"
    foreach ($name in $names) {
        $path = if ($name -eq 'SHA256SUMS.txt') { Join-Path $upload $name } else { Join-Path $releaseRoot $name }
        $asset = @($assets | Where-Object name -eq $name)
        if ($asset.Count -gt 1) { throw 'Duplicate asset names.' }
        if ($asset.Count -eq 1 -and $asset[0].state -eq 'uploaded' -and $asset[0].digest -eq ('sha256:' + $hashes[$name])) { continue }
        $current = Api GET "$repo/releases/$($draft.id)"
        if (-not $current.draft -or $current.target_commitish -ne $manifest.sourceBaseCommit) { throw 'Release no longer matches this unpublished preview.' }
        if ($asset.Count) { $null = Api DELETE "$repo/releases/assets/$($asset[0].id)" }
        $url = "https://uploads.github.com$repo/releases/$($draft.id)/assets?name=$([Uri]::EscapeDataString($name))"
        $uploaded = Invoke-RestMethod -Method Post -Uri $url -Headers $headers -InFile $path -ContentType 'application/octet-stream' -TimeoutSec 600
        if ($uploaded.state -ne 'uploaded' -or $uploaded.size -ne (Get-Item -LiteralPath $path).Length -or $uploaded.digest -ne ('sha256:' + $hashes[$name])) { throw 'Remote attachment size or SHA256 mismatch.' }
        Write-Output "Draft attachment verified: $name"
    }
    $result = Api GET "$repo/releases/$($draft.id)"
    if (-not $result.draft -or -not $result.prerelease -or $result.assets.Count -ne $names.Count) { throw 'Draft attachment set is incomplete.' }
    "Unpublished preview: $($result.html_url)" >> $env:GITHUB_STEP_SUMMARY
    Write-Output "Unpublished preview ready: $($result.html_url)"
} finally { $headers = $null }
