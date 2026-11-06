using TMPro;
using UnityEngine;

namespace TOHE;

// Owned by the current HUD/lobby pair. Use the existing native HUD update,
// rather than registering another managed MonoBehaviour in IL2CPP.
public static class LobbySettingsPreview
{
    private const float PageControlsHeight = 0.72f;
    public static TextMeshPro Text { get; private set; }
    private static ToggleButtonBehaviour previousPageButton, nextPageButton;
    private static HudManager owner;
    private static LobbyBehaviour lobby;
    private static Camera layoutCamera;
    private static Rect layoutSafeArea, layoutViewport;
    private static bool layoutReady;
    private static int layoutLanguage;
    private static bool layoutOwnLanguage;

    public static void Update(HudManager hud)
    {
        if (!hud || !AmongUsClient.Instance || !GameStates.IsLobby || !GameStates.IsModHost ||
            !LobbyBehaviour.Instance || !PlayerControl.LocalPlayer || !Options.IsLoaded || Options.HideGameSettings == null)
        {
            Dispose();
            return;
        }
        if (owner != hud || lobby != LobbyBehaviour.Instance)
        {
            Dispose();
            owner = hud;
            lobby = LobbyBehaviour.Instance;
        }
        if (!CanShow(hud))
        {
            if (Text && Text.gameObject.activeSelf) Text.gameObject.SetActive(false);
            return;
        }
        if (!Text)
        {
            var source = hud.AbilityButton ? hud.AbilityButton.cooldownTimerText : null;
            if (!source) return;
            Text = Object.Instantiate(source, hud.transform);
            Text.name = "TOHEELobbySettingsPreview";
            Text.text = string.Empty;
            Text.enabled = true;
            Text.DestroyTranslator();
            Text.transform.localScale = Vector3.one;
            Text.color = Color.white;
            Text.outlineColor = Color.black;
            Text.outlineWidth = 0.15f;
            Text.alignment = TextAlignmentOptions.TopLeft;
            Text.rectTransform.pivot = new Vector2(0f, 1f);
            Text.margin = Vector4.zero;
            Text.enableWordWrapping = true;
            Text.enableAutoSizing = false;
            Text.fontSize = Text.fontSizeMax = 1.05f;
            Text.fontSizeMin = 0.75f;
            Text.autoSizeTextContainer = false;
            Text.overflowMode = TextOverflowModes.Overflow;
            Text.richText = true;
            layoutReady = false;
        }
        if (Application.platform == RuntimePlatform.Android) CreatePageButtons(hud);
        if (!Text.gameObject.activeSelf) Text.gameObject.SetActive(true);
        Position(hud);
        UpdatePageButtonLabels();
        OptionShower.GetTextNoFresh();
        if (Application.isFocused)
        {
            if (Input.GetKeyDown(KeyCode.Tab)) OptionShower.Next();
            for (int i = 0; i < 9; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i)) OptionShower.SelectPage(i);
        }
        var value = OptionShower.GetTextNoFresh();
        if (Text.text != value) Text.text = value;
    }

    private static bool CanShow(HudManager hud)
    {
        if (!hud.gameObject.activeInHierarchy || !lobby.gameObject.activeInHierarchy ||
            hud.IsIntroDisplayed || (hud.Chat && (!hud.Chat.IsClosedOrClosing || hud.Chat.IsAnimating)) ||
            (hud.GameMenu && hud.GameMenu.gameObject.activeInHierarchy) ||
            (GameSettingMenu.Instance && GameSettingMenu.Instance.gameObject.activeInHierarchy)) return false;
        var controller = ControllerManager.Instance;
        // Scene navigation itself does not obscure the lobby; every overlay does.
        var state = controller ? controller.CurrentUiState : null;
        return state == null || state.IsScene || string.IsNullOrEmpty(state.MenuName);
    }

    private static void Position(HudManager hud)
    {
        var camera = hud.UICamera;
        if (!camera) return;
        var safe = Screen.safeArea;
        var viewport = camera.pixelRect;
        int language = TranslationController.InstanceExists ? (int)TranslationController.Instance.currentLanguage.languageID : -1;
        bool ownLanguage = Main.ForceOwnLanguage.Value;
        if (layoutReady && camera == layoutCamera && safe == layoutSafeArea && viewport == layoutViewport &&
            layoutLanguage == language && layoutOwnLanguage == ownLanguage) return;
        layoutReady = true;
        layoutCamera = camera;
        layoutSafeArea = safe;
        layoutViewport = viewport;
        layoutLanguage = language;
        layoutOwnLanguage = ownLanguage;
        float left = Mathf.Max(safe.xMin, viewport.xMin), right = Mathf.Min(safe.xMax, viewport.xMax);
        float bottom = Mathf.Max(safe.yMin, viewport.yMin), top = Mathf.Min(safe.yMax, viewport.yMax);
        if (right <= left || top <= bottom) return;
        // Leave the room-code/start row below and the native toolbar/status area
        // on the right. Pixel-safe-area positioning also supports narrow windows.
        float depth = camera.WorldToScreenPoint(hud.transform.position).z;
        var lower = camera.ScreenToWorldPoint(new Vector3(left, bottom, depth));
        var upper = camera.ScreenToWorldPoint(new Vector3(right, top, depth));
        float width = upper.x - lower.x, height = upper.y - lower.y;
        // Keep the preview against the visible screen edge at every resolution;
        // world-unit padding grows noticeably on narrower windows.
        var anchor = camera.ScreenToWorldPoint(new Vector3(left + 4f, top - 4f, depth));
        Text.transform.position = new Vector3(anchor.x, anchor.y, hud.transform.position.z - 0.1f);
        float controlsHeight = Application.platform == RuntimePlatform.Android ? PageControlsHeight : 0f;
        var size = new Vector2(Mathf.Min(5.8f, width * 0.58f), Mathf.Max(1f, height - 1.4f - controlsHeight));
        Text.rectTransform.sizeDelta = size;
        PositionPageButtons(size);
        // TMP measures wrapping at the final fixed font size. Reserve the footer
        // before splitting pages so narrow windows cannot truncate the page count.
        string footer = Application.platform == RuntimePlatform.Android
            ? "\n\n(999/999)"
            : "\n\n" + Translator.GetString("PressTabToNextPage") + " (999/999)";
        OptionShower.ConfigurePagination(value => Text &&
            Text.GetPreferredValues(value + footer, size.x, float.PositiveInfinity).y <= size.y);
    }

    private static void CreatePageButtons(HudManager hud)
    {
        if (previousPageButton && nextPageButton) return;
        var template = hud.GameMenu ? hud.GameMenu.DisableMouseMovement : null;
        if (!template || !template.Text || !Text) return;

        ToggleButtonBehaviour Create(string key, string name, System.Action action)
        {
            var button = UnityEngine.Object.Instantiate(template, Text.transform);
            button.gameObject.SetActive(false);
            button.name = name;
            button.transform.localScale = Vector3.one;
            button.Text.DestroyTranslator();
            button.Text.text = Translator.GetString(key);
            button.Text.alignment = TextAlignmentOptions.Center;
            button.Text.enableAutoSizing = true;
            button.Text.fontSizeMin = 0.55f;
            button.Text.fontSizeMax = 1.1f;
            button.Text.enableWordWrapping = false;
            button.Text.overflowMode = TextOverflowModes.Ellipsis;
            button.Text.margin = Vector4.zero;
            button.Text.transform.localPosition = new Vector3(0f, 0f, -1f);
            button.Text.transform.localScale = Vector3.one;
            button.Background.color = new Color32(55, 112, 153, byte.MaxValue);
            var passive = button.GetComponent<PassiveButton>();
            passive.OnClick = new();
            passive.OnClick.AddListener((UnityEngine.Events.UnityAction)(() => action()));
            button.gameObject.SetActive(true);
            return button;
        }

        previousPageButton = Create("PreviousPage", "TOHEELobbySettingsPreviousPage", OptionShower.Previous);
        nextPageButton = Create("NextPage", "TOHEELobbySettingsNextPage", OptionShower.Next);
        layoutReady = false;
    }

    private static void PositionPageButtons(Vector2 textSize)
    {
        if (!previousPageButton || !nextPageButton) return;
        float gap = Mathf.Min(0.12f, textSize.x * 0.04f);
        float buttonWidth = Mathf.Min(2.15f, (textSize.x - gap) / 2f);
        var buttonSize = new Vector2(buttonWidth, 0.52f);
        float centerY = -textSize.y - 0.36f;
        Position(previousPageButton, buttonWidth / 2f, centerY, buttonSize);
        Position(nextPageButton, buttonWidth * 1.5f + gap, centerY, buttonSize);

        static void Position(ToggleButtonBehaviour button, float x, float y, Vector2 size)
        {
            button.transform.localPosition = new Vector3(x, y, -0.1f);
            button.Background.size = size;
            button.GetComponent<BoxCollider2D>().size = size;
            button.Text.rectTransform.anchorMin = button.Text.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            button.Text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            button.Text.rectTransform.sizeDelta = size;
        }
    }

    private static void UpdatePageButtonLabels()
    {
        if (previousPageButton && previousPageButton.Text)
        {
            string label = Translator.GetString("PreviousPage");
            if (previousPageButton.Text.text != label) previousPageButton.Text.text = label;
        }
        if (nextPageButton && nextPageButton.Text)
        {
            string label = Translator.GetString("NextPage");
            if (nextPageButton.Text.text != label) nextPageButton.Text.text = label;
        }
    }

    public static void Dispose()
    {
        if (!Text && !owner && !lobby) return;
        if (Text)
        {
            Text.gameObject.SetActive(false);
            Object.Destroy(Text.gameObject);
        }
        Text = null;
        previousPageButton = nextPageButton = null;
        owner = null;
        lobby = null;
        layoutReady = false;
        OptionShower.Reset();
    }

    internal static void Dispose(HudManager hud)
    {
        if (owner == hud) Dispose();
    }

    internal static void Dispose(LobbyBehaviour currentLobby)
    {
        if (lobby == currentLobby) Dispose();
    }
}

[HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
internal static class LobbySettingsPreviewUpdatePatch
{
    public static void Postfix(HudManager __instance) => LobbySettingsPreview.Update(__instance);
}

[HarmonyPatch(typeof(HudManager), nameof(HudManager.OnDestroy))]
internal static class LobbySettingsPreviewHudDestroyPatch
{
    public static void Prefix(HudManager __instance) => LobbySettingsPreview.Dispose(__instance);
}

[HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.OnDestroy))]
internal static class LobbySettingsPreviewLobbyDestroyPatch
{
    public static void Prefix(LobbyBehaviour __instance) => LobbySettingsPreview.Dispose(__instance);
}
