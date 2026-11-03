using BepInEx.Unity.IL2CPP.Utils.Collections;
using System;
using TMPro;
using TOHE.Modules;
using TOHE.Patches;
using UnityEngine;
using static TOHE.Translator;
using Object = UnityEngine.Object;

namespace TOHE;

// Thanks: https://github.com/Yumenopai/TownOfHost_Y/blob/main/Patches/GameOptionsMenuPatch.cs
public static class ModGameOptionsMenu
{
    public static int TabIndex = 0;
    public static Il2CppSystem.Collections.Generic.Dictionary<OptionBehaviour, int> OptionList = new();
    public static Il2CppSystem.Collections.Generic.Dictionary<int, OptionBehaviour> BehaviourList = new();
    public static Il2CppSystem.Collections.Generic.Dictionary<int, CategoryHeaderMasked> CategoryHeaderList = new();
}

[HarmonyPatch(typeof(MatchInfoGuide), nameof(MatchInfoGuide.CreateSettingsEntry))]
internal static class MatchInfoGuideSettingsPatch
{
    private static bool Prefix([HarmonyArgument(0)] StringNames settingName) => settingName != StringNames.GameConfirmImpostor;
}

[HarmonyPatch(typeof(GameOptionsMenu))]
public static class GameOptionsMenuPatch
{
    public static GameOptionsMenu Instance;
    private static readonly Dictionary<int, TabGroup> MenuTabs = [];
    private static readonly Dictionary<int, Coroutine> Builds = [];
    private static readonly Dictionary<int, int> BuildVersions = [];
    private static int nextBuildVersion;
    private static int reopenVersion;
    public static bool CanEdit => AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost;

    public static void RegisterTab(GameOptionsMenu menu, TabGroup tab)
        => MenuTabs[menu.GetInstanceID()] = tab;

    private static bool TryGetTab(GameOptionsMenu menu, out TabGroup tab)
    {
        tab = default;
        return menu != null && MenuTabs.TryGetValue(menu.GetInstanceID(), out tab);
    }

    public static void CancelReopen() => reopenVersion++;

    public static void ReleaseMenus()
    {
        CancelReopen();
        MenuTabs.Clear();
        Builds.Clear();
        BuildVersions.Clear();
        Instance = null;
        ModGameOptionsMenu.OptionList = new();
        ModGameOptionsMenu.BehaviourList = new();
        ModGameOptionsMenu.CategoryHeaderList = new();
    }

    // Invoke before the owning GameSettingMenu hides a custom tab. Do not patch
    // OnDisable: its forwarding/empty native entry may be shared with other types.
    public static void HideModMenu(GameOptionsMenu menu)
    {
        if (menu == null) return;
        CancelBuild(menu);
        menu.gameObject.SetActive(false);
    }

    public static void CancelBuild(GameOptionsMenu menu)
    {
        if (!TryGetTab(menu, out _)) return;
        var id = menu.GetInstanceID();
        if (!Builds.TryGetValue(id, out var coroutine)) return;
        menu.StopCoroutine(coroutine);
        Builds.Remove(id);
        BuildVersions.Remove(id);
        ClearPartialBuild(menu);
    }

    private static void ClearPartialBuild(GameOptionsMenu menu)
    {
        if (menu.Children != null)
        {
            foreach (var child in menu.Children.ToArray())
            {
                if (child == null) continue;
                if (ModGameOptionsMenu.OptionList.TryGetValue(child, out var index))
                {
                    ModGameOptionsMenu.OptionList.Remove(child);
                    ModGameOptionsMenu.BehaviourList.Remove(index);
                }
                Object.Destroy(child.gameObject);
            }
            menu.Children.Clear();
        }
        var headerKeys = new List<int>();
        var headers = ModGameOptionsMenu.CategoryHeaderList.GetEnumerator();
        while (headers.MoveNext()) headerKeys.Add(headers.Current.Key);
        foreach (var key in headerKeys)
        {
            var header = ModGameOptionsMenu.CategoryHeaderList[key];
            if (header == null)
            {
                ModGameOptionsMenu.CategoryHeaderList.Remove(key);
                continue;
            }
            if (!header.transform.IsChildOf(menu.settingsContainer)) continue;
            Object.Destroy(header.gameObject);
            ModGameOptionsMenu.CategoryHeaderList.Remove(key);
        }
        menu.ControllerSelectable.Clear();
    }

    private static void RebuildNavigation(GameOptionsMenu menu)
    {
        menu.ControllerSelectable.Clear();
        foreach (var element in menu.scrollBar.GetComponentsInChildren<UiElement>())
            menu.ControllerSelectable.Add(element);
        var groups = menu.Children.ToArray().Where(child => child != null && child.gameObject.activeInHierarchy)
            .Select(child => child.GetComponentsInChildren<UiElement>().ToArray()).Where(group => group.Length > 0).ToArray();
        for (var row = 0; row < groups.Length; row++)
        {
            for (var column = 0; column < groups[row].Length; column++)
            {
                var navigation = groups[row][column].ControllerNav;
                navigation.mode = ControllerNavigation.Mode.Explicit;
                navigation.selectOnUp = row > 0 ? groups[row - 1][Math.Min(column, groups[row - 1].Length - 1)] : null;
                navigation.selectOnDown = row + 1 < groups.Length ? groups[row + 1][Math.Min(column, groups[row + 1].Length - 1)] : null;
                groups[row][column].ControllerNav = navigation;
            }
        }
    }

