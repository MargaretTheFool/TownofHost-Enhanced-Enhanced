using AmongUs.GameOptions;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using Hazel;
using System;
using System.Collections;
using TOHE.Roles.Core.AssignManager;
using UnityEngine;

namespace TOHE;

internal enum RoleDistributionPhase
{
    Idle, Preparing, SendingNonHostRoles, ApplyingStartMask, Waiting3Seconds,
    PublishingHostRole, Intro, RestoringStartMask, Running, Cancelled, Failed
}

internal static class RoleDistribution
{
    internal static RoleDistributionPhase Phase { get; private set; }
    internal static bool IsActive => Phase != RoleDistributionPhase.Idle;
    internal static bool RestoreComplete { get; private set; }
    internal static bool GameplayReady => !IsActive ||
        RestoreComplete && Main.IntroDestroyed;
    internal static bool BlocksOrdinaryRoleChanges => IsActive && !GameplayReady;
    internal static bool IsMaskActive => mask?.Active == true;
    internal static RoleDistributionPlan Plan { get; private set; }
    internal static int ReceivedRoleViews => receiver?.Subjects.Count ?? 0;
    internal static int ExpectedRoleViews => receiver?.Count ?? 0;
    private static OfficialSessionContext context;
    private static StartDisconnectedMask mask;
    private static Dictionary<byte, PlayerControl> players;
    private static Dictionary<byte, CustomRoles> customRoles;
    private static Dictionary<byte, nint> identities;
    private static float introStartedAt = float.PositiveInfinity;
    private static bool applyingNative;
    internal static bool AllowNativeRole => !BlocksOrdinaryRoleChanges || applyingNative || Phase == RoleDistributionPhase.Preparing;
    internal static bool IsAuthoritativeRoleCall => applyingNative || Phase == RoleDistributionPhase.Preparing;

    internal static void Prepare()
    {
        Reset();
        context = OfficialSessionContext.Capture();
        Phase = RoleDistributionPhase.Preparing;
    }

    internal static bool ObjectsReady => GameManager.Instance != null && Main.AllPlayerControls
        .All(player => player != null && player.Data != null);

    internal static void FreezePlan()
    {
        players = Main.AllPlayerControls.Where(player => player != null && player.Data != null && !player.Data.Disconnected)
            .OrderBy(player => player.PlayerId).ToDictionary(player => player.PlayerId);
        identities = players.ToDictionary(pair => pair.Key, pair => pair.Value.Pointer);
        Plan = new RoleDistributionPlan(players.Values.Select(player =>
        {
            var role = RoleAssign.RoleResult[player.PlayerId];
            var native = RpcSetRoleReplacer.StoragedData.GetValueOrDefault(player.PlayerId, role.GetRoleTypes());
            return new RoleDistributionPlan.Actor(player.PlayerId, native, role.IsDesyncRole(), role.GetDYRole(), role.GetVNRole() == CustomRoles.Noisemaker);
        }), PlayerControl.LocalPlayer.PlayerId);
        // Keep the authoritative recipient/subject matrix for later recovery.
        RpcSetRoleReplacer.RoleMap = Plan.DesiredNativeRole.ToDictionary(pair => (pair.Key.Recipient, pair.Key.Subject),
            pair => (pair.Value, RoleAssign.RoleResult[pair.Key.Subject]));
        foreach (var subject in Plan.PlayerIds.Where(id => id != Plan.HostPlayerId))
        {
            var player = players[subject];
            var wanted = Plan.DesiredNativeRole[(Plan.HostPlayerId, subject)];
            // Existing one-time local false initialization is retained. Only correct
            // a differing view after it completed; the host alone may unlock it.
            if (player.Data.Role == null || player.Data.Role.Role != wanted)
            {
                player.roleAssigned = false;
                Apply(player, wanted, true);
            }
        }
    }

