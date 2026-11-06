using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TOHE.Modules;

internal static class CloudMenu
{
    private const string OverlayName = "TOHEECloudSettings";
    private static SpriteRenderer panel;
    private static TextMeshPro status;
    private static TextMeshPro roomStatus;
    private static TextMeshPro notice;
    private static ToggleButtonBehaviour bind;
    private static ToggleButtonBehaviour accountRefresh;
    private static ToggleButtonBehaviour manage;
    private static ToggleButtonBehaviour share;
    private static ToggleButtonBehaviour roomShare;
    private static ToggleButtonBehaviour tags;
    private static ToggleButtonBehaviour tagsRefresh;
    private static ToggleButtonBehaviour systemProxy;
    private static ToggleButtonBehaviour lobbyButton;
    private static bool restoreOptions;
    private static bool lobbyMenu;
    private static readonly Dictionary<ToggleButtonBehaviour, bool> buttonStates = new();
    private static CloudService.MenuState? renderedState;
    private static SupportedLangs renderedLanguage;

    internal static void Close()
    {
        if (panel != null)
        {
            if (ControllerManager.Instance != null && ControllerManager.Instance.IsMenuActiveAtAll(OverlayName))
                ControllerManager.Instance.CloseOverlayMenu(OverlayName);
            Object.Destroy(panel.gameObject);
        }
        panel = null;
        status = null;
        roomStatus = null;
        notice = null;
        bind = accountRefresh = manage = share = roomShare = tags = tagsRefresh = systemProxy = lobbyButton = null;
        buttonStates.Clear();
        renderedState = null;
        if (restoreOptions && ClientOptionItem.CustomBackground != null)
            ClientOptionItem.CustomBackground.gameObject.SetActive(true);
        restoreOptions = false;
        lobbyMenu = false;
    }

    internal static void Open(OptionsMenuBehaviour options)
    {
        if (panel != null || options == null) return;
        Open(options, options.transform, false);
    }

    internal static void Open(MainMenuManager menu)
    {
        if (panel != null || menu == null) return;
        var options = Il2CppHelper.FindObjectsOfTypeAll<OptionsMenuBehaviour>()
            .FirstOrDefault(x => x.gameObject.scene.name == "MainMenu");
        Open(options, menu.mainMenuUI.transform, true);
    }

    internal static void Open(HudManager hud)
    {
        if (panel != null || hud == null) return;
        Open(hud.GameMenu, hud.transform, true);
        lobbyMenu = panel != null;
    }

