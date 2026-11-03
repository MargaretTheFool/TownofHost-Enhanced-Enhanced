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
