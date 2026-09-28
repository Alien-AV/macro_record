Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-OcrHash([string] $Path) {
    $stream = [IO.File]::OpenRead($Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose(); $stream.Dispose() }
}

function Assert-OcrWindowsImports([string[]] $Imports) {
    # Windows 10 inbox libraries only. VC/OpenMP runtimes are deliberately NOT on this list.
    $windows = @('kernel32.dll', 'user32.dll', 'advapi32.dll', 'ws2_32.dll', 'crypt32.dll',
        'bcrypt.dll', 'ncrypt.dll', 'secur32.dll', 'normaliz.dll', 'wldap32.dll', 'iphlpapi.dll',
        'shell32.dll', 'ole32.dll', 'oleaut32.dll', 'gdi32.dll', 'comdlg32.dll', 'winmm.dll',
        'rpcrt4.dll', 'ntdll.dll', 'version.dll', 'shlwapi.dll', 'userenv.dll', 'msvcrt.dll')
    if (!$Imports -or $Imports.Count -eq 0) { throw 'OCR executable has no audited PE imports.' }
    foreach ($dll in $Imports) {
        if ($dll -notin $windows -and $dll -notmatch '^api-ms-win-[a-z0-9-]+\.dll$') {
            throw "OCR executable requires non-inbox DLL '$dll'. Rebuild with static CRT/libraries and OpenMP disabled; do not rely on PATH or a globally installed runtime."
        }
    }
}

function Get-OcrBinaryImports([string] $Executable, [string] $Dumpbin) {
    $image = [IO.File]::ReadAllBytes($Executable)
    $pe = [BitConverter]::ToInt32($image, 0x3c)
    if ($image.Length -lt $pe + 6 -or [BitConverter]::ToUInt16($image, $pe + 4) -ne 0x8664) {
        throw 'OCR executable is not an x64 PE image.'
    }
    $output = & $Dumpbin /nologo /dependents $Executable 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Could not audit OCR executable imports: $output" }
    # /DEPENDENTS includes both normal and delay-loaded DLLs.
    $imports = @($output | ForEach-Object {
        if ("$_" -match '^\s+([a-zA-Z0-9_.-]+\.dll)\s*$') { $Matches[1].ToLowerInvariant() }
    } | Sort-Object -Unique)
    Assert-OcrWindowsImports $imports
    return $imports
}

function Assert-OcrProvenance([string] $SpdxPath, [string] $Executable, $Pins) {
    $spdx = Get-Content -LiteralPath $SpdxPath -Raw | ConvertFrom-Json
    $port = @($spdx.packages | Where-Object SPDXID -EQ 'SPDXRef-port')
    $source = @($spdx.packages | Where-Object name -EQ 'tesseract-ocr/tesseract')
    $binary = @($spdx.files | Where-Object fileName -EQ './tools/tesseract/tesseract.exe')
    if ($port.Count -ne 1 -or $port[0].versionInfo -ne $Pins.tesseractVersion -or
        $port[0].downloadLocation -ne "git+https://github.com/microsoft/vcpkg@$($Pins.tesseractPortTree)" -or
        $source.Count -ne 1 -or $source[0].downloadLocation -ne "git+https://github.com/tesseract-ocr/tesseract@$($Pins.tesseractVersion)") {
        throw 'Tesseract provenance does not match the pinned official port/source. Overlays or stale installs cannot be packaged.'
    }
    $sourceHash = @($source[0].checksums | Where-Object algorithm -EQ 'SHA512')
    if ($sourceHash.Count -ne 1 -or $sourceHash[0].checksumValue -ne $Pins.tesseractSourceSha512) {
        throw 'Tesseract provenance source hash does not match the pinned upstream archive.'
    }
    if ($binary.Count -ne 1) { throw 'Tesseract provenance does not identify the CLI binary.' }
    $binaryHash = @($binary[0].checksums | Where-Object algorithm -EQ 'SHA256')
    if ($binaryHash.Count -ne 1 -or $binaryHash[0].checksumValue -ne (Get-OcrHash $Executable)) {
        throw 'Tesseract binary differs from installed source/build provenance.'
    }
}

