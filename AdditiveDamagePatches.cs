using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace AdditiveDamageModifier;

internal enum DamageModifierProfile
{
    Player,
    NonPlayerCharacter
}

internal static class AdditiveDamageMath
{
    private const int CustomModifierBase = 1000000000;
    private const int CustomModifierScale = 1000;
    private const int CombineClampAbs = 100000;
    private const int CustomModifierZero = CustomModifierBase + CombineClampAbs * CustomModifierScale;
    private const int CustomModifierMax = CustomModifierBase + 2 * CombineClampAbs * CustomModifierScale;

    public static HitData.DamageModifier Combine(HitData.DamageModifier current, HitData.DamageModifier incoming, DamageModifierProfile profile)
    {
        if (current == HitData.DamageModifier.Ignore || incoming == HitData.DamageModifier.Ignore)
        {
            return HitData.DamageModifier.Ignore;
        }

        if (current == HitData.DamageModifier.Normal)
        {
            return incoming;
        }

        if (incoming == HitData.DamageModifier.Normal)
        {
            return current;
        }

        float combinedDelta = ModifierToDelta(current, profile) + ModifierToDelta(incoming, profile);
        // Do not clamp by the minimum damage cap here; clamping during accumulation makes
        // the final result order-dependent when several modifiers are combined.
        return EncodeCustomDelta(combinedDelta);
    }

    public static float ModifierToDelta(HitData.DamageModifier modifier, DamageModifierProfile profile)
    {
        if (TryDecodeCustomDelta(modifier, out float delta))
        {
            return delta;
        }

        return AdditiveDamageModifierPlugin.GetConfiguredDelta(modifier, profile);
    }

    public static void ApplyModifiers(ref HitData.DamageModifiers modifiers, List<HitData.DamageModPair> incoming, DamageModifierProfile profile)
    {
        // Match DamageModifiers.Apply's channels, including NonPlayer, without changing
        // that shared game method or applying character policy to unrelated structs.
        foreach (HitData.DamageModPair pair in incoming)
        {
            switch (pair.m_type)
            {
                case HitData.DamageType.Blunt: modifiers.m_blunt = Combine(modifiers.m_blunt, pair.m_modifier, profile); break;
                case HitData.DamageType.Slash: modifiers.m_slash = Combine(modifiers.m_slash, pair.m_modifier, profile); break;
                case HitData.DamageType.Pierce: modifiers.m_pierce = Combine(modifiers.m_pierce, pair.m_modifier, profile); break;
                case HitData.DamageType.Chop: modifiers.m_chop = Combine(modifiers.m_chop, pair.m_modifier, profile); break;
                case HitData.DamageType.Pickaxe: modifiers.m_pickaxe = Combine(modifiers.m_pickaxe, pair.m_modifier, profile); break;
                case HitData.DamageType.Fire: modifiers.m_fire = Combine(modifiers.m_fire, pair.m_modifier, profile); break;
                case HitData.DamageType.Frost: modifiers.m_frost = Combine(modifiers.m_frost, pair.m_modifier, profile); break;
                case HitData.DamageType.Lightning: modifiers.m_lightning = Combine(modifiers.m_lightning, pair.m_modifier, profile); break;
                case HitData.DamageType.Poison: modifiers.m_poison = Combine(modifiers.m_poison, pair.m_modifier, profile); break;
                case HitData.DamageType.Spirit: modifiers.m_spirit = Combine(modifiers.m_spirit, pair.m_modifier, profile); break;
                case HitData.DamageType.NonPlayer: modifiers.m_nonPlayer = Combine(modifiers.m_nonPlayer, pair.m_modifier, profile); break;
            }
        }
    }

    internal static bool IsCustomModifier(HitData.DamageModifier modifier) => IsCustomModifierRaw((int)modifier);

    internal static bool TryDecodeCustomDelta(HitData.DamageModifier modifier, out float delta)
    {
        int raw = (int)modifier;
        if (!IsCustomModifierRaw(raw))
        {
            delta = 0f;
            return false;
        }

        delta = (raw - CustomModifierZero) / (float)CustomModifierScale;
        return true;
    }

    private static bool IsCustomModifierRaw(int rawValue) => rawValue >= CustomModifierBase && rawValue <= CustomModifierMax;

