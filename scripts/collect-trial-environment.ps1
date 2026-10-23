#Requires -Version 5.1
param([string]$OutputPath)
$ErrorActionPreference = 'Stop'
if (-not $OutputPath) { $OutputPath = Join-Path $PSScriptRoot ('环境记录-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.json') }
if (Test-Path -LiteralPath $OutputPath) { throw '输出文件已存在，请换一个文件名。' }
$os = Get-ItemProperty 'HKLM:/SOFTWARE/Microsoft/Windows NT/CurrentVersion'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try { $administrator = ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) }
finally { $identity.Dispose() }
$sdkVersions = @()
foreach ($root in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
    if (-not $root) { continue }
    $sdk = Join-Path $root 'dotnet/sdk'
    if (Test-Path -LiteralPath $sdk) { $sdkVersions += @(Get-ChildItem -LiteralPath $sdk -Directory | Select-Object -ExpandProperty Name) }
}
$adapterCount = $null
if (Get-Command Get-NetAdapter -ErrorAction SilentlyContinue) {
    try { $adapterCount = @(Get-NetAdapter -ErrorAction Stop | Where-Object Status -eq 'Up').Count }
    catch [Microsoft.Management.Infrastructure.CimException] { $adapterCount = $null }
}
$key = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/{A73DF38C-55D1-48F8-B8C9-5A5D3D50C28B}_is1'
$installed = if (Test-Path $key) { Get-ItemProperty $key } else { $null }
$manifestPath = Join-Path $PSScriptRoot '../TRIAL.json'
$manifest = if (Test-Path $manifestPath) { Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json } else { $null }
$record = [ordered]@{
    recordedAt = (Get-Date).ToUniversalTime().ToString('o')
    windows = [ordered]@{ productName = $os.ProductName; edition = $os.EditionID; displayVersion = $os.DisplayVersion; build = $os.CurrentBuildNumber; revision = $os.UBR; is64Bit = [Environment]::Is64BitOperatingSystem }
    elevatedProcess = $administrator
    standardSdkDirectories = @($sdkVersions | Sort-Object -Unique)
    pandocOnPath = [bool](Get-Command pandoc.exe -CommandType Application -ErrorAction SilentlyContinue)
    activeAdapterCount = $adapterCount
    networkDisconnectionVerified = $false
    cleanEnvironmentVerified = $false
    environmentNote = '只读环境线索；未全面扫描软件，未关闭网络，不能据此认定干净或离线环境。'
    installedVersion = $(if ($installed) { $installed.DisplayVersion } else { $null })
    installedPublisher = $(if ($installed) { $installed.Publisher } else { $null })
    candidateVersion = $(if ($manifest) { $manifest.version } else { $null })
    installerSha256 = $(if ($manifest) { $manifest.installerSha256 } else { $null })
    manualResults = [ordered]@{ install = $null; upgrade = $null; uninstall = $null; offlineConversion = $null; dialogsAndDrop = $null; keyboardAndDpi = $null; wordLayout = $null }
    privacy = '不包含账户名、计算机名、文档正文、完整文件路径、IP地址或网络名称；不自动上传。'
}
$record | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
Write-Output '环境线索已保存，人工检查结果仍为空。请按检查表记录实际操作。'
