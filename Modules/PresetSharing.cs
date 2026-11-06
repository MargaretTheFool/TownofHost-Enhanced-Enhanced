using System;
using System.IO;
using System.Text.Json;

namespace TOHE.Modules;

/// <summary>Local host commands only. Imported values never select a different preset slot.</summary>
public static class PresetSharing
{
    public const int FormatVersion = 1;
    public const int MaximumFileBytes = 2 * 1024 * 1024;
    private const string CompatibleGameVersion = Main.SupportedVersionAU;
    private static string PresetDirectory => Path.Combine(DataDirectoryMigration.DataDirectory, "Presets");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public enum Error
    {
        None, NotHost, LobbyOnly, InvalidName, NotFound, TooLarge, InvalidData,
        VersionMismatch, IncompatibleOptions, IoError, ApplyFailed
    }

    public readonly record struct Result(Error Error, string FileName = null, int OptionCount = 0)
    {
        public bool Success => Error == PresetSharing.Error.None;
        public string TranslationKey => "PresetSharing" + Error;
    }

    public static Result Save(string name)
    {
        var guard = CheckPermission();
        if (guard != Error.None) return new(guard);
        if (!TryGetPath(name, out var path)) return new(Error.InvalidName);
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (HasLink(path)) return new(Error.InvalidName);
            Directory.CreateDirectory(PresetDirectory);
            if (HasLink(path)) return new(Error.InvalidName);
            var options = EditableOptions();
            if (options.Length is 0 or > 10000) return new(Error.IncompatibleOptions);
            var entries = options.Select(CreateEntry).ToArray();
            var data = new
            {
                format = "TOHEE-preset",
                version = FormatVersion,
                optionVersion = OptionSaver.Version,
                gameVersion = CompatibleGameVersion,
                modVersion = Main.PluginVersion,
                sourcePreset = OptionItem.CurrentPreset + 1,
                options = entries
            };
            var json = JsonSerializer.SerializeToUtf8Bytes(data, JsonOptions);
            if (json.Length > MaximumFileBytes) return new(Error.TooLarge);
            File.WriteAllBytes(temporaryPath, json);
            File.Move(temporaryPath, path, overwrite: true);
            return new(Error.None, Path.GetFileName(path), options.Length);
        }
        catch (InvalidOperationException) { return new(Error.IncompatibleOptions); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Warn("Unable to save local preset: " + ex.GetType().Name, "PresetSharing");
            return new(Error.IoError);
        }
        finally { DeleteTemporaryFile(temporaryPath); }
    }

    public static Result Load(string name)
    {
        var guard = CheckPermission();
        if (guard != Error.None) return new(guard);
        if (!TryGetPath(name, out var path)) return new(Error.InvalidName);
        try
        {
            if (HasLink(path)) return new(Error.InvalidName);
            if (!File.Exists(path)) return new(Error.NotFound);
            // Open once and bound both the observed size and actual read, including a growing file.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumFileBytes) return new(Error.TooLarge);
            using var bytes = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
            {
                if (bytes.Length + read > MaximumFileBytes) return new(Error.TooLarge);
                bytes.Write(buffer, 0, read);
            }
            using var document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
            var validation = Validate(document.RootElement, out var plan);
            if (validation != Error.None) return new(validation);
            // No yields/coroutines: validation, application and commit run on the command thread.
            guard = CheckPermission();
            if (guard != Error.None) return new(guard);
            var snapshots = plan.Select(change => (change.Option, change.Option.SingleValue,
                Values: (int[])change.Option.AllValues.Clone())).ToArray();
            try
            {
                foreach (var change in plan)
                    change.Option.SetValue(change.Index, doSave: false, doSync: false);
                if (!OptionSaver.TrySave())
                {
                    Restore(snapshots);
                    return new(Error.IoError);
                }
            }
            catch (Exception ex)
            {
                Restore(snapshots);
                Logger.Warn("Preset application rolled back: " + ex.GetType().Name, "PresetSharing");
                return new(Error.ApplyFailed);
            }
            // Persistence is now committed. A transport error cannot invalidate this successful import.
            try { OptionItem.SyncAllOptions(); }
            catch (Exception ex) { Logger.Warn("Preset committed; settings synchronization failed: " + ex.GetType().Name, "PresetSharing"); }
            return new(Error.None, Path.GetFileName(path), plan.Count);
        }
        catch (JsonException) { return new(Error.InvalidData); }
        catch (InvalidOperationException) { return new(Error.IncompatibleOptions); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Warn("Unable to load local preset: " + ex.GetType().Name, "PresetSharing");
            return new(Error.IoError);
        }
    }

    private static Error CheckPermission()
    {
        if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost || PlayerControl.LocalPlayer == null)
            return Error.NotHost;
        return GameStates.IsLobby && !GameStates.IsCoStartGame ? Error.None : Error.LobbyOnly;
    }

    private static OptionItem[] EditableOptions() => OptionItem.AllOptions
        .Where(option => option.Id != OptionItem.PresetId && !option.IsText && option is not TextOptionItem)
        .OrderBy(option => option.Id).ToArray();

    private static Dictionary<string, object> CreateEntry(OptionItem option)
    {
        var index = option.GetValue();
        if (!IsValidIndex(option, index)) throw new InvalidOperationException("Invalid current option value");
        var entry = new Dictionary<string, object>
        {
            ["id"] = option.Id,
            ["name"] = option.Name,
            ["scope"] = option.IsSingleValue ? "global" : "preset",
            ["index"] = index
        };
        switch (option)
        {
            case BooleanOptionItem:
                entry["type"] = "boolean";
                entry["value"] = index != 0; // Never use GetBool(): parent-off is not the stored value.
                break;
            case IntegerOptionItem integer:
                entry["type"] = "integer"; entry["value"] = integer.Rule.GetValueByIndex(index);
                entry["min"] = integer.Rule.MinValue; entry["max"] = integer.Rule.MaxValue; entry["step"] = integer.Rule.Step;
                break;
            case FloatOptionItem number:
                entry["type"] = "float"; entry["value"] = number.Rule.GetValueByIndex(index);
                entry["min"] = number.Rule.MinValue; entry["max"] = number.Rule.MaxValue; entry["step"] = number.Rule.Step;
                break;
            case StringOptionItem choice:
                entry["type"] = "choice"; entry["value"] = choice.Selections[index]; entry["choices"] = choice.Selections;
                break;
            default: throw new InvalidOperationException("Unsupported option type");
        }
        return entry;
    }

    private static Error Validate(JsonElement root, out List<(OptionItem Option, int Index)> plan)
    {
        plan = [];
        if (!HasProperties(root, "format", "version", "optionVersion", "gameVersion", "modVersion", "sourcePreset", "options") ||
            !IsString(root.GetProperty("format"), "TOHEE-preset") ||
            !TryInt(root.GetProperty("version"), out var version) ||
            !TryInt(root.GetProperty("optionVersion"), out var optionVersion) ||
            root.GetProperty("gameVersion").ValueKind != JsonValueKind.String ||
            root.GetProperty("modVersion").ValueKind != JsonValueKind.String ||
            !TryInt(root.GetProperty("sourcePreset"), out var sourcePreset) || sourcePreset < 1 || sourcePreset > OptionItem.NumPresets)
            return Error.InvalidData;
        if (version != FormatVersion || optionVersion != OptionSaver.Version ||
            !IsString(root.GetProperty("gameVersion"), CompatibleGameVersion) ||
            !IsString(root.GetProperty("modVersion"), Main.PluginVersion)) return Error.VersionMismatch;
        var entries = root.GetProperty("options");
        var expected = EditableOptions().ToDictionary(option => option.Id);
        if (expected.Count == 0) return Error.IncompatibleOptions;
        if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > 10000) return Error.InvalidData;
        var ids = new HashSet<int>();
        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("id", out var idValue) ||
                !TryInt(idValue, out var id) || !ids.Add(id)) return Error.InvalidData;
            if (!expected.TryGetValue(id, out var option)) return Error.IncompatibleOptions;
            var schema = CreateEntry(option);
            if (!HasProperties(entry, schema.Keys.ToArray())) return Error.InvalidData;
            if (!TryInt(entry.GetProperty("index"), out var index)) return Error.InvalidData;
            if (!IsValidIndex(option, index)) return Error.InvalidData;
            foreach (var field in schema)
            {
                if (field.Key is "index" or "value") continue;
                if (!Matches(entry.GetProperty(field.Key), field.Value)) return Error.IncompatibleOptions;
            }
            // Semantic value and raw index must agree; all fields are typed, with no coercion/wrapping.
            var semanticValue = option switch
            {
                BooleanOptionItem => (object)(index != 0),
                IntegerOptionItem integer => integer.Rule.GetValueByIndex(index),
                FloatOptionItem number => number.Rule.GetValueByIndex(index),
                StringOptionItem choice => choice.Selections[index],
                _ => throw new InvalidOperationException("Unsupported option type")
            };
            if (!Matches(entry.GetProperty("value"), semanticValue)) return Error.InvalidData;
            plan.Add((option, index));
        }
        return ids.Count == expected.Count ? Error.None : Error.IncompatibleOptions;
    }

    private static bool IsValidIndex(OptionItem option, int index) => index >= 0 && (option switch
    {
        BooleanOptionItem => index <= 1,
        IntegerOptionItem integer => index <= (integer.Rule.MaxValue - (long)integer.Rule.MinValue) / integer.Rule.Step,
        FloatOptionItem number => index <= (int)((number.Rule.MaxValue - number.Rule.MinValue) / number.Rule.Step),
        StringOptionItem choice => index < choice.Selections.Length,
        _ => false
    });

    private static bool HasProperties(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!seen.Add(property.Name) || !names.Contains(property.Name, StringComparer.Ordinal)) return false;
        return seen.Count == names.Length;
    }

    private static bool Matches(JsonElement actual, object expected) => expected switch
    {
        string text => IsString(actual, text),
        int number => TryInt(actual, out var value) && value == number,
        float number => actual.ValueKind == JsonValueKind.Number && actual.TryGetSingle(out var value) && float.IsFinite(value) && value == number,
        bool boolean => actual.ValueKind is JsonValueKind.True or JsonValueKind.False && actual.GetBoolean() == boolean,
        string[] choices => actual.ValueKind == JsonValueKind.Array && actual.GetArrayLength() == choices.Length &&
            actual.EnumerateArray().Select((value, index) => IsString(value, choices[index])).All(match => match),
        _ => false
    };

    private static bool IsString(JsonElement value, string expected) => value.ValueKind == JsonValueKind.String && value.GetString() == expected;
    private static bool TryInt(JsonElement value, out int number)
    {
        number = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out number);
    }

    private static void Restore((OptionItem Option, int SingleValue, int[] Values)[] snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            try
            {
                if (snapshot.Option.IsSingleValue) snapshot.Option.SetValue(snapshot.SingleValue, doSave: false, doSync: false);
                else { snapshot.Option.SetAllValues(snapshot.Values); snapshot.Option.Refresh(); }
            }
            catch (Exception ex) { Logger.Warn("Preset value restored; refresh failed: " + ex.GetType().Name, "PresetSharing"); }
        }
    }

    private static bool TryGetPath(string name, out string path)
    {
        path = null;
        if (name == null) return false;
        name = name.Trim();
        if (name.EndsWith(".preset", StringComparison.OrdinalIgnoreCase)) name = name[..^7];
        if (name.Length is < 1 or > 64 || name.EndsWith(' ') ||
            name.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_' and not ' ')) return false;
        var reserved = name.ToUpperInvariant();
        if (reserved is "CON" or "PRN" or "AUX" or "NUL" ||
            reserved.Length == 4 && (reserved.StartsWith("COM") || reserved.StartsWith("LPT")) && char.IsDigit(reserved[3])) return false;
        path = Path.Combine(PresetDirectory, name + ".preset");
        return true;
    }

    private static bool HasLink(string path) =>
        IsLink(Path.GetDirectoryName(PresetDirectory)) || IsLink(PresetDirectory) || IsLink(path);
    private static bool IsLink(string path) => (File.Exists(path) || Directory.Exists(path)) &&
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    private static void DeleteTemporaryFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Logger.Warn("Temporary preset cleanup failed: " + ex.GetType().Name, "PresetSharing"); }
    }
}