    internal static IEnumerator Publish() => Guard(PublishCore());
    private static IEnumerator Guard(IEnumerator routine)
    {
        bool completed = false;
        try
        {
            while (true)
            {
                bool more = false, failed = false;
                object current = null;
                try { more = routine.MoveNext(); if (more) current = routine.Current; }
                catch (Exception error)
                {
                    Cancel("Role distribution failed: " + error.GetType().Name, failed: true);
                    Logger.Error(error.ToString(), "RoleDistribution");
                    if (AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost && context.IsCurrent())
                        Utils.ErrorEnd("Role distribution failed");
                    failed = true;
                }
                if (failed) yield break;
                if (!more) { completed = true; yield break; }
                yield return current;
            }
        }
        finally
        {
            (routine as IDisposable)?.Dispose();
            if (!completed && Phase != RoleDistributionPhase.Failed) Cancel("Role coroutine stopped");
        }
    }

    private static void RequireCurrent()
    {
        if (!context.IsCurrent() || AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost ||
            OfficialNetworkSend.Failed(AmongUsClient.Instance) || OfficialNativePacking.Failed(AmongUsClient.Instance) ||
            Phase is RoleDistributionPhase.Cancelled or RoleDistributionPhase.Failed || GameStates.IsEnded)
            throw new InvalidOperationException("Role distribution context expired");
        foreach (var pair in players)
            if (pair.Value == null || pair.Value.Data == null || pair.Value.Pointer != identities[pair.Key] ||
                Utils.GetPlayerById(pair.Key)?.Pointer != identities[pair.Key])
                throw new InvalidOperationException("Role distribution roster changed");
    }

    private static IEnumerator PublishCore()
    {
        RequireCurrent();
        // FreezePlan runs before AssignCustomRole installs normal-role metadata.
        // Capture the completed assignments that were just synchronized instead
        // of sending the earlier NotAssigned placeholders in native views.
        customRoles = players.Keys.ToDictionary(id => id, id => Main.PlayerStates[id].MainRole);
        if (customRoles.Values.Any(role => role >= CustomRoles.NotAssigned))
            throw new InvalidOperationException("Native role publication requires completed custom role assignments");
        // Custom roles, add-ons and initial options are causal dependencies of
        // direct vanilla publication, so flush and await their actual sends.
        var settingsBarrier = CustomRpcTransport.CaptureBarrier();
        GameStartProgress.HostBarrier("StartProgress.SettingsSending", settingsBarrier);
        while (!settingsBarrier.IsComplete)
        {
            RequireCurrent();
            if (settingsBarrier.IsCancelled) throw new InvalidOperationException("Initial settings send cancelled");
            yield return null;
        }
        RequireCurrent();
        Phase = RoleDistributionPhase.SendingNonHostRoles;
        int roleViews = SendRoles(hostOnly: false);
        CustomRpcTransport.FlushPending();
        var nonHostBarrier = CustomRpcTransport.CaptureBarrier();
        GameStartProgress.HostBarrier("StartProgress.RoleSending", nonHostBarrier, roleViews);
        while (!nonHostBarrier.IsComplete)
        {
            RequireCurrent();
            if (nonHostBarrier.IsCancelled) throw new InvalidOperationException("Role send cancelled");
            yield return null;
        }
        RequireCurrent();
        // Native CoSetRole has no yielding path once Data/GameManager are ready.
        // Its local one-time initialization therefore finished before this mask.
        if (!ObjectsReady) throw new InvalidOperationException("Native role objects became unavailable");
        Phase = RoleDistributionPhase.ApplyingStartMask;
        mask = new StartDisconnectedMask();
        mask.Begin(GameData.Instance.AllPlayers.ToArray().Where(info => info != null));
        var maskBatch = new OfficialPacketBuilder.Batch();
        foreach (var info in GameData.Instance.AllPlayers.ToArray().OrderBy(info => info.PlayerId))
        {
            RequireCurrent();
            SendPlayerInfo(info, maskBatch);
        }
        RequireCurrent();
        maskBatch.Send();
        // The packed mask sends completed synchronously. Start the intentional
        // delay after the final submission; direct sends need no extra wait.
        Phase = RoleDistributionPhase.Waiting3Seconds;
        float releaseAt = Time.realtimeSinceStartup + 3f;
        GameStartProgress.HostWait("StartProgress.SafeWait", releaseAt, 3f, true);
        while (Time.realtimeSinceStartup < releaseAt)
        {
            RequireCurrent();
            yield return null;
        }
        RequireCurrent();
        Phase = RoleDistributionPhase.PublishingHostRole;
        // Queue modded final views through the native reliable queue first.
        // Direct vanilla publication and local host commit then form one
        // synchronous segment after that queue's actual-send barrier.
        roleViews = SendRoles(hostOnly: true, moddedOnly: true);
        CustomRpcTransport.FlushPending();
        var hostBarrier = CustomRpcTransport.CaptureBarrier();
        GameStartProgress.HostBarrier("StartProgress.HostRoleSending", hostBarrier, roleViews);
        while (!hostBarrier.IsComplete)
        {
            RequireCurrent();
            if (hostBarrier.IsCancelled) throw new InvalidOperationException("Host role send cancelled");
            yield return null;
        }
        RequireCurrent();
        SendRoles(hostOnly: true, moddedOnly: false);
        Apply(players[Plan.HostPlayerId], Plan.DesiredNativeRole[(Plan.HostPlayerId, Plan.HostPlayerId)], true);
        Phase = RoleDistributionPhase.RestoringStartMask;
        var restoreIds = GameData.Instance.AllPlayers.ToArray().Select(info => info.PlayerId).OrderBy(id => id).ToArray();
        int restored = 0;
        GameStartProgress.HostCount("StartProgress.Restoring", restored, restoreIds.Length);
        foreach (var id in restoreIds)
        {
            RequireCurrent();
            var info = mask.Restore(id);
            if (info != null) SendPlayerInfo(info);
            mask.CommitRestore(id);
            GameStartProgress.HostCount("StartProgress.Restoring", ++restored, restoreIds.Length);
            yield return null;
        }
        RequireCurrent();
        foreach (var recipient in players.Values.Where(player => !player.AmOwner && RpcCompatibility.SupportsPackedRpc(player)))
            SendNative(recipient, Plan.HostPlayerId, 2);
        CustomRpcTransport.FlushPending();
        var restoreBarrier = CustomRpcTransport.CaptureBarrier();
        GameStartProgress.HostBarrier("StartProgress.RestoreSending", restoreBarrier);
        while (!restoreBarrier.IsComplete)
        {
            RequireCurrent();
            if (restoreBarrier.IsCancelled) throw new InvalidOperationException("Restore notification cancelled");
            yield return null;
        }
        RequireCurrent();
        RestoreComplete = true;
        Phase = RoleDistributionPhase.Running;
        RpcSetRoleReplacer.EndReplace();
    }

