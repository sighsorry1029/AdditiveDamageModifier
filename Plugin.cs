using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Timers;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using JetBrains.Annotations;
using ServerSync;
using UnityEngine;

namespace AdditiveDamageModifier;

[BepInPlugin(ModGUID, ModName, ModVersion)]
public class AdditiveDamageModifierPlugin : BaseUnityPlugin
{
    internal const string ModName = "AdditiveDamageModifier";
    internal const string ModVersion = "1.0.4";
    internal const string Author = "sighsorry";
    private const string ModGUID = $"{Author}.{ModName}";
    private static string ConfigFileName = $"{ModGUID}.cfg";
    private static string ConfigFileFullPath = Paths.ConfigPath + Path.DirectorySeparatorChar + ConfigFileName;
    internal static string ConnectionError = "";
    private readonly Harmony _harmony = new(ModGUID);
    public static readonly ManualLogSource AdditiveDamageModifierLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);
    private static readonly ConfigSync ConfigSync = new(ModGUID) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };
    private FileSystemWatcher? _watcher;
    private readonly object _reloadLock = new();
    private DateTime _lastConfigReloadTime;
    private const long RELOAD_DELAY = 10000000; // One second

    public enum Toggle
    {
        On = 1,
        Off = 0
    }

    public void Awake()
    {
        bool saveOnSet = Config.SaveOnConfigSet;
        Config.SaveOnConfigSet = false;

        // Uncomment the line below to use the LocalizationManager for localizing your mod.
        // Make sure to populate the English.yml file in the translation folder with your keys to be localized and the values associated before uncommenting!.
        //Localizer.Load(); // Use this to initialize the LocalizationManager (for more information on LocalizationManager, see the LocalizationManager documentation https://github.com/blaxxun-boop/LocalizationManager#example-project).

        _serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On, "If on, the configuration is locked and can be changed by server admins only.");
        _ = ConfigSync.AddLockingConfigEntry(_serverConfigLocked);

        _veryWeakPercent = additivePercentConfig("Very Weak Percent", 45f, "Very Weak modifier value. 45 means +45% damage taken.", 800);
        _weakPercent = additivePercentConfig("Weak Percent", 30f, "Weak modifier value. 30 means +30% damage taken.", 700);
        _slightlyWeakPercent = additivePercentConfig("Slightly Weak Percent", 15f, "Slightly Weak modifier value. 15 means +15% damage taken.", 600);
        _slightlyResistantPercent = additivePercentConfig("Slightly Resistant Percent", -15f, "Slightly Resistant modifier value. -15 means -15% damage taken.", 400);
        _resistantPercent = additivePercentConfig("Resistant Percent", -30f, "Resistant modifier value. -30 means -30% damage taken.", 300);
        _veryResistantPercent = additivePercentConfig("Very Resistant Percent", -45f, "Very Resistant modifier value. -45 means -45% damage taken.", 200);
        _minimumDamageTakenCapPercentBlunt = playerMinimumDamageCapConfig("Minimum Damage Taken Cap Percent on Player - Blunt", 10f, 190);
        _minimumDamageTakenCapPercentPierce = playerMinimumDamageCapConfig("Minimum Damage Taken Cap Percent on Player - Pierce", 10f, 180);
        _minimumDamageTakenCapPercentSlash = playerMinimumDamageCapConfig("Minimum Damage Taken Cap Percent on Player - Slash", 10f, 170);
        _minimumDamageTakenCapPercentFire = playerMinimumDamageCapConfig("Minimum Damage Taken Cap Percent on Player - Fire", 10f, 160);
        _minimumDamageTakenCapPercentPoison = playerMinimumDamageCapConfig("Minimum Damage Taken Cap Percent on Player - Poison", 10f, 150);
        _minimumDamageTakenCapPercentFrost = playerMinimumDamageCapConfig("Minimum Damage Taken Cap Percent on Player - Frost", 10f, 140);
        _minimumDamageTakenCapPercentLightning = playerMinimumDamageCapConfig("Minimum Damage Taken Cap Percent on Player - Lightning", 10f, 130);
        _frostEnvImmunityTriggerFrostDeltaPercent = config(
            "2 - Additive Damage",
            "Cold/Freezing Immunity Trigger Frost Delta Percent",
            -15f,
            new ConfigDescription(
                "Shared trigger threshold for Cold and Freezing immunity in Player.UpdateEnvStatusEffects. If effective additive frost delta is <= this value, both Cold and Freezing are blocked/cleared by vanilla flow. -15 means -15%.",
                new AcceptableValueRange<float>(-100f, 0f),
                new ConfigurationManagerAttributes { Order = 30 }));


        Assembly assembly = Assembly.GetExecutingAssembly();
        _harmony.PatchAll(assembly);
        SetupWatcher();

        Config.Save();
        if (saveOnSet)
        {
            Config.SaveOnConfigSet = saveOnSet;
        }
    }

    private void OnDestroy()
    {
        SaveWithRespectToConfigSet();
        _watcher?.Dispose();
    }

    private void SetupWatcher()
    {
        _watcher = new FileSystemWatcher(Paths.ConfigPath, ConfigFileName);
        _watcher.Changed += ReadConfigValues;
        _watcher.Created += ReadConfigValues;
        _watcher.Renamed += ReadConfigValues;
        _watcher.IncludeSubdirectories = true;
        _watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
        _watcher.EnableRaisingEvents = true;
    }

    private void ReadConfigValues(object sender, FileSystemEventArgs e)
    {
        DateTime now = DateTime.Now;
        long time = now.Ticks - _lastConfigReloadTime.Ticks;
        if (time < RELOAD_DELAY)
        {
            return;
        }

        lock (_reloadLock)
        {
            if (!File.Exists(ConfigFileFullPath))
            {
                AdditiveDamageModifierLogger.LogWarning("Config file does not exist. Skipping reload.");
                return;
            }

            try
            {
                AdditiveDamageModifierLogger.LogDebug("Reloading configuration...");
                SaveWithRespectToConfigSet(true);
                AdditiveDamageModifierLogger.LogInfo("Configuration reload complete.");
            }
            catch (Exception ex)
            {
                AdditiveDamageModifierLogger.LogError($"Error reloading configuration: {ex.Message}");
            }
        }

        _lastConfigReloadTime = now;
    }

    private void SaveWithRespectToConfigSet(bool reload = false)
    {
        bool originalSaveOnSet = Config.SaveOnConfigSet;
        Config.SaveOnConfigSet = false;
        if (reload)
            Config.Reload();
        Config.Save();
        if (originalSaveOnSet)
        {
            Config.SaveOnConfigSet = originalSaveOnSet;
        }
        
        // If you want to do something once localization completes, LocalizationManager has a hook for that.
        /*Localizer.OnLocalizationComplete += () =>
        {
            // Do something
            ItemManagerModTemplateLogger.LogDebug("OnLocalizationComplete called");
        };*/
    }


    #region ConfigOptions

    private static ConfigEntry<Toggle> _serverConfigLocked = null!;
    private static ConfigEntry<float> _veryWeakPercent = null!;
    private static ConfigEntry<float> _weakPercent = null!;
    private static ConfigEntry<float> _slightlyWeakPercent = null!;
    private static ConfigEntry<float> _slightlyResistantPercent = null!;
    private static ConfigEntry<float> _resistantPercent = null!;
    private static ConfigEntry<float> _veryResistantPercent = null!;
    private static ConfigEntry<float> _minimumDamageTakenCapPercentBlunt = null!;
    private static ConfigEntry<float> _minimumDamageTakenCapPercentPierce = null!;
    private static ConfigEntry<float> _minimumDamageTakenCapPercentSlash = null!;
    private static ConfigEntry<float> _minimumDamageTakenCapPercentFire = null!;
    private static ConfigEntry<float> _minimumDamageTakenCapPercentPoison = null!;
    private static ConfigEntry<float> _minimumDamageTakenCapPercentFrost = null!;
    private static ConfigEntry<float> _minimumDamageTakenCapPercentLightning = null!;
    private static ConfigEntry<float> _frostEnvImmunityTriggerFrostDeltaPercent = null!;

    internal static float GetConfiguredDelta(HitData.DamageModifier modifier)
    {
        return modifier switch
        {
            HitData.DamageModifier.VeryWeak => _veryWeakPercent.Value / 100f,
            HitData.DamageModifier.Weak => _weakPercent.Value / 100f,
            HitData.DamageModifier.SlightlyWeak => _slightlyWeakPercent.Value / 100f,
            HitData.DamageModifier.Normal => 0f,
            HitData.DamageModifier.SlightlyResistant => _slightlyResistantPercent.Value / 100f,
            HitData.DamageModifier.Resistant => _resistantPercent.Value / 100f,
            HitData.DamageModifier.VeryResistant => _veryResistantPercent.Value / 100f,
            HitData.DamageModifier.Immune => -1f,
            _ => 0f
        };
    }

    internal static float GetMinimumDamageTakenMultiplier(HitData.DamageType damageType)
    {
        float capPercent = damageType switch
        {
            HitData.DamageType.Blunt => _minimumDamageTakenCapPercentBlunt.Value,
            HitData.DamageType.Pierce => _minimumDamageTakenCapPercentPierce.Value,
            HitData.DamageType.Slash => _minimumDamageTakenCapPercentSlash.Value,
            HitData.DamageType.Fire => _minimumDamageTakenCapPercentFire.Value,
            HitData.DamageType.Poison => _minimumDamageTakenCapPercentPoison.Value,
            HitData.DamageType.Frost => _minimumDamageTakenCapPercentFrost.Value,
            HitData.DamageType.Lightning => _minimumDamageTakenCapPercentLightning.Value,
            HitData.DamageType.Spirit => 0f,
            HitData.DamageType.Chop => 0f,
            HitData.DamageType.Pickaxe => 0f,
            _ => 0f
        };

        return Mathf.Clamp(capPercent / 100f, 0f, 0.5f);
    }

    internal static float GetFrostEnvImmunityTriggerDelta()
    {
        return Mathf.Clamp(_frostEnvImmunityTriggerFrostDeltaPercent.Value / 100f, -1f, 0f);
    }

    private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
    {
        ConfigDescription extendedDescription = new(description.Description + (synchronizedSetting ? " [Synced with Server]" : " [Not Synced with Server]"), description.AcceptableValues, description.Tags);
        ConfigEntry<T> configEntry = Config.Bind(group, name, value, extendedDescription);
        //var configEntry = Config.Bind(group, name, value, description);

        SyncedConfigEntry<T> syncedConfigEntry = ConfigSync.AddConfigEntry(configEntry);
        syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

        return configEntry;
    }

    private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true)
    {
        return config(group, name, value, new ConfigDescription(description), synchronizedSetting);
    }

    private ConfigEntry<float> additivePercentConfig(string name, float value, string description, int order)
    {
        return config(
            "2 - Additive Damage",
            name,
            value,
            new ConfigDescription(
                description,
                new AcceptableValueRange<float>(-100f, 100f),
                new ConfigurationManagerAttributes { Order = order }));
    }

    private ConfigEntry<float> playerMinimumDamageCapConfig(string name, float value, int order)
    {
        return config(
            "2 - Additive Damage",
            name,
            value,
            new ConfigDescription(
                "Lower bound for final damage taken on Player after additive sum for this damage type. 0 means can go down to 0%, 50 means cannot go below 50%.",
                new AcceptableValueRange<float>(0f, 50f),
                new ConfigurationManagerAttributes { Order = order }));
    }

    private class ConfigurationManagerAttributes
    {
        [UsedImplicitly] public int? Order = null!;
        [UsedImplicitly] public bool? Browsable = null!;
        [UsedImplicitly] public string? Category = null!;
        [UsedImplicitly] public Action<ConfigEntryBase>? CustomDrawer = null!;
    }

    class AcceptableShortcuts() : AcceptableValueBase(typeof(KeyboardShortcut))
    {
        public override object Clamp(object value) => value;
        public override bool IsValid(object value) => true;

        public override string ToDescriptionString() => $"# Acceptable values: {string.Join(", ", UnityInput.Current.SupportedKeyCodes)}";
    }

    #endregion
}

public static class KeyboardExtensions
{
    extension(KeyboardShortcut shortcut)
    {
        public bool IsKeyDown()
        {
            return shortcut.MainKey != KeyCode.None && Input.GetKeyDown(shortcut.MainKey) && shortcut.Modifiers.All(Input.GetKey);
        }

        public bool IsKeyHeld()
        {
            return shortcut.MainKey != KeyCode.None && Input.GetKey(shortcut.MainKey) && shortcut.Modifiers.All(Input.GetKey);
        }
    }
}

public static class ToggleExtentions
{
    extension(AdditiveDamageModifierPlugin.Toggle value)
    {
        public bool IsOn()
        {
            return value == AdditiveDamageModifierPlugin.Toggle.On;
        }

        public bool IsOff()
        {
            return value == AdditiveDamageModifierPlugin.Toggle.Off;
        }
    }
}
