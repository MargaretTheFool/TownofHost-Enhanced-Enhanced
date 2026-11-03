using UnityEngine;

namespace TOHE;

// The five equally weighted checkpoints are local player, map, player readiness,
// settings/roles, and the final spawn handoff. Only readiness has a known count.
internal static class GameStartProgress
{
    private static OfficialSessionContext context;
    private static bool active;
    private static bool customHost;
    private static int completed;
    private static int hostPhase;

    internal static void Begin(OfficialSessionContext round)
    {
        context = round;
        active = true;
        completed = 0;
        hostPhase = 0;
        var client = AmongUsClient.Instance;
        customHost = client.AmHost && GameStates.IsModHost && GameStates.IsNormalGame && !GameStates.IsFreePlay;
        Update();
    }

    internal static void HostPreparingOptions()
    {
        if (active && customHost) hostPhase = 1;
    }

    internal static void HostAssigningRoles()
    {
        if (active && customHost) hostPhase = 2;
    }

    internal static void HostRolesAssigned()
    {
        if (active && customHost) hostPhase = 3;
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
        float percent = completed * 20f;
        if (completed == 2 && total > 0) percent += 20f * ready / total;

        if (!DestroyableSingleton<LoadingBarManager>.InstanceExists) return;
        var bar = DestroyableSingleton<LoadingBarManager>.Instance.loadingBar;
        if (!bar) return;
        if (!bar.gameObject.activeSelf) bar.gameObject.SetActive(true);
        bar.SetLoadingPercent(Mathf.Clamp(percent, 0f, 100f), StringNames.None);
        bar.loadingText.text = message;
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
