using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace AdditiveDamageModifier;

internal static class AdditiveDamageMath
{
    private const int CustomModifierBase = 100000;
    private const int CustomModifierScale = 1000;
    private const float CombineClampAbs = 100000f;
    private const float MaxDeltaCap = 100f;
    private const int CustomModifierThreshold = 10000;

    public static HitData.DamageModifier Combine(HitData.DamageModifier current, HitData.DamageModifier incoming)
    {
        if (current == HitData.DamageModifier.Ignore || incoming == HitData.DamageModifier.Ignore)
        {
            return HitData.DamageModifier.Ignore;
        }

        float combinedDelta = ModifierToDelta(current) + ModifierToDelta(incoming);
        // Do not clamp by the minimum damage cap here; clamping during accumulation makes
        // the final result order-dependent when several modifiers are combined.
        combinedDelta = Mathf.Clamp(combinedDelta, -CombineClampAbs, CombineClampAbs);
        return EncodeCustomDelta(combinedDelta);
    }

    public static float ModifierToDelta(HitData.DamageModifier modifier)
    {
        int raw = (int)modifier;
        if (IsCustomModifier(raw))
        {
            return (raw - CustomModifierBase) / (float)CustomModifierScale;
        }

        return AdditiveDamageModifierPlugin.GetConfiguredDelta(modifier);
    }

    private static bool IsCustomModifier(int rawValue) => rawValue <= -CustomModifierThreshold || rawValue >= CustomModifierThreshold;

    private static HitData.DamageModifier EncodeCustomDelta(float delta)
    {
        float clamped = Mathf.Clamp(delta, -CombineClampAbs, CombineClampAbs);
        int encoded = CustomModifierBase + Mathf.RoundToInt(clamped * CustomModifierScale);
        return (HitData.DamageModifier)encoded;
    }
}

internal static class DamageCapContext
{
    [ThreadStatic] private static int _playerDamageContextDepth;
    [ThreadStatic] private static Stack<HitData.DamageType>? _damageTypeStack;

    public static bool UsePlayerMinimumCap => _playerDamageContextDepth > 0;
    public static HitData.DamageType CurrentDamageType =>
        _damageTypeStack is { Count: > 0 } stack ? stack.Peek() : HitData.DamageType.Blunt;

    public static void Push(bool isPlayerDamageContext)
    {
        if (isPlayerDamageContext)
        {
            _playerDamageContextDepth++;
        }
    }

    public static void Pop(bool wasPlayerDamageContext)
    {
        if (!wasPlayerDamageContext || _playerDamageContextDepth <= 0)
        {
            return;
        }

        _playerDamageContextDepth--;
    }

    public static void PushDamageType(HitData.DamageType damageType)
    {
        (_damageTypeStack ??= new Stack<HitData.DamageType>(4)).Push(damageType);
    }

    public static void PopDamageType()
    {
        if (_damageTypeStack is { Count: > 0 } stack)
        {
            stack.Pop();
        }
    }
}

[HarmonyPatch(typeof(Character), "RPC_Damage")]
internal static class CharacterRpcDamageContextPatch
{
    private static void Prefix(Character __instance, out bool __state)
    {
        __state = __instance is Player;
        DamageCapContext.Push(__state);
    }

    private static void Postfix(bool __state)
    {
        DamageCapContext.Pop(__state);
    }
}

[HarmonyPatch(typeof(Character), "Damage")]
internal static class CharacterDamageContextPatch
{
    private static void Prefix(Character __instance, out bool __state)
    {
        __state = __instance is Player;
        DamageCapContext.Push(__state);
    }

    private static void Postfix(bool __state)
    {
        DamageCapContext.Pop(__state);
    }
}

[HarmonyPatch(typeof(Character), "ApplyDamage")]
internal static class CharacterApplyDamageContextPatch
{
    private static void Prefix(Character __instance, out bool __state)
    {
        __state = __instance is Player;
        DamageCapContext.Push(__state);
    }

    private static void Postfix(bool __state)
    {
        DamageCapContext.Pop(__state);
    }
}

[HarmonyPatch(typeof(HitData.DamageModifiers), nameof(HitData.DamageModifiers.ApplyIfBetter))]
internal static class DamageModifiersApplyIfBetterPatch
{
    private static bool Prefix(ref HitData.DamageModifier original, HitData.DamageModifier mod)
    {
        original = AdditiveDamageMath.Combine(original, mod);
        return false;
    }
}

[HarmonyPatch(typeof(HitData), nameof(HitData.ApplyResistance))]
internal static class HitDataApplyResistanceDamageTypeContextPatch
{
    private static readonly MethodInfo ApplyModifierMethod = AccessTools.Method(
        typeof(HitData),
        nameof(HitData.ApplyModifier),
        new[] { typeof(float), typeof(HitData.DamageModifier), typeof(float).MakeByRefType(), typeof(float).MakeByRefType(), typeof(float).MakeByRefType(), typeof(float).MakeByRefType() });

    private static readonly MethodInfo ApplyModifierWithTypeMethod = AccessTools.Method(
        typeof(HitDataApplyResistanceDamageTypeContextPatch),
        nameof(ApplyModifierWithType));