    private static HitData.DamageModifier EncodeCustomDelta(float delta)
    {
        float clamped = Mathf.Clamp(delta, -CombineClampAbs, CombineClampAbs);
        int encoded = CustomModifierZero + Mathf.RoundToInt(clamped * CustomModifierScale);
        return (HitData.DamageModifier)encoded;
    }

    public static float ApplyModifier(
        float baseDamage,
        HitData.DamageModifier mod,
        float minimumDamageTakenMultiplier,
        DamageModifierProfile profile,
        ref float normalDmg,
        ref float resistantDmg,
        ref float weakDmg,
        ref float immuneDmg)
    {
        if (mod == HitData.DamageModifier.Ignore)
        {
            return 0f;
        }

        float minDeltaCap = minimumDamageTakenMultiplier - 1f;
        float delta = Mathf.Clamp(ModifierToDelta(mod, profile), minDeltaCap, 100f);
        float finalMultiplier = Mathf.Max(minimumDamageTakenMultiplier, 1f + delta);
        float finalDamage = Mathf.Max(0f, baseDamage * finalMultiplier);

        if (Mathf.Approximately(delta, 0f))
        {
            normalDmg += baseDamage;
        }
        else if (delta < 0f)
        {
            if (finalDamage <= 0f)
            {
                immuneDmg += baseDamage;
            }
            else
            {
                resistantDmg += baseDamage;
            }
        }
        else
        {
            weakDmg += baseDamage;
        }

        return finalDamage;
    }
}

internal static class CharacterDamageContext
{
    [ThreadStatic] private static List<HitScope>? _hitStack;

    private readonly struct HitScope
    {
        public HitScope(HitData hit, DamageModifierProfile profile)
        {
            Hit = hit;
            Profile = profile;
        }

        public HitData Hit { get; }
        public DamageModifierProfile Profile { get; }
    }

    public static int Enter(HitData hitData, DamageModifierProfile profile)
    {
        if (hitData == null)
        {
            throw new ArgumentNullException(nameof(hitData));
        }

        List<HitScope> stack = _hitStack ??= new List<HitScope>(4);
        int depth = stack.Count;
        stack.Add(new HitScope(hitData, profile));
        return depth;
    }

    public static void Exit(int depth)
    {
        if (_hitStack is { } stack && depth >= 0 && depth < stack.Count)
        {
            stack.RemoveRange(depth, stack.Count - depth);
        }
    }

    public static bool TryGetProfile(HitData hitData, out DamageModifierProfile profile)
    {
        if (_hitStack is { Count: > 0 } stack && ReferenceEquals(stack[stack.Count - 1].Hit, hitData))
        {
            profile = stack[stack.Count - 1].Profile;
            return true;
        }

        profile = default;
        return false;
    }
}

[HarmonyPatch(typeof(Player), "Awake")]
internal static class PlayerBaseDamageModifiersPatch
{
    private static void Prefix(Player __instance)
    {
        // Instance data only, before armor/status effects are aggregated. Respect explicit
        // non-Immune defaults supplied by other mods; do not rewrite a shared prefab.
        ConvertBaseImmunities(ref __instance.m_damageModifiers);
    }

    internal static void ConvertBaseImmunities(ref HitData.DamageModifiers modifiers)
    {
        if (modifiers.m_spirit == HitData.DamageModifier.Immune) modifiers.m_spirit = HitData.DamageModifier.Ignore;
        if (modifiers.m_chop == HitData.DamageModifier.Immune) modifiers.m_chop = HitData.DamageModifier.Ignore;
        if (modifiers.m_pickaxe == HitData.DamageModifier.Immune) modifiers.m_pickaxe = HitData.DamageModifier.Ignore;
    }
}