    private static int SendRoles(bool hostOnly, bool? moddedOnly = null)
    {
        var vanilla = CustomRpcSender.Create("Initial native role distribution", SendOption.Reliable);
        bool wrote = false;
        int roleViews = 0;
        try
        {
            foreach (var recipient in players.Values.Where(player => !player.AmOwner &&
                (!moddedOnly.HasValue || RpcCompatibility.SupportsPackedRpc(player) == moddedOnly.Value)).OrderBy(player => player.PlayerId))
                foreach (var subject in Plan.PlayerIds.Where(id => (id == Plan.HostPlayerId) == hostOnly))
                {
                    RequireCurrent();
                    if (RpcCompatibility.SupportsPackedRpc(recipient)) SendNative(recipient, subject, hostOnly ? (byte)1 : (byte)0);
                    else
                    {
                        if (recipient.IsModded()) throw new InvalidOperationException("Unsupported mod protocol in initial role distribution");
                        vanilla.AutoStartRpc(players[subject].NetId, (byte)RpcCalls.SetRole, recipient.OwnerId)
                            .Write((ushort)Plan.DesiredNativeRole[(recipient.PlayerId, subject)]).Write(true).EndRpc();
                        wrote = true;
                    }
                    roleViews++;
                }
            if (wrote) vanilla.SendMessage();
            return roleViews;
        }
        finally { vanilla.Dispose(); }
    }

