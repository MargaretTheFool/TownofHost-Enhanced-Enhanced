using System;
using TOHE.Modules;

namespace TOHE;

public class PresetOptionItem(int defaultValue, TabGroup tab, bool vanilla) : OptionItem(0, "Preset", defaultValue, tab, true, vanillaStr: vanilla)
{
    public IntegerValueRule Rule = (0, NumPresets - 1, 1);
    public int ValuePresets = NumPresets;

    public static PresetOptionItem Create(int defaultValue, TabGroup tab, bool vanillaText = false)
    {
        return new PresetOptionItem(defaultValue, tab, vanillaText);
    }

    // Getter
    public override int GetInt() => IsRemotePreset ? RemotePreset : Rule.GetValueByIndex(CurrentValue);
    public override float GetFloat() => IsRemotePreset ? RemotePreset : Rule.GetValueByIndex(CurrentValue);
    public override string GetString()
    {
        if (IsRemotePreset) return Translator.GetString("Preset") + " 9";

        return GetPresetName(CurrentValue);
    }

    public static string GetPresetName(int preset)
    {
        var name = preset switch
        {
            0 => Main.Preset1,
            1 => Main.Preset2,
            2 => Main.Preset3,
            3 => Main.Preset4,
            4 => Main.Preset5,
            5 => Main.Preset6,
            6 => Main.Preset7,
            7 => Main.Preset8,
            _ => throw new ArgumentOutOfRangeException(nameof(preset))
        };

        return name.Value == (string)name.DefaultValue ? Translator.GetString($"Preset_{preset + 1}") : name.Value;
    }
    public override int GetValue()
        => IsRemotePreset ? RemotePreset : Rule.RepeatIndex(base.GetValue());

    // Setter
    public override void SetValue(int value, bool doSync = true)
    {
        SetValue(value, doSave: true, doSync: doSync);
    }
    public override void SetValue(int afterValue, bool doSave, bool doSync = true)
    {
        var preset = Rule.RepeatIndex(afterValue);
        if (doSave && preset != CurrentPreset && !OptionSaver.ReloadPreset(preset)) return;

        base.SetValue(preset, doSave: false, doSync: false);
        SwitchPreset(preset, doSync);
        if (doSave) OptionSaver.Save();
    }
}
