using AmongUs.GameOptions;
using Hazel;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using InnerNet;
using UnityEngine;

namespace TOHE.Modules;

// Adapted from Overloaded (https://github.com/All-Of-Us-Mods/Overloaded),
// a CrowdedMod fork. See Resources/Notices/Overloaded.LICENSE.txt.
[HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.HostGame))]
internal static class OverloadedHostGamePatch
{
    private static void Prefix([HarmonyArgument(0)] IGameOptions settings) => ServerLimits.Normalize(settings);
}

[HarmonyPatch(typeof(SecurityLogger), nameof(SecurityLogger.Awake))]
internal static class OverloadedSecurityLoggerPatch
{
    private static void Postfix(SecurityLogger __instance)
    {
        __instance.Timers = new Il2CppStructArray<float>(127);
        if (SecurityLogger.hits.Length < 127) SecurityLogger.hits = new Il2CppReferenceArray<Collider2D>(127);
    }
}

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckColor))]
internal static class OverloadedCheckColorPatch
{
    internal static bool AllowDuplicateColors => AmongUsClient.Instance != null &&
        (AmongUsClient.Instance.NetworkMode != NetworkModes.OnlineGame || !ServerLimits.IsOfficial) &&
        GameOptionsManager.Instance != null && GameOptionsManager.Instance.CurrentGameOptions != null &&
        GameOptionsManager.Instance.CurrentGameOptions.MaxPlayers > Palette.PlayerColors.Length;

    private static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] byte color)
    {
        if (!AllowDuplicateColors || color >= Palette.PlayerColors.Length) return true;
        __instance.RpcSetColor(color);
        return false;
    }
}

[HarmonyPatch(typeof(PlayerTab), nameof(PlayerTab.UpdateAvailableColors))]
internal static class OverloadedAvailableColorsPatch
{
    private static bool Prefix(PlayerTab __instance)
    {
        if (!OverloadedCheckColorPatch.AllowDuplicateColors) return true;
        __instance.AvailableColors.Clear();
        for (int i = 0; i < Palette.PlayerColors.Length; i++)
            if (!PlayerControl.LocalPlayer || PlayerControl.LocalPlayer.CurrentOutfit.ColorId != i)
                __instance.AvailableColors.Add(i);
        return false;
    }
}

[HarmonyPatch(typeof(PlayerTab), nameof(PlayerTab.Update))]
internal static class OverloadedSelectedColorPatch
{
    private static void Postfix(PlayerTab __instance)
    {
        if (OverloadedCheckColorPatch.AllowDuplicateColors) __instance.currentColorIsEquipped = false;
    }
}

// The built-in local server has its own hard-coded join and rejoin limits.
[HarmonyPatch(typeof(InnerNetServer), nameof(InnerNetServer.HandleNewGameJoin))]
internal static class OverloadedLocalServerJoinPatch
{
    internal static void Reject(InnerNetServer.Player client)
    {
        var writer = MessageWriter.Get(SendOption.Reliable);
        try
        {
            writer.StartMessage(1);
            writer.Write(1);
            writer.EndMessage();
            client.Connection.Send(writer);
        }
        finally
        {
            writer.Recycle();
        }
    }

    private static bool Prefix(InnerNetServer __instance, [HarmonyArgument(0)] InnerNetServer.Player client)
    {
        int capacity = GameOptionsManager.Instance.CurrentGameOptions.MaxPlayers;
        if (__instance.Clients.Count >= capacity || __instance.Clients.Count >= 127)
        {
            Reject(client);
            return false;
        }
        if (__instance.Clients.Count < 15) return true;

        __instance.Clients.Add(client);
        client.LimboState = LimboStates.PreSpawn;
        if (__instance.HostId == -1) __instance.HostId = __instance.Clients[0].Id;
        if (__instance.HostId == client.Id) client.LimboState = LimboStates.NotLimbo;

        var writer = MessageWriter.Get(SendOption.Reliable);
        try
        {
            __instance.WriteJoinedMessage(client, writer, true);
            client.Connection.Send(writer);
            __instance.BroadcastJoinMessage(client, writer);
        }
        catch (Il2CppException error)
        {
            Main.Logger.LogError($"Local server could not send its join message: {error}");
        }
        finally
        {
            writer.Recycle();
        }
        return false;
    }
}

[HarmonyPatch(typeof(InnerNetServer), nameof(InnerNetServer.HandleRejoin))]
internal static class OverloadedLocalServerRejoinPatch
{
    private static bool Prefix(InnerNetServer __instance, [HarmonyArgument(0)] InnerNetServer.Player client)
    {
        // The host returns through the native path and occupies the reserved slot.
        if (client.Id == __instance.HostId) return true;
        int capacity = GameOptionsManager.Instance.CurrentGameOptions.MaxPlayers;
        if (__instance.Clients.Count >= capacity - 1 || __instance.Clients.Count >= 126)
        {
            OverloadedLocalServerJoinPatch.Reject(client);
            return false;
        }
        if (__instance.Clients.Count < 14) return true;

        __instance.Clients.Add(client);
        client.LimboState = LimboStates.WaitingForHost;
        var writer = MessageWriter.Get(SendOption.Reliable);
        try
        {
            writer.StartMessage(12);
            writer.Write(32);
            writer.Write(client.Id);
            writer.EndMessage();
            client.Connection.Send(writer);
            __instance.BroadcastJoinMessage(client, writer);
        }
        catch (Il2CppException error)
        {
            Main.Logger.LogError($"Local server could not send its rejoin message: {error}");
        }
        finally
        {
            writer.Recycle();
        }
        return false;
    }
}

[HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.Update))]
internal static class OverloadedLocalPlayerCounterPatch
{
    private static GameStartManager lastManager;
    private static int lastCount = -1, lastCapacity = -1, lastMinimum = -1;

    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix(GameStartManager __instance)
    {
        if (!AmongUsClient.Instance || AmongUsClient.Instance.NetworkMode != NetworkModes.LocalGame ||
            !GameData.Instance || !GameManager.Instance || GameManager.Instance.LogicOptions == null) return;
        int count = GameData.Instance.PlayerCount;
        int capacity = GameManager.Instance.LogicOptions.MaxPlayers;
        int minimum = __instance.MinPlayers;
        if (lastManager == __instance && lastCount == count && lastCapacity == capacity && lastMinimum == minimum) return;
        lastManager = __instance;
        lastCount = count;
        lastCapacity = capacity;
        lastMinimum = minimum;
        string color = count > minimum ? "#00FF00FF" : count == minimum ? "#FFFF00FF" : "#FF0000FF";
        __instance.PlayerCounter.text = $"<color={color}>{count}/{capacity}";
    }
}
