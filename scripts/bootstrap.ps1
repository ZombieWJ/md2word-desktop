param([switch]$DirectNetwork)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$sdkVersion = (Get-Content -LiteralPath (Join-Path $projectRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
$sdkRoot = Join-Path $projectRoot '.tools\dotnet'
$dotnet = Join-Path $sdkRoot 'dotnet.exe'
if ((Test-Path -LiteralPath $dotnet) -and ((& $dotnet --version) -eq $sdkVersion)) {
    Write-Host "项目SDK已就绪：$sdkVersion"
    exit 0
}
$sdkUrl = "https://builds.dotnet.microsoft.com/dotnet/Sdk/$sdkVersion/dotnet-sdk-$sdkVersion-win-x64.zip"
$expectedSha512 = 'ae4fd528cb7e9f6af6aaf7de36715a69f761f183744f1bb3025d19f5a92447090ec561410e46266c1d10a63781ed6f3de771760bc76c876d87daabc367b898cd'
if ($sdkVersion -ne '10.0.402') { throw 'SDK版本已改变，请先更新下载地址对应的官方SHA512。' }
$downloadRoot = Join-Path $projectRoot '.tools\downloads'
New-Item -ItemType Directory -Path $downloadRoot -Force | Out-Null
$archive = Join-Path $downloadRoot "dotnet-sdk-$sdkVersion-win-x64.zip"
if (-not (Test-Path -LiteralPath $archive)) {
    $webOptions = @{ Uri = $sdkUrl; OutFile = "$archive.partial"; TimeoutSec = 600 }
    if ($DirectNetwork) { $webOptions.NoProxy = $true }
    Invoke-WebRequest @webOptions
    Move-Item -LiteralPath "$archive.partial" -Destination $archive
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash -ne $expectedSha512) {
    throw 'SDK下载文件的SHA512与微软官方记录不一致，停止解压。'
}
New-Item -ItemType Directory -Path $sdkRoot -Force | Out-Null
Expand-Archive -LiteralPath $archive -DestinationPath $sdkRoot -Force
if ((& $dotnet --version) -ne $sdkVersion) { throw '项目SDK版本检查失败。' }
Write-Host "项目SDK已就绪：$sdkVersion"
