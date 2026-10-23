#Requires -Version 7.2
param([switch]$DirectNetwork, [switch]$SkipInstaller, [switch]$PublicRelease)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$config = Get-Content (Join-Path $projectRoot 'release/portable.json') -Raw | ConvertFrom-Json
[xml]$versions = Get-Content (Join-Path $projectRoot 'release/version.props') -Raw
$appVersion = $versions.Project.PropertyGroup.Version
$appFileVersion = $versions.Project.PropertyGroup.FileVersion
$readiness = Get-Content (Join-Path $projectRoot 'release/public-readiness.json') -Raw | ConvertFrom-Json
$pendingChecks = @($readiness.checks | Where-Object { $_.status -ne 'passed' -or -not $_.evidence -or -not (Test-Path (Join-Path $projectRoot $_.evidence)) })
if ($PublicRelease) {
    if ($pendingChecks.Count -gt 0) { throw ('正式发行检查未完成：' + ($pendingChecks.id -join '、')) }
    if ($appVersion -match '-') { throw '正式发行版本不能使用候选版本号。' }
    if (& git -C $projectRoot status --porcelain) { throw '正式包必须来自干净源码。' }
    $tag = & git -C $projectRoot describe --tags --exact-match HEAD 2>$null
    if ($tag -ne "v$appVersion") { throw '正式包必须对应相同版本的Git标签。' }
}
$dotnet = Join-Path $projectRoot '.tools/dotnet/dotnet.exe'
$env:DOTNET_ROOT = Split-Path -Parent $dotnet
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools/cli-home'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools/nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

function Assert-Hash([string]$Path, [string]$Expected) {
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Expected) { throw "文件校验失败：$Path" }
}
function Write-Json($Value, [string]$Path) { $Value | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $Path -Encoding utf8 }

