using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using AmongUs.Data;
using InnerNet;

namespace TOHE.Modules;

internal static class CloudRooms
{
    internal sealed record Forbidden(Uri Origin, string Identity, string Token, string Reason);
    internal enum RequestOperation { None, Create, Update, Delete }
    internal readonly record struct SharingStatus(bool IsShared, bool IsRequestInFlight, RequestOperation Operation);
    private sealed record Acknowledgement(string RoomId, long Revision, DateTimeOffset AcceptedAt,
        DateTimeOffset ExpiresAt, int HeartbeatAfterSeconds);

    internal static readonly ConcurrentQueue<string> Messages = new();
    internal static readonly ConcurrentQueue<Forbidden> ForbiddenReasons = new();
    private static readonly object gate = new();
    private static readonly (string Id, string Name, string Url)[] knownRegions =
    {
        ("na", "North America", "https://matchmaker.among.us/"),
        ("eu", "Europe", "https://matchmaker-eu.among.us/"),
        ("as", "Asia", "https://matchmaker-as.among.us/"),
        ("nikona", "NikoNA", "https://au-us.niko233.top/"),
        ("nikoas", "NikoAS", "https://au-as.niko233.top/"),
        ("nikoeu", "NikoEU", "https://au-eu.niko233.top/"),
        ("mna", "Modded NA", "https://aumods.org/"),
        ("meu", "Modded EU", "https://au-eu.duikbo.at/"),
        ("mas", "Modded AS", "https://au-as.duikbo.at/"),
        ("maulna", "MAUL NA", "https://au.moddedamong.us/"),
        ("mauleu", "MAUL EU", "https://eu.moddedamong.us/"),
        ("aoueu", "All of Us EU", "https://eu.allofus.dev/")
    };

    private static Uri desiredOrigin;
    private static string desiredIdentity;
    private static string desiredToken;
    private static string desiredKey;
    private static string desiredSnapshot;
    private static long desiredVersion;
    private static long desiredEpoch;
    private static Uri activeOrigin;
    private static string activeIdentity;
    private static string activeToken;
    private static string activeKey;
    private static string activeRoomId;
    private static string activeSnapshot;
    private static long activeEpoch;
    private static bool paused;
    private static bool forbiddenPending;
    private static bool running;
    private static long retryAt;
    private static long rateLimitedUntil;
    private static long heartbeatAt;
    private static Uri confirmedOrigin;
    private static string confirmedIdentity;
    private static string confirmedKey;
    private static string confirmedRoomId;
    private static long confirmedUntil;
    private static string requestKey;
    private static RequestOperation requestOperation;
    private static string lastNotice;
    private static Uri unauthorizedOrigin;
    private static string unauthorizedToken;

    internal static bool TrySnapshot(AmongUsClient client, int gameVersion, IReadOnlyList<CloudAccount.Region> regions,
        out string json, out string roomKey)
    {
        json = roomKey = null;
        var region = ServerRegion.Current;
        string source = ServerRegion.ConnectedMatchmakerAddress;
        if (region == null || !Uri.TryCreate(source, UriKind.Absolute, out var request)
            || request.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(request.UserInfo)) return false;
        const string suffix = "/api/games";
        int api = request.AbsolutePath.LastIndexOf(suffix, StringComparison.OrdinalIgnoreCase);
        int end = api + suffix.Length;
        if (api < 0 || (end < request.AbsolutePath.Length && request.AbsolutePath[end] != '/')) return false;
        var builder = new UriBuilder(request) { Path = request.AbsolutePath[..api] + "/", Query = string.Empty, Fragment = string.Empty };
        Uri matchmaker = builder.Uri;
        string id = "custom";
        string name = region.Name;
        foreach (var known in regions)
        {
            if (known.Kind == "known" && known.Id is not ("all" or "custom")
                && Uri.TryCreate(known.Url, UriKind.Absolute, out var configured) && SameEndpoint(matchmaker, configured))
            {
                id = known.Id;
                name = known.Name;
                break;
            }
        }
        if (id == "custom")
        {
            foreach (var known in knownRegions)
            {
                if (!SameEndpoint(matchmaker, new Uri(known.Url))) continue;
                id = known.Id;
                name = known.Name;
                break;
            }
        }

        string code = GameCode.IntToGameName(client.GameId);
        var player = PlayerControl.LocalPlayer;
        var options = GameOptionsManager.Instance?.CurrentGameOptions;
        if ((!GameStates.IsLobby && !client.IsGameStarted && !client.IsGameOver)
            || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code) || !player || player.Data == null || options == null
            || client.allClients.Count > options.MaxPlayers) return false;
        string hostName = Main.AllPlayerNames.TryGetValue(player.PlayerId, out var realName) ? realName : player.Data.PlayerName;
        hostName = new string((hostName ?? string.Empty).RemoveHtmlTags().Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (hostName.Length > 64)
        {
            hostName = hostName[..64];
            if (char.IsHighSurrogate(hostName[^1])) hostName = hostName[..^1];
            hostName = hostName.TrimEnd();
        }
        if (hostName.Length == 0) return false;
        string status = GameStates.IsLobby ? "Lobby" : client.IsGameOver ? "Ended" : client.IsGameStarted ? "Started" : "Lobby";
        var game = new
        {
            code, host_name = hostName, status, game_version = gameVersion,
            player_count = client.allClients.Count, max_players = options.MaxPlayers,
            chat_lang = (uint)options.Keywords, chat_mode = (int)DataManager.Settings.Multiplayer.ChatMode,
            map_id = (int)options.MapId, region_id = id,
            mods = new[] { new { id = Main.PluginGuid, version = Main.PluginVersion, display_version = Main.PluginDisplayVersion, flags = 0 } }
        };
        json = JsonSerializer.Serialize(new { game, region = new { id, name, url = matchmaker.AbsoluteUri } });
        roomKey = $"{client.GameId}:{OnGameJoinedPatch.Generation}:{matchmaker.AbsoluteUri}";
        return true;
    }

