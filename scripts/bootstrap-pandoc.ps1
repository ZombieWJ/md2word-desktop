#Requires -Version 7.2
param([switch]$DirectNetwork)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$config = Get-Content (Join-Path $root 'release/portable.json') -Raw | ConvertFrom-Json
$destination = Join-Path $root "pandoc-$($config.pandocVersion)"
$executable = Join-Path $destination 'pandoc.exe'
if (Test-Path -LiteralPath $executable) {
    if ((Get-FileHash $executable -Algorithm SHA256).Hash -ne $config.pandocExeSha256) { throw '现有Pandoc摘要不符，未覆盖，请核对来源。' }
    foreach ($file in @('COPYING.rtf', 'COPYRIGHT.txt', 'MANUAL.html')) {
        if (-not (Test-Path -LiteralPath (Join-Path $destination $file))) { throw "现有Pandoc缺少$file，请补齐官方材料。" }
    }
    Write-Output "Pandoc已就绪：$($config.pandocVersion)"
    exit 0
}
$cache = Join-Path $root '.tools/downloads'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$archive = Join-Path $cache "pandoc-$($config.pandocVersion)-windows-x86_64.zip"
if (-not (Test-Path -LiteralPath $archive)) {
    $options = @{ Uri=$config.pandocArchiveUrl; OutFile="$archive.partial"; TimeoutSec=300 }
    if ($DirectNetwork) { $options.NoProxy = $true }
    Invoke-WebRequest @options
    Move-Item -LiteralPath "$archive.partial" -Destination $archive
}
if ((Get-FileHash $archive -Algorithm SHA256).Hash -ne $config.pandocArchiveSha256) { throw 'Pandoc ZIP与官方摘要不一致，停止解压。' }
$verified = Join-Path $cache ('pandoc-verified-' + [guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath $archive -DestinationPath $verified
$source = Join-Path $verified "pandoc-$($config.pandocVersion)"
if ((Get-FileHash (Join-Path $source 'pandoc.exe') -Algorithm SHA256).Hash -ne $config.pandocExeSha256) { throw 'Pandoc程序摘要不一致，停止复制。' }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach ($file in @('pandoc.exe', 'COPYING.rtf', 'COPYRIGHT.txt', 'MANUAL.html')) { Copy-Item -LiteralPath (Join-Path $source $file) -Destination $destination }
Write-Output "Pandoc已就绪：$($config.pandocVersion)"
