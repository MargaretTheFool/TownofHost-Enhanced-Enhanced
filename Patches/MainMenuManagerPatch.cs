using System;
using TMPro;
using TOHE.Modules;
using UnityEngine;
using UnityEngine.UI;
using static TOHE.Translator;
using Object = UnityEngine.Object;

namespace TOHE;

[HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start)), HarmonyPriority(Priority.First)]
public class MainMenuManagerStartPatch
{
    public static GameObject amongUsLogo;
    public static GameObject Ambience;
    public static SpriteRenderer ToheLogo { get; private set; }

    private static void Postfix(MainMenuManager __instance)
    {
        amongUsLogo = GameObject.Find("LOGO-AU");
        if (amongUsLogo != null) amongUsLogo.SetActive(false);

        var rightpanel = __instance.gameModeButtons.transform.parent;
        var logoObject = new GameObject("titleLogo_TOHE");
        var logoTransform = logoObject.transform;

        ToheLogo = logoObject.AddComponent<SpriteRenderer>();
        logoTransform.parent = rightpanel;
        logoTransform.localPosition = new(-0.16f, 0f, 1f);
        logoTransform.localScale *= 1.2f;

        if ((Ambience = GameObject.Find("Ambience")) != null)
        {
            // Show play button when mod is fully loaded
            //if (Options.IsLoaded && !__instance.playButton.enabled)
            //    __instance.playButton.transform.gameObject.SetActive(true);

            //else if (!Options.IsLoaded)
            //    __instance.playButton?.transform.gameObject.SetActive(false);

            //Logger.Msg($"Play button showed? : Options is loaded: {Options.IsLoaded} - check play button enabled {__instance.playButton.enabled}", "PlayButton");

            Ambience.SetActive(false);
        }
    }
}
[HarmonyPatch(typeof(MainMenuManager))]
public static class MainMenuManagerPatch
{
    private static PassiveButton template;
    private static PassiveButton gitHubButton;
    private static PassiveButton websiteButton;
    private static PassiveButton discordButton;
    private static PassiveButton cloudButton;
    private static TMP_Text cloudButtonText;
    //private static PassiveButton patreonButton;

