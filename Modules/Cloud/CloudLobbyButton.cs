using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TOHE.Modules;

internal static class CloudLobbyButton
{
    private static PassiveButton button;
    private static TextMeshPro label;

    internal static void Update(HudManager hud)
    {
        if (!CloudService.ShowLobbyButton)
        {
            if (button != null && button.gameObject.activeSelf) button.gameObject.SetActive(false);
            return;
        }

        if (button == null)
        {
            var start = GameStartManager.Instance;
            if (start == null || start.StartButton == null) return;
            button = Object.Instantiate(start.StartButton, hud.transform);
            button.gameObject.SetActive(false);
            button.name = "TOHEECloudLobbyButton";
            foreach (var position in button.GetComponentsInChildren<AspectPosition>(true))
            {
                position.enabled = false;
                Object.Destroy(position);
            }
            button.transform.localPosition = Vector3.zero;
            button.transform.localScale = new Vector3(0.85f, 0.85f, 1f);
            button.enabled = true;
            button.inactiveSprites.SetActive(true);
            button.activeSprites.SetActive(false);
            button.OnClick = new();
            button.OnClick.AddListener((UnityEngine.Events.UnityAction)(() => CloudMenu.Open(hud)));

            var normal = button.inactiveSprites.GetComponent<SpriteRenderer>();
            var hover = button.activeSprites.GetComponent<SpriteRenderer>();
            normal.transform.localPosition = hover.transform.localPosition = Vector3.zero;
            normal.transform.localScale = hover.transform.localScale = Vector3.one;
            normal.transform.DestroyChildren();
            hover.transform.DestroyChildren();
            normal.color = new Color32(55, 112, 153, byte.MaxValue);
            hover.color = new Color32(100, 167, 209, byte.MaxValue);
            var hitbox = button.GetComponent<BoxCollider2D>();
            normal.size = hover.size = hitbox.size = new Vector2(1.75f, 0.62f);
            hitbox.offset = Vector2.zero;
            button.activeTextColor = button.inactiveTextColor = Color.white;

            label = button.buttonText;
            label.DestroyTranslator();
            label.transform.SetParent(button.transform, false);
            // Discard the start button's offset font backing, disabled states and input glyph.
            foreach (var child in button.GetComponentsInChildren<Transform>(true))
            {
                if (child.parent != button.transform || child == label.transform
                    || child.gameObject == button.inactiveSprites || child.gameObject == button.activeSprites) continue;
                child.gameObject.SetActive(false);
                Object.Destroy(child.gameObject);
            }
            var fontTemplate = hud.GameMenu.DisableMouseMovement.Text;
            label.font = fontTemplate.font;
            label.fontSharedMaterial = fontTemplate.fontSharedMaterial;
            label.fontStyle = FontStyles.Bold;
            label.transform.localPosition = new Vector3(0f, 0f, -1f);
            label.transform.localScale = Vector3.one;
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            label.rectTransform.sizeDelta = new Vector2(1.6f, 0.53f);
            label.margin = Vector4.zero;
            label.autoSizeTextContainer = false;
            label.enableAutoSizing = true;
            label.fontSizeMin = 0.85f;
            label.fontSizeMax = 1.45f;
            label.enableWordWrapping = true;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.alignment = TextAlignmentOptions.Center;
            label.lineSpacing = label.paragraphSpacing = label.wordSpacing = label.characterSpacing = 0f;

            var anchor = button.gameObject.AddComponent<AspectPosition>();
            anchor.parentCam = hud.UICamera;
            anchor.Alignment = AspectPosition.EdgeAlignments.RightBottom;
            // Align with the use button and leave a gap to its left.
            anchor.DistanceFromEdge = new Vector3(2.1f, 0.7f, -7f);
            anchor.AdjustPosition();
        }

        string text = "CLOUD\n" + CloudService.RoomStatusText(compact: true);
        if (label.text != text) label.text = text;
        if (!button.gameObject.activeSelf) button.gameObject.SetActive(true);
    }
}
