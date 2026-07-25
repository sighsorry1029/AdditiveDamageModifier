using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace AdditiveDamageModifier;

internal static class AdditiveDamageStatusEffectCatalog
{
    private const string NamePrefix = "adm_";
    private const int IconSize = 32;
    private static readonly Dictionary<string, Sprite> Icons = new();

    public static void Register(ObjectDB objectDb)
    {
        if (objectDb?.m_StatusEffects == null)
        {
            return;
        }

        int addedCount = 0;
        foreach (DamageTypeDefinition damageType in AdditiveDamageDefinitions.DamageTypes)
        {
            if (!damageType.HasStatusEffect)
            {
                continue;
            }

            foreach (DamageModifierDefinition damageModifier in AdditiveDamageDefinitions.DamageModifiers)
            {
                string statusEffectName = GetStatusEffectName(damageType.StatusName, damageModifier.StatusName);
                if (objectDb.GetStatusEffect(StringExtensionMethods.GetStableHashCode(statusEffectName)) != null)
                {
                    continue;
                }

                objectDb.m_StatusEffects.Add(CreateStatusEffect(statusEffectName, damageType, damageModifier));
                addedCount++;
            }
        }

        if (addedCount > 0)
        {
            AdditiveDamageModifierPlugin.AdditiveDamageModifierLogger.LogDebug(
                $"Registered {addedCount} additive damage modifier status effects.");
        }
    }

    private static SE_Stats CreateStatusEffect(
        string statusEffectName,
        DamageTypeDefinition damageType,
        DamageModifierDefinition damageModifier)
    {
        string damageTypeDisplayName = FormatStatusDisplayName(damageType.StatusName);
        string modifierDisplayName = FormatStatusDisplayName(damageModifier.StatusName);
        SE_AdditiveDamageModifier statusEffect = ScriptableObject.CreateInstance<SE_AdditiveDamageModifier>();
        statusEffect.name = statusEffectName;
        statusEffect.m_name = $"{damageTypeDisplayName} {modifierDisplayName}";
        statusEffect.m_tooltip = $"Applies {damageModifier.DisplayName} to {damageType.DisplayName} damage.";
        statusEffect.m_icon = GetIcon(damageType, damageModifier);
        statusEffect.m_modifier = damageModifier.Modifier;
        statusEffect.m_damageTypeName = damageTypeDisplayName;
        statusEffect.m_modifierName = modifierDisplayName;
        statusEffect.m_mods = new List<HitData.DamageModPair>
        {
            new()
            {
                m_type = damageType.Type,
                m_modifier = damageModifier.Modifier
            }
        };

        return statusEffect;
    }

    private static Sprite GetIcon(DamageTypeDefinition damageType, DamageModifierDefinition damageModifier)
    {
        string key = $"{damageType.StatusName}_{damageModifier.StatusName}";
        if (Icons.TryGetValue(key, out Sprite icon))
        {
            return icon;
        }

        Texture2D texture = new(IconSize, IconSize, TextureFormat.RGBA32, false)
        {
            name = $"{key}_icon",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        DrawIcon(texture, GetDamageTypeColor(damageType.Type), GetModifierColor(damageModifier.Modifier), damageModifier.Modifier);
        texture.Apply();

        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, IconSize, IconSize), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = texture.name;
        UnityEngine.Object.DontDestroyOnLoad(texture);
        UnityEngine.Object.DontDestroyOnLoad(sprite);
        Icons[key] = sprite;
        return sprite;
    }

    private static void DrawIcon(Texture2D texture, Color baseColor, Color modifierColor, HitData.DamageModifier modifier)
    {
        Color darkBase = Color.Lerp(baseColor, Color.black, 0.35f);
        Color borderColor = modifierColor;

        for (int y = 0; y < IconSize; y++)
        {
            for (int x = 0; x < IconSize; x++)
            {
                bool border = x < 3 || y < 3 || x >= IconSize - 3 || y >= IconSize - 3;
                texture.SetPixel(x, y, border ? borderColor : darkBase);
            }
        }

        FillRect(texture, 8, 8, 16, 16, baseColor);
        DrawModifierGlyph(texture, modifier, modifierColor);
    }