    private static bool SameEndpoint(Uri actual, Uri known) =>
        string.IsNullOrEmpty(known.UserInfo) && string.IsNullOrEmpty(known.Query) && string.IsNullOrEmpty(known.Fragment)
        && Uri.Compare(actual, known, UriComponents.SchemeAndServer, UriFormat.UriEscaped, StringComparison.OrdinalIgnoreCase) == 0
        && string.Equals(actual.AbsolutePath.TrimEnd('/'), known.AbsolutePath.TrimEnd('/'), StringComparison.Ordinal);

    internal static SharingStatus GetStatus(string roomKey)
    {
        lock (gate)
            return new(roomKey != null && roomKey == confirmedKey && Environment.TickCount64 < confirmedUntil,
                running, roomKey != null && roomKey == requestKey ? requestOperation : RequestOperation.None);
    }

    internal static void Publish(Uri origin, string identity, string token, string key, string snapshot)
    {
        lock (gate)
        {
            if (unauthorizedToken != null && (token != unauthorizedToken || origin != unauthorizedOrigin))
            {
                unauthorizedToken = null;
                unauthorizedOrigin = null;
                retryAt = rateLimitedUntil;
            }
            bool roomChanged = desiredOrigin != origin || desiredIdentity != identity || desiredKey != key;
            bool changed = roomChanged || desiredSnapshot != snapshot || desiredToken != token;
            if (roomChanged) desiredEpoch++;
            desiredOrigin = origin;
            desiredIdentity = identity;
            desiredToken = token;
            desiredKey = key;
            desiredSnapshot = snapshot;
            paused = false;
            if (activeIdentity == identity && activeOrigin == origin) activeToken = token;
            if (changed) desiredVersion++;
            long now = Environment.TickCount64;
            if (!running && !forbiddenPending && (token != unauthorizedToken || origin != unauthorizedOrigin)
                && now >= retryAt && (activeRoomId == null || activeEpoch != desiredEpoch
                    || activeSnapshot != snapshot || now >= heartbeatAt))
            {
                running = true;
                _ = Task.Run(Run);
            }
        }
    }

    internal static void Pause()
    {
        lock (gate) paused = true;
    }

    // The consumer releases this only after handling or discarding the event.
    internal static void ForbiddenHandled()
    {
        lock (gate) forbiddenPending = false;
    }

    internal static void Stop()
    {
        lock (gate)
        {
            if (desiredSnapshot != null)
            {
                desiredSnapshot = null;
                desiredVersion++;
                desiredEpoch++;
                retryAt = rateLimitedUntil;
            }
            if (!running && activeRoomId != null && Environment.TickCount64 >= retryAt
                && (activeToken != unauthorizedToken || activeOrigin != unauthorizedOrigin))
            {
                running = true;
                _ = Task.Run(Run);
            }
        }
    }