[HarmonyPatch]
internal static class CharacterDamageCallSitePatch
{
    private static readonly MethodInfo ApplyListMethod = AccessTools.Method(typeof(HitData.DamageModifiers), nameof(HitData.DamageModifiers.Apply), new[] { typeof(List<HitData.DamageModPair>) });
    private static readonly MethodInfo ApplyResistanceMethod = AccessTools.Method(typeof(HitData), nameof(HitData.ApplyResistance), new[] { typeof(HitData.DamageModifiers), typeof(HitData.DamageModifier).MakeByRefType() });
    private static readonly MethodInfo ApplyListForCharacterMethod = AccessTools.Method(typeof(CharacterDamageCallSitePatch), nameof(ApplyModifiersForCharacter));
    private static readonly MethodInfo ApplyResistanceForCharacterMethod = AccessTools.Method(typeof(CharacterDamageCallSitePatch), nameof(ApplyResistanceForCharacter));
    private static readonly FieldInfo EffectCharacterField = AccessTools.Field(typeof(StatusEffect), "m_character");

    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return RequiredMethod(typeof(Character), "RPC_Damage", typeof(long), typeof(HitData));
        yield return RequiredMethod(typeof(Humanoid), "BlockAttack", typeof(HitData), typeof(Character));
        yield return RequiredMethod(typeof(Player), "ApplyArmorDamageMods", typeof(HitData.DamageModifiers).MakeByRefType());
        yield return RequiredMethod(typeof(SE_Stats), nameof(SE_Stats.ModifyDamageMods), typeof(HitData.DamageModifiers).MakeByRefType());
    }

    private static MethodInfo RequiredMethod(Type type, string name, params Type[] arguments)
    {
        return AccessTools.DeclaredMethod(type, name, arguments)
               ?? throw new MissingMethodException(type.FullName, name);
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        List<CodeInstruction> code = new();
        int applyCount = 0;
        int resistanceCount = 0;
        foreach (CodeInstruction original in instructions)
        {
            CodeInstruction instruction = new(original);
            if (instruction.Calls(ApplyListMethod)) applyCount++;
            if (instruction.Calls(ApplyResistanceMethod)) resistanceCount++;
            code.Add(instruction);
        }

        bool isRpc = __originalMethod.DeclaringType == typeof(Character);
        bool isBlock = __originalMethod.DeclaringType == typeof(Humanoid);
        bool isEffect = __originalMethod.DeclaringType == typeof(SE_Stats);
        int expectedApply = isRpc ? 0 : isBlock || isEffect ? 1 : 4;
        int expectedResistance = isRpc || isBlock ? 1 : 0;
        if (applyCount != expectedApply || resistanceCount != expectedResistance
            || (isEffect && (EffectCharacterField == null || EffectCharacterField.FieldType != typeof(Character))))
        {
            // Abort PatchAll, whose caller rolls back this plugin's entire patch set.
            // Partial aggregation without resistance handling would corrupt the result.
            throw new InvalidOperationException($"Unsupported {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name} damage calls: Apply {applyCount}/{expectedApply}, ApplyResistance {resistanceCount}/{expectedResistance}.");
        }

        foreach (CodeInstruction instruction in code)
        {
            bool applyList = instruction.Calls(ApplyListMethod);
            if (applyList || instruction.Calls(ApplyResistanceMethod))
            {
                if (instruction.blocks.Count != 0)
                {
                    throw new InvalidOperationException($"Unsupported exception boundary at {__originalMethod.Name} damage call.");
                }

                CodeInstruction loadOwner = new(OpCodes.Ldarg_0);
                loadOwner.labels.AddRange(instruction.labels);
                instruction.labels.Clear();
                yield return loadOwner;
                if (isEffect)
                {
                    // Read the effect owner's original field at this specific call site.
                    yield return new CodeInstruction(OpCodes.Ldfld, EffectCharacterField);
                }

                instruction.opcode = OpCodes.Call;
                instruction.operand = applyList ? ApplyListForCharacterMethod : ApplyResistanceForCharacterMethod;
            }

            yield return instruction;
        }
    }

    internal static void ApplyModifiersForCharacter(ref HitData.DamageModifiers modifiers, List<HitData.DamageModPair> incoming, Character? character)
    {
        if (ReferenceEquals(character, null))
        {
            modifiers.Apply(incoming);
            return;
        }

        AdditiveDamageMath.ApplyModifiers(ref modifiers, incoming,
            character is Player ? DamageModifierProfile.Player : DamageModifierProfile.NonPlayerCharacter);
    }

    internal static void ApplyResistanceForCharacter(HitData hit, HitData.DamageModifiers modifiers, out HitData.DamageModifier significantModifier, Character character)
    {
        int depth = CharacterDamageContext.Enter(hit,
            character is Player ? DamageModifierProfile.Player : DamageModifierProfile.NonPlayerCharacter);
        try
        {
            // Retain other mods' Harmony hooks on the common resistance method.
            // Scope only this exact hit, not the whole RPC or all hits on this thread.
            hit.ApplyResistance(modifiers, out significantModifier);
        }
        finally
        {
            CharacterDamageContext.Exit(depth);
        }
    }
}

