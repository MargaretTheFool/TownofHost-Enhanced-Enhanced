using System;
using System.Globalization;
using TMPro;
using TOHE.Modules;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace TOHE;

internal static class CustomSoundMenu
{
    private const string OverlayName = "TOHECustomSounds";
    private const float RowSpacing = 0.46f;
    private static readonly List<(GameObject Root, TextBoxTMP Input)> Rows = [];
    private static PassiveButton entry;
    private static SpriteRenderer entryImage;
    private static Sprite enabledIcon, mutedIcon, circle, white;
    private static GameObject panel;
    private static Transform listContent;
    private static TextBoxTMP inputTemplate;
    private static TextBoxTMP masterInput;
    private static TextMeshPro previewLabel;
    private static string selectedSound = "Teleport";
    private static bool lastEnabled;
    private static bool sceneHooked;

    internal static void Create(MainMenuManager menu)
    {
        if (entry != null) Object.Destroy(entry.gameObject);
        Close();
        if (inputTemplate == null)
        {
            var template = Il2CppHelper.FindObjectsOfTypeAll<TextBoxTMP>()
                .FirstOrDefault(x => x.name == "EnterCodeField" && x.gameObject.scene.name == "MainMenu");
            if (template != null)
            {
                inputTemplate = Object.Instantiate(template);
                inputTemplate.gameObject.SetActive(false);
                inputTemplate.name = "TOHECustomSoundInputTemplate";
                Object.DontDestroyOnLoad(inputTemplate.gameObject);
            }
        }
        if (!sceneHooked)
        {
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded += (UnityAction<UnityEngine.SceneManagement.Scene>)(scene =>
            {
                if (panel == null || panel.scene == scene) Close();
            });
            sceneHooked = true;
        }

        var root = new GameObject("TOHECustomSoundsButton");
        root.SetActive(false);
        root.transform.SetParent(menu.mainMenuUI.transform, false);
        root.transform.localScale = Vector3.one * 0.72f;
        entryImage = root.AddComponent<SpriteRenderer>();
        entryImage.sprite = enabledIcon ? enabledIcon : enabledIcon = DrawIcon(false);
        var hitbox = root.AddComponent<CircleCollider2D>();
        hitbox.radius = 0.5f;
        entry = root.AddComponent<PassiveButton>();
        entry.OnMouseOver = new();
        entry.OnMouseOut = new();
        entry.OnClick = new();
        entry.OnClick.AddListener((UnityAction)(() => Open(menu)));
        var anchor = root.AddComponent<AspectPosition>();
        anchor.parentCam = Camera.main;
        anchor.Alignment = AspectPosition.EdgeAlignments.RightBottom;
        anchor.DistanceFromEdge = new Vector3(2.12f, 0.58f, -7f);
        root.SetActive(true);
        RefreshEnabled();
    }

    internal static void RefreshEnabled()
    {
        var enabled = Main.EnableCustomSoundEffect.Value;
        if (!enabled)
        {
            Close();
            CustomSoundsManager.StopAll();
        }
        if (entry != null)
        {
            entry.enabled = enabled;
            entryImage.sprite = enabled ? enabledIcon : mutedIcon ? mutedIcon : mutedIcon = DrawIcon(true);
        }
        lastEnabled = enabled;
    }

    internal static void SetMenuVisible(bool visible)
    {
        if (!visible) Close();
        if (entry != null) entry.gameObject.SetActive(visible);
    }

    internal static void SyncMenuVisibility(bool menuVisible)
    {
        var optionsOpen = Il2CppHelper.FindObjectsOfTypeAll<OptionsMenuBehaviour>()
            .Any(menu => menu.gameObject.scene.name == "MainMenu" && menu.gameObject.activeInHierarchy);
        SetMenuVisible(menuVisible && !optionsOpen);
    }

    private static void SelectSound(string id)
    {
        selectedSound = id;
        if (previewLabel != null)
            previewLabel.text = string.Format(Translator.GetString("CustomSoundsPreview"), id);
    }

    internal static void Update()
    {
        if (lastEnabled != Main.EnableCustomSoundEffect.Value) RefreshEnabled();
        if (panel == null) return;
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
            return;
        }

