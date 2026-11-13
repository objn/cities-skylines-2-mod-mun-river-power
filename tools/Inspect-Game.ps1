param([string]$Type, [string]$Method, [switch]$IL, [string]$Assembly = 'Game')
$ErrorActionPreference = 'Stop'
$managed = 'F:\Cities Skylines II\Cities2_Data\Managed'
$null = [Reflection.Assembly]::LoadFrom("$managed\Colossal.Mono.Cecil.dll")
$asm = [Colossal.Mono.Cecil.AssemblyDefinition]::ReadAssembly("$managed\$Assembly.dll")
function Show-Type($t) {
    if ($t.FullName -match $Type) {
        $t.FullName
        if (-not $Method) { $t.Fields | ForEach-Object { '  ' + $_.FullName } }
        $t.Methods | Where-Object { -not $Method -or $_.Name -match $Method } | ForEach-Object {
            '  ' + $_.FullName
            if ($IL) { $_.Body.Instructions | ForEach-Object ToString }
        }
    }
    $t.NestedTypes | ForEach-Object { Show-Type $_ }
}
$asm.MainModule.Types | ForEach-Object { Show-Type $_ }
