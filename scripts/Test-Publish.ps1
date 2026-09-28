# Run after building macro_record.sln in the selected configuration. Never launches the app.
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'OcrPackaging.psm1') -Force
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'MacroRecorderGUI/MacroRecorderGUI.csproj'
$checkDirectory = Join-Path $repo ('artifacts/publish-check-' + [Guid]::NewGuid().ToString('N'))
$publishDirectory = Join-Path $checkDirectory 'publish'
$missingNativeDirectory = Join-Path $checkDirectory 'missing-native'
$properties = & dotnet msbuild $project "-p:Configuration=$Configuration" -p:Platform=x64 `
    -p:SelfContained=true -getProperty:TargetDir,NativeRuntimePath,OcrBundleDirectory
if ($LASTEXITCODE -ne 0) { throw 'Could not evaluate publish paths.' }
$paths = ($properties -join "`n" | ConvertFrom-Json).Properties
if (!(Test-Path -LiteralPath $paths.NativeRuntimePath -PathType Leaf)) {
    throw "Build macro_record.sln for $Configuration|x64 before running this check."
}

$publishArguments = @('publish', $project, '-c', $Configuration, '-p:Platform=x64',
    '-r', 'win-x64', '--self-contained', 'true',
    '-o', $publishDirectory, '--verbosity', 'minimal')