    // A single chain serializes create, update and deletion, including late creates.
    private static async Task Run()
    {
        while (true)
        {
            Uri origin;
            string identity, token, key, snapshot, roomId;
            long version, epoch, requestStartedAt;
            bool deleting;
            HttpMethod method;
            Task<CloudService.Response> pendingRequest;
            lock (gate)
            {
                deleting = activeRoomId != null && (desiredSnapshot == null || activeEpoch != desiredEpoch);
                if (deleting)
                {
                    origin = activeOrigin; identity = activeIdentity; token = activeToken;
                    key = activeKey; roomId = activeRoomId; snapshot = null; epoch = activeEpoch;
                }
                else if (desiredSnapshot != null && !paused && !forbiddenPending)
                {
                    origin = desiredOrigin; identity = desiredIdentity; token = desiredToken;
                    key = desiredKey; roomId = activeRoomId; snapshot = desiredSnapshot; epoch = desiredEpoch;
                }
                else { running = false; return; }
                if (token == unauthorizedToken && origin == unauthorizedOrigin) { running = false; return; }
                version = desiredVersion;
                method = deleting ? HttpMethod.Delete : roomId == null ? HttpMethod.Post : HttpMethod.Put;
                requestKey = key;
                requestOperation = deleting ? RequestOperation.Delete : roomId == null ? RequestOperation.Create : RequestOperation.Update;
                requestStartedAt = Environment.TickCount64;
                string path = roomId == null ? "/api/mod/rooms" : "/api/mod/rooms/" + Uri.EscapeDataString(roomId);
                // Start the asynchronous request before Pause or Stop can take the gate.
                pendingRequest = CloudService.Send(origin, method, path, token, snapshot);
            }

            var response = await pendingRequest.ConfigureAwait(false);
            string acknowledgedId = null;
            Acknowledgement ack = null;
            if ((method == HttpMethod.Post && response.Status == HttpStatusCode.Created)
                || (method == HttpMethod.Put && response.Status == HttpStatusCode.OK))
            {
                try
                {
                    using var document = JsonDocument.Parse(response.Body);
                    var root = document.RootElement;
                    if (root.TryGetProperty("room_id", out var idValue) && idValue.ValueKind == JsonValueKind.String)
                        acknowledgedId = idValue.GetString();
                    if (!string.IsNullOrWhiteSpace(acknowledgedId) && (roomId == null || roomId == acknowledgedId)
                        && root.TryGetProperty("revision", out var revision) && revision.TryGetInt64(out long revisionValue) && revisionValue > 0
                        && root.TryGetProperty("accepted_at", out var accepted) && accepted.TryGetDateTimeOffset(out var acceptedValue)
                        && root.TryGetProperty("expires_at", out var expires) && expires.TryGetDateTimeOffset(out var expiresValue) && expiresValue > acceptedValue
                        && root.TryGetProperty("heartbeat_after_seconds", out var heartbeat) && heartbeat.TryGetInt32(out int heartbeatValue) && heartbeatValue > 0)
                        ack = new(acknowledgedId, revisionValue, acceptedValue, expiresValue, heartbeatValue);
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { }
            }
            string reason = "FORBIDDEN";
            if (response.Status == HttpStatusCode.Forbidden)
            {
                try
                {
                    using var document = JsonDocument.Parse(response.Body);
                    if (document.RootElement.TryGetProperty("error", out var error)
                        && error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String)
                        reason = code.GetString() ?? reason;
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
            }

            lock (gate)
            {
                requestKey = null;
                requestOperation = RequestOperation.None;
                // Keep a returned ID even if another ACK field is invalid, so a late create can still be removed.
                if (!deleting && method == HttpMethod.Post && !string.IsNullOrWhiteSpace(acknowledgedId))
                {
                    activeOrigin = origin; activeIdentity = identity; activeKey = key; activeRoomId = acknowledgedId; activeEpoch = epoch;
                    activeToken = desiredOrigin == origin && desiredIdentity == identity ? desiredToken : token;
                    activeSnapshot = null;
                }
                if (deleting && response.Status is HttpStatusCode.NoContent or HttpStatusCode.OK or HttpStatusCode.NotFound)
                {
                    if (confirmedOrigin == origin && confirmedIdentity == identity && confirmedRoomId == roomId)
                    {
                        confirmedKey = confirmedRoomId = confirmedIdentity = null;
                        confirmedOrigin = null;
                    }
                    activeRoomId = activeKey = activeToken = activeIdentity = activeSnapshot = null;
                    activeOrigin = null;
                    retryAt = rateLimitedUntil = 0;
                    lastNotice = null;
                    continue;
                }
                if (ack != null)
                {
                    activeOrigin = origin; activeIdentity = identity; activeKey = key; activeRoomId = ack.RoomId; activeEpoch = epoch;
                    activeToken = desiredOrigin == origin && desiredIdentity == identity ? desiredToken : token;
                    activeSnapshot = snapshot;
                    confirmedOrigin = origin; confirmedIdentity = identity; confirmedKey = key; confirmedRoomId = ack.RoomId;
                    // Measure the server-provided lifetime without relying on the client's wall clock.
                    confirmedUntil = requestStartedAt + (long)(ack.ExpiresAt - ack.AcceptedAt).TotalMilliseconds;
                    heartbeatAt = Environment.TickCount64 + ack.HeartbeatAfterSeconds * 1000L;
                    retryAt = rateLimitedUntil = 0;
                    lastNotice = null;
                    if (desiredVersion != version) continue;
                    running = false;
                    return;
                }
                if (method == HttpMethod.Put && response.Status == HttpStatusCode.NotFound)
                {
                    if (confirmedOrigin == origin && confirmedIdentity == identity && confirmedRoomId == roomId)
                    {
                        confirmedKey = confirmedRoomId = confirmedIdentity = null;
                        confirmedOrigin = null;
                    }
                    activeRoomId = activeKey = activeToken = activeIdentity = activeSnapshot = null;
                    activeOrigin = null;
                    continue;
                }
                // The server removes an account's rooms when sharing is denied for these reasons.
                if (response.Status == HttpStatusCode.Forbidden && confirmedOrigin == origin && confirmedIdentity == identity
                    && reason is "PUID_NOT_REGISTERED" or "USER_BANNED" or "ACCOUNT_BANNED" or "ACCOUNT_REQUIRED" or "SHARING_DISABLED")
                {
                    confirmedKey = confirmedRoomId = confirmedIdentity = null;
                    confirmedOrigin = null;
                }
                if (!deleting && response.Status == HttpStatusCode.Forbidden)
                {
                    forbiddenPending = true;
                    ForbiddenReasons.Enqueue(new(origin, identity, token, reason));
                    if (desiredOrigin == origin && desiredIdentity == identity && desiredToken == token && desiredSnapshot != null)
                    {
                        desiredSnapshot = null;
                        desiredVersion++;
                        desiredEpoch++;
                    }
                    if (activeRoomId != null && (desiredSnapshot == null || activeEpoch != desiredEpoch)) continue;
                    running = false;
                    return;
                }
                if (response.Status == HttpStatusCode.Unauthorized)
                {
                    unauthorizedOrigin = origin;
                    unauthorizedToken = token;
                    if (deleting)
                    {
                        activeRoomId = activeKey = activeToken = activeIdentity = activeSnapshot = null;
                        activeOrigin = null;
                        if (desiredSnapshot != null && (desiredToken != token || desiredOrigin != origin)) continue;
                    }
                }
                else if (deleting && response.Status == HttpStatusCode.Forbidden)
                {
                    activeRoomId = activeKey = activeToken = activeIdentity = activeSnapshot = null;
                    activeOrigin = null;
                    continue;
                }
                // An invalid create ACK can still contain an ID that needs deletion.
                if (!deleting && response.Status != HttpStatusCode.TooManyRequests && activeRoomId != null
                    && (desiredSnapshot == null || activeEpoch != desiredEpoch)) continue;
                long delay = response.Status == HttpStatusCode.TooManyRequests
                    ? Math.Max(1, response.RetryAfterSeconds == 0 ? 15 : response.RetryAfterSeconds) : 30;
                retryAt = Environment.TickCount64 + delay * 1000L;
                if (response.Status == HttpStatusCode.TooManyRequests) rateLimitedUntil = retryAt;
                string notice = response.Status switch
                {
                    HttpStatusCode.Unauthorized => "CloudRoomUnauthorized",
                    HttpStatusCode.TooManyRequests => "CloudRoomRateLimited",
                    _ => "CloudRoomUnavailable"
                };
                if (notice != lastNotice) { Messages.Enqueue(notice); lastNotice = notice; }
                running = false;
                return;
            }
        }
    }
}
