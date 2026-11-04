using TMPro;
using UnityEngine;

namespace TOHE;

// Overall progress represents completed startup work, not an estimate of time remaining.
internal static class GameStartProgress
{
    private static OfficialSessionContext context;
    private static bool active;
    private static bool customHost;
    private static int completed;
    private static int hostPhase;
    private static string hostStatus;
    private static int hostCount, hostTotal, hostRoleViews;
    private static int roleViewsSent, roleViewsTotal;
    private static RpcSendBarrier hostBarrier;
    private static float waitUntil, waitDuration;
    private static float readyElapsed, readyTimeout;
    private static bool waitRealtime;
    private static float progress;
    private static TextMeshProUGUI configuredText;
    private static TextAlignmentOptions originalAlignment;
    private static TextOverflowModes originalOverflow;
    private static bool originalWordWrapping, originalAutoSizing;
    private static float originalFontSize, originalFontSizeMin, originalFontSizeMax;
    private static Vector2 originalSize;

    internal static void Begin(OfficialSessionContext round)
    {
        context = round;
        active = true;
        completed = 0;
        hostPhase = 0;
        hostStatus = null;
        hostBarrier = null;
        waitDuration = 0f;
        readyElapsed = readyTimeout = 0f;
        roleViewsSent = roleViewsTotal = 0;
        progress = 0f;
        var client = AmongUsClient.Instance;
        customHost = client.AmHost && GameStates.IsModHost && GameStates.IsNormalGame && !GameStates.IsFreePlay;
        Update();
    }

    internal static void HostPreparingOptions()
    {
        if (active && customHost) hostPhase = 1;
    }

    internal static void HostWaitingPlayers(float elapsed, float timeout)
    {
        if (!active || !customHost) return;
        readyElapsed = elapsed;
        readyTimeout = timeout;
    }

    internal static void HostAssigningRoles()
    {
        if (!active || !customHost) return;
        hostPhase = 2;
        hostStatus = null;
        hostBarrier = null;
        waitDuration = 0f;
    }

    internal static void HostRolesAssigned()
    {
        if (active && customHost) hostPhase = 3;
    }

    internal static void HostWait(string status, float until, float duration, bool realtime)
    {
        if (!active || !customHost) return;
        hostStatus = status;
        hostBarrier = null;
        waitUntil = until;
        waitDuration = duration;
        waitRealtime = realtime;
    }

    internal static void HostBarrier(string status, RpcSendBarrier barrier, int roleViews = 0)
    {
        if (!active || !customHost) return;
        hostStatus = status;
        hostBarrier = barrier;
        hostTotal = barrier.PendingCount;
        hostRoleViews = roleViews;
        waitDuration = 0f;
    }

    internal static void HostCount(string status, int count, int total)
    {
        if (!active || !customHost) return;
        hostStatus = status;
        hostBarrier = null;
        hostCount = count;
        hostTotal = total;
        waitDuration = 0f;
    }

    internal static void HostRoleViews(int sent, int total)
    {
        if (!active || !customHost) return;
        roleViewsSent = sent;
        roleViewsTotal = total;
        if (total > 0) progress = Mathf.Max(progress, 45f + 20f * sent / total);
    }

