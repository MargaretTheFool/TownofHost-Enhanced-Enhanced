using System.Linq;
using UnityEngine;

namespace TOHE.Modules;

// The three lists correspond to Overloaded's meeting, shapeshifter and vitals paging.
internal static class PlayerListScroll
{
    private static View meeting;
    private static View shifter;
    private static View vitals;
    private static View active;
    private static Sprite white;

    internal static void Begin(ShapeshifterMinigame minigame)
    {
        var items = minigame.potentialVictims.ToArray();
        shifter = items.Length > 15
            ? new View(minigame, minigame.transform, items.Select(item => item.transform).ToArray(),
                minigame.XStart, minigame.YStart, minigame.XOffset, minigame.YOffset, false)
            : null;
    }

    internal static void Begin(VitalsMinigame minigame)
    {
        var items = minigame.vitals.ToArray();
        vitals = items.Length > 15
            ? new View(minigame, minigame.transform, items.Select(item => item.transform).ToArray(),
                minigame.XStart, minigame.YStart, minigame.XOffset, minigame.YOffset, false)
            : null;
    }

    internal static void Update(PassiveButtonManager manager)
    {
        var hud = MeetingHud.Instance;
        if (hud && hud.playerStates != null && hud.playerStates.Length > 15 && (meeting == null || meeting.Owner != hud))
        {
            var items = hud.playerStates.OrderBy(item => item.AmDead).ThenBy(item => item.PlayerId.Value)
                .Select(item => item.transform).ToArray();
            meeting = new View(hud, hud.ButtonParent, items, hud.VoteOrigin.x, hud.VoteOrigin.y,
                hud.VoteButtonOffsets.x, hud.VoteButtonOffsets.y, true);
        }

        View next = null;
        if (shifter != null && shifter.Owner && shifter.Owner.gameObject.activeInHierarchy) next = shifter;
        else if (vitals != null && vitals.Owner && vitals.Owner.gameObject.activeInHierarchy) next = vitals;
        else if (meeting != null && meeting.Owner && meeting.Owner.gameObject.activeInHierarchy) next = meeting;

        if (active != next)
        {
            active?.Cancel();
            active = next;
        }
        active?.Tick(manager);
    }

    private sealed class View
    {
        internal readonly MonoBehaviour Owner;
        private readonly Transform parent;
        private readonly Transform[] items;
        private readonly float[] depths;
        private readonly VitalsMinigame vitalsOwner;
        private readonly bool isMeeting;
        private readonly float xStart, yStart, xStep, yStep, bottom, maxOffset, barX, trackHeight;
        private readonly GameObject bar;
        private readonly Transform knob;
        private float offset;
        private bool pressed, dragging, draggingBar, sabotage;
        private Vector2 pressScreen;
        private float pressY, pressOffset;
        private PassiveButton pressedButton;
        private bool oldDown, oldUp, oldRepeat;
        private int restoreFrame = -1;

