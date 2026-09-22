# Run after building macro_record.sln in the selected configuration. Never launches the app.
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'MacroRecorderGUI/MacroRecorderGUI.csproj'
$checkDirectory = Join-Path $repo ('artifacts/publish-check-' + [Guid]::NewGuid().ToString('N'))
$publishDirectory = Join-Path $checkDirectory 'publish'
$missingNativeDirectory = Join-Path $checkDirectory 'missing-native'
$properties = & dotnet msbuild $project "-p:Configuration=$Configuration" -p:Platform=x64 `
    -p:SelfContained=true -getProperty:TargetDir,NativeRuntimePath
if ($LASTEXITCODE -ne 0) { throw 'Could not evaluate publish paths.' }
$paths = ($properties -join "`n" | ConvertFrom-Json).Properties
if (!(Test-Path -LiteralPath $paths.NativeRuntimePath -PathType Leaf)) {
    throw "Build macro_record.sln for $Configuration|x64 before running this check."
}

$publishArguments = @('publish', $project, '-c', $Configuration, '-p:Platform=x64',
    '-r', 'win-x64', '--self-contained', 'true', '--no-build', '--no-restore',
    '-o', $publishDirectory, '--verbosity', 'minimal')

function Assert-MissingNativeFails {
    $output = & dotnet @publishArguments "-p:NativeOutputDirectory=$missingNativeDirectory" 2>&1
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

Assert-MissingNativeFails
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed.' }

Assert-SameFile $paths.NativeRuntimePath (Join-Path $publishDirectory 'RecordPlaybackDLL.dll')
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
& dotnet @publishArguments "-p:NativeOutputDirectory=$alternateNativeDirectory"
if ($LASTEXITCODE -ne 0) { throw 'Republish with an alternate native directory failed.' }
Assert-SameFile $alternateNative $publishedNative
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
Write-Output "PASS ($Configuration): missing native runtime rejected; native DLL, PRI, $($xbfFiles.Count) XBF files, and self-contained runtimes verified in $publishDirectory"
exit 0
