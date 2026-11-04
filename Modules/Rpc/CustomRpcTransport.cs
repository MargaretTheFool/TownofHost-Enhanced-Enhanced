using AmongUs.InnerNet.GameDataMessages;
using Hazel;
using Il2CppInterop.Runtime.Injection;
using System;

namespace TOHE;

// Main-thread transport. No raw root packet generation or alternate packetizer.
internal static class CustomRpcTransport
{
    private sealed record BuilderState(CustomRPC Rpc, int Target, SendOption Option, int Offset,
        uint NetId, int GameId, int OwnerId, int PlayerInstanceId, OfficialSessionContext Context);
    private static readonly RpcBuilderLeases<MessageWriter, BuilderState> Builders = new(writer => writer.Pointer);
    private static readonly Dictionary<IntPtr, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>> BuilderBuffers = new();
    private static bool registered;
    private static int mainThread;
    private static bool flushing;
    private static readonly List<RpcPayloadSnapshot> Pending = new();
    private static readonly Dictionary<long, TOHERpcMessage> Queued = new();
    private static readonly Dictionary<IntPtr, HashSet<long>> Packets = new();
    private static int pendingBytes;
    private static TOHERpcMessage pendingContext;
    private static uint transferId;
    private static long sequence;
    private static int generation;
    private static OfficialSessionContext? session;

    internal static void Register()
    {
        if (registered) { AssertMainThread(); return; }
        ClassInjector.RegisterTypeInIl2Cpp<TOHERpcMessage>();
        mainThread = Environment.CurrentManagedThreadId;
        registered = true;
    }

    internal static bool IsValidRpcId(uint rpc) => Enum.IsDefined(typeof(CustomRPC), rpc);

    private static void AssertMainThread()
    {
        if (!registered) throw new InvalidOperationException("CustomRpcTransport.Register must run before use");
        if (Environment.CurrentManagedThreadId != mainThread) throw new InvalidOperationException("RPC transport requires its registration thread");
    }

    private static (AmongUsClient Client, PlayerControl Player) GetContext()
    {
        AssertMainThread();
        var client = AmongUsClient.Instance;
        var player = PlayerControl.LocalPlayer;
        if (!client || !client.AmConnected || client.ClientId < 0 || !player || !player.AmOwner ||
            player.OwnerId != client.ClientId || player.NetId == uint.MaxValue)
            throw new InvalidOperationException("Connected locally owned PlayerControl required");
        if (session.HasValue && !session.Value.IsCurrent())
        {
            var current = OfficialSessionContext.Capture();
            bool sameRoom = session.Value.LobbyGeneration == current.LobbyGeneration &&
                session.Value.ClientIdentity == current.ClientIdentity && session.Value.ClientId == current.ClientId &&
                session.Value.GameId == current.GameId && session.Value.HostId == current.HostId;
            Reset(preserveCapabilities: sameRoom);
        }
        session ??= OfficialSessionContext.Capture();
        return (client, player);
    }

    private static void ValidateOption(SendOption option)
    {
        if (option != SendOption.Reliable) throw new ArgumentOutOfRangeException(nameof(option));
    }

    internal static MessageWriter Start(CustomRPC rpc, SendOption option = SendOption.Reliable, int target = -1)
    {
        RpcPayloadSnapshot.ValidateTarget(target);
        if (!IsValidRpcId((uint)rpc)) throw new ArgumentOutOfRangeException(nameof(rpc));
        ValidateOption(option);
        var (client, player) = GetContext();
        var writer = MessageWriter.Get(option);
        try
        {
            if (OfficialAnticheatPolicy.Enabled)
            {
                BuilderBuffers.Add(writer.Pointer, writer.Buffer);
                writer.Buffer = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>(1200);
            }
            Builders.Add(writer, new BuilderState(rpc, target, option, writer.Position,
                player.NetId, client.GameId, player.OwnerId, player.GetInstanceID(), OfficialSessionContext.Capture()));
            return writer;
        }
        catch { RecycleBuilder(writer); throw; }
    }

    private static void RecycleBuilder(MessageWriter writer)
    {
        if (BuilderBuffers.Remove(writer.Pointer, out var buffer)) writer.Buffer = buffer;
        writer.Recycle();
    }

    internal static void Finish(MessageWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        AssertMainThread();
        var state = Builders.Take(writer);
        byte[] payload;
        try
        {
            if (writer.Length < state.Offset || writer.Position != writer.Length)
                throw new InvalidOperationException("RPC builder has an invalid payload cursor");
            if (writer.Length - state.Offset > PackedRpcCodec.MaxPayload)
                throw new InvalidOperationException("RPC payload exceeds bounded transfer size");
            payload = new byte[writer.Length - state.Offset];
            var buffer = writer.Buffer;
            for (int index = 0; index < payload.Length; index++) payload[index] = buffer[state.Offset + index];
        }
        finally { RecycleBuilder(writer); }
        var (client, player) = GetContext();
        if (!state.Context.IsCurrent() || client.GameId != state.GameId || player.OwnerId != state.OwnerId || player.NetId != state.NetId ||
            player.GetInstanceID() != state.PlayerInstanceId) throw new InvalidOperationException("RPC builder belongs to an expired room");
        Send(new CustomRpcPayloadMessage(state.NetId, state.GameId, state.OwnerId, state.PlayerInstanceId,
            state.Rpc, state.Target, state.Option, payload));
    }

