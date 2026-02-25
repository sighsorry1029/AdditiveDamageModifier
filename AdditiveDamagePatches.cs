using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AdditiveDamageModifier;

internal static class AdditiveDamageMath
{
    private const int CustomModifierBase = 100000;
    private const int CustomModifierScale = 1000;
    private const float MaxDeltaCap = 100f;
    private const int CustomModifierMinRaw = 99000;
    private const int CustomModifierMaxRaw = 200000;

    public static HitData.DamageModifier Combine(HitData.DamageModifier current, HitData.DamageModifier incoming)
    {
        if (current == HitData.DamageModifier.Ignore || incoming == HitData.DamageModifier.Ignore)
        {
            return HitData.DamageModifier.Ignore;
        }

        float combinedDelta = ModifierToDelta(current) + ModifierToDelta(incoming);
        combinedDelta = Mathf.Clamp(combinedDelta, AdditiveDamageModifierPlugin.GetMinimumDeltaCap(), MaxDeltaCap);
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
        float clamped = Mathf.Clamp(delta, AdditiveDamageModifierPlugin.GetMinimumDeltaCap(), MaxDeltaCap);
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

        float minDeltaCap = AdditiveDamageModifierPlugin.GetMinimumDeltaCap();
        float delta = Mathf.Clamp(AdditiveDamageMath.ModifierToDelta(mod), minDeltaCap, 100f);
        float finalDamage = Mathf.Max(0f, baseDamage * Mathf.Max(AdditiveDamageModifierPlugin.GetMinimumDamageTakenMultiplier(), 1f + delta));

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

[HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
internal static class PlayerImmuneToIgnorePatch
{
    private static readonly FieldInfo? DamageModifiersField = AccessTools.Field(typeof(Character), "m_damageModifiers");

    private static void Prefix(Character __instance)
    {
        if (__instance is not Player || DamageModifiersField is null)
        {
            return;
        }

        if (DamageModifiersField.GetValue(__instance) is not HitData.DamageModifiers modifiers)
        {
            return;
        }

        bool changed = false;

        if (modifiers.m_chop == HitData.DamageModifier.Immune)
        {
            modifiers.m_chop = HitData.DamageModifier.Ignore;
            changed = true;
        }

        if (modifiers.m_pickaxe == HitData.DamageModifier.Immune)
        {
            modifiers.m_pickaxe = HitData.DamageModifier.Ignore;
            changed = true;
        }

        if (modifiers.m_spirit == HitData.DamageModifier.Immune)
        {
            modifiers.m_spirit = HitData.DamageModifier.Ignore;
            changed = true;
        }

        if (changed)
        {
            DamageModifiersField.SetValue(__instance, modifiers);
        }
    }
}
