#Requires -Version 7.2
param([Parameter(Mandatory)][string]$ReleaseDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$releaseRoot = [IO.Path]::GetFullPath($ReleaseDirectory)
$work = Join-Path $releaseRoot ('verification-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
if (Test-Path -LiteralPath $work) { throw '检查目录已存在，请稍后重试。' }
New-Item -ItemType Directory -Path $work | Out-Null
$records = [Collections.Generic.List[object]]::new()

function Run-PackagedProcess([string]$Executable, [string[]]$Arguments, [bool]$Visible = $false) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    $start.WorkingDirectory = $work
    $start.CreateNoWindow = -not $Visible
    $start.WindowStyle = if ($Visible) { [Diagnostics.ProcessWindowStyle]::Normal } else { [Diagnostics.ProcessWindowStyle]::Hidden }
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    # 只修改本次子进程环境。无效运行时位置及精简PATH用于排除开发目录依赖。
    $start.Environment['DOTNET_ROOT'] = Join-Path $work 'no-installed-dotnet'
    $start.Environment['DOTNET_ROOT_X64'] = Join-Path $work 'no-installed-dotnet'
    $start.Environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'
    $start.Environment['PATH'] = Join-Path $env:SystemRoot 'System32'
    $start.Environment['HTTP_PROXY'] = 'http://127.0.0.1:9'
    $start.Environment['HTTPS_PROXY'] = 'http://127.0.0.1:9'
    $start.Environment['ALL_PROXY'] = 'http://127.0.0.1:9'
    $trace = Join-Path $work ('host-' + [guid]::NewGuid().ToString('N') + '.txt')
    $start.Environment['DOTNET_HOST_TRACE'] = '1'
    $start.Environment['DOTNET_HOST_TRACEFILE'] = $trace
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(45000)) { $process.Kill($true); throw "便携程序超时：$Executable" }
        $process.WaitForExit()
        $traceText = if (Test-Path -LiteralPath $trace) { [IO.File]::ReadAllText($trace) } else { '' }
        [pscustomobject]@{ executable = $Executable; arguments = $Arguments; exitCode = $process.ExitCode; stdout = $stdout.GetAwaiter().GetResult(); stderr = $stderr.GetAwaiter().GetResult(); hostTrace = $trace; traceText = $traceText }
    } finally { $process.Dispose() }
}

function Check([string]$Name, [bool]$Passed, $Detail) {
    $records.Add([ordered]@{ name = $Name; passed = $Passed; detail = $Detail })
    if (-not $Passed) { throw "便携检查失败：$Name" }
}

function Verify-Docx([string]$Path, [bool]$ExpectImage) {
    $zip = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        Check ('DOCX正文：' + [IO.Path]::GetFileName($Path)) ($null -ne $zip.GetEntry('word/document.xml')) $Path
        if ($ExpectImage) {
            Check ('图片嵌入：' + [IO.Path]::GetFileName($Path)) (@($zip.Entries | Where-Object FullName -Like 'word/media/*').Count -gt 0) $Path
        }
    } finally { $zip.Dispose() }
}

