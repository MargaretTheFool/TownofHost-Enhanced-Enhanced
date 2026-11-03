using System;
using TOHE.Modules;
using UnityEngine;

namespace TOHE;

public abstract class OptionItem
{
    #region static
    public static IReadOnlyList<OptionItem> AllOptions => _allOptions;
    private static readonly List<OptionItem> _allOptions = new(1024);
    public static IReadOnlyDictionary<int, OptionItem> FastOptions => _fastOptions;
    private static readonly Dictionary<int, OptionItem> _fastOptions = new(1024);
    private static readonly Dictionary<int, string> nameSettings = [];

    private static int currentPreset;
    private static bool remotePresetActive;
    public static bool IsRemotePreset => remotePresetActive;
    public static bool HasRemotePreset { get; internal set; }
    public static int CurrentPreset
    {
        get => remotePresetActive ? RemotePreset : currentPreset;
        set
        {
            currentPreset = Math.Clamp(value, 0, NumPresets - 1);
            remotePresetActive = false;
        }
    }
    #endregion

    // Constructor variables
    public int Id { get; }
    public string Name { get; }
    public int DefaultValue { get; }
    public TabGroup Tab { get; }
    public bool IsSingleValue { get; }
    // Remote slot 9 is session-only; SingleValue and AllValues remain local save data.
    private int remoteValue;

    // Nullable/Empty Variables
    public Color NameColor { get; protected set; }
    public OptionFormat ValueFormat { get; protected set; }
    public CustomGameMode GameMode { get; protected set; }
    public CustomGameMode HideOptionInFFA { get; protected set; }
    public CustomGameMode HideOptionInHnS { get; protected set; }
    public bool IsHeader { get; protected set; }
    public bool IsHidden { get; protected set; }
    public bool IsText { get; protected set; }
    public bool IsVanillaText { get; protected set; }
    public Dictionary<string, string> ReplacementDictionary
    {
        get => _replacementDictionary;
        set
        {
            if (value == null) _replacementDictionary?.Clear();
            else _replacementDictionary = value;
        }
    }
    private Dictionary<string, string> _replacementDictionary;

    public int[] AllValues { get; private set; } = new int[NumPresets];
    public int CurrentValue
    {
        get => GetValue();
        set => SetValue(value);
    }
    public int SingleValue { get; private set; }

    // Parent and Child Info
    public OptionItem Parent { get; private set; }
    public List<OptionItem> Children;

    public OptionBehaviour OptionBehaviour;

    public event EventHandler<UpdateValueEventArgs> UpdateValueEvent;

    // Constructor
    public OptionItem(int id, string name, int defaultValue, TabGroup tab, bool isSingleValue, bool vanillaStr)
    {
        // Info Setting
        Id = id;
        Name = name;
        DefaultValue = defaultValue;
        remoteValue = defaultValue;
        Tab = tab;
        IsSingleValue = isSingleValue;
        IsVanillaText = vanillaStr;

        // Nullable Info Setting
        NameColor = Color.white;
        ValueFormat = OptionFormat.None;
        GameMode = CustomGameMode.All;
        HideOptionInFFA = CustomGameMode.All;
        HideOptionInHnS = CustomGameMode.All;
        IsHeader = false;
        IsHidden = false;
        IsText = false;

        // Initialize Objects
        Children = [];

        // Set default value
        if (Id == PresetId)
        {
            SingleValue = DefaultValue;
            CurrentPreset = SingleValue;
        }
        else if (IsSingleValue)
        {
            SingleValue = DefaultValue;
        }
        else
        {
            for (int i = 0; i < NumPresets; i++)
            {
                AllValues[i] = DefaultValue;
            }
        }

        if (_fastOptions.TryAdd(id, this))
        {
            _allOptions.Add(this);
            nameSettings.Add(id, name);
        }
        else
        {
            Logger.Error($"Duplicate ID: {id} Name: {name}", "OptionItem");

            nameSettings.TryGetValue(id, out var setting);
            Logger.Error($"Duplicate from: {setting}", "OptionItem");
        }
    }

    // Setter
    public OptionItem Do(Action<OptionItem> action)
    {
        action(this);
        return this;
    }

    public OptionItem SetColor(Color value) => Do(i => i.NameColor = value);
    public OptionItem SetValueFormat(OptionFormat value) => Do(i => i.ValueFormat = value);
    public OptionItem SetGameMode(CustomGameMode value) => Do(i => i.GameMode = value);
    public OptionItem SetHeader(bool value) => Do(i => i.IsHeader = value);
    public OptionItem SetHidden(bool value) => Do(i => i.IsHidden = value);
    public OptionItem SetText(bool value) => Do(i => i.IsText = value);
    public OptionItem HideInFFA(CustomGameMode value = CustomGameMode.FFA) => Do(i => i.HideOptionInFFA = value);
    public OptionItem HideInHnS(CustomGameMode value = CustomGameMode.HidenSeekTOHE) => Do(i => i.HideOptionInHnS = value);

