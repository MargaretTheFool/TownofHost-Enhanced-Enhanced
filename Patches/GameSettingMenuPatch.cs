using System;
using TMPro;
using UnityEngine;
using static TOHE.Translator;
using Object = UnityEngine.Object;

namespace TOHE;

// Thanks: https://github.com/Yumenopai/TownOfHost_Y/blob/main/Patches/GameSettingMenuPatch.cs
[HarmonyPatch(typeof(GameSettingMenu))]
public class GameSettingMenuPatch
{
    private const float TabRowSpacing = 0.5f;
    private static readonly Vector3 ButtonPositionLeft = new(-3.9f, -0.4f, 0f);
    private static readonly Vector3 ButtonPositionRight = new(-2.4f, -0.4f, 0f);

    private static readonly Vector3 ButtonSize = new(0.45f, 0.6f, 1f);

    private static GameOptionsMenu TemplateGameOptionsMenu;
    private static PassiveButton TemplateGameSettingsButton;
    private static GameObject TemplateRoot;

    static Dictionary<TabGroup, PassiveButton> ModSettingsButtons = [];
    static Dictionary<TabGroup, GameOptionsMenu> ModSettingsTabs = [];
    public static GameSettingMenu Instance;

    private static void CreateModTabs(GameSettingMenu __instance)
    {
        Instance = __instance;

        TabGroup[] ExludeList = Options.CurrentGameMode switch
        {
            CustomGameMode.HidenSeekTOHE => Enum.GetValues<TabGroup>().Skip(3).ToArray(),
            CustomGameMode.FFA => Enum.GetValues<TabGroup>().Skip(2).ToArray(),
            _ => []
        };

        // https://gyazo.com/a8f6ec93e44eca8e6febb7d2e91c3750 So much empy space, I think this definetevly calls for HnS roles 😼

        ModSettingsButtons = [];
        foreach (var tab in EnumHelper.GetAllValues<TabGroup>().Except(ExludeList))
        {
            var button = Object.Instantiate(TemplateGameSettingsButton, __instance.GameSettingsButton.transform.parent);
            button.gameObject.SetActive(true);
            button.name = "Button_" + tab;
            var label = button.GetComponentInChildren<TextMeshPro>();
            label.DestroyTranslator();
            string htmlcolor = tab switch
            {
                TabGroup.SystemSettings => Main.ModColor,
                TabGroup.ModSettings => "#59ef83",
                TabGroup.ModifierSettings => "#EF59AF",
                TabGroup.ImpostorRoles => "#f74631",
                TabGroup.CrewmateRoles => "#8cffff",
                TabGroup.NeutralRoles => "#7f8c8d",
                TabGroup.Addons => "#ff9ace",
                _ => "#ffffff",
            };
            label.fontStyle = FontStyles.UpperCase;
            label.text = $"<color={htmlcolor}>{GetString("TabGroup." + tab)}</color>";

            _ = ColorUtility.TryParseHtmlString(htmlcolor, out Color tabColor);
            button.inactiveSprites.GetComponent<SpriteRenderer>().color = tabColor;
            button.activeSprites.GetComponent<SpriteRenderer>().color = tabColor;
            button.selectedSprites.GetComponent<SpriteRenderer>().color = tabColor;

            Vector3 offset = new(0.0f, TabRowSpacing * (((int)tab + 1) / 2), 0.0f);
            button.transform.localPosition = ((((int)tab + 1) % 2 == 0) ? ButtonPositionLeft : ButtonPositionRight) - offset;
            button.transform.localScale = ButtonSize;

            var buttonComponent = button.GetComponent<PassiveButton>();
            buttonComponent.OnClick = new();
            buttonComponent.OnMouseOver = new();
            buttonComponent.OnMouseOver.AddListener(
                (UnityEngine.Events.UnityAction)(() => __instance.ChangeTab((int)tab + 3, true)));
            buttonComponent.OnClick.AddListener(
                (UnityEngine.Events.UnityAction)(() => __instance.ChangeTab((int)tab + 3, false)));

            ModSettingsButtons.Add(tab, button);
        }

        ModSettingsTabs = [];
        foreach (var tab in EnumHelper.GetAllValues<TabGroup>())
        {
            var setTab = Object.Instantiate(TemplateGameOptionsMenu, __instance.GameSettingsTab.transform.parent);
            setTab.name = "tab_" + tab;
            setTab.gameObject.SetActive(false);

            ModSettingsTabs.Add(tab, setTab);
            GameOptionsMenuPatch.RegisterTab(setTab, tab);
        }

        foreach (var tab in EnumHelper.GetAllValues<TabGroup>())
        {
            if (ModSettingsButtons.TryGetValue(tab, out var button))
            {
                __instance.ControllerSelectable.Add(button);
            }
        }

        HiddenBySearch.Do(x => x.SetHidden(false));
        HiddenBySearch.Clear();

        SetupAdittionalButtons(__instance);
    }
    private static void SetDefaultButton(GameSettingMenu __instance)
    {
        __instance.GamePresetsButton.gameObject.SetActive(false);

        var gameSettingButton = __instance.GameSettingsButton;
        gameSettingButton.transform.localPosition = new(-3f, -0.5f, 0f);

        var textLabel = gameSettingButton.GetComponentInChildren<TextMeshPro>();
        textLabel.DestroyTranslator();
        textLabel.fontStyle = FontStyles.UpperCase;
        textLabel.text = GetString("TabVanilla.GameSettings");
        //gameSettingButton.activeTextColor = gameSettingButton.inactiveTextColor = Color.black;
        //gameSettingButton.selectedTextColor = Color.blue;

        //var vanillaActiveButton = Utils.LoadSprite($"TownOfHost_Y.Resources.Tab_Active_VanillaGameSettings.png", 100f);
        //gameSettingButton.inactiveSprites.GetComponent<SpriteRenderer>().sprite = Utils.LoadSprite($"TownOfHost_Y.Resources.Tab_Small_VanillaGameSettings.png", 100f);
        //gameSettingButton.activeSprites.GetComponent<SpriteRenderer>().sprite = vanillaActiveButton;
        //gameSettingButton.selectedSprites.GetComponent<SpriteRenderer>().sprite = vanillaActiveButton;

        gameSettingButton.transform.localPosition = ButtonPositionLeft;
        gameSettingButton.transform.localScale = ButtonSize;

        __instance.RoleSettingsButton.gameObject.SetActive(false);

        __instance.DefaultButtonSelected = gameSettingButton;
        __instance.ControllerSelectable = new();
        __instance.ControllerSelectable.Add(gameSettingButton);
    }
    public static StringOption PresetBehaviour;
    private static GameObject PresetSelectorRoot;
    private static TextMeshPro PresetLabel;
    public static FreeChatInputField InputField;
    public static List<OptionItem> HiddenBySearch = [];
    public static Action _SearchForOptions;

