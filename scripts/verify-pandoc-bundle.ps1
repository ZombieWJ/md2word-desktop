#Requires -Version 7.2
param([Parameter(Mandatory)][string]$BundleDirectory)
$ErrorActionPreference = 'Stop'
$bundleRoot = [IO.Path]::GetFullPath($BundleDirectory)
$root = Split-Path -Parent $PSScriptRoot
$pinned = Get-Content (Join-Path $root 'release/pandoc-build.json') -Raw | ConvertFrom-Json
$metadata = Get-Content (Join-Path $bundleRoot 'BUNDLE.json') -Raw | ConvertFrom-Json
if ($metadata.provider -ne 'md2word-source-build' -or $metadata.version -ne $pinned.version -or $metadata.sourceCommit -ne $pinned.sourceCommit -or $metadata.ghcVersion -ne $pinned.ghcVersion -or $metadata.cabalVersion -ne $pinned.cabalVersion -or -not $metadata.remoteRepositoriesDisabled -or -not $metadata.correspondingSourceComplete -or $metadata.configuredPackages.Count -eq 0 -or $metadata.compilerPackages.Count -eq 0) { throw 'Pandoc source-build record does not match the pinned configuration.' }
$sourcePath = Join-Path $bundleRoot 'pandoc-3.10-corresponding-sources.zip'
$binaryPath = Join-Path $bundleRoot 'pandoc-3.10-md2word-win-x64.zip'
foreach ($item in @(@($sourcePath, $metadata.sourceArchiveSha256), @($binaryPath, $metadata.binaryArchiveSha256))) {
    if ((Get-FileHash -LiteralPath $item[0] -Algorithm SHA256).Hash.ToLowerInvariant() -ne $item[1]) { throw 'Pandoc bundle archive checksum mismatch.' }
}
function Read-Entry($Zip, [string]$Name) {
    $entry = $Zip.GetEntry($Name)
    if (-not $entry) { throw "Missing source material: $Name" }
    $reader = [IO.StreamReader]::new($entry.Open())
    try { $reader.ReadToEnd() } finally { $reader.Dispose() }
}
$sourceZip = [IO.Compression.ZipFile]::OpenRead($sourcePath)
try {
    $inventory = (Read-Entry $sourceZip 'FILES.json') | ConvertFrom-Json
    if ($sourceZip.Entries.Count -ne $inventory.Count + 1) { throw 'Source archive file inventory is incomplete.' }
    foreach ($file in $inventory) {
        if ($file.path -match '(^/|^[A-Za-z]:|(^|/)\.\.(/|$)|\\)') { throw 'Source inventory path is not relative.' }
        $entry = $sourceZip.GetEntry($file.path)
        if (-not $entry) { throw 'Source inventory entry is missing.' }
        $stream = $entry.Open()
        try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() } finally { $stream.Dispose() }
        if ($hash -ne $file.sha256) { throw 'Source inventory checksum mismatch.' }
    }
    $build = (Read-Entry $sourceZip 'provenance/BUILD.json') | ConvertFrom-Json
    if ($build.exeSha256 -ne $metadata.exeSha256 -or $build.builderCommit -ne $metadata.builderCommit -or -not $build.correspondingSourceComplete) { throw 'Source/binary provenance mismatch.' }
    $project = Read-Entry $sourceZip 'source/cabal.project'
    if ($project -notmatch '(?m)^active-repositories:\s*:none\s*$') { throw 'Rebuild project does not disable remote repositories.' }
    $dependencies = (Read-Entry $sourceZip 'provenance/dependency-sources.json') | ConvertFrom-Json -AsHashtable
    foreach ($identity in $dependencies.Keys) {
        $package = $dependencies[$identity]
        foreach ($name in @("source/vendor/$identity/$($package.name).cabal", "dependency-archives/$identity.tar.gz", "dependency-archives/$identity.cabal")) {
            $file = @($inventory | Where-Object path -eq $name)
            $expectedHash = if ($name.EndsWith('.tar.gz')) { $package.tarSha256 } else { $package.cabalSha256 }
            if ($file.Count -ne 1 -or $file[0].sha256 -ne $expectedHash) { throw 'Resolved dependency source or revision is missing.' }
        }
    }
    foreach ($compiler in $pinned.compilerSources) {
        $name = 'toolchain-source/' + ([Uri]$compiler.url).Segments[-1]
        $file = @($inventory | Where-Object path -eq $name)
        if ($file.Count -ne 1 -or $file[0].sha256 -ne $compiler.sha256) { throw 'Compiler source archive is missing or changed.' }
    }
} finally { $sourceZip.Dispose() }
$binaryZip = [IO.Compression.ZipFile]::OpenRead($binaryPath)
try {
    $entry = $binaryZip.GetEntry('pandoc-3.10/pandoc.exe')
    if (-not $entry) { throw 'Pandoc executable missing.' }
    $stream = $entry.Open()
    try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() } finally { $stream.Dispose() }
    if ($hash -ne $metadata.exeSha256) { throw 'Pandoc executable source-build checksum mismatch.' }
    $binaryMetadata = (Read-Entry $binaryZip 'pandoc-3.10/SOURCE.json') | ConvertFrom-Json
    if ($binaryMetadata.sourceArchiveSha256 -ne $metadata.sourceArchiveSha256) { throw 'Bundled source reference mismatch.' }
    foreach ($name in @('pandoc-3.10/COPYING.md', 'pandoc-3.10/COPYING.rtf', 'pandoc-3.10/COPYRIGHT.txt', 'pandoc-3.10/MANUAL.html')) {
        if (-not $binaryZip.GetEntry($name)) { throw 'Pandoc license or manual missing.' }
    }
    if (@($binaryZip.Entries | Where-Object FullName -Like 'pandoc-3.10/licenses/*').Count -eq 0) { throw 'Dependency license texts missing.' }
} finally { $binaryZip.Dispose() }
Write-Output "Pandoc corresponding-source bundle verified: $($metadata.exeSha256)"