try {
    $zip = @(Get-ChildItem -LiteralPath $releaseRoot -Filter 'Md2Word-*-win-x64.zip')
    if ($zip.Count -ne 1) { throw '请指定仅含一个便携ZIP的候选包目录。' }
    foreach ($line in Get-Content (Join-Path $releaseRoot 'SHA256SUMS.txt')) {
        if ($line -notmatch '^([a-f0-9]{64})  (.+)$') { throw '校验清单格式无效。' }
        $expected, $relative = $Matches[1], $Matches[2]
        $target = [IO.Path]::GetFullPath((Join-Path $releaseRoot $relative))
        if (-not $target.StartsWith($releaseRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw '校验路径越界。' }
        Check ('SHA256：' + $relative) ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -eq $expected) $relative
    }
    $extracted = Join-Path $work '解压 测试'
    Expand-Archive -LiteralPath $zip[0].FullName -DestinationPath $extracted
    $app = @(Get-ChildItem -LiteralPath $extracted -Directory)[0].FullName
    $fileManifest = @(Get-Content (Join-Path $app 'FILES.json') -Raw | ConvertFrom-Json)
    $fileFailures = @($fileManifest | Where-Object { (Get-FileHash -LiteralPath (Join-Path $app $_.path) -Algorithm SHA256).Hash -ne $_.sha256 })
    $actualFiles = @(Get-ChildItem -LiteralPath $app -Recurse -File)
    Check '包内文件清单完整且摘要一致' ($fileFailures.Count -eq 0 -and $actualFiles.Count -eq $fileManifest.Count + 1) $fileManifest.Count
    $build = Get-Content (Join-Path $app 'BUILD.json') -Raw | ConvertFrom-Json
    $sourceZip = [IO.Compression.ZipFile]::OpenRead((Join-Path $releaseRoot 'source-materials/md2word-source.zip'))
    try {
        $sourceFailures = @()
        foreach ($file in $build.sourceFiles) {
            $entry = $sourceZip.GetEntry($file.path.Replace('\', '/'))
            if (-not $entry) { $sourceFailures += $file.path; continue }
            $stream = $entry.Open()
            try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
            if ($hash -ne $file.sha256) { $sourceFailures += $file.path }
        }
        Check '源码快照与构建清单一致' ($sourceFailures.Count -eq 0 -and $build.sourceFiles.Count -gt 0) $build.sourceFiles.Count
    } finally { $sourceZip.Dispose() }
    foreach ($name in @('Md2Word.Desktop', 'Md2Word.Cli')) {
        $runtime = Get-Content (Join-Path $app "$name.runtimeconfig.json") -Raw | ConvertFrom-Json
        Check ("自包含配置：$name") ($null -eq $runtime.runtimeOptions.frameworks -and $null -eq $runtime.runtimeOptions.framework -and $runtime.runtimeOptions.includedFrameworks.Count -gt 0) $runtime.runtimeOptions
    }
    foreach ($file in @('coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'pandoc/pandoc.exe', 'templates/cn-general/reference.docx', 'resources/convert.lua', 'DEPENDENCIES.json', 'LICENSE')) {
        Check ("包内资源：$file") (Test-Path -LiteralPath (Join-Path $app $file) -PathType Leaf) $file
    }
    foreach ($file in @('licenses/Open-XML-SDK-LICENSE.txt', 'licenses/Microsoft.NETCore.App.Runtime.win-x64/10.0.13/LICENSE.TXT', 'licenses/Microsoft.NETCore.App.Runtime.win-x64/10.0.13/THIRD-PARTY-NOTICES.TXT', 'licenses/Microsoft.WindowsDesktop.App.Runtime.win-x64/10.0.13/LICENSE')) {
        Check ("随包许可：$file") (Test-Path -LiteralPath (Join-Path $app $file) -PathType Leaf) $file
    }
    $bad = @(Get-ChildItem $app -Recurse -File | Where-Object { $_.Name -eq 'settings.json' -or $_.Extension -in @('.pdb', '.ttf', '.otf', '.odttf') -or $_.FullName -match '[\\/](\.tools|obj|TestResults)[\\/]' })
    Check '无个人设置、调试文件或字体文件' ($bad.Count -eq 0) @($bad | ForEach-Object FullName)
    $gui = Run-PackagedProcess (Join-Path $app 'Md2Word.Desktop.exe') @('--smoke-test') $true
    Check '解压后主窗口可见' ($gui.exitCode -eq 0) $gui.exitCode

    $inputs = Join-Path $work '中文 输入'
    Copy-Item -LiteralPath (Join-Path $projectRoot 'tests/fixtures') -Destination $inputs -Recurse
    $output = Join-Path $work '转换 输出'
    $cli = Join-Path $app 'Md2Word.Cli.exe'
    $selected = @('basic.md', 'headings.md', 'lists.md', 'tables.md', 'code-links.md', 'local-images.md', '中文 空格.md', 'long-text.md', 'wide-table.md', 'long-table.md', 'images-in-table.md', 'large-image.md', 'warnings.md')
    $arguments = @($selected | ForEach-Object { Join-Path $inputs $_ }) + @('--template', 'cn-general', '--output', $output)
    $conversion = Run-PackagedProcess $cli $arguments
    Check '包内CLI批量转换13份样本' ($conversion.exitCode -eq 0) $conversion.stderr
    # Windows原生宿主日志可能用问号替换中文路径，保留ASCII父目录和应用目录核对。
    $runtimeTail = '\' + (Split-Path $app -Leaf) + '\coreclr.dll'
    $loadLines = @($conversion.traceText -split "`n" | Where-Object { $_.StartsWith('Loaded library from ') -and $_.TrimEnd().EndsWith($runtimeTail, [StringComparison]::OrdinalIgnoreCase) -and $_.Contains($work) })
    Check '包内运行时加载' ($loadLines.Count -gt 0) $conversion.hostTrace
    Check '没有加载项目运行时' (-not $conversion.traceText.Contains((Join-Path $projectRoot '.tools'))) $conversion.hostTrace
    Check '生成13份DOCX' (@(Get-ChildItem $output -Filter '*.docx').Count -eq 13) $output
    foreach ($sample in $selected) {
        Verify-Docx (Join-Path $output ([IO.Path]::GetFileNameWithoutExtension($sample) + '.docx')) ($sample -in @('local-images.md', '中文 空格.md', 'images-in-table.md', 'large-image.md'))
    }
    $missing = Run-PackagedProcess $cli @((Join-Path $inputs 'missing-image.md'), '--template', 'cn-general', '--output', $output)
    Check '缺图失败且无半成品' ($missing.exitCode -eq 1 -and -not (Test-Path (Join-Path $output 'missing-image.docx'))) $missing.stderr
    $repeat = Run-PackagedProcess $cli @((Join-Path $inputs 'basic.md'), '--template', 'original', '--output', $output)
    Check '另一模板与重名另存' ($repeat.exitCode -eq 0 -and (Test-Path (Join-Path $output 'basic（2）.docx'))) $repeat.stderr
    # 移动范围严格限定在本次新建的检查目录，不触碰项目及原始候选目录。
    $moved = Join-Path $work '移动后 程序'
    $workPrefix = [IO.Path]::GetFullPath($work) + [IO.Path]::DirectorySeparatorChar
    foreach ($path in @($app, $moved)) { if (-not ([IO.Path]::GetFullPath($path)).StartsWith($workPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw '移动目标超出检查目录。' } }
    Move-Item -LiteralPath $app -Destination $moved
    $movedGui = Run-PackagedProcess (Join-Path $moved 'Md2Word.Desktop.exe') @('--smoke-test') $true
    Check '移动后主窗口可见' ($movedGui.exitCode -eq 0) $movedGui.exitCode
    $afterMove = Run-PackagedProcess (Join-Path $moved 'Md2Word.Cli.exe') @((Join-Path $inputs 'local-images.md'), '--template', 'cn-general', '--output', (Join-Path $work '移动后输出'))
    Check '移动后转换' ($afterMove.exitCode -eq 0) $afterMove.stderr
    Verify-Docx (Join-Path $work '移动后输出/local-images.docx') $true
} finally {
    $os = Get-ItemProperty 'HKLM:/SOFTWARE/Microsoft/Windows NT/CurrentVersion'
    [ordered]@{
        checkedAt = (Get-Date).ToUniversalTime().ToString('o')
        os = [ordered]@{ product = $os.ProductName; edition = $os.EditionID; displayVersion = $os.DisplayVersion; build = $os.CurrentBuildNumber; revision = $os.UBR }
        checks = $records.ToArray()
        allPassed = ($records.Count -gt 0 -and @($records | Where-Object { -not $_.passed }).Count -eq 0 -and $null -ne $afterMove -and $afterMove.exitCode -eq 0)
        networkCondition = '子进程代理指向不可用地址；未切断整机网络，不能替代断网电脑验证。'
        cleanMachineVerified = $false
        windows11Verified = $false
        wordLayoutRechecked = $false
    } | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath (Join-Path $releaseRoot 'verification.json') -Encoding utf8
}
Write-Output "本机便携检查通过：$releaseRoot"
