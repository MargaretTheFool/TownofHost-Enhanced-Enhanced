using System;
using System.IO;
using System.Text.Json;

namespace TOHE.Modules;

// https://github.com/tukasa0001/TownOfHost/blob/main/Modules/OptionSaver.cs
public static class OptionSaver
{
    private static string SaveDataDirectory => Path.Combine(DataDirectoryMigration.DataDirectory, "SaveData");
    private static string LegacyPath => Path.Combine(SaveDataDirectory, "Options.json");
    private static string GlobalPath => Path.Combine(SaveDataDirectory, "GlobalOptions.jsonc");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    private static Dictionary<int, int> savedGlobal;
    private static readonly Dictionary<int, int>[] savedPresets = new Dictionary<int, int>[OptionItem.NumPresets];
    private static readonly bool[] blockedPresets = new bool[OptionItem.NumPresets];
    private static bool loading;
    private static bool loadFailed = true;

    private static string PresetPath(int preset) => Path.Combine(SaveDataDirectory, $"Preset{preset + 1}.jsonc");

    public static void Initialize()
    {
        Directory.CreateDirectory(SaveDataDirectory);
    }

    /// <summary>Save current options to the files whose values have changed.</summary>
    public static void Save() => TrySave();

    /// <summary>Atomically persist changed files and report whether all required writes succeeded.</summary>
    public static bool TrySave()
    {
        if (loading || loadFailed || blockedPresets.Any(blocked => blocked) ||
            (AmongUsClient.Instance != null && AmongUsClient.Instance.AmConnected && !AmongUsClient.Instance.AmHost)) return false;

        Dictionary<int, int> global = [];
        var presets = Enumerable.Range(0, OptionItem.NumPresets)
            .Select(_ => new Dictionary<int, int>()).ToArray();
        foreach (var option in OptionItem.AllOptions)
        {
            if (option.IsSingleValue)
                global[option.Id] = option.SingleValue;
            else
                for (int i = 0; i < OptionItem.NumPresets; i++)
                    presets[i][option.Id] = option.AllValues[i];
        }

        if (!SameValues(global, savedGlobal) || !File.Exists(GlobalPath))
        {
            if (!WriteOptionsFile(GlobalPath, global, isPreset: false)) return false;
            savedGlobal = global;
        }
        for (int i = 0; i < OptionItem.NumPresets; i++)
        {
            string path = PresetPath(i);
            if (SameValues(presets[i], savedPresets[i]) && File.Exists(path)) continue;
            if (!WriteOptionsFile(path, presets[i], isPreset: true)) return false;
            savedPresets[i] = presets[i];
        }
        return true;
    }

    private static bool SameValues(Dictionary<int, int> current, Dictionary<int, int> saved) =>
        saved != null && current.Count == saved.Count &&
        current.All(entry => saved.TryGetValue(entry.Key, out var value) && value == entry.Value);