    private static void SetupAdittionalButtons(GameSettingMenu __instance)
    {
        var ParentLeftPanel = __instance.GamePresetsButton.transform.parent;

        // The sidebar label is outside the settings scroller's stencil mask.
        var valueTemplate = __instance.GamePresetsButton.GetComponentInChildren<TextMeshPro>(true);
        var presetBackground = CreatePresetSelector(__instance, valueTemplate);

        var GameSettingsLabel = __instance.GameSettingsButton.transform.parent.parent.FindChild("GameSettingsLabel").GetComponent<TextMeshPro>();
        GameSettingsLabel.DestroyTranslator();
        GameSettingsLabel.text = GetString($"{Options.CurrentGameMode}");

        var FreeChatField = DestroyableSingleton<ChatController>.Instance.freeChatField;
        var TextField = Object.Instantiate(FreeChatField, ParentLeftPanel.parent);
        TextField.transform.localScale = new Vector3(0.3f, 0.59f, 1);
        TextField.transform.localPosition = new Vector3(-2.07f, -2.57f, -5f);
        TextField.textArea.outputText.transform.localScale = new Vector3(3.5f, 2f, 1f);
        TextField.textArea.outputText.font = valueTemplate.font;
        TextField.name = "InputField";

        // Both controls share a row, but the search field belongs to a different
        // parent. Align their rendered bounds in world space to avoid overlap.
        var searchBounds = TextField.Background.bounds;
        var presetBounds = presetBackground.bounds;
        float gap = ParentLeftPanel.TransformVector(new Vector3(0.12f, 0f, 0f)).magnitude;
        TextField.transform.position += new Vector3(
            presetBounds.max.x + gap - searchBounds.min.x,
            presetBounds.center.y - searchBounds.center.y, 0f);
        InputField = TextField;


        var button = TextField.transform.FindChild("ChatSendButton");

        Object.Destroy(button.FindChild("Normal").FindChild("Icon").GetComponent<SpriteRenderer>());
        Object.Destroy(button.FindChild("Hover").FindChild("Icon").GetComponent<SpriteRenderer>());
        Object.Destroy(button.FindChild("Disabled").FindChild("Icon").GetComponent<SpriteRenderer>());
        Object.Destroy(button.transform.FindChild("Text").GetComponent<TextMeshPro>());

        button.FindChild("Normal").FindChild("Background").GetComponent<SpriteRenderer>().sprite = Utils.LoadSprite("TOHE.Resources.Images.SearchIconActive.png", 100f);
        button.FindChild("Hover").FindChild("Background").GetComponent<SpriteRenderer>().sprite = Utils.LoadSprite("TOHE.Resources.Images.SearchIconHover.png", 100f);
        button.FindChild("Disabled").FindChild("Background").GetComponent<SpriteRenderer>().sprite = Utils.LoadSprite("TOHE.Resources.Images.SearchIcon.png", 100f);

        if (DestroyableSingleton<TranslationController>.Instance.currentLanguage.languageID == SupportedLangs.Russian)
        {
            Vector3 FixedScale = new(0.7f, 1f, 1f);
            button.FindChild("Normal").FindChild("Background").transform.localScale = FixedScale;
            button.FindChild("Hover").FindChild("Background").transform.localScale = FixedScale;
            button.FindChild("Disabled").FindChild("Background").transform.localScale = FixedScale;
        }

        PassiveButton passiveButton = button.GetComponent<PassiveButton>();

        passiveButton.OnClick = new();
        passiveButton.OnClick.AddListener(
                (UnityEngine.Events.UnityAction)(() =>
                {
                    SearchForOptions(TextField);
                }));

        _SearchForOptions = (() =>
        {
            if (TextField.textArea.text == string.Empty)
                return;

            passiveButton.ReceiveClickDown();
        });

        static void SearchForOptions(FreeChatInputField textField)
        {
            if (ModGameOptionsMenu.TabIndex < 3) return;

            HiddenBySearch.Do(x => x.SetHidden(false));
            string text = textField.textArea.text.Trim().ToLower();
            var Result = OptionItem.AllOptions.Where(x => x.Parent == null && !x.IsHiddenOn(Options.CurrentGameMode)
            && !GetString($"{x.Name}").ToLower().Contains(text) && x.Tab == (TabGroup)(ModGameOptionsMenu.TabIndex - 3)).ToList();
            HiddenBySearch = Result;
            var SearchWinners = OptionItem.AllOptions.Where(x => x.Parent == null && !x.IsHiddenOn(Options.CurrentGameMode) && x.Tab == (TabGroup)(ModGameOptionsMenu.TabIndex - 3) && !Result.Contains(x)).ToList();
            if (!SearchWinners.Any() || !ModSettingsTabs.TryGetValue((TabGroup)(ModGameOptionsMenu.TabIndex - 3), out var settingsTab) || settingsTab == null)
            {
                HiddenBySearch.Clear();
                Logger.SendInGame(GetString("SearchNoResult")); // okay so showpopup nor this will overlay the menu, but I use this just for the sound lol
                return;
            }

            Result.Do(x => x.SetHidden(true));

            GameOptionsMenuPatch.ReCreateSettings(settingsTab);
            textField.Clear();
        }
    }