    private static void DrawModifierGlyph(Texture2D texture, HitData.DamageModifier modifier, Color color)
    {
        switch (modifier)
        {
            case HitData.DamageModifier.VeryWeak:
                DrawTextGlyph(texture, "+3", color);
                break;
            case HitData.DamageModifier.Weak:
                DrawTextGlyph(texture, "+2", color);
                break;
            case HitData.DamageModifier.SlightlyWeak:
                DrawTextGlyph(texture, "+1", color);
                break;
            case HitData.DamageModifier.SlightlyResistant:
                DrawTextGlyph(texture, "-1", color);
                break;
            case HitData.DamageModifier.Resistant:
                DrawTextGlyph(texture, "-2", color);
                break;
            case HitData.DamageModifier.VeryResistant:
                DrawTextGlyph(texture, "-3", color);
                break;
            case HitData.DamageModifier.Immune:
                for (int i = 8; i < 24; i++)
                {
                    FillRect(texture, i, i, 3, 3, color);
                    FillRect(texture, i, IconSize - i - 3, 3, 3, color);
                }
                break;
        }
    }

    private static void DrawTextGlyph(Texture2D texture, string text, Color color)
    {
        const int scale = 2;
        const int gap = 1;
        const int glyphHeight = 5;
        int width = 0;
        for (int i = 0; i < text.Length; i++)
        {
            width += GetGlyphRows(text[i])[0].Length * scale;
            if (i < text.Length - 1)
            {
                width += gap;
            }
        }

        int startX = (IconSize - width) / 2;
        int startY = (IconSize - glyphHeight * scale) / 2;
        int x = startX;
        foreach (char character in text)
        {
            string[] rows = GetGlyphRows(character);
            DrawGlyph(texture, rows, x, startY, scale, color);
            x += rows[0].Length * scale + gap;
        }
    }

    private static void DrawGlyph(Texture2D texture, string[] rows, int left, int bottom, int scale, Color color)
    {
        for (int row = 0; row < rows.Length; row++)
        {
            string pattern = rows[row];
            int y = bottom + (rows.Length - 1 - row) * scale;
            for (int column = 0; column < pattern.Length; column++)
            {
                if (pattern[column] != '1')
                {
                    continue;
                }

                FillRect(texture, left + column * scale, y, scale, scale, color);
            }
        }
    }

    private static string[] GetGlyphRows(char character)
    {
        return character switch
        {
            '+' => new[] { "010", "010", "111", "010", "010" },
            '-' => new[] { "000", "000", "111", "000", "000" },
            '1' => new[] { "010", "110", "010", "010", "111" },
            '2' => new[] { "111", "001", "111", "100", "111" },
            '3' => new[] { "111", "001", "111", "001", "111" },
            _ => new[] { "000", "000", "000", "000", "000" }
        };
    }

    private static void FillRect(Texture2D texture, int left, int bottom, int width, int height, Color color)
    {
        for (int y = bottom; y < bottom + height; y++)
        {
            for (int x = left; x < left + width; x++)
            {
                texture.SetPixel(x, y, color);
            }
        }
    }

    private static Color GetDamageTypeColor(HitData.DamageType damageType)
    {
        return damageType switch
        {
            HitData.DamageType.Blunt => new Color(0.62f, 0.53f, 0.43f, 1f),
            HitData.DamageType.Pierce => new Color(0.72f, 0.72f, 0.68f, 1f),
            HitData.DamageType.Slash => new Color(0.78f, 0.18f, 0.18f, 1f),
            HitData.DamageType.Fire => new Color(1f, 0.38f, 0.08f, 1f),
            HitData.DamageType.Poison => new Color(0.28f, 0.72f, 0.22f, 1f),
            HitData.DamageType.Frost => new Color(0.32f, 0.72f, 0.95f, 1f),
            HitData.DamageType.Lightning => new Color(0.95f, 0.82f, 0.2f, 1f),
            HitData.DamageType.Spirit => new Color(0.72f, 0.55f, 0.95f, 1f),
            _ => Color.white
        };
    }