    private static void SendNative(PlayerControl recipient, byte subject, byte phase) =>
        CustomRpcTransport.Send(CustomRPC.SetCustomRole, writer =>
        {
            writer.Write(subject);
            writer.WritePacked((int)customRoles[subject]);
            writer.Write(context.RoundIdentity);
            writer.Write(phase);
            writer.Write((ushort)Plan.DesiredNativeRole[(recipient.PlayerId, subject)]);
            writer.Write(true);
            writer.Write(Plan.PlayerIds.Length);
        }, recipient.OwnerId);

    private static void SendPlayerInfo(NetworkedPlayerInfo info, OfficialPacketBuilder.Batch batch = null)
    {
        void Serialize(MessageWriter writer)
        {
            writer.StartMessage(1);
            writer.WritePacked(info.NetId);
            info.Serialize(writer, false);
            writer.EndMessage();
        }
        if (batch == null) OfficialPacketBuilder.SendChild(-1, SendOption.Reliable, Serialize);
        else batch.AddChild(-1, Serialize);
    }

    private static void Apply(PlayerControl player, RoleTypes role, bool canOverride)
    {
        applyingNative = true;
        try { player.SetRole(role, canOverride); }
        finally { applyingNative = false; }
    }

    internal static bool NotifyIntroStarted()
    {
        if (!IsActive) return true;
        if (Phase is < RoleDistributionPhase.PublishingHostRole or > RoleDistributionPhase.Running) return false;
        if (!float.IsPositiveInfinity(introStartedAt)) return false;
        introStartedAt = Time.realtimeSinceStartup;
        if (Phase == RoleDistributionPhase.PublishingHostRole) Phase = RoleDistributionPhase.Intro;
        return true;
    }
    internal static bool TasksMayBegin => RestoreComplete && Time.realtimeSinceStartup >= introStartedAt + 4f;
    internal static IEnumerator AfterRestore(Action action, bool tasks = false)
    {
        var captured = OfficialSessionContext.Capture();
        bool Cancelled() => Phase is RoleDistributionPhase.Cancelled or RoleDistributionPhase.Failed;
        while (captured.IsCurrent() && IsActive && !RestoreComplete)
        {
            if (Cancelled()) yield break;
            yield return null;
        }
        while (captured.IsCurrent() && !Cancelled() && tasks && !TasksMayBegin) yield return null;
        if (captured.IsCurrent() && !Cancelled()) action();
    }

    internal static void OnPlayerLeft(byte id)
    {
        mask?.RecordDeparture(id);
        if (Phase is >= RoleDistributionPhase.SendingNonHostRoles and < RoleDistributionPhase.Running)
        {
            Cancel("Player left during role distribution", failed: true);
            if (AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost)
                Utils.ErrorEnd("Player left during role distribution");
        }
    }
    internal static void Cancel(string reason, bool failed = false)
    {
        bool hadPendingDistribution = IsActive && !RestoreComplete;
        mask?.Cancel();
        RpcSetRoleReplacer.BlockSetRole = false;
        Main.AssignRolesIsStarted = false;
        receiver = null;
        if (IsActive) Phase = failed ? RoleDistributionPhase.Failed : RoleDistributionPhase.Cancelled;
        if (hadPendingDistribution) CustomRpcTransport.Reset(preserveCapabilities: true);
        Logger.Info(reason, "RoleDistribution");
    }

    internal static void TickContext()
    {
        if (IsActive && Phase is not (RoleDistributionPhase.Cancelled or RoleDistributionPhase.Failed) && !context.IsCurrent())
            Cancel("Role distribution context changed");
    }
    internal static void Reset()
    {
        Cancel("Reset");
        mask = null;
        players = null;
        customRoles = null;
        identities = null;
        Plan = null;
        RestoreComplete = false;
        introStartedAt = float.PositiveInfinity;
        Phase = RoleDistributionPhase.Idle;
    }

    private sealed class Receiving(OfficialSessionContext session, uint round, int count)
    {
        internal readonly OfficialSessionContext Session = session;
        internal readonly uint Round = round;
        internal readonly int Count = count;
        internal readonly Dictionary<byte, RoleTypes> Subjects = [];
        internal readonly Queue<(byte Subject, RoleTypes Role, byte Phase)> Queue = [];
        internal bool Draining, HostApplied, Complete;
    }
    private static Receiving receiver;

