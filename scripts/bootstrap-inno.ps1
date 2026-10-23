#Requires -Version 7.2
param([switch]$DirectNetwork)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$config = Get-Content (Join-Path $root 'release/inno.json') -Raw | ConvertFrom-Json
$archive = Join-Path $root ".tools/downloads/innosetup-$($config.version)-x64.exe"
$destination = Join-Path $root '.tools/inno'
New-Item -ItemType Directory -Path (Split-Path $archive) -Force | Out-Null
if (-not (Test-Path -LiteralPath $archive)) {
    $options = @{ Uri = $config.url; OutFile = "$archive.partial"; TimeoutSec = 180 }
    if ($DirectNetwork) { $options.NoProxy = $true }
    Invoke-WebRequest @options
    Move-Item -LiteralPath "$archive.partial" -Destination $archive
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $config.sha256) { throw 'Inno Setup校验失败。' }
$signature = Get-AuthenticodeSignature -LiteralPath $archive
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike '*CN=Pyrsys B.V.*') { throw 'Inno Setup官方签名验证失败。' }
if (-not (Test-Path (Join-Path $destination 'ISCC.exe'))) {
    # 官方安装脚本支持PORTABLE模式，不注册卸载项、文件关联或快捷方式。
    $process = Start-Process -FilePath $archive -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/PORTABLE=1', "/DIR=`"$destination`"") -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Inno Setup便携展开失败：$($process.ExitCode)" }
}
$version = & (Join-Path $destination 'ISCC.exe') --version
if ($LASTEXITCODE -ne 0 -or ($version -join "`n") -notmatch [regex]::Escape($config.version)) { throw "编译工具版本不符：$version" }
Write-Output "项目安装包工具已准备：$destination"