function Assert-OcrBundle([string] $Directory, [string] $LockPath) {
    try {
        $pins = Get-Content -LiteralPath $LockPath -Raw | ConvertFrom-Json
        $manifestPath = Join-Path (Split-Path -Parent $LockPath) '../vcpkg.json'
        $receipt = Get-Content -LiteralPath (Join-Path $Directory 'bundle.json') -Raw | ConvertFrom-Json
        if ($receipt.format -ne 1 -or $receipt.lockSha256 -ne (Get-OcrHash $LockPath) -or
            $receipt.manifestSha256 -ne (Get-OcrHash $manifestPath) -or
            $receipt.tesseractVersion -ne $pins.tesseractVersion -or $receipt.triplet -ne $pins.triplet) {
            throw 'Preparation receipt is stale or incompatible with packaging/ocr.lock.json or vcpkg.json.'
        }
        Assert-OcrWindowsImports @($receipt.imports)
        $expected = @('tesseract.exe', 'tessdata/eng.traineddata', 'licenses/tesseract.txt',
            'licenses/tessdata-fast.txt', 'provenance/vcpkg-status.txt', 'provenance/tesseract.spdx.json')
        $entries = @($receipt.files.PSObject.Properties)
        foreach ($required in $expected) {
            if ($required -notin $entries.Name) { throw "Receipt is missing required file $required." }
        }
        foreach ($entry in $entries) {
            if ($entry.Name -notmatch '^[a-zA-Z0-9_-]+(?:/[a-zA-Z0-9_.-]+)*\.[a-zA-Z0-9]+$' -or
                $entry.Value -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid receipt file entry.' }
            $path = Join-Path $Directory $entry.Name
            if (!(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-OcrHash $path) -ne $entry.Value) {
                throw "Missing or changed file: $($entry.Name)."
            }
        }
        if ($receipt.files.'tessdata/eng.traineddata' -ne $pins.modelSha256 -or
            $receipt.files.'licenses/tessdata-fast.txt' -ne $pins.modelLicenseSha256) {
            throw 'English model or model license does not match the pinned upstream hash.'
        }
        Assert-OcrProvenance (Join-Path $Directory 'provenance/tesseract.spdx.json') (Join-Path $Directory 'tesseract.exe') $pins
        $root = [IO.Path]::GetFullPath($Directory).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
        foreach ($file in Get-ChildItem -LiteralPath $Directory -Recurse -File) {
            $relative = $file.FullName.Substring($root.Length).Replace('\', '/')
            if ($relative -ne 'bundle.json' -and $relative -notin $entries.Name) {
                throw "Untracked/stale file in OCR bundle: $relative."
            }
        }
        return $receipt
    }
    catch { throw "OCR bundle missing or invalid: $Directory. $($_.Exception.Message) Run powershell -NoProfile -File scripts/Prepare-Ocr.ps1 before building or publishing." }
}

function Invoke-OcrCli([string] $Executable, [string[]] $Arguments) {
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $Executable
    $start.Arguments = ($Arguments | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' }) -join ' '
    $start.WorkingDirectory = Split-Path -Parent $Executable
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    # Exercise the shipped tool, not a model/CLI/DLL found in developer machine settings.
    $start.EnvironmentVariables['PATH'] = [Environment]::SystemDirectory
    $start.EnvironmentVariables.Remove('TESSDATA_PREFIX')
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $start
    $started = $false
    try {
        $started = $process.Start()
        if (!$started) { throw 'Could not start the bundled OCR CLI.' }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (!$process.WaitForExit(30000)) { $process.Kill(); throw 'Bundled OCR CLI exceeded 30 seconds.' }
        $result = $stdout.GetAwaiter().GetResult()
        $errors = $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw "Bundled OCR CLI failed ($($process.ExitCode)): $errors" }
        return ($result + $errors)
    }
    finally {
        if ($started -and !$process.HasExited) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
    }
}

function Test-OcrFixture([string] $Directory, [string] $FixtureDirectory) {
    Add-Type -AssemblyName System.Drawing
    New-Item -ItemType Directory -Force -Path $FixtureDirectory | Out-Null
    $path = Join-Path $FixtureDirectory 'owned-ocr-fixture.png'
    $bitmap = New-Object Drawing.Bitmap 1100, 180
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $font = New-Object Drawing.Font 'Arial', 56, ([Drawing.FontStyle]::Regular), ([Drawing.GraphicsUnit]::Pixel)
    try {
        $graphics.Clear([Drawing.Color]::White)
        $graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        $graphics.DrawString('PORTABLE OCR READY 12345', $font, [Drawing.Brushes]::Black, 25, 50)
        $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
        # Match the backend contract: top-down 32-bit BGRA BMP on stdin, not a captured screen.
        $pixels = $bitmap.LockBits((New-Object Drawing.Rectangle 0, 0, $bitmap.Width, $bitmap.Height),
            [Drawing.Imaging.ImageLockMode]::ReadOnly, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            if ($pixels.Stride -ne $bitmap.Width * 4) { throw 'Unexpected owned fixture stride.' }
            $bmp = New-Object byte[] (54 + $pixels.Stride * $bitmap.Height)
            $bmp[0] = 66; $bmp[1] = 77
            [BitConverter]::GetBytes([int]$bmp.Length).CopyTo($bmp, 2)
            [BitConverter]::GetBytes([int]54).CopyTo($bmp, 10)
            [BitConverter]::GetBytes([int]40).CopyTo($bmp, 14)
            [BitConverter]::GetBytes([int]$bitmap.Width).CopyTo($bmp, 18)
            [BitConverter]::GetBytes([int](-$bitmap.Height)).CopyTo($bmp, 22)
            [BitConverter]::GetBytes([int16]1).CopyTo($bmp, 26)
            [BitConverter]::GetBytes([int16]32).CopyTo($bmp, 28)
            [BitConverter]::GetBytes([int]($bmp.Length - 54)).CopyTo($bmp, 34)
            [Runtime.InteropServices.Marshal]::Copy($pixels.Scan0, $bmp, 54, $bmp.Length - 54)
        }
        finally { $bitmap.UnlockBits($pixels) }
    }
    finally { $font.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    $result = Invoke-OcrCli (Join-Path $Directory 'tesseract.exe') @($path, 'stdout',
        '--tessdata-dir', (Join-Path $Directory 'tessdata'), '-l', 'eng', '--oem', '1', '--psm', '7')
    if ($result.Trim() -ne 'PORTABLE OCR READY 12345') { throw "Fixture OCR mismatch: $result" }
    $bmpPath = Join-Path $FixtureDirectory 'owned-ocr-fixture.bmp'
    [IO.File]::WriteAllBytes($bmpPath, $bmp)
    $result = Invoke-OcrCli (Join-Path $Directory 'tesseract.exe') @($bmpPath, 'stdout',
        '--tessdata-dir', (Join-Path $Directory 'tessdata'), '-l', 'eng', '--psm', '6')
    if ($result.Trim() -ne 'PORTABLE OCR READY 12345') { throw "BMP fixture OCR mismatch: $result" }
    return [IO.Path]::GetFullPath($bmpPath)
}

Export-ModuleMember -Function Get-OcrHash, Assert-OcrWindowsImports, Get-OcrBinaryImports, Assert-OcrProvenance, Assert-OcrBundle, Invoke-OcrCli, Test-OcrFixture