    [HarmonyPatch(nameof(MainMenuManager.Start)), HarmonyPostfix, HarmonyPriority(Priority.Normal)]
    public static void Start_Postfix(MainMenuManager __instance)
    {
        if (template == null) template = __instance.quitButton;

        // FPS
        Application.targetFrameRate = Main.UnlockFPS.Value ? 165 : 60;

        __instance.screenTint.gameObject.transform.localPosition += new Vector3(1000f, 0f);
        __instance.screenTint.enabled = false;
        __instance.rightPanelMask.SetActive(true);
        // The background texture (large sprite asset)
        __instance.mainMenuUI.FindChild<SpriteRenderer>("BackgroundTexture").transform.gameObject.SetActive(false);
        // The glint on the Among Us Menu
        __instance.mainMenuUI.FindChild<SpriteRenderer>("WindowShine").transform.gameObject.SetActive(false);
        __instance.mainMenuUI.FindChild<Transform>("ScreenCover").gameObject.SetActive(false);

        GameObject leftPanel = __instance.mainMenuUI.FindChild<Transform>("LeftPanel").gameObject;
        GameObject rightPanel = __instance.mainMenuUI.FindChild<Transform>("RightPanel").gameObject;
        rightPanel.gameObject.GetComponent<SpriteRenderer>().enabled = false;
        GameObject maskedBlackScreen = rightPanel.FindChild<Transform>("MaskedBlackScreen").gameObject;
        maskedBlackScreen.GetComponent<SpriteRenderer>().enabled = false;
        //maskedBlackScreen.transform.localPosition = new Vector3(-3.345f, -2.05f); //= new Vector3(0f, 0f);
        maskedBlackScreen.transform.localScale = new Vector3(7.35f, 4.5f, 4f);

        __instance.mainMenuUI.gameObject.transform.position += new Vector3(-0.2f, 0f);

        leftPanel.gameObject.GetComponent<SpriteRenderer>().enabled = false;
        leftPanel.gameObject.FindChild<SpriteRenderer>("Divider").enabled = false;
        leftPanel.GetComponentsInChildren<SpriteRenderer>(true).Where(r => r.name == "Shine").ToList().ForEach(r => r.enabled = false);

        GameObject splashArt = new("SplashArt");
        splashArt.transform.position = new Vector3(0, 0f, 600f); //= new Vector3(0, 0.40f, 600f);
        var spriteRenderer = splashArt.AddComponent<SpriteRenderer>();
        string folder = "TOHE.Resources.Background.";
        IRandom rand = IRandom.Instance;
        if (rand.Next(0, 100) < 30) folder += "PrevArtWinner";
        else folder += "CurrentArtWinner";
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        string[] fileNames = assembly.GetManifestResourceNames().Where(resourceName => resourceName.StartsWith(folder) && resourceName.EndsWith(".png")).ToArray();
        int choice = rand.Next(0, fileNames.Length);

        spriteRenderer.sprite = Utils.LoadSprite(fileNames[choice], 150f);


        //__instance.playLocalButton.inactiveSprites.GetComponent<SpriteRenderer>().color = new Color(0.1647f, 0f, 0.7765f);
        //__instance.PlayOnlineButton.inactiveSprites.GetComponent<SpriteRenderer>().color = new Color(0.1647f, 0f, 0.7765f);
        //__instance.playLocalButton.transform.position = new Vector3(2.095f, -0.25f, 520f);
        //__instance.PlayOnlineButton.transform.position = new Vector3(0f, -0.25f, 0f);


        /*    __instance.playButton.inactiveSprites.GetComponent<SpriteRenderer>().color = new Color(1f, 0f, 0.35f);
            __instance.playButton.activeSprites.GetComponent<SpriteRenderer>().color = new Color(0.929f, 0.255f, 0.773f);
            Color originalColorPlayButton = __instance.playButton.inactiveSprites.GetComponent<SpriteRenderer>().color;
            __instance.playButton.inactiveSprites.GetComponent<SpriteRenderer>().color = originalColorPlayButton * 0.5f;
            __instance.playButton.activeTextColor = Color.white;
            __instance.playButton.inactiveTextColor = Color.white;
            __instance.playButton.inactiveSprites.GetComponent<SpriteRenderer>().color = new Color(1f, 0f, 0.35f);

            __instance.inventoryButton.inactiveSprites.GetComponent<SpriteRenderer>().color = new Color(1f, 0f, 0.35f);
            __instance.inventoryButton.activeSprites.GetComponent<SpriteRenderer>().color = new Color(0.929f, 0.255f, 0.773f);
            Color originalColorInventoryButton = __instance.inventoryButton.inactiveSprites.GetComponent<SpriteRenderer>().color;
            __instance.inventoryButton.inactiveSprites.GetComponent<SpriteRenderer>().color = originalColorInventoryButton * 0.5f;
            __instance.inventoryButton.activeTextColor = Color.white;
            __instance.inventoryButton.inactiveTextColor = Color.white;
            __instance.inventoryButton.inactiveSprites.GetComponent<SpriteRenderer>().color = new Color(1f, 0f, 0.35f);

            __instance.shopButton.inactiveSprites.GetComponent<SpriteRenderer>().color = new Color(1f, 0f, 0.35f);
            __instance.shopButton.activeSprites.GetComponent<SpriteRenderer>().color = new Color(0.929f, 0.255f, 0.773f);
            Color originalColorShopButton = __instance.shopButton.inactiveSprites.GetComponent<SpriteRenderer>().color;
            __instance.shopButton.inactiveSprites.GetComponent<SpriteRenderer>().color = originalColorShopButton * 0.5f;
            __instance.shopButton.activeTextColor = Color.white;
            __instance.shopButton.inactiveTextColor = Color.white;
            __instance.shopButton.inactiveSprites.GetComponent<SpriteRenderer>().color = new Color(1f, 0f, 0.35f);



            __instance.newsButton.inactiveSprites.GetComponent<SpriteRenderer>().color = new Color(0.95f, 0f, 1f);
            __instance.newsButton.activeSprites.GetComponent<SpriteRenderer>().color = new Color(1f, 0f, 0.85f);
            Color originalColorNewsButton = __instance.newsButton.inactiveSprites.GetComponent<SpriteRenderer>().color;
            __instance.newsButton.inactiveSprites.GetComponent<SpriteRenderer>().color = originalColorNewsButton * 0.6f;
            __instance.newsButton.activeTextColor = Color.white;
            __instance.newsButton.inactiveTextColor = Color.white;

            __instance.myAccountButton.inactiveSprites.GetComponent<SpriteRenderer>().color = new Color(0.95f, 0f, 1f);
            __instance.myAccountButton.activeSprites.GetComponent<SpriteRenderer>().color = new Color(1f, 0f, 0.85f);
            Color originalColorMyAccount = __instance.myAccountButton.inactiveSprites.GetComponent<SpriteRenderer>().color;
            __instance.myAccountButton.inactiveSprites.GetComponent<SpriteRenderer>().color = originalColorMyAccount * 0.6f;
            __instance.myAccountButton.activeTextColor = Color.white;
            __instance.myAccountButton.inactiveTextColor = Color.white;
            __instance.accountButtons.transform.position += new Vector3(0f, 0f, -1f);

            __instance.settingsButton.inactiveSprites.GetComponent<SpriteRenderer>().color = new Color(0.95f, 0f, 1f);
            __instance.settingsButton.activeSprites.GetComponent<SpriteRenderer>().color = new Color(1f, 0f, 0.85f);
            Color originalColorSettingsButton = __instance.settingsButton.inactiveSprites.GetComponent<SpriteRenderer>().color;
            __instance.settingsButton.inactiveSprites.GetComponent<SpriteRenderer>().color = originalColorSettingsButton * 0.6f;
            __instance.settingsButton.activeTextColor = Color.white;
            __instance.settingsButton.inactiveTextColor = Color.white;



            //__instance.creditsButton.gameObject.SetActive(false);
            //__instance.quitButton.gameObject.SetActive(false);

            __instance.quitButton.inactiveSprites.GetComponent<SpriteRenderer>().color = new Color(0.95f, 0f, 1f);
            __instance.quitButton.activeSprites.GetComponent<SpriteRenderer>().color = new Color(1f, 0f, 0.85f);
            Color originalColorQuitButton = __instance.quitButton.inactiveSprites.GetComponent<SpriteRenderer>().color;
            __instance.quitButton.inactiveSprites.GetComponent<SpriteRenderer>().color = originalColorQuitButton * 0.6f;
            __instance.quitButton.activeTextColor = Color.white;
            __instance.quitButton.inactiveTextColor = Color.white;

            __instance.creditsButton.inactiveSprites.GetComponent<SpriteRenderer>().color = new Color(0.95f, 0f, 1f);
            __instance.creditsButton.activeSprites.GetComponent<SpriteRenderer>().color = new Color(1f, 0f, 0.85f);
            Color originalColorCreditsButton = __instance.creditsButton.inactiveSprites.GetComponent<SpriteRenderer>().color;
            __instance.creditsButton.inactiveSprites.GetComponent<SpriteRenderer>().color = originalColorCreditsButton * 0.6f;
            __instance.creditsButton.activeTextColor = Color.white;
            __instance.creditsButton.inactiveTextColor = Color.white; */



        if (template == null) return;


        // GitHub Button
        if (gitHubButton == null)
        {
            gitHubButton = CreateButton(
                "GitHubButton",
                new(-1.8f, -1.1f, 1f),
                new(153, 153, 153, byte.MaxValue),
                new(209, 209, 209, byte.MaxValue),
                (UnityEngine.Events.UnityAction)(() => Application.OpenURL(Main.GitHubInviteUrl)),
                GetString("GitHub")); //"GitHub"
        }
        gitHubButton.gameObject.SetActive(Main.ShowGitHubButton);

        // Discord Button
        if (discordButton == null)
        {
            discordButton = CreateButton(
                "DiscordButton",
                new(-1.8f, -1.5f, 1f),
                new(88, 101, 242, byte.MaxValue),
                new(148, 161, byte.MaxValue, byte.MaxValue),
                (UnityEngine.Events.UnityAction)(() => Application.OpenURL(Main.DiscordInviteUrl)),
                GetString("Discord"));
        }
        discordButton.gameObject.SetActive(Main.ShowDiscordButton);

        // Website Button
        if (websiteButton == null)
        {
            websiteButton = CreateButton(
                "WebsiteButton",
                new(-1.8f, -1.9f, 1f),
                new(58, 166, 117, byte.MaxValue),
                new(118, 216, 167, byte.MaxValue),
                (UnityEngine.Events.UnityAction)(() => Application.OpenURL(Main.WebsiteUrl)),
                GetString("Website"));
        }
        websiteButton.gameObject.SetActive(Main.ShowWebsiteButton);

        if (cloudButton == null)
        {
            cloudButton = CreateButton(
                "CloudButton",
                Vector3.zero,
                new(55, 112, 153, byte.MaxValue),
                new(100, 167, 209, byte.MaxValue),
                (UnityEngine.Events.UnityAction)(() => CloudMenu.Open(__instance)),
                CloudService.MainMenuAccountLabel,
                new Vector2(1.08f, 0.84f));
            cloudButton.transform.SetParent(__instance.mainMenuUI.transform, false);
            cloudButton.transform.localPosition = Vector3.zero;
            cloudButton.transform.localScale = new Vector3(0.85f, 0.85f, 1f);
            var cloudAnchor = cloudButton.gameObject.AddComponent<AspectPosition>();
            cloudAnchor.parentCam = Camera.main;
            cloudAnchor.Alignment = AspectPosition.EdgeAlignments.RightBottom;
            cloudAnchor.DistanceFromEdge = new Vector3(3.025f, 0.58f, -7f);
            cloudAnchor.AdjustPosition();
            cloudButtonText = cloudButton.transform.Find("FontPlacer/Text_TMP").GetComponent<TMP_Text>();
            cloudButtonText.enableAutoSizing = true;
            cloudButtonText.fontSizeMin = 1.2f;
            cloudButtonText.fontSizeMax = 2.8f;
            cloudButtonText.enableWordWrapping = true;
            cloudButtonText.alignment = TextAlignmentOptions.Center;
            cloudButtonText.lineSpacing = 0f;
            cloudButtonText.paragraphSpacing = 0f;
            cloudButtonText.wordSpacing = 0f;
            cloudButtonText.characterSpacing = 0f;
            cloudButtonText.rectTransform.sizeDelta = new Vector2(1.16f, 0.87f);
        }
        cloudButton.gameObject.SetActive(false);

        var howToPlayButton = __instance.howToPlayButton;
        var freeplayButton = howToPlayButton.transform.parent.Find("FreePlayButton");

        if (freeplayButton != null) freeplayButton.gameObject.SetActive(false);

        howToPlayButton.transform.SetLocalX(0);

        CustomSoundMenu.Create(__instance);

    }