    private static SpriteRenderer CreatePresetSelector(GameSettingMenu menu, TextMeshPro textTemplate)
    {
        var parent = menu.GamePresetsButton.transform.parent;
        // Measure the two columns rather than carrying coordinates/scales from
        // the old ModeValue prefab into the current game's menu.
        var sidebarBounds = new Bounds();
        bool hasBounds = false;
        foreach (var button in ModSettingsButtons.Values)
        {
            var sprite = button.inactiveSprites.GetComponent<SpriteRenderer>();
            var bounds = sprite.localBounds;
            var min = parent.InverseTransformPoint(sprite.transform.TransformPoint(bounds.min));
            var max = parent.InverseTransformPoint(sprite.transform.TransformPoint(bounds.max));
            if (!hasBounds)
            {
                sidebarBounds = new Bounds(min, Vector3.zero);
                hasBounds = true;
            }
            sidebarBounds.Encapsulate(min);
            sidebarBounds.Encapsulate(max);
        }

        const float height = 0.5f;
        const float arrowWidth = 0.48f;
        float width = sidebarBounds.size.x;
        int lastRow = Enum.GetValues<TabGroup>().Max(tab => ((int)tab + 1) / 2);
        int visibleLastRow = ModSettingsButtons.Keys.Max(tab => ((int)tab + 1) / 2);
        // Keep the preset/search row at the same bottom position in every game mode.
        float sidebarBottom = sidebarBounds.min.y - TabRowSpacing * (lastRow - visibleLastRow);
        var root = new GameObject("TOHEPresetSelector");
        PresetSelectorRoot = root;
        root.layer = menu.GamePresetsButton.gameObject.layer;
        root.transform.SetParent(parent, false);
        root.transform.localPosition = new Vector3(sidebarBounds.center.x,
            sidebarBottom - 0.08f - height / 2f, -2f);

        var backgroundObject = new GameObject("Background");
        backgroundObject.layer = root.layer;
        backgroundObject.transform.SetParent(root.transform, false);
        var background = backgroundObject.AddComponent<SpriteRenderer>();
        background.sprite = PresetSelectorVisuals.Panel;
        background.drawMode = SpriteDrawMode.Sliced;
        background.size = new Vector2(width, height);

        PresetLabel = Object.Instantiate(textTemplate, root.transform);
        ConfigurePresetText(PresetLabel, GetString($"Preset_{OptionItem.CurrentPreset + 1}"),
            new Vector2(width - 2f * arrowWidth - 0.16f, height), 2.8f);
        PresetLabel.color = new Color32(211, 234, 234, 255);
        PresetLabel.outlineWidth = 0.05f;
        PresetLabel.enableAutoSizing = true;
        PresetLabel.fontSizeMin = 2.2f;
        CenterPresetText(PresetLabel);

        CreateArrow(-1);
        CreateArrow(1);
        return background;

        void CreateArrow(int direction)
        {
            var button = Object.Instantiate(menu.GamePresetsButton, root.transform);
            button.name = direction < 0 ? "PreviousPreset" : "NextPreset";
            foreach (var anchor in button.GetComponentsInChildren<AspectPosition>(true))
            {
                anchor.enabled = false;
                Object.Destroy(anchor);
            }
            button.transform.localScale = Vector3.one;
            button.transform.localPosition = new Vector3(direction * (width - arrowWidth) / 2f, 0f, -1f);
            PresetSelectorVisuals.StyleArrow(button, direction > 0, 0.36f);
            var collider = button.GetComponent<BoxCollider2D>();
            collider.offset = Vector2.zero;
            collider.size = new Vector2(arrowWidth, height);
            button.OnClick = new();
            button.OnClick.AddListener((UnityEngine.Events.UnityAction)(() => ChangePreset(menu, direction)));
            button.gameObject.SetActive(GameOptionsMenuPatch.CanEdit);
            if (GameOptionsMenuPatch.CanEdit) menu.ControllerSelectable.Add(button);
        }
    }