    public static void OpenModMenu(GameOptionsMenu menu)
    {
        if (!TryGetTab(menu, out _) || Builds.ContainsKey(menu.GetInstanceID()) || menu.ControllerSelectable.Count == 0) return;
        foreach (var child in menu.Children.ToArray())
            if (ModGameOptionsMenu.OptionList.TryGetValue(child, out var index) && OptionItem.AllOptions[index] == Options.NumImpostorsHnS)
                child.Initialize();
        ControllerManager.Instance.OpenOverlayMenu(menu.name, menu.BackButton, menu.ControllerSelectable[0], menu.ControllerSelectable);
    }
    [HarmonyPatch(nameof(GameOptionsMenu.Initialize)), HarmonyPrefix]
    private static bool InitializePrefix(GameOptionsMenu __instance)
    {
        Instance = __instance;
        if (!TryGetTab(__instance, out _)) return true;

        if (__instance.Children == null || __instance.Children.Count == 0)
        {
            __instance.MapPicker.gameObject.SetActive(false);
            __instance.CreateSettings();
            __instance.cachedData = GameOptionsManager.Instance.CurrentGameOptions;
        }

        return false;
    }
    // Thanks: https://github.com/Gurge44/EndlessHostRoles
    [HarmonyPatch(nameof(GameOptionsMenu.Initialize)), HarmonyPostfix]
    private static void InitializePostfix(GameOptionsMenu __instance)
    {
        if (!TryGetTab(__instance, out _) && __instance.Children != null)
            foreach (var child in __instance.Children.ToArray())
                if (child.Title == StringNames.GameNumImpostors) child.Initialize();

        var optionMenu = GameObject.Find("PlayerOptionsMenu(Clone)");
        optionMenu?.transform.FindChild("Background")?.gameObject.SetActive(false);

        _ = new LateTask(() =>
        {
            if (__instance == null || optionMenu == null || GameSettingMenu.Instance == null) return;
            var menuDescription = optionMenu.transform.FindChild("What Is This?");
            if (menuDescription == null) return;

            var infoImage = menuDescription.transform.FindChild("InfoImage");
            if (infoImage == null) return;
            infoImage.transform.localPosition = new(-4.65f, 0.16f, -1f);
            infoImage.transform.localScale = new(0.2202f, 0.2202f, 0.3202f);

            var infoText = menuDescription.transform.FindChild("InfoText");
            if (infoText == null) return;
            infoText.transform.localPosition = new(-3.5f, 0.83f, -2f);
            infoText.transform.localScale = new(1f, 1f, 1f);

            var cubeObject = menuDescription.transform.FindChild("Cube");
            if (cubeObject == null) return;
            cubeObject.transform.localPosition = new(-3.2f, 0.55f, -0.1f);
            cubeObject.transform.localScale = new(0.61f, 0.64f, 1f);

            var menuDescriptionText = GameSettingMenu.Instance.MenuDescriptionText;
            if (menuDescriptionText != null) menuDescriptionText.m_marginWidth = 2.5f;
        }, 0.2f, "Set Menu", shoudLog: false);
    }

