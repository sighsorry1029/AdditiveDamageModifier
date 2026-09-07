#Requires -Version 7.0
<#
Runs bounded regression checks against an already-built mod and installed game DLLs.
Run in a fresh pwsh process. Does not call Awake, PatchAll, Unity native APIs or save config.
Gameplay-config binding/ServerSync, full localization loading and game/UI/network behavior
still require Valheim. Static contract checks below are explicitly labelled separately.
#>
[CmdletBinding()]
param (
    [string] $AssemblyPath = (Join-Path $PSScriptRoot 'bin\Debug\AdditiveDamageModifier.dll'),
    [string] $GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$AssemblyPath = (Resolve-Path -LiteralPath $AssemblyPath).Path
$managedPath = Join-Path $GamePath 'valheim_Data\Managed'
$corePath = Join-Path $GamePath 'BepInEx\core'
$dependencyPaths = @($managedPath, $corePath, (Split-Path $AssemblyPath))
$static = [Reflection.BindingFlags]'Static,Public,NonPublic'
$all = [Reflection.BindingFlags]'Static,Instance,Public,NonPublic'
$script:checks = 0

function Assert-Check([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw "FAIL: $Message" }
    $script:checks++
}

function Assert-Near([single] $Actual, [single] $Expected, [string] $Message) {
    Assert-Check ([Math]::Abs($Actual - $Expected) -lt 0.0001) "$Message (actual $Actual, expected $Expected)"
}

function Invoke-Static([Type] $Type, [string] $Name, [object[]] $Arguments = @()) {
    $method = $Type.GetMethod($Name, $static)
    if ($null -eq $method) { throw "Missing test target: $($Type.FullName).$Name" }
    return $method.Invoke($null, $Arguments)
}

function Find-GameType([string] $Name) {
    foreach ($gameAssembly in $gameAssemblies) {
        $found = $gameAssembly.GetType($Name, $false)
        if ($null -ne $found) { return $found }
    }
    throw "Missing game type: $Name"
}

function Type-Signature([Type] $Type) {
    if ($Type.IsGenericType) {
        return $Type.GetGenericTypeDefinition().FullName + '[' + ((@($Type.GetGenericArguments() | ForEach-Object { Type-Signature $_ })) -join ',') + ']'
    }
    return $Type.FullName
}

# Never resolve framework assemblies from Unity's Managed folder: use pwsh's runtime.
$resolver = [ResolveEventHandler] {
    param($sender, $eventArgs)
    $name = ([Reflection.AssemblyName]::new($eventArgs.Name)).Name
    if ($name -in @('mscorlib', 'netstandard', 'System') -or $name.StartsWith('System.')) { return $null }
    foreach ($directory in $dependencyPaths) {
        $candidate = Join-Path $directory ($name + '.dll')
        if (Test-Path -LiteralPath $candidate) { return [Reflection.Assembly]::LoadFrom($candidate) }
    }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)

try {
    $gameAssemblies = @('assembly_valheim', 'assembly_guiutils', 'assembly_utils') | ForEach-Object {
        [Reflection.Assembly]::LoadFrom((Join-Path $managedPath ($_ + '.dll')))
    }
    $mod = [Reflection.Assembly]::LoadFrom($AssemblyPath)
    Write-Output "Testing $AssemblyPath ($($mod.GetName().Version))"
    $math = $mod.GetType('AdditiveDamageModifier.AdditiveDamageMath', $true)
    $modifierType = Find-GameType 'HitData+DamageModifier'
    $normal = [Enum]::Parse($modifierType, 'Normal')
    $ignore = [Enum]::Parse($modifierType, 'Ignore')
    $immune = [Enum]::Parse($modifierType, 'Immune')

    function Encode([single] $Delta) { Invoke-Static $math 'EncodeCustomDelta' @($Delta) }
    function Delta($Modifier) { Invoke-Static $math 'ModifierToDelta' @($Modifier) }
    function Combine($Left, $Right) { Invoke-Static $math 'Combine' @($Left, $Right) }
    function Damage($Modifier, [single] $Minimum) {
        $arguments = [object[]]@([single]100, $Modifier, $Minimum, [single]0, [single]0, [single]0, [single]0)
        $amount = $math.GetMethod('ApplyModifier', $static).Invoke($null, $arguments)
        return @{ Amount = $amount; Normal = $arguments[3]; Resistant = $arguments[4]; Weak = $arguments[5]; Immune = $arguments[6] }
    }

    # Actual math implementation; encoded values avoid requiring live synced ConfigEntries.
    foreach ($value in @(-100000, -1.25, -1, -0.45, -0.3, -0.15, 0, 0.15, 0.3, 1.25, 100000)) {
        Assert-Near (Delta (Encode $value)) $value "Encoded delta round trip $value"
    }
    foreach ($raw in @(999999999, 1200000001)) {
        Assert-Check (-not (Invoke-Static $math 'IsCustomModifier' @([Enum]::ToObject($modifierType, $raw)))) "Custom encoding rejects $raw"
    }
    Assert-Near (Delta (Encode -100001)) -100000 'Encoding negative saturation'
    Assert-Near (Delta (Encode 100001)) 100000 'Encoding positive saturation'
    $first = Encode -0.45
    $second = Encode -0.3
    $third = Encode 0.3
    foreach ($order in @(@($first, $second, $third), @($first, $third, $second), @($second, $first, $third),
                         @($second, $third, $first), @($third, $first, $second), @($third, $second, $first))) {
        Assert-Near (Delta (Combine (Combine $order[0] $order[1]) $order[2])) -0.45 'Stacking permutation'
    }
    Assert-Check ((Combine $normal $first) -eq $first -and (Combine $first $normal) -eq $first) 'Normal identity'
    Assert-Check ((Combine $ignore $first) -eq $ignore -and (Combine $first $ignore) -eq $ignore) 'Ignore dominates either order'
    Assert-Near (Delta (Combine $immune (Encode 0.3))) -0.7 'Actual Immune can be offset by weakness'
    Assert-Near (Delta (Combine (Encode -0.01) (Encode -0.14))) -0.15 'Percent-step threshold precision'
    Assert-Near (Damage $ignore 0.5).Amount 0 'Ignore bypasses player minimum'
    Assert-Near (Damage $immune 0.1).Amount 10 'Actual Immune respects player minimum'
    Assert-Near (Damage (Encode -1.3) 0).Amount 0 'Uncapped target reaches zero'
    Assert-Near (Damage (Encode -1.3) 0.1).Amount 10 'Player minimum applies after sum'
    Assert-Near (Damage (Encode 0.3) 0.1).Amount 130 'Weakness damage'
    Assert-Near (Damage (Encode 0) 0).Normal 100 'Normal damage classification'
    Assert-Near (Damage (Encode -0.3) 0).Resistant 100 'Resistance damage classification'
    Assert-Near (Damage (Encode 0.3) 0).Weak 100 'Weakness damage classification'
    Assert-Near (Damage (Encode -1) 0).Immune 100 'Immune damage classification'
    Write-Output 'PASS: real-DLL math (encoded contributions, Immune/Ignore, caps, encoding and order)'

    $context = $mod.GetType('AdditiveDamageModifier.DamageCapContext', $true)
    $hitType = Find-GameType 'HitData'
    $hitA = [Activator]::CreateInstance($hitType)
    $hitB = [Activator]::CreateInstance($hitType)
    $hitC = [Activator]::CreateInstance($hitType)
    Invoke-Static $context 'EnterPlayerContext' @($hitA)
    Invoke-Static $context 'EnterPlayerContext' @($hitB)
    Assert-Check (Invoke-Static $context 'IsPlayerContext' @($hitB)) 'Nested hit is current'
    Assert-Check (-not (Invoke-Static $context 'IsPlayerContext' @($hitA))) 'Outer hit is shadowed'
    Invoke-Static $context 'ExitPlayerContext' @($hitC)
    Assert-Check (Invoke-Static $context 'IsPlayerContext' @($hitB)) 'Unmatched exit preserves nested hit'
    Invoke-Static $context 'ExitPlayerContext' @($hitB)
    Assert-Check (Invoke-Static $context 'IsPlayerContext' @($hitA)) 'Nested exit restores outer hit'
    $stack = $context.GetField('_playerHitStack', $static).GetValue($null)
    Invoke-Static $context 'ExitPlayerContext' @($hitA)
    Assert-Check ([object]::ReferenceEquals($stack, $context.GetField('_playerHitStack', $static).GetValue($null))) 'Empty context retains reusable storage'
    Invoke-Static $context 'EnterPlayerContext' @($hitA)
    Invoke-Static $context 'EnterPlayerContext' @($hitA)
    Invoke-Static $context 'ExitPlayerContext' @($hitA)
    Assert-Check (Invoke-Static $context 'IsPlayerContext' @($hitA)) 'Repeated HitData enters unwind one at a time'
    $rpc = $mod.GetType('AdditiveDamageModifier.CharacterRpcDamagePlayerCapPatch', $true)
    $exception = [InvalidOperationException]::new('Regression test exception')
    $returned = Invoke-Static $rpc 'Finalizer' @($hitA, $exception)
    Assert-Check ([object]::ReferenceEquals($returned, $exception)) 'Finalizer preserves exception identity'
    Assert-Check (-not (Invoke-Static $context 'IsPlayerContext' @($hitA))) 'Finalizer cleans captured original HitData'
    Invoke-Static $rpc 'Finalizer' @($null, $null)
    Assert-Check (-not (Invoke-Static $context 'IsPlayerContext' @($hitA))) 'Null state finalizer is harmless'
    Write-Output 'PASS: real-DLL damage context nesting, same-object reuse and exception cleanup'

    $localizer = $mod.GetType('LocalizationManager.Localizer', $true)
    $translations = @{}
    foreach ($language in @('English', 'Korean')) {
        $source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot "Resources\Translations\$language.yml"))
        $translations[$language] = Invoke-Static $localizer 'Deserialize' @($source, "test $language")
        $stream = $mod.GetManifestResourceStream("AdditiveDamageModifier.translations.$language.yml")
        Assert-Check ($null -ne $stream) "Embedded $language resource exists"
        $reader = [IO.StreamReader]::new($stream)
        try { $embedded = $reader.ReadToEnd() } finally { $reader.Dispose() }
        Assert-Check ($embedded -ceq $source) "Embedded $language resource matches current source"
    }
    $english = $translations.English
    $korean = $translations.Korean
    Assert-Check (@(Compare-Object @($english.Keys) @($korean.Keys)).Count -eq 0) 'Translation key sets match'
    foreach ($key in $english.Keys) {
        $enArguments = @([regex]::Matches($english[$key], '\{[0-9]+\}') | ForEach-Object Value | Sort-Object)
        $koArguments = @([regex]::Matches($korean[$key], '\{[0-9]+\}') | ForEach-Object Value | Sort-Object)
        Assert-Check (($enArguments -join ',') -ceq ($koArguments -join ',')) "Translation placeholders match: $key"
    }
    $override = Invoke-Static $localizer 'Deserialize' @('"$adm_tooltip_net_label": "Custom net"
adm_tooltip_min_total_label: " "
not_a_mod_key: "Unexpected"', 'test override')
    $minimumBefore = $english['adm_tooltip_min_total_label']
    Invoke-Static $localizer 'Merge' @($english, $override)
    Assert-Check ($english['adm_tooltip_net_label'] -ceq 'Custom net') 'Known normalized token is overridden'
    Assert-Check ($english['adm_tooltip_min_total_label'] -ceq $minimumBefore) 'Blank override retains English fallback'
    Assert-Check (-not $english.ContainsKey('not_a_mod_key')) 'Unknown override key is ignored'
    $malformedRejected = $false
    try { $null = Invoke-Static $localizer 'Deserialize' @('key: [unterminated', 'malformed test') } catch { $malformedRejected = $true }
    Assert-Check $malformedRejected 'Malformed YAML is rejected by actual parser'
    Write-Output 'PASS: real-DLL YAML parser/known-key merge, embedded resources, keys and placeholders'

    # Data and source contracts: these checks do not execute config binding or Unity registration.
    $definitions = $mod.GetType('AdditiveDamageModifier.AdditiveDamageDefinitions', $true)
    $damageTypes = $definitions.GetField('DamageTypes', $static).GetValue($null)
    $modifierDefinitions = $definitions.GetField('DamageModifiers', $static).GetValue($null)
    $expectedTypes = @('Blunt', 'Pierce', 'Slash', 'Chop', 'Pickaxe', 'Fire', 'Poison', 'Frost', 'Lightning', 'Spirit')
    Assert-Check ((@($damageTypes | ForEach-Object DisplayName) -join '|') -ceq ($expectedTypes -join '|')) 'Persisted damage-type display names/order'
    $expectedModifiers = @('Very Weak', 'Weak', 'Slightly Weak', 'Slightly Resistant', 'Resistant', 'Very Resistant', 'Immune')
    Assert-Check ((@($modifierDefinitions | ForEach-Object DisplayName) -join '|') -ceq ($expectedModifiers -join '|')) 'Persisted modifier display names/order'
    Assert-Check ((@($damageTypes | Where-Object HasPlayerMinimumCap | ForEach-Object DisplayName) -join '|') -ceq 'Blunt|Pierce|Slash|Fire|Poison|Frost|Lightning') 'Player minimum types exclude Spirit/Chop/Pickaxe'
    $catalog = $mod.GetType('AdditiveDamageModifier.AdditiveDamageStatusEffectCatalog', $true)
    $actualNames = @(foreach ($damageType in $damageTypes | Where-Object HasStatusEffect) {
        foreach ($modifier in $modifierDefinitions) { Invoke-Static $catalog 'GetStatusEffectName' @($damageType.StatusName, $modifier.StatusName) }
    })
    $expectedNames = @(foreach ($damageName in @('blunt', 'pierce', 'slash', 'fire', 'poison', 'frost', 'lightning', 'spirit')) {
        foreach ($modifierName in @('very_weak', 'weak', 'slightly_weak', 'slightly_resistant', 'resistant', 'very_resistant', 'immune')) {
            "adm_${damageName}_${modifierName}"
        }
    })
    Assert-Check ($actualNames.Count -eq 56 -and @(Compare-Object $actualNames $expectedNames).Count -eq 0) 'Exact 56 external adm_ status names'
    Assert-Check (@($actualNames | Sort-Object -Unique).Count -eq 56) 'External status names are unique'
    $pluginSource = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Plugin.cs'))
    foreach ($literal in @('1 - General', '2 - Additive Damage', '3 - Fall Damage', 'Lock Configuration',
        'Show Modifier Percent in Tooltips Outside Compendium', 'Maximum Fall Damage', 'Fall Damage Multiplier',
        'Cold/Freezing Immunity Trigger Frost Delta Percent',
        'Minimum Damage Taken Cap Percent on Player - {definition.DisplayName}',
        'Player Minimum Damage Taken Percent - {definition.DisplayName}', '{definition.DisplayName} Percent')) {
        Assert-Check ($pluginSource.Contains('"' + $literal + '"')) "Static config key/template: $literal"
    }
    Write-Output 'PASS: definition data and static config-key/status-name contracts (not config migration execution)'

    # Resolve all current Harmony targets by their exact parameter types, without applying patches.
    $targetSpecs = @(
        @('Character', 'RPC_Damage', 'System.Int64,HitData'),
        @('Character', 'UpdateGroundContact', 'System.Single'),
        @('HitData+DamageModifiers', 'ApplyIfBetter', 'HitData+DamageModifier&,HitData+DamageModifier'),
        @('HitData', 'ApplyResistance', 'HitData+DamageModifiers,HitData+DamageModifier&'),
        @('HitData', 'ApplyModifier', 'System.Single,HitData+DamageModifier,System.Single&,System.Single&,System.Single&,System.Single&'),
        @('Player', 'UpdateEnvStatusEffects', 'System.Single'),
        @('ItemDrop+ItemData', 'GetTooltip', 'ItemDrop+ItemData,System.Int32,System.Boolean,System.Single,System.Int32'),
        @('SE_Stats', 'GetTooltipString', ''),
        @('SE_Stats', 'GetDamageModifiersTooltipString', 'System.Collections.Generic.List`1[HitData+DamageModPair]'),
        @('Hud', 'UpdateStatusEffects', 'System.Collections.Generic.List`1[StatusEffect]'),
        @('TextsDialog', 'AddActiveEffects', ''), @('TextsDialog', 'UpdateTextsList', ''),
        @('ObjectDB', 'Awake', ''), @('ObjectDB', 'CopyOtherDB', 'ObjectDB'),
        @('Localization', 'SetupLanguage', 'System.String')
    )
    foreach ($spec in $targetSpecs) {
        $targetType = Find-GameType $spec[0]
        $matches = @($targetType.GetMethods($all) | Where-Object {
            $_.Name -eq $spec[1] -and ((@($_.GetParameters() | ForEach-Object { Type-Signature $_.ParameterType }) -join ',') -ceq $spec[2])
        })
        Assert-Check ($matches.Count -eq 1) "Exact Harmony target: $($spec[0]).$($spec[1])"
    }
    $prefixState = $rpc.GetMethod('Prefix', $static).GetParameters() | Where-Object Name -eq '__state'
    Assert-Check ($null -ne $prefixState -and $prefixState.IsOut -and $prefixState.ParameterType -eq $hitType.MakeByRefType()) 'RPC Prefix captures HitData in Harmony __state'
    $finalizerNames = @($rpc.GetMethod('Finalizer', $static).GetParameters() | ForEach-Object Name)
    Assert-Check (($finalizerNames -join ',') -ceq '__state,__exception') 'RPC Finalizer uses captured state, not mutable hit argument'
    Write-Output 'PASS: actual-game target signatures and mod Harmony state metadata (patch application not executed)'
    Write-Output "PASS: $script:checks checks. No Valheim launch, live config/ServerSync, UI, ownership or network tests were performed."
} finally {
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}