    public OptionItem SetParent(OptionItem parent) => Do(i =>
    {
        foreach (var role in Options.CustomRoleSpawnChances.Where(x => x.Value.Name == parent.Name).ToArray())
        {
            var roleName = Translator.GetString(Enum.GetName(typeof(CustomRoles), role.Key));
            ReplacementDictionary ??= [];
            ReplacementDictionary.TryAdd(roleName, Utils.ColorString(Utils.GetRoleColor(role.Key), roleName));
            break;
        }
        i.Parent = parent;
        parent.SetChild(i);
    });
    public OptionItem SetChild(OptionItem child) => Do(i => i.Children.Add(child));
    public OptionItem RegisterUpdateValueEvent(EventHandler<UpdateValueEventArgs> handler)
        => Do(i => UpdateValueEvent += handler);

    // 置き換え辞書
    public OptionItem AddReplacement((string key, string value) kvp)
        => Do(i =>
        {
            ReplacementDictionary ??= [];
            ReplacementDictionary.Add(kvp.key, kvp.value);
        });
    public OptionItem RemoveReplacement(string key)
        => Do(i => ReplacementDictionary?.Remove(key));

    // Getter
    public virtual string GetName(bool disableColor = false, bool console = false)
    {
        return disableColor ?
            Translator.GetString(Name, ReplacementDictionary, console) :
            Utils.ColorString(NameColor, Translator.GetString(Name, ReplacementDictionary));
    }
    public virtual string GetNameVanilla()
    {
        return Translator.GetString(Name, ReplacementDictionary, vanilla: true);
    }
    public virtual bool GetBool() => CurrentValue != 0 && (Parent == null || Parent.GetBool());
    public virtual int GetInt() => CurrentValue;
    public virtual float GetFloat() => CurrentValue;
    public virtual string GetString()
    {
        return ApplyFormat(CurrentValue.ToString());
    }
    public virtual int GetValue() => IsRemotePreset ? remoteValue : IsSingleValue ? SingleValue : AllValues[CurrentPreset];

    // Deprecated IsHidden function
    public virtual bool IsHiddenOn(CustomGameMode mode)
    {
        return IsHidden || this.Parent?.IsHiddenOn(mode) == true || (HideOptionInFFA != CustomGameMode.All && HideOptionInFFA == mode) || (HideOptionInHnS != CustomGameMode.All && HideOptionInHnS == mode) || (GameMode != CustomGameMode.All && GameMode != mode);
    }
    public string ApplyFormat(string value)
    {
        if (ValueFormat == OptionFormat.None) return value;
        return string.Format(Translator.GetString("Format." + ValueFormat), value);
    }

    public virtual void Refresh()
    {
        if (OptionBehaviour is not null and StringOption opt)
        {
            if (IsVanillaText == true)
            {
                opt.TitleText.text = GetNameVanilla();
            }
            else
            {
                opt.TitleText.text = GetName();
            }
            opt.ValueText.text = GetString();
            opt.oldValue = opt.Value = CurrentValue;
        }
    }
    public virtual void SetValue(int afterValue, bool doSave, bool doSync = true)
    {
        int beforeValue = CurrentValue;
        if (IsRemotePreset)
        {
            remoteValue = afterValue;
        }
        else if (IsSingleValue)
        {
            SingleValue = afterValue;
        }
        else
        {
            AllValues[CurrentPreset] = afterValue;
        }

        CallUpdateValueEvent(beforeValue, afterValue);
        Refresh();
        if (doSync && beforeValue != afterValue)
        {
            RPC.SyncCustomSettingRPC(this);
        }
        if (doSave)
        {
            OptionSaver.Save();
        }
    }
    public virtual void SetValue(int afterValue, bool doSync = true)
    {
        SetValue(afterValue, true, doSync);
    }
    public void SetAllValues(int[] values)
    {
        var restoredValues = new int[NumPresets];
        Array.Fill(restoredValues, DefaultValue);
        if (values != null)
            Array.Copy(values, restoredValues, Math.Min(values.Length, NumPresets));
        AllValues = restoredValues;
    }
    // This Code For Reset All TOHE Setting To Default
    public virtual void SetValueNoRpc(int value)
    {
        SetValue(value, doSave: false, doSync: false);
    }

    public static OptionItem operator ++(OptionItem item)
        => item.Do(item => item.SetValue(item.CurrentValue + 1));
    public static OptionItem operator --(OptionItem item)
        => item.Do(item => item.SetValue(item.CurrentValue - 1));

