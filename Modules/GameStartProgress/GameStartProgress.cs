using UnityEngine;

namespace TOHE;

// The bar shows progress within the named stage, never an estimate of total time.
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
        float percent = -1f;
        if (completed == 1 && client.AmHost)
        {
            var handle = client.ShipLoadingAsyncHandle;
            if (handle.IsValid()) percent = handle.PercentComplete * 100f;
        }
        else if (completed == 2 && total > 0)
        {
            percent = 100f * ready / total;
            if (customHost && readyTimeout > 0f)
                message = string.Format(Translator.GetString("StartProgress.OtherPlayersTimed"), ready, total, readyElapsed, readyTimeout);
        }
        else if (completed == 3 && customHost && hostStatus != null)
        {
            if (waitDuration > 0f)
            {
                float now = waitRealtime ? Time.realtimeSinceStartup : Time.time;
                float remaining = Mathf.Clamp(waitUntil - now, 0f, waitDuration);
                percent = 100f * (waitDuration - remaining) / waitDuration;
                message = string.Format(Translator.GetString(hostStatus), Mathf.Ceil(remaining * 10f) / 10f,
                    roleViewsSent, roleViewsTotal);
            }
            else if (hostBarrier != null && !hostBarrier.IsCancelled && hostTotal > 0)
            {
                int sent = hostTotal - hostBarrier.PendingCount;
                percent = 100f * sent / hostTotal;
                message = string.Format(Translator.GetString(hostStatus), sent, hostTotal, hostRoleViews);
            }
            else if (hostBarrier == null)
            {
                if (hostTotal > 0) percent = 100f * hostCount / hostTotal;
                message = string.Format(Translator.GetString(hostStatus), hostCount, hostTotal);
            }
        }
        else if (completed == 3 && !client.AmHost)
        {
            int expected = RoleDistribution.ExpectedRoleViews;
            if (expected > 0 && !RoleDistribution.RestoreComplete)
            {
                int received = RoleDistribution.ReceivedRoleViews;
                percent = 100f * received / expected;
                message = string.Format(Translator.GetString("StartProgress.ReceivedRoles"), received, expected);
            }
            else if (player?.Data?.Role != null)
                message = Translator.GetString("StartProgress.Tasks");
        }

        if (!DestroyableSingleton<LoadingBarManager>.InstanceExists) return;
        var bar = DestroyableSingleton<LoadingBarManager>.Instance.loadingBar;
        if (!bar) return;
        if (!bar.gameObject.activeSelf) bar.gameObject.SetActive(true);
        bar.SetLoadingPercent(Mathf.Clamp(percent, 0f, 100f), StringNames.None);
        if (percent < 0f) bar.percentText.text = "—";
        bar.loadingText.text = string.Format(Translator.GetString("StartProgress.Stage"), message);
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
        if (!DestroyableSingleton<LoadingBarManager>.InstanceExists) return;
        var bar = DestroyableSingleton<LoadingBarManager>.Instance.loadingBar;
        if (bar && bar.gameObject.activeSelf) bar.gameObject.SetActive(false);
    }
}
