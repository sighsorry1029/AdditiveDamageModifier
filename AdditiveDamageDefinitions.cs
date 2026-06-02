using System.Collections.Generic;

namespace AdditiveDamageModifier;

internal static class AdditiveDamageDefinitions
{
    public static readonly DamageTypeDefinition[] DamageTypes =
    {
        new(HitData.DamageType.Blunt, "Blunt", "blunt", "$inventory_blunt", hasStatusEffect: true, hasPlayerMinimumCap: true),
        new(HitData.DamageType.Pierce, "Pierce", "pierce", "$inventory_pierce", hasStatusEffect: true, hasPlayerMinimumCap: true),
        new(HitData.DamageType.Slash, "Slash", "slash", "$inventory_slash", hasStatusEffect: true, hasPlayerMinimumCap: true),
        new(HitData.DamageType.Chop, "Chop", "chop", "$inventory_chop", hasStatusEffect: false, hasPlayerMinimumCap: false),
        new(HitData.DamageType.Pickaxe, "Pickaxe", "pickaxe", "$inventory_pickaxe", hasStatusEffect: false, hasPlayerMinimumCap: false),
        new(HitData.DamageType.Fire, "Fire", "fire", "$inventory_fire", hasStatusEffect: true, hasPlayerMinimumCap: true),
        new(HitData.DamageType.Poison, "Poison", "poison", "$inventory_poison", hasStatusEffect: true, hasPlayerMinimumCap: true),
        new(HitData.DamageType.Frost, "Frost", "frost", "$inventory_frost", hasStatusEffect: true, hasPlayerMinimumCap: true),
        new(HitData.DamageType.Lightning, "Lightning", "lightning", "$inventory_lightning", hasStatusEffect: true, hasPlayerMinimumCap: true),
        // Players are immune to Spirit in vanilla, so Spirit intentionally has no player minimum cap.
        new(HitData.DamageType.Spirit, "Spirit", "spirit", "$inventory_spirit", hasStatusEffect: true, hasPlayerMinimumCap: false)
    };

    public static readonly DamageTypeDefinition[] StatusEffectDamageTypes = GetStatusEffectDamageTypes();
    public static readonly DamageTypeDefinition[] PlayerMinimumCapDamageTypes = GetPlayerMinimumCapDamageTypes();

    public static readonly DamageModifierDefinition[] DamageModifiers =
    {
        new(HitData.DamageModifier.VeryWeak, "Very Weak", "very_weak", "$inventory_veryweak", 45, "Very Weak modifier value. 45 means +45% damage taken.", 800),
        new(HitData.DamageModifier.Weak, "Weak", "weak", "$inventory_weak", 30, "Weak modifier value. 30 means +30% damage taken.", 700),
        new(HitData.DamageModifier.SlightlyWeak, "Slightly Weak", "slightly_weak", "$inventory_slightlyweak", 15, "Slightly Weak modifier value. 15 means +15% damage taken.", 600),
        new(HitData.DamageModifier.SlightlyResistant, "Slightly Resistant", "slightly_resistant", "$inventory_slightlyresistant", -15, "Slightly Resistant modifier value. -15 means -15% damage taken.", 400),
        new(HitData.DamageModifier.Resistant, "Resistant", "resistant", "$inventory_resistant", -30, "Resistant modifier value. -30 means -30% damage taken.", 300),
        new(HitData.DamageModifier.VeryResistant, "Very Resistant", "very_resistant", "$inventory_veryresistant", -45, "Very Resistant modifier value. -45 means -45% damage taken.", 200),
        new(HitData.DamageModifier.Immune, "Immune", "immune", "$inventory_immune")
    };

    public static readonly DamageModifierDefinition[] DamageModifierConfigs = GetConfigurableDamageModifiers();
    public static readonly DamageModifierDefinition[] StatusEffectDamageModifiers = DamageModifiers;

    public static bool TryGetDamageType(HitData.DamageType type, out DamageTypeDefinition definition)
    {
        foreach (DamageTypeDefinition candidate in DamageTypes)
        {
            if (candidate.Type == type)
            {
                definition = candidate;
                return true;
            }
        }

        definition = default;
        return false;
    }

    public static bool TryGetDamageModifier(HitData.DamageModifier modifier, out DamageModifierDefinition definition)
    {
        foreach (DamageModifierDefinition candidate in DamageModifiers)
        {
            if (candidate.Modifier == modifier)
            {
                definition = candidate;
                return true;
            }
        }

        definition = default;
        return false;
    }

    public static bool UsesPlayerMinimumCap(HitData.DamageType damageType)
    {
        return TryGetDamageType(damageType, out DamageTypeDefinition definition)
               && definition.HasPlayerMinimumCap;
    }

    private static DamageTypeDefinition[] GetStatusEffectDamageTypes()
    {
        List<DamageTypeDefinition> result = new();
        foreach (DamageTypeDefinition definition in DamageTypes)
        {
            if (definition.HasStatusEffect)
            {
                result.Add(definition);
            }
        }

        return result.ToArray();
    }

    private static DamageTypeDefinition[] GetPlayerMinimumCapDamageTypes()
    {
        List<DamageTypeDefinition> result = new();
        foreach (DamageTypeDefinition definition in DamageTypes)
        {
            if (definition.HasPlayerMinimumCap)
            {
                result.Add(definition);
            }
        }

        return result.ToArray();
    }

    private static DamageModifierDefinition[] GetConfigurableDamageModifiers()
    {
        List<DamageModifierDefinition> result = new();
        foreach (DamageModifierDefinition definition in DamageModifiers)
        {
            if (definition.HasConfig)
            {
                result.Add(definition);
            }
        }

        return result.ToArray();
    }
}

internal readonly struct DamageTypeDefinition
{
    public DamageTypeDefinition(
        HitData.DamageType type,
        string displayName,
        string statusName,
        string localizationKey,
        bool hasStatusEffect,
        bool hasPlayerMinimumCap)
    {
        Type = type;
        DisplayName = displayName;
        StatusName = statusName;
        LocalizationKey = localizationKey;
        HasStatusEffect = hasStatusEffect;
        HasPlayerMinimumCap = hasPlayerMinimumCap;
    }

    public HitData.DamageType Type { get; }
    public string DisplayName { get; }
    public string StatusName { get; }
    public string LocalizationKey { get; }
    public bool HasStatusEffect { get; }
    public bool HasPlayerMinimumCap { get; }
}

internal readonly struct DamageModifierDefinition
{
    public DamageModifierDefinition(
        HitData.DamageModifier modifier,
        string displayName,
        string statusName,
        string localizationKey,
        int defaultPercent,
        string description,
        int order)
    {
        Modifier = modifier;
        DisplayName = displayName;
        StatusName = statusName;
        LocalizationKey = localizationKey;
        DefaultPercent = defaultPercent;
        Description = description;
        Order = order;
        HasConfig = true;
    }

    public DamageModifierDefinition(
        HitData.DamageModifier modifier,
        string displayName,
        string statusName,
        string localizationKey)
    {
        Modifier = modifier;
        DisplayName = displayName;
        StatusName = statusName;
        LocalizationKey = localizationKey;
        DefaultPercent = 0;
        Description = "";
        Order = 0;
        HasConfig = false;
    }

    public HitData.DamageModifier Modifier { get; }
    public string DisplayName { get; }
    public string StatusName { get; }
    public string LocalizationKey { get; }
    public int DefaultPercent { get; }
    public string Description { get; }
    public int Order { get; }
    public bool HasConfig { get; }
}