    private static void Open(OptionsMenuBehaviour options, Transform parent, bool standalone)
    {
        if (panel != null || options == null || options.DisableMouseMovement == null || options.DisableMouseMovement.Text == null || options.Background == null) return;
        restoreOptions = !standalone && ClientOptionItem.CustomBackground != null && ClientOptionItem.CustomBackground.gameObject.activeSelf;
        if (restoreOptions) ClientOptionItem.CustomBackground.gameObject.SetActive(false);
        panel = Object.Instantiate(options.Background, parent);
        panel.name = "TOHEECloudSettings";
        panel.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
        panel.transform.localPosition = standalone ? new Vector3(0f, 0f, -20f) : panel.transform.localPosition + Vector3.back * 15f;
        panel.gameObject.SetActive(true);
        if (standalone)
        {
            panel.gameObject.AddComponent<UnityEngine.Rendering.SortingGroup>().sortingOrder = 100;
            var blocker = new GameObject("CloudClickBlocker");
            blocker.SetActive(false);
            blocker.transform.SetParent(panel.transform, false);
            blocker.transform.localPosition = new Vector3(0f, 0f, 1f);
            blocker.AddComponent<BoxCollider2D>().size = new Vector2(30f, 20f);
            var blockerButton = blocker.AddComponent<PassiveButton>();
            blockerButton.OnMouseOver = new();
            blockerButton.OnMouseOut = new();
            blockerButton.OnClick = new();
            blocker.SetActive(true);
        }

        var warning = Object.Instantiate(options.DisableMouseMovement.Text, panel.transform);
        warning.DestroyTranslator();
        warning.name = "CloudWarning";
        warning.text = Translator.GetString("CloudWarning");
        warning.alignment = TextAlignmentOptions.Center;
        warning.enableWordWrapping = true;
        warning.enableAutoSizing = true;
        warning.overflowMode = TextOverflowModes.Ellipsis;
        warning.richText = false;
        var anchor = warning.GetComponent<AspectPosition>();
        if (anchor != null)
        {
            anchor.enabled = false;
            Object.Destroy(anchor);
        }
        warning.transform.localScale = Vector3.one;
        warning.rectTransform.anchorMin = warning.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        warning.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        warning.margin = Vector4.zero;
        warning.fontSizeMin = 0.65f;
        warning.fontSizeMax = 1.3f;
        warning.rectTransform.sizeDelta = new Vector2(4.7f, 0.6f);
        warning.transform.localPosition = new Vector3(0f, 2.12f, -5f);

        roomStatus = Object.Instantiate(warning, panel.transform);
        roomStatus.name = "CloudRoomStatus";
        roomStatus.fontSizeMin = 0.75f;
        roomStatus.fontSizeMax = 1.3f;
        roomStatus.rectTransform.sizeDelta = new Vector2(4.7f, 0.3f);
        roomStatus.transform.localPosition = new Vector3(0f, 1.65f, -5f);

        status = Object.Instantiate(warning, panel.transform);
        status.name = "CloudStatus";
        status.fontSizeMin = 0.65f;
        status.fontSizeMax = 1.25f;
        status.rectTransform.sizeDelta = new Vector2(4.7f, 0.86f);
        status.transform.localPosition = new Vector3(0f, 1.04f, -5f);

        notice = Object.Instantiate(warning, panel.transform);
        notice.name = "CloudNotice";
        notice.fontSizeMin = 0.6f;
        notice.fontSizeMax = 1.2f;
        notice.rectTransform.sizeDelta = new Vector2(4.7f, 0.28f);
        notice.transform.localPosition = new Vector3(0f, -1.98f, -5f);
        notice.text = CloudService.LastMessage;

        bind = Button("CloudBind", -1.3f, 0.38f, () => Run("/cloud bind"));
        accountRefresh = Button("CloudAccountRefresh", 1.3f, 0.38f, () => CloudService.RefreshAccount());
        manage = Button("CloudManage", -1.3f, -0.10f, CloudService.ManageAccount);
        share = Button("CloudShareOn", 1.3f, -0.10f,
            () => Run(CloudService.ShareEnabled ? "/cloud share off" : "/cloud share on"));
        roomShare = Button("CloudRoomShare", -1.3f, -0.58f, CloudService.ToggleRoomShare);
        tags = Button("CloudTagsUse", 1.3f, -0.58f, () => CloudService.SetTagsEnabled(!CloudService.TagsEnabled));
        tagsRefresh = Button("CloudTagsRefresh", -1.3f, -1.06f, CloudService.RefreshTags);
#if ANDROID
        lobbyButton = Button("CloudLobbyButtonVisible", 1.3f, -1.06f,
            () => CloudService.SetLobbyButtonEnabled(!CloudService.LobbyButtonEnabled));
#else
        systemProxy = Button("CloudProxyUse", 1.3f, -1.06f, () => CloudService.SetUseSystemProxy(!CloudService.UseSystemProxy));
        lobbyButton = Button("CloudLobbyButtonVisible", -1.3f, -1.54f,
            () => CloudService.SetLobbyButtonEnabled(!CloudService.LobbyButtonEnabled));
#endif
        var back = Button("Back", 0f, -2.40f, Close);

        CloudService.RefreshAccount();
        Refresh();
        foreach (var item in panel.GetComponentsInChildren<Transform>(true))
            item.gameObject.layer = options.DisableMouseMovement.Text.gameObject.layer;
        if (standalone)
        {
            // SortingGroup children need explicit order; TMP also applies it to fallback font meshes.
            foreach (var renderer in panel.GetComponentsInChildren<SpriteRenderer>(true))
                renderer.sortingOrder = renderer == panel ? 0 : 1;
            foreach (var text in panel.GetComponentsInChildren<TextMeshPro>(true))
                text.sortingOrder = 2;
        }
        ControllerManager.Instance?.OpenOverlayMenu(OverlayName, back.GetComponent<PassiveButton>());

        void Run(string command) => CloudService.TryHandleCommand(command);

        ToggleButtonBehaviour Button(string label, float x, float y, Action action)
        {
            var button = Object.Instantiate(options.DisableMouseMovement, panel.transform);
            button.name = label;
            button.transform.localPosition = new Vector3(x, y, -5f);
            button.Text.DestroyTranslator();
            button.Text.text = Translator.GetString(label);
            button.Text.transform.localScale = Vector3.one;
            button.Text.transform.localPosition = new Vector3(0f, 0f, -1f);
            button.Text.rectTransform.anchorMin = button.Text.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            button.Text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            button.Text.rectTransform.sizeDelta = new Vector2(2.15f, 0.4f);
            button.Text.margin = Vector4.zero;
            button.Text.alignment = TextAlignmentOptions.Center;
            button.Text.enableWordWrapping = true;
            button.Text.enableAutoSizing = true;
            button.Text.fontSizeMin = 0.7f;
            button.Text.fontSizeMax = 1.4f;
            button.Text.overflowMode = TextOverflowModes.Ellipsis;
            var passive = button.GetComponent<PassiveButton>();
            passive.OnClick = new();
            passive.OnClick.AddListener((UnityEngine.Events.UnityAction)(() => { action(); Refresh(); }));
            return button;
        }
    }

