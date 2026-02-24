using HarmonyLib;
using UnityEngine;

namespace AdditiveDamageModifier;

internal static class AdditiveDamageMath
{
    private const int CustomModifierBase = 100000;
    private const int CustomModifierScale = 1000;
    private const float MinDeltaCap = -1f;
    private const float MaxDeltaCap = 100f;
    private const int CustomModifierMinRaw = 99000;
    private const int CustomModifierMaxRaw = 200000;

    public static HitData.DamageModifier Combine(HitData.DamageModifier current, HitData.DamageModifier incoming)
    {
        if (incoming == HitData.DamageModifier.Ignore)
        {
            return current;
        }

        if (current == HitData.DamageModifier.Ignore)
        {
            current = HitData.DamageModifier.Normal;
        }

        float combinedDelta = ModifierToDelta(current) + ModifierToDelta(incoming);
        combinedDelta = Mathf.Clamp(combinedDelta, MinDeltaCap, MaxDeltaCap);
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

    private static bool IsCustomModifier(int rawValue) => rawValue >= CustomModifierMinRaw && rawValue <= CustomModifierMaxRaw;

    private static HitData.DamageModifier EncodeCustomDelta(float delta)
    {
        float clamped = Mathf.Clamp(delta, MinDeltaCap, MaxDeltaCap);
        int encoded = CustomModifierBase + Mathf.RoundToInt(clamped * CustomModifierScale);
        return (HitData.DamageModifier)encoded;
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

        float delta = Mathf.Clamp(AdditiveDamageMath.ModifierToDelta(mod), -1f, 100f);
        float finalDamage = Mathf.Max(0f, baseDamage * Mathf.Max(0f, 1f + delta));

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
