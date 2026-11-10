#Requires -Version 7.0
<#
One-time, reproducible compatibility repair of the vendored ServerSync binary.
Not a runtime hook or a normal build step. Requires Mono.Cecil 0.11.6.
Only the three obsolete ZRoutedRpc.Everybody loads in the known input are changed.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $InputAssembly,
    [Parameter(Mandatory)] [string] $OutputAssembly,
    [Parameter(Mandatory)] [string] $GameAssembly,
    [Parameter(Mandatory)] [string] $CecilPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$expected = '166956302A294E224474B26F4C7D58409084AD3F48BD0AF1FEB7551F229C8F60'
if ((Get-FileHash -LiteralPath $InputAssembly -Algorithm SHA256).Hash -ne $expected) {
    throw 'Unexpected ServerSync input. Review a new binary rather than applying this repair blindly.'
}
Add-Type -Path (Resolve-Path -LiteralPath $CecilPath).Path
if ([Mono.Cecil.ModuleDefinition].Assembly.GetName().Version.ToString() -ne '0.11.6.0') {
    throw 'This repair was verified with Mono.Cecil 0.11.6 only.'
}
$game = [Mono.Cecil.ModuleDefinition]::ReadModule((Resolve-Path -LiteralPath $GameAssembly).Path)
$library = [Mono.Cecil.ModuleDefinition]::ReadModule((Resolve-Path -LiteralPath $InputAssembly).Path)
try {
    $field = $game.Types | Where-Object FullName -eq 'ZRoutedRpc' |
        ForEach-Object Fields | Where-Object Name -eq 'Everybody'
    if ($null -eq $field -or -not $field.IsLiteral -or $field.FieldType.FullName -ne 'System.Int64' -or $field.Constant -ne 0) {
        throw 'Expected original Valheim 1.0 Int64 constant ZRoutedRpc.Everybody = 0.'
    }
    function Get-AllTypes($Types) {
        foreach ($type in $Types) { $type; Get-AllTypes $type.NestedTypes }
    }
    $count = 0
    foreach ($type in (Get-AllTypes $library.Types)) {
        foreach ($method in $type.Methods) {
            if (-not $method.HasBody) { continue }
            foreach ($instruction in $method.Body.Instructions) {
                $operand = $instruction.Operand
                if ($operand -isnot [Mono.Cecil.FieldReference] -or
                    $operand.FullName -ne 'System.Int64 ZRoutedRpc::Everybody' -or
                    $operand.DeclaringType.Scope.Name -ne 'assembly_valheim') { continue }
                if ($instruction.OpCode.Code -ne [Mono.Cecil.Cil.Code]::Ldsfld) {
                    throw 'Unexpected Everybody access; refusing to change it.'
                }
                $instruction.OpCode = [Mono.Cecil.Cil.OpCodes]::Ldc_I8
                $instruction.Operand = [long]$field.Constant
                $count++
                Write-Output "Repaired: $($method.FullName)"
            }
        }
    }
    if ($count -ne 3) { throw "Expected exactly 3 loads, found $count." }
    $writer = [Mono.Cecil.WriterParameters]::new()
    $writer.DeterministicMvid = $true
    $library.Write([IO.Path]::GetFullPath($OutputAssembly), $writer)
} finally {
    $library.Dispose()
    $game.Dispose()
}
Get-FileHash -LiteralPath $OutputAssembly -Algorithm SHA256