    private static void ConfigurePresetText(TextMeshPro label, string caption, Vector2 size, float fontSize)
    {
        label.DestroyTranslator();
        var anchor = label.GetComponent<AspectPosition>();
        if (anchor != null)
        {
            anchor.enabled = false;
            Object.Destroy(anchor);
        }
        label.gameObject.SetActive(true);
        label.transform.localScale = Vector3.one;
        label.transform.localPosition = new Vector3(0f, 0f, -1f);
        label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        label.rectTransform.sizeDelta = size;
        label.margin = Vector4.zero;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.enableAutoSizing = false;
        label.fontSize = label.fontSizeMin = label.fontSizeMax = fontSize;
        label.text = caption;
    }

    private static void CenterPresetText(TextMeshPro label)
    {
        // Align visible glyphs to the row center, independent of font baselines.
        label.ForceMeshUpdate(true, true);
        var center = label.textBounds.center;
        label.transform.localPosition = new Vector3(-center.x, -center.y, -1f);
    }

    private static void ChangePreset(GameSettingMenu menu, int delta)
    {
        if (menu == null || !GameOptionsMenuPatch.CanEdit ||
            !OptionItem.FastOptions.TryGetValue(OptionItem.PresetId, out var preset)) return;
        var previousMode = Options.CurrentGameMode;
        preset.SetValue(preset.CurrentValue + delta);
        if (Options.GameMode.GetInt() == 2 && !GameStates.IsHideNSeek)
            Options.GameMode.SetValue(0);
        else if (Options.GameMode.GetInt() != 2 && GameStates.IsHideNSeek)
            Options.GameMode.SetValue(2);
        if (previousMode != Options.CurrentGameMode)
        {
            GameOptionsMenuPatch.ReOpenSettings(1);
            return;
        }
        foreach (var tab in ModSettingsTabs.Values)
        {
            if (tab == null || tab.Children == null) continue;
            foreach (var child in tab.Children.ToArray())
                if (child != null) child.Initialize();
            GameOptionsMenuPatch.ReCreateSettings(tab);
        }
        if (PresetLabel != null)
        {
            PresetLabel.text = preset.GetString();
            CenterPresetText(PresetLabel);
        }
        if (InputField != null) InputField.Clear();
    }

