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

    public static readonly DamageModifierDefinition[] DamageModifiers =
    {
        new(HitData.DamageModifier.VeryWeak, "Very Weak", "very_weak", "$inventory_veryweak", 45, 800),
        new(HitData.DamageModifier.Weak, "Weak", "weak", "$inventory_weak", 30, 700),
        new(HitData.DamageModifier.SlightlyWeak, "Slightly Weak", "slightly_weak", "$inventory_slightlyweak", 15, 600),
        new(HitData.DamageModifier.SlightlyResistant, "Slightly Resistant", "slightly_resistant", "$inventory_slightlyresistant", -15, 400),
        new(HitData.DamageModifier.Resistant, "Resistant", "resistant", "$inventory_resistant", -30, 300),
        new(HitData.DamageModifier.VeryResistant, "Very Resistant", "very_resistant", "$inventory_veryresistant", -45, 200),
        new(HitData.DamageModifier.Immune, "Immune", "immune", "$inventory_immune")
    };

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
        int order)
    {
        Modifier = modifier;
        DisplayName = displayName;
        StatusName = statusName;
        LocalizationKey = localizationKey;
        DefaultPercent = defaultPercent;
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
        Order = 0;
        HasConfig = false;
    }

    public HitData.DamageModifier Modifier { get; }
    public string DisplayName { get; }
    public string StatusName { get; }
    public string LocalizationKey { get; }
    public int DefaultPercent { get; }
    public int Order { get; }
    public bool HasConfig { get; }
}
