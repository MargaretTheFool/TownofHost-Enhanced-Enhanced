using System;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TOHE.Modules;

internal static class CloudMenu
{
    private static SpriteRenderer panel;
    private static TextMeshPro status;
    private static TextMeshPro notice;
    private static ToggleButtonBehaviour bind;
    private static ToggleButtonBehaviour accountRefresh;
    private static ToggleButtonBehaviour manage;
    private static ToggleButtonBehaviour share;
    private static ToggleButtonBehaviour privateRoom;
    private static ToggleButtonBehaviour tags;
    private static ToggleButtonBehaviour tagsRefresh;
    private static ToggleButtonBehaviour systemProxy;
    private static bool restoreOptions;

    internal static void Close()
    {
        if (panel != null) Object.Destroy(panel.gameObject);
        panel = null;
        status = null;
        notice = null;
        bind = accountRefresh = manage = share = privateRoom = tags = tagsRefresh = systemProxy = null;
        if (restoreOptions && ClientOptionItem.CustomBackground != null)
            ClientOptionItem.CustomBackground.gameObject.SetActive(true);
        restoreOptions = false;
    }

    internal static void Open(OptionsMenuBehaviour options)
    {
        if (panel != null || options == null || options.DisableMouseMovement == null) return;
        restoreOptions = ClientOptionItem.CustomBackground != null && ClientOptionItem.CustomBackground.gameObject.activeSelf;
        if (restoreOptions) ClientOptionItem.CustomBackground.gameObject.SetActive(false);
        panel = Object.Instantiate(options.Background, options.transform);
        panel.name = "TOHEECloudSettings";
        panel.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
        panel.transform.localPosition += Vector3.back * 15f;
        panel.gameObject.SetActive(true);

        var warning = Object.Instantiate(options.DisableMouseMovement.Text, panel.transform);
        warning.DestroyTranslator();
        warning.name = "CloudWarning";
        warning.text = Translator.GetString("CloudWarning");
        warning.alignment = TextAlignmentOptions.Center;
        warning.enableWordWrapping = true;
        warning.enableAutoSizing = true;
        warning.fontSizeMin = 0.75f;
        warning.fontSizeMax = 1.45f;
        warning.overflowMode = TextOverflowModes.Ellipsis;
        warning.rectTransform.sizeDelta = new Vector2(5.5f, 0.76f);
        warning.transform.localPosition = new Vector3(0f, 2f, -5f);

        status = Object.Instantiate(warning, panel.transform);
        status.name = "CloudStatus";
        status.richText = false;
        status.fontSizeMin = 0.85f;
        status.fontSizeMax = 1.4f;
        status.overflowMode = TextOverflowModes.Ellipsis;
        status.rectTransform.sizeDelta = new Vector2(5.5f, 0.96f);
        status.transform.localPosition = new Vector3(0f, 1.10f, -5f);

        notice = Object.Instantiate(warning, panel.transform);
        notice.name = "CloudNotice";
        notice.richText = false;
        notice.fontSizeMin = 0.85f;
        notice.fontSizeMax = 1.25f;
        notice.overflowMode = TextOverflowModes.Ellipsis;
        notice.rectTransform.sizeDelta = new Vector2(5.5f, 0.32f);
        notice.transform.localPosition = new Vector3(0f, -1.86f, -5f);
        notice.text = CloudService.LastMessage;

        bind = Button("CloudBind", -1.3f, 0.38f, () => Run("/cloud bind"));
        accountRefresh = Button("CloudAccountRefresh", 1.3f, 0.38f, () => CloudService.RefreshAccount());
        manage = Button("CloudManage", -1.3f, -0.20f, CloudService.ManageAccount);
        share = Button("CloudShareOn", 1.3f, -0.20f,
            () => Run(CloudService.ShareEnabled ? "/cloud share off" : "/cloud share on"));
        privateRoom = Button("CloudPrivate", -1.3f, -0.78f, () => Run("/cloud private confirm"));
        tags = Button("CloudTagsUse", 1.3f, -0.78f, () => CloudService.SetTagsEnabled(!CloudService.TagsEnabled));
        tagsRefresh = Button("CloudTagsRefresh", -1.3f, -1.36f, CloudService.RefreshTags);
        systemProxy = Button("CloudProxyUse", 1.3f, -1.36f, () => CloudService.SetUseSystemProxy(!CloudService.UseSystemProxy));
        Button("Back", 0f, -2.30f, Close);

        CloudService.RefreshAccount();
        Refresh();

        void Run(string command) => CloudService.TryHandleCommand(command);

        ToggleButtonBehaviour Button(string label, float x, float y, Action action)
        {
            var button = Object.Instantiate(options.DisableMouseMovement, panel.transform);
            button.name = label;
            button.transform.localPosition = new Vector3(x, y, -5f);
            button.Text.DestroyTranslator();
            button.Text.text = Translator.GetString(label);
            button.Text.enableWordWrapping = true;
            button.Text.enableAutoSizing = true;
            button.Text.fontSizeMin = 0.95f;
            button.Text.fontSizeMax = 1.55f;
            button.Text.overflowMode = TextOverflowModes.Ellipsis;
            var passive = button.GetComponent<PassiveButton>();
            passive.OnClick = new();
            passive.OnClick.AddListener((UnityEngine.Events.UnityAction)(() => action()));
            return button;
        }
    }

    internal static void Refresh()
    {
        if (panel == null) return;
        status.text = CloudService.AccountStatus();
        SetButton(bind, CloudService.CanBind);
        SetButton(accountRefresh, !CloudAccount.IsLoading);
        SetButton(manage, CloudService.CanManage);
        SetButton(share, CloudService.CanChangeShare);
        SetButton(privateRoom, CloudService.CanConfirmPrivate);
        SetButton(tags, true);
        SetButton(tagsRefresh, CloudService.TagsEnabled);
        SetButton(systemProxy, true);
        share.Text.text = Translator.GetString(CloudService.ShareEnabled ? "CloudShareOff" : "CloudShareOn");
        tags.Text.text = Translator.GetString("CloudTagsUse") + ": " +
            Translator.GetString(CloudService.TagsEnabled ? "CloudOptionOn" : "CloudOptionOff");
        systemProxy.Text.text = Translator.GetString("CloudProxyUse") + ": " +
            Translator.GetString(CloudService.UseSystemProxy ? "CloudOptionOn" : "CloudOptionOff");
    }

    private static void SetButton(ToggleButtonBehaviour button, bool enabled)
    {
        button.GetComponent<PassiveButton>().enabled = enabled;
        var color = enabled ? new Color32(255, 192, 203, byte.MaxValue) : new Color32(77, 77, 77, byte.MaxValue);
        button.Background.color = color;
        button.Rollover?.ChangeOutColor(color);
    }

    internal static void UpdateStatus(string message)
    {
        if (notice != null) notice.text = message;
    }
}
