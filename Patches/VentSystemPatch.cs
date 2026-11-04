using Hazel;
using System;

namespace TOHE.Patches;

// Patches here is also activated from ShipStatus.Serialize and IntroCutScene 
// through Utils.SetAllVentInteractions

[HarmonyPatch(typeof(VentilationSystem), nameof(VentilationSystem.PerformVentOp))]
static class PerformVentOpPatch
{
    public static bool Prefix(VentilationSystem __instance, [HarmonyArgument(0)] byte playerId, [HarmonyArgument(1)] VentilationSystem.Operation op/*, [HarmonyArgument(2)] byte ventId*/, [HarmonyArgument(3)] SequenceBuffer<VentilationSystem.VentMoveInfo> seqBuffer)
    {
        if (!AmongUsClient.Instance.AmHost) return true;
        if (Utils.GetPlayerById(playerId) == null) return true;
        switch (op)
        {
            case VentilationSystem.Operation.Move:
                if (!__instance.PlayersInsideVents.ContainsKey(playerId))
                {
                    seqBuffer.BumpSid();
                    return false;
                }

                break;
        }

        return true;
    }
}
[HarmonyPatch(typeof(VentilationSystem), nameof(VentilationSystem.Deteriorate))]
static class VentSystemDeterioratePatch
{
    public static Dictionary<byte, int> LastClosestVent = [];
    public static long LastUpadate;
    public static bool ForceUpdate;

    public static void Postfix(VentilationSystem __instance)
    {
        if (!AmongUsClient.Instance.AmHost || !Main.IntroDestroyed) return;

        var nowTime = Utils.GetTimeStamp();
        if (ForceUpdate || (nowTime != LastUpadate))
        {
            ForceUpdate = false;
            LastUpadate = nowTime;
            var batch = new OfficialPacketBuilder.Batch();
            foreach (var pc in PlayerControl.AllPlayerControls.GetFastEnumerator())
            {
                if (pc.BlockVentInteraction())
                {
                    LastClosestVent[pc.PlayerId] = pc.GetVentsFromClosest()[0].Id;
                    pc.RpcCloseVent(__instance, batch);
                }
            }
            batch.Send();
        }
    }
    /// <summary>
    /// Check blocking vents
    /// </summary>
    public static bool BlockVentInteraction(this PlayerControl pc)
    {
        if (!pc.AmOwner && pc.IsAlive() && (!pc.CanUseVents() || pc.HasAnyBlockedVent()))
        {
            return true;
        }
        return false;
    }

    public static void SerializeV2(VentilationSystem __instance, PlayerControl player = null)
    {
        var batch = new OfficialPacketBuilder.Batch();
        foreach (var pc in PlayerControl.AllPlayerControls.GetFastEnumerator())
        {
            if (pc.AmOwner || (player != null && pc != player)) continue;

            if (pc.BlockVentInteraction())
            {
                pc.RpcCloseVent(__instance, batch);
            }
            else
            {
                pc.RpcSerializeVent(__instance, batch);
            }
        }
        batch.Send();
    }
    /// <summary>
    /// Send rpc for blocking specifics vent use or all vents
    /// </summary>
    private static void RpcCloseVent(this PlayerControl pc, VentilationSystem __instance, OfficialPacketBuilder.Batch batch)
    {
        batch.AddChild(pc.GetClientId(), writer =>
        {
            writer.StartMessage(1);
            writer.WritePacked(ShipStatus.Instance.NetId);
            {
                writer.StartMessage((byte)SystemTypes.Ventilation);
                int vents = 0;
                foreach (var vent in ShipStatus.Instance.AllVents)
                {
                    if (pc.CantUseVent(vent.Id))
                        ++vents;
                }
                List<NetworkedPlayerInfo> AllPlayers = [];
                foreach (var playerInfo in GameData.Instance.AllPlayers.GetFastEnumerator())
                {
                    if (playerInfo != null && !playerInfo.Disconnected)
                        AllPlayers.Add(playerInfo);
                }
                int maxVents = Math.Min(vents, AllPlayers.Count);
                int blockedVents = 0;
                writer.WritePacked(maxVents);
                foreach (var vent in pc.GetVentsFromClosest())
                {
                    if (pc.CantUseVent(vent.Id))
                    {
                        writer.Write(AllPlayers[blockedVents].PlayerId);
                        writer.Write((byte)vent.Id);
                        ++blockedVents;
                    }
                    if (blockedVents >= maxVents)
                        break;
                }
                writer.WritePacked(__instance.PlayersInsideVents.Count);
                foreach (Il2CppSystem.Collections.Generic.KeyValuePair<byte, byte> keyValuePair2 in __instance.PlayersInsideVents)
                {
                    writer.Write(keyValuePair2.Key);
                    writer.Write(keyValuePair2.Value);
                }
                writer.EndMessage();
            }
            writer.EndMessage();
        });
    }

    private static void RpcSerializeVent(this PlayerControl pc, VentilationSystem __instance, OfficialPacketBuilder.Batch batch)
    {
        batch.AddChild(pc.GetClientId(), writer =>
        {
            writer.StartMessage(1);
            writer.WritePacked(ShipStatus.Instance.NetId);
            {
                writer.StartMessage((byte)SystemTypes.Ventilation);
                {
                    __instance.Serialize(writer, false);
                }
                writer.EndMessage();
            }
            writer.EndMessage();
        });
    }
}
[HarmonyPatch(typeof(VentilationSystem), nameof(VentilationSystem.IsVentCurrentlyBeingCleaned))]
static class VentSystemIsVentCurrentlyBeingCleanedPatch
{
    // Patch block use vent for host becouse host always skips RpcSerializeVent
    public static bool Prefix([HarmonyArgument(0)] int id, ref bool __result)
    {
        if (!AmongUsClient.Instance.AmHost) return true;

        if (PlayerControl.LocalPlayer.CantUseVent(id))
        {
            __result = true;
            return false;
        }

        // Run original code if host not have bloked vent
        return true;
    }
}
