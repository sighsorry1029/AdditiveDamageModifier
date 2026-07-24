using System;
using System.Globalization;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace AdditiveDamageModifier;

internal static class AdditiveDamageCompendium
{
    private const string PageTopic = "$adm_compendium_title";

    internal static void AddPage(TextsDialog dialog)
    {
        if (dialog?.m_texts == null)
        {
            return;
        }

        dialog.m_texts.RemoveAll(text => string.Equals(text?.m_topic, PageTopic, StringComparison.Ordinal));
        dialog.m_texts.Add(new TextsDialog.TextInfo(PageTopic, BuildPageText()));
    }

    private static string BuildPageText()
    {
        StringBuilder builder = new();

        AppendParagraph(builder, Localize("$adm_compendium_intro"));

        AppendHeading(builder, "$adm_compendium_current_heading");
        AppendParagraph(builder, Localize("$adm_compendium_current_intro"));
        foreach (DamageModifierDefinition definition in AdditiveDamageDefinitions.DamageModifiers)
        {
            AppendValueLine(
                builder,
                Localize(definition.LocalizationKey, definition.DisplayName),
                AdditiveDamageDisplay.FormatModifierPercent(definition.Modifier));
        }

        AppendHeading(builder, "$adm_compendium_calculation_heading");
        AppendParagraph(builder, Localize("$adm_compendium_sum_rule"));
        AppendParagraph(builder, Localize("$adm_compendium_formula"));
        AppendParagraph(builder, BuildExample());

        AppendHeading(builder, "$adm_compendium_minimum_heading");
        AppendParagraph(builder, Localize("$adm_compendium_minimum_intro"));
        foreach (DamageTypeDefinition definition in AdditiveDamageDefinitions.DamageTypes)
        {
            if (!definition.HasPlayerMinimumCap)
            {
                continue;
            }

            float minimumMultiplier = AdditiveDamageModifierPlugin.GetMinimumDamageTakenMultiplier(definition.Type);
            string minimumTaken = FormatUnsignedPercent(minimumMultiplier * 100f);
            string minimumTotal = AdditiveDamageDisplay.FormatMinimumTotalPercent(definition.Type);
            AppendParagraph(
                builder,
                Format(
                    "$adm_compendium_minimum_line",
                    "{0}: at least {1} of original damage (MinTotal {2})",
                    Localize(definition.LocalizationKey, definition.DisplayName),
                    minimumTaken,
                    minimumTotal));
        }

        AppendParagraph(builder, Localize("$adm_compendium_spirit_rule"));

        AppendHeading(builder, "$adm_compendium_vanilla_heading");
        AppendParagraph(builder, Localize("$adm_compendium_vanilla_rule"));
        foreach (DamageModifierDefinition definition in AdditiveDamageDefinitions.DamageModifiers)
        {
            AppendValueLine(
                builder,
                Localize(definition.LocalizationKey, definition.DisplayName),
                AdditiveDamageDisplay.FormatPercent(GetVanillaPercent(definition.Modifier)));
        }

        AppendParagraph(builder, Localize("$adm_compendium_additive_rule"));

        AppendHeading(builder, "$adm_compendium_special_heading");
        AppendParagraph(
            builder,
            Format(
                "$adm_compendium_immune_rule",
                "Immune contributes {0}, so weakness can offset it. Player minimums still apply to capped damage types.",
                AdditiveDamageDisplay.FormatModifierPercent(HitData.DamageModifier.Immune)));
        AppendParagraph(builder, Localize("$adm_compendium_ignore_rule"));
        AppendParagraph(
            builder,
            Format(
                "$adm_compendium_frost_rule",
                "Cold and Freezing immunity triggers when the combined Frost modifier is {0} or lower.",
                AdditiveDamageDisplay.FormatPercent(Mathf.RoundToInt(AdditiveDamageModifierPlugin.GetFrostEnvImmunityTriggerDelta() * 100f))));

        return builder.ToString().TrimEnd();
    }

    private static string BuildExample()
    {
        HitData.DamageModifier[] modifiers =
        {
            HitData.DamageModifier.Resistant,
            HitData.DamageModifier.VeryResistant,
            HitData.DamageModifier.Weak,
            HitData.DamageModifier.SlightlyResistant
        };

        int total = 0;
        StringBuilder expression = new();
        for (int index = 0; index < modifiers.Length; index++)
        {
            int value = Mathf.RoundToInt(AdditiveDamageMath.ModifierToDelta(modifiers[index]) * 100f);
            total += value;
            if (index == 0)
            {
                expression.Append(AdditiveDamageDisplay.FormatPercent(value));
            }
            else
            {
                expression
                    .Append(value < 0 ? " - " : " + ")
                    .Append(Math.Abs(value).ToString(CultureInfo.InvariantCulture))
                    .Append('%');
            }
        }

        int damageTakenPercent = Mathf.Max(0, 100 + total);
        return Format(
            "$adm_compendium_example",
            "Example: {0} = {1}, so {2} of the original damage is taken before any player minimum.",
            expression,
            AdditiveDamageDisplay.FormatPercent(total),
            FormatUnsignedPercent(damageTakenPercent));
    }

    private static int GetVanillaPercent(HitData.DamageModifier modifier)
    {
        return modifier switch
        {
            HitData.DamageModifier.SlightlyResistant => -25,
            HitData.DamageModifier.Resistant => -50,
            HitData.DamageModifier.VeryResistant => -75,
            HitData.DamageModifier.SlightlyWeak => 25,
            HitData.DamageModifier.Weak => 50,
            HitData.DamageModifier.VeryWeak => 100,
            HitData.DamageModifier.Immune => -100,
            _ => 0
        };
    }

    private static void AppendHeading(StringBuilder builder, string token)
    {
        if (builder.Length > 0)
        {
            builder.Append('\n');
        }

        builder
            .Append("<color=#FFD27A><b>")
            .Append(Localize(token))
            .Append("</b></color>\n");
    }

    private static void AppendValueLine(StringBuilder builder, string name, string value)
    {
        builder
            .Append("- <color=orange>")
            .Append(name)
            .Append("</color>: ")
            .Append(value)
            .Append('\n');
    }

    private static void AppendParagraph(StringBuilder builder, string text)
    {
        builder.Append(text).Append('\n');
    }

    private static string Localize(string token)
    {
        return Localization.instance?.Localize(token) ?? token;
    }

    private static string Localize(string token, string fallback)
    {
        string localized = Localize(token);
        string word = token.StartsWith("$", StringComparison.Ordinal) ? token.Substring(1) : token;
        string missingWord = $"[{word}]";
        return string.IsNullOrWhiteSpace(localized)
               || string.Equals(localized, token, StringComparison.Ordinal)
               || string.Equals(localized, missingWord, StringComparison.Ordinal)
            ? fallback
            : localized;
    }

    private static string Format(string token, string fallback, params object[] args)
    {
        string format = Localize(token, fallback);
        try
        {
            return string.Format(CultureInfo.InvariantCulture, format, args);
        }
        catch (FormatException)
        {
            return string.Format(CultureInfo.InvariantCulture, fallback, args);
        }
    }

    private static string FormatUnsignedPercent(float value)
    {
        return Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture) + "%";
    }
}

[HarmonyPatch(typeof(TextsDialog), "UpdateTextsList")]
internal static class TextsDialogUpdateTextsListAdditiveDamagePatch
{
    private static void Postfix(TextsDialog __instance)
    {
        AdditiveDamageCompendium.AddPage(__instance);
    }
}
