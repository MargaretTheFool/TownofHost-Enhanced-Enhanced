using System;

namespace TOHE.Modules;

internal static class ServerRegion
{
    private static IRegionInfo connectedRegion;
    private static IRegionInfo foundRegion;
    private static int foundGameId;
    private static string foundAddress;
    private static ushort foundPort;
    private static string matchmakerAddress;
    private static string foundMatchmakerAddress;
    private static string connectedMatchmakerAddress;
    private static string connectedAddress;

    // The menu selection is restored after a cross-region game-code lookup.
    // Keep the region that supplied the listing until its endpoint is joined.
    public static IRegionInfo Current => connectedRegion ?? ServerManager.Instance?.CurrentRegion;
    internal static string ConnectedMatchmakerAddress => connectedMatchmakerAddress;
    public static bool IsOfficialConnection => IsOfficialHost(connectedAddress) ||
        IsOfficialHost(connectedRegion != null ? connectedMatchmakerAddress : ServerManager.Instance?.CurrentUdpServer?.Ip);

    public static void RememberMatchmakerSource(string address) => matchmakerAddress = address;

    public static void ClearLookup() => ClearLookup(true);

    public static void ClearLookup(bool clearRequestSource)
    {
        foundRegion = null;
        foundAddress = null;
        foundMatchmakerAddress = null;
        if (clearRequestSource) matchmakerAddress = null;
    }

    public static void RememberFoundGame(HttpMatchmakerManager.FindGameByCodeResponse response)
    {
        if (response == null) return;

        foundRegion = ServerManager.Instance?.CurrentRegion?.Duplicate();
        foundGameId = response.Game.GameId;
        foundAddress = response.Game.IPString;
        foundPort = response.Game.Port;
        foundMatchmakerAddress = matchmakerAddress ?? ServerManager.Instance?.CurrentUdpServer?.Ip;
    }

    public static void BeginConnection(int gameId, string address, ushort port)
    {
        OfficialHazelSendHistory.Reset();
        var fromLookup = foundRegion != null && foundGameId == gameId &&
                         foundAddress == address && foundPort == port;
        connectedRegion = fromLookup ? foundRegion : ServerManager.Instance?.CurrentRegion?.Duplicate();
        connectedMatchmakerAddress = fromLookup ? foundMatchmakerAddress :
            matchmakerAddress ?? ServerManager.Instance?.CurrentUdpServer?.Ip;
        connectedAddress = address;
        OfficialSessionContext.Reset();
        OfficialAnticheatPolicy.Reset();
        TOHE.Patches.OfficialAnticheatLifecyclePatch.ResetOperations();
        ClearLookup();
    }

    public static void ClearConnection()
    {
        connectedRegion = null;
        connectedAddress = null;
        connectedMatchmakerAddress = null;
        ClearLookup();
    }

    public static bool IsOfficialHost(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return false;

        // ServerInfo.Ip may be an HTTP URL; PingServer is a bare hostname.
        if (address.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) return false;
            address = uri.Host;
        }

        address = address.Trim().TrimEnd('.');
        return address.Equals("among.us", StringComparison.OrdinalIgnoreCase) ||
               address.EndsWith(".among.us", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsOfficial(IRegionInfo region)
    {
        return region?.TryCast<StaticHttpRegionInfo>() is { } httpRegion &&
               IsOfficialHost(httpRegion.PingServer) && httpRegion.Servers.Length > 0 &&
               httpRegion.Servers.All(server => IsOfficialHost(server.Ip));
    }
}
