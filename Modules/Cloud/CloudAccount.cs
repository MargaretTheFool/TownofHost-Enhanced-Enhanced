using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace TOHE.Modules;

// Tick and all published state belong to the Unity thread. The worker only
// handles HTTP and creates immutable response values.
internal static class CloudAccount
{
    internal sealed record Region(string Id, string Name, string Url, string Kind);
    internal sealed record BindingPublicKey(string Kid, string Kty, string N, string E);
    internal sealed record State(bool Bound, string FriendCode, bool SharingAllowed, bool SharingEnabled,
        string Reason, IReadOnlyList<Region> Regions, IReadOnlyList<BindingPublicKey> BindingPublicKeys,
        JsonElement? TagOverrides);

    private sealed record Result(State State, string Error, int RetryAfterSeconds);

    private static Uri currentOrigin;
    private static string currentToken;
    private static string currentPuid;
    private static State current;
    private static Task<Result> pending;
    private static int pendingVersion;
    private static int stateVersion;
    private static bool refreshRequested;
    private static bool authenticationRejected;
    private static float nextPoll;
    private static float retryAfter;
    private static float lastNow;

    internal static bool IsLoading => pending != null || refreshRequested;
    internal static State Current => IsLoading || Error != null ? null : current;
    // These are diagnostic codes, not display strings.
    internal static string Error { get; private set; }

    internal static void Tick(Uri origin, string token, string puid, bool shareIntent, float now, Action beforeQuery = null)
    {
        lastNow = now;
        if (origin == null || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(puid))
        {
            token = null;
            puid = null;
        }

        if (!Equals(origin, currentOrigin) || token != currentToken || puid != currentPuid)
        {
            bool sameOrigin = Equals(origin, currentOrigin);
            currentOrigin = origin;
            currentToken = token;
            currentPuid = puid;
            current = null;
            Error = null;
            pending = null;
            stateVersion++;
            refreshRequested = origin != null && token != null;
            authenticationRejected = false;
            nextPoll = 0;
            if (!sameOrigin) retryAfter = 0;
        }

        if (pending is { IsCompleted: true })
        {
            var result = pending.IsCompletedSuccessfully ? pending.Result : new Result(null, "NETWORK_ERROR", 0);
            pending = null;
            if (result.RetryAfterSeconds > 0)
                retryAfter = Math.Max(retryAfter, now + result.RetryAfterSeconds);
            if (pendingVersion == stateVersion)
            {
                current = result.State;
                Error = result.Error;
                if (result.Error == "HTTP_401")
                {
                    // An invalid token must not be retried by the periodic poll.
                    authenticationRejected = true;
                    refreshRequested = false;
                }
                else if (result.State != null) authenticationRejected = false;
            }
            nextPoll = now + 30f;
        }

        if (currentOrigin == null || currentToken == null) return;
        bool pollDue = shareIntent && !authenticationRejected && now >= nextPoll;
        if ((!refreshRequested && !pollDue) || pending != null || now < retryAfter) return;

        beforeQuery?.Invoke();
        refreshRequested = false;
        Error = null;
        pendingVersion = stateVersion;
        var requestOrigin = currentOrigin;
        var requestToken = currentToken;
        var requestPuid = currentPuid;
        pending = Task.Run(() => Query(requestOrigin, requestToken, requestPuid));
    }

    internal static void RequestRefresh()
    {
        if (currentOrigin == null || currentToken == null) return;
        // A query already in flight may predate a completed browser binding.
        // Coalesce repeated refreshes into one follow-up and ignore its result.
        if (pending != null && !refreshRequested) stateVersion++;
        refreshRequested = true;
    }

    internal static void Deny(string reason)
    {
        if (current == null) return;
        reason = string.IsNullOrWhiteSpace(reason) ? "FORBIDDEN" : reason;
        current = current with
        {
            SharingAllowed = false,
            SharingEnabled = reason == "SHARING_DISABLED" ? false : current.SharingEnabled,
            Reason = reason
        };
        stateVersion++;
        nextPoll = lastNow + 30f;
    }

    private static async Task<Result> Query(Uri origin, string token, string puid)
    {
        var response = await CloudService.Send(origin, HttpMethod.Get, "/api/mod/me", token).ConfigureAwait(false);
        if (response.Status != HttpStatusCode.OK)
        {
            if ((int)response.Status == 429)
                return new(null, "RATE_LIMITED", response.RetryAfterSeconds > 0 ? response.RetryAfterSeconds : 30);
            return new(null, (int)response.Status == 0 ? "NETWORK_ERROR" : $"HTTP_{(int)response.Status}", 0);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Body);
            var root = document.RootElement;
            bool bound = root.GetProperty("bound").GetBoolean();
            var account = root.GetProperty("account");
            var tags = root.GetProperty("tags");
            string friendCode = null;
            JsonElement? overrides = null;
            if (bound)
            {
                if (account.GetProperty("puid").GetString() != puid) return new(null, "INVALID_RESPONSE", 0);
                friendCode = account.GetProperty("friend_code").GetString();
                if (string.IsNullOrWhiteSpace(friendCode)) return new(null, "INVALID_RESPONSE", 0);
                var fields = tags.GetProperty("overrides");
                if (fields.ValueKind != JsonValueKind.Object) return new(null, "INVALID_RESPONSE", 0);
                overrides = fields.Clone();
            }
            else if (account.ValueKind != JsonValueKind.Null || tags.ValueKind != JsonValueKind.Null)
                return new(null, "INVALID_RESPONSE", 0);

            var sharing = root.GetProperty("sharing");
            bool enabled = sharing.GetProperty("enabled").GetBoolean();
            bool allowed = sharing.GetProperty("allowed").GetBoolean();
            var reasonValue = sharing.GetProperty("reason");
            string reason = reasonValue.ValueKind == JsonValueKind.Null ? null : reasonValue.GetString();
            if (allowed && (!bound || !enabled || reason != null)) return new(null, "INVALID_RESPONSE", 0);

            var config = root.GetProperty("config");
            var regions = new List<Region>();
            foreach (var region in config.GetProperty("regions").EnumerateArray())
            {
                string id = region.GetProperty("id").GetString();
                string name = region.GetProperty("name").GetString();
                string url = region.GetProperty("url").GetString();
                string kind = region.GetProperty("kind").GetString();
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)
                    || string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(kind))
                    return new(null, "INVALID_RESPONSE", 0);
                regions.Add(new(id, name, url, kind));
            }

            var keys = new List<BindingPublicKey>();
            foreach (var key in config.GetProperty("binding_public_keys").GetProperty("keys").EnumerateArray())
            {
                string kid = key.GetProperty("kid").GetString();
                string kty = key.GetProperty("kty").GetString();
                string n = key.GetProperty("n").GetString();
                string e = key.GetProperty("e").GetString();
                if (string.IsNullOrWhiteSpace(kid) || string.IsNullOrWhiteSpace(kty)
                    || string.IsNullOrWhiteSpace(n) || string.IsNullOrWhiteSpace(e))
                    return new(null, "INVALID_RESPONSE", 0);
                keys.Add(new(kid, kty, n, e));
            }

            return new(new State(bound, friendCode, allowed, enabled, reason, regions.AsReadOnly(), keys.AsReadOnly(), overrides), null, 0);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException)
        {
            return new(null, "INVALID_RESPONSE", 0);
        }
    }
}