        internal View(MonoBehaviour owner, Transform parent, Transform[] items,
            float xStart, float yStart, float xStep, float yStep, bool isMeeting)
        {
            Owner = owner;
            vitalsOwner = owner.TryCast<VitalsMinigame>();
            this.parent = parent;
            this.items = items;
            this.xStart = xStart;
            this.yStart = yStart;
            this.xStep = xStep;
            this.yStep = yStep;
            this.isMeeting = isMeeting;
            bottom = yStart + 4f * yStep;
            maxOffset = (Mathf.CeilToInt(items.Length / 3f) - 5) * -yStep;
            barX = xStart + 2f * xStep + (isMeeting ? 1.48f : 1.02f);
            trackHeight = yStart - bottom + 0.4f;
            depths = items.Select(item => item.localPosition.z).ToArray();

            if (!white) white = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            bar = new GameObject("TOHEPlayerListScrollbar");
            bar.SetActive(false);
            bar.layer = parent.gameObject.layer;
            bar.transform.SetParent(parent, false);
            bar.transform.localPosition = new Vector3(barX, (yStart + bottom) / 2f, -4f);
            var hitbox = bar.AddComponent<BoxCollider2D>();
            hitbox.size = new Vector2(0.55f, trackHeight);
            var button = bar.AddComponent<PassiveButton>();
            button.OnClick = new();
            button.OnMouseOver = new();
            button.OnMouseOut = new();
            button.OnUp = false;
            button.OnDown = false;
            var track = bar.AddComponent<SpriteRenderer>();
            track.sprite = white;
            track.drawMode = SpriteDrawMode.Sliced;
            track.size = new Vector2(0.055f, trackHeight);
            track.color = new Color(0.2f, 0.25f, 0.27f, 0.85f);
            var knobObject = new GameObject("Handle");
            knobObject.layer = bar.layer;
            knobObject.transform.SetParent(bar.transform, false);
            knobObject.transform.localPosition = new Vector3(0f, trackHeight / 2f, -0.1f);
            var handle = knobObject.AddComponent<SpriteRenderer>();
            handle.sprite = white;
            handle.drawMode = SpriteDrawMode.Sliced;
            handle.size = new Vector2(0.16f, Mathf.Max(0.28f, trackHeight * 15f / items.Length));
            handle.color = new Color(0.8f, 0.9f, 0.9f, 1f);
            knob = knobObject.transform;
            bar.SetActive(true);
            Layout();
        }

        internal void Tick(PassiveButtonManager manager)
        {
            if (restoreFrame >= 0 && Time.frameCount > restoreFrame) RestoreButton();

            if (vitalsOwner)
            {
                bool nowSabotaged = vitalsOwner.SabText.gameObject.activeSelf;
                if (sabotage != nowSabotaged)
                {
                    sabotage = nowSabotaged;
                    bar.SetActive(!sabotage);
                    if (!sabotage) Layout();
                }
                if (sabotage) return;
            }

            if (!Application.isFocused || (HudManager.Instance && HudManager.Instance.Chat && HudManager.Instance.Chat.IsOpenOrOpening))
            {
                if (pressed) { manager.controller.ClearTouch(); Cancel(); }
                return;
            }

            var camera = Camera.main;
            if (!camera) return;
            var mouse = Input.mousePosition;
            var world = camera.ScreenToWorldPoint(new Vector3(mouse.x, mouse.y, parent.position.z - camera.transform.position.z));
            var local = parent.InverseTransformPoint(world);
            float left = xStart - Mathf.Abs(xStep) / 2f;
            float right = xStart + 2f * xStep + Mathf.Abs(xStep) / 2f;
            bool overList = local.x >= left && local.x <= right && local.y >= bottom - Mathf.Abs(yStep) / 2f && local.y <= yStart + Mathf.Abs(yStep) / 2f;
            bool overBar = Mathf.Abs(local.x - barX) <= 0.3f && local.y >= bottom - 0.2f && local.y <= yStart + 0.2f;

            bool mouseDown = Input.GetMouseButtonDown(0);
            float wheel = Input.mouseScrollDelta.y;
            if (!pressed && (mouseDown || wheel != 0f) && (overList || overBar) && !BlockedByOtherUi(manager, world))
            {
                if (wheel != 0f) SetOffset(offset + wheel * yStep);
                if (mouseDown)
                {
                    pressed = true;
                    draggingBar = overBar;
                    dragging = overBar;
                    pressScreen = mouse;
                    pressY = local.y;
                    pressOffset = offset;
                    if (overBar)
                    {
                        manager.controller.ClearTouch();
                        SetOffset(Mathf.InverseLerp(yStart + 0.2f, bottom - 0.2f, local.y) * maxOffset);
                    }
                    else
                    {
                        // Delay a list button's down action until release so a drag cannot select it.
                        foreach (var element in manager.Buttons)
                        {
                            if (!element || !element.isActiveAndEnabled || !IsListItem(element)) continue;
                            var candidate = element.TryCast<PassiveButton>();
                            if (!candidate || !candidate.Colliders.Any(collider => collider && collider.OverlapPoint(world))) continue;
                            pressedButton = candidate;
                            oldDown = candidate.OnDown;
                            oldUp = candidate.OnUp;
                            oldRepeat = candidate.OnRepeat;
                            candidate.OnDown = false;
                            candidate.OnUp = true;
                            candidate.OnRepeat = false;
                            break;
                        }
                    }
                }
            }

            if (pressed && Input.GetMouseButton(0))
            {
                if (!dragging && Vector2.Distance(pressScreen, mouse) > 8f)
                {
                    dragging = true;
                    manager.controller.ClearTouch();
                }
                if (dragging)
                    SetOffset(draggingBar
                        ? Mathf.InverseLerp(yStart + 0.2f, bottom - 0.2f, local.y) * maxOffset
                        : pressOffset + local.y - pressY);
            }
            if (pressed && Input.GetMouseButtonUp(0))
            {
                if (dragging) manager.controller.ClearTouch();
                pressed = dragging = draggingBar = false;
                restoreFrame = Time.frameCount;
            }
        }

