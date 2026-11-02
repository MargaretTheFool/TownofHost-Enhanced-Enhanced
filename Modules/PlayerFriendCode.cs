namespace TOHE.Modules;

internal static class PlayerFriendCode
{
    internal static bool IsValid(string code)
    {
        if (code == null || code.Length < 6 || code.Length > 32) return false;
        int separator = code.Length - 5;
        if (code[separator] != '#') return false;
        for (int i = 0; i < separator; i++)
            if (code[i] is < 'a' or > 'z') return false;
        for (int i = separator + 1; i < code.Length; i++)
            if (code[i] is < '0' or > '9') return false;
        return true;
    }

    // Native player-info synchronization can replace Data.FriendCode. Resolve
    // the connected owner and reject stale/reused player objects instead.
    internal static string FromOwner(PlayerControl player)
    {
        if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return string.Empty;
        return FromConnectedOwner(player);
    }

    internal static string FromConnectedOwner(PlayerControl player)
    {
        if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmConnected
            || player == null || player.Data == null || player.Data.Disconnected || player.OwnerId < 0)
            return string.Empty;
        var owner = AmongUsClient.Instance.FindClientById(player.OwnerId);
        return owner?.Character != null && owner.Character.Pointer == player.Pointer
            ? owner.FriendCode ?? string.Empty : string.Empty;
    }
}
