namespace TOHE.Modules;

internal static class PlayerIdentifiers
{
    private static readonly string[] Prefixes = Enumerable.Range(0, 127).Select(id => $"{id} ").ToArray();

    public static bool Manages(bool modded) => Options.PlayerIdAudience?.GetInt() switch
    {
        1 => !modded,
        2 => true,
        _ => false
    };

    public static bool Shows(bool modded, bool meeting) => Manages(modded) && (Options.PlayerIdPeriod?.GetInt() switch
    {
        0 => !meeting,
        1 => meeting,
        2 => true,
        _ => false
    });

    public static string Prefix(byte id, string name) => Prefixes[id] + name;

    public static bool ReplacesRoleName(PlayerControl seer) => Manages(seer.IsModded()) && seer.GetCustomRole() is
        CustomRoles.Judge or CustomRoles.Inspector or CustomRoles.Swapper or CustomRoles.Councillor or
        CustomRoles.Doomsayer or CustomRoles.NiceGuesser or CustomRoles.EvilGuesser;

    public static void RefreshLobby(PlayerControl target = null, PlayerControl seer = null, bool force = false)
    {
        if (Options.PlayerIdAudience == null || Options.PlayerIdPeriod == null || AmongUsClient.Instance == null ||
            !AmongUsClient.Instance.AmHost || !GameStates.IsLobby) return;

        var players = Main.AllPlayerControls.Where(player => player != null && player.Data != null && !player.Data.Disconnected).ToArray();
        foreach (var viewer in players)
        {
            if (viewer.AmOwner || (seer != null && viewer != seer)) continue;
            bool show = Shows(viewer.IsModded(), false) && !viewer.IsModded();
            foreach (var player in players)
            {
                if (target != null && player != target) continue;
                if (!show && !Main.LastNotifyNames.ContainsKey((player.PlayerId, viewer.PlayerId))) continue;
                string name = show ? Prefix(player.PlayerId, player.name) : player.name;
                player.RpcSetNamePrivate(name, viewer, force);
            }
        }
    }
}