function Assert-MissingNativeFails {
    $output = & dotnet @publishArguments --no-build --no-restore "-p:NativeOutputDirectory=$missingNativeDirectory" 2>&1
    if ($LASTEXITCODE -eq 0 -or ($output -join "`n") -notmatch 'Native runtime missing:') {
        throw "Publish must reject a missing native runtime with an actionable error.`n$($output -join "`n")"
    }
}

function Assert-SameFile([string] $Source, [string] $Destination) {
    if (!(Test-Path -LiteralPath $Destination -PathType Leaf) -or
        (Get-FileHash -LiteralPath $Source).Hash -ne (Get-FileHash -LiteralPath $Destination).Hash) {
        throw "Published file is missing or differs from its build output: $Destination"
    }
}

function Assert-OcrSourceFails([string] $Source) {
    $output = & dotnet @publishArguments --no-build --no-restore "-p:OcrBundleDirectory=$Source" 2>&1
    if ($LASTEXITCODE -eq 0 -or ($output -join "`n") -notmatch 'OCR bundle missing or invalid:') {
        throw "Publish must reject missing/stale OCR source despite an existing destination bundle.`n$($output -join "`n")"
    }
}

function Assert-SelfContainedPublish([string] $Directory) {
    $runtimeOptions = (Get-Content -LiteralPath (Join-Path $Directory 'MacroRecorderGUI.runtimeconfig.json') -Raw |
        ConvertFrom-Json).runtimeOptions
    if ($runtimeOptions.PSObject.Properties.Name -contains 'framework' -or
        $runtimeOptions.PSObject.Properties.Name -contains 'frameworks') {
        throw 'Published runtimeconfig still requires a shared framework.'
    }
    $coreFramework = @($runtimeOptions.includedFrameworks | Where-Object { $_.name -eq 'Microsoft.NETCore.App' })
    if ($coreFramework.Count -ne 1 -or !$coreFramework[0].version) {
        throw 'Published runtimeconfig must include Microsoft.NETCore.App with a version.'
    }

    $deps = Get-Content -LiteralPath (Join-Path $Directory 'MacroRecorderGUI.deps.json') -Raw | ConvertFrom-Json
    $runtimeTarget = $deps.runtimeTarget.name
    $runtimePack = 'runtimepack.Microsoft.NETCore.App.Runtime.win-x64/' + $coreFramework[0].version
    $runtimeAssets = $deps.targets.$runtimeTarget.$runtimePack
    if ($runtimeTarget -notlike '*/win-x64' -or !$deps.libraries.$runtimePack -or
        !$runtimeAssets.runtime.'System.Private.CoreLib.dll' -or !$runtimeAssets.native.'coreclr.dll') {
        throw "Published deps must include $runtimePack and its managed/native runtime assets for win-x64."
    }
}

Assert-MissingNativeFails
& (Join-Path $PSScriptRoot 'Test-OcrPackaging.ps1') -Directory $paths.OcrBundleDirectory
Assert-OcrSourceFails (Join-Path $checkDirectory 'missing-ocr')
# Build with the publish settings to regenerate runtime metadata after an ordinary solution build.
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed.' }
Assert-SelfContainedPublish $publishDirectory

Assert-SameFile $paths.NativeRuntimePath (Join-Path $publishDirectory 'RecordPlaybackDLL.dll')
$ocrLock = Join-Path $repo 'packaging/ocr.lock.json'
$null = Assert-OcrBundle (Join-Path $paths.TargetDir 'ocr') $ocrLock
$null = Assert-OcrBundle (Join-Path $publishDirectory 'ocr') $ocrLock
Test-OcrFixture (Join-Path $publishDirectory 'ocr') (Join-Path $checkDirectory 'published-fixture')
# A stale/newer OCR destination must be replaced by the verified source, including the model.
foreach ($relative in @('tesseract.exe', 'tessdata/eng.traineddata')) {
    $destination = Join-Path $publishDirectory "ocr/$relative"
    Copy-Item -LiteralPath (Join-Path $paths.OcrBundleDirectory 'licenses/tessdata-fast.txt') -Destination $destination
    (Get-Item -LiteralPath $destination).LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(1)
}
# An alternate native source must replace a different, newer destination binary.
$alternateNativeDirectory = Join-Path $checkDirectory 'alternate-native'
New-Item -ItemType Directory -Path $alternateNativeDirectory | Out-Null
$alternateNative = Join-Path $alternateNativeDirectory 'RecordPlaybackDLL.dll'
Copy-Item -LiteralPath $paths.NativeRuntimePath -Destination $alternateNative
(Get-Item -LiteralPath $alternateNative).LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-1)
$publishedNative = Join-Path $publishDirectory 'RecordPlaybackDLL.dll'
Copy-Item -LiteralPath (Join-Path $paths.TargetDir 'MacroRecorderGUI.dll') -Destination $publishedNative
(Get-Item -LiteralPath $publishedNative).LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(1)
# Exercise an explicit native directory without a trailing separator.
& dotnet @publishArguments --no-build --no-restore "-p:NativeOutputDirectory=$alternateNativeDirectory"
if ($LASTEXITCODE -ne 0) { throw 'Republish with an alternate native directory failed.' }
Assert-SelfContainedPublish $publishDirectory
Assert-SameFile $alternateNative $publishedNative
$receipt = Assert-OcrBundle (Join-Path $publishDirectory 'ocr') $ocrLock
foreach ($relative in @($receipt.files.PSObject.Properties.Name) + 'bundle.json') {
    Assert-SameFile (Join-Path $paths.OcrBundleDirectory $relative) (Join-Path $publishDirectory "ocr/$relative")
}
Test-OcrFixture (Join-Path $publishDirectory 'ocr') (Join-Path $checkDirectory 'republished-fixture')
$staleOcr = Join-Path $checkDirectory 'stale-ocr-source'
Copy-Item -LiteralPath $paths.OcrBundleDirectory -Destination $staleOcr -Recurse
Add-Content -LiteralPath (Join-Path $staleOcr 'tessdata/eng.traineddata') -Value 'owned stale source fixture'
Assert-OcrSourceFails $staleOcr
Assert-OcrSourceFails (Join-Path $checkDirectory 'missing-ocr')
Assert-SameFile (Join-Path $paths.TargetDir 'MacroRecorderGUI.pri') (Join-Path $publishDirectory 'MacroRecorderGUI.pri')
$xbfFiles = @(Get-ChildItem -LiteralPath $paths.TargetDir -Recurse -Filter '*.xbf' -File)
if ($xbfFiles.Count -eq 0) { throw 'No compiled WinUI resources were found in the build output.' }
foreach ($file in $xbfFiles) {
    $relativePath = $file.FullName.Substring($paths.TargetDir.Length)
    Assert-SameFile $file.FullName (Join-Path $publishDirectory $relativePath)
}
foreach ($file in @('MacroRecorderGUI.exe', 'MacroRecorderGUI.dll', 'MacroRecorderGUI.deps.json',
    'MacroRecorderGUI.runtimeconfig.json', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll',
    'Microsoft.UI.Xaml.dll', 'Microsoft.WindowsAppRuntime.dll', 'Microsoft.WindowsAppRuntime.pri')) {
    if (!(Test-Path -LiteralPath (Join-Path $publishDirectory $file) -PathType Leaf)) {
        throw "Self-contained publish is missing $file."
    }
}
# A DLL left in the destination by an earlier publish must not conceal a missing source.
Assert-MissingNativeFails
Write-Output "PASS ($Configuration): missing native/OCR and stale OCR sources rejected; native DLL, portable OCR + English fixture, PRI, $($xbfFiles.Count) XBF files, and self-contained runtimes verified in $publishDirectory"
exit 0
