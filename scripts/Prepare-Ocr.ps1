# Explicit development preparation only. Build/publish and application startup never download OCR assets.
[CmdletBinding()]
param(
    [string] $VcpkgExecutable,
    [string] $Dumpbin
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'OcrPackaging.psm1') -Force
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$lockPath = Join-Path $repo 'packaging/ocr.lock.json'
$pins = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
$manifest = Get-Content -LiteralPath (Join-Path $repo 'vcpkg.json') -Raw | ConvertFrom-Json
if ($manifest.'builtin-baseline' -ne $pins.vcpkgBaseline) { throw 'OCR lock and vcpkg baseline disagree.' }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$VcpkgExecutable) { $VcpkgExecutable = Join-Path $visualStudio 'VC/vcpkg/vcpkg.exe' }
if (!$Dumpbin) {
    $compiler = Get-ChildItem -LiteralPath (Join-Path $visualStudio 'VC/Tools/MSVC') -Directory |
        Sort-Object { [version] $_.Name } -Descending | Select-Object -First 1
    $Dumpbin = Join-Path $compiler.FullName 'bin/Hostx64/x64/dumpbin.exe'
}
$artifacts = Join-Path $repo 'artifacts'
$installed = Join-Path $artifacts 'ocr-vcpkg'
$oldConcurrency = $env:VCPKG_MAX_CONCURRENCY
try {
    $env:VCPKG_MAX_CONCURRENCY = '4'
    & $VcpkgExecutable install --triplet $pins.triplet --x-feature=portable-ocr "--x-manifest-root=$repo" "--x-install-root=$installed"
    if ($LASTEXITCODE -ne 0) { throw 'Pinned OCR dependency build failed.' }
}
finally { $env:VCPKG_MAX_CONCURRENCY = $oldConcurrency }
$packages = Join-Path $installed $pins.triplet
$executable = Join-Path $packages 'tools/tesseract/tesseract.exe'
if (!(Test-Path -LiteralPath $executable -PathType Leaf)) { throw "Pinned OCR CLI missing after vcpkg preparation: $executable." }
Assert-OcrProvenance (Join-Path $packages 'share/tesseract/vcpkg.spdx.json') $executable $pins
$imports = @(Get-OcrBinaryImports $executable $Dumpbin)
$version = Invoke-OcrCli $executable @('--version')
if ($version -notmatch ('(?m)^tesseract ' + [regex]::Escape($pins.tesseractVersion) + '(?:\s|$)')) {
    throw "Unexpected Tesseract version: $version"
}
$statusPath = Join-Path $installed 'vcpkg/status'
$status = Get-Content -LiteralPath $statusPath -Raw
if ($status -notmatch ('(?ms)^Package: tesseract\r?\nVersion: ' + [regex]::Escape($pins.tesseractVersion) + '\r?\n.*?Architecture: ' + [regex]::Escape($pins.triplet))) {
    throw 'Installed vcpkg status does not contain the pinned Tesseract package.'
}

