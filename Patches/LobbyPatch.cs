using BepInEx.Unity.IL2CPP.Utils.Collections;
using TMPro;
using UnityEngine;

namespace TOHE.Patches;

[HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Start))]
public class LobbyStartPatch
{
    private static GameObject LobbyPaintObject;
    private static GameObject DropshipDecorationsObject;
    private static Sprite LobbyPaintSprite;
    private static Sprite DropshipDecorationsSprite;

    private static bool FirstDecorationsLoad = true;
    public static void Prefix()
    {
        LobbyPaintSprite = Utils.LoadSprite("TOHE.Resources.Images.LobbyPaint.png", 881.71875f);
        DropshipDecorationsSprite = Utils.LoadSprite("TOHE.Resources.Images.Dropship-Decorations.png", 60f);
    }
    public static void Postfix(LobbyBehaviour __instance)
    {
        float waitTime = 0f;
        if (FirstDecorationsLoad)
            waitTime = 0.25f;
        else
            waitTime = 0.05f;

        _ = new LateTask(() =>
        {
            __instance.StartCoroutine(CoLoadDecorations().WrapToIl2Cpp());
        }, waitTime, "Co Load Dropship Decorations", shoudLog: false);

        static System.Collections.IEnumerator CoLoadDecorations()
        {
            var LeftBox = GameObject.Find("Leftbox");
            if (LeftBox != null)
            {
                LobbyPaintObject = Object.Instantiate(LeftBox, LeftBox.transform.parent.transform);
                LobbyPaintObject.name = "Lobby Paint";
                LobbyPaintObject.transform.localPosition = new Vector3(0.042f, -2.59f, -10.5f);
                SpriteRenderer renderer = LobbyPaintObject.GetComponent<SpriteRenderer>();
                renderer.sprite = LobbyPaintSprite;
            }

            yield return null;

            if (Main.EnableCustomDecorations.Value)
            {
                var Dropship = GameObject.Find("SmallBox");
                if (Dropship != null)
                {
                    DropshipDecorationsObject = Object.Instantiate(Dropship, Object.FindAnyObjectByType<LobbyBehaviour>().transform);
                    DropshipDecorationsObject.name = "Lobby_Decorations";
                    DropshipDecorationsObject.transform.DestroyChildren();
                    Object.Destroy(DropshipDecorationsObject.GetComponent<PolygonCollider2D>());
                    DropshipDecorationsObject.GetComponent<SpriteRenderer>().sprite = DropshipDecorationsSprite;
                    DropshipDecorationsObject.transform.SetSiblingIndex(1);
                    DropshipDecorationsObject.transform.localPosition = new(0.05f, 0.8334f);
                }
            }

            yield return null;

            FirstDecorationsLoad = false;
        }
    }
}
// https://github.com/SuperNewRoles/SuperNewRoles/blob/master/SuperNewRoles/Patches/LobbyBehaviourPatch.cs
public class LobbyBehaviourPatch
{
    // Called from the existing substantive GameStartManager.Update entry.
    // LobbyBehaviour.Update is only a friends-cache forwarding wrapper in source.
    public static void UpdateMusic(LobbyBehaviour lobby)
    {
        if (lobby == null || lobby != LobbyBehaviour.Instance || !lobby.gameObject.activeInHierarchy ||
            !GameStates.IsLobby || SoundManager.Instance == null) return;
        var soundManager = SoundManager.Instance;
        bool playing = false;
        // Index the native list; no delegate/conversion is rebuilt on each frame.
        for (int i = 0; i < soundManager.soundPlayers.Count; i++)
        {
            var player = soundManager.soundPlayers[i];
            if (player != null && player.Name == "MapTheme") { playing = true; break; }
        }
        if (Main.DisableLobbyMusic.Value)
        {
            if (!playing) return;
            soundManager.StopNamedSound("MapTheme");
        }
        else
        {
            if (playing) return;
            soundManager.CrossFadeSound("MapTheme", lobby.MapTheme, 0.5f);
        }
    }
}
[HarmonyPatch(typeof(HostInfoPanel), nameof(HostInfoPanel.SetUp))]
public static class HostInfoPanelUpdatePatch
{
    private static TextMeshPro HostText;
    public static bool Prefix()
    {
        return GameStates.IsLobby;
    }
    public static void Postfix(HostInfoPanel __instance)
    {
        try
        {
            if (AmongUsClient.Instance.AmHost)
            {
                if (HostText == null)
                    HostText = __instance.content.transform.FindChild("Name").GetComponent<TextMeshPro>();

                string htmlStringRgb = ColorUtility.ToHtmlStringRGB(Palette.PlayerColors[__instance.player.ColorId]);
                string hostName = Main.HostRealName;
                string youLabel = DestroyableSingleton<TranslationController>.Instance.GetString(StringNames.HostYouLabel);

                // Set text in host info panel
                HostText.text = $"<color=#{htmlStringRgb}>{hostName}</color>  <size=90%><b><font=\"Barlow-BoldItalic SDF\" material=\"Barlow-BoldItalic SDF Outline\">({youLabel})";
            }
        }
        catch (System.Exception error)
        {
            Logger.Exception(error, "HostInfoPanelUpdatePatch.Postfix");
        }
    }
}