    internal static void Update()
    {
        if (!active) return;
        if (!context.IsCurrent())
        {
            Reset();
            return;
        }

        var client = AmongUsClient.Instance;
        var player = PlayerControl.LocalPlayer;
        if (completed == 0 && player != null && player.Data != null) completed = 1;
        if (completed == 1 && ShipStatus.Instance && (client.AmHost || !LobbyBehaviour.Instance)) completed = 2;

        int ready = 0, total = 0;
        if (completed >= 2)
        {
            // The network callback removes clients under this native monitor.
            var clients = client.allClients;
            Il2CppSystem.Threading.Monitor.Enter(clients);
            try
            {
                for (int i = 0; i < clients.Count; i++)
                {
                    var peer = clients[i];
                    if (client.AmHost && peer.Id == client.ClientId) continue;
                    total++;
                    if (peer.IsReady) ready++;
                }
            }
            finally { Il2CppSystem.Threading.Monitor.Exit(clients); }
            if (ready == total && completed == 2) completed = 3;
        }

        if (completed == 3)
        {
            if (customHost ? hostPhase == 3 : client.AmHost
                ? client.FindClientById(client.ClientId)?.IsReady == true
                : player != null && player.Data != null && player.Data.Role != null && player.myTasks.Count > 0)
                completed = 4;
        }

        string message = completed switch
        {
            0 => Translator.GetString("StartProgress.LocalPlayer"),
            1 => Translator.GetString("StartProgress.Map"),
            2 => string.Format(Translator.GetString(client.AmHost ? "StartProgress.OtherPlayers" : "StartProgress.Players"), ready, total),
            3 when customHost && hostPhase < 2 => Translator.GetString("StartProgress.Settings"),
            3 when customHost || client.AmHost => Translator.GetString("StartProgress.Roles"),
            3 => Translator.GetString("StartProgress.RolesAndTasks"),
            _ => Translator.GetString("StartProgress.Spawn")
        };
        float percent = completed switch { 2 => 15f, 3 => 35f, 4 => 99f, _ => 0f };
        if (completed == 1 && client.AmHost)
        {
            var handle = client.ShipLoadingAsyncHandle;
            if (handle.IsValid()) percent = handle.PercentComplete * 15f;
        }
        else if (completed == 2 && total > 0)
        {
            percent = 15f + 20f * ready / total;
            if (customHost && readyTimeout > 0f)
                message = string.Format(Translator.GetString("StartProgress.OtherPlayersTimed"), ready, total, readyElapsed, readyTimeout);
        }
        else if (completed == 3 && customHost && hostStatus != null)
        {
            float stageProgress = 0f;
            if (waitDuration > 0f)
            {
                float now = waitRealtime ? Time.realtimeSinceStartup : Time.time;
                float remaining = Mathf.Clamp(waitUntil - now, 0f, waitDuration);
                stageProgress = (waitDuration - remaining) / waitDuration;
                message = string.Format(Translator.GetString(hostStatus), Mathf.Ceil(remaining * 10f) / 10f,
                    roleViewsSent, roleViewsTotal);
            }
            else if (hostBarrier != null && !hostBarrier.IsCancelled && hostTotal > 0)
            {
                int sent = hostTotal - hostBarrier.PendingCount;
                stageProgress = (float)sent / hostTotal;
                message = string.Format(Translator.GetString(hostStatus), sent, hostTotal, hostRoleViews);
            }
            else if (hostBarrier == null)
            {
                if (hostTotal > 0) stageProgress = (float)hostCount / hostTotal;
                message = string.Format(Translator.GetString(hostStatus), hostCount, hostTotal);
            }
            percent = hostStatus switch
            {
                "StartProgress.OptionsWait" => 35f + 10f * stageProgress,
                "StartProgress.SettingsSending" => 45f + 5f * stageProgress,
                "StartProgress.RoleSending" => 50f + 15f * stageProgress,
                "StartProgress.RoleWait" => 65f + 10f * stageProgress,
                "StartProgress.SafeWait" => 65f + 15f * stageProgress,
                "StartProgress.FinalWait" => 75f + 20f * stageProgress,
                "StartProgress.HostRoleSending" => 80f + 5f * stageProgress,
                "StartProgress.Restoring" => 85f + 10f * stageProgress,
                "StartProgress.RestoreSending" => 95f + 3f * stageProgress,
                _ => percent
            };
        }
        else if (completed == 3 && customHost && hostPhase >= 2) percent = 45f;
        else if (completed == 3 && !client.AmHost)
        {
            int expected = RoleDistribution.ExpectedRoleViews;
            if (expected > 0 && !RoleDistribution.RestoreComplete)
            {
                int received = RoleDistribution.ReceivedRoleViews;
                percent = 35f + 55f * received / expected;
                message = string.Format(Translator.GetString("StartProgress.ReceivedRoles"), received, expected);
            }
            else if (player?.Data?.Role != null)
            {
                percent = 90f;
                message = Translator.GetString("StartProgress.Tasks");
            }
        }
        progress = Mathf.Max(progress, Mathf.Clamp(percent, 0f, 99f));

        if (!DestroyableSingleton<LoadingBarManager>.InstanceExists) return;
        var bar = DestroyableSingleton<LoadingBarManager>.Instance.loadingBar;
        if (!bar) return;
        if (!bar.gameObject.activeSelf) bar.gameObject.SetActive(true);
        var text = bar.loadingText;
        if (!configuredText)
        {
            var rect = text.rectTransform;
            var fillRect = bar.barFill.rectTransform;
            float textScale = Mathf.Abs(rect.lossyScale.x);
            float width = fillRect.rect.width * Mathf.Abs(fillRect.lossyScale.x);
            if (textScale > 0f && width > 0f)
            {
                configuredText = text;
                originalAlignment = text.alignment;
                originalOverflow = text.overflowMode;
                originalWordWrapping = text.enableWordWrapping;
                originalAutoSizing = text.enableAutoSizing;
                originalFontSize = text.fontSize;
                originalFontSizeMin = text.fontSizeMin;
                originalFontSizeMax = text.fontSizeMax;
                originalSize = rect.sizeDelta;

                text.alignment = TextAlignmentOptions.Center;
                text.enableWordWrapping = false;
                text.fontSizeMax = originalFontSize;
                text.fontSizeMin = Mathf.Max(0.1f, originalFontSize * 0.3f);
                text.enableAutoSizing = true;
                text.overflowMode = TextOverflowModes.Ellipsis;
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width / textScale);
            }
        }
        bar.SetLoadingPercent(progress, StringNames.None);
        text.text = message;
    }

    internal static void End(OfficialSessionContext round, bool finished)
    {
        if (!active || context != round) return;
        if (finished && round.IsCurrent())
        {
            Update();
            if (active && completed == 4 && DestroyableSingleton<LoadingBarManager>.InstanceExists)
            {
                var bar = DestroyableSingleton<LoadingBarManager>.Instance.loadingBar;
                if (bar) bar.SetLoadingPercent(100f, StringNames.None);
            }
        }
        Reset();
    }

    internal static void Reset()
    {
        if (!active) return;
        active = false;
        if (configuredText)
        {
            var text = configuredText;
            configuredText = null;
            text.alignment = originalAlignment;
            text.overflowMode = originalOverflow;
            text.enableWordWrapping = originalWordWrapping;
            text.enableAutoSizing = originalAutoSizing;
            text.fontSizeMin = originalFontSizeMin;
            text.fontSizeMax = originalFontSizeMax;
            text.fontSize = originalFontSize;
            text.rectTransform.sizeDelta = originalSize;
        }
        if (!DestroyableSingleton<LoadingBarManager>.InstanceExists) return;
        var bar = DestroyableSingleton<LoadingBarManager>.Instance.loadingBar;
        if (bar && bar.gameObject.activeSelf) bar.gameObject.SetActive(false);
    }
}
