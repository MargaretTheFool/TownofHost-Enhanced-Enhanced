using System;
using System.IO;
using System.Text.Json;

namespace TOHE.Modules;

// https://github.com/tukasa0001/TownOfHost/blob/main/Modules/OptionSaver.cs
public static class OptionSaver
{
    private static readonly DirectoryInfo SaveDataDirectoryInfo = new("./TOHE-DATA/SaveData/");
    private static readonly FileInfo OptionSaverFileInfo = new($"{SaveDataDirectoryInfo.FullName}/Options.json");

    public static void Initialize()
    {
        if (!SaveDataDirectoryInfo.Exists)
        {
            SaveDataDirectoryInfo.Create();
            SaveDataDirectoryInfo.Attributes |= FileAttributes.Hidden;
        }
        if (!OptionSaverFileInfo.Exists)
        {
            OptionSaverFileInfo.Create().Dispose();
        }
    }
    /// <summary>Generate object for json serialization from current options</summary>
    private static SerializableOptionsData GenerateOptionsData()
    {
        Dictionary<int, int> singleOptions = [];
        Dictionary<int, int[]> presetOptions = [];
        foreach (var option in OptionItem.AllOptions)
        {
            if (option.IsSingleValue)
            {
                if (!singleOptions.TryAdd(option.Id, option.SingleValue))
                {
                    Logger.Warn($"Duplicate SingleOption ID: {option.Id}", "Option Saver");
                }
            }
            else if (!presetOptions.TryAdd(option.Id, (int[])option.AllValues.Clone()))
            {
                Logger.Warn($"Duplicate preset option ID: {option.Id}", "Option Saver");
            }
        }
        return new SerializableOptionsData
        {
            Version = Version,
            SingleOptions = singleOptions,
            PresetOptions = presetOptions,
        };
    }
    /// <summary>Read deserialized object and set option values</summary>
    private static void LoadOptionsData(SerializableOptionsData serializableOptionsData)
    {
        if (serializableOptionsData.Version != Version)
        {
            // If you want to provide a method for migrating between versions in the future, you can distribute the conversion method for each version here
            Logger.Warn($"Loaded option version {serializableOptionsData.Version} does not match current version {Version}; keeping defaults and preserving the saved file", "Option Saver");
            return;
        }
        if (serializableOptionsData.SingleOptions == null || serializableOptionsData.PresetOptions == null)
            throw new JsonException("Option data is missing its option dictionaries");
        Dictionary<int, int> singleOptions = serializableOptionsData.SingleOptions;
        Dictionary<int, int[]> presetOptions = serializableOptionsData.PresetOptions;
        foreach (var singleOption in singleOptions)
        {
            var id = singleOption.Key;
            var value = singleOption.Value;
            if (OptionItem.FastOptions.TryGetValue(id, out var optionItem) && optionItem.IsSingleValue)
            {
                optionItem.SetValue(value, doSave: false, doSync: false);
            }
        }
        foreach (var presetOption in presetOptions)
        {
            var id = presetOption.Key;
            var values = presetOption.Value;
            if (OptionItem.FastOptions.TryGetValue(id, out var optionItem) && !optionItem.IsSingleValue)
            {
                optionItem.SetAllValues(values);
            }
        }
        foreach (var option in OptionItem.AllOptions)
            option.Refresh();
    }
    /// <summary>Save current options to json file</summary>
    public static void Save()
        => TrySave();

    /// <summary>Atomically persist all presets and report whether the file was committed.</summary>
    public static bool TrySave()
    {
        if (AmongUsClient.Instance != null && !AmongUsClient.Instance.AmHost) return false;

        var temporaryPath = OptionSaverFileInfo.FullName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var jsonString = JsonSerializer.Serialize(GenerateOptionsData(), new JsonSerializerOptions { WriteIndented = true, });
            Directory.CreateDirectory(SaveDataDirectoryInfo.FullName);
            File.WriteAllText(temporaryPath, jsonString);
            File.Move(temporaryPath, OptionSaverFileInfo.FullName, overwrite: true);
            return true;
        }
        catch (System.Exception error)
        {
            Logger.Error($"Error: {error}", "OptionSaver.Save");
            return false;
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (Exception error) { Logger.Error($"Temporary option file cleanup failed: {error}", "OptionSaver.Save"); }
        }
    }
    /// <summary>Read options from json file</summary>
    public static void Load()
    {
        try
        {
            var jsonString = File.ReadAllText(OptionSaverFileInfo.FullName);
            // if empty, do not read, save default value
            if (string.IsNullOrWhiteSpace(jsonString))
            {
                Logger.Info("Save default value as option data is empty", "Option Saver");
                Save();
                return;
            }
            var data = JsonSerializer.Deserialize<SerializableOptionsData>(jsonString)
                ?? throw new JsonException("Option data is null");
            LoadOptionsData(data);
        }
        catch (Exception error)
        {
            Logger.Error($"Unable to load options; keeping defaults and preserving the saved file: {error}", "OptionSaver.Load");
        }
    }

    /// <summary>Optional data suitable for json storage</summary>
    public class SerializableOptionsData
    {
        public int Version { get; init; }
        /// <summary>Non-preset options</summary>
        public Dictionary<int, int> SingleOptions { get; init; }
        /// <summary>Options in the preset</summary>
        public Dictionary<int, int[]> PresetOptions { get; init; }
    }

    /// <summary>Raise the number here when making incompatible changes to the format of an option (e.g., changing the number of presets)</summary>
    public static readonly int Version = 1;
}