        private bool IsListItem(PassiveUiElement element)
        {
            if (!element.transform.IsChildOf(parent)) return false;
            return isMeeting ? element.GetComponentInParent<PlayerVoteArea>() != null :
                element.GetComponentInParent<ShapeshifterPanel>() != null;
        }

        private bool BlockedByOtherUi(PassiveButtonManager manager, Vector2 world)
        {
            foreach (var element in manager.Buttons)
            {
                if (!element || !element.isActiveAndEnabled || element.transform.IsChildOf(bar.transform) || IsListItem(element)) continue;
                if (element.transform.position.z > items[0].position.z) continue;
                if (element.ClickMask && !element.ClickMask.OverlapPoint(world)) continue;
                if (element.Colliders != null && element.Colliders.Any(collider => collider && collider.isActiveAndEnabled && collider.OverlapPoint(world))) return true;
            }
            return false;
        }

        private void SetOffset(float value)
        {
            value = Mathf.Clamp(value, 0f, maxOffset);
            if (Mathf.Approximately(value, offset)) return;
            offset = value;
            Layout();
        }

        private void Layout()
        {
            for (int i = 0; i < items.Length; i++)
            {
                float y = yStart + i / 3 * yStep + offset;
                bool visible = y <= yStart + 0.001f && y >= bottom - 0.001f;
                var item = items[i];
                if (item.gameObject.activeSelf != visible) item.gameObject.SetActive(visible);
                if (visible) item.localPosition = new Vector3(xStart + i % 3 * xStep, y, depths[i]);
            }
            knob.localPosition = new Vector3(0f, Mathf.Lerp(trackHeight / 2f, -trackHeight / 2f, offset / maxOffset), -0.1f);
        }

        internal void Cancel()
        {
            pressed = dragging = draggingBar = false;
            RestoreButton();
        }

        private void RestoreButton()
        {
            if (pressedButton)
            {
                pressedButton.OnDown = oldDown;
                pressedButton.OnUp = oldUp;
                pressedButton.OnRepeat = oldRepeat;
            }
            pressedButton = null;
            restoreFrame = -1;
        }
    }
}

[HarmonyPatch(typeof(ShapeshifterMinigame), nameof(ShapeshifterMinigame.Begin))]
internal static class OverloadedShapeshifterScrollPatch
{
    private static void Postfix(ShapeshifterMinigame __instance) => PlayerListScroll.Begin(__instance);
}

[HarmonyPatch(typeof(VitalsMinigame), nameof(VitalsMinigame.Begin))]
internal static class OverloadedVitalsScrollPatch
{
    private static void Postfix(VitalsMinigame __instance) => PlayerListScroll.Begin(__instance);
}

[HarmonyPatch(typeof(PassiveButtonManager), nameof(PassiveButtonManager.Update))]
internal static class OverloadedScrollInputPatch
{
    private static void Prefix(PassiveButtonManager __instance) => PlayerListScroll.Update(__instance);
}