    internal static void Send(CustomRPC rpc, Action<MessageWriter> serialize, int target = -1, SendOption option = SendOption.Reliable)
    {
        ArgumentNullException.ThrowIfNull(serialize);
        var writer = Start(rpc, option, target);
        try { serialize(writer); Finish(writer); }
        catch
        {
            if (Builders.Cancel(writer)) RecycleBuilder(writer);
            throw;
        }
    }

    internal static void SendPayload(CustomRPC rpc, Action<RpcPayloadWriter> serialize, int target = -1,
        SendOption option = SendOption.Reliable)
    {
        ArgumentNullException.ThrowIfNull(serialize);
        RpcPayloadSnapshot.ValidateTarget(target);
        if (!IsValidRpcId((uint)rpc)) throw new ArgumentOutOfRangeException(nameof(rpc));
        ValidateOption(option);
        var (client, player) = GetContext();
        using var payload = new RpcPayloadWriter();
        serialize(payload);
        Send(new CustomRpcPayloadMessage(player.NetId, client.GameId, player.OwnerId, player.GetInstanceID(),
            rpc, target, option, payload.ToArray()));
    }

    internal static void Send(TOHERpcMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var (client, player) = GetContext();
        if (message.GetType() == typeof(TOHERpcMessage)) throw new ArgumentException("A payload subclass is required", nameof(message));
        if (flushing) throw new InvalidOperationException("RPC serialization must not recursively send messages");
        message.ValidateContext(client, player);
        var option = message.GetSendOption();
        ValidateOption(option);
        // The handshake must remain readable before either peer knows the other's
        // capabilities. A later joiner did not receive the room's first declaration.
        if (OfficialAnticheatPolicy.Enabled && message is CustomRpcPayloadMessage payload &&
            (CustomRPC)payload.Snapshot.RpcId is not (CustomRPC.ProtocolCapabilities
                or CustomRPC.VersionCheck or CustomRPC.RequestRetryVersionCheck))
        {
            RpcCompatibility.EnsureDeclared();
            RpcCompatibility.ValidateRecipient(payload.Snapshot.Target);
            // All compatible custom traffic uses Reliable, including any message
            // that later needs fragmentation. This preserves dependency order.
            if (PackedRpcCodec.HeaderLength(player.NetId) + PackedRpcCodec.EntryLength(payload.Snapshot) > PackedRpcCodec.ChildBudget)
            {
                foreach (var fragment in PackedRpcCodec.Fragment(payload.Snapshot, ++transferId, player.NetId)) Append(fragment, player.NetId, message);
            }
            else Append(payload.Snapshot, player.NetId, message);
            return;
        }
        FlushPending();
        Enqueue(message, client);
        if (OfficialAnticheatPolicy.Enabled) return;
        var nativeMessage = message.Cast<IGameDataMessage>();
        var queue = client.reliableMessageQueue;
        flushing = true;
        try
        {
            // Ignore the out flag: this game's implementation leaves it false even
            // when MTU packing stops with messages remaining. Require real progress.
            while (queue.Count > 0)
            {
                int before = queue.Count;
                client.PackAndSendQueuedMessages(queue, option, out _);
                if (queue.Count >= before) throw new InvalidOperationException("Native RPC queue made no progress");
            }
        }
        catch
        {
            // Do not leave this failed custom send for a later frame or room.
            // Rotate only to remove our entry; preserve all other native messages.
            int remaining = queue.Count;
            for (int index = 0; index < remaining; index++)
            {
                var pending = queue.Dequeue();
                if (pending.Pointer != nativeMessage.Pointer) queue.Enqueue(pending);
            }
            throw;
        }
        finally { Queued.Remove(message.Sequence); flushing = false; GC.KeepAlive(message); }
    }

    private static void Append(RpcPayloadSnapshot snapshot, uint netId, TOHERpcMessage contextMessage)
    {
        int bytes = PackedRpcCodec.EntryLength(snapshot);
        if (Pending.Count > 0 && (pendingContext.SenderNetId != netId ||
            pendingContext.GetSendOption() != contextMessage.GetSendOption() ||
            pendingBytes + bytes + PackedRpcCodec.HeaderLength(netId) > PackedRpcCodec.ChildBudget))
            FlushPending();
        pendingContext ??= contextMessage;
        Pending.Add(snapshot);
        pendingBytes += bytes;
    }

