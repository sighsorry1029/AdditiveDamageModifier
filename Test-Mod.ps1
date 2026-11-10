#Requires -Version 7.0
<#
Runs bounded regression checks against an already-built mod and installed game DLLs.
Run in a fresh pwsh process. Does not call Awake, PatchAll, Unity native APIs or save game/plugin config.
Creates and removes one empty temporary BepInEx core-config fixture for loader-registry checks.
Plugin config migration/ServerSync, full localization loading and game/UI/network behavior
still require Valheim. In-memory ConfigEntries exercise profile isolation without saving.
A managed-only ThreadingHelper fixture queues (but never runs)
ServerSync patch installation so config default lookups can be tested outside Unity.
Static contract checks below are explicitly labelled separately.
#>
[CmdletBinding()]
param (
    [string] $AssemblyPath = (Join-Path $PSScriptRoot 'bin\Debug\AdditiveDamageModifier.dll'),
    [string] $GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim',
    [string] $BepInExCorePath = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$AssemblyPath = (Resolve-Path -LiteralPath $AssemblyPath).Path
$managedPath = Join-Path $GamePath 'valheim_Data\Managed'
if (-not (Test-Path -LiteralPath $managedPath)) { $managedPath = Join-Path $GamePath 'valheim_server_Data\Managed' }
$corePath = if ($BepInExCorePath) { $BepInExCorePath } else { Join-Path $GamePath 'BepInEx\core' }
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

# Decode managed IL without installing detours or executing Unity methods.
$opCodes = @{}
foreach ($field in [Reflection.Emit.OpCodes].GetFields([Reflection.BindingFlags]'Public,Static')) {
    $op = $field.GetValue($null)
    $opCodes[[int]$op.Value -band 65535] = $op
}
function Get-MethodInstructions([Reflection.MethodBase] $Method) {
    $body = $Method.GetMethodBody()
    if ($null -eq $body) { return }
    $bytes = $body.GetILAsByteArray()
    $typeArguments = $Method.DeclaringType.GetGenericArguments()
    $methodArguments = if ($Method -is [Reflection.MethodInfo]) { $Method.GetGenericArguments() } else { [Type[]]@() }
    for ($offset = 0; $offset -lt $bytes.Length;) {
        $instructionOffset = $offset
        $value = [int]$bytes[$offset++]
        if ($value -eq 254) { $value = 65024 + [int]$bytes[$offset++] }
        $op = $opCodes[$value]
        if ($null -eq $op) { throw "Unknown IL opcode in $Method" }
        $operand = $null
        switch ($op.OperandType.ToString()) {
            'InlineNone' { }
            { $_ -in 'InlineMethod','InlineField','InlineType','InlineTok' } {
                $token = [BitConverter]::ToInt32($bytes, $offset); $offset += 4
                $operand = $Method.Module.ResolveMember($token, $typeArguments, $methodArguments)
            }
            'InlineSwitch' { $count = [BitConverter]::ToInt32($bytes, $offset); $offset += 4 + 4 * $count }
            { $_ -in 'ShortInlineI','ShortInlineVar','ShortInlineBrTarget' } { $operand = $bytes[$offset++] }
            'InlineVar' { $operand = [BitConverter]::ToUInt16($bytes, $offset); $offset += 2 }
            { $_ -in 'InlineI','InlineBrTarget','InlineString','InlineSig' } { $operand = [BitConverter]::ToInt32($bytes, $offset); $offset += 4 }
            'InlineI8' { $operand = [BitConverter]::ToInt64($bytes, $offset); $offset += 8 }
            'ShortInlineR' { $operand = [BitConverter]::ToSingle($bytes, $offset); $offset += 4 }
            'InlineR' { $operand = [BitConverter]::ToDouble($bytes, $offset); $offset += 8 }
            default { throw "Unsupported IL operand $($op.OperandType) in $Method" }
        }
        [PSCustomObject]@{ Offset = $instructionOffset; OpCode = $op; Operand = $operand }
    }
}

$threadingInstanceField = $null
$threadingFixtureInstalled = $false
$coreConfigFixture = $null
$coreConfigPathProperty = $null
$savedCoreConfigPath = $null
$savedCoreSaveOnSet = $false
$coreConfigReadPath = $null
$coreConfigOriginalHash = $null
$coreConfigOriginalWriteTime = $null
$coreConfigFixtureCreated = $false
try {
    $gameAssemblies = @('assembly_valheim', 'assembly_guiutils', 'assembly_utils') | ForEach-Object {
        [Reflection.Assembly]::LoadFrom((Join-Path $managedPath ($_ + '.dll')))
    }
    $mod = [Reflection.Assembly]::LoadFrom($AssemblyPath)
    $threadingType = [Reflection.Assembly]::LoadFrom((Join-Path $corePath 'BepInEx.dll')).GetType('BepInEx.ThreadingHelper', $true)
    $threadingInstanceField = $threadingType.GetField('<Instance>k__BackingField', $static)
    if ($null -ne $threadingInstanceField.GetValue($null)) { throw 'Run tests in a fresh process, not inside BepInEx.' }
    $threadingFixture = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($threadingType)
    $threadingType.GetField('_invokeLock', $all).SetValue($threadingFixture, [object]::new())
    $threadingInstanceField.SetValue($null, $threadingFixture)
    $threadingFixtureInstalled = $true
    Write-Output "Testing $AssemblyPath ($($mod.GetName().Version))"
    $math = $mod.GetType('AdditiveDamageModifier.AdditiveDamageMath', $true)
    $modifierType = Find-GameType 'HitData+DamageModifier'
    $normal = [Enum]::Parse($modifierType, 'Normal')
    $ignore = [Enum]::Parse($modifierType, 'Ignore')
    $immune = [Enum]::Parse($modifierType, 'Immune')
    $resistant = [Enum]::Parse($modifierType, 'Resistant')
    $weak = [Enum]::Parse($modifierType, 'Weak')
    $profileType = $mod.GetType('AdditiveDamageModifier.DamageModifierProfile', $true)
    $playerProfile = [Enum]::Parse($profileType, 'Player')
    $creatureProfile = [Enum]::Parse($profileType, 'NonPlayerCharacter')

    function Encode([single] $Delta) { Invoke-Static $math 'EncodeCustomDelta' @($Delta) }
    function Delta($Modifier, $Profile = $playerProfile) { Invoke-Static $math 'ModifierToDelta' @($Modifier, $Profile) }
    function Combine($Left, $Right, $Profile = $playerProfile) { Invoke-Static $math 'Combine' @($Left, $Right, $Profile) }
    function Damage($Modifier, [single] $Minimum, $Profile = $playerProfile) {
        $arguments = [object[]]@([single]100, $Modifier, $Minimum, $Profile, [single]0, [single]0, [single]0, [single]0)
        $amount = $math.GetMethod('ApplyModifier', $static).Invoke($null, $arguments)
        return @{ Amount = $amount; Normal = $arguments[4]; Resistant = $arguments[5]; Weak = $arguments[6]; Immune = $arguments[7] }
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

    $context = $mod.GetType('AdditiveDamageModifier.CharacterDamageContext', $true)
    $hitType = Find-GameType 'HitData'
    $hitA = [Activator]::CreateInstance($hitType)
    $hitB = [Activator]::CreateInstance($hitType)
    $hitC = [Activator]::CreateInstance($hitType)
    function Hit-Context($Hit) {
        $arguments = [object[]]@($Hit, $playerProfile)
        $active = $context.GetMethod('TryGetProfile', $static).Invoke($null, $arguments)
        return @{ Active = $active; Profile = $arguments[1] }
    }
    Assert-Check (-not (Hit-Context $hitA).Active) 'No implicit Character context'
    $outerDepth = Invoke-Static $context 'Enter' @($hitA, $playerProfile)
    $innerDepth = Invoke-Static $context 'Enter' @($hitB, $creatureProfile)
    Assert-Check ((Hit-Context $hitB).Active -and (Hit-Context $hitB).Profile -eq $creatureProfile) 'Nested hit has its own non-player profile'
    Assert-Check (-not (Hit-Context $hitA).Active) 'Outer hit is shadowed'
    Assert-Check (-not (Hit-Context $hitC).Active) 'Unrelated Other hit never inherits active Character profile'
    Invoke-Static $context 'Exit' @($innerDepth)
    Assert-Check ((Hit-Context $hitA).Active -and (Hit-Context $hitA).Profile -eq $playerProfile) 'Nested exit restores outer player profile'
    $stack = $context.GetField('_hitStack', $static).GetValue($null)
    Invoke-Static $context 'Exit' @($outerDepth)
    Assert-Check ([object]::ReferenceEquals($stack, $context.GetField('_hitStack', $static).GetValue($null))) 'Empty context retains reusable storage'
    $outerDepth = Invoke-Static $context 'Enter' @($hitA, $playerProfile)
    $innerDepth = Invoke-Static $context 'Enter' @($hitA, $creatureProfile)
    Assert-Check ((Hit-Context $hitA).Profile -eq $creatureProfile) 'Same HitData reentry can use another defender profile'
    Invoke-Static $context 'Exit' @($innerDepth)
    Assert-Check ((Hit-Context $hitA).Profile -eq $playerProfile) 'Same HitData reentry restores the previous profile'
    $exception = [InvalidOperationException]::new('Regression test exception')
    $caught = $null
    try { throw $exception } catch { $caught = $_.Exception } finally { Invoke-Static $context 'Exit' @($outerDepth) }
    Assert-Check ([object]::ReferenceEquals($caught, $exception)) 'Exception identity survives scope consumer failure'
    Assert-Check (-not (Hit-Context $hitA).Active) 'Depth-token cleanup removes exception-interrupted context'
    Write-Output 'PASS: real-DLL exact-hit/profile context nesting and reusable storage; wrapper finally inspected separately'

    # Exercise the full replacement, not just its shared arithmetic helper.
    $resistancePatch = $mod.GetType('AdditiveDamageModifier.HitDataApplyResistancePatch', $true)
    $damageModifiersType = Find-GameType 'HitData+DamageModifiers'
    foreach ($profile in @($playerProfile, $creatureProfile)) {
        foreach ($case in @(@($normal, 100), @($immune, 0), @($ignore, 0), @((Encode -0.3), 70))) {
            $testHit = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($hitType)
            $damage = $hitType.GetField('m_damage').GetValue($testHit)
            $damage.GetType().GetField('m_nonPlayer').SetValue($damage, [single]100)
            $hitType.GetField('m_damage').SetValue($testHit, $damage)
            $modifiers = [Activator]::CreateInstance($damageModifiersType)
            $damageModifiersType.GetField('m_nonPlayer').SetValue($modifiers, $case[0])
            $depth = Invoke-Static $context 'Enter' @($testHit, $profile)
            try {
                $arguments = [object[]]@($testHit, $modifiers, $normal)
                $runOriginal = $resistancePatch.GetMethod('Prefix', $static).Invoke($null, $arguments)
                Assert-Check (-not $runOriginal) 'ApplyResistance replacement skips original exactly once'
                $resultDamage = $hitType.GetField('m_damage').GetValue($testHit)
                Assert-Near $resultDamage.m_nonPlayer $case[1] "NonPlayer $($case[0]), profile $profile"
            } finally {
                Invoke-Static $context 'Exit' @($depth)
            }
        }
    }
    Write-Output 'PASS: 1.0 NonPlayer damage channel through actual Character replacement with both profiles'

    $damageType = Find-GameType 'HitData+DamageType'
    $pairType = Find-GameType 'HitData+DamageModPair'
    $pairListType = [Collections.Generic.List``1].MakeGenericType($pairType)
    $channels = [ordered]@{
        Blunt = 'm_blunt'; Slash = 'm_slash'; Pierce = 'm_pierce'; Chop = 'm_chop'; Pickaxe = 'm_pickaxe'
        Fire = 'm_fire'; Frost = 'm_frost'; Lightning = 'm_lightning'; Poison = 'm_poison'; Spirit = 'm_spirit'; NonPlayer = 'm_nonPlayer'
    }
    function Modifier-Pair([string] $Type, $Modifier) {
        $pair = [Activator]::CreateInstance($pairType)
        $pairType.GetField('m_type').SetValue($pair, [Enum]::Parse($damageType, $Type))
        $pairType.GetField('m_modifier').SetValue($pair, $Modifier)
        return $pair
    }
    function Apply-Pairs($Modifiers, $Pairs, $Profile) {
        $arguments = [object[]]@($Modifiers, $Pairs, $Profile)
        $null = $math.GetMethod('ApplyModifiers', $static).Invoke($null, $arguments)
        return $arguments[0]
    }

    # Unscoped or unrelated hits leave both damage and the caller's out-value untouched.
    foreach ($outerProfile in @($null, $playerProfile, $creatureProfile)) {
        $otherHit = [Activator]::CreateInstance($hitType)
        $otherDamage = $hitType.GetField('m_damage').GetValue($otherHit)
        $otherModifiers = [Activator]::CreateInstance($damageModifiersType)
        foreach ($fieldName in $channels.Values) {
            $otherDamage.GetType().GetField($fieldName).SetValue($otherDamage, [single]100)
            $damageModifiersType.GetField($fieldName).SetValue($otherModifiers, $immune)
        }
        $hitType.GetField('m_damage').SetValue($otherHit, $otherDamage)
        $depth = if ($null -ne $outerProfile) { Invoke-Static $context 'Enter' @($hitA, $outerProfile) } else { -1 }
        try {
            $arguments = [object[]]@($otherHit, $otherModifiers, $weak)
            Assert-Check ($resistancePatch.GetMethod('Prefix', $static).Invoke($null, $arguments)) "Other returns to original, outer profile $outerProfile"
            Assert-Check ($arguments[2] -eq $weak) 'Other significant modifier is not rewritten'
            $resultDamage = $hitType.GetField('m_damage').GetValue($otherHit)
            foreach ($fieldName in $channels.Values) {
                Assert-Near ($resultDamage.GetType().GetField($fieldName).GetValue($resultDamage)) 100 "Other channel $fieldName is untouched"
            }
        } finally {
            if ($depth -ge 0) { Invoke-Static $context 'Exit' @($depth) }
        }
    }
    Assert-Check ($null -eq $mod.GetType('AdditiveDamageModifier.DamageModifiersApplyIfBetterPatch', $false)) 'No global additive ApplyIfBetter patch remains'
    Assert-Check ($null -eq $mod.GetType('AdditiveDamageModifier.HitDataApplyModifierPatch', $false)) 'No global custom ApplyModifier patch remains'
    Write-Output 'PASS: Other is unchanged on all 11 channels, including nested Character processing'

    $basePatch = $mod.GetType('AdditiveDamageModifier.PlayerBaseDamageModifiersPatch', $true)
    $protectedFields = @('m_spirit', 'm_chop', 'm_pickaxe')
    $playerBase = [Activator]::CreateInstance($damageModifiersType)
    foreach ($fieldName in $protectedFields + @('m_fire')) { $damageModifiersType.GetField($fieldName).SetValue($playerBase, $immune) }
    $damageModifiersType.GetField('m_nonPlayer').SetValue($playerBase, $ignore)
    $arguments = [object[]]@(,$playerBase)
    $null = $basePatch.GetMethod('ConvertBaseImmunities', $static).Invoke($null, $arguments)
    $protectedBase = $arguments[0]
    foreach ($fieldName in $channels.Values) {
        $expected = if ($fieldName -in $protectedFields -or $fieldName -eq 'm_nonPlayer') { $ignore } elseif ($fieldName -eq 'm_fire') { $immune } else { $normal }
        Assert-Check ($damageModifiersType.GetField($fieldName).GetValue($protectedBase) -eq $expected) "Player base conversion is limited: $fieldName"
    }
    foreach ($value in @($normal, $weak, $ignore, (Encode -0.2))) {
        $customBase = [Activator]::CreateInstance($damageModifiersType)
        foreach ($fieldName in $protectedFields) { $damageModifiersType.GetField($fieldName).SetValue($customBase, $value) }
        $arguments = [object[]]@(,$customBase)
        $null = $basePatch.GetMethod('ConvertBaseImmunities', $static).Invoke($null, $arguments)
        foreach ($fieldName in $protectedFields) {
            Assert-Check ($damageModifiersType.GetField($fieldName).GetValue($arguments[0]) -eq $value) "Explicit base $value is preserved: $fieldName"
        }
    }
    $baseAttribute = @($basePatch.GetCustomAttributes($false) | Where-Object { $_.GetType().FullName -eq 'HarmonyLib.HarmonyPatch' })[0]
    Assert-Check ($baseAttribute.info.declaringType -eq (Find-GameType 'Player') -and $baseAttribute.info.methodName -ceq 'Awake') 'Base conversion hook is Player-only'
    Write-Output 'PASS: Player base Immune-to-Ignore conversion preserves all unrelated/default-overridden values'

    # Isolated ConfigEntries, never plugin Awake/config migration and never disk writes.
    $plugin = $mod.GetType('AdditiveDamageModifier.AdditiveDamageModifierPlugin', $true)
    $resistanceEnabledProperty = $plugin.GetProperty('ResistancePatchesEnabled', $static)
    $setResistanceEnabled = $resistanceEnabledProperty.GetSetMethod($true)
    Assert-Check ($resistanceEnabledProperty.GetValue($null)) 'Resistance features default to enabled without a loaded PoV plugin'
    $povDependencies = @($plugin.GetCustomAttributes($false) | Where-Object {
        $_.GetType().FullName -eq 'BepInEx.BepInDependency' -and $_.DependencyGUID -ceq 'treextr.pathofvalheiman'
    })
    Assert-Check ($povDependencies.Count -eq 1 -and $povDependencies[0].Flags.ToString() -ceq 'SoftDependency') 'PoV GUID has one optional load-order dependency, not a hard requirement'
    # Only a fresh-process registry fixture is modified; do not construct/start plugins.
    $bepAssembly = [Reflection.Assembly]::LoadFrom((Join-Path $corePath 'BepInEx.dll'))
    # Chainloader's static settings initialize ConfigFile.CoreConfig with saveOnInit=true.
    # Reserve an empty temporary file so that constructor reads rather than creates it.
    # Disable subsequent writes before Chainloader binds settings. Never point it at
    # the real game config, and remove only this exact fixture in the outer finally.
    $coreConfigReadPath = [IO.Path]::GetTempFileName()
    $coreConfigFixtureCreated = $true
    $coreConfigOriginalHash = (Get-FileHash -LiteralPath $coreConfigReadPath -Algorithm SHA256).Hash
    $coreConfigOriginalWriteTime = [IO.File]::GetLastWriteTimeUtc($coreConfigReadPath)
    $coreConfigPathProperty = $bepAssembly.GetType('BepInEx.Paths', $true).GetProperty('BepInExConfigPath', $static)
    $savedCoreConfigPath = $coreConfigPathProperty.GetValue($null)
    $coreConfigPathProperty.SetValue($null, $coreConfigReadPath)
    $coreConfigFixture = $bepAssembly.GetType('BepInEx.Configuration.ConfigFile', $true).GetProperty('CoreConfig', $static).GetValue($null)
    $savedCoreSaveOnSet = $coreConfigFixture.SaveOnConfigSet
    $coreConfigFixture.SaveOnConfigSet = $false
    $chainloader = $bepAssembly.GetType('BepInEx.Bootstrap.Chainloader', $true)
    $registry = $chainloader.GetProperty('PluginInfos', $static).GetValue($null)
    $povGuid = 'treextr.pathofvalheiman'
    $unrelatedGuid = 'adm.regression.unrelated.' + [Guid]::NewGuid().ToString('N')
    $hadPoV = $registry.ContainsKey($povGuid)
    $savedPoV = if ($hadPoV) { $registry[$povGuid] } else { $null }
    $originalResistanceEnabled = $resistanceEnabledProperty.GetValue($null)
    try {
        $null = $registry.Remove($povGuid)
        Invoke-Static $plugin 'DetectResistanceCompatibility'
        Assert-Check ($resistanceEnabledProperty.GetValue($null)) 'No PoV registry entry leaves resistance enabled'
        $infoType = $bepAssembly.GetType('BepInEx.PluginInfo', $true)
        $info = [Activator]::CreateInstance($infoType)
        $metadataType = $bepAssembly.GetType('BepInEx.BepInPlugin', $true)
        $metadataConstructor = $metadataType.GetConstructor([Type[]]@([string], [string], [string]))
        $infoType.GetProperty('Metadata', $all).SetValue($info, $metadataConstructor.Invoke([object[]]@($povGuid, 'PathOfValheiman', '6.0.7')))
        $registry[$povGuid] = $info
        Invoke-Static $plugin 'DetectResistanceCompatibility'
        Assert-Check ($resistanceEnabledProperty.GetValue($null)) 'A registered PoV entry without a plugin instance does not disable resistance'
        $infoType.GetProperty('Instance', $all).SetValue($info, [Runtime.Serialization.FormatterServices]::GetUninitializedObject($plugin))
        $null = $registry.Remove($povGuid)
        $registry[$unrelatedGuid] = $info
        Invoke-Static $plugin 'DetectResistanceCompatibility'
        Assert-Check ($resistanceEnabledProperty.GetValue($null)) 'A loaded plugin under another GUID does not trigger PoV compatibility'
        $registry[$povGuid] = $info
        foreach ($version in @('6.0.7', '99.0.0')) {
            $infoType.GetProperty('Metadata', $all).SetValue($info, $metadataConstructor.Invoke([object[]]@($povGuid, 'PathOfValheiman', $version)))
            Invoke-Static $plugin 'DetectResistanceCompatibility'
            Assert-Check (-not $resistanceEnabledProperty.GetValue($null)) "Loaded PoV $version disables resistance without an exact-version gate"
        }
        $null = $registry.Remove($povGuid)
        Invoke-Static $plugin 'DetectResistanceCompatibility'
        Assert-Check ($resistanceEnabledProperty.GetValue($null)) 'Detection with PoV removed returns to the normal startup policy'
    } finally {
        $null = $registry.Remove($povGuid)
        $null = $registry.Remove($unrelatedGuid)
        if ($hadPoV) { $registry[$povGuid] = $savedPoV }
        $null = $setResistanceEnabled.Invoke($null, [object[]]@($originalResistanceEnabled))
    }
    $playerSettings = $plugin.GetField('PlayerModifierPercentConfigs', $static).GetValue($null)
    $creatureSettings = $plugin.GetField('NonPlayerModifierPercentConfigs', $static).GetValue($null)
    $minimumSettings = $plugin.GetField('PlayerMinimumDamageCapConfigs', $static).GetValue($null)
    Assert-Check ($playerSettings.Count -eq 0 -and $creatureSettings.Count -eq 0 -and $minimumSettings.Count -eq 0) 'Config fixtures start without live plugin state'
    $bepAssembly = [Reflection.Assembly]::LoadFrom((Join-Path $corePath 'BepInEx.dll'))
    $configFileType = $bepAssembly.GetType('BepInEx.Configuration.ConfigFile', $true)
    $fixturePath = Join-Path ([IO.Path]::GetTempPath()) ('adm-test-' + [Guid]::NewGuid().ToString('N') + '.cfg')
    $fixtureConfig = $configFileType.GetConstructor([Type[]]@([string], [bool])).Invoke([object[]]@([string]$fixturePath, $false))
    $fixtureConfig.SaveOnConfigSet = $false
    $bind = @($configFileType.GetMethods() | Where-Object {
        $_.Name -eq 'Bind' -and $_.IsGenericMethodDefinition -and $_.GetParameters().Count -eq 4 -and $_.GetParameters()[3].ParameterType -eq [string]
    })[0].MakeGenericMethod([int])
    function Fixture-Setting([string] $Name, [int] $Value) {
        return $bind.Invoke($fixtureConfig, [object[]]@('InMemoryOnly', $Name, $Value, 'Regression fixture; never saved'))
    }
    try {
        foreach ($profile in @($playerProfile, $creatureProfile)) {
            Assert-Near (Delta $immune $profile) -1 "Default Immune remains -100 for $profile"
            Assert-Near (Delta $resistant $profile) -0.3 "Default Resistant remains -30 for $profile"
        }
        $playerSettings[$immune] = Fixture-Setting 'PlayerImmune' -60
        $creatureSettings[$immune] = Fixture-Setting 'CreatureImmune' -75
        $playerSettings[$resistant] = Fixture-Setting 'PlayerResistant' -30
        $creatureSettings[$resistant] = Fixture-Setting 'CreatureResistant' -45
        $playerSettings[$weak] = Fixture-Setting 'PlayerWeak' 30
        $creatureSettings[$weak] = Fixture-Setting 'CreatureWeak' 15
        $minimumSettings[[Enum]::Parse($damageType, 'Blunt')] = Fixture-Setting 'PlayerBluntMinimum' 10
        Assert-Near (Delta $immune $playerProfile) -0.6 'Player Immune uses its own config'
        Assert-Near (Delta $immune $creatureProfile) -0.75 'Non-player Immune uses its own config'
        Assert-Near (Delta (Combine $immune $weak $playerProfile) $playerProfile) -0.3 'Player raw tiers are combined using Player values'
        Assert-Near (Delta (Combine $immune $weak $creatureProfile) $creatureProfile) -0.6 'Creature raw tiers are combined using Creature values'
        $playerSettings[$immune].Value = -55
        Assert-Near (Delta $immune $playerProfile) -0.55 'Player config updates take effect without stale cache'
        Assert-Near (Delta $immune $creatureProfile) -0.75 'Changing Player config does not alter Creature config'
        $playerSettings[$immune].Value = -60

        foreach ($profile in @($playerProfile, $creatureProfile)) {
            $pairs = [Activator]::CreateInstance($pairListType)
            foreach ($channel in $channels.Keys) {
                $pairs.Add((Modifier-Pair $channel $resistant))
                $pairs.Add((Modifier-Pair $channel $weak))
            }
            $combined = Apply-Pairs ([Activator]::CreateInstance($damageModifiersType)) $pairs $profile
            $expectedDelta = if ($profile -eq $playerProfile) { 0 } else { -0.3 }
            foreach ($fieldName in $channels.Values) {
                Assert-Near (Delta ($damageModifiersType.GetField($fieldName).GetValue($combined)) $profile) $expectedDelta "Explicit list aggregation $profile $fieldName"
            }
            $pairs.Reverse()
            $reversed = Apply-Pairs ([Activator]::CreateInstance($damageModifiersType)) $pairs $profile
            foreach ($fieldName in $channels.Values) {
                Assert-Near (Delta ($damageModifiersType.GetField($fieldName).GetValue($reversed)) $profile) $expectedDelta "Reversed list aggregation $profile $fieldName"
            }

            $testHit = [Activator]::CreateInstance($hitType)
            $damage = $hitType.GetField('m_damage').GetValue($testHit)
            $modifiers = [Activator]::CreateInstance($damageModifiersType)
            foreach ($fieldName in $channels.Values) {
                $damage.GetType().GetField($fieldName).SetValue($damage, [single]100)
                $damageModifiersType.GetField($fieldName).SetValue($modifiers, $immune)
            }
            $hitType.GetField('m_damage').SetValue($testHit, $damage)
            $depth = Invoke-Static $context 'Enter' @($testHit, $profile)
            try {
                $arguments = [object[]]@($testHit, $modifiers, $normal)
                Assert-Check (-not $resistancePatch.GetMethod('Prefix', $static).Invoke($null, $arguments)) "Character replacement owns $profile hit"
                $resultDamage = $hitType.GetField('m_damage').GetValue($testHit)
                $expectedAmount = if ($profile -eq $playerProfile) { 40 } else { 25 }
                foreach ($fieldName in $channels.Values) {
                    Assert-Near ($resultDamage.GetType().GetField($fieldName).GetValue($resultDamage)) $expectedAmount "Profile-aware resistance $profile $fieldName"
                }
            } finally { Invoke-Static $context 'Exit' @($depth) }
        }

        $pairs = [Activator]::CreateInstance($pairListType)
        foreach ($channel in @('Spirit', 'Chop', 'Pickaxe')) {
            $pairs.Add((Modifier-Pair $channel $weak))
            $pairs.Add((Modifier-Pair $channel $immune))
        }
        $protectedAfterEffects = Apply-Pairs $protectedBase $pairs $playerProfile
        foreach ($fieldName in $protectedFields) {
            $value = $damageModifiersType.GetField($fieldName).GetValue($protectedAfterEffects)
            Assert-Check ($value -eq $ignore) "Player Ignore survives configured Immune/Weak: $fieldName"
            Assert-Near (Damage $value 0.1).Amount 0 "Player Ignore still returns zero: $fieldName"
        }
        foreach ($profile in @($playerProfile, $creatureProfile)) {
            $testHit = [Activator]::CreateInstance($hitType)
            $damage = $hitType.GetField('m_damage').GetValue($testHit)
            $damage.GetType().GetField('m_blunt').SetValue($damage, [single]100)
            $hitType.GetField('m_damage').SetValue($testHit, $damage)
            $modifiers = [Activator]::CreateInstance($damageModifiersType)
            $damageModifiersType.GetField('m_blunt').SetValue($modifiers, (Encode -1.2))
            $depth = Invoke-Static $context 'Enter' @($testHit, $profile)
            try {
                $arguments = [object[]]@($testHit, $modifiers, $normal)
                $null = $resistancePatch.GetMethod('Prefix', $static).Invoke($null, $arguments)
                $expectedMinimum = if ($profile -eq $playerProfile) { 10 } else { 0 }
                Assert-Near ($hitType.GetField('m_damage').GetValue($testHit).m_blunt) $expectedMinimum "Player minimum is not inherited by $profile"
            } finally { Invoke-Static $context 'Exit' @($depth) }
        }
        # Exercise the real patch-selection policy in both startup modes. This does not
        # install detours or simulate an unsupported live mode switch in Valheim.
        $disabledResistancePatches = @(
            'PlayerBaseDamageModifiersPatch', 'CharacterDamageCallSitePatch',
            'HitDataApplyResistancePatch', 'PlayerEnvStatusImmunityPatch',
            'ItemDataGetTooltipAdditiveDamagePatch', 'SEStatsGetTooltipStringAdditiveDamagePatch',
            'SEStatsDamageModifiersTooltipPatch', 'TextsDialogAddActiveEffectsPatch'
        )
        $ownPatchTypes = @($mod.GetTypes() | Where-Object {
            $_.Namespace -in 'AdditiveDamageModifier','LocalizationManager' -and
            @($_.GetCustomAttributes($false) | Where-Object { $_.GetType().FullName -eq 'HarmonyLib.HarmonyPatch' }).Count -gt 0
        })
        $originalResistanceEnabled = $resistanceEnabledProperty.GetValue($null)
        $playerEntry = $playerSettings[$immune]
        $creatureEntry = $creatureSettings[$immune]
        $minimumEntry = $minimumSettings[[Enum]::Parse($damageType, 'Blunt')]
        try {
            foreach ($enabled in @($true, $false)) {
                $null = $setResistanceEnabled.Invoke($null, [object[]]@($enabled))
                $actualDisabled = @(foreach ($patchType in $ownPatchTypes) {
                    $selected = $plugin.GetMethod('ShouldApplyPatch', $static).Invoke($null, [Type[]]@($patchType))
                    $expected = $enabled -or $patchType.Name -notin $disabledResistancePatches
                    Assert-Check ($selected -eq $expected) "Patch selection resistance=$enabled : $($patchType.FullName)"
                    if (-not $selected) { $patchType.Name }
                })
                if ($enabled) {
                    Assert-Check ($actualDisabled.Count -eq 0) 'Normal startup selects every ADM/localization patch'
                } else {
                    Assert-Check ($actualDisabled.Count -eq 8 -and @(Compare-Object $actualDisabled $disabledResistancePatches).Count -eq 0) 'PoV startup suppresses exactly the complete resistance patch group'
                }
            }
            foreach ($patchName in @('CharacterUpdateGroundContactFallDamagePatch', 'ObjectDbAwakeStatusEffectPatch',
                'ObjectDbCopyOtherDbStatusEffectPatch', 'HudUpdateStatusEffectsPatch', 'TextsDialogUpdateTextsListAdditiveDamagePatch')) {
                Assert-Check ($plugin.GetMethod('ShouldApplyPatch', $static).Invoke($null, [Type[]]@($mod.GetType("AdditiveDamageModifier.$patchName", $true)))) "PoV mode retains independent/catalog/informational patch $patchName"
            }
            Assert-Check ([object]::ReferenceEquals($playerEntry, $playerSettings[$immune]) -and $playerEntry.Value -eq -60) 'PoV mode preserves the player config entry and stored value'
            Assert-Check ([object]::ReferenceEquals($creatureEntry, $creatureSettings[$immune]) -and $creatureEntry.Value -eq -75) 'PoV mode preserves the non-player config entry and stored value'
            Assert-Check ([object]::ReferenceEquals($minimumEntry, $minimumSettings[[Enum]::Parse($damageType, 'Blunt')]) -and $minimumEntry.Value -eq 10) 'PoV mode preserves the minimum-damage config entry and stored value'
        } finally {
            $null = $setResistanceEnabled.Invoke($null, [object[]]@($originalResistanceEnabled))
        }
        Assert-Check (-not [IO.File]::Exists($fixturePath)) 'In-memory config tests did not create a config file'
    } finally {
        $playerSettings.Clear()
        $creatureSettings.Clear()
        $minimumSettings.Clear()
    }
    Write-Output 'PASS: independent configurable profiles, all-channel aggregation, Ignore preservation and player-only minimum'
    Write-Output 'PASS: optional PoV load order, both patch-selection modes, retained independent features and unchanged in-memory config values'

    $tooltipContext = $mod.GetType('AdditiveDamageModifier.AdditiveDamageTooltipContext', $true)
    $pairType = Find-GameType 'HitData+DamageModPair'
    $pairListType = [Collections.Generic.List``1].MakeGenericType($pairType)
    $outerSource = [Activator]::CreateInstance($pairListType)
    $appendedSource = [Activator]::CreateInstance($pairListType)
    Invoke-Static $tooltipContext 'PushModifierSource' @($outerSource, $true)
    Invoke-Static $tooltipContext 'PushModifierSource' @($appendedSource, $false)
    Assert-Check (Invoke-Static $tooltipContext 'IsAppliedSource' @(,$outerSource)) 'Appended tooltip retains outer equipped source'
    Assert-Check (-not (Invoke-Static $tooltipContext 'IsAppliedSource' @(,$appendedSource))) 'Appended source does not inherit equipped state'
    $itemPatch = $mod.GetType('AdditiveDamageModifier.ItemDataGetTooltipAdditiveDamagePatch', $true)
    $returned = Invoke-Static $itemPatch 'Finalizer' @($exception, $true)
    Assert-Check ([object]::ReferenceEquals($returned, $exception)) 'Appended tooltip finalizer preserves exception'
    Assert-Check (Invoke-Static $tooltipContext 'IsAppliedSource' @(,$outerSource)) 'Inner finalizer preserves outer scope'
    $null = Invoke-Static $itemPatch 'Finalizer' @($null, $true)
    Assert-Check ($tooltipContext.GetField('_tooltipScopeDepth', $static).GetValue($null) -eq 0) 'Tooltip depth is cleared after outer finalizer'
    Assert-Check (-not (Invoke-Static $tooltipContext 'IsAppliedSource' @(,$outerSource))) 'Tooltip sources cleared after finalizer'
    $tooltipBuilder = $mod.GetType('AdditiveDamageModifier.AdditiveDamageTooltipBuilder', $true)
    $ignoreSuffix = Invoke-Static $tooltipBuilder 'GetModifierTooltipSuffix' @([Enum]::Parse($damageType, 'Spirit'), $immune, $true, $ignore)
    Assert-Check ($ignoreSuffix.Contains('$adm_tooltip_net_label $adm_tooltip_ignore')) 'Active protected Spirit source can show Net Ignore'
    Assert-Check (-not $ignoreSuffix.Contains('$adm_tooltip_min_total_label')) 'Net Ignore omits the inapplicable minimum'

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

    # Managed-only display fixtures. In particular, Localization.instance must not
    # lazily construct its singleton, which would call Unity Resources.Load.
    $originalResistanceEnabled = $resistanceEnabledProperty.GetValue($null)
    $localizationType = Find-GameType 'Localization'
    $localizationInstance = $localizationType.GetField('m_instance', $static)
    $savedLocalization = $localizationInstance.GetValue($null)
    $savedDisplayConfigs = @{}
    foreach ($fieldName in @('_fallDamageCap', '_fallDamageMultiplier', '_frostEnvImmunityTriggerFrostDeltaPercent')) {
        $savedDisplayConfigs[$fieldName] = $plugin.GetField($fieldName, $static).GetValue($null)
    }
    try {
        $plugin.GetField('_fallDamageCap', $static).SetValue($null, (Fixture-Setting 'DisplayFallCap' 225))
        $plugin.GetField('_frostEnvImmunityTriggerFrostDeltaPercent', $static).SetValue($null, (Fixture-Setting 'DisplayFrostThreshold' -15))
        $floatBind = $bind.GetGenericMethodDefinition().MakeGenericMethod([single])
        $fallMultiplierEntry = $floatBind.Invoke($fixtureConfig, [object[]]@('InMemoryOnly', 'DisplayFallMultiplier', [single]1.5, 'Regression fixture; never saved'))
        $plugin.GetField('_fallDamageMultiplier', $static).SetValue($null, $fallMultiplierEntry)
        $effectType = $mod.GetType('AdditiveDamageModifier.SE_AdditiveDamageModifier', $true)
        $effect = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($effectType)
        $effectType.GetField('m_modifier', $all).SetValue($effect, $resistant)
        $iconMethod = $effectType.GetMethod('GetIconText', $all)
        $null = $setResistanceEnabled.Invoke($null, [object[]]@($false))
        Assert-Check ($iconMethod.Invoke($effect, @()) -ceq '') 'PoV mode hides the custom status effect icon percentage without looking up a character/config'
        $null = $setResistanceEnabled.Invoke($null, [object[]]@($true))
        Assert-Check ($iconMethod.Invoke($effect, @()) -ceq '-30%') 'Normal mode retains the configured custom status effect icon percentage'

        $localizationFixture = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($localizationType)
        $localizationType.GetField('m_stringBuilder', $all).SetValue($localizationFixture, [Text.StringBuilder]::new())
        $localizationType.GetField('m_endChars', $all).SetValue($localizationFixture, ' (){}[]+-!?/\&%,.:-=<>'.ToCharArray())
        $localizationCacheField = $localizationType.GetField('m_cache', $all)
        $localizationInstance.SetValue($null, $localizationFixture)
        $compendium = $mod.GetType('AdditiveDamageModifier.AdditiveDamageCompendium', $true)
        $povTranslationKeys = @('adm_compendium_pov_heading', 'adm_compendium_pov_intro',
            'adm_compendium_pov_disabled', 'adm_compendium_pov_settings', 'adm_compendium_pov_retained', 'adm_compendium_pov_cold')
        foreach ($language in @('English', 'Korean')) {
            $words = $translations[$language]
            foreach ($key in $povTranslationKeys) {
                Assert-Check ($words.ContainsKey($key) -and -not [string]::IsNullOrWhiteSpace($words[$key])) "$language PoV compatibility translation exists: $key"
            }
            $localizationType.GetField('m_translations', $all).SetValue($localizationFixture, $words)
            $localizationCacheField.SetValue($localizationFixture, $localizationCacheField.FieldType.GetConstructor([Type[]]@([int])).Invoke([object[]]@(100)))
            $null = $setResistanceEnabled.Invoke($null, [object[]]@($false))
            $page = Invoke-Static $compendium 'BuildPageText'
            foreach ($key in $povTranslationKeys | Where-Object { $_ -ne 'adm_compendium_pov_retained' }) {
                Assert-Check ($page.Contains($words[$key])) "$language compatibility page includes $key"
            }
            $fallCap = (Invoke-Static $plugin 'GetFallDamageCap').ToString('0.##', [Globalization.CultureInfo]::InvariantCulture)
            $fallMultiplier = (Invoke-Static $plugin 'GetFallDamageMultiplier').ToString('0.00', [Globalization.CultureInfo]::InvariantCulture)
            $retained = [string]::Format([Globalization.CultureInfo]::InvariantCulture, $words['adm_compendium_pov_retained'], [object[]]@($fallCap, $fallMultiplier))
            Assert-Check ($page.Contains($retained)) "$language compatibility page reports retained fall-damage values and native status-effect tiers"
            foreach ($key in @('adm_compendium_current_heading', 'adm_compendium_calculation_heading', 'adm_compendium_minimum_heading')) {
                Assert-Check (-not $page.Contains('<b>' + $words[$key] + '</b>')) "$language compatibility page omits inactive resistance section $key"
            }
            Assert-Check (-not $page.Contains('$adm_')) "$language compatibility page resolves every ADM translation token"
            $null = $setResistanceEnabled.Invoke($null, [object[]]@($true))
            $page = Invoke-Static $compendium 'BuildPageText'
            Assert-Check ($page.Contains($words['adm_compendium_current_heading']) -and -not $page.Contains($words['adm_compendium_pov_heading'])) "$language normal startup retains the ADM resistance settings page"
        }
    } finally {
        foreach ($fieldName in $savedDisplayConfigs.Keys) {
            $plugin.GetField($fieldName, $static).SetValue($null, $savedDisplayConfigs[$fieldName])
        }
        $localizationInstance.SetValue($null, $savedLocalization)
        $null = $setResistanceEnabled.Invoke($null, [object[]]@($originalResistanceEnabled))
    }
    Write-Output 'PASS: managed custom-status icon and English/Korean Compendium mode branches (no Unity UI or localization initialization)'

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
    foreach ($literal in @('1 - General', '2 - Additive Damage', '2 - Non-Player Character Damage', '3 - Fall Damage', 'Lock Configuration',
        'Show Modifier Percent in Tooltips Outside Compendium', 'Maximum Fall Damage', 'Fall Damage Multiplier',
        'Cold/Freezing Immunity Trigger Frost Delta Percent',
        'Minimum Damage Taken Cap Percent on Player - {definition.DisplayName}',
        'Player Minimum Damage Taken Percent - {definition.DisplayName}', '{definition.DisplayName} Percent')) {
        Assert-Check ($pluginSource.Contains('"' + $literal + '"')) "Static config key/template: $literal"
    }
    Assert-Check ($pluginSource.Contains('"Non-player Character", playerValue.Value')) 'Static migration contract: new Creature values inherit saved Player values once'
    Assert-Check ($pluginSource.Contains('_harmony.UnpatchSelf();')) 'Static rollback contract: failed patch installation removes this plugin patches'
    Write-Output 'PASS: definition data and static config-key/status-name contracts (not config migration execution)'

    $awakeInstructions = @(Get-MethodInstructions ($plugin.GetMethod('Awake', $all)))
    $detectionCalls = @($awakeInstructions | Where-Object {
        $_.Operand -is [Reflection.MethodInfo] -and $_.Operand.DeclaringType -eq $plugin -and $_.Operand.Name -eq 'DetectResistanceCompatibility'
    })
    $installationCalls = @($awakeInstructions | Where-Object {
        $_.Operand -is [Reflection.MethodInfo] -and $_.Operand.DeclaringType -eq $plugin -and $_.Operand.Name -eq 'ApplyPatches'
    })
    Assert-Check ($detectionCalls.Count -eq 1 -and $installationCalls.Count -eq 1 -and $detectionCalls[0].Offset -lt $installationCalls[0].Offset) 'Startup IL detects compatibility exactly once before installing patches'
    $installerInstructions = @(Get-MethodInstructions ($plugin.GetMethod('ApplyPatches', $all)))
    $selectionCalls = @($installerInstructions | Where-Object {
        $_.Operand -is [Reflection.MethodInfo] -and $_.Operand.DeclaringType -eq $plugin -and $_.Operand.Name -eq 'ShouldApplyPatch'
    })
    $processorCalls = @($installerInstructions | Where-Object {
        $_.Operand -is [Reflection.MethodInfo] -and $_.Operand.DeclaringType.FullName -eq 'HarmonyLib.Harmony' -and $_.Operand.Name -eq 'CreateClassProcessor'
    })
    Assert-Check ($selectionCalls.Count -eq 1 -and $processorCalls.Count -eq 1 -and $selectionCalls[0].Offset -lt $processorCalls[0].Offset) 'Installer IL evaluates the shared selection policy before creating a Harmony class processor'
    Assert-Check (@($mod.GetReferencedAssemblies() | Where-Object { $_.Name -eq 'PathOfValheiman' }).Count -eq 0) 'Optional compatibility introduces no hard reference to the PoV assembly'
    Write-Output 'PASS: compatibility detection/installation IL ordering and optional dependency boundary (no detours installed)'

    # Resolve all current Harmony targets by their exact parameter types, without applying patches.
    $targetSpecs = @(
        @('Character', 'RPC_Damage', 'System.Int64,HitData'),
        @('Character', 'UpdateGroundContact', 'System.Single'),
        @('Humanoid', 'BlockAttack', 'HitData,Character'),
        @('Player', 'Awake', ''),
        @('Player', 'ApplyArmorDamageMods', 'HitData+DamageModifiers&'),
        @('SE_Stats', 'ModifyDamageMods', 'HitData+DamageModifiers&'),
        @('HitData+DamageModifiers', 'Apply', 'System.Collections.Generic.List`1[HitData+DamageModPair]'),
        @('HitData', 'ApplyResistance', 'HitData+DamageModifiers,HitData+DamageModifier&'),
        @('Player', 'UpdateEnvStatusEffects', 'System.Single'),
        @('ItemDrop+ItemData', 'GetTooltip', 'ItemDrop+ItemData,System.Int32,System.Boolean,System.Single,System.Int32,System.Boolean'),
        @('SE_Stats', 'GetTooltipString', ''),
        @('SE_Stats', 'GetDamageModifiersTooltipString', 'System.Collections.Generic.List`1[HitData+DamageModPair]'),
        @('Hud', 'UpdateStatusEffects', 'System.Collections.Generic.List`1[StatusEffect]'),
        @('TextsDialog', 'AddActiveEffects', ''), @('TextsDialog', 'UpdateTextsList', ''),
        @('ObjectDB', 'Awake', ''), @('ObjectDB', 'CopyOtherDB', 'ObjectDB'),
        @('Localization', 'SetupLanguage', 'System.String'),
        @('Localization', 'AddWord', 'System.String,System.String'),
        # Bundled ServerSync Harmony and reflected game targets.
        @('ZRpc', 'HandlePackage', 'ZPackage'), @('ZNet', 'Awake', ''),
        @('ZNet', 'Shutdown', 'System.Boolean'), @('ZNet', 'OnNewConnection', 'ZNetPeer'),
        @('ZNet', 'RPC_PeerInfo', 'ZRpc,ZPackage'), @('ZNet', 'Disconnect', 'ZNetPeer'),
        @('ZNet', 'ListContainsId', 'SyncedList,System.String'), @('ZNet', 'GetPeer', 'ZRpc'),
        @('FejdStartup', 'ShowConnectError', 'ZNet+ConnectionStatus')
    )
    foreach ($spec in $targetSpecs) {
        $targetType = Find-GameType $spec[0]
        $matches = @($targetType.GetMethods($all) | Where-Object {
            $_.Name -eq $spec[1] -and ((@($_.GetParameters() | ForEach-Object { Type-Signature $_.ParameterType }) -join ',') -ceq $spec[2])
        })
        Assert-Check ($matches.Count -eq 1) "Exact Harmony target: $($spec[0]).$($spec[1])"
    }
    $callSitePatch = $mod.GetType('AdditiveDamageModifier.CharacterDamageCallSitePatch', $true)
    $wrapper = $callSitePatch.GetMethod('ApplyResistanceForCharacter', $static)
    $wrapperInstructions = @(Get-MethodInstructions $wrapper)
    $finallyBlocks = @($wrapper.GetMethodBody().ExceptionHandlingClauses | Where-Object { $_.Flags -eq [Reflection.ExceptionHandlingClauseOptions]::Finally })
    Assert-Check ($finallyBlocks.Count -eq 1) 'Resistance wrapper has one finally cleanup block'
    $finallyCalls = @($wrapperInstructions | Where-Object {
        $_.Offset -ge $finallyBlocks[0].HandlerOffset -and $_.Offset -lt ($finallyBlocks[0].HandlerOffset + $finallyBlocks[0].HandlerLength) -and
        $_.Operand -is [Reflection.MethodInfo] -and $_.Operand.DeclaringType -eq $context -and $_.Operand.Name -eq 'Exit'
    })
    Assert-Check ($finallyCalls.Count -eq 1) 'Exact-hit scope is popped by finally even when resistance hooks throw'
    $originalCalls = @($wrapperInstructions | Where-Object {
        $_.Operand -is [Reflection.MethodInfo] -and $_.Operand.DeclaringType -eq $hitType -and $_.Operand.Name -eq 'ApplyResistance'
    })
    Assert-Check ($originalCalls.Count -eq 1) 'Wrapper retains one shared ApplyResistance call for other Harmony hooks'
    Write-Output 'PASS: actual-game targets and wrapper finally IL (patch execution/exception injection not performed)'

    $tooltipAttribute = @($itemPatch.GetCustomAttributes($false) | Where-Object { $_.GetType().FullName -eq 'HarmonyLib.HarmonyPatch' })[0]
    Assert-Check (($tooltipAttribute.info.argumentTypes | ForEach-Object { Type-Signature $_ }) -join ',' -ceq 'ItemDrop+ItemData,System.Int32,System.Boolean,System.Single,System.Int32,System.Boolean') 'Actual tooltip Harmony attribute selects six-argument 1.0 overload'
    foreach ($spec in @(
        @('Hud', 'm_statusEffects', 'System.Collections.Generic.List`1[UnityEngine.RectTransform]'),
        @('StatusEffect', 'm_character', 'Character'),
        @('Character', 'm_damageModifiers', 'HitData+DamageModifiers'),
        @('TextsDialog', 'm_texts', 'System.Collections.Generic.List`1[TextsDialog+TextInfo]'),
        @('ZNet', 'm_adminList', 'SyncedList'), @('ZNet', 'm_connectionStatus', 'ZNet+ConnectionStatus'),
        @('ZRpc', 'm_socket', 'ISocket'), @('ZNetPeer', 'm_socket', 'ISocket'),
        @('ZRoutedRpc', 'm_peers', 'System.Collections.Generic.List`1[ZNetPeer]'),
        @('ZRpc', 'm_functions', 'System.Collections.Generic.Dictionary`2[System.Int32,ZRpc+RpcMethodBase]'),
        @('ZPlayFabSocket', 'm_remotePlayerId', 'System.String'))) {
        $field = (Find-GameType $spec[0]).GetField($spec[1], $all)
        Assert-Check ($null -ne $field -and (Type-Signature $field.FieldType) -ceq $spec[2]) "Harmony/reflected field: $($spec[0]).$($spec[1])"
    }

    $literalLoads = 0
    $scannedMethods = 0
    foreach ($type in $mod.GetTypes() | Where-Object { $_.Namespace -in 'AdditiveDamageModifier','LocalizationManager','ServerSync' }) {
        foreach ($method in @($type.GetMethods($all -bor [Reflection.BindingFlags]::DeclaredOnly)) + @($type.GetConstructors($all))) {
            foreach ($instruction in (Get-MethodInstructions $method)) {
                if ($instruction.OpCode.Name -in 'ldsfld','ldsflda','stsfld' -and $instruction.Operand -is [Reflection.FieldInfo] -and $instruction.Operand.IsLiteral) { $literalLoads++ }
            }
            $scannedMethods++
        }
    }
    Assert-Check ($literalLoads -eq 0) 'Final merged mod has no static-field instructions targeting literal constants (including ServerSync Everybody)'
    Write-Output "PASS: resolved IL operands in $scannedMethods mod/ServerSync methods; no obsolete literal field loads"

    foreach ($spec in @(@('Character', 'UpdateGroundContact', 'UnityEngine.Mathf', 'Clamp01', 'CharacterUpdateGroundContactFallDamagePatch', 'ScaleFallDamageProgress'), @('Player', 'UpdateEnvStatusEffects', 'HitData+DamageModifiers', 'GetModifier', 'PlayerEnvStatusImmunityPatch', 'GetModifierForEnv'))) {
        $method = (Find-GameType $spec[0]).GetMethod($spec[1], $all)
        $instructions = @(Get-MethodInstructions $method)
        $calls = @($instructions | Where-Object { $_.Operand -is [Reflection.MethodInfo] -and $_.Operand.DeclaringType.FullName -eq $spec[2] -and $_.Operand.Name -eq $spec[3] })
        Assert-Check ($calls.Count -eq 1) "Original 1.0 transpiler anchor: $($spec[0]).$($spec[1])"
    }
    foreach ($spec in @(@('Character', 'RPC_Damage', 0, 1), @('Humanoid', 'BlockAttack', 1, 1), @('Player', 'ApplyArmorDamageMods', 4, 0), @('SE_Stats', 'ModifyDamageMods', 1, 0))) {
        $method = (Find-GameType $spec[0]).GetMethod($spec[1], $all -bor [Reflection.BindingFlags]::DeclaredOnly)
        $instructions = @(Get-MethodInstructions $method)
        $applyCalls = @($instructions | Where-Object { $_.Operand -is [Reflection.MethodInfo] -and $_.Operand.DeclaringType -eq $damageModifiersType -and $_.Operand.Name -eq 'Apply' })
        $resistanceCalls = @($instructions | Where-Object { $_.Operand -is [Reflection.MethodInfo] -and $_.Operand.DeclaringType -eq $hitType -and $_.Operand.Name -eq 'ApplyResistance' })
        Assert-Check ($applyCalls.Count -eq $spec[2] -and $resistanceCalls.Count -eq $spec[3]) "Original Character-only callsite anchors: $($spec[0]).$($spec[1])"
    }
    Write-Output 'PASS: original game IL transpiler anchors (HarmonyX/Mono patch execution still requires Valheim)'
    Assert-Check ((Get-FileHash -LiteralPath $coreConfigReadPath -Algorithm SHA256).Hash -ceq $coreConfigOriginalHash -and [IO.File]::GetLastWriteTimeUtc($coreConfigReadPath) -eq $coreConfigOriginalWriteTime) 'Registry detection tests did not save settings into the disposable core-config fixture'
    Write-Output "PASS: $script:checks checks. No Valheim launch, live config/ServerSync, UI, ownership or network tests were performed."
} finally {
    if ($null -ne $coreConfigFixture) { $coreConfigFixture.SaveOnConfigSet = $savedCoreSaveOnSet }
    if ($null -ne $coreConfigPathProperty) { $coreConfigPathProperty.SetValue($null, $savedCoreConfigPath) }
    if ($coreConfigFixtureCreated) { [IO.File]::Delete($coreConfigReadPath) }
    if ($threadingFixtureInstalled) { $threadingInstanceField.SetValue($null, $null) }
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}