[HarmonyPatch(typeof(Character), "UpdateGroundContact")]
internal static class CharacterUpdateGroundContactFallDamagePatch
{
    private static readonly MethodInfo Clamp01Method = AccessTools.Method(typeof(Mathf), nameof(Mathf.Clamp01), new[] { typeof(float) });
    private static readonly MethodInfo ScaleFallDamageProgressMethod = AccessTools.Method(typeof(CharacterUpdateGroundContactFallDamagePatch), nameof(ScaleFallDamageProgress));

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> codeList = new(instructions);
        int clampCallCount = 0;
        for (int i = 0; i < codeList.Count; i++)
        {
            if (codeList[i].Calls(Clamp01Method))
            {
                clampCallCount++;
            }
        }

        if (clampCallCount != 1)
        {
            AdditiveDamageModifierPlugin.AdditiveDamageModifierLogger.LogWarning(
                $"Character.UpdateGroundContact shape changed (expected 1 Mathf.Clamp01 call, found {clampCallCount}). " +
                "Disabling fall damage scaling patch for safety.");
            foreach (CodeInstruction instruction in codeList)
            {
                yield return instruction;
            }

            yield break;
        }

        bool replaced = false;
        foreach (CodeInstruction instruction in codeList)
        {
            if (!replaced && instruction.Calls(Clamp01Method))
            {
                replaced = true;
                instruction.opcode = OpCodes.Call;
                instruction.operand = ScaleFallDamageProgressMethod;
                yield return instruction;
                continue;
            }

            yield return instruction;
        }
    }

    private static float ScaleFallDamageProgress(float normalizedFallDistance)
    {
        float scaledProgress = Mathf.Max(0f, normalizedFallDistance) * AdditiveDamageModifierPlugin.GetFallDamageMultiplier();
        float capProgress = AdditiveDamageModifierPlugin.GetFallDamageCap() / 100f;
        return Mathf.Min(scaledProgress, capProgress);
    }
}

[HarmonyPatch(typeof(HitData), nameof(HitData.ApplyResistance))]
internal static class HitDataApplyResistancePatch
{
    private static bool Prefix(HitData __instance, HitData.DamageModifiers modifiers, ref HitData.DamageModifier significantModifier)
    {
        if (!CharacterDamageContext.TryGetProfile(__instance, out DamageModifierProfile profile))
        {
            return true;
        }

        float normalDmg = __instance.m_damage.m_damage;
        float resistantDmg = 0f;
        float weakDmg = 0f;
        float immuneDmg = 0f;

        ApplyModifierForType(ref __instance.m_damage.m_blunt, modifiers.m_blunt, HitData.DamageType.Blunt, profile, ref normalDmg, ref resistantDmg, ref weakDmg, ref immuneDmg);
        ApplyModifierForType(ref __instance.m_damage.m_slash, modifiers.m_slash, HitData.DamageType.Slash, profile, ref normalDmg, ref resistantDmg, ref weakDmg, ref immuneDmg);
        ApplyModifierForType(ref __instance.m_damage.m_pierce, modifiers.m_pierce, HitData.DamageType.Pierce, profile, ref normalDmg, ref resistantDmg, ref weakDmg, ref immuneDmg);
        ApplyModifierForType(ref __instance.m_damage.m_chop, modifiers.m_chop, HitData.DamageType.Chop, profile, ref normalDmg, ref resistantDmg, ref weakDmg, ref immuneDmg);
        ApplyModifierForType(ref __instance.m_damage.m_pickaxe, modifiers.m_pickaxe, HitData.DamageType.Pickaxe, profile, ref normalDmg, ref resistantDmg, ref weakDmg, ref immuneDmg);
        ApplyModifierForType(ref __instance.m_damage.m_fire, modifiers.m_fire, HitData.DamageType.Fire, profile, ref normalDmg, ref resistantDmg, ref weakDmg, ref immuneDmg);
        ApplyModifierForType(ref __instance.m_damage.m_frost, modifiers.m_frost, HitData.DamageType.Frost, profile, ref normalDmg, ref resistantDmg, ref weakDmg, ref immuneDmg);
        ApplyModifierForType(ref __instance.m_damage.m_lightning, modifiers.m_lightning, HitData.DamageType.Lightning, profile, ref normalDmg, ref resistantDmg, ref weakDmg, ref immuneDmg);
        ApplyModifierForType(ref __instance.m_damage.m_poison, modifiers.m_poison, HitData.DamageType.Poison, profile, ref normalDmg, ref resistantDmg, ref weakDmg, ref immuneDmg);
        ApplyModifierForType(ref __instance.m_damage.m_spirit, modifiers.m_spirit, HitData.DamageType.Spirit, profile, ref normalDmg, ref resistantDmg, ref weakDmg, ref immuneDmg);

        // Valheim 1.0 added this channel. Do not leave its resistance unprocessed
        // when replacing ApplyResistance; it has no configurable player minimum.
        ApplyModifierForType(ref __instance.m_damage.m_nonPlayer, modifiers.m_nonPlayer, HitData.DamageType.NonPlayer, profile, ref normalDmg, ref resistantDmg, ref weakDmg, ref immuneDmg);

        significantModifier = DetermineSignificantModifier(normalDmg, resistantDmg, weakDmg, immuneDmg);
        return false;
    }

