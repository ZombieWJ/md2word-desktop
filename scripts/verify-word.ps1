param(
    [Parameter(Mandatory)][string[]]$Documents,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$word = $null
$currentDocument = $null
$report = @()
$hadOpenDocuments = $false
try {
    $word = New-Object -ComObject Word.Application
    $hadOpenDocuments = $word.Documents.Count -gt 0
    $originalAlerts = $word.DisplayAlerts
    $originalSecurity = $word.AutomationSecurity
    $originalUpdateLinks = $word.Options.UpdateLinksAtOpen
    $word.DisplayAlerts = 0
    $word.AutomationSecurity = 3
    $word.Options.UpdateLinksAtOpen = $false
    foreach ($documentPath in $Documents) {
        $fullPath = (Resolve-Path -LiteralPath $documentPath).Path
        $stem = [IO.Path]::GetFileNameWithoutExtension($fullPath)
        $renderDirectory = Join-Path $outputRoot $stem
        New-Item -ItemType Directory -Path $renderDirectory -Force | Out-Null
        $pdf = Join-Path $renderDirectory ($stem + '.pdf')
        $currentDocument = $word.Documents.Open($fullPath, $false, $true, $false)
        $currentDocument.Repaginate()
        $pages = $currentDocument.ComputeStatistics(2)
        $currentDocument.ExportAsFixedFormat($pdf, 17)
        $currentDocument.Close(0)
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($currentDocument)
        $currentDocument = $null
        $report += [pscustomobject][ordered]@{ document = $fullPath; pdf = $pdf; wordReportedPages = $pages; wordVersion = $word.Version; wordBuild = $word.Build; sourceOpenedReadOnly = $true; globalFieldUpdateRequested = $false }
    }
}
finally {
    if ($currentDocument) { $currentDocument.Close(0); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($currentDocument) }
    if ($word) {
        $word.Options.UpdateLinksAtOpen = $originalUpdateLinks
        $word.AutomationSecurity = $originalSecurity
        $word.DisplayAlerts = $originalAlerts
        if (-not $hadOpenDocuments) { $word.Quit(0) }
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($word)
    }
}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputRoot 'word-render.json') -Encoding utf8
$report | Format-Table document,wordReportedPages,wordVersion,wordBuild