    internal static void Update()
    {
        if (panel != null && ((lobbyMenu && !GameStates.IsLobby) || Input.GetKeyDown(KeyCode.Escape))) Close();
    }

    internal static void Refresh()
    {
        if (panel == null) return;
        var state = CloudService.CaptureMenuState();
        var language = Translator.GetCommandLanguage();
        if (renderedState == state && renderedLanguage == language) return;
        renderedState = state;
        renderedLanguage = language;
        SetText(status, CloudService.AccountStatus());
        SetText(roomStatus, CloudService.RoomStatusText());
        SetButton(bind, CloudService.CanBind);
        SetButton(accountRefresh, CloudService.CanRefreshAccount);
        SetButton(manage, CloudService.CanManage);
        SetButton(share, CloudService.CanChangeShare);
        SetButton(roomShare, CloudService.CanChangeRoomShare);
        SetButton(tags, CloudService.CanChangeTags);
        SetButton(tagsRefresh, CloudService.TagsEnabled && CloudService.CanChangeTags);
#if !ANDROID
        SetButton(systemProxy, CloudService.CanChangeProxy);
#endif
        SetButton(lobbyButton, true);
        SetText(share.Text, Translator.GetString(CloudService.ShareEnabled ? "CloudShareOff" : "CloudShareOn"));
        SetText(roomShare.Text, Translator.GetString(CloudService.RoomIsShared ? "CloudRoomStop" : "CloudRoomShare"));
        SetText(tags.Text, Translator.GetString("CloudTagsUse") + ": " +
            Translator.GetString(CloudService.TagsEnabled ? "CloudOptionOn" : "CloudOptionOff"));
#if !ANDROID
        SetText(systemProxy.Text, Translator.GetString("CloudProxyUse") + ": " +
            Translator.GetString(CloudService.UseSystemProxy ? "CloudOptionOn" : "CloudOptionOff"));
#endif
        SetText(lobbyButton.Text, Translator.GetString("CloudLobbyButtonVisible") + ": " +
            Translator.GetString(CloudService.LobbyButtonEnabled ? "CloudOptionOn" : "CloudOptionOff"));
    }

    private static void SetButton(ToggleButtonBehaviour button, bool enabled)
    {
        if (buttonStates.TryGetValue(button, out bool previous) && previous == enabled) return;
        buttonStates[button] = enabled;
        button.GetComponent<PassiveButton>().enabled = enabled;
        var color = enabled ? new Color32(255, 192, 203, byte.MaxValue) : new Color32(77, 77, 77, byte.MaxValue);
        button.Background.color = color;
        button.Rollover?.ChangeOutColor(color);
    }

    internal static void UpdateStatus(string message)
    {
        if (notice != null) SetText(notice, message);
    }

    private static void SetText(TextMeshPro target, string value)
    {
        if (target.text != value) target.text = value;
    }
}