    internal static void ReceiveNativeRole(PlayerControl sender, MessageReader reader, byte subject, CustomRoles customRole)
    {
        if (sender == null || AmongUsClient.Instance == null || sender.OwnerId != AmongUsClient.Instance.HostId ||
            AmongUsClient.Instance.AmHost) throw new InvalidOperationException("Native role sender must be the current host");
        uint round = reader.ReadUInt32();
        byte phase = reader.ReadByte();
        var role = (RoleTypes)reader.ReadUInt16();
        bool canOverride = reader.ReadBoolean();
        int count = reader.ReadInt32();
        if (!canOverride || phase > 2 || count < 1 || count > 127 || !Enum.IsDefined(typeof(RoleTypes), role) ||
            RoleManager.IsGhostRole(role) || reader.Position != reader.Length)
            throw new InvalidOperationException("Invalid native role payload");
        if (!Main.PlayerStates.TryGetValue(subject, out var playerState) || playerState.MainRole != customRole ||
            customRole >= CustomRoles.NotAssigned)
            throw new InvalidOperationException("Native role view requires synchronized custom role metadata");
        if (receiver == null || !receiver.Session.IsCurrent())
        {
            if (phase != 0 && count != 1) throw new InvalidOperationException("Host role arrived before non-host roles");
            receiver = new(OfficialSessionContext.Capture(), round, count);
            context = receiver.Session;
            Phase = RoleDistributionPhase.SendingNonHostRoles;
            RestoreComplete = false;
        }
        var state = receiver;
        if (round != state.Round || count != state.Count) throw new InvalidOperationException("Native role round mismatch");
        bool isHostSubject = Utils.GetPlayerById(subject)?.OwnerId == AmongUsClient.Instance.HostId;
        if (phase == 0 && isHostSubject || phase != 0 && !isHostSubject)
            throw new InvalidOperationException("Native role subject violates phase");
        if (phase == 2 && state.Complete) return;
        if (phase == 2)
        {
            if (state.Subjects.Count != count) throw new InvalidOperationException("Restore completed before roles");
            state.Queue.Enqueue((subject, role, phase));
        }
        else if (state.Subjects.TryGetValue(subject, out var previous))
        {
            if (previous != role) throw new InvalidOperationException("Conflicting duplicate native role");
            return;
        }
        else
        {
            if (state.HostApplied || phase == 1 && state.Subjects.Count != count - 1)
                throw new InvalidOperationException("Out-of-order native role");
            state.Subjects.Add(subject, role);
            state.Queue.Enqueue((subject, role, phase));
        }
        if (!state.Draining)
        {
            state.Draining = true;
            AmongUsClient.Instance.StartCoroutine(ReceivePending(state).WrapToIl2Cpp());
        }
    }

    private static IEnumerator ReceivePending(Receiving state)
    {
        float deadline = Time.realtimeSinceStartup + 10f;
        while (state.Queue.Count > 0)
        {
            if (!state.Session.IsCurrent() || receiver != state) yield break;
            if (!ObjectsReady)
            {
                if (Time.realtimeSinceStartup > deadline) { Cancel("Native role readiness timeout", true); yield break; }
                yield return null;
                continue;
            }
            var entry = state.Queue.Dequeue();
            if (entry.Phase == 2)
            {
                if (!state.HostApplied) { Cancel("Restore completed before host role", true); yield break; }
                state.Complete = true;
                RestoreComplete = true;
                Phase = RoleDistributionPhase.Running;
                continue;
            }
            var player = Utils.GetPlayerById(entry.Subject);
            if (player == null) { Cancel("Native role subject disappeared", true); yield break; }
            if (entry.Phase == 1) Phase = RoleDistributionPhase.PublishingHostRole;
            Apply(player, entry.Role, true);
            if (entry.Phase == 1)
            {
                state.HostApplied = true;
                Phase = RoleDistributionPhase.RestoringStartMask;
            }
        }
        state.Draining = false;
    }
}