    [HarmonyPatch(nameof(GameOptionsMenu.CreateSettings)), HarmonyPrefix]
    private static bool CreateSettingsPrefix(GameOptionsMenu __instance)
    {
        Instance = __instance;
        // When is vanilla tab, run vanilla code
        if (!TryGetTab(__instance, out var modTab)) return true;

        var menuId = __instance.GetInstanceID();
        if (Builds.ContainsKey(menuId)) return false;
        // CreateSettings can be reached through native/inlined initialization as
        // well as our Initialize prefix. Own the list at the actual build entry;
        // never append into a list copied from a Unity template. A positive
        // capacity also allocates storage without relying on the empty-array path.
        ClearPartialBuild(__instance);
        int capacity = Math.Max(1, OptionItem.AllOptions.Count(option => option.Tab == modTab && option is not TextOptionItem));
        var children = new Il2CppSystem.Collections.Generic.List<OptionBehaviour>(capacity);
        __instance.Children = children;
        var buildVersion = ++nextBuildVersion;
        BuildVersions[menuId] = buildVersion;
        __instance.ControllerSelectable.Clear();

        __instance.scrollBar.SetYBoundsMax(CalculateScrollBarYBoundsMax());
        Builds[menuId] = __instance.StartCoroutine(CoRoutine().WrapToIl2Cpp());
        return false;

        System.Collections.IEnumerator CoRoutine()
        {
            float num = 2.0f;
            const float posX = 0.952f;
            const float posZ = -2.0f;
            for (int index = 0; index < OptionItem.AllOptions.Count; index++)
            {
                if (__instance == null || !__instance.gameObject.activeInHierarchy ||
                    !BuildVersions.TryGetValue(menuId, out var currentVersion) || currentVersion != buildVersion)
                    yield break;
                var option = OptionItem.AllOptions[index];
                if (option.Tab != modTab) continue;

                var enabled = !option.IsHiddenOn(Options.CurrentGameMode) && option.Parent?.GetBool() is null or true;

                if (option is TextOptionItem)
                {
                    CategoryHeaderMasked categoryHeaderMasked = Object.Instantiate(__instance.categoryHeaderOrigin, Vector3.zero, Quaternion.identity, __instance.settingsContainer);
                    categoryHeaderMasked.SetHeader(StringNames.RolesCategory, 20);
                    categoryHeaderMasked.Title.text = option.GetName();
                    categoryHeaderMasked.transform.localScale = Vector3.one * 0.68f;
                    categoryHeaderMasked.transform.localPosition = new(-0.913f, num, posZ);
                    var chmText = categoryHeaderMasked.transform.FindChild("HeaderText").GetComponent<TextMeshPro>();
                    chmText.fontStyle = FontStyles.Bold;
                    chmText.outlineWidth = 0.17f;
                    categoryHeaderMasked.gameObject.SetActive(enabled);
                    ModGameOptionsMenu.CategoryHeaderList.TryAdd(index, categoryHeaderMasked);

                    if (enabled) num -= 0.63f;
                }
                else if (option.IsHeader && enabled) num -= 0.3f;

                if (option is TextOptionItem) continue;

                var baseGameSetting = GetSetting(option);
                if (baseGameSetting == null) continue;


                OptionBehaviour optionBehaviour;

                switch (baseGameSetting.Type)
                {
                    case OptionTypes.Checkbox:
                        {
                            optionBehaviour = Object.Instantiate(__instance.checkboxOrigin, Vector3.zero, Quaternion.identity, __instance.settingsContainer);
                            optionBehaviour.transform.localPosition = new(posX, num, posZ);

                            OptionBehaviourSetSizeAndPosition(optionBehaviour, option, baseGameSetting.Type);

                            optionBehaviour.SetClickMask(__instance.ButtonClickMask);
                            optionBehaviour.SetUpFromData(baseGameSetting, 20);
                            ModGameOptionsMenu.OptionList.TryAdd(optionBehaviour, index);
                            break;
                        }
                    case OptionTypes.String:
                        {
                            optionBehaviour = Object.Instantiate(__instance.stringOptionOrigin, Vector3.zero, Quaternion.identity, __instance.settingsContainer);
                            optionBehaviour.transform.localPosition = new(posX, num, posZ);

                            OptionBehaviourSetSizeAndPosition(optionBehaviour, option, baseGameSetting.Type);

                            if (option.Name == "Preset" && !ModGameOptionsMenu.OptionList.ContainsValue(index))
                            {
                                GameSettingMenuPatch.PresetBehaviour = optionBehaviour as StringOption;
                            }

                            optionBehaviour.SetClickMask(__instance.ButtonClickMask);
                            optionBehaviour.SetUpFromData(baseGameSetting, 20);
                            ModGameOptionsMenu.OptionList.TryAdd(optionBehaviour, index);
                            break;
                        }
                    case OptionTypes.Float:
                    case OptionTypes.Int:
                        {
                            optionBehaviour = Object.Instantiate(__instance.numberOptionOrigin, Vector3.zero, Quaternion.identity, __instance.settingsContainer);
                            optionBehaviour.transform.localPosition = new(posX, num, posZ);

                            OptionBehaviourSetSizeAndPosition(optionBehaviour, option, baseGameSetting.Type);

                            optionBehaviour.SetClickMask(__instance.ButtonClickMask);
                            optionBehaviour.SetUpFromData(baseGameSetting, 20);
                            ModGameOptionsMenu.OptionList.TryAdd(optionBehaviour, index);
                            break;
                        }
                    default:
                        continue;
                }

                optionBehaviour.transform.localPosition = new(0.952f, num, -2f);
                optionBehaviour.SetClickMask(__instance.ButtonClickMask);
                optionBehaviour.SetUpFromData(baseGameSetting, 20);
                ModGameOptionsMenu.OptionList.TryAdd(optionBehaviour, index);
                ModGameOptionsMenu.BehaviourList.TryAdd(index, optionBehaviour);
                optionBehaviour.gameObject.SetActive(enabled);
                optionBehaviour.OnValueChanged = new Action<OptionBehaviour>(__instance.ValueChanged);
                children.Add(optionBehaviour);
                optionBehaviour.Initialize();
                if (!CanEdit) optionBehaviour.SetAsPlayer();

                if (enabled) num -= 0.45f;

                if (index % 50 == 0) yield return null;
            }

            yield return null;

            if (__instance == null || !__instance.gameObject.activeInHierarchy ||
                !BuildVersions.TryGetValue(menuId, out var finalVersion) || finalVersion != buildVersion)
                yield break;
            RebuildNavigation(__instance);
            Builds.Remove(menuId);
            BuildVersions.Remove(menuId);
            OpenModMenu(__instance);
        }

        float CalculateScrollBarYBoundsMax()
        {
            float num = 2.0f;
            foreach (var option in OptionItem.AllOptions)
            {
                if (option.Tab != modTab) continue;

                var enabled = !option.IsHiddenOn(Options.CurrentGameMode) && option.Parent?.GetBool() is null or true;

                if (option is TextOptionItem && enabled) num -= 0.63f;
                else if (enabled)
                {
                    if (option.IsHeader) num -= 0.3f;
                    num -= 0.45f;
                }
            }

            return -num - 1.65f;
        }
    }