    private static bool WriteOptionsFile(string path, Dictionary<int, int> options, bool isPreset)
    {
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var data = new SerializableOptionFileData { Version = Version, Options = options };
            string header = isPreset
                ? "// Option IDs map to stored indexes, not displayed values.\n// Copy over any preset file and select that preset to load it.\n"
                : "// Global option IDs map to stored indexes, including the local preset selector.\n";
            Directory.CreateDirectory(SaveDataDirectory);
            File.WriteAllText(temporaryPath, header + JsonSerializer.Serialize(data, JsonOptions));
            File.Move(temporaryPath, path, overwrite: true);
            return true;
        }
        catch (Exception error)
        {
            Logger.Error($"Unable to save option file {path}: {error}", "OptionSaver.Save");
            return false;
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (Exception error) { Logger.Error($"Temporary option file cleanup failed: {error}", "OptionSaver.Save"); }
        }
    }

    private static SerializableOptionFileData ReadOptionsFile(string path)
    {
        var data = JsonSerializer.Deserialize<SerializableOptionFileData>(File.ReadAllText(path), JsonOptions)
            ?? throw new JsonException($"Option file is null: {path}");
        if (data.Version != Version || data.Options == null)
            throw new JsonException($"Option file has an unsupported version or is missing its options: {path}");
        return data;
    }

    /// <summary>Load every file before applying any values, falling back to legacy data for missing files.</summary>
    public static void Load()
    {
        if (loading) return;
        loading = true;
        loadFailed = true;
        try
        {
            var global = File.Exists(GlobalPath) ? ReadOptionsFile(GlobalPath) : null;
            var presets = new SerializableOptionFileData[OptionItem.NumPresets];
            for (int i = 0; i < presets.Length; i++)
            {
                string path = PresetPath(i);
                if (File.Exists(path)) presets[i] = ReadOptionsFile(path);
            }

            SerializableOptionsData legacy = null;
            if ((global == null || presets.Any(preset => preset == null)) && File.Exists(LegacyPath))
            {
                string json = File.ReadAllText(LegacyPath);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    legacy = JsonSerializer.Deserialize<SerializableOptionsData>(json, JsonOptions)
                        ?? throw new JsonException("Legacy option data is null");
                    if (legacy.Version != Version || legacy.SingleOptions == null || legacy.PresetOptions == null)
                        throw new JsonException("Legacy option data has an unsupported version or is missing its option dictionaries");
                }
            }

            Dictionary<OptionItem, int> singleValues = [];
            Dictionary<OptionItem, int[]> presetValues = [];
            foreach (var option in OptionItem.AllOptions)
            {
                if (option.IsSingleValue)
                {
                    var values = global?.Options ?? legacy?.SingleOptions;
                    singleValues[option] = values != null && values.TryGetValue(option.Id, out int value)
                        ? value : option.DefaultValue;
                    continue;
                }

                var restored = new int[OptionItem.NumPresets];
                for (int i = 0; i < restored.Length; i++)
                {
                    restored[i] = option.DefaultValue;
                    if (presets[i] != null)
                    {
                        if (presets[i].Options.TryGetValue(option.Id, out int value)) restored[i] = value;
                    }
                    else if (legacy != null)
                    {
                        if (legacy.PresetOptions.TryGetValue(option.Id, out int[] values))
                        {
                            if (values != null && i < values.Length) restored[i] = values[i];
                        }
                        else if (legacy.SingleOptions.TryGetValue(option.Id, out int value))
                            restored[i] = value;
                    }
                }
                presetValues[option] = restored;
            }

            // The preset selector is a single option; its SetValue calls SwitchPreset.
            // Apply the arrays first and keep loading true so its reload is a no-op.
            foreach (var entry in presetValues) entry.Key.SetAllValues(entry.Value);
            foreach (var entry in singleValues) entry.Key.SetValue(entry.Value, doSave: false, doSync: false);
            foreach (var option in OptionItem.AllOptions) option.Refresh();

            savedGlobal = global == null ? null : OptionItem.AllOptions
                .Where(option => option.IsSingleValue).ToDictionary(option => option.Id, option => option.SingleValue);
            for (int i = 0; i < presets.Length; i++)
            {
                int preset = i;
                savedPresets[i] = presets[i] == null ? null :
                    OptionItem.AllOptions.Where(option => !option.IsSingleValue)
                        .ToDictionary(option => option.Id, option => option.AllValues[preset]);
            }
            Array.Fill(blockedPresets, false);
            loadFailed = false;
        }
        catch (Exception error)
        {
            Logger.Error($"Unable to load options; keeping existing values and preserving saved files: {error}", "OptionSaver.Load");
        }
        finally
        {
            loading = false;
        }

        // Create only missing files after all input has been validated and applied.
        if (!loadFailed) TrySave();
    }

    /// <summary>Re-read a zero-based preset before selecting it, without touching any other file.</summary>
    public static bool ReloadPreset(int preset)
    {
        if (loading) return true;
        if (loadFailed || preset < 0 || preset >= OptionItem.NumPresets) return false;
        try
        {
            string path = PresetPath(preset);
            var data = File.Exists(path) ? ReadOptionsFile(path) : null;
            Dictionary<int, int> values = [];
            foreach (var option in OptionItem.AllOptions)
            {
                if (option.IsSingleValue) continue;
                values[option.Id] = data != null && data.Options.TryGetValue(option.Id, out int value)
                    ? value : option.DefaultValue;
            }

            foreach (var option in OptionItem.AllOptions)
            {
                if (option.IsSingleValue) continue;
                option.AllValues[preset] = values[option.Id];
                if (OptionItem.CurrentPreset == preset) option.Refresh();
            }
            savedPresets[preset] = data == null ? null : values;
            blockedPresets[preset] = false;
            return true;
        }
        catch (Exception error)
        {
            blockedPresets[preset] = true;
            Logger.Error($"Unable to reload preset {preset + 1}; keeping existing values and preserving the saved file: {error}", "OptionSaver.Load");
            return false;
        }
    }

    public class SerializableOptionFileData
    {
        public int Version { get; init; }
        public Dictionary<int, int> Options { get; init; }
    }

    /// <summary>Legacy Options.json data, retained for migration.</summary>
    public class SerializableOptionsData
    {
        public int Version { get; init; }
        public Dictionary<int, int> SingleOptions { get; init; }
        public Dictionary<int, int[]> PresetOptions { get; init; }
    }

    /// <summary>Raise when making incompatible changes to the format of an option.</summary>
    public static readonly int Version = 1;
}