$proxyBackup = @{}
foreach ($proxyName in @('HTTP_PROXY', 'HTTPS_PROXY', 'ALL_PROXY')) {
    $proxyBackup[$proxyName] = [Environment]::GetEnvironmentVariable($proxyName, 'Process')
    if ($DirectNetwork) { [Environment]::SetEnvironmentVariable($proxyName, $null, 'Process') }
}
Push-Location $projectRoot
try {
    $commit = & git rev-parse --short HEAD
    if ($LASTEXITCODE -ne 0) { throw '无法取得源码基线。' }
    $buildId = 'candidate-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $commit
    $releaseRoot = Join-Path $projectRoot "artifacts/releases/$buildId"
    if (Test-Path -LiteralPath $releaseRoot) { throw '候选包目录已存在，请稍后重试。' }
    $appName = "md2word-$appVersion-win-x64"
    $app = Join-Path $releaseRoot $appName
    New-Item -ItemType Directory -Path $app -Force | Out-Null

    # 使用官方ZIP验证来源；开发目录原件不被覆盖。
    $archive = Join-Path $projectRoot ".tools/downloads/pandoc-$($config.pandocVersion)-windows-x86_64.zip"
    if (-not (Test-Path -LiteralPath $archive)) {
        New-Item -ItemType Directory -Path (Split-Path $archive) -Force | Out-Null
        $webOptions = @{ Uri = $config.pandocArchiveUrl; OutFile = "$archive.partial"; TimeoutSec = 300 }
        if ($DirectNetwork) { $webOptions.NoProxy = $true }
        Invoke-WebRequest @webOptions
        Move-Item -LiteralPath "$archive.partial" -Destination $archive
    }
    Assert-Hash $archive $config.pandocArchiveSha256
    $official = Join-Path $releaseRoot 'work/pandoc'
    Expand-Archive -LiteralPath $archive -DestinationPath $official
    $officialPandoc = Join-Path $official "pandoc-$($config.pandocVersion)"
    Assert-Hash (Join-Path $officialPandoc 'pandoc.exe') $config.pandocExeSha256
    $comparisons = foreach ($file in Get-ChildItem -LiteralPath $officialPandoc -File) {
        $local = Join-Path $projectRoot "pandoc-$($config.pandocVersion)/$($file.Name)"
        $expected = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        Assert-Hash $local $expected
        [ordered]@{ file = $file.Name; sha256 = $expected.ToLowerInvariant(); matchesLocal = $true }
    }
    Assert-Hash 'templates/cn-general/reference.docx' $config.templateSha256
    $sourceArchive = "third_party/pandoc/$($config.pandocVersion)/pandoc-$($config.pandocVersion)-source.tar.gz"
    if (-not (Test-Path -LiteralPath $sourceArchive)) {
        $sourceOptions = @{ Uri = "https://github.com/jgm/pandoc/archive/refs/tags/$($config.pandocVersion).tar.gz"; OutFile = "$sourceArchive.partial"; TimeoutSec = 180 }
        if ($DirectNetwork) { $sourceOptions.NoProxy = $true }
        Invoke-WebRequest @sourceOptions
        Assert-Hash "$sourceArchive.partial" $config.pandocSourceSha256
        Move-Item -LiteralPath "$sourceArchive.partial" -Destination $sourceArchive
    }
    Assert-Hash $sourceArchive $config.pandocSourceSha256
    foreach ($license in Get-Content 'third_party/licenses/sources.json' -Raw | ConvertFrom-Json) {
        Assert-Hash (Join-Path 'third_party/licenses' $license.file) $license.sha256
    }

    $properties = @('-r', $config.runtimeIdentifier, '-p:SelfContained=true', "-p:RuntimeFrameworkVersion=$($config.runtimeVersion)", '-p:NuGetLockFilePath=packages.win-x64.lock.json')
    foreach ($name in @('Desktop', 'Cli')) {
        $project = "src/Md2Word.$name/Md2Word.$name.csproj"
        & $dotnet restore $project @properties --locked-mode -m:1
        if ($LASTEXITCODE -ne 0) { throw "便携依赖还原失败：$name" }
        $destination = if ($name -eq 'Desktop') { $app } else { Join-Path $releaseRoot 'work/cli' }
        & $dotnet publish $project @properties --no-restore -c Release -o $destination -m:1 -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false
        if ($LASTEXITCODE -ne 0) { throw "自包含发布失败：$name" }
        if ($name -eq 'Cli') {
            Assert-Hash (Join-Path $app 'Md2Word.Core.dll') (Get-FileHash (Join-Path $destination 'Md2Word.Core.dll') -Algorithm SHA256).Hash
            # 桌面包已包含Core运行时和共用依赖，保留其桌面版门面程序集。
            foreach ($file in @('Md2Word.Cli.exe', 'Md2Word.Cli.dll', 'Md2Word.Cli.deps.json', 'Md2Word.Cli.runtimeconfig.json')) {
                Copy-Item -LiteralPath (Join-Path $destination $file) -Destination $app
            }
        }
    }
    Copy-Item -LiteralPath $officialPandoc -Destination (Join-Path $app 'pandoc') -Recurse
    Copy-Item -Path 'templates/cn-general/*' -Destination (Join-Path $app 'templates/cn-general') -Force
    foreach ($file in @('LICENSE', 'THIRD-PARTY-NOTICES.md', 'release/使用说明.txt', 'release/RELEASE-STATUS.md')) { Copy-Item -LiteralPath $file -Destination $app }

    $licenses = Join-Path $app 'licenses'
    New-Item -ItemType Directory -Path $licenses -Force | Out-Null
    Copy-Item -Path 'third_party/licenses/*' -Destination $licenses
    if (-not $SkipInstaller) { Copy-Item -LiteralPath (Join-Path $projectRoot '.tools/inno/License.txt') -Destination (Join-Path $licenses 'Inno-Setup.txt') }
    $inventory = @()
    $packageLibraries = (Get-Content (Join-Path $app 'Md2Word.Desktop.deps.json') -Raw | ConvertFrom-Json -AsHashtable).libraries
    foreach ($identity in $packageLibraries.Keys | Sort-Object) {
        if ($packageLibraries[$identity].type -notin @('package', 'runtimepack')) { continue }
        $id, $version = $identity.Split('/')
        $id = $id -replace '^runtimepack\.', ''
        $packagePath = Join-Path $env:NUGET_PACKAGES ("$id/$version".ToLowerInvariant())
        $target = Join-Path $licenses "$id/$version"
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        $nuspec = Get-ChildItem -LiteralPath $packagePath -Filter '*.nuspec' | Select-Object -First 1
        Copy-Item -LiteralPath $nuspec.FullName -Destination $target
        $texts = @(Get-ChildItem -LiteralPath $packagePath -File | Where-Object Name -Match '^(LICENSE|COPYING|THIRD.PARTY.NOTICES)(\..*)?$')
        foreach ($text in $texts) { Copy-Item -LiteralPath $text.FullName -Destination $target }
        [xml]$metadata = Get-Content -LiteralPath $nuspec.FullName -Raw
        $hashFile = Get-ChildItem -LiteralPath $packagePath -Filter '*.nupkg.sha512' | Select-Object -First 1
        $cacheMetadata = Get-Content (Join-Path $packagePath '.nupkg.metadata') -Raw | ConvertFrom-Json
        $inventory += [ordered]@{ id = $id; version = $version; type = $packageLibraries[$identity].type; packageSha512 = (Get-Content -LiteralPath $hashFile.FullName -Raw).Trim(); nugetContentHash = $cacheMetadata.contentHash; source = $cacheMetadata.source; license = $metadata.package.metadata.license.InnerText; repository = $metadata.package.metadata.repository.url; commit = $metadata.package.metadata.repository.commit; bundledNoticeFiles = @($texts | ForEach-Object Name) }
    }
    Write-Json $inventory (Join-Path $app 'DEPENDENCIES.json')
    Write-Json ([ordered]@{ archiveUrl = $config.pandocArchiveUrl; archiveSha256 = $config.pandocArchiveSha256; releaseAssets = 'https://github.com/jgm/pandoc/releases/expanded_assets/3.10'; files = @($comparisons); correspondingSourceComplete = $false }) (Join-Path $app 'pandoc/SOURCE.json')

    # 记录模板属性，不改动参考文件及其历史元数据。
    $template = [IO.Compression.ZipFile]::OpenRead((Join-Path $app 'templates/cn-general/reference.docx'))
    try {
        $propertiesXml = [ordered]@{}
        foreach ($name in @('docProps/core.xml', 'docProps/app.xml', 'docProps/custom.xml')) {
            $entry = $template.GetEntry($name)
            if ($entry) {
                $reader = [IO.StreamReader]::new($entry.Open())
                try { $propertiesXml[$name] = $reader.ReadToEnd() } finally { $reader.Dispose() }
            }
        }
        $embeddedFonts = @($template.Entries | Where-Object FullName -Match '^word/fonts/|\.(odttf|ttf|otf)$' | ForEach-Object FullName)
        if ($embeddedFonts.Count -gt 0) { throw '模板包含字体文件，请先核对分发范围。' }
        Write-Json ([ordered]@{ referenceSha256 = $config.templateSha256; embeddedFonts = $embeddedFonts; metadata = $propertiesXml; publicLicenseConfirmed = $true; license = 'MIT'; evidence = 'templates/cn-general/SOURCE.md' }) (Join-Path $app 'templates/cn-general/AUDIT.json')
    } finally { $template.Dispose() }

    # 快照包含当前未提交修改；不把Git旧HEAD误称为本次完整源码。
    $snapshot = Join-Path $releaseRoot 'work/source'
    New-Item -ItemType Directory -Path $snapshot -Force | Out-Null
    $sourceFiles = @(& git -c core.quotepath=false ls-files --cached --others --exclude-standard | Sort-Object -Unique)
    $sourceHashes = foreach ($file in $sourceFiles) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { continue }
        $target = Join-Path $snapshot $file
        New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
        Copy-Item -LiteralPath $file -Destination $target
        [ordered]@{ path = $file; sha256 = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    $manifest = [ordered]@{ version = $appVersion; buildId = $buildId; builtAt = (Get-Date).ToUniversalTime().ToString('o'); sourceBaseCommit = (& git rev-parse HEAD); sourceHasLocalChanges = [bool](& git status --porcelain); sourceFiles = @($sourceHashes); runtimeIdentifier = $config.runtimeIdentifier; runtimeVersion = $config.runtimeVersion; publicReleaseReady = [bool]$PublicRelease; pendingChecks = @($pendingChecks.id); deferredChecks = @($readiness.deferredChecks); signing = 'unsigned'; distribution = 'installer-primary-portable-secondary' }
    Write-Json $manifest (Join-Path $app 'BUILD.json')
    $materials = Join-Path $releaseRoot 'source-materials'
    New-Item -ItemType Directory -Path $materials -Force | Out-Null
    Compress-Archive -Path (Join-Path $snapshot '*') -DestinationPath (Join-Path $materials 'md2word-source.zip')
    # Compress-Archive忽略隐藏文件，显式补入Git忽略规则。
    $sourceZip = [IO.Compression.ZipFile]::Open((Join-Path $materials 'md2word-source.zip'), [IO.Compression.ZipArchiveMode]::Update)
    try {
        if (-not $sourceZip.GetEntry('.gitignore')) { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($sourceZip, (Join-Path $snapshot '.gitignore'), '.gitignore') | Out-Null }
    } finally { $sourceZip.Dispose() }
    Copy-Item -LiteralPath $sourceArchive, 'third_party/pandoc/3.10/SOURCE.md', 'third_party/pandoc/3.10/SHA256SUMS.txt', 'release/RELEASE-STATUS.md' -Destination $materials
    Write-Json $manifest (Join-Path $materials 'BUILD.json')
    function Write-PayloadManifest {
      Get-ChildItem -LiteralPath $app -Recurse -File | Where-Object Name -ne 'FILES.json' | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path = [IO.Path]::GetRelativePath($app, $_.FullName).Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(); fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($_.FullName).FileVersion }
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $app 'FILES.json') -Encoding utf8
    }
    Write-PayloadManifest
    if (-not $SkipInstaller) {
        & (Join-Path $PSScriptRoot 'build-installer.ps1') -PayloadDirectory $app -OutputDirectory $releaseRoot -AppVersion $appVersion -AppFileVersion $appFileVersion
    }
    'Portable mode: settings.json is stored beside the program.' | Set-Content (Join-Path $app 'portable.flag') -Encoding ascii
    Copy-Item -LiteralPath 'release/便携版使用说明.txt' -Destination $app
    Write-PayloadManifest
    Compress-Archive -LiteralPath $app -DestinationPath (Join-Path $releaseRoot "$appName.zip")
    & (Join-Path $PSScriptRoot 'measure-size.ps1') -PayloadDirectory $app -ReleaseDirectory $releaseRoot
    $checksums = foreach ($file in @(Get-ChildItem $releaseRoot -File | Where-Object Extension -in @('.zip', '.exe')) + @(Get-ChildItem $materials -File | Sort-Object Name)) {
        ((Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()) + '  ' + ([IO.Path]::GetRelativePath($releaseRoot, $file.FullName).Replace('\', '/'))
    }
    $checksums | Set-Content (Join-Path $releaseRoot 'SHA256SUMS.txt') -Encoding utf8
    Write-Output "候选包已生成：$releaseRoot"
} finally {
    Pop-Location
    foreach ($proxyName in $proxyBackup.Keys) { [Environment]::SetEnvironmentVariable($proxyName, $proxyBackup[$proxyName], 'Process') }
}