    private static void OptionBehaviourSetSizeAndPosition(OptionBehaviour optionBehaviour, OptionItem option, OptionTypes type)
    {
        Vector3 positionOffset = new(0f, 0f, 0f);
        Vector3 scaleOffset = new(0f, 0f, 0f);
        Color color = new(0.8f, 0.8f, 0.8f);
        float sizeDelta_x = 5.7f;

        if (option.Parent?.Parent?.Parent != null)
        {
            scaleOffset = new(-0.18f, 0, 0);
            positionOffset = new(0.3f, 0f, 0f);
            color = new(0.8f, 0.8f, 0.2f);
            sizeDelta_x = 5.1f;
        }
        else if (option.Parent?.Parent != null)
        {
            scaleOffset = new(-0.12f, 0, 0);
            positionOffset = new(0.2f, 0f, 0f);
            color = new(0.5f, 0.2f, 0.8f);
            sizeDelta_x = 5.3f;
        }
        else if (option.Parent != null)
        {
            scaleOffset = new(-0.05f, 0, 0);
            positionOffset = new(0.1f, 0f, 0f);
            color = new(0.2f, 0.8f, 0.8f);
            sizeDelta_x = 5.5f;
        }

        var labelBackground = optionBehaviour.transform.FindChild("LabelBackground");
        labelBackground.GetComponent<SpriteRenderer>().color = color;
        labelBackground.localScale += new Vector3(1f, -0.2f, 0f) + scaleOffset;
        labelBackground.localPosition += new Vector3(-0.6f, 0f, 0f) + positionOffset;

        var titleText = optionBehaviour.transform.FindChild("Title Text");
        titleText.localPosition += new Vector3(-0.7f, 0f, 0f) + positionOffset;
        titleText.GetComponent<RectTransform>().sizeDelta = new(sizeDelta_x, 0.37f);
        var textMeshPro = titleText.GetComponent<TextMeshPro>();
        textMeshPro.alignment = TextAlignmentOptions.MidlineLeft;
        textMeshPro.fontStyle = FontStyles.Bold;
        textMeshPro.outlineWidth = 0.17f;

        switch (type)
        {
            case OptionTypes.Checkbox:
                optionBehaviour.transform.FindChild("Toggle").localPosition = new(1.46f, -0.042f);
                break;

            case OptionTypes.String:
                optionBehaviour.transform.FindChild("PlusButton").localPosition += new Vector3(option.IsText ? 500f : 1.7f, option.IsText ? 500f : 0f, option.IsText ? 500f : 0f);
                optionBehaviour.transform.FindChild("MinusButton").localPosition += new Vector3(option.IsText ? 500f : 0.9f, option.IsText ? 500f : 0f, option.IsText ? 500f : 0f);

                var valueTMP = optionBehaviour.transform.FindChild("Value_TMP (1)");
                valueTMP.localPosition += new Vector3(1.3f, 0f, 0f);
                valueTMP.GetComponent<RectTransform>().sizeDelta = new(2.3f, 0.4f);
                goto default;

            case OptionTypes.Float:
            case OptionTypes.Int:
                optionBehaviour.transform.FindChild("PlusButton").localPosition += new Vector3(option.IsText ? 500f : 1.7f, option.IsText ? 500f : 0f, option.IsText ? 500f : 0f);
                optionBehaviour.transform.FindChild("MinusButton").localPosition += new Vector3(option.IsText ? 500f : 0.9f, option.IsText ? 500f : 0f, option.IsText ? 500f : 0f);
                optionBehaviour.transform.FindChild("Value_TMP").localPosition += new Vector3(1.3f, 0f, 0f);
                goto default;

            default:
                var valueBox = optionBehaviour.transform.FindChild("ValueBox");
                valueBox.localScale += new Vector3(0.2f, 0f, 0f);
                valueBox.localPosition += new Vector3(1.3f, 0f, 0f);
                break;
        }
    }
    public static void ReOpenSettings(int index = 4)
    {
        if (!CanEdit || GameSettingMenu.Instance == null) return;
        //Close setting menu
        GameSettingMenu.Instance.Close();
        var request = ++reopenVersion;
        GameSettingMenu reopenedMenu = null;

        // Auto Click "Edit" Button
        _ = new LateTask(() =>
        {
            if (request != reopenVersion || !GameStates.IsLobby) return;
            if (GameSettingMenu.Instance != null) { CancelReopen(); return; }
            var hostButtons = GameObject.Find("Host Buttons");
            if (hostButtons == null) return;
            var editButton = hostButtons.transform.FindChild("Edit")?.GetComponent<PassiveButton>();
            if (editButton != null)
            {
                editButton.ReceiveClickDown();
                reopenedMenu = GameSettingMenu.Instance;
            }
        }, 0.1f, "Click Edit Button");


        if (index < 3)
            return;

        // Change tab to Original Tab
        _ = new LateTask(() =>
        {
            if (request != reopenVersion || !GameStates.IsLobby || reopenedMenu == null || GameSettingMenu.Instance != reopenedMenu) return;
            GameSettingMenu.Instance.ChangeTab(index, Controller.currentTouchType == Controller.TouchType.Joystick);
        }, 0.28f, "Change Tab");

    }
    [HarmonyPatch(nameof(GameOptionsMenu.CreateSettings)), HarmonyPostfix]
    private static void CreateSettingsPostfix(GameOptionsMenu __instance)
    {
        if (TryGetTab(__instance, out _)) return;
        foreach (var option in __instance.Children.ToArray())
        {
            if (option.Title != StringNames.GameConfirmImpostor) continue;
            float y = option.transform.localPosition.y;
            option.gameObject.SetActive(false);
            __instance.Children.Remove(option);
            foreach (var child in __instance.Children.ToArray())
                if (child.transform.parent == __instance.settingsContainer && child.transform.localPosition.y < y)
                    child.transform.localPosition += new Vector3(0f, 0.45f, 0f);
            foreach (var header in __instance.settingsContainer.GetComponentsInChildren<CategoryHeaderMasked>())
                if (header != __instance.categoryHeaderOrigin && header.transform.localPosition.y < y)
                    header.transform.localPosition += new Vector3(0f, 0.45f, 0f);
            __instance.scrollBar.SetYBoundsMax(__instance.scrollBar.GetYBounds().max - 0.45f);
            RebuildNavigation(__instance);
            break;
        }
    }

