param([string]$GamePath = 'F:\Cities Skylines II')
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:MSBuildEnableWorkloadResolver = 'false'
dotnet build (Join-Path $PSScriptRoot 'src\MunRiverPower\MunRiverPower.csproj') -c Release --nologo "-p:GamePath=$GamePath"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $PSScriptRoot 'dist\MunRiverPower\README.md')
foreach ($name in @('Install-Local.ps1','Install.cmd','TESTING.md','SPECIFICATION.md','Collect-Diagnostics.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $PSScriptRoot "dist\MunRiverPower\$name")
}
