using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using LocalizationManager;
using ServerSync;
using UnityEngine;

namespace AdditiveDamageModifier;

[BepInPlugin(ModGUID, ModName, ModVersion)]
public class AdditiveDamageModifierPlugin : BaseUnityPlugin
{
    internal const string ModName = "AdditiveDamageModifier";
    internal const string ModVersion = "1.2.1";
    internal const string Author = "sighsorry";
    private const string ModGUID = $"{Author}.{ModName}";
    private readonly Harmony _harmony = new(ModGUID);
    public static readonly ManualLogSource AdditiveDamageModifierLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);
    private static readonly ConfigSync ConfigSync = new(ModGUID) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };
    private static readonly Dictionary<HitData.DamageModifier, ConfigEntry<int>> ModifierPercentConfigs = new();
    private static readonly Dictionary<HitData.DamageType, ConfigEntry<int>> PlayerMinimumDamageCapConfigs = new();

    public enum Toggle
    {
        On = 1,
        Off = 0
    }

    public void Awake()
    {
        Localizer.Load(this);

        bool saveOnSet = Config.SaveOnConfigSet;
        Config.SaveOnConfigSet = false;
        try
        {
            BindConfigEntries();
            _harmony.PatchAll(typeof(AdditiveDamageModifierPlugin).Assembly);
            Config.Save();
        }
        finally
        {
            Config.SaveOnConfigSet = saveOnSet;
        }
    }

    #region ConfigOptions

    private static ConfigEntry<Toggle> _serverConfigLocked = null!;
    private static ConfigEntry<Toggle> _showModifierPercentInTooltipsOutsideCompendium = null!;
    private static ConfigEntry<int> _fallDamageCap = null!;
    private static ConfigEntry<float> _fallDamageMultiplier = null!;
    private static ConfigEntry<int> _frostEnvImmunityTriggerFrostDeltaPercent = null!;

    private void BindConfigEntries()
    {
        ModifierPercentConfigs.Clear();
        PlayerMinimumDamageCapConfigs.Clear();

        _serverConfigLocked = config(
            "1 - General",
            "Lock Configuration",
            Toggle.On,
            new ConfigDescription("If on, the configuration is locked and can be changed by server admins only."));
        _ = ConfigSync.AddLockingConfigEntry(_serverConfigLocked);
        _showModifierPercentInTooltipsOutsideCompendium = config(
            "1 - General",
            "Show Modifier Percent in Tooltips Outside Compendium",
            Toggle.On,
            new ConfigDescription(
                "If on, item and other non-compendium damage modifier tooltip lines include the configured source percent, like (-30%). Applied passive armor and active status effects also show the current player Net. The Active effects compendium always shows percent, Net for active effects, and MinTotal when applicable.",
                null,
                new ConfigurationManagerAttributes { Order = 900 }),
            synchronizedSetting: false);

        foreach (DamageModifierDefinition definition in AdditiveDamageDefinitions.DamageModifiers)
        {
            if (!definition.HasConfig)
            {
                continue;
            }

            ModifierPercentConfigs[definition.Modifier] = additivePercentConfig(definition);
        }

        int capOrder = 190;
        foreach (DamageTypeDefinition definition in AdditiveDamageDefinitions.DamageTypes)
        {
            if (!definition.HasPlayerMinimumCap)
            {
                continue;
            }

            ConfigEntry<int> legacyConfig = Config.Bind(
                "2 - Additive Damage",
                $"Minimum Damage Taken Cap Percent on Player - {definition.DisplayName}",
                10,
                new ConfigDescription(
                    "Legacy player minimum damage setting used for migration.",
                    new AcceptableValueRange<int>(0, 50)));
            int migratedDefaultValue = legacyConfig.Value;
            Config.Remove(legacyConfig.Definition);

            PlayerMinimumDamageCapConfigs[definition.Type] = playerMinimumDamageCapConfig(
                $"Player Minimum Damage Taken Percent - {definition.DisplayName}",
                migratedDefaultValue,
                capOrder);
            capOrder -= 10;
        }

        _fallDamageCap = config(
            "3 - Fall Damage",
            "Maximum Fall Damage",
            100,
            new ConfigDescription(
                "Maximum fall damage before status effects. Vanilla is 100 damage at 20m. Example: 200 with multiplier 1.00 reaches 200 damage at 36m; 200 with multiplier 2.00 reaches 200 damage at 20m.",
                new AcceptableValueRange<int>(100, 500),
                configAttributes(120)));
        _fallDamageMultiplier = config(
            "3 - Fall Damage",
            "Fall Damage Multiplier",
            1f,
            new ConfigDescription(
                "Controls how fast fall damage grows. 1.00 is vanilla speed. 2.00 doubles growth speed: 100 damage at 12m, and 200 damage at 20m if Maximum Fall Damage is 200. Values are rounded to 2 decimal places.",
                new AcceptableValueRange<float>(1f, 2f),
                configAttributes(110)));
        RoundFallDamageMultiplierConfig();
        _fallDamageMultiplier.SettingChanged += (_, _) => RoundFallDamageMultiplierConfig();
        _frostEnvImmunityTriggerFrostDeltaPercent = config(
            "2 - Additive Damage",
            "Cold/Freezing Immunity Trigger Frost Delta Percent",
            -15,
            new ConfigDescription(
                "Shared trigger threshold for Cold and Freezing immunity in Player.UpdateEnvStatusEffects. If effective additive frost delta is <= this value, both Cold and Freezing are blocked/cleared by vanilla flow. -15 means -15%.",
                new AcceptableValueRange<int>(-100, 0),
                configAttributes(30)));
    }

    internal static float GetConfiguredDelta(HitData.DamageModifier modifier)
    {
        if (modifier == HitData.DamageModifier.Immune)
        {
            return -1f;
        }

        return ModifierPercentConfigs.TryGetValue(modifier, out ConfigEntry<int> configEntry)
            ? configEntry.Value / 100f
            : 0f;
    }

    internal static float GetMinimumDamageTakenMultiplier(HitData.DamageType damageType)
    {
        return PlayerMinimumDamageCapConfigs.TryGetValue(damageType, out ConfigEntry<int> configEntry)
            ? Mathf.Clamp(configEntry.Value / 100f, 0f, 0.5f)
            : 0f;
    }

    internal static float GetFrostEnvImmunityTriggerDelta()
    {
        return Mathf.Clamp(_frostEnvImmunityTriggerFrostDeltaPercent.Value / 100f, -1f, 0f);
    }

    internal static float GetFallDamageCap()
    {
        return Mathf.Clamp(_fallDamageCap.Value, 100, 500);
    }

    internal static float GetFallDamageMultiplier()
    {
        return RoundFallDamageMultiplier(_fallDamageMultiplier.Value);
    }

    internal static bool ShowModifierPercentInTooltipsOutsideCompendium()
    {
        return _showModifierPercentInTooltipsOutsideCompendium.Value == Toggle.On;
    }

    private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
    {
        ConfigDescription extendedDescription = new(description.Description + (synchronizedSetting ? " [Synced with Server]" : " [Not Synced with Server]"), description.AcceptableValues, description.Tags);
        ConfigEntry<T> configEntry = Config.Bind(group, name, value, extendedDescription);

        SyncedConfigEntry<T> syncedConfigEntry = ConfigSync.AddConfigEntry(configEntry);
        syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

        return configEntry;
    }

    private ConfigEntry<int> additivePercentConfig(DamageModifierDefinition definition)
    {
        string defaultPercent = definition.DefaultPercent.ToString(CultureInfo.InvariantCulture);
        string signedDefaultPercent = definition.DefaultPercent.ToString("+0;-0;0", CultureInfo.InvariantCulture);
        return config(
            "2 - Additive Damage",
            $"{definition.DisplayName} Percent",
            definition.DefaultPercent,
            new ConfigDescription(
                $"{definition.DisplayName} modifier value. {defaultPercent} means {signedDefaultPercent}% damage taken.",
                new AcceptableValueRange<int>(-100, 100),
                configAttributes(definition.Order)));
    }

    private ConfigEntry<int> playerMinimumDamageCapConfig(string name, int value, int order)
    {
        return config(
            "2 - Additive Damage",
            name,
            value,
            new ConfigDescription(
                "Minimum final damage percent the player can take after additive modifiers for this damage type. 10 means damage cannot be reduced below 10% of the original damage, shown as MinTotal -90% in the Active effects compendium. Immune still respects this cap.",
                new AcceptableValueRange<int>(0, 50),
                configAttributes(order)));
    }

    private static ConfigurationManagerAttributes configAttributes(int order)
    {
        return new ConfigurationManagerAttributes
        {
            Order = order
        };
    }

    private static void RoundFallDamageMultiplierConfig()
    {
        float roundedValue = RoundFallDamageMultiplier(_fallDamageMultiplier.Value);
        if (!Mathf.Approximately(_fallDamageMultiplier.Value, roundedValue))
        {
            _fallDamageMultiplier.Value = roundedValue;
        }
    }

    private static float RoundFallDamageMultiplier(float value)
    {
        return Mathf.Clamp(Mathf.Round(value * 100f) / 100f, 1f, 2f);
    }

    private sealed class ConfigurationManagerAttributes
    {
        public int? Order;
    }

    #endregion
}
