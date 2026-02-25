using System;
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

    public static bool UsePlayerMinimumCap => _playerDamageContextDepth > 0;

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

[HarmonyPatch(typeof(HitData.DamageModifiers), nameof(HitData.DamageModifiers.ApplyIfBetter))]
internal static class DamageModifiersApplyIfBetterPatch
{
    private static bool Prefix(ref HitData.DamageModifier original, HitData.DamageModifier mod)
    {
        original = AdditiveDamageMath.Combine(original, mod);
        return false;
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
            ? AdditiveDamageModifierPlugin.GetMinimumDamageTakenMultiplier()
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