    private static void Enqueue(TOHERpcMessage message, AmongUsClient client)
    {
        message.Sequence = ++sequence;
        Queued.Add(message.Sequence, message);
        var nativeMessage = message.Cast<IGameDataMessage>();
        try
        {
            client.LateBroadcastReliableMessage(nativeMessage);
        }
        catch { Queued.Remove(message.Sequence); throw; }
    }

    internal static void FlushPending()
    {
        AssertMainThread();
        if (Pending.Count == 0) return;
        var (client, player) = GetContext();
        if (Pending.Count == 0) return; // stale-context cleanup
        try { pendingContext.ValidateContext(client, player); }
        catch { Reset(); throw; }
        var message = new PackedTOHERpcMessage(player.NetId, client.GameId, player.OwnerId,
            player.GetInstanceID(), Pending);
        // A peer may join after the previous declaration was broadcast. Keep
        // the declaration adjacent to each packed batch on the reliable queue,
        // including a batch already pending when a new handshake starts.
        var declaration = new CustomRpcPayloadMessage(player.NetId, client.GameId, player.OwnerId,
            player.GetInstanceID(), CustomRPC.ProtocolCapabilities, -1, SendOption.Reliable,
            RpcCompatibility.CreateDeclarationPayload());
        Pending.Clear(); pendingBytes = 0; pendingContext = null;
        Enqueue(declaration, client);
        Enqueue(message, client);
    }

    internal static RpcSendBarrier CaptureBarrier()
    {
        FlushPending();
        return new RpcSendBarrier(generation, sequence);
    }
    internal static bool BarrierCancelled(int capturedGeneration) => capturedGeneration != generation;
    internal static bool BarrierComplete(int capturedGeneration, long through)
        => capturedGeneration == generation && !Queued.Keys.Any(value => value <= through);
    internal static int BarrierPendingCount(int capturedGeneration, long through)
        => capturedGeneration == generation ? Queued.Keys.Count(value => value <= through) : 0;
    internal static void BeginNativePacket(MessageWriter writer) => Packets[writer.Pointer] = new();
    internal static void RecordSerialized(MessageWriter writer, TOHERpcMessage message)
    {
        if (!OfficialAnticheatPolicy.Enabled) return;
        if (!Packets.TryGetValue(writer.Pointer, out var messages)) Packets[writer.Pointer] = messages = new();
        messages.Add(message.Sequence);
    }
    internal static void CommitNativeChild(MessageWriter child, MessageWriter packet)
    {
        if (!Packets.Remove(child.Pointer, out var messages)) return;
        if (!Packets.TryGetValue(packet.Pointer, out var destination)) Packets[packet.Pointer] = destination = new();
        destination.UnionWith(messages);
    }
    internal static void DiscardNativePacket(MessageWriter writer) => Packets.Remove(writer.Pointer);
    internal static void NotifyPacketSent(MessageWriter writer)
    {
        if (!Packets.Remove(writer.Pointer, out var messages)) return;
        foreach (var id in messages) Queued.Remove(id);
    }

    // Invoke at room/plugin teardown to release abandoned builders. Native queues
    // are owned by InnerNetClient and cleared by its JoinGame lifecycle.
    internal static void Reset(bool preserveCapabilities = false)
    {
        AssertMainThread();
        Builders.Clear(RecycleBuilder);
        CustomRpcSender.CancelPending();
        OfficialImmediateRpc.CancelImmediate();
        var client = AmongUsClient.Instance;
        if (client != null)
        {
            RemoveOwned(client.reliableMessageQueue);
            RemoveOwned(client.unreliableMessageQueue);
        }
        Pending.Clear(); pendingBytes = 0; pendingContext = null; Queued.Clear(); Packets.Clear();
        CustomRpcReceiver.Reset();
        if (!preserveCapabilities) RpcCompatibility.ResetCapabilities();
        else RpcCompatibility.ResetDeclaration(); // an unsent local declaration may have been cancelled
        generation++; session = null;
    }

    private static void RemoveOwned(Il2CppSystem.Collections.Generic.Queue<IGameDataMessage> queue)
    {
        var owned = Queued.Values.Select(value => value.Pointer).ToHashSet();
        int remaining = queue.Count;
        for (int index = 0; index < remaining; index++)
        {
            var message = queue.Dequeue();
            if (!owned.Contains(message.Pointer)) queue.Enqueue(message);
        }
    }
}

internal sealed class RpcSendBarrier(int generation, long sequence)
{
    internal bool IsComplete => CustomRpcTransport.BarrierComplete(generation, sequence);
    internal bool IsCancelled => CustomRpcTransport.BarrierCancelled(generation);
    internal int PendingCount => CustomRpcTransport.BarrierPendingCount(generation, sequence);
}
