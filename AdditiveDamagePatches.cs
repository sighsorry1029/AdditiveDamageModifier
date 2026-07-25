using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace AdditiveDamageModifier;

internal static class AdditiveDamageMath
{
    private const int CustomModifierBase = 1000000000;
    private const int CustomModifierScale = 1000;
    private const int CombineClampAbs = 100000;
    private const int CustomModifierZero = CustomModifierBase + CombineClampAbs * CustomModifierScale;
    private const int CustomModifierMax = CustomModifierBase + 2 * CombineClampAbs * CustomModifierScale;

    public static HitData.DamageModifier Combine(HitData.DamageModifier current, HitData.DamageModifier incoming)
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

        float combinedDelta = ModifierToDelta(current) + ModifierToDelta(incoming);
        // Do not clamp by the minimum damage cap here; clamping during accumulation makes
        // the final result order-dependent when several modifiers are combined.
        combinedDelta = Mathf.Clamp(combinedDelta, -CombineClampAbs, CombineClampAbs);
        return EncodeCustomDelta(combinedDelta);
    }

    public static float ModifierToDelta(HitData.DamageModifier modifier)
    {
        if (TryDecodeCustomDelta(modifier, out float delta))
        {
            return delta;
        }

        return AdditiveDamageModifierPlugin.GetConfiguredDelta(modifier);
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
        float delta = Mathf.Clamp(ModifierToDelta(mod), minDeltaCap, 100f);
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

internal static class DamageCapContext
{
    [ThreadStatic] private static List<HitData>? _playerHitStack;

    public static void EnterPlayerContext(HitData hitData)
    {
        (_playerHitStack ??= new List<HitData>(4)).Add(hitData);
    }

    public static void ExitPlayerContext(HitData hitData)
    {
        if (_playerHitStack is not { Count: > 0 } stack)
        {
            return;
        }

        for (int i = stack.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(stack[i], hitData))
            {
                continue;
            }

            stack.RemoveAt(i);
            if (stack.Count == 0)
            {
                _playerHitStack = null;
            }
            return;
        }
    }

    public static bool IsPlayerContext(HitData hitData)
    {
        return _playerHitStack is { Count: > 0 } stack && ReferenceEquals(stack[stack.Count - 1], hitData);
    }
}

[HarmonyPatch(typeof(Character), "RPC_Damage")]
internal static class CharacterRpcDamagePlayerCapPatch
{
    private static void Prefix(Character __instance, HitData hit)
    {
        if (__instance is Player && hit != null)
        {
            DamageCapContext.EnterPlayerContext(hit);
        }
    }

