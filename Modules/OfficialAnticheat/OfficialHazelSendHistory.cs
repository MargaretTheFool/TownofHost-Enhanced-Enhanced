using Hazel;
using Hazel.Udp;
using InnerNet;
using System;
using System.Text.Json;
using TOHE.Modules;

namespace TOHE;

internal static class OfficialHazelSendHistory
{
    private sealed record Entry(long Timestamp, DateTime SentAtUtc, byte[] Bytes);
    private static readonly Queue<Entry> Messages = new();
    private static readonly object Sync = new();

    internal static void Reset()
    {
        lock (Sync) Messages.Clear();
    }

    private static void Trim(long now)
    {
        while (Messages.Count > 0 && now - Messages.Peek().Timestamp > 10_000)
            Messages.Dequeue();
    }

    internal static void OnDisconnect(DisconnectReasons reason)
    {
        if (reason == DisconnectReasons.NewConnection) return;
        string capture = null;
        lock (Sync)
        {
            Trim(Environment.TickCount64);
            if ((reason is DisconnectReasons.Hacking or DisconnectReasons.Custom) &&
                AmongUsClient.Instance.NetworkMode == NetworkModes.OnlineGame)
            {
                // Base64(UTF-8 JSON). Array order is send order; System.Text.Json
                // encodes each byte[] as Base64 and DateTime as ISO 8601 UTC.
                capture = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new
                {
                    version = 1,
                    messages = Messages.Select(message => new { sentAtUtc = message.SentAtUtc, bytes = message.Bytes })
                }));
            }
            Messages.Clear();
        }
        if (capture != null) Logger.Warn($"Hazel message logger captured message: {capture}", "OfficialHazelSendHistory");
    }

    // Both plain UDP and DTLS clients inherit this Send implementation.
    // Capture successful MessageWriter submissions once, before caller recycling.
    // Bytes include the writer header, before transport encryption/retransmission.
    [HarmonyPatch(typeof(UdpConnection), nameof(UdpConnection.Send), typeof(MessageWriter))]
    private static class SendPatch
    {
        private static void Postfix(UdpConnection __instance, [HarmonyArgument(0)] MessageWriter message, SendErrors __result)
        {
            var client = AmongUsClient.Instance;
            if (__result != SendErrors.None || client == null || client.NetworkMode != NetworkModes.OnlineGame ||
                client.connection == null || client.connection.Pointer != __instance.Pointer) return;

            lock (Sync)
            {
                long now = Environment.TickCount64;
                Trim(now);
                Messages.Enqueue(new(now, DateTime.UtcNow, OfficialPacketBuilder.Snapshot(message)));
            }
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.Update))]
    private static class UpdatePatch
    {
        private static void Postfix()
        {
            lock (Sync) Trim(Environment.TickCount64);
        }
    }
}