    public static void SwitchPreset(int newPreset, bool doSync = true)
    {
        CurrentPreset = Math.Clamp(newPreset, 0, NumPresets - 1);

        if (AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost && GameStates.IsHideNSeek)
            ServerLimits.NormalizeHideNSeekImpostors(GameOptionsManager.Instance.CurrentGameOptions.MaxPlayers, doSave: false, doSync: false);

        foreach (var op in AllOptions.ToArray())
            op.Refresh();

        if (doSync) SyncAllOptions();
        PlayerIdentifiers.RefreshLobby();
    }
    public static void SetRemotePreset(bool active)
    {
        if (remotePresetActive == active) return;
        remotePresetActive = active;
        if (active) HasRemotePreset = false;
        foreach (var option in AllOptions) option.Refresh();
    }
    public static bool CopyPreset(int sourcePreset, int targetPreset)
    {
        bool sourceIsRemote = sourcePreset == RemotePreset;
        if (targetPreset < 0 || targetPreset >= NumPresets ||
            (!sourceIsRemote && (sourcePreset < 0 || sourcePreset >= NumPresets)) ||
            (sourceIsRemote && !HasRemotePreset) ||
            (!sourceIsRemote && sourcePreset == targetPreset) ||
            IsRemotePreset || AmongUsClient.Instance == null ||
            !AmongUsClient.Instance.AmHost || !GameStates.IsLobby) return false;

        int activePreset = CurrentPreset;
        var sourceValues = AllOptions
            .Where(option => option.Id != PresetId && !option.IsSingleValue)
            .Select(option => (Option: option, Value: sourceIsRemote ? option.remoteValue : option.AllValues[sourcePreset]))
            .ToArray();
        var previousValues = sourceValues
            .Select(entry => (entry.Option, Value: entry.Option.AllValues[targetPreset]))
            .ToArray();

        for (int i = 0; i < sourceValues.Length; i++)
        {
            var entry = sourceValues[i];
            if (targetPreset == activePreset)
                entry.Option.SetValue(entry.Value, doSave: false, doSync: false);
            else
                entry.Option.AllValues[targetPreset] = entry.Value;
        }

        if (!OptionSaver.TrySave())
        {
            for (int i = 0; i < previousValues.Length; i++)
            {
                var entry = previousValues[i];
                if (targetPreset == activePreset)
                    entry.Option.SetValue(entry.Value, doSave: false, doSync: false);
                else
                    entry.Option.AllValues[targetPreset] = entry.Value;
            }
            return false;
        }

        if (targetPreset == activePreset) SyncAllOptions();
        return true;
    }
    public static bool ResetPresets(int targetPreset = -1)
    {
        if (targetPreset < -1 || targetPreset >= NumPresets || IsRemotePreset ||
            AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost || !GameStates.IsLobby) return false;

        int activePreset = CurrentPreset;
        int firstPreset = targetPreset == -1 ? 0 : targetPreset;
        int presetEndExclusive = targetPreset == -1 ? NumPresets : targetPreset + 1;
        var affectedOptions = AllOptions
            .Where(option => option.Id != PresetId && !option.IsSingleValue)
            .ToArray();
        var previousValues = affectedOptions
            .Select(option => (Option: option, Values: (int[])option.AllValues.Clone()))
            .ToArray();

        foreach (var option in affectedOptions)
        {
            for (int preset = firstPreset; preset < presetEndExclusive; preset++)
            {
                if (preset == activePreset)
                    option.SetValue(option.DefaultValue, doSave: false, doSync: false);
                else
                    option.AllValues[preset] = option.DefaultValue;
            }
        }

        if (!OptionSaver.TrySave())
        {
            foreach (var entry in previousValues)
            {
                for (int preset = firstPreset; preset < presetEndExclusive; preset++)
                {
                    if (preset == activePreset)
                    {
                        entry.Option.SetValue(entry.Values[preset], doSave: false, doSync: false);
                        entry.Option.AllValues[preset] = entry.Values[preset];
                        entry.Option.Refresh();
                    }
                    else
                        entry.Option.AllValues[preset] = entry.Values[preset];
                }
            }
            return false;
        }

        if (targetPreset == -1 || targetPreset == activePreset) SyncAllOptions();
        return true;
    }
    public static void SyncAllOptions(int targetId = -1)
    {
        if (
            Main.AllPlayerControls.Length <= 1 ||
            AmongUsClient.Instance == null ||
            AmongUsClient.Instance.AmHost == false ||
            PlayerControl.LocalPlayer == null
        ) return;

        RPC.SyncCustomSettingsRPC(targetId);
    }


    // EventArgs
    private void CallUpdateValueEvent(int beforeValue, int currentValue)
    {
        if (UpdateValueEvent == null) return;
        try
        {
            UpdateValueEvent(this, new UpdateValueEventArgs(beforeValue, currentValue));
        }
        catch (Exception ex)
        {
            Logger.Error($"[{Name}] - Exception occurred when calling UpdateValueEvent", "OptionItem.UpdateValueEvent");
            Logger.Exception(ex, "OptionItem.UpdateValueEvent");
        }
    }

    public class UpdateValueEventArgs(int beforeValue, int currentValue) : EventArgs
    {
        public int CurrentValue { get; set; } = currentValue;
        public int BeforeValue { get; set; } = beforeValue;
    }

    public const int NumPresets = 8;
    public const int RemotePreset = 8;
    public const int PresetId = 0;
}

public enum TabGroup
{
    SystemSettings,
    ModSettings,
    ModifierSettings,
    ImpostorRoles,
    CrewmateRoles,
    NeutralRoles,
    Addons
}
public enum OptionFormat
{
    None,
    Players,
    Seconds,
    Percent,
    Times,
    Multiplier,
    Votes,
    Pieces,
    Health,
    Level,
}