    [HarmonyPatch(nameof(GameOptionsMenu.ValueChanged)), HarmonyPrefix]
    private static bool ValueChangedPrefix(GameOptionsMenu __instance, OptionBehaviour option)
    {
        if (!TryGetTab(__instance, out _))
        {
            if (option.TryCast<NumberOption>()?.intOptionName == AmongUs.GameOptions.Int32OptionNames.MaxPlayers)
            {
                ServerLimits.Normalize(GameOptionsManager.Instance.CurrentGameOptions);
                foreach (var child in __instance.Children.ToArray())
                    if (child.Title == StringNames.GameNumImpostors) child.Initialize();
            }
            return true;
        }

        if (ModGameOptionsMenu.OptionList.TryGetValue(option, out var index))
        {
            var item = OptionItem.AllOptions[index];
            if (item != null && item.Children.Count > 0) ReCreateSettings(__instance);
        }
        return false;
    }
    public static void ReCreateSettings(GameOptionsMenu __instance)
    {
        if (!TryGetTab(__instance, out var modTab)) return;

        float num = 2.0f;
        for (int index = 0; index < OptionItem.AllOptions.Count; index++)
        {
            var option = OptionItem.AllOptions[index];
            if (option.Tab != modTab) continue;

            var enabled = !option.IsHiddenOn(Options.CurrentGameMode) && option.Parent?.GetBool() is null or true;

            if (ModGameOptionsMenu.CategoryHeaderList.TryGetValue(index, out var categoryHeaderMasked))
            {
                categoryHeaderMasked.transform.localPosition = new(-0.903f, num, -2f);
                categoryHeaderMasked.gameObject.SetActive(enabled);
                if (enabled) num -= 0.63f;
            }
            else if (option.IsHeader && enabled) num -= 0.3f;

            if (ModGameOptionsMenu.BehaviourList.TryGetValue(index, out var optionBehaviour))
            {
                optionBehaviour.transform.localPosition = new(0.952f, num, -2f);
                optionBehaviour.gameObject.SetActive(enabled);
                if (enabled) num -= 0.45f;
            }
        }

        RebuildNavigation(__instance);
        __instance.scrollBar.SetYBoundsMax(-num - 1.65f);
    }
    private static BaseGameSetting GetSetting(OptionItem item)
    {
        static t CreateAndInvoke<t>(Func<t> func) where t : BaseGameSetting
        {
            return func.Invoke();
        }

        // Redundant casts are here for clarity
        // C# dosen't support intra switch statement methods 😭

        BaseGameSetting baseGameSetting = item switch
        {
            BooleanOptionItem => CreateAndInvoke(() =>
            {
                var x = ScriptableObject.CreateInstance<CheckboxGameSetting>();
                x.Type = OptionTypes.Checkbox;

                return x;
            }),
            IntegerOptionItem integerOptionItem => CreateAndInvoke(() =>
            {
                var x = ScriptableObject.CreateInstance<IntGameSetting>();
                x.Type = OptionTypes.Int;
                x.Value = integerOptionItem.GetInt();
                x.Increment = integerOptionItem.Rule.Step;
                x.ValidRange = new(integerOptionItem.Rule.MinValue, integerOptionItem.Rule.MaxValue);
                x.ZeroIsInfinity = false;
                x.SuffixType = NumberSuffixes.Multiplier;
                x.FormatString = string.Empty;

                return x;
            }),
            FloatOptionItem floatOptionItem => CreateAndInvoke(() =>
            {
                var x = ScriptableObject.CreateInstance<FloatGameSetting>();
                x.Type = OptionTypes.Float;
                x.Value = floatOptionItem.GetFloat();
                x.Increment = floatOptionItem.Rule.Step;
                x.ValidRange = new(floatOptionItem.Rule.MinValue, floatOptionItem.Rule.MaxValue);
                x.ZeroIsInfinity = false;
                x.SuffixType = NumberSuffixes.Multiplier;
                x.FormatString = string.Empty;

                return x;
            }),
            StringOptionItem stringOptionItem => CreateAndInvoke(() =>
            {
                var x = ScriptableObject.CreateInstance<StringGameSetting>();
                x.Type = OptionTypes.String;
                x.Values = new StringNames[stringOptionItem.Selections.Length];
                x.Index = stringOptionItem.GetInt();

                return x;
            }),
            PresetOptionItem presetOptionItem => CreateAndInvoke(() =>
            {
                var x = ScriptableObject.CreateInstance<StringGameSetting>();
                x.Type = OptionTypes.String;
                x.Values = new StringNames[presetOptionItem.ValuePresets];
                x.Index = presetOptionItem.GetInt();

                return x;
            }),
            _ => null
        };

        if (baseGameSetting != null)
        {
            baseGameSetting.Title = StringNames.Accept;
        }

        return baseGameSetting;
    }
}

