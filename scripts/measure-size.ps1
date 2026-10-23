#Requires -Version 7.2
param([Parameter(Mandatory)][string]$PayloadDirectory, [Parameter(Mandatory)][string]$ReleaseDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$payload = [IO.Path]::GetFullPath($PayloadDirectory)
$groups = [ordered]@{ runtime = [long]0; pandoc = [long]0; application = [long]0; dependencies = [long]0; resourcesAndNotices = [long]0 }
foreach ($file in Get-ChildItem -LiteralPath $payload -Recurse -File) {
    $relative = [IO.Path]::GetRelativePath($payload, $file.FullName).Replace('\', '/')
    $category = if ($relative.StartsWith('pandoc/')) { 'pandoc' }
        elseif ($file.Name -like 'Md2Word.*') { 'application' }
        elseif ($file.Name -like 'DocumentFormat.OpenXml*') { 'dependencies' }
        elseif ($file.Extension -in @('.dll', '.exe', '.dat')) { 'runtime' }
        else { 'resourcesAndNotices' }
    $groups[$category] += $file.Length
}
$workspace = foreach ($item in Get-ChildItem -LiteralPath $root -Force) {
    $bytes = if ($item.PSIsContainer) { (Get-ChildItem -LiteralPath $item.FullName -Recurse -File -Force | Measure-Object Length -Sum).Sum } else { $item.Length }
    [ordered]@{ name = $item.Name; bytes = [long]$bytes }
}
$downloads = @(Get-ChildItem -LiteralPath $ReleaseDirectory -File | Where-Object Extension -in @('.zip', '.exe') | ForEach-Object { [ordered]@{ file = $_.Name; bytes = $_.Length } })
$report = [ordered]@{ measuredAt = (Get-Date).ToUniversalTime().ToString('o'); downloadFiles = $downloads; portableExpandedBytes = ($groups.Values | Measure-Object -Sum).Sum; componentsBytes = $groups; classification = '按路径和文件名统计；运行时包含各语言资源程序集，不是逐程序集归属审计。'; workspaceTopLevelBytes = @($workspace) }
$report | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $ReleaseDirectory 'size-report.json') -Encoding utf8
$lines = @('# 候选包体积', '', '下载大小与解压后的磁盘占用分别统计；安装器压缩不消除运行时及Pandoc。', '')
foreach ($download in $downloads) { $lines += '- ' + $download.file + '：' + [math]::Round($download.bytes / 1MB, 2) + ' MiB' }
$lines += ''; $lines += '便携目录展开：' + [math]::Round($report.portableExpandedBytes / 1MB, 2) + ' MiB'; $lines += ''
foreach ($key in $groups.Keys) { $lines += '- ' + $key + '：' + [math]::Round($groups[$key] / 1MB, 2) + ' MiB' }
$lines += ''; $lines += '精确字节及开发目录占用见size-report.json。安装后实际大小另由安装验收记录；不要把候选目录当作已安装结果。'
$lines | Set-Content (Join-Path $ReleaseDirectory 'SIZE.md') -Encoding utf8
