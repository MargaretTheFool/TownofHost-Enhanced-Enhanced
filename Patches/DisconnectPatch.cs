using AmongUs.Data.Player;

namespace TOHE;

[HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnDisconnected))]
class OnDisconnectedPatch
{
    public static void Postfix(/*AmongUsClient __instance*/)
    {
        BanManager.ClearSessionBans();
        CheckMurderPatch.LastKillAttemptTime.Clear();
        Main.VisibleTasksCount = false;
    }
}

[HarmonyPatch(typeof(PlayerBanData), nameof(PlayerBanData.BanMinutesLeft), MethodType.Getter)]
public static class PlayerBanData_BanMinutesLeft_Getter
{
    public static void Postfix(PlayerBanData __instance, ref int __result)
    {
        __instance.BanPoints = 0f;
        __result = 0;
    }
}