[HarmonyPatch(typeof(ToggleOption))]
public static class ToggleOptionPatch
{
    [HarmonyPatch(nameof(ToggleOption.Toggle)), HarmonyPrefix]
    public static bool TogglePrefix(ToggleOption __instance)
    {
        if (!ModGameOptionsMenu.OptionList.ContainsKey(__instance)) return true;
        if (!GameOptionsMenuPatch.CanEdit) return false;
        __instance.CheckMark.enabled = !__instance.CheckMark.enabled;
        // Native Toggle inlines UpdateValue on this game version. Route registered
        // mod rows directly; their synthetic data has no native option identifier.
        UpdateValuePrefix(__instance);
        __instance.OnValueChanged?.Invoke(__instance);
        return false;
    }
    [HarmonyPatch(nameof(ToggleOption.Initialize)), HarmonyPrefix]
    private static bool InitializePrefix(ToggleOption __instance)
    {
        if (ModGameOptionsMenu.OptionList.TryGetValue(__instance, out var index))
        {
            var item = OptionItem.AllOptions[index];
            //Logger.Info($"{item.Name}, {index}", "ToggleOption.Initialize.TryGetValue");
            __instance.TitleText.text = item.GetName();
            __instance.CheckMark.enabled = item.GetBool();
            return false;
        }
        return true;
    }
    [HarmonyPatch(nameof(ToggleOption.UpdateValue)), HarmonyPrefix]
    private static bool UpdateValuePrefix(ToggleOption __instance)
    {
        if (ModGameOptionsMenu.OptionList.TryGetValue(__instance, out var index))
        {
            var item = OptionItem.AllOptions[index];
            //Logger.Info($"{item.Name}, {index}", "ToggleOption.UpdateValue.TryGetValue");
            if (!GameOptionsMenuPatch.CanEdit)
            {
                __instance.CheckMark.enabled = item.GetBool();
                return false;
            }
            item.SetValue(__instance.GetBool() ? 1 : 0);
            NotificationPopperPatch.AddSettingsChangeMessage(index, item, false);
            return false;
        }
        return true;
    }
}
[HarmonyPatch(typeof(NumberOption))]
public static class NumberOptionPatch
{
    private static int IncrementMultiplier
    {
        get
        {
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) return 5;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) return 10;
            return 1;
        }
    }
    [HarmonyPatch(nameof(NumberOption.SetUpFromData)), HarmonyPrefix]
    private static void SetUpFromDataPrefix(BaseGameSetting data)
    {
        if (data != null && data.Type == OptionTypes.Int &&
            data.Cast<IntGameSetting>().OptionName is AmongUs.GameOptions.Int32OptionNames.MaxPlayers or AmongUs.GameOptions.Int32OptionNames.NumImpostors)
            ServerLimits.Refresh();
    }
    [HarmonyPatch(nameof(NumberOption.Initialize)), HarmonyPrefix]
    private static bool InitializePrefix(NumberOption __instance)
    {
        switch (__instance.Title)
        {
            case StringNames.GameVotingTime:
                __instance.ValidRange = new(0, 600);
                __instance.Value = (float)Math.Round(__instance.Value, 2);
                break;
            case StringNames.GameShortTasks:
            case StringNames.GameLongTasks:
            case StringNames.GameCommonTasks:
                __instance.ValidRange = new(0, 90);
                __instance.Value = (float)Math.Round(__instance.Value, 2);
                break;
            case StringNames.GameKillCooldown:
                __instance.ValidRange = new(0, 180);
                __instance.Increment = 0.5f;
                __instance.Value = (float)Math.Round(__instance.Value, 2);
                break;
            case StringNames.GamePlayerSpeed:
            case StringNames.GameCrewLight:
            case StringNames.GameImpostorLight:
                __instance.Increment = 0.05f;
                __instance.Value = (float)Math.Round(__instance.Value, 2);
                break;
            case StringNames.GameNumImpostors:
                __instance.ValidRange.min = ServerLimits.MinImpostors;
                __instance.ValidRange.max = ServerLimits.MaxImpostors(GameOptionsManager.Instance.CurrentGameOptions.MaxPlayers);
                break;
        }

        if (__instance.intOptionName == AmongUs.GameOptions.Int32OptionNames.MaxPlayers)
        {
            __instance.ValidRange.min = ServerLimits.MinPlayers;
            __instance.ValidRange.max = ServerLimits.MaxPlayers;
        }

        if (ModGameOptionsMenu.OptionList.TryGetValue(__instance, out var index))
        {
            var item = OptionItem.AllOptions[index];
            if (item == Options.NumImpostorsHnS && GameStates.IsHideNSeek)
            {
                int players = GameOptionsManager.Instance.CurrentGameOptions.MaxPlayers;
                if (GameOptionsMenuPatch.CanEdit) ServerLimits.NormalizeHideNSeekImpostors(players);
                __instance.ValidRange.max = Math.Min(3, players / 2);
            }
            __instance.TitleText.text = item.GetName();
            __instance.Value = item.GetFloat();
            __instance.ValueText.text = item.GetString();
            return false;
        }

        return true;
    }
    [HarmonyPatch(nameof(NumberOption.UpdateValue)), HarmonyPrefix]
    private static bool UpdateValuePrefix(NumberOption __instance)
    {
        if (ModGameOptionsMenu.OptionList.TryGetValue(__instance, out var index))
        {
            var item = OptionItem.AllOptions[index];
            if (!GameOptionsMenuPatch.CanEdit)
            {
                __instance.Value = item.GetFloat();
                return false;
            }
            //Logger.Info($"{item.Name}, {index}", "NumberOption.UpdateValue.TryGetValue");

            if (item is IntegerOptionItem integerOptionItem)
            {
                integerOptionItem.SetValue(integerOptionItem.Rule.GetNearestIndex(__instance.GetInt()));
            }
            else if (item is FloatOptionItem floatOptionItem)
            {
                floatOptionItem.SetValue(floatOptionItem.Rule.GetNearestIndex(__instance.GetFloat()));
            }
            NotificationPopperPatch.AddSettingsChangeMessage(index, item, false);
            return false;
        }
        return true;
    }
    [HarmonyPatch(nameof(NumberOption.FixedUpdate)), HarmonyPrefix]
    private static bool FixedUpdatePrefix(NumberOption __instance)
    {
        if (ModGameOptionsMenu.OptionList.TryGetValue(__instance, out var index))
        {
            __instance.MinusBtn.SetInteractable(GameOptionsMenuPatch.CanEdit);
            __instance.PlusBtn.SetInteractable(GameOptionsMenuPatch.CanEdit);

            if (__instance.oldValue != __instance.Value)
            {
                __instance.oldValue = __instance.Value;
                __instance.ValueText.text = GetValueString(__instance, __instance.Value, OptionItem.AllOptions[index]);
            }
            return false;
        }
        return true;
    }
    public static string GetValueString(NumberOption __instance, float value, OptionItem item)
    {
        if (__instance.ZeroIsInfinity && Mathf.Abs(value) < 0.0001f) return "<b>∞</b>";
        return item == null ? value.ToString(__instance.FormatString) : item.GetString();
    }
    [HarmonyPatch(nameof(NumberOption.Increase)), HarmonyPrefix]
    public static bool IncreasePrefix(NumberOption __instance)
    {
        if (ModGameOptionsMenu.OptionList.ContainsKey(__instance))
        {
            if (!GameOptionsMenuPatch.CanEdit) return false;
            if (__instance.Value == __instance.ValidRange.max) __instance.Value = __instance.ValidRange.min;
            else
            {
                var modIncrement = IncrementMultiplier * __instance.Increment;
                __instance.Value = __instance.Value + modIncrement < __instance.ValidRange.max
                    ? __instance.Value + modIncrement : __instance.ValidRange.Clamp(__instance.Value + __instance.Increment);
            }
            UpdateValuePrefix(__instance);
            __instance.OnValueChanged?.Invoke(__instance);
            return false;
        }
        if (__instance.Value == __instance.ValidRange.max)
        {
            __instance.Value = __instance.ValidRange.min;
            __instance.UpdateValue();
            __instance.OnValueChanged.Invoke(__instance);
            __instance.AdjustButtonsActiveState();
            return false;
        }

        var increment = IncrementMultiplier * __instance.Increment;
        if (__instance.Value + increment < __instance.ValidRange.max)
        {
            __instance.Value += increment;
            __instance.UpdateValue();
            __instance.OnValueChanged.Invoke(__instance);
            __instance.AdjustButtonsActiveState();
            return false;
        }

        return true;
    }
    [HarmonyPatch(nameof(NumberOption.Decrease)), HarmonyPrefix]
    public static bool DecreasePrefix(NumberOption __instance)
    {
        if (ModGameOptionsMenu.OptionList.ContainsKey(__instance))
        {
            if (!GameOptionsMenuPatch.CanEdit) return false;
            if (__instance.Value == __instance.ValidRange.min) __instance.Value = __instance.ValidRange.max;
            else
            {
                var modIncrement = IncrementMultiplier * __instance.Increment;
                __instance.Value = __instance.Value - modIncrement > __instance.ValidRange.min
                    ? __instance.Value - modIncrement : __instance.ValidRange.Clamp(__instance.Value - __instance.Increment);
            }
            UpdateValuePrefix(__instance);
            __instance.OnValueChanged?.Invoke(__instance);
            return false;
        }
        if (__instance.Value == __instance.ValidRange.min)
        {
            __instance.Value = __instance.ValidRange.max;
            __instance.UpdateValue();
            __instance.OnValueChanged.Invoke(__instance);
            __instance.AdjustButtonsActiveState();
            return false;
        }

        var increment = IncrementMultiplier * __instance.Increment;
        if (__instance.Value - increment > __instance.ValidRange.min)
        {
            __instance.Value -= increment;
            __instance.UpdateValue();
            __instance.OnValueChanged.Invoke(__instance);
            __instance.AdjustButtonsActiveState();
            return false;
        }

        return true;
    }
}
[HarmonyPatch(typeof(StringOption))]
public static class StringOptionPatch
{
    [HarmonyPatch(nameof(StringOption.Initialize)), HarmonyPrefix]
    private static bool InitializePrefix(StringOption __instance)
    {
        if (ModGameOptionsMenu.OptionList.TryGetValue(__instance, out var index))
        {
            var item = OptionItem.AllOptions[index];
            var name = item.GetName();
            var name1 = name;
            var language = DestroyableSingleton<TranslationController>.Instance.currentLanguage.languageID;
            //Logger.Info($" Language: {language}", "StringOption.Initialize");

            if (EnumHelper.GetAllValues<CustomRoles>().Find(x => GetString($"{x}") == name1.RemoveHtmlTags(), out var role))
            {
                name = $"<size=3.5>{name}</size>";
                __instance.TitleText.fontWeight = FontWeight.Black;
                __instance.TitleText.outlineWidth = language switch
                {
                    SupportedLangs.Russian or SupportedLangs.Japanese or SupportedLangs.SChinese or SupportedLangs.TChinese => 0.15f,
                    _ => 0.35f,
                };

                SetupHelpIcon(role, __instance);
            }
            __instance.TitleText.text = name;
            __instance.Value = item.CurrentValue;
            __instance.ValueText.text = item.GetString();
            return false;
        }
        return true;
    }

