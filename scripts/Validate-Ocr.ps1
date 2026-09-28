[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $Directory,
    [string] $LockPath
)
$ErrorActionPreference = 'Stop'
if (!$LockPath) { $LockPath = Join-Path $PSScriptRoot '../packaging/ocr.lock.json' }
Import-Module (Join-Path $PSScriptRoot 'OcrPackaging.psm1') -Force
$null = Assert-OcrBundle $Directory $LockPath