    [HarmonyPatch(nameof(GameSettingMenu.ChangeTab)), HarmonyPrefix]
    public static bool ChangeTabPrefix(GameSettingMenu __instance, ref int tabNum, [HarmonyArgument(1)] bool previewOnly)
    {
        if (HiddenBySearch.Any())
        {
            HiddenBySearch.Do(x => x.SetHidden(false));
            if (ModSettingsTabs.TryGetValue((TabGroup)(ModGameOptionsMenu.TabIndex - 3), out var GameSettingsTab) && GameSettingsTab != null)
                GameOptionsMenuPatch.ReCreateSettings(GameSettingsTab);

            HiddenBySearch.Clear();
        }

        if (!previewOnly || tabNum != 1) ModGameOptionsMenu.TabIndex = tabNum;

        GameOptionsMenu settingsTab;
        PassiveButton button;

        if ((previewOnly && Controller.currentTouchType == Controller.TouchType.Joystick) || !previewOnly)
        {
            foreach (var tab in EnumHelper.GetAllValues<TabGroup>())
            {
                if (ModSettingsTabs.TryGetValue(tab, out settingsTab) &&
                    settingsTab != null)
                {
                    GameOptionsMenuPatch.HideModMenu(settingsTab);
                }
            }
            foreach (var tab in EnumHelper.GetAllValues<TabGroup>())
            {
                if (ModSettingsButtons.TryGetValue(tab, out button) &&
                    button != null)
                {
                    button.SelectButton(false);
                }
            }
        }

        if (tabNum < 3) return true;

        var tabGroupId = (TabGroup)(tabNum - 3);
        if ((previewOnly && Controller.currentTouchType == Controller.TouchType.Joystick) || !previewOnly)
        {
            __instance.PresetsTab.gameObject.SetActive(false);
            __instance.GameSettingsTab.gameObject.SetActive(false);
            __instance.RoleSettingsTab.gameObject.SetActive(false);
            __instance.GamePresetsButton.SelectButton(false);
            __instance.GameSettingsButton.SelectButton(false);
            __instance.RoleSettingsButton.SelectButton(false);

            if (ModSettingsTabs.TryGetValue(tabGroupId, out settingsTab) && settingsTab != null)
            {
                settingsTab.gameObject.SetActive(true);
                __instance.MenuDescriptionText.DestroyTranslator();
                switch (tabGroupId)
                {
                    case TabGroup.SystemSettings:
                    case TabGroup.ModSettings:
                    case TabGroup.ModifierSettings:
                        __instance.MenuDescriptionText.text = GetString("TabMenuDescription_General");
                        break;
                    case TabGroup.ImpostorRoles:
                    case TabGroup.CrewmateRoles:
                    case TabGroup.NeutralRoles:
                    case TabGroup.Addons:
                        __instance.MenuDescriptionText.text = GetString("TabMenuDescription_Roles&AddOns");
                        break;
                }
            }
        }

        if (previewOnly)
        {
            __instance.ToggleLeftSideDarkener(false);
            __instance.ToggleRightSideDarkener(true);
            return false;
        }
        __instance.ToggleLeftSideDarkener(true);
        __instance.ToggleRightSideDarkener(false);

        if (ModSettingsButtons.TryGetValue(tabGroupId, out button) &&
            button != null)
        {
            button.SelectButton(true);
        }
        if (ModSettingsTabs.TryGetValue(tabGroupId, out settingsTab) && settingsTab != null)
            GameOptionsMenuPatch.OpenModMenu(settingsTab);

        return false;
    }