    private static Color GetModifierColor(HitData.DamageModifier modifier)
    {
        return modifier switch
        {
            HitData.DamageModifier.VeryWeak => new Color(1f, 0.08f, 0.08f, 1f),
            HitData.DamageModifier.Weak => new Color(1f, 0.35f, 0.08f, 1f),
            HitData.DamageModifier.SlightlyWeak => new Color(1f, 0.68f, 0.18f, 1f),
            HitData.DamageModifier.SlightlyResistant => new Color(0.3f, 0.85f, 1f, 1f),
            HitData.DamageModifier.Resistant => new Color(0.12f, 0.45f, 1f, 1f),
            HitData.DamageModifier.VeryResistant => new Color(0.24f, 0.2f, 0.95f, 1f),
            HitData.DamageModifier.Immune => Color.white,
            _ => Color.white
        };
    }

    private static string GetStatusEffectName(string damageTypeName, string damageModifierName) =>
        $"{NamePrefix}{damageTypeName}_{damageModifierName}";

    private static string FormatStatusDisplayName(string statusName) =>
        statusName.Replace('_', ' ');
}

internal sealed class SE_AdditiveDamageModifier : SE_Stats
{
    public HitData.DamageModifier m_modifier;
    public string m_damageTypeName = "";
    public string m_modifierName = "";

    public override string GetIconText() => AdditiveDamageDisplay.FormatModifierPercent(m_modifier);

    public string GetHudName() => $"{m_damageTypeName}\n{m_modifierName}";
}

[HarmonyPatch(typeof(Hud), "UpdateStatusEffects")]
internal static class HudUpdateStatusEffectsPatch
{
    private const float HudNameVerticalOffset = 12f;

    private static void Postfix(List<StatusEffect> statusEffects, List<RectTransform> ___m_statusEffects)
    {
        if (statusEffects == null || ___m_statusEffects == null)
        {
            return;
        }

        int count = Mathf.Min(statusEffects.Count, ___m_statusEffects.Count);
        for (int i = 0; i < count; i++)
        {
            TMP_Text? nameText = GetNameText(___m_statusEffects[i]);
            if (nameText == null)
            {
                continue;
            }

            RectTransform? nameTransform = nameText.transform as RectTransform;
            if (nameTransform == null)
            {
                continue;
            }

            HudStatusEffectNamePosition position = nameText.GetComponent<HudStatusEffectNamePosition>()
                                                   ?? nameText.gameObject.AddComponent<HudStatusEffectNamePosition>();
            if (statusEffects[i] is SE_AdditiveDamageModifier statusEffect)
            {
                nameText.text = statusEffect.GetHudName();
                nameTransform.anchoredPosition = position.OriginalPosition + new Vector2(0f, HudNameVerticalOffset);
            }
            else
            {
                nameTransform.anchoredPosition = position.OriginalPosition;
            }
        }
    }

    private static TMP_Text? GetNameText(RectTransform statusEffectRoot)
    {
        foreach (TMP_Text text in statusEffectRoot.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.gameObject.name != "TimeText")
            {
                return text;
            }
        }

        return null;
    }
}

internal sealed class HudStatusEffectNamePosition : MonoBehaviour
{
    public Vector2 OriginalPosition { get; private set; }

    private void Awake()
    {
        if (transform is RectTransform rectTransform)
        {
            OriginalPosition = rectTransform.anchoredPosition;
        }
    }
}

internal static class AdditiveDamageDisplay
{
    public static string FormatModifierPercent(HitData.DamageModifier modifier)
    {
        return FormatPercent(AdditiveDamageMath.ModifierToDelta(modifier) * 100f);
    }

    public static string FormatMinimumTotalPercent(HitData.DamageType damageType)
    {
        float minimumTotal = AdditiveDamageModifierPlugin.GetMinimumDamageTakenMultiplier(damageType) - 1f;
        return FormatPercent(minimumTotal * 100f);
    }