    [HarmonyPatch("LateUpdate"), HarmonyPostfix]
    public static void LateUpdate_Postfix(MainMenuManager __instance)
    {
        CustomSoundMenu.Update();
        CloudMenu.Update();
        if (cloudButton != null)
        {
            var eos = EOSManager.Instance;
            bool loginFinished = eos && eos.HasFinishedLoginFlow();
            if (cloudButton.gameObject.activeSelf != loginFinished)
                cloudButton.gameObject.SetActive(loginFinished);
        }
        if (cloudButtonText != null)
        {
            string label = CloudService.MainMenuAccountLabel;
            if (cloudButtonText.text != label) cloudButtonText.text = label;
        }
        if (MainMenuManagerStartPatch.ToheLogo == null || !MainMenuManagerStartPatch.ToheLogo.gameObject.activeInHierarchy
            || gitHubButton == null || discordButton == null || websiteButton == null) return;

        AlignLinkButton(gitHubButton, __instance.newsButton);
        AlignLinkButton(discordButton, __instance.myAccountButton);
        AlignLinkButton(websiteButton, __instance.settingsButton);
    }

    private static void AlignLinkButton(PassiveButton button, PassiveButton anchor)
    {
        if (!button.gameObject.activeInHierarchy) return;

        var anchorBounds = anchor.inactiveSprites.GetComponent<SpriteRenderer>().bounds;
        var buttonSprite = button.inactiveSprites.GetComponent<SpriteRenderer>();
        var buttonBounds = buttonSprite.bounds;
        var targetWidth = anchorBounds.size.x * 0.6f;
        if (Mathf.Abs(buttonBounds.size.x - targetWidth) > 0.001f)
        {
            var scale = button.transform.localScale;
            scale.x *= targetWidth / buttonBounds.size.x;
            button.transform.localScale = scale;
            buttonBounds = buttonSprite.bounds;
        }

        var position = button.transform.position;
        position.x += anchorBounds.max.x + 0.25f - buttonBounds.min.x;
        position.y += anchorBounds.center.y - buttonBounds.center.y;
        button.transform.position = position;
    }

