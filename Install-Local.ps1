param([string]$Destination = "$env:USERPROFILE\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\MunRiverPower")
$ErrorActionPreference = 'Stop'
if (Get-Process -Name Cities2 -ErrorAction SilentlyContinue) { throw 'Close Cities: Skylines II before installing.' }
$package = $PSScriptRoot
$source = Join-Path $package 'MunRiverPower.dll'
if (-not (Test-Path -LiteralPath $source)) {
    $package = Join-Path $PSScriptRoot 'dist\MunRiverPower'
    $source = Join-Path $package 'MunRiverPower.dll'
}
if (-not (Test-Path -LiteralPath $source)) { throw 'Run Build.ps1 first.' }
$null = New-Item -ItemType Directory -Path $Destination -Force
foreach ($name in @('MunRiverPower.dll', 'MunRiverPower.pdb', 'README.md', 'SPECIFICATION.th.md')) {
    $file = Join-Path $package $name
    if (Test-Path -LiteralPath $file) { Copy-Item -LiteralPath $file -Destination (Join-Path $Destination $name) -Force }
}
Write-Output "Installed local development mod: $Destination"
Write-Output ('Version: ' + [Diagnostics.FileVersionInfo]::GetVersionInfo($source).FileVersion)