    public static string GetModifierTooltipSuffix(HitData.DamageType damageType, HitData.DamageModifier modifier, bool includeMinimumTotal)
    {
        string modifierPercent = FormatModifierPercent(modifier);
        if (!includeMinimumTotal)
        {
            return AdditiveDamageModifierPlugin.ShowModifierPercentInTooltipsOutsideCompendium()
                ? $" ({modifierPercent})"
                : "";
        }

        return $" ({modifierPercent} / MinTotal {FormatMinimumTotalPercent(damageType)})";
    }

    internal static string FormatPercent(float value)
    {
        int roundedValue = Mathf.RoundToInt(value);
        return $"{roundedValue.ToString("+0;-0;0", CultureInfo.InvariantCulture)}%";
    }
}

[HarmonyPatch(typeof(SE_Stats), nameof(SE_Stats.GetDamageModifiersTooltipString))]
internal static class SEStatsDamageModifiersTooltipPatch
{
    private static bool Prefix(List<HitData.DamageModPair> mods, ref string __result)
    {
        if (!AdditiveDamageTooltipBuilder.TryBuildDamageModifiersTooltipString(
                mods,
                AdditiveDamageTooltipContext.IncludeMinimumTotal,
                out string tooltip))
        {
            return true;
        }

        __result = tooltip;
        return false;
    }
}

[HarmonyPatch(typeof(TextsDialog), "AddActiveEffects")]
internal static class TextsDialogAddActiveEffectsPatch
{
    private static void Prefix()
    {
        AdditiveDamageTooltipContext.PushActiveEffectsCompendium();
    }

    private static Exception? Finalizer(Exception? __exception)
    {
        AdditiveDamageTooltipContext.PopActiveEffectsCompendium();
        return __exception;
    }
}

internal static class AdditiveDamageTooltipContext
{
    [System.ThreadStatic] private static int _activeEffectsCompendiumDepth;

    public static bool IncludeMinimumTotal => _activeEffectsCompendiumDepth > 0;

    public static void PushActiveEffectsCompendium()
    {
        _activeEffectsCompendiumDepth++;
    }

    public static void PopActiveEffectsCompendium()
    {
        if (_activeEffectsCompendiumDepth > 0)
        {
            _activeEffectsCompendiumDepth--;
        }
    }
}

internal static class AdditiveDamageTooltipBuilder
{
    public static bool TryBuildDamageModifiersTooltipString(
        List<HitData.DamageModPair> mods,
        bool includeMinimumTotal,
        out string tooltip)
    {
        if (mods.Count == 0)
        {
            tooltip = "";
            return true;
        }

        string text = "";
        foreach (HitData.DamageModPair mod in mods)
        {
            if (mod.m_modifier == HitData.DamageModifier.Ignore || mod.m_modifier == HitData.DamageModifier.Normal)
            {
                continue;
            }

            if (!AdditiveDamageDefinitions.TryGetDamageModifier(
                    mod.m_modifier,
                    out DamageModifierDefinition modifierDefinition)
                || !AdditiveDamageDefinitions.TryGetDamageType(
                    mod.m_type,
                    out DamageTypeDefinition damageTypeDefinition))
            {
                tooltip = "";
                return false;
            }

            if (string.IsNullOrEmpty(modifierDefinition.LocalizationKey)
                || string.IsNullOrEmpty(damageTypeDefinition.LocalizationKey))
            {
                continue;
            }

            text += "\n$inventory_dmgmod: ";
            text += $"<color=orange>{modifierDefinition.LocalizationKey}</color> VS ";
            text += $"<color=orange>{damageTypeDefinition.LocalizationKey}</color>";
            text += AdditiveDamageDisplay.GetModifierTooltipSuffix(mod.m_type, mod.m_modifier, includeMinimumTotal);
        }

        tooltip = text;
        return true;
    }
}

[HarmonyPatch(typeof(ObjectDB), "Awake")]
internal static class ObjectDbAwakeStatusEffectPatch
{
    private static void Postfix(ObjectDB __instance)
    {
        AdditiveDamageStatusEffectCatalog.Register(__instance);
    }
}

[HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
internal static class ObjectDbCopyOtherDbStatusEffectPatch
{
    private static void Postfix(ObjectDB __instance)
    {
        AdditiveDamageStatusEffectCatalog.Register(__instance);
    }
}
