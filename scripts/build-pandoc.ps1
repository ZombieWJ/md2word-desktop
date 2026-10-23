#Requires -Version 7.2
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$config = Get-Content (Join-Path $root 'release/pandoc-build.json') -Raw | ConvertFrom-Json
$indexState = if ($config.indexState -is [datetime]) { $config.indexState.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ', [Globalization.CultureInfo]::InvariantCulture) } else { [string]$config.indexState }
if ((& ghc --numeric-version).Trim() -ne $config.ghcVersion -or (& cabal --numeric-version).Trim() -ne $config.cabalVersion) { throw 'Haskell tool versions do not match the pinned configuration.' }
$work = Join-Path $root 'artifacts/pandoc-build'
if (Test-Path -LiteralPath $work) { throw 'Pandoc build directory already exists; use a fresh build workspace.' }
$materials = Join-Path $work 'materials'
$source = Join-Path $materials 'source'
$provenance = Join-Path $materials 'provenance'
$toolchain = Join-Path $materials 'toolchain-source'
New-Item -ItemType Directory -Path $source, $provenance, $toolchain -Force | Out-Null
function Download-Verified([string]$Url, [string]$Hash, [string]$Destination) {
    Invoke-WebRequest -Uri $Url -OutFile $Destination -TimeoutSec 300
    if ((Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Hash) { throw "Source archive checksum failed: $([IO.Path]::GetFileName($Destination))" }
}
$archive = Join-Path $source 'pandoc-3.10.tar.gz'
Download-Verified $config.sourceUrl $config.sourceSha256 $archive
tar -xzf $archive -C $source
if ($LASTEXITCODE -ne 0) { throw 'Pandoc source extraction failed.' }
foreach ($item in $config.compilerSources) {
    Download-Verified $item.url $item.sha256 (Join-Path $toolchain ([Uri]$item.url).Segments[-1])
}
foreach ($relative in @('release/pandoc-build.json', 'scripts/build-pandoc.ps1', 'scripts/pandoc_materials.py', '.github/workflows/pandoc-source.yml')) {
    Copy-Item -LiteralPath (Join-Path $root $relative) -Destination $provenance
}
& ghc --info | Set-Content (Join-Path $provenance 'ghc-info.txt') -Encoding utf8
& ghc-pkg dump --global | Set-Content (Join-Path $provenance 'ghc-global-packages.txt') -Encoding utf8
$env:CABAL_DIR = Join-Path $root '.tools/pandoc-cabal'
$env:CABAL_CONFIG = Join-Path $env:CABAL_DIR 'config'
New-Item -ItemType Directory -Path $env:CABAL_DIR -Force | Out-Null
& cabal user-config init --force
if ($LASTEXITCODE -ne 0) { throw 'Cabal configuration failed.' }
& cabal update
if ($LASTEXITCODE -ne 0) { throw 'Hackage secure index update failed.' }
Push-Location (Join-Path $source 'pandoc-3.10')
try {
    & cabal build pandoc-cli:exe:pandoc --dry-run -fembed_data_files -fserver -flua "--index-state=$indexState" "--builddir=$work/solver"
    if ($LASTEXITCODE -ne 0) { throw 'Dependency resolution failed.' }
} finally { Pop-Location }
Copy-Item -LiteralPath (Join-Path $work 'solver/cache/plan.json') -Destination (Join-Path $provenance 'resolved-plan.json')
& python (Join-Path $PSScriptRoot 'pandoc_materials.py') prepare --work $work --cabal-root $env:CABAL_DIR
if ($LASTEXITCODE -ne 0) { throw 'Dependency source collection failed.' }
Push-Location $source
try {
    & cabal build pandoc-cli:exe:pandoc --offline --project-file=cabal.project "--builddir=$work/offline-build" -j2
    if ($LASTEXITCODE -ne 0) { throw 'Building from archived local sources failed.' }
    & cabal freeze --offline --project-file=cabal.project "--builddir=$work/offline-build"
    if ($LASTEXITCODE -ne 0) { throw 'Recording dependency constraints failed.' }
    $binary = (& cabal list-bin pandoc-cli:exe:pandoc --offline --project-file=cabal.project "--builddir=$work/offline-build").Trim()
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $binary)) { throw 'Built Pandoc binary not found.' }
} finally { Pop-Location }
Copy-Item -LiteralPath (Join-Path $work 'offline-build/cache/plan.json') -Destination (Join-Path $provenance 'built-plan.json')
$binaryDirectory = Join-Path $work 'binary/pandoc-3.10'
New-Item -ItemType Directory -Path $binaryDirectory -Force | Out-Null
Copy-Item -LiteralPath $binary -Destination (Join-Path $binaryDirectory 'pandoc.exe')
$executable = Join-Path $binaryDirectory 'pandoc.exe'
& $executable --version | Tee-Object -FilePath (Join-Path $provenance 'pandoc-version.txt')
if ($LASTEXITCODE -ne 0) { throw 'Built Pandoc cannot run.' }
& $executable -s (Join-Path $source 'pandoc-3.10/COPYING.md') -t rtf -o (Join-Path $binaryDirectory 'COPYING.rtf')
if ($LASTEXITCODE -ne 0) { throw 'License output failed.' }
& $executable -s --toc (Join-Path $source 'pandoc-3.10/MANUAL.txt') -o (Join-Path $binaryDirectory 'MANUAL.html')
if ($LASTEXITCODE -ne 0) { throw 'Manual output failed.' }
Copy-Item -LiteralPath (Join-Path $source 'pandoc-3.10/COPYRIGHT') -Destination (Join-Path $binaryDirectory 'COPYRIGHT.txt')
Copy-Item -LiteralPath (Join-Path $source 'pandoc-3.10/COPYING.md') -Destination $binaryDirectory
& python (Join-Path $PSScriptRoot 'pandoc_materials.py') package --work $work
if ($LASTEXITCODE -ne 0) { throw 'Source/binary bundle verification failed.' }
