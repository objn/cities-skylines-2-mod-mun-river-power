$ErrorActionPreference = 'Stop'
$gameData = Join-Path $env:USERPROFILE 'AppData\LocalLow\Colossal Order\Cities Skylines II'
$destination = Join-Path $PSScriptRoot ('Diagnostics-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$null = New-Item -ItemType Directory -Path $destination
foreach ($relative in @('version','Logs\MunRiverPower.log','ModsData\MunRiverPower\economic-profiles.json')) {
    $file = Join-Path $gameData $relative
    if (Test-Path -LiteralPath $file) { Copy-Item -LiteralPath $file -Destination $destination }
}
foreach ($name in @('Player.log','Player-prev.log')) {
    $file = Join-Path $gameData $name
    if (Test-Path -LiteralPath $file) {
        Get-Content -LiteralPath $file -Tail 300 | Set-Content -LiteralPath (Join-Path $destination ($name + '.tail.txt'))
    }
}
$modding = Join-Path $gameData 'Logs\Modding.log'
if (Test-Path -LiteralPath $modding) {
    Select-String -LiteralPath $modding -Pattern 'MunRiverPower|MunRiver|WeirPrefabSystem|WeirFactory' -Context 0,5 |
        Out-String | Set-Content -LiteralPath (Join-Path $destination 'Modding-excerpt.txt')
}
Write-Output "Saved local diagnostics to $destination. Nothing was uploaded."