    public static PassiveButton CreateButton(string name, Vector3 localPosition, Color32 normalColor, Color32 hoverColor, UnityEngine.Events.UnityAction action, string label, Vector2? scale = null)
    {
        var button = Object.Instantiate(template, MainMenuManagerStartPatch.ToheLogo.transform);
        button.name = name;
        Object.Destroy(button.GetComponent<AspectPosition>());
        button.transform.localPosition = localPosition;

        button.OnClick = new();
        button.OnClick.AddListener(action);

        var buttonText = button.transform.Find("FontPlacer/Text_TMP").GetComponent<TMP_Text>();
        buttonText.DestroyTranslator();
        buttonText.fontSize = buttonText.fontSizeMax = buttonText.fontSizeMin = 3.5f;
        buttonText.enableWordWrapping = false;
        buttonText.text = label;
        var normalSprite = button.inactiveSprites.GetComponent<SpriteRenderer>();
        var hoverSprite = button.activeSprites.GetComponent<SpriteRenderer>();
        normalSprite.color = normalColor;
        hoverSprite.color = hoverColor;

        var container = buttonText.transform.parent;
        Object.Destroy(container.GetComponent<AspectPosition>());
        Object.Destroy(buttonText.GetComponent<AspectPosition>());
        container.SetLocalX(0f);
        buttonText.transform.SetLocalX(0f);
        buttonText.horizontalAlignment = HorizontalAlignmentOptions.Center;

        var buttonCollider = button.GetComponent<BoxCollider2D>();
        if (scale.HasValue)
        {
            normalSprite.size = hoverSprite.size = buttonCollider.size = scale.Value;
        }

        buttonCollider.offset = new(0f, 0f);

        return button;
    }
    public static void Modify(this PassiveButton passiveButton, UnityEngine.Events.UnityAction action)
    {
        if (passiveButton == null) return;
        passiveButton.OnClick = new Button.ButtonClickedEvent();
        passiveButton.OnClick.AddListener(action);
    }
    public static T FindChild<T>(this MonoBehaviour obj, string name) where T : Object
    {
        string name2 = name;
        return obj.GetComponentsInChildren<T>().First((T c) => c.name == name2);
    }
    public static T FindChild<T>(this GameObject obj, string name) where T : Object
    {
        string name2 = name;
        return obj.GetComponentsInChildren<T>().First((T c) => c.name == name2);
    }
    public static void ForEach<TSource>(this IEnumerable<TSource> source, Action<TSource> action)
    {
        //if (source == null) throw new ArgumentNullException("source");
        if (source == null) throw new ArgumentNullException(nameof(source));

        IEnumerator<TSource> enumerator = source.GetEnumerator();
        while (enumerator.MoveNext())
        {
            action(enumerator.Current);
        }

        enumerator.Dispose();
    }

    [HarmonyPatch(nameof(MainMenuManager.OpenGameModeMenu))]
    [HarmonyPatch(nameof(MainMenuManager.OpenAccountMenu))]
    [HarmonyPatch(nameof(MainMenuManager.OpenCredits))]
    [HarmonyPostfix]
    public static void OpenMenu_Postfix()
    {
        CustomSoundMenu.Close();
        CloudMenu.Close();
        if (MainMenuManagerStartPatch.ToheLogo != null) MainMenuManagerStartPatch.ToheLogo.gameObject.SetActive(false);
    }
    [HarmonyPatch(nameof(MainMenuManager.ResetScreen)), HarmonyPostfix]
    public static void ResetScreen_Postfix()
    {
        if (MainMenuManagerStartPatch.ToheLogo != null) MainMenuManagerStartPatch.ToheLogo.gameObject.SetActive(true);
    }

    [HarmonyPatch("DeactivateMainMenuUI"), HarmonyPostfix]
    public static void DeactivateMainMenuUI_Postfix()
    {
        CustomSoundMenu.Close();
        CloudMenu.Close();
    }
}