    private static void ApplyModifierForType(
        ref float damage,
        HitData.DamageModifier mod,
        HitData.DamageType damageType,
        DamageModifierProfile profile,
        ref float normalDmg,
        ref float resistantDmg,
        ref float weakDmg,
        ref float immuneDmg)
    {
        float minimumDamageTakenMultiplier = profile == DamageModifierProfile.Player
            ? AdditiveDamageModifierPlugin.GetMinimumDamageTakenMultiplier(damageType)
            : 0f;

        damage = AdditiveDamageMath.ApplyModifier(
            damage,
            mod,
            minimumDamageTakenMultiplier,
            profile,
            ref normalDmg,
            ref resistantDmg,
            ref weakDmg,
            ref immuneDmg);
    }

    private static HitData.DamageModifier DetermineSignificantModifier(
        float normalDmg,
        float resistantDmg,
        float weakDmg,
        float immuneDmg)
    {
        if (weakDmg >= resistantDmg && weakDmg >= immuneDmg && weakDmg >= normalDmg)
        {
            return HitData.DamageModifier.Weak;
        }

        if (resistantDmg >= weakDmg && resistantDmg >= immuneDmg && resistantDmg >= normalDmg)
        {
            return HitData.DamageModifier.Resistant;
        }

        if (normalDmg >= resistantDmg && normalDmg >= weakDmg && normalDmg >= immuneDmg)
        {
            return HitData.DamageModifier.Normal;
        }

        return HitData.DamageModifier.Immune;
    }
}

[HarmonyPatch(typeof(Player), "UpdateEnvStatusEffects")]
internal static class PlayerEnvStatusImmunityPatch
{
    private static readonly MethodInfo GetModifierMethod = AccessTools.Method(typeof(HitData.DamageModifiers), nameof(HitData.DamageModifiers.GetModifier));
    private static readonly MethodInfo GetModifierForEnvMethod = AccessTools.Method(typeof(PlayerEnvStatusImmunityPatch), nameof(GetModifierForEnv));

    private static HitData.DamageModifier GetModifierForEnv(ref HitData.DamageModifiers modifiers, HitData.DamageType damageType)
    {
        HitData.DamageModifier modifier = modifiers.GetModifier(damageType);
        if (damageType != HitData.DamageType.Frost)
        {
            return modifier;
        }

        float frostDelta = AdditiveDamageMath.ModifierToDelta(modifier, DamageModifierProfile.Player);
        float threshold = AdditiveDamageModifierPlugin.GetFrostEnvImmunityTriggerDelta();
        bool immuneByThreshold = frostDelta <= threshold;

        if (immuneByThreshold)
        {
            // Preserve vanilla flow in Player.UpdateEnvStatusEffects:
            // return a resistant tier so vanilla !isCold/!isFreezing logic runs unchanged.
            return HitData.DamageModifier.SlightlyResistant;
        }

        // Vanilla also treats raw resistant tiers as environmental immunity. Do not
        // let a single source bypass a stricter threshold than an equal custom sum.
        return HitData.DamageModifier.Normal;
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        int replacementCount = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(GetModifierMethod))
            {
                replacementCount++;
                instruction.opcode = OpCodes.Call;
                instruction.operand = GetModifierForEnvMethod;
                yield return instruction;
                continue;
            }

            yield return instruction;
        }

        if (replacementCount == 0)
        {
            AdditiveDamageModifierPlugin.AdditiveDamageModifierLogger.LogWarning(
                "Player.UpdateEnvStatusEffects shape changed (no DamageModifiers.GetModifier calls found). " +
                "Cold/Freezing immunity threshold patch was not applied.");
        }
    }

}