    //Credit For SetupHelpIcon to EHR https://github.com/Gurge44/EndlessHostRoles/blob/main/Patches/GameOptionsMenuPatch.cs
    private static void SetupHelpIcon(CustomRoles role, StringOption __instance)
    {
        if (__instance.transform.Find($"{role}HelpIcon") != null) return;
        var template = __instance.transform.FindChild("MinusButton");
        if (template == null) return;
        var icon = Object.Instantiate(template, template.parent, true);
        icon.gameObject.SetActive(true);
        icon.name = $"{role}HelpIcon";
        var text = icon.GetComponentInChildren<TextMeshPro>();
        text.text = "?";
        text.color = Color.white;
        _ = ColorUtility.TryParseHtmlString("#117055", out var clr);
        _ = ColorUtility.TryParseHtmlString("#33d6a3", out var clr2);
        icon.FindChild("ButtonSprite").GetComponent<SpriteRenderer>().color = clr;
        var GameOptionsButton = icon.GetComponent<GameOptionButton>();
        GameOptionsButton.OnClick = new();
        GameOptionsButton.OnClick.AddListener((UnityEngine.Events.UnityAction)(() =>
        {
            if (__instance == null || GameSettingMenu.Instance == null) return;
            if (ModGameOptionsMenu.OptionList.TryGetValue(__instance, out var index))
            {
                var item = OptionItem.AllOptions[index];
                var name = item.GetName();
                if (Enum.GetValues<CustomRoles>().Find(x => GetString($"{x}") == name.RemoveHtmlTags(), out var role))
                {
                    var roleName = role == CustomRoles.DetectiveVanilla ? nameof(CustomRoles.DetectiveTOHE)
                        : role.IsVanilla() ? role + "TOHE" : role.ToString();
                    var str = GetString($"{roleName}InfoLong");
                    int size = str.Length > 500 ? str.Length > 550 ? 65 : 70 : 100;
                    var infoLong = str[(str.IndexOf('\n') + 1)..str.Length];
                    var ColorRole = Utils.ColorString(Utils.GetRoleColor(role), GetString(role.ToString()));
                    var info = $"<size={size}%>{ColorRole}: {infoLong}</size>";
                    GameSettingMenu.Instance.MenuDescriptionText.text = info;
                }
            }
        }));
        GameOptionsButton.interactableColor = clr;
        GameOptionsButton.interactableHoveredColor = clr2;
        icon.localPosition += new Vector3(-0.8f, 0f, 0f);
        icon.SetAsLastSibling();

    }

