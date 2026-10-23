#Requires -Version 7.2
[CmdletBinding(SupportsShouldProcess, ConfirmImpact='Medium')]
param([switch]$Apply, [ValidateRange(2, 100)][int]$KeepLatest = 2)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/releases'))
if (-not (Test-Path -LiteralPath $root)) { return }
$candidates = @(Get-ChildItem -LiteralPath $root -Directory | Where-Object Name -Match '^candidate-\d{8}-\d{6}-[a-f0-9]+$' | Sort-Object Name -Descending)
$keep = @($candidates | Where-Object { @(Get-ChildItem -LiteralPath $_.FullName -Filter '*.zip' -File).Count -gt 0 } | Select-Object -First $KeepLatest -ExpandProperty Name)
foreach ($candidate in $candidates | Where-Object Name -NotIn $keep) {
    $resolved = [IO.Path]::GetFullPath($candidate.FullName)
    if (-not $resolved.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or $candidate.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw '清理路径越界或包含链接。' }
    if (Get-ChildItem -LiteralPath $resolved -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw "跳过包含链接的目录：$resolved" }
    # 保留源码快照和证据，仅清理官方程序展开及CLI构建副本。
    foreach ($name in @('work/cli', 'work/pandoc')) {
      $work = Join-Path $resolved $name
      if (Test-Path -LiteralPath $work) {
        $userDocuments = @(Get-ChildItem -LiteralPath $work -Recurse -File | Where-Object {
            $_.Extension -in @('.docx', '.md', '.markdown', '.txt', '.pdf', '.xlsx') -and
            [IO.Path]::GetRelativePath($work, $_.FullName).Replace('\', '/') -notin @('templates/cn-general/reference.docx', 'pandoc-3.10/COPYRIGHT.txt')
        })
        if ($userDocuments.Count -gt 0) { continue }
        $bytes = (Get-ChildItem -LiteralPath $work -Recurse -File | Measure-Object Length -Sum).Sum
        [pscustomobject]@{ path = $work; bytes = [long]$bytes; action = $(if ($Apply) { '删除中间目录' } else { '仅预览' }) }
        if ($Apply -and $PSCmdlet.ShouldProcess($work, '删除可重建的中间目录')) { Remove-Item -LiteralPath $work -Recurse -Force }
      }
    }
    foreach ($payload in Get-ChildItem -LiteralPath $resolved -Directory) {
        $manifestFile = Join-Path $payload.FullName 'FILES.json'
        $archive = Join-Path $resolved ($payload.Name + '.zip')
        if (-not (Test-Path $manifestFile) -or -not (Test-Path $archive)) { continue }
        $buildFile = Join-Path $payload.FullName 'BUILD.json'
        if (-not (Test-Path $buildFile) -or (Get-Content $buildFile -Raw | ConvertFrom-Json).publicReleaseReady) { continue }
        $files = @(Get-Content $manifestFile -Raw | ConvertFrom-Json)
        $actual = @(Get-ChildItem -LiteralPath $payload.FullName -Recurse -File -Force)
        if ($actual.Count -ne $files.Count + 1) { continue }
        $unchanged = $true
        foreach ($file in $files) {
            $target = [IO.Path]::GetFullPath((Join-Path $payload.FullName $file.path))
            if (-not $target.StartsWith($payload.FullName + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $target) -or (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $file.sha256) { $unchanged = $false; break }
        }
        $checksumFile = Join-Path $resolved 'SHA256SUMS.txt'
        if (-not $unchanged -or -not (Test-Path $checksumFile)) { continue }
        $expected = Get-Content $checksumFile | Where-Object { $_ -match ('^[a-f0-9]{64}  ' + [regex]::Escape($payload.Name + '.zip') + '$') }
        if (@($expected).Count -ne 1 -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected.Substring(0,64)) { continue }
        [pscustomobject]@{ path = $payload.FullName; bytes = [long](($actual | Measure-Object Length -Sum).Sum); action = $(if ($Apply) { '删除未改动的展开副本，保留ZIP' } else { '仅预览，保留ZIP' }) }
        if ($Apply -and $PSCmdlet.ShouldProcess($payload.FullName, '删除经完整清单核对的重复展开副本，保留原ZIP')) { Remove-Item -LiteralPath $payload.FullName -Recurse -Force }
    }
}
