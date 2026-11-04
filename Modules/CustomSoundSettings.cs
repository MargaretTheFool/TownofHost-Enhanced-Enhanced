using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TOHE.Modules;

internal sealed class CustomSoundSettings
{
    private const int CurrentVersion = 1;
    private static readonly string ConfigPath = Path.Combine(Environment.CurrentDirectory, "TOHEE-DATA", "CustomSoundsVolumeConfig.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public int MasterVolume { get; set; } = 100;

    public Dictionary<string, CustomSoundEffectSettings> Effects { get; } = [];

    public static CustomSoundSettings Load(IEnumerable<string> effectIds)
    {
        var settings = new CustomSoundSettings();
        foreach (var effectId in effectIds)
            settings.Effects.TryAdd(effectId, new CustomSoundEffectSettings());

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(ConfigPath));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new JsonException("Custom sound settings must be a JSON object");

            if (root.TryGetProperty("version", out var versionElement)
                && TryGetNonNegativeInt(versionElement, out var version)
                && version != CurrentVersion)
            {
                Logger.Warn($"Unsupported custom sound settings version {version}; using defaults", "CustomSoundSettings.Load");
                return settings;
            }

            if (root.TryGetProperty("masterVolume", out var masterVolumeElement)
                && TryGetNonNegativeInt(masterVolumeElement, out var masterVolume))
                settings.MasterVolume = masterVolume;

            if (root.TryGetProperty("effects", out var effectsElement) && effectsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var effectElement in effectsElement.EnumerateObject())
                {
                    if (!settings.Effects.TryGetValue(effectElement.Name, out var effectSettings)) continue;
                    if (effectElement.Value.ValueKind == JsonValueKind.Object)
                    {
                        if (effectElement.Value.TryGetProperty("enabled", out var enabledElement)
                            && enabledElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                            effectSettings.Enabled = enabledElement.GetBoolean();

                        if (effectElement.Value.TryGetProperty("volume", out var volumeElement)
                            && TryGetNonNegativeInt(volumeElement, out var volume))
                            effectSettings.Volume = volume;
                    }
                }
            }
        }
        catch (FileNotFoundException)
        {
            return settings;
        }
        catch (DirectoryNotFoundException)
        {
            return settings;
        }
        catch (JsonException error)
        {
            Logger.Warn($"Unable to parse custom sound settings; using defaults: {error}", "CustomSoundSettings.Load");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Logger.Error($"Unable to read custom sound settings; using defaults: {error}", "CustomSoundSettings.Load");
        }

        return settings;
    }

    public void Save()
    {
        try
        {
            var data = new
            {
                version = CurrentVersion,
                masterVolume = MasterVolume,
                effects = Effects.ToDictionary(
                    effect => effect.Key,
                    effect => new { enabled = effect.Value.Enabled, volume = effect.Value.Volume })
            };
            var json = JsonSerializer.Serialize(data, JsonOptions);
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Logger.Error($"Unable to save custom sound settings: {error}", "CustomSoundSettings.Save");
        }
    }

    private static bool TryGetNonNegativeInt(JsonElement element, out int value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value) && value >= 0;
    }
}

internal sealed class CustomSoundEffectSettings
{
    public bool Enabled { get; set; } = true;
    public int Volume { get; set; } = 100;
}
