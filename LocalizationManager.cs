// Adapted from Azumatt's LocalizationManager 1.4.0 (MIT-0).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using HarmonyLib;
using YamlDotNet.Serialization;

namespace LocalizationManager;

internal static class Localizer
{
    private static readonly string[] FileExtensions = { ".json", ".yml" };
    private static readonly MethodInfo? AddWordMethod = AccessTools.DeclaredMethod(
        typeof(Localization),
        "AddWord",
        new[] { typeof(string), typeof(string) });
    private static BaseUnityPlugin? _plugin;
    private static BepInEx.Logging.ManualLogSource Logger =>
        AdditiveDamageModifier.AdditiveDamageModifierPlugin.AdditiveDamageModifierLogger;

    internal static void Load(BaseUnityPlugin plugin)
    {
        _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        Localization localization = Localization.instance;
        LoadLocalization(localization, localization.GetSelectedLanguage());
    }

    internal static void LoadLocalization(Localization localization, string language)
    {
        if (_plugin == null || localization == null)
        {
            return;
        }

        string selectedLanguage = string.IsNullOrWhiteSpace(language) ? "English" : language;
        Dictionary<string, string> externalFiles = FindExternalLocalizationFiles();
        Dictionary<string, string> texts = LoadEmbeddedTranslation("English", required: true)!;

        if (externalFiles.TryGetValue("English", out string englishFile))
        {
            TryMergeExternalTranslation(texts, englishFile, "English");
        }

        if (!string.Equals(selectedLanguage, "English", StringComparison.OrdinalIgnoreCase))
        {
            bool loadedExternal = externalFiles.TryGetValue(selectedLanguage, out string selectedFile)
                                  && TryMergeExternalTranslation(texts, selectedFile, selectedLanguage);
            if (!loadedExternal && LoadEmbeddedTranslation(selectedLanguage, required: false) is { } embeddedTexts)
            {
                Merge(texts, embeddedTexts);
            }
        }

        if (AddWordMethod == null)
        {
            throw new MissingMethodException(typeof(Localization).FullName, "AddWord");
        }

        foreach (KeyValuePair<string, string> entry in texts)
        {
            AddWordMethod.Invoke(localization, new object[] { entry.Key, entry.Value });
        }
    }

    private static Dictionary<string, string> FindExternalLocalizationFiles()
    {
        Dictionary<string, string> filesByLanguage = new(StringComparer.OrdinalIgnoreCase);
        if (_plugin == null)
        {
            return filesByLanguage;
        }

        string? bepInExRoot = Path.GetDirectoryName(Paths.PluginPath);
        if (string.IsNullOrEmpty(bepInExRoot) || !Directory.Exists(bepInExRoot))
        {
            return filesByLanguage;
        }

        string filePrefix = _plugin.Info.Metadata.Name + ".";
        string[] files;
        try
        {
            files = Directory
                .EnumerateFiles(bepInExRoot, filePrefix + "*", SearchOption.AllDirectories)
                .Where(file => FileExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception)
        {
            Logger.LogWarning(
                $"Could not search BepInEx for external {_plugin.Info.Metadata.Name} translations: {exception.Message}");
            return filesByLanguage;
        }

        foreach (string file in files)
        {
            string fileName = Path.GetFileNameWithoutExtension(file);
            if (!fileName.StartsWith(filePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string language = fileName.Substring(filePrefix.Length);
            if (string.IsNullOrWhiteSpace(language))
            {
                continue;
            }

            if (filesByLanguage.ContainsKey(language))
            {
                Logger.LogWarning(
                    $"Duplicate {language} translation for {_plugin.Info.Metadata.Name}. Skipping {file}; using {filesByLanguage[language]}.");
                continue;
            }

            filesByLanguage[language] = file;
        }

        return filesByLanguage;
    }

    private static Dictionary<string, string>? LoadEmbeddedTranslation(string language, bool required)
    {
        byte[]? data = LoadTranslationFromAssembly(language);
        if (data == null)
        {
            if (required)
            {
                throw new InvalidDataException(
                    $"No embedded English localization was found for {_plugin?.Info.Metadata.Name}. Expected translations.English.yml or translations.English.json.");
            }

            return null;
        }

        try
        {
            return Deserialize(Encoding.UTF8.GetString(data), $"embedded {language} translation");
        }
        catch (Exception exception)
        {
            if (required)
            {
                throw new InvalidDataException($"The embedded {language} localization could not be loaded.", exception);
            }

            Logger.LogWarning(
                $"Could not load the embedded {language} translation. Falling back to English: {exception.Message}");
            return null;
        }
    }

    private static bool TryMergeExternalTranslation(
        Dictionary<string, string> target,
        string file,
        string language)
    {
        try
        {
            Dictionary<string, string> externalTexts = Deserialize(
                File.ReadAllText(file, Encoding.UTF8),
                file);
            Merge(target, externalTexts);
            Logger.LogDebug($"Loaded {_plugin!.Info.Metadata.Name} {language} translation from {file}.");
            return true;
        }
        catch (Exception exception)
        {
            Logger.LogWarning(
                $"Could not load {_plugin!.Info.Metadata.Name} {language} translation at {file}. Falling back to an embedded translation or English: {exception.Message}");
            return false;
        }
    }

    private static Dictionary<string, string> Deserialize(string sourceText, string sourceName)
    {
        Dictionary<string, string>? parsed = new DeserializerBuilder()
            .IgnoreFields()
            .Build()
            .Deserialize<Dictionary<string, string>?>(sourceText);
        if (parsed == null)
        {
            throw new InvalidDataException($"{sourceName} is empty.");
        }

        Dictionary<string, string> normalized = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in parsed)
        {
            string key = entry.Key?.Trim() ?? "";
            if (key.StartsWith("$", StringComparison.Ordinal))
            {
                key = key.Substring(1);
            }

            if (key.Length == 0 || string.IsNullOrWhiteSpace(entry.Value))
            {
                continue;
            }

            normalized[key] = entry.Value;
        }

        return normalized;
    }

    private static void Merge(Dictionary<string, string> target, Dictionary<string, string> source)
    {
        foreach (KeyValuePair<string, string> entry in source)
        {
            if (target.ContainsKey(entry.Key))
            {
                target[entry.Key] = entry.Value;
            }
        }
    }

    private static byte[]? LoadTranslationFromAssembly(string language)
    {
        foreach (string extension in FileExtensions)
        {
            if (ReadEmbeddedFileBytes("translations." + language + extension) is { } data)
            {
                return data;
            }
        }

        return null;
    }

    private static byte[]? ReadEmbeddedFileBytes(string resourceFileName)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string? resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(resourceFileName, StringComparison.Ordinal));
        if (resourceName == null)
        {
            return null;
        }

        using Stream? resourceStream = assembly.GetManifestResourceStream(resourceName);
        if (resourceStream == null)
        {
            return null;
        }

        using MemoryStream output = new();
        resourceStream.CopyTo(output);
        return output.Length == 0 ? null : output.ToArray();
    }
}

[HarmonyPatch(typeof(Localization), nameof(Localization.SetupLanguage))]
internal static class LocalizationSetupLanguageAdditiveDamagePatch
{
    private static void Postfix(Localization __instance, string language)
    {
        Localizer.LoadLocalization(__instance, language);
    }
}
