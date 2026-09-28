# No desktop observation, GUI activation, or runtime downloads: only owned files and the bundled CLI.
[CmdletBinding()]
param([string] $Directory)
$ErrorActionPreference = 'Stop'
if (!$Directory) { $Directory = Join-Path $PSScriptRoot '../artifacts/ocr' }
Import-Module (Join-Path $PSScriptRoot 'OcrPackaging.psm1') -Force
$lockPath = Join-Path $PSScriptRoot '../packaging/ocr.lock.json'
$receipt = Assert-OcrBundle $Directory $lockPath
$checks = Join-Path $PSScriptRoot ('../artifacts/ocr-check-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $checks | Out-Null
function Assert-Rejected([string] $Path, [string] $Expected) {
    try { $null = Assert-OcrBundle $Path $lockPath }
    catch {
        if ($_.Exception.Message -notlike "*$Expected*") { throw }
        return
    }
    throw "Expected OCR validation failure: $Expected"
}
function Copy-TestBundle([string] $Name) {
    $copy = Join-Path $checks $Name
    Copy-Item -LiteralPath $Directory -Destination $copy -Recurse
    return $copy
}
Assert-Rejected (Join-Path $checks 'missing-bundle') 'OCR bundle missing or invalid'
foreach ($file in @('tesseract.exe', 'tessdata/eng.traineddata', 'licenses/tesseract.txt', 'licenses/tessdata-fast.txt')) {
    $copy = Copy-TestBundle ('missing-' + [Guid]::NewGuid().ToString('N'))
    # Move one owned fixture aside; never alter the real prepared bundle.
    Move-Item -LiteralPath (Join-Path $copy $file) -Destination (Join-Path $checks ([Guid]::NewGuid().ToString('N')))
    Assert-Rejected $copy 'Missing or changed file'
}
foreach ($file in @('tesseract.exe', 'tessdata/eng.traineddata')) {
    $copy = Copy-TestBundle ('corrupt-' + [Guid]::NewGuid().ToString('N'))
    Add-Content -LiteralPath (Join-Path $copy $file) -Value 'owned corruption fixture'
    Assert-Rejected $copy 'Missing or changed file'
}
$copy = Copy-TestBundle 'stale-receipt'
$stale = Get-Content -LiteralPath (Join-Path $copy 'bundle.json') -Raw | ConvertFrom-Json
$stale.lockSha256 = '0' * 64
$stale | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $copy 'bundle.json') -Encoding UTF8
Assert-Rejected $copy 'receipt is stale'
$copy = Copy-TestBundle 'stale-extra-file'
Set-Content -LiteralPath (Join-Path $copy 'vcomp140.dll') -Value 'owned stale dependency fixture'
Assert-Rejected $copy 'Untracked/stale file'
foreach ($mutation in @('port', 'source', 'binary')) {
    $copy = Copy-TestBundle "stale-provenance-$mutation"
    $spdxPath = Join-Path $copy 'provenance/tesseract.spdx.json'
    $spdx = Get-Content -LiteralPath $spdxPath -Raw | ConvertFrom-Json
    switch ($mutation) {
        'port' { ($spdx.packages | Where-Object SPDXID -EQ 'SPDXRef-port').downloadLocation = 'file:///unapproved-overlay' }
        'source' { ($spdx.packages | Where-Object name -EQ 'tesseract-ocr/tesseract').checksums[0].checksumValue = '0' * 128 }
        'binary' { ($spdx.files | Where-Object fileName -EQ './tools/tesseract/tesseract.exe').checksums[0].checksumValue = '0' * 64 }
    }
    $spdx | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $spdxPath -Encoding UTF8
    # A fresh receipt must not bless stale/overlaid package provenance.
    $changed = Get-Content -LiteralPath (Join-Path $copy 'bundle.json') -Raw | ConvertFrom-Json
    $changed.files.'provenance/tesseract.spdx.json' = Get-OcrHash $spdxPath
    $changed | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $copy 'bundle.json') -Encoding UTF8
    Assert-Rejected $copy 'provenance'
}
foreach ($dll in @('vcomp140.dll', 'libomp.dll', 'msvcp140.dll', 'vcruntime140.dll', 'not-inbox.dll')) {
    $rejected = $false
    try { Assert-OcrWindowsImports @('kernel32.dll', $dll) }
    catch { $rejected = $_.Exception.Message -like '*non-inbox DLL*' }
    if (!$rejected) { throw "Non-portable dependency was accepted: $dll" }
}
Assert-OcrWindowsImports @('KERNEL32.dll', 'USER32.dll', 'api-ms-win-core-synch-l1-2-0.dll')
$fixture = Test-OcrFixture $Directory (Join-Path $checks 'fixture')
Write-Output "BMP-FIXTURE:$fixture"
Write-Output "PASS: portable OCR hashes, missing/corrupt/stale assets, non-inbox dependencies and generated fixture OCR ($checks)."
