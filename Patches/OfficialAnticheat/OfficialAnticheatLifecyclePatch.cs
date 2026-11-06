using AmongUs.GameOptions;
using AmongUs.HTTP;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using Hazel;
using InnerNet;
using TOHE.Modules;

namespace TOHE.Patches;

[HarmonyPatch]
internal static class OfficialAnticheatLifecyclePatch
{
    internal static void ResetOperations(bool preserveCapabilities = false)
    {
#if ANDROID
        StartGameHostPatch.isStartingAsHost = false;
#endif
        GameStartProgress.Reset();
        AntiBlackout.Reset();
        RoleDistribution.Reset();
        RpcSetRoleReplacer.RoleMap.Clear();
        RpcSetRoleReplacer.StoragedData.Clear();
        CustomRpcTransport.Reset(preserveCapabilities);
        OfficialNativePacking.Reset();
        OfficialNetworkSend.Reset();
        PhantomRolePatch.ResetViews();
        Main.MessagesToSend.Clear();
        TOHE.Modules.ChatManager.ChatManager.ResetHistory();
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
    [HarmonyPostfix, HarmonyPriority(Priority.First)]
    private static void Joined()
    {
        ResetOperations();
        OfficialSessionContext.BeginLobby();
        OfficialAnticheatPolicy.BeginLobby();
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnDisconnected))]
    [HarmonyPrefix]
    private static void Disconnected()
    {
        OptionItem.SetRemotePreset(false);
        OfficialSessionContext.Reset();
        OfficialAnticheatPolicy.Reset();
        ResetOperations();
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.CoStartGame))]
    [HarmonyPostfix, HarmonyPriority(Priority.First)]
    private static void StartRound(ref Il2CppSystem.Collections.IEnumerator __result)
    {
        if (__result == null) return;
        __result = BeginRoundOnFirstAdvance(__result, OfficialSessionContext.Capture()).WrapToIl2Cpp();
    }

    private static System.Collections.IEnumerator BeginRoundOnFirstAdvance(
        Il2CppSystem.Collections.IEnumerator original, OfficialSessionContext context)
    {
        // CoStartGame only constructs its native iterator. Freeze when Unity
        // actually advances it, and ignore an unstarted iterator from an old room.
        var round = default(OfficialSessionContext);
        bool progressStarted = false, finished = false;
        try
        {
            if (!context.IsCurrent()) yield break;
            OfficialAnticheatPolicy.FreezeRound();
            OfficialSessionContext.BeginRound();
            // Initialize after the context changes, never at iterator construction:
            // initialization sends must belong to the round they initialize.
            ChangeRoleSettings.Initialize(AmongUsClient.Instance);
            round = OfficialSessionContext.Capture();
            progressStarted = true;
            GameStartProgress.Begin(round);
            while (round.IsCurrent())
            {
                if (!original.MoveNext())
                {
                    finished = true;
                    break;
                }
                var current = original.Current;
#if ANDROID
                // Android ARM64 can inline CoStartGameHost into this iterator.
                // Replace its yielded native iterator before Unity advances it.
                var host = current?.TryCast<AmongUsClient._CoStartGameHost_d__28>();
                var hostRoutine = host?.TryCast<Il2CppSystem.Collections.IEnumerator>();
                if (hostRoutine != null)
                {
                    var client = host.__4__this;
                    if (LobbyBehaviour.Instance != null) StartGameHostPatch.isStartingAsHost = true;
                    try
                    {
                        if (!TOHE.StartGameHostPatch.CoStartGameHost_Prefix(client, ref hostRoutine))
                            host.TryCast<Il2CppSystem.IDisposable>()?.Dispose();
                        DleksPatch.Postfix(client, ref hostRoutine);
                        yield return hostRoutine;
                    }
                    finally { StartGameHostPatch.isStartingAsHost = false; }
                    continue;
                }
#endif
                yield return current;
            }
        }
        finally
        {
            try
            {
                if (progressStarted) GameStartProgress.End(round, finished);
            }
            finally { original.TryCast<Il2CppSystem.IDisposable>()?.Dispose(); }
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameEnd))]
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static void EndRound()
    {
        OfficialSessionContext.EndRound();
        OfficialAnticheatPolicy.EndRound();
        ResetOperations(preserveCapabilities: true);
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnBecomeHost))]
    [HarmonyPrefix]
    private static void HostChanged()
    {
        OptionItem.SetRemotePreset(false);
        OfficialSessionContext.Reset();
        ResetOperations();
    }

    // Both host and guest receive OnPlayerLeft after HostId is updated.
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerLeft))]
    [HarmonyPostfix]
    private static void AfterPlayerLeft()
    {
        if (OfficialSessionContext.ObserveHostMigration()) ResetOperations();
    }

    [HarmonyPatch(typeof(GameOptionsManager), nameof(GameOptionsManager.GameHostOptions), MethodType.Setter)]
    [HarmonyPostfix]
    private static void HostOptions(IGameOptions value)
    {
        if (AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost)
            OfficialAnticheatPolicy.CaptureCanonicalOptions(value);
    }

    [HarmonyPatch(typeof(GameOptionsManager), nameof(GameOptionsManager.CurrentGameOptions), MethodType.Setter)]
    [HarmonyPostfix]
    private static void CurrentOptions(IGameOptions value) => OfficialAnticheatPolicy.CaptureCanonicalOptions(value);

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcSyncSettings))]
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static void SyncLobbyOptions() =>
        OfficialAnticheatPolicy.CaptureCanonicalOptions(GameOptionsManager.Instance?.CurrentGameOptions);

    // Unity invokes this substantial lobby callback every frame. It also covers
    // setters/getters that IL2CPP may inline into native settings editors.
    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.Update))]
    [HarmonyPostfix]
    private static void LobbyUpdate() => SyncLobbyOptions();

    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.FinallyBegin))]
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool BeforeHostStart(GameStartManager __instance)
    {
        SyncLobbyOptions();
        var client = AmongUsClient.Instance;
        if (client == null || !client.AmHost ||
            __instance.startState != GameStartManager.StartingStates.Countdown) return true;

        // FinallyBegin runs after the countdown for both manual and automatic
        // starts. Submit the final snapshot before the native start message.
        OptionItem.SyncAllOptions();
        CustomRpcTransport.FlushPending();
        var queue = client.reliableMessageQueue;
        while (queue.Count > 0)
        {
            int before = queue.Count;
            client.PackAndSendQueuedMessages(queue, SendOption.Reliable, out _);
            if (OfficialNetworkSend.Failed(client) || queue.Count >= before)
            {
                Logger.Error("Could not submit the final options snapshot; cancelling start.", "OptionSync");
                __instance.ResetStartState();
                return false;
            }
        }
        return true;
    }

    // Native HandleMessage sets Started before constructing CoStartGame. Read
    // the canonical lobby options at that boundary, while they are still lobby
    // settings, rather than depending on an inlinable property setter alone.
    [HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.HandleMessage))]
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static void BeforeNetworkStart(MessageReader reader)
    {
        if (reader.Tag == 2) SyncLobbyOptions();
    }

    // The request URL is captured before cross-region lookup restores the menu
    // selection. Authentication retries do not overwrite the game request.
    [HarmonyPatch(typeof(HttpMatchmakerManager), nameof(HttpMatchmakerManager.CoSendRequest),
        typeof(RetryableWebRequest), typeof(string), typeof(int), typeof(Il2CppSystem.Action<HttpMatchmakerManager.MatchmakerFailure>))]
    [HarmonyPostfix]
    private static void MatchmakerRequest(RetryableWebRequest request, string context,
        ref Il2CppSystem.Collections.IEnumerator __result)
    {
        if (__result == null || context is not ("request gamecode server" or "find host server")) return;
        __result = ServerRegionCoroutine.BeforeStart(__result,
            () => ServerRegion.RememberMatchmakerSource(request.Url)).WrapToIl2Cpp();
    }
}