    [HarmonyPatch(nameof(GameSettingMenu.OnEnable)), HarmonyPrefix]
    private static bool OnEnablePrefix(GameSettingMenu __instance)
    {

        if (TemplateGameOptionsMenu == null)
        {
            // Instantiate below an inactive parent: cloning an active native tab
            // otherwise runs OnEnable/Initialize before its copied rows are cleaned.
            TemplateRoot = new GameObject("TOHESettingsTemplates");
            TemplateRoot.SetActive(false);
            TemplateRoot.transform.SetParent(__instance.transform, false);
            TemplateGameOptionsMenu = Object.Instantiate(__instance.GameSettingsTab, TemplateRoot.transform);
            TemplateGameOptionsMenu.gameObject.SetActive(false);
            // The vanilla tab can already contain runtime rows. Unity clones their
            // objects but not OptionBehaviour's nonserialized data binding.
            // Detach them before copying this template into each custom tab.
            foreach (var row in TemplateGameOptionsMenu.settingsContainer.GetComponentsInChildren<OptionBehaviour>(true))
            {
                if (row == TemplateGameOptionsMenu.checkboxOrigin || row == TemplateGameOptionsMenu.numberOptionOrigin
                    || row == TemplateGameOptionsMenu.stringOptionOrigin || row == TemplateGameOptionsMenu.playerOptionOrigin
                    || row == TemplateGameOptionsMenu.MapPicker) continue;
                row.gameObject.SetActive(false);
                row.transform.SetParent(null);
                Object.Destroy(row.gameObject);
            }
            foreach (var header in TemplateGameOptionsMenu.settingsContainer.GetComponentsInChildren<CategoryHeaderMasked>(true))
            {
                if (header == TemplateGameOptionsMenu.categoryHeaderOrigin) continue;
                header.gameObject.SetActive(false);
                header.transform.SetParent(null);
                Object.Destroy(header.gameObject);
            }
            TemplateGameOptionsMenu.Children = new(1);
            TemplateGameOptionsMenu.ControllerSelectable.Clear();
            TemplateGameOptionsMenu.cachedData = null;
        }
        if (TemplateGameSettingsButton == null)
        {
            TemplateGameSettingsButton = Object.Instantiate(__instance.GameSettingsButton, TemplateRoot.transform);
            TemplateGameSettingsButton.gameObject.SetActive(false);
        }
        ModGameOptionsMenu.OptionList = new();
        ModGameOptionsMenu.BehaviourList = new();
        ModGameOptionsMenu.CategoryHeaderList = new();

        SetDefaultButton(__instance);
        // Unity calls OnEnable before Start, and Start is not repeated after a
        // disable/re-enable. Build/register every custom tab before any selection.
        CreateModTabs(__instance);

        ControllerManager.Instance.OpenOverlayMenu(__instance.name, __instance.BackButton, __instance.DefaultButtonSelected, __instance.ControllerSelectable, false);
        DestroyableSingleton<HudManager>.Instance.menuNavigationPrompts.SetActive(false);
        if (Controller.currentTouchType != Controller.TouchType.Joystick)
        {
            __instance.ChangeTab(1, Controller.currentTouchType == Controller.TouchType.Joystick);
        }
        __instance.StartCoroutine(__instance.CoSelectDefault());

        return false;
    }
    [HarmonyPatch(nameof(GameSettingMenu.Close)), HarmonyPostfix]
    private static void ClosePostfix(GameSettingMenu __instance)
    {
        if (Instance != null && Instance != __instance) return;
        foreach (var button in ModSettingsButtons.Values)
            if (button != null) Object.Destroy(button.gameObject);
        foreach (var tab in ModSettingsTabs.Values)
        {
            GameOptionsMenuPatch.CancelBuild(tab);
            if (tab != null) Object.Destroy(tab.gameObject);
        }
        ModSettingsButtons = [];
        ModSettingsTabs = [];
        TemplateGameOptionsMenu = null;
        TemplateGameSettingsButton = null;
        if (TemplateRoot != null) Object.Destroy(TemplateRoot);
        TemplateRoot = null;
        // The native menu can be reused after Close/OnDisable. Remove the
        // controls we added before another OnEnable creates their replacements.
        // Hide them immediately because Destroy is deferred until frame end.
        if (PresetSelectorRoot != null)
        {
            PresetSelectorRoot.SetActive(false);
            Object.Destroy(PresetSelectorRoot);
        }
        PresetSelectorRoot = null;
        if (InputField != null)
        {
            InputField.gameObject.SetActive(false);
            Object.Destroy(InputField.gameObject);
        }
        PresetBehaviour = null;
        PresetLabel = null;
        InputField = null;
        _SearchForOptions = null;
        Instance = null;
        HiddenBySearch.Do(option => option.SetHidden(false));
        HiddenBySearch.Clear();
        ModGameOptionsMenu.TabIndex = 0;
        GameOptionsMenuPatch.ReleaseMenus();
    }