$downloads = Join-Path $artifacts 'ocr-downloads'
New-Item -ItemType Directory -Force -Path $downloads | Out-Null
function Get-VerifiedDownload([string] $Name, [string] $Url, [string] $Hash) {
    $destination = Join-Path $downloads $Name
    if (!(Test-Path -LiteralPath $destination -PathType Leaf)) {
        $temporary = $destination + '.download-' + [Guid]::NewGuid().ToString('N')
        Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $temporary
        if ((Get-OcrHash $temporary) -ne $Hash) { throw "Upstream hash mismatch for $Url. Untrusted download retained at $temporary; not staged." }
        Move-Item -LiteralPath $temporary -Destination $destination
    }
    if ((Get-OcrHash $destination) -ne $Hash) { throw "Cached download hash mismatch: $destination. Nothing was staged." }
    return $destination
}
$model = Get-VerifiedDownload 'eng.traineddata' $pins.modelUrl $pins.modelSha256
$license = Get-VerifiedDownload 'LICENSE' $pins.modelLicenseUrl $pins.modelLicenseSha256
$candidate = Join-Path $artifacts ('ocr-prepared-' + [Guid]::NewGuid().ToString('N'))
foreach ($child in @('tessdata', 'licenses', 'provenance')) {
    New-Item -ItemType Directory -Path (Join-Path $candidate $child) -Force | Out-Null
}
Copy-Item -LiteralPath $executable -Destination (Join-Path $candidate 'tesseract.exe')
Copy-Item -LiteralPath $model -Destination (Join-Path $candidate 'tessdata/eng.traineddata')
Copy-Item -LiteralPath $license -Destination (Join-Path $candidate 'licenses/tessdata-fast.txt')
Copy-Item -LiteralPath $statusPath -Destination (Join-Path $candidate 'provenance/vcpkg-status.txt')
# Keep the redistributable notices and source provenance for every installed target package,
# including statically linked dependencies. Extra manifest dependency notices are harmless.
$packageNames = @($status -split '(?:\r?\n){2}' | ForEach-Object {
    if ($_ -match ('(?m)^Architecture: ' + [regex]::Escape($pins.triplet) + '\r?$') -and
        $_ -match '(?m)^Status: install ok installed\r?$' -and $_ -match '(?m)^Package: ([a-z0-9-]+)\r?$') { $Matches[1] }
} | Sort-Object -Unique)
foreach ($packageName in $packageNames) {
    $packagePath = Join-Path $packages "share/$packageName"
    $copyright = Join-Path $packagePath 'copyright'
    if (!(Test-Path -LiteralPath $copyright -PathType Leaf)) { throw "Missing redistributable license: $copyright" }
    Copy-Item -LiteralPath $copyright -Destination (Join-Path $candidate ("licenses/$packageName.txt"))
    $provenance = Join-Path $packagePath 'vcpkg.spdx.json'
    if (Test-Path -LiteralPath $provenance -PathType Leaf) {
        Copy-Item -LiteralPath $provenance -Destination (Join-Path $candidate ("provenance/$packageName.spdx.json"))
    }
    $abi = Join-Path $packagePath 'vcpkg_abi_info.txt'
    if (Test-Path -LiteralPath $abi -PathType Leaf) {
        Copy-Item -LiteralPath $abi -Destination (Join-Path $candidate ("provenance/$packageName.abi.txt"))
    }
}
$files = [ordered]@{}
foreach ($file in Get-ChildItem -LiteralPath $candidate -Recurse -File | Sort-Object FullName) {
    $files[$file.FullName.Substring($candidate.Length + 1).Replace('\', '/')] = Get-OcrHash $file.FullName
}
$receipt = [ordered]@{ format = 1; lockSha256 = (Get-OcrHash $lockPath);
    manifestSha256 = (Get-OcrHash (Join-Path $repo 'vcpkg.json')); tesseractVersion = $pins.tesseractVersion;
    triplet = $pins.triplet; imports = $imports; versionOutput = $version.Trim(); files = $files }
$receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $candidate 'bundle.json') -Encoding UTF8
$null = Assert-OcrBundle $candidate $lockPath
$null = Test-OcrFixture $candidate (Join-Path $artifacts ('ocr-fixture-' + [Guid]::NewGuid().ToString('N')))
$destination = [IO.Path]::GetFullPath((Join-Path $artifacts 'ocr'))
if ($destination -ne [IO.Path]::GetFullPath((Join-Path $repo 'artifacts/ocr')) -or
    !$candidate.StartsWith([IO.Path]::GetFullPath($artifacts) + [IO.Path]::DirectorySeparatorChar) -or
    !(Split-Path -Leaf $candidate).StartsWith('ocr-prepared-')) {
    throw 'Refusing to move a bundle outside the task artifacts directory.'
}
if (Test-Path -LiteralPath $destination) {
    $backup = Join-Path $artifacts ('ocr-previous-' + [Guid]::NewGuid().ToString('N'))
    Move-Item -LiteralPath $destination -Destination $backup
    Write-Output "Previous prepared bundle retained at $backup"
}
Move-Item -LiteralPath $candidate -Destination $destination
Write-Output "Prepared and fixture-tested Tesseract $($pins.tesseractVersion) + pinned English tessdata_fast at $destination. Imports: $($imports -join ', ')."