    private static Exception? Finalizer(Character __instance, HitData hit, Exception? __exception)
    {
        if (__instance is Player && hit != null)
        {
            DamageCapContext.ExitPlayerContext(hit);
        }

        return __exception;
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

[HarmonyPatch(typeof(HitData.DamageModifiers), "ApplyIfBetter")]
internal static class DamageModifiersApplyIfBetterPatch
{
    private static bool Prefix(ref HitData.DamageModifier original, HitData.DamageModifier mod)
    {
        original = AdditiveDamageMath.Combine(original, mod);
        return false;
    }
}

[HarmonyPatch(typeof(HitData), nameof(HitData.ApplyResistance))]
internal static class HitDataApplyResistancePatch
{
    private static bool Prefix(HitData __instance, HitData.DamageModifiers modifiers, ref HitData.DamageModifier significantModifier)
    {
        float normalDmg = __instance.m_damage.m_damage;
        float resistantDmg = 0f;
        float weakDmg = 0f;
        float immuneDmg = 0f;
        bool isPlayerContext = DamageCapContext.IsPlayerContext(__instance);

        ApplyModifierForType(ref __instance.m_damage.m_blunt, modifiers.m_blunt, HitData.DamageType.Blunt, isPlayerContext, ref normalDmg, ref resistantDmg, ref weakDmg, ref immuneDmg);
        ApplyModifierForType(ref __instance.m_damage.m_slash, modifiers.m_slash, HitData.DamageType.Slash, isPlayerContext, ref normalDmg, ref resistantDmg, ref weakDmg, ref immuneDmg);
        ApplyModifierForType(ref __instance.m_damage.m_pierce, modifiers.m_pierce, HitData.DamageType.Pierce, isPlayerContext, ref normalDmg, ref resistantDmg, ref weakDmg, ref immuneDmg);
        ApplyModifierForType(ref __instance.m_damage.m_chop, modifiers.m_chop, HitData.DamageType.Chop, isPlayerContext, ref normalDmg, ref resistantDmg, ref weakDmg, ref immuneDmg);
        ApplyModifierForType(ref __instance.m_damage.m_pickaxe, modifiers.m_pickaxe, HitData.DamageType.Pickaxe, isPlayerContext, ref normalDmg, ref resistantDmg, ref weakDmg, ref immuneDmg);
        ApplyModifierForType(ref __instance.m_damage.m_fire, modifiers.m_fire, HitData.DamageType.Fire, isPlayerContext, ref normalDmg, ref resistantDmg, ref weakDmg, ref immuneDmg);
        ApplyModifierForType(ref __instance.m_damage.m_frost, modifiers.m_frost, HitData.DamageType.Frost, isPlayerContext, ref normalDmg, ref resistantDmg, ref weakDmg, ref immuneDmg);
        ApplyModifierForType(ref __instance.m_damage.m_lightning, modifiers.m_lightning, HitData.DamageType.Lightning, isPlayerContext, ref normalDmg, ref resistantDmg, ref weakDmg, ref immuneDmg);
        ApplyModifierForType(ref __instance.m_damage.m_poison, modifiers.m_poison, HitData.DamageType.Poison, isPlayerContext, ref normalDmg, ref resistantDmg, ref weakDmg, ref immuneDmg);
        ApplyModifierForType(ref __instance.m_damage.m_spirit, modifiers.m_spirit, HitData.DamageType.Spirit, isPlayerContext, ref normalDmg, ref resistantDmg, ref weakDmg, ref immuneDmg);

        significantModifier = DetermineSignificantModifier(normalDmg, resistantDmg, weakDmg, immuneDmg);
        return false;
    }

    private static void ApplyModifierForType(
        ref float damage,
        HitData.DamageModifier mod,
        HitData.DamageType damageType,
        bool isPlayerContext,
        ref float normalDmg,
        ref float resistantDmg,
        ref float weakDmg,
        ref float immuneDmg)
    {
        float minimumDamageTakenMultiplier = isPlayerContext
            ? AdditiveDamageModifierPlugin.GetMinimumDamageTakenMultiplier(damageType)
            : 0f;

        damage = AdditiveDamageMath.ApplyModifier(
            damage,
            mod,
            minimumDamageTakenMultiplier,
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

[HarmonyPatch(typeof(HitData), nameof(HitData.ApplyModifier))]
internal static class HitDataApplyModifierPatch
{
    [HarmonyPatch(new[] { typeof(float), typeof(HitData.DamageModifier), typeof(float), typeof(float), typeof(float), typeof(float) }, new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Ref, ArgumentType.Ref, ArgumentType.Ref })]
    private static bool Prefix(
        float baseDamage,
        HitData.DamageModifier mod,
        ref float normalDmg,
        ref float resistantDmg,
        ref float weakDmg,
        ref float immuneDmg,
        ref float __result)
    {
        if (AdditiveDamageMath.IsCustomModifier(mod))
        {
            __result = AdditiveDamageMath.ApplyModifier(baseDamage, mod, 0f, ref normalDmg, ref resistantDmg, ref weakDmg, ref immuneDmg);
            return false;
        }

        return true;
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

        float frostDelta = AdditiveDamageMath.ModifierToDelta(modifier);
        float threshold = AdditiveDamageModifierPlugin.GetFrostEnvImmunityTriggerDelta();
        bool immuneByThreshold = frostDelta <= threshold;

        if (immuneByThreshold)
        {
            // Preserve vanilla flow in Player.UpdateEnvStatusEffects:
            // return a resistant tier so vanilla !isCold/!isFreezing logic runs unchanged.
            return HitData.DamageModifier.SlightlyResistant;
        }

        return modifier;
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
