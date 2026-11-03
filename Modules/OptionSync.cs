using System;
using Hazel;
using UnityEngine;

namespace TOHE;

// One snapshot is captured once on the host and applied only after all its chunks arrive.
internal static class OptionSync
{
    private const int ChunkSize = 32;
    private const float RetryDelay = 3f;
    private const double CacheLifetime = 30d;

    private readonly record struct Room(uint Generation, IntPtr Client, int ClientId, int GameId, int HostId)
    {
        internal static Room Capture(AmongUsClient client)
            => new(OnGameJoinedPatch.Generation, client.Pointer, client.ClientId, client.GameId, client.HostId);

        internal bool IsCurrent()
        {
            var client = AmongUsClient.Instance;
            return client != null && client.AmConnected && Capture(client) == this;
        }
    }

    private sealed record Snapshot(uint Revision, int[] Values, int Target, IntPtr TargetIdentity, double Created);

    private sealed class PendingSnapshot(uint revision, int optionCount, Room room)
    {
        internal readonly uint Revision = revision;
        internal readonly Room Room = room;
        internal readonly int[] Values = new int[optionCount];
        internal readonly bool[] Chunks = new bool[(optionCount + ChunkSize - 1) / ChunkSize];
        internal int Received;
    }

    private static readonly Dictionary<uint, Snapshot> Snapshots = [];
    private static Room room;
    private static bool hasRoom;
    // The host's increasing revision is the sync nonce; all chunks of one snapshot share it.
    private static uint revision;
    private static uint latestSnapshot;
    private static uint completedSnapshot;
    private static uint[] deltaRevisions = [];
    private static int[] deltaValues = [];
    private static PendingSnapshot pending;

    private static void EnsureRoom(AmongUsClient client)
    {
        var current = Room.Capture(client);
        if (hasRoom && room == current && deltaRevisions.Length == OptionItem.AllOptions.Count) return;
        room = current;
        hasRoom = true;
        Snapshots.Clear();
        pending = null;
        latestSnapshot = 0;
        completedSnapshot = 0;
        deltaRevisions = new uint[OptionItem.AllOptions.Count];
        deltaValues = new int[OptionItem.AllOptions.Count];
        if (!client.AmHost) OptionItem.HasRemotePreset = false;
    }

    private static bool CanSync(int target)
    {
        if (target != -1)
        {
            var recipient = Utils.GetClientById(target);
            if (recipient == null || recipient.Character == null || !Main.playerVersion.ContainsKey(recipient.Id)) return false;
        }
        else if (!Main.AllPlayerControls.Any(player => player.IsNonHostModdedClient())) return false;

        return Main.AllPlayerControls.Length > 1 && AmongUsClient.Instance != null &&
            AmongUsClient.Instance.AmHost && PlayerControl.AllPlayerControls.Count > 1 &&
            PlayerControl.LocalPlayer != null;
    }

    private static void RemoveExpiredSnapshots()
    {
        var now = Time.realtimeSinceStartupAsDouble;
        foreach (var entry in Snapshots.Where(entry => now - entry.Value.Created >= CacheLifetime).ToArray())
            Snapshots.Remove(entry.Key);
    }

    internal static void SendAll(int target = -1)
    {
        if (!CanSync(target)) return;
        var client = AmongUsClient.Instance;
        EnsureRoom(client);
        RemoveExpiredSnapshots();
        if (target >= 0)
        {
            CustomRpcTransport.FlushPending();
            var queue = client.reliableMessageQueue;
            while (queue.Count > 0)
            {
                int before = queue.Count;
                client.PackAndSendQueuedMessages(queue, SendOption.Reliable, out _);
                if (OfficialNetworkSend.Failed(client) || queue.Count >= before)
                    throw new InvalidOperationException("Could not submit queued RPCs before the targeted options snapshot");
            }
        }

        var values = OptionItem.AllOptions.Select(option => option.GetValue()).ToArray();
        if (values.Length == 0) return;
        var recipient = target >= 0 ? Utils.GetClientById(target) : null;
        var snapshot = new Snapshot(++revision, values, target, recipient?.Pointer ?? IntPtr.Zero, Time.realtimeSinceStartupAsDouble);
        Snapshots.Add(snapshot.Revision, snapshot);
        int count = (values.Length + ChunkSize - 1) / ChunkSize;
        for (int index = 0; index < count; index++) SendChunk(snapshot, index, target);
    }

    internal static void SendOne(OptionItem option)
    {
        if (!CanSync(-1)) return;
        EnsureRoom(AmongUsClient.Instance);
        var options = OptionItem.AllOptions;
        for (int index = 0; index < options.Count; index++)
        {
            if (options[index] != option) continue;
            var updateRevision = ++revision;
            var optionIndex = index;
            var value = option.GetValue();
            CustomRpcTransport.Send(CustomRPC.SyncCustomSettings, writer =>
            {
                writer.Write(false);
                writer.Write(updateRevision);
                writer.WritePacked(optionIndex);
                writer.WritePacked(value);
            }, -1, SendOption.Reliable);
            return;
        }
    }

    private static void SendChunk(Snapshot snapshot, int chunk, int target)
    {
        void WriteChunk(MessageWriter writer)
        {
            // Full: flag, revision, total option count, chunk index, packed values.
            writer.Write(true);
            writer.Write(snapshot.Revision);
            writer.WritePacked(snapshot.Values.Length);
            writer.WritePacked(chunk);
            int start = chunk * ChunkSize;
            int end = Math.Min(start + ChunkSize, snapshot.Values.Length);
            for (int index = start; index < end; index++) writer.WritePacked(snapshot.Values[index]);
        }

        if (target < 0)
        {
            CustomRpcTransport.Send(CustomRPC.SyncCustomSettings, WriteChunk, -1, SendOption.Reliable);
            return;
        }

        var client = AmongUsClient.Instance;
        var writer = client.StartImmediate(PlayerControl.LocalPlayer.NetId, RpcPayloadSnapshot.OuterCallId,
            SendOption.Reliable, target);
        writer.Write(target);
        writer.Write((uint)CustomRPC.SyncCustomSettings);
        WriteChunk(writer);
        client.FinishImmediate(writer);
    }