    [HarmonyPatch(nameof(GameSettingMenu.OnDisable)), HarmonyPostfix]
    private static void OnDisablePostfix(GameSettingMenu __instance) => ClosePostfix(__instance);
}
[HarmonyPatch(typeof(FreeChatInputField), nameof(FreeChatInputField.UpdateCharCount))]
public static class FixInputChatField
{
    public static bool Prefix(FreeChatInputField __instance)
    {
        if (GameSettingMenuPatch.InputField != null && __instance == GameSettingMenuPatch.InputField)
        {
            Vector2 size = __instance.Background.size;
            size.y = Math.Max(0.62f, __instance.textArea.TextHeight + 0.2f);
            __instance.Background.size = size;
            return false;
        }
        return true;
    }
}
[HarmonyPatch(typeof(ChatController), nameof(ChatController.Update))]
public static class FixDarkThemeForSearchBar
{
    public static void Postfix()
    {
        if (!GameSettingMenu.Instance || !Main.DarkTheme.Value) return;
        var field = GameSettingMenuPatch.InputField;
        if (field != null)
        {
            field.background.color = new Color32(40, 40, 40, byte.MaxValue);
            field.textArea.compoText.Color(Color.white);
            field.textArea.outputText.color = Color.white;
        }
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcSyncSettings))]
public class RpcSyncSettingsPatch
{
    public static bool Prefix()
    {
        // AU 2026.8.18 still sends legacy PlayerControl RPC 2 here, but its
        // HandleRpc no longer handles it: it falls through to Data.Role in the
        // lobby. All callers use the current options, which LogicOptions already
        // owns; its normal dirty serialization is the native synchronization path.
        var client = AmongUsClient.Instance;
        if (client && client.AmConnected && client.AmHost && !GameStates.IsFreePlay &&
            GameManager.Instance && GameManager.Instance.LogicOptions != null)
        {
            GameManager.Instance.LogicOptions.SetDirty();
            OptionItem.SyncAllOptions();
        }
        return false;
    }
}
