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
    private static PassiveButton RemotePresetCopyButton;
    private static TextMeshPro RemotePresetCopyLabel;
    private static GameObject RemotePresetCopyDialog;
    private static Sprite SolidSprite;
    public static FreeChatInputField InputField;
    public static List<OptionItem> HiddenBySearch = [];
    public static Action _SearchForOptions;

    private static void SetupAdittionalButtons(GameSettingMenu __instance)
    {
        var ParentLeftPanel = __instance.GamePresetsButton.transform.parent;

        // The sidebar label is outside the settings scroller's stencil mask.
        var valueTemplate = __instance.GamePresetsButton.GetComponentInChildren<TextMeshPro>(true);
        var readableFont = Resources.FindObjectsOfTypeAll<TMP_FontAsset>()
            .FirstOrDefault(font => font.name == "Barlow-SemiBold SDF") ?? valueTemplate.font;
        var presetBackground = CreatePresetSelector(__instance, valueTemplate, readableFont);

        var GameSettingsLabel = __instance.GameSettingsButton.transform.parent.parent.FindChild("GameSettingsLabel").GetComponent<TextMeshPro>();
        GameSettingsLabel.DestroyTranslator();
        GameSettingsLabel.text = GetString($"{Options.CurrentGameMode}");

        var FreeChatField = DestroyableSingleton<ChatController>.Instance.freeChatField;
        var TextField = Object.Instantiate(FreeChatField, ParentLeftPanel.parent);
        TextField.transform.localScale = new Vector3(0.3f, 0.59f, 1);
        TextField.transform.localPosition = new Vector3(-2.07f, -2.57f, -5f);
        TextField.textArea.outputText.transform.localScale = new Vector3(3.5f, 2f, 1f);
        TextField.textArea.outputText.font = readableFont;
        TextField.name = "InputField";

        // The copy action sits between the preset selector and search field.
        // Align all rendered bounds in world space to avoid overlap.
        var searchBounds = TextField.Background.bounds;
        var presetBounds = presetBackground.bounds;
        var copyCollider = RemotePresetCopyButton.GetComponent<BoxCollider2D>();
        float copyRight = RemotePresetCopyButton.transform.position.x
            + RemotePresetCopyButton.transform.TransformVector(new Vector3(copyCollider.size.x / 2f, 0f, 0f)).magnitude;
        float gap = ParentLeftPanel.TransformVector(new Vector3(0.12f, 0f, 0f)).magnitude;
        TextField.transform.position += new Vector3(
            copyRight + gap - searchBounds.min.x,
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

        RefreshRemotePresetCopyButton();

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

    private static SpriteRenderer CreatePresetSelector(GameSettingMenu menu, TextMeshPro textTemplate, TMP_FontAsset readableFont)
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
        ConfigurePresetText(PresetLabel, OptionItem.FastOptions[OptionItem.PresetId].GetString(),
            new Vector2(width - 2f * arrowWidth - 0.16f, height), 2.4f);
        PresetLabel.font = readableFont;
        PresetLabel.fontSharedMaterial = readableFont.material;
        PresetLabel.fontStyle = FontStyles.Normal;
        PresetLabel.color = Color.white;
        PresetLabel.outlineWidth = 0.05f;
        PresetLabel.enableAutoSizing = true;
        PresetLabel.fontSizeMin = 1.8f;
        CenterPresetText(PresetLabel);

        CreateArrow(-1);
        CreateArrow(1);

        var copyButton = Object.Instantiate(menu.GameSettingsButton, root.transform);
        RemotePresetCopyButton = copyButton;
        copyButton.name = "CopyLastHostSettings";
        foreach (var anchor in copyButton.GetComponentsInChildren<AspectPosition>(true))
        {
            anchor.enabled = false;
            Object.Destroy(anchor);
        }
        copyButton.transform.localScale = Vector3.one;
        copyButton.transform.localPosition = new Vector3(width / 2f + 0.56f, 0f, -1f);
        var copyCollider = copyButton.GetComponent<BoxCollider2D>();
        copyCollider.offset = Vector2.zero;
        copyCollider.size = new Vector2(1f, height);
        CenterClonedButtonContents(copyButton);
        foreach (var state in new[] { copyButton.inactiveSprites, copyButton.activeSprites,
            copyButton.selectedSprites, copyButton.selectedInactiveSprites, copyButton.disabledSprites,
            copyButton.onClickSprites })
        {
            var sprite = state ? state.GetComponentInChildren<SpriteRenderer>(true) : null;
            if (sprite == null) continue;
            sprite.drawMode = SpriteDrawMode.Sliced;
            sprite.size = new Vector2(1f, height);
        }
        RemotePresetCopyLabel = copyButton.GetComponentInChildren<TextMeshPro>(true);
        ConfigurePresetText(RemotePresetCopyLabel, GetString("RemotePresetCopyUnavailable"),
            new Vector2(0.9f, 0.42f), 2.4f);
        RemotePresetCopyLabel.font = readableFont;
        RemotePresetCopyLabel.fontSharedMaterial = readableFont.material;
        RemotePresetCopyLabel.fontStyle = FontStyles.Normal;
        RemotePresetCopyLabel.color = Color.white;
        RemotePresetCopyLabel.enableAutoSizing = true;
        RemotePresetCopyLabel.fontSizeMin = 1.5f;
        RemotePresetCopyLabel.fontSizeMax = 2.4f;
        copyButton.OnClick = new();
        copyButton.OnClick.AddListener((UnityEngine.Events.UnityAction)(() =>
            ShowPresetCopySource(menu)));
        copyButton.SetButtonEnableState(false);
        copyButton.gameObject.SetActive(true);
        menu.ControllerSelectable.Add(copyButton);
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

    private static void CenterClonedButtonContents(PassiveButton button)
    {
        var label = button.GetComponentInChildren<TextMeshPro>(true);
        var labelParent = label.transform.parent;
        labelParent.localPosition = new Vector3(0f, 0f, labelParent.localPosition.z);
        foreach (var state in new[] { button.inactiveSprites, button.activeSprites,
            button.selectedSprites, button.selectedInactiveSprites, button.disabledSprites,
            button.onClickSprites })
        {
            if (!state) continue;
            var transform = state.transform;
            transform.localPosition = new Vector3(0f, 0f, transform.localPosition.z);
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

    private static bool CanCopyPreset()
        => GameOptionsMenuPatch.CanEdit && GameStates.IsLobby && !OptionItem.IsRemotePreset;

    private static void RefreshRemotePresetCopyButton()
    {
        if (RemotePresetCopyButton == null) return;

        bool canCopy = CanCopyPreset();
        RemotePresetCopyButton.SetButtonEnableState(canCopy);
        RemotePresetCopyLabel.text = canCopy
            ? GetString("RemotePresetCopyButton")
            : !GameOptionsMenuPatch.CanEdit ? GetString("RemotePresetCopyHostOnly")
            : !GameStates.IsLobby ? GetString("RemotePresetCopyLobbyOnly")
            : OptionItem.IsRemotePreset ? GetString("RemotePresetCopySelectLocal")
            : GetString("RemotePresetCopyUnavailable");
        CenterPresetText(RemotePresetCopyLabel);
    }

    private static void ShowPresetCopySource(GameSettingMenu menu)
    {
        RefreshRemotePresetCopyButton();
        if (menu == null || !menu.isActiveAndEnabled || !CanCopyPreset()) return;

        var root = CreatePresetCopyDialog(menu);
        CreatePresetCopyText(menu, root, "Title", GetString("PresetCopyChooseSource"),
            new Vector3(0f, 1.15f, -1f), new Vector2(5.2f, 0.5f), 2.3f);
        CreatePresetCopyButton(menu, root, "PreviousHost", GetString("PresetCopyPreviousHostChoice"),
            new Vector3(0f, 0.46f, -2f), new Vector2(4.9f, 0.55f), OptionItem.HasRemotePreset,
            () => ShowPresetCopySelection(menu, OptionItem.RemotePreset, OptionItem.CurrentPreset));
        CreatePresetCopyButton(menu, root, "LocalPresets", GetString("PresetCopyLocalChoice"),
            new Vector3(0f, -0.29f, -2f), new Vector2(4.9f, 0.55f), true,
            () => ShowPresetCopySelection(menu, OptionItem.CurrentPreset, OptionItem.CurrentPreset));
        CreatePresetCopyButton(menu, root, "Cancel", GetString("Cancel"),
            new Vector3(0f, -1.2f, -2f), new Vector2(1.6f, 0.5f), true, CloseRemotePresetCopyConfirmation);
    }

    private static void ShowPresetCopySelection(GameSettingMenu menu, int sourcePreset, int targetPreset)
    {
        if (menu == null || !menu.isActiveAndEnabled || !CanCopyPreset()) return;

        bool fromHost = sourcePreset == OptionItem.RemotePreset;
        if (fromHost && !OptionItem.HasRemotePreset) return;

        var root = CreatePresetCopyDialog(menu);
        CreatePresetCopyText(menu, root, "Title", GetString("PresetCopySelectSlots"),
            new Vector3(0f, 1.15f, -1f), new Vector2(5.2f, 0.5f), 2.3f);
        CreatePresetCopyText(menu, root, "SourceLabel", GetString("PresetCopySource"),
            new Vector3(-1.65f, 0.48f, -1f), new Vector2(1.8f, 0.48f), 2.1f);
        CreatePresetCopyText(menu, root, "SourceValue",
            fromHost ? GetString("PresetCopyPreviousHost") : GetPresetCopyName(sourcePreset),
            new Vector3(0.62f, 0.48f, -1f), new Vector2(2.3f, 0.48f), 2.1f);
        CreatePresetCopyText(menu, root, "TargetLabel", GetString("PresetCopyTarget"),
            new Vector3(-1.65f, -0.23f, -1f), new Vector2(1.8f, 0.48f), 2.1f);
        CreatePresetCopyText(menu, root, "TargetValue", GetPresetCopyName(targetPreset),
            new Vector3(0.62f, -0.23f, -1f), new Vector2(1.7f, 0.48f), 2.1f);

        if (!fromHost)
        {
            CreatePresetCopyButton(menu, root, "SourcePrevious", "−",
                new Vector3(-0.58f, 0.48f, -2f), new Vector2(0.48f, 0.48f), true,
                () => ShowPresetCopySelection(menu, (sourcePreset + OptionItem.NumPresets - 1) % OptionItem.NumPresets, targetPreset));
            CreatePresetCopyButton(menu, root, "SourceNext", "+",
                new Vector3(1.82f, 0.48f, -2f), new Vector2(0.48f, 0.48f), true,
                () => ShowPresetCopySelection(menu, (sourcePreset + 1) % OptionItem.NumPresets, targetPreset));
        }
        CreatePresetCopyButton(menu, root, "TargetPrevious", "−",
            new Vector3(-0.58f, -0.23f, -2f), new Vector2(0.48f, 0.48f), true,
            () => ShowPresetCopySelection(menu, sourcePreset, (targetPreset + OptionItem.NumPresets - 1) % OptionItem.NumPresets));
        CreatePresetCopyButton(menu, root, "TargetNext", "+",
            new Vector3(1.82f, -0.23f, -2f), new Vector2(0.48f, 0.48f), true,
            () => ShowPresetCopySelection(menu, sourcePreset, (targetPreset + 1) % OptionItem.NumPresets));

        bool canContinue = fromHost || sourcePreset != targetPreset;
        if (!canContinue)
            CreatePresetCopyText(menu, root, "SamePreset", GetString("PresetCopySamePreset"),
                new Vector3(0f, -0.8f, -1f), new Vector2(5.1f, 0.36f), 1.7f);
        CreatePresetCopyButton(menu, root, "Back", GetString("Back"),
            new Vector3(-1.85f, -1.27f, -2f), new Vector2(1.5f, 0.5f), true, () => ShowPresetCopySource(menu));
        CreatePresetCopyButton(menu, root, "Next", GetString("PresetCopyNext"),
            new Vector3(0f, -1.27f, -2f), new Vector2(1.5f, 0.5f), canContinue,
            () => ShowPresetCopyConfirmation(menu, sourcePreset, targetPreset));
        CreatePresetCopyButton(menu, root, "Cancel", GetString("Cancel"),
            new Vector3(1.85f, -1.27f, -2f), new Vector2(1.5f, 0.5f), true, CloseRemotePresetCopyConfirmation);
    }

    private static void ShowPresetCopyConfirmation(GameSettingMenu menu, int sourcePreset, int targetPreset)
    {
        if (menu == null || !menu.isActiveAndEnabled || !CanCopyPreset()) return;

        bool fromHost = sourcePreset == OptionItem.RemotePreset;
        if ((fromHost && !OptionItem.HasRemotePreset) || sourcePreset == targetPreset) return;

        string sourceName = fromHost ? GetString("PresetCopyPreviousHost") : GetPresetCopyName(sourcePreset);
        string targetName = GetPresetCopyName(targetPreset);
        string impactKey = targetPreset == OptionItem.CurrentPreset ? "PresetCopyAffectsCurrent" : "PresetCopyKeepsCurrent";
        string prompt = string.Format(GetString("PresetCopyConfirm"), sourceName, targetName)
            + "\n" + GetString(impactKey);
        var root = CreatePresetCopyDialog(menu);
        CreatePresetCopyText(menu, root, "Message", prompt,
            new Vector3(0f, 0.3f, -1f), new Vector2(5.3f, 1.65f), 2.15f);
        CreatePresetCopyButton(menu, root, "Back", GetString("Back"),
            new Vector3(-1.85f, -1.27f, -2f), new Vector2(1.5f, 0.5f), true,
            () => ShowPresetCopySelection(menu, sourcePreset, targetPreset));
        CreatePresetCopyButton(menu, root, "Confirm", GetString("RemotePresetCopyConfirmButton"),
            new Vector3(0f, -1.27f, -2f), new Vector2(1.5f, 0.5f), true,
            () => ConfirmPresetCopy(menu, sourcePreset, targetPreset));
        CreatePresetCopyButton(menu, root, "Cancel", GetString("Cancel"),
            new Vector3(1.85f, -1.27f, -2f), new Vector2(1.5f, 0.5f), true, CloseRemotePresetCopyConfirmation);
    }

    private static string GetPresetCopyName(int preset)
    {
        var name = preset switch
        {
            0 => Main.Preset1,
            1 => Main.Preset2,
            2 => Main.Preset3,
            3 => Main.Preset4,
            _ => Main.Preset5,
        };
        return name.Value == (string)name.DefaultValue ? GetString($"Preset_{preset + 1}") : name.Value;
    }

    private static GameObject CreatePresetCopyDialog(GameSettingMenu menu)
    {
        CloseRemotePresetCopyConfirmation();
        var root = new GameObject("PresetCopyDialog");
        RemotePresetCopyDialog = root;
        root.layer = menu.gameObject.layer;
        root.transform.SetParent(menu.transform, false);
        root.transform.localPosition = new Vector3(0f, 0f, -15f);

        if (!SolidSprite)
        {
            var texture = Texture2D.whiteTexture;
            SolidSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 1f);
        }

        var dimObject = new GameObject("DimBackground") { layer = root.layer };
        dimObject.transform.SetParent(root.transform, false);
        dimObject.transform.localPosition = new Vector3(0f, 0f, 0.3f);
        var dim = dimObject.AddComponent<SpriteRenderer>();
        dim.sprite = SolidSprite;
        dim.drawMode = SpriteDrawMode.Sliced;
        dim.size = new Vector2(30f, 20f);
        dim.color = new Color(0f, 0f, 0f, 0.6f);

        var blocker = Object.Instantiate(menu.GameSettingsButton, root.transform);
        blocker.name = "Blocker";
        foreach (var anchor in blocker.GetComponentsInChildren<AspectPosition>(true))
        {
            anchor.enabled = false;
            Object.Destroy(anchor);
        }
        blocker.transform.localPosition = Vector3.zero;
        blocker.transform.localScale = Vector3.one;
        var blockerCollider = blocker.GetComponent<BoxCollider2D>();
        blockerCollider.offset = Vector2.zero;
        blockerCollider.size = new Vector2(30f, 20f);
        blocker.OnClick = new();
        blocker.OnClick.AddListener((UnityEngine.Events.UnityAction)(() => { }));
        blocker.SetButtonEnableState(true);
        foreach (var sprite in blocker.GetComponentsInChildren<SpriteRenderer>(true)) sprite.enabled = false;
        foreach (var label in blocker.GetComponentsInChildren<TextMeshPro>(true)) label.gameObject.SetActive(false);

        var panelObject = new GameObject("Panel") { layer = root.layer };
        panelObject.transform.SetParent(root.transform, false);
        panelObject.transform.localPosition = new Vector3(0f, 0f, -1f);
        var panel = panelObject.AddComponent<SpriteRenderer>();
        panel.sprite = PresetSelectorVisuals.Panel;
        panel.drawMode = SpriteDrawMode.Sliced;
        panel.size = new Vector2(5.8f, 3.4f);

        var fillObject = new GameObject("OpaqueFill") { layer = root.layer };
        fillObject.transform.SetParent(root.transform, false);
        fillObject.transform.localPosition = new Vector3(0f, 0f, -0.95f);
        var fill = fillObject.AddComponent<SpriteRenderer>();
        fill.sprite = SolidSprite;
        fill.drawMode = SpriteDrawMode.Sliced;
        fill.size = new Vector2(5.76f, 3.36f);
        fill.color = new Color32(27, 37, 42, 255);
        return root;
    }

    private static void CreatePresetCopyText(GameSettingMenu menu, GameObject root, string name,
        string caption, Vector3 position, Vector2 size, float fontSize)
    {
        var label = Object.Instantiate(menu.GamePresetsButton.GetComponentInChildren<TextMeshPro>(true), root.transform);
        label.name = name;
        ConfigurePresetText(label, caption, size, fontSize);
        var font = RemotePresetCopyLabel.font;
        label.font = font;
        label.fontSharedMaterial = font.material;
        label.fontStyle = FontStyles.Normal;
        label.enableWordWrapping = true;
        label.enableAutoSizing = true;
        label.fontSizeMin = 1.45f;
        label.fontSizeMax = fontSize;
        label.color = new Color32(235, 243, 243, 255);
        label.margin = new Vector4(0.08f, 0.03f, 0.08f, 0.03f);
        CenterPresetText(label);
        label.transform.localPosition += position;
    }

    private static void CreatePresetCopyButton(GameSettingMenu menu, GameObject root, string name,
        string caption, Vector3 position, Vector2 size, bool enabled, Action action)
    {
        var button = Object.Instantiate(menu.GameSettingsButton, root.transform);
        button.gameObject.SetActive(true);
        button.name = name;
        foreach (var anchor in button.GetComponentsInChildren<AspectPosition>(true))
        {
            anchor.enabled = false;
            Object.Destroy(anchor);
        }
        button.transform.localPosition = position;
        button.transform.localScale = Vector3.one;
        var collider = button.GetComponent<BoxCollider2D>();
        collider.offset = Vector2.zero;
        collider.size = size;
        CenterClonedButtonContents(button);
        foreach (var state in new[] { button.inactiveSprites, button.activeSprites,
            button.selectedSprites, button.selectedInactiveSprites, button.disabledSprites,
            button.onClickSprites })
        {
            var sprite = state ? state.GetComponentInChildren<SpriteRenderer>(true) : null;
            if (sprite == null) continue;
            sprite.drawMode = SpriteDrawMode.Sliced;
            sprite.size = size;
        }
        var label = button.GetComponentInChildren<TextMeshPro>(true);
        ConfigurePresetText(label, caption, new Vector2(size.x - 0.12f, size.y - 0.04f), 2.1f);
        var font = RemotePresetCopyLabel.font;
        label.font = font;
        label.fontSharedMaterial = font.material;
        label.fontStyle = FontStyles.Normal;
        label.color = Color.white;
        label.enableAutoSizing = true;
        label.fontSizeMin = 1.45f;
        label.fontSizeMax = 2.1f;
        CenterPresetText(label);
        button.OnClick = new();
        if (enabled) button.OnClick.AddListener((UnityEngine.Events.UnityAction)(() => action()));
        button.SetButtonEnableState(true);
        if (!enabled)
        {
            collider.enabled = false;
            foreach (var sprite in button.GetComponentsInChildren<SpriteRenderer>(true))
                sprite.color = new Color32(80, 84, 85, 255);
            label.color = new Color32(157, 161, 161, 255);
        }
    }

    private static void ConfirmPresetCopy(GameSettingMenu menu, int sourcePreset, int targetPreset)
    {
        CloseRemotePresetCopyConfirmation();
        RefreshRemotePresetCopyButton();
        if (menu == null || !menu.isActiveAndEnabled || !CanCopyPreset()) return;

        var previousMode = Options.CurrentGameMode;
        if (!OptionItem.CopyPreset(sourcePreset, targetPreset))
        {
            HudManager.Instance?.ShowPopUp(GetString("RemotePresetCopyFailed"));
            return;
        }

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
        if (PresetLabel != null && OptionItem.FastOptions.TryGetValue(OptionItem.PresetId, out var presetOption))
        {
            PresetLabel.text = presetOption.GetString();
            CenterPresetText(PresetLabel);
        }
        if (InputField != null) InputField.Clear();
        RefreshRemotePresetCopyButton();
    }
    private static void CloseRemotePresetCopyConfirmation()
    {
        if (RemotePresetCopyDialog == null) return;
        RemotePresetCopyDialog.SetActive(false);
        Object.Destroy(RemotePresetCopyDialog);
        RemotePresetCopyDialog = null;
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
        CloseRemotePresetCopyConfirmation();
        RemotePresetCopyButton = null;
        RemotePresetCopyLabel = null;
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
        }
        return false;
    }
}