    internal static void Receive(MessageReader reader)
    {
        var client = AmongUsClient.Instance;
        if (client.AmHost) return;
        EnsureRoom(client);
        bool full = reader.ReadBoolean();
        uint receivedRevision = reader.ReadUInt32();
        var options = OptionItem.AllOptions;
        if (!full)
        {
            // Delta: flag, revision, option index, packed value.
            int index = reader.ReadPackedInt32();
            int value = reader.ReadPackedInt32();
            if (index < 0 || index >= options.Count || reader.BytesRemaining != 0 ||
                receivedRevision <= completedSnapshot || receivedRevision <= deltaRevisions[index]) return;

            deltaRevisions[index] = receivedRevision;
            deltaValues[index] = value;
            if (pending == null && OptionItem.HasRemotePreset && options[index].Id != OptionItem.PresetId)
            {
                OptionItem.SetRemotePreset(true);
                if (options[index].CurrentValue != value) options[index].SetValue(value, doSave: false, doSync: false);
            }
            return;
        }

        int total = reader.ReadPackedInt32();
        int chunk = reader.ReadPackedInt32();
        if (total != options.Count || total <= 0 || chunk < 0 || chunk >= (total + ChunkSize - 1) / ChunkSize ||
            receivedRevision < latestSnapshot || (receivedRevision == latestSnapshot && pending == null)) return;

        int start = chunk * ChunkSize;
        int length = Math.Min(ChunkSize, total - start);
        var values = new int[length];
        for (int index = 0; index < length; index++) values[index] = reader.ReadPackedInt32();
        if (reader.BytesRemaining != 0) return;

        if (receivedRevision > latestSnapshot)
        {
            // A newer snapshot nonce replaces the old transfer and invalidates its retry task.
            latestSnapshot = receivedRevision;
            pending = new PendingSnapshot(receivedRevision, total, room);
            OptionItem.SetRemotePreset(true);
            OptionItem.HasRemotePreset = false;
            ScheduleRetry(pending);
        }
        var snapshot = pending;
        if (snapshot.Chunks[chunk]) return;
        Array.Copy(values, 0, snapshot.Values, start, length);
        snapshot.Chunks[chunk] = true;
        snapshot.Received++;
        if (snapshot.Received != snapshot.Chunks.Length) return;

        for (int index = 0; index < total; index++)
        {
            var option = options[index];
            if (option.Id == OptionItem.PresetId) continue;
            int value = deltaRevisions[index] > snapshot.Revision ? deltaValues[index] : snapshot.Values[index];
            if (option.CurrentValue != value) option.SetValue(value, doSave: false, doSync: false);
        }
        completedSnapshot = snapshot.Revision;
        pending = null;
        OptionItem.HasRemotePreset = true;
    }

    private static void ScheduleRetry(PendingSnapshot snapshot)
    {
        _ = new LateTask(() =>
        {
            if (!snapshot.Room.IsCurrent() || pending != snapshot) return;
            // Re-arm first, so a failed send can still be retried while this room remains current.
            ScheduleRetry(snapshot);
            var client = AmongUsClient.Instance;
            var writer = client.StartImmediate(PlayerControl.LocalPlayer.NetId, RpcPayloadSnapshot.OuterCallId,
                SendOption.Reliable, client.HostId);
            writer.Write(client.HostId);
            writer.Write((uint)CustomRPC.RequestOptionChunks);
            writer.Write(snapshot.Revision);
            writer.WritePacked(snapshot.Chunks.Length);
            for (int offset = 0; offset < snapshot.Chunks.Length; offset += 8)
            {
                byte missing = 0;
                for (int bit = 0; bit < 8 && offset + bit < snapshot.Chunks.Length; bit++)
                    if (!snapshot.Chunks[offset + bit]) missing |= (byte)(1 << bit);
                writer.Write(missing);
            }
            client.FinishImmediate(writer);
        }, RetryDelay, "Retry missing option chunks", false);
    }

    internal static void ReceiveRequest(PlayerControl sender, MessageReader reader)
    {
        var client = AmongUsClient.Instance;
        EnsureRoom(client);
        uint requestedRevision = reader.ReadUInt32();
        int count = reader.ReadPackedInt32();
        int expected = (OptionItem.AllOptions.Count + ChunkSize - 1) / ChunkSize;
        if (count != expected || count <= 0 || reader.BytesRemaining != (count + 7) / 8) return;
        var missing = new byte[(count + 7) / 8];
        bool any = false;
        for (int index = 0; index < missing.Length; index++)
        {
            missing[index] = reader.ReadByte();
            any |= missing[index] != 0;
        }
        if (!any || (count % 8 != 0 && (missing[^1] >> (count % 8)) != 0)) return;

        RemoveExpiredSnapshots();
        if (!Snapshots.TryGetValue(requestedRevision, out var snapshot))
        {
            SendAll(sender.OwnerId);
            return;
        }
        var requester = Utils.GetClientById(sender.OwnerId);
        if (snapshot.Target >= 0 && (snapshot.Target != sender.OwnerId || requester == null ||
            requester.Pointer != snapshot.TargetIdentity)) return;
        for (int index = 0; index < count; index++)
            if ((missing[index / 8] & (1 << (index % 8))) != 0) SendChunk(snapshot, index, sender.OwnerId);
    }
}