    [HarmonyPatch(nameof(StringOption.UpdateValue)), HarmonyPrefix]
    private static bool UpdateValuePrefix(StringOption __instance)
    {
        if (ModGameOptionsMenu.OptionList.TryGetValue(__instance, out var index))
        {
            var item = OptionItem.AllOptions[index];
            if (!GameOptionsMenuPatch.CanEdit)
            {
                __instance.Value = item.CurrentValue;
                return false;
            }
            //Logger.Info($"{item.Name}, {index}", "StringOption.UpdateValue.TryAdd");

            item.SetValue(__instance.GetInt());
            NotificationPopperPatch.AddSettingsChangeMessage(index, item, false);

            if (item is PresetOptionItem || (item is StringOptionItem && item.Name == "GameMode"))
            {
                if (Options.GameMode.GetInt() == 2 && !GameStates.IsHideNSeek) //Hide And Seek
                {
                    Options.GameMode.SetValue(0);
                }
                else if (Options.GameMode.GetInt() != 2 && GameStates.IsHideNSeek)
                {
                    Options.GameMode.SetValue(2);
                }
                GameOptionsMenuPatch.ReOpenSettings(item.Name != "GameMode" ? 1 : 4);
            }
            return false;
        }
        return true;
    }
    [HarmonyPatch(nameof(StringOption.FixedUpdate)), HarmonyPrefix]
    private static bool FixedUpdatePrefix(StringOption __instance)
    {
        if (ModGameOptionsMenu.OptionList.TryGetValue(__instance, out var index))
        {
            var item = OptionItem.AllOptions[index];
            __instance.MinusBtn.SetInteractable(GameOptionsMenuPatch.CanEdit);
            __instance.PlusBtn.SetInteractable(GameOptionsMenuPatch.CanEdit);

            if (item is StringOptionItem stringOptionItem)
            {
                if (__instance.oldValue != __instance.Value)
                {
                    __instance.oldValue = __instance.Value;
                    __instance.ValueText.text = stringOptionItem.GetString();
                }
            }
            else if (item is PresetOptionItem presetOptionItem)
            {
                if (__instance.oldValue != __instance.Value)
                {
                    __instance.oldValue = __instance.Value;
                    __instance.ValueText.text = presetOptionItem.GetString();
                }
            }
            return false;
        }
        return true;
    }
    [HarmonyPatch(nameof(StringOption.Increase)), HarmonyPrefix]
    public static bool IncreasePrefix(StringOption __instance)
    {
        if (ModGameOptionsMenu.OptionList.ContainsKey(__instance))
        {
            if (!GameOptionsMenuPatch.CanEdit) return false;
            __instance.Value = __instance.Value == __instance.Values.Length - 1 ? 0 : __instance.Value + 1;
            UpdateValuePrefix(__instance);
            __instance.OnValueChanged?.Invoke(__instance);
            return false;
        }
        if (__instance.Value == __instance.Values.Length - 1)
        {
            __instance.Value = 0;
            __instance.UpdateValue();
            __instance.OnValueChanged?.Invoke(__instance);
            return false;
        }
        return true;
    }
    [HarmonyPatch(nameof(StringOption.Decrease)), HarmonyPrefix]
    public static bool DecreasePrefix(StringOption __instance)
    {
        if (ModGameOptionsMenu.OptionList.ContainsKey(__instance))
        {
            if (!GameOptionsMenuPatch.CanEdit) return false;
            __instance.Value = __instance.Value == 0 ? __instance.Values.Length - 1 : __instance.Value - 1;
            UpdateValuePrefix(__instance);
            __instance.OnValueChanged?.Invoke(__instance);
            return false;
        }
        if (__instance.Value == 0)
        {
            __instance.Value = __instance.Values.Length - 1;
            __instance.UpdateValue();
            __instance.OnValueChanged?.Invoke(__instance);
            return false;
        }
        return true;
    }
}