    private static readonly HitData.DamageType[] ApplyResistanceDamageTypeOrder =
    {
        HitData.DamageType.Blunt,
        HitData.DamageType.Slash,
        HitData.DamageType.Pierce,
        HitData.DamageType.Chop,
        HitData.DamageType.Pickaxe,
        HitData.DamageType.Fire,
        HitData.DamageType.Frost,
        HitData.DamageType.Lightning,
        HitData.DamageType.Poison,
        HitData.DamageType.Spirit
    };

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        int applyModifierCallIndex = 0;

        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(ApplyModifierMethod))
            {
                HitData.DamageType damageType = applyModifierCallIndex < ApplyResistanceDamageTypeOrder.Length
                    ? ApplyResistanceDamageTypeOrder[applyModifierCallIndex]
                    : HitData.DamageType.Blunt;
                applyModifierCallIndex++;
                yield return new CodeInstruction(OpCodes.Ldc_I4, (int)damageType);
                yield return new CodeInstruction(OpCodes.Call, ApplyModifierWithTypeMethod);
                continue;
            }

            yield return instruction;
        }
    }

    private static float ApplyModifierWithType(
        HitData hitData,
        float baseDamage,
        HitData.DamageModifier mod,
        ref float normalDmg,
        ref float resistantDmg,
        ref float weakDmg,
        ref float immuneDmg,
        HitData.DamageType damageType)
    {
        // Type-specific minimum cap is only used for player-damage context.
        // Skip context push/pop on every other path to minimize per-hit overhead.
        if (!DamageCapContext.UsePlayerMinimumCap)
        {
            return hitData.ApplyModifier(baseDamage, mod, ref normalDmg, ref resistantDmg, ref weakDmg, ref immuneDmg);
        }

        DamageCapContext.PushDamageType(damageType);
        try
        {
            return hitData.ApplyModifier(baseDamage, mod, ref normalDmg, ref resistantDmg, ref weakDmg, ref immuneDmg);
        }
        finally
        {
            DamageCapContext.PopDamageType();
        }
    }
}

[HarmonyPatch(typeof(HitData))]
internal static class HitDataApplyModifierPatch
{
    [HarmonyPatch(nameof(HitData.ApplyModifier))]
    [HarmonyPatch(new[] { typeof(float), typeof(HitData.DamageModifier), typeof(float), typeof(float), typeof(float), typeof(float) }, new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Ref, ArgumentType.Ref, ArgumentType.Ref })]
    private static bool Prefix(float baseDamage, HitData.DamageModifier mod, ref float normalDmg, ref float resistantDmg, ref float weakDmg, ref float immuneDmg, ref float __result)
    {
        if (mod == HitData.DamageModifier.Ignore)
        {
            __result = 0f;
            return false;
        }

        float minimumDamageTakenMultiplier = DamageCapContext.UsePlayerMinimumCap
            ? AdditiveDamageModifierPlugin.GetMinimumDamageTakenMultiplier(DamageCapContext.CurrentDamageType)
            : 0f;
        float minDeltaCap = minimumDamageTakenMultiplier - 1f;
        float delta = Mathf.Clamp(AdditiveDamageMath.ModifierToDelta(mod), minDeltaCap, 100f);
        float finalDamage = Mathf.Max(0f, baseDamage * Mathf.Max(minimumDamageTakenMultiplier, 1f + delta));

        if (Mathf.Approximately(delta, 0f))
        {
            normalDmg += baseDamage;
        }
        else if (delta < 0f)
        {
            if (delta <= -0.35f)
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

        __result = finalDamage;
        return false;
    }
}

internal static class FrostStatusImmunityContext
{
    public static HitData.DamageModifier GetModifierForEnv(ref HitData.DamageModifiers modifiers, HitData.DamageType damageType)
    {
        HitData.DamageModifier modifier = modifiers.GetModifier(damageType);
        if (damageType != HitData.DamageType.Frost)
        {
            return modifier;
        }

        float frostDelta = AdditiveDamageMath.ModifierToDelta(modifier);
        float threshold = GetEnvImmunityFrostThreshold();
        bool immuneByThreshold = frostDelta <= threshold;

        if (immuneByThreshold)
        {
            // Preserve vanilla flow in Player.UpdateEnvStatusEffects:
            // return a resistant tier so vanilla !isCold/!isFreezing logic runs unchanged.
            return HitData.DamageModifier.SlightlyResistant;
        }

        return modifier;
    }

    private static float GetEnvImmunityFrostThreshold() =>
        AdditiveDamageModifierPlugin.GetFrostEnvImmunityTriggerDelta();
}

[HarmonyPatch(typeof(Player), "UpdateEnvStatusEffects")]
internal static class PlayerEnvStatusImmunityPatch
{
    private static readonly MethodInfo GetModifierMethod = AccessTools.Method(typeof(HitData.DamageModifiers), nameof(HitData.DamageModifiers.GetModifier));
    private static readonly MethodInfo GetModifierForEnvMethod = AccessTools.Method(typeof(FrostStatusImmunityContext), nameof(FrostStatusImmunityContext.GetModifierForEnv));

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(GetModifierMethod))
            {
                yield return new CodeInstruction(OpCodes.Call, GetModifierForEnvMethod);
                continue;
            }

            yield return instruction;
        }
    }

}