        foreach (var row in Rows)
        {
            float y = row.Root.transform.localPosition.y + listContent.localPosition.y;
            bool visible = y <= 1.05f && y >= -1.95f;
            if (row.Root.activeSelf && !visible) row.Input.LoseFocus();
            if (row.Root.activeSelf != visible) row.Root.SetActive(visible);
        }
    }

    internal static void Close()
    {
        if (panel != null)
        {
            masterInput?.LoseFocus();
            foreach (var row in Rows) if (row.Input != null) row.Input.LoseFocus();
            if (ControllerManager.Instance != null && ControllerManager.Instance.IsMenuActiveAtAll(OverlayName))
                ControllerManager.Instance.CloseOverlayMenu(OverlayName);
            Object.Destroy(panel);
        }
        panel = null;
        listContent = null;
        masterInput = null;
        previewLabel = null;
        Rows.Clear();
        CustomSoundsManager.StopPreview();
    }

    private static void Open(MainMenuManager menu)
    {
        var options = Il2CppHelper.FindObjectsOfTypeAll<OptionsMenuBehaviour>()
            .FirstOrDefault(x => x.gameObject.scene.name == "MainMenu");
        if (options != null && options.gameObject.activeInHierarchy) return;
        var labelTemplate = options != null ? options.DisableMouseMovement.Text : menu.quitButton.GetComponentInChildren<TextMeshPro>(true);
        Open(menu.transform, labelTemplate, labelTemplate.gameObject.layer, null);
    }

    internal static void Open(OptionsMenuBehaviour options)
    {
        if (options == null || !options.gameObject.activeInHierarchy) return;
        Open(options.transform, options.DisableMouseMovement.Text, options.DisableMouseMovement.Text.gameObject.layer, options.DisableMouseMovement.Text);
    }

    private static void Open(Transform parent, TextMeshPro labelTemplate, int uiLayer, TextMeshPro inputStyle)
    {
        if (panel != null) return;
        if (inputTemplate == null)
        {
            Logger.Warn("Custom sound text input template is unavailable", "CustomSounds");
            return;
        }

        panel = new GameObject(OverlayName);
        panel.transform.SetParent(parent, false);
        panel.transform.localPosition = new Vector3(0f, 0f, -20f);
        var sortingGroup = panel.AddComponent<UnityEngine.Rendering.SortingGroup>();
        sortingGroup.sortingOrder = 100;

        var blocker = new GameObject("ClickBlocker");
        blocker.transform.SetParent(panel.transform, false);
        blocker.transform.localPosition = new Vector3(0f, 0f, 1f);
        var blockerCollider = blocker.AddComponent<BoxCollider2D>();
        blockerCollider.size = new Vector2(30f, 20f);
        var blockerButton = blocker.AddComponent<PassiveButton>();
        blockerButton.OnMouseOver = new();
        blockerButton.OnMouseOut = new();
        blockerButton.OnClick = new();

        if (!white) white = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f, 0, SpriteMeshType.FullRect);
        Surface(panel.transform, "Dimmer", new Vector3(0f, 0f, 0.5f), new Vector2(30f, 20f), new Color(0f, 0f, 0f, 0.58f));

        var background = new GameObject("Background");
        background.transform.SetParent(panel.transform, false);
        var surface = background.AddComponent<SpriteRenderer>();
        surface.sprite = PresetSelectorVisuals.Panel;
        surface.drawMode = SpriteDrawMode.Sliced;
        surface.size = new Vector2(7.2f, 5.5f);

        Label(panel.transform, "Title", Translator.GetString("CustomSoundsTitle"), new Vector3(-1.45f, 2.34f, -2f), new Vector2(3.5f, 0.5f), 2.7f, TextAlignmentOptions.Left);
        previewLabel = Label(panel.transform, "PreviewSelection", string.Format(Translator.GetString("CustomSoundsPreview"), selectedSound), new Vector3(1.05f, 2.34f, -2f), new Vector2(2.3f, 0.4f), 1.55f, TextAlignmentOptions.Right);
        previewLabel.color = new Color32(180, 216, 215, 255);
        var close = Button(panel.transform, "Close", new Vector3(2.7f, 2.3f, -3f), new Vector2(0.55f, 0.45f), "X", () => Close());
        Surface(panel.transform, "HeaderDivider", new Vector3(0f, 2.03f, -1f), new Vector2(6.55f, 0.018f), new Color(0.36f, 0.48f, 0.5f, 0.75f));
        Label(panel.transform, "MasterLabel", Translator.GetString("CustomSoundsMaster"), new Vector3(-2.0f, 1.7f, -2f), new Vector2(2.4f, 0.45f), 2.2f, TextAlignmentOptions.Left);
        VolumeControl(panel.transform, null, 1.7f, out masterInput);
        var listHint = Label(panel.transform, "ListHint", Translator.GetString("CustomSoundsScrollHint"), new Vector3(-1.55f, -2.35f, -2f), new Vector2(3.3f, 0.3f), 1.35f, TextAlignmentOptions.Left);
        listHint.color = new Color32(180, 207, 207, 255);
        Surface(panel.transform, "ListDivider", new Vector3(0f, 1.3f, -1f), new Vector2(6.55f, 0.018f), new Color(0.36f, 0.48f, 0.5f, 0.75f));

        var scrollObject = new GameObject("SoundListScroller");
        scrollObject.SetActive(false);
        scrollObject.transform.SetParent(panel.transform, false);
        scrollObject.transform.localPosition = new Vector3(0f, 0f, -0.5f);
        var scrollHitbox = scrollObject.AddComponent<BoxCollider2D>();
        scrollHitbox.size = new Vector2(6.8f, 3.2f);
        scrollHitbox.offset = new Vector2(0f, -0.45f);
        var scroller = scrollObject.AddComponent<Scroller>();
        scroller.OnMouseOver = new();
        scroller.OnMouseOut = new();
        var content = new GameObject("Content");
        content.transform.SetParent(scrollObject.transform, false);
        content.transform.localPosition = new Vector3(0f, 0f, -0.5f);
        listContent = content.transform;
        scroller.Inner = listContent;
        scroller.allowY = true;
        scroller.allowX = false;
        scroller.showY = false;
        scroller.MouseMustBeOverToScroll = true;
        scroller.ClickMask = scrollHitbox;
        scroller.ScrollWheelSpeed = 0.48f;
        scroller.SetBounds(new FloatRange(0f, (CustomSoundsManager.EffectIds.Length - 7) * RowSpacing), new FloatRange(0f, 0f));

        for (int i = 0; i < CustomSoundsManager.EffectIds.Length; i++)
        {
            var id = CustomSoundsManager.EffectIds[i];
            var effect = CustomSoundsManager.Settings.Effects[id];
            var row = new GameObject("Sound_" + id);
            row.transform.SetParent(listContent, false);
            row.transform.localPosition = new Vector3(0f, 0.95f - i * RowSpacing, -1f);
            Surface(row.transform, "RowBackground", new Vector3(0f, 0f, -0.1f), new Vector2(6.55f, 0.41f), new Color(0.12f, 0.19f, 0.21f, 0.6f));
            Label(row.transform, "Name", id, new Vector3(-2.25f, 0f, -1f), new Vector2(1.8f, 0.4f), 1.9f, TextAlignmentOptions.Center);
            PassiveButton toggle = null;
            toggle = Button(row.transform, "Toggle_" + id, new Vector3(-0.95f, 0f, -1f), new Vector2(0.7f, 0.34f),
                Translator.GetString(effect.Enabled ? "CustomSoundsOn" : "CustomSoundsOff"), () =>
                {
                    SelectSound(id);
                    effect.Enabled = !effect.Enabled;
                    toggle.GetComponentInChildren<TextMeshPro>(true).text = Translator.GetString(effect.Enabled ? "CustomSoundsOn" : "CustomSoundsOff");
                    toggle.GetComponent<SpriteRenderer>().color = effect.Enabled ? new Color32(93, 152, 129, 255) : new Color32(91, 98, 105, 255);
                    CustomSoundsManager.Settings.Save();
                });
            toggle.GetComponent<SpriteRenderer>().color = effect.Enabled ? new Color32(93, 152, 129, 255) : new Color32(91, 98, 105, 255);
            VolumeControl(row.transform, id, 0f, out var input);
            Rows.Add((row, input));
        }
        Button(panel.transform, "ScrollUp", new Vector3(2.0f, -2.35f, -3f), new Vector2(0.8f, 0.34f), Translator.GetString("CustomSoundsUp"), () => scroller.ScrollRelative(new Vector2(0f, -3 * RowSpacing)));
        Button(panel.transform, "ScrollDown", new Vector3(3.0f, -2.35f, -3f), new Vector2(0.9f, 0.34f), Translator.GetString("CustomSoundsDown"), () => scroller.ScrollRelative(new Vector2(0f, 3 * RowSpacing)));
        scrollObject.SetActive(true);
        Update();
        foreach (var item in panel.GetComponentsInChildren<Transform>(true)) item.gameObject.layer = uiLayer;
        ControllerManager.Instance?.OpenOverlayMenu(OverlayName, close);

        SpriteRenderer Surface(Transform parent, string name, Vector3 position, Vector2 size, Color color)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position;
            var renderer = obj.AddComponent<SpriteRenderer>();
            renderer.sprite = white;
            renderer.drawMode = SpriteDrawMode.Sliced;
            renderer.size = size;
            renderer.color = color;
            return renderer;
        }

        TextMeshPro Label(Transform parent, string name, string text, Vector3 position, Vector2 size, float fontSize, TextAlignmentOptions alignment)
        {
            var label = Object.Instantiate(labelTemplate, parent);
            label.name = name;
            label.DestroyTranslator();
            var anchor = label.GetComponent<AspectPosition>();
            if (anchor != null)
            {
                anchor.enabled = false;
                Object.Destroy(anchor);
            }
            label.transform.localPosition = position;
            label.transform.localScale = Vector3.one;
            label.rectTransform.sizeDelta = size;
            label.margin = Vector4.zero;
            label.alignment = alignment;
            label.enableAutoSizing = true;
            label.fontSize = label.fontSizeMax = fontSize;
            label.fontSizeMin = 1f;
            label.color = Color.white;
            label.text = text;
            label.gameObject.SetActive(true);
            return label;
        }

        PassiveButton Button(Transform parent, string name, Vector3 position, Vector2 size, string caption, Action action)
        {
            var obj = new GameObject(name);
            obj.SetActive(false);
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position;
            var renderer = obj.AddComponent<SpriteRenderer>();
            renderer.sprite = white;
            renderer.drawMode = SpriteDrawMode.Sliced;
            renderer.size = size;
            renderer.color = new Color32(65, 91, 96, 255);
            var collider = obj.AddComponent<BoxCollider2D>();
            collider.size = size;
            var button = obj.AddComponent<PassiveButton>();
            button.OnMouseOver = new();
            button.OnMouseOut = new();
            button.OnClick = new();
            button.OnClick.AddListener((UnityAction)(() => action()));
            Label(obj.transform, "Label", caption, new Vector3(0f, 0f, -0.2f), size, 1.8f, TextAlignmentOptions.Center);
            obj.SetActive(true);
            return button;
        }

        void VolumeControl(Transform parent, string id, float y, out TextBoxTMP input)
        {
            var settings = CustomSoundsManager.Settings;
            int current = id == null ? settings.MasterVolume : settings.Effects[id].Volume;
            var sliderObject = new GameObject(id == null ? "MasterSlider" : "Slider_" + id);
            sliderObject.SetActive(false);
            sliderObject.transform.SetParent(parent, false);
            sliderObject.transform.localPosition = new Vector3(0.65f, y, -3f);
            var sliderHitbox = sliderObject.AddComponent<BoxCollider2D>();
            sliderHitbox.size = new Vector2(1.8f, 0.38f);
            var slider = sliderObject.AddComponent<SlideBar>();
            slider.OnMouseOver = new();
            slider.OnMouseOut = new();
            var barObject = new GameObject("Bar");
            barObject.transform.SetParent(sliderObject.transform, false);
            var bar = barObject.AddComponent<SpriteRenderer>();
            bar.sprite = white;
            bar.drawMode = SpriteDrawMode.Sliced;
            bar.size = new Vector2(1.5f, 0.05f);
            var dotObject = new GameObject("Dot");
            dotObject.transform.SetParent(barObject.transform, false);
            dotObject.transform.localPosition = new Vector3(0f, 0f, -0.1f);
            dotObject.transform.localScale = Vector3.one * 0.2f;
            var dot = dotObject.AddComponent<SpriteRenderer>();
            dot.sprite = circle ? circle : circle = DrawIcon(false, false);
            slider.Bar = bar;
            slider.Dot = dot;
            slider.Range = new FloatRange(-0.75f, 0.75f);
            slider.OnValueChange = new();

            var field = Object.Instantiate(inputTemplate, parent);
            field.gameObject.SetActive(false);
            var inputButton = field.GetComponent<PassiveButton>();
            inputButton.OnMouseOver = new();
            inputButton.OnMouseOut = new();
            inputButton.OnClick = new();
            inputButton.OnUp = true;
            inputButton.OnDown = false;
            inputButton.OnRepeat = false;
            inputButton.HoldToUse = false;
            inputButton.OnClick.AddListener((UnityAction)(() =>
            {
                DestroyableSingleton<PassiveButtonManager>.Instance.LoseFocusForAll();
                field.GiveFocus();
            }));
            field.name = id == null ? "MasterVolumeInput" : "VolumeInput_" + id;
            field.transform.localPosition = new Vector3(2.45f, y, -4f);
            field.transform.localScale = Vector3.one;
            field.GetComponent<BoxCollider2D>().size = new Vector2(1.3f, 0.36f);
            field.Background.size = new Vector2(1.3f, 0.4f);
            field.Background.maskInteraction = SpriteMaskInteraction.None;
            TextMeshPro valueLabel = null;
            if (inputStyle != null) field.outputText.GetComponent<Renderer>().enabled = false;
            field.outputText.transform.localScale = Vector3.one;
            field.outputText.fontSize = field.outputText.fontSizeMax = 1.9f;
            field.outputText.fontSizeMin = 0.8f;
            field.outputText.enableAutoSizing = true;
            field.outputText.rectTransform.sizeDelta = new Vector2(1.2f, 0.35f);
            field.outputText.alignment = TextAlignmentOptions.Center;
            Label(parent, "Percent", "%", new Vector3(3.22f, y, -4f), new Vector2(0.3f, 0.35f), 1.7f, TextAlignmentOptions.Center);
            if (field.placeholderText != null) field.placeholderText.gameObject.SetActive(false);
            field.placeholderText = null;
            var checkmark = field.transform.Find("Checkmark");
            if (checkmark != null) checkmark.gameObject.SetActive(false);
            field.characterLimit = 0;
            field.IpMode = false;
            field.AllowSymbols = true;
            field.AllowPaste = true;
            field.ClearOnFocus = true;
            field.ClearOnlyPlaceholderOnFocus = false;
            field.SendOnFullChars = false;
            field.OnChange = new();
            field.OnEnter = new();
            field.OnFocusLost = new();
            field.OnFocus = new();
            field.OnChange.AddListener((UnityAction)(() =>
            {
                if (valueLabel != null) valueLabel.text = field.text;
            }));
            field.SetText(current.ToString(CultureInfo.InvariantCulture));
            field.OnFocus.AddListener((UnityAction)(() =>
            {
                if (id != null) SelectSound(id);
                if (valueLabel != null) valueLabel.text = field.text;
            }));
            field.OnEnter.AddListener((UnityAction)Commit);
            field.OnFocusLost.AddListener((UnityAction)Commit);
            slider.OnValueChange.AddListener((UnityAction)(() =>
            {
                if (id != null) SelectSound(id);
                int value = (int)Math.Round(slider.Value * 100f);
                if (value == current) return;
                current = value;
                if (id == null) settings.MasterVolume = value;
                else settings.Effects[id].Volume = value;
                field.SetText(value.ToString(CultureInfo.InvariantCulture));
                settings.Save();
                CustomSoundsManager.Preview(id ?? selectedSound);
            }));
            slider.SetValue(current / 100f);
            sliderObject.SetActive(true);
            field.gameObject.SetActive(true);
            if (inputStyle != null)
            {
                valueLabel = Label(field.transform, "Value", field.text, new Vector3(0f, 0f, 31f), new Vector2(1.2f, 0.35f), 1.9f, TextAlignmentOptions.Center);
                valueLabel.ForceMeshUpdate(true, true);
            }
            input = field;

            void Commit()
            {
                if (!int.TryParse(field.text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value > 500)
                {
                    field.SetText(current.ToString(CultureInfo.InvariantCulture));
                    return;
                }
                field.SetText(value.ToString(CultureInfo.InvariantCulture));
                if (value == current) return;
                current = value;
                slider.SetValue(value / 100f);
                if (id == null) settings.MasterVolume = value;
                else settings.Effects[id].Volume = value;
                settings.Save();
                CustomSoundsManager.Preview(id ?? selectedSound);
            }
        }
    }

    private static Sprite DrawIcon(bool muted, bool speaker = true)
    {
        const int size = 96;
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float px = (x + 0.5f) / size * 2f - 1f;
            float py = (y + 0.5f) / size * 2f - 1f;
            float distance = Mathf.Sqrt(px * px + py * py);
            Color color = distance > 0.97f ? Color.clear : distance > 0.87f ? (Color)new Color32(248, 185, 203, 255) : new Color32(35, 52, 59, 255);
            bool horn = px >= -0.53f && px <= -0.19f && Mathf.Abs(py) <= 0.20f
                || px > -0.19f && px <= 0.14f && Mathf.Abs(py) <= 0.20f + (px + 0.19f) * 0.75f;
            float arc = Mathf.Sqrt((px - 0.10f) * (px - 0.10f) + py * py);
            bool waves = px > 0.22f && (Mathf.Abs(arc - 0.34f) < 0.055f || Mathf.Abs(arc - 0.57f) < 0.055f);
            bool cross = px > 0.18f && px < 0.65f && (Mathf.Abs(py - (px - 0.42f)) < 0.065f || Mathf.Abs(py + (px - 0.42f)) < 0.065f);
            if (speaker && (horn || (muted ? cross : waves))) color = Color.white;
            if (!speaker) color = distance < 0.9f ? Color.white : Color.clear;
            pixels[y * size + x] = color;
        }
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        var sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}
