using AmongUs.Data;
using BepInEx.Configuration;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using UnityEngine;

namespace TOHE.Modules;

internal static class CloudService
{
    internal sealed record Response(HttpStatusCode Status, string Body, int RetryAfterSeconds);
    private static readonly HttpClient systemProxyHttp = new(new HttpClientHandler { UseProxy = true }) { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly HttpClient directHttp = new(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(10) };
    private static ConfigEntry<string> endpoint;
    private static ConfigEntry<bool> shareRooms;
    private static ConfigEntry<bool> systemProxy;
    private static volatile bool useSystemProxy = true;
    private static Uri accountOrigin;
    private static string accountToken;
    private static string accountPuid;
    private static float nextTick;
    private static int sessionGame;
    private static int sessionHost = -1;
    private static uint sessionGeneration;
    private static bool privateConfirmed;
    private static bool wasPublic;
    private static int gameVersion;
    private static bool snapshotUnavailable;
    private static bool bindingOpened;
    private static bool bindingLostFocus;
    private static bool bindingReturned;
    private static Il2CppSystem.Action<bool> bindingFocusCallback;

    internal static bool ShareEnabled => shareRooms?.Value == true;
    internal static bool TagsEnabled => CloudTags.Enabled;
    internal static bool UseSystemProxy => useSystemProxy;
    internal static bool CanBind => Origin != null && CloudUserAgent.TryGet(out _) && !CloudAccount.IsLoading
        && CloudAccount.Current is { Bound: false };
    private static bool CanEnableShare => Origin != null && CloudUserAgent.TryGet(out _) && !CloudAccount.IsLoading
        && CloudAccount.Current is { SharingAllowed: true };
    internal static bool CanChangeShare => ShareEnabled || CanEnableShare;
    internal static bool CanConfirmPrivate => ShareEnabled && CanEnableShare;
    internal static bool CanManage => Origin != null;
    internal static string LastMessage { get; private set; } = string.Empty;

    internal static void Initialize(ConfigFile config)
    {
        endpoint = config.Bind("TOHEE Cloud", "Origin", "https://tohee.niko233.top/", "HTTPS origin of the Cloud deployment; blank disables Cloud.");
        shareRooms = config.Bind("TOHEE Cloud", "Share rooms", false, "Room codes become visible to eligible Cloud users and authorized API callers.");
        if (systemProxy != null) systemProxy.SettingChanged -= OnSystemProxyChanged;
        systemProxy = config.Bind("TOHEE Cloud", "Use system proxy", true, "Use the default system proxy for Cloud HTTP requests; disable to connect directly.");
        systemProxy.SettingChanged += OnSystemProxyChanged;
        useSystemProxy = systemProxy.Value;
        CloudUserAgent.Initialize();
        CloudTags.Initialize(config);
    }

    private static void OnSystemProxyChanged(object sender, EventArgs args) => useSystemProxy = systemProxy.Value;

    private static Uri Origin
    {
        get
        {
            if (!Uri.TryCreate(endpoint?.Value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
                || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
                || uri.AbsolutePath != "/") return null;
            return uri;
        }
    }

    private static void ReadIdentity(out string token, out string puid)
    {
        token = puid = null;
        var eos = EOSManager.Instance;
        if (!eos || !eos.HasFinishedLoginFlow() || DataManager.Player?.Account == null
            || DataManager.Player.Account.LoginStatus != EOSManager.AccountLoginStatus.LoggedIn
            || string.IsNullOrWhiteSpace(eos.ProductUserId) || string.IsNullOrWhiteSpace(eos.UserIDToken)) return;
        token = eos.UserIDToken;
        puid = eos.ProductUserId;
    }

    private static void SyncAccount(Uri origin, string token, string puid, float now)
    {
        if (!Equals(accountOrigin, origin) || accountToken != token || accountPuid != puid)
        {
            CloudRooms.Stop();
            accountOrigin = origin;
            accountToken = token;
            accountPuid = puid;
        }

        CloudAccount.Tick(origin, token, puid, ShareEnabled, now, CloudRooms.Pause);
        while (CloudRooms.ForbiddenReasons.TryDequeue(out var forbidden))
        {
            if (!Equals(origin, forbidden.Origin) || puid != forbidden.Identity || token != forbidden.Token)
            {
                CloudRooms.ForbiddenHandled();
                continue;
            }
            CloudAccount.Deny(forbidden.Reason);
            CloudRooms.Stop();
            CloudRooms.ForbiddenHandled();
            Notify(SharingReason(forbidden.Reason));
        }

        if (origin == null || token == null || puid == null || !CloudUserAgent.TryGet(out _)) CloudRooms.Stop();
        else if (CloudAccount.IsLoading) CloudRooms.Pause();
        else if (CloudAccount.Current?.SharingAllowed != true) CloudRooms.Stop();
    }

    internal static void Tick()
    {
        if (bindingReturned)
        {
            bindingReturned = false;
            RefreshAccount(ensureAfterCurrent: true);
        }
        if (bindingOpened)
        {
            if (!Application.isFocused) bindingLostFocus = true;
            else if (bindingLostFocus) FinishBindingReturn();
        }

        float now = Time.realtimeSinceStartup;
        Uri origin = Origin;
        ReadIdentity(out string token, out string puid);
        SyncAccount(origin, token, puid, now);

        var client = AmongUsClient.Instance;
        if (client && sessionHost != -1 && wasPublic && !client.IsGamePublic)
        {
            wasPublic = false;
            privateConfirmed = false;
            CloudRooms.Stop();
        }
        if (now < nextTick) return;
        nextTick = now + 1f;
        while (CloudRooms.Messages.TryDequeue(out string message)) Notify(Translator.GetString(message));
        CloudTags.Tick(origin);
        CloudMenu.Refresh();

        if (!client || !client.AmConnected || !GameStates.IsOnlineGame)
        {
            if (sessionHost != -1) Disconnect();
            return;
        }
        if (sessionHost != client.HostId || sessionGame != client.GameId || sessionGeneration != OnGameJoinedPatch.Generation)
        {
            Disconnect();
            sessionHost = client.HostId;
            sessionGame = client.GameId;
            sessionGeneration = OnGameJoinedPatch.Generation;
            wasPublic = client.IsGamePublic;
            gameVersion = Constants.GetBroadcastVersion();
        }
        if (wasPublic && !client.IsGamePublic) privateConfirmed = false;
        wasPublic = client.IsGamePublic;
        if (!client.AmHost || origin == null || token == null || puid == null || !ShareEnabled
            || CloudAccount.IsLoading || CloudAccount.Current?.SharingAllowed != true || (!client.IsGamePublic && !privateConfirmed))
        {
            if (!client.AmHost || !ShareEnabled || (!client.IsGamePublic && !privateConfirmed)) CloudRooms.Stop();
            return;
        }
        if (!CloudRooms.TrySnapshot(client, gameVersion, CloudAccount.Current.Regions, out string snapshot, out string roomKey))
        {
            CloudRooms.Stop();
            if (!snapshotUnavailable)
            {
                Notify(Translator.GetString("CloudRoomSnapshotUnavailable"));
                snapshotUnavailable = true;
            }
            return;
        }
        snapshotUnavailable = false;
        CloudRooms.Publish(origin, puid, token, roomKey, snapshot);
    }

    internal static void Disconnect()
    {
        CloudRooms.Stop();
        sessionHost = -1;
        privateConfirmed = false;
        wasPublic = false;
        snapshotUnavailable = false;
    }

    internal static void Shutdown()
    {
        bindingOpened = bindingLostFocus = bindingReturned = false;
        RemoveBindingFocusWatch();
        if (systemProxy != null) systemProxy.SettingChanged -= OnSystemProxyChanged;
        Disconnect();
    }

    internal static void RefreshAccount(bool ensureAfterCurrent = false)
    {
        Uri origin = Origin;
        ReadIdentity(out string token, out string puid);
        SyncAccount(origin, token, puid, Time.realtimeSinceStartup);
        if (!CloudAccount.IsLoading || ensureAfterCurrent)
        {
            CloudRooms.Pause();
            CloudAccount.RequestRefresh();
            SyncAccount(origin, token, puid, Time.realtimeSinceStartup);
        }
        CloudMenu.Refresh();
    }

    internal static void SetTagsEnabled(bool enabled)
    {
        CloudTags.SetEnabled(enabled, Origin);
        if (!enabled) Notify(Translator.GetString("CloudTagsDisabled"));
        CloudMenu.Refresh();
    }

    internal static void SetUseSystemProxy(bool enabled)
    {
        systemProxy.Value = enabled;
        CloudMenu.Refresh();
    }

    internal static void RefreshTags() => CloudTags.Refresh(Origin);

    internal static void ManageAccount()
    {
        Uri origin = Origin;
        if (origin == null) Notify(Translator.GetString("CloudOriginDisabled"));
        else OpenBrowser(new Uri(origin, "/accounts").AbsoluteUri);
    }

    private static bool OpenBrowser(string url)
    {
        try
        {
            Application.OpenURL(url);
            return true;
        }
        catch (Exception error)
        {
            Logger.Warn(error.GetType().Name, "Cloud.OpenBrowser");
            Notify(Translator.GetString("CloudBrowserUnavailable"));
            return false;
        }
    }

    private static void RemoveBindingFocusWatch()
    {
        if (bindingFocusCallback == null) return;
        try
        {
            Application.focusChanged -= bindingFocusCallback;
            bindingFocusCallback = null;
        }
        catch (Exception error)
        {
            Logger.Warn(error.GetType().Name, "Cloud.FocusChanged");
        }
    }

    private static void OnBindingFocusChanged(bool focused)
    {
        if (!bindingOpened) return;
        if (!focused) bindingLostFocus = true;
        else if (bindingLostFocus) FinishBindingReturn();
    }

    private static void FinishBindingReturn()
    {
        bindingOpened = false;
        bindingLostFocus = false;
        bindingReturned = true;
        RemoveBindingFocusWatch();
    }

    internal static string AccountStatus()
    {
        if (Origin == null) return Translator.GetString("CloudOriginDisabled");
        if (!CloudUserAgent.TryGet(out string userAgentError)) return Translator.GetString(userAgentError);
        if (accountToken == null || accountPuid == null) return Translator.GetString("CloudLoginRequired");
        if (CloudAccount.IsLoading) return Translator.GetString("CloudAccountLoading");
        var state = CloudAccount.Current;
        if (state == null)
        {
            return Translator.GetString(CloudAccount.Error switch
            {
                "RATE_LIMITED" => "CloudAccountRateLimited",
                "HTTP_401" => "CloudAccountUnauthorized",
                "HTTP_403" => "CloudAccountForbidden",
                "INVALID_RESPONSE" => "CloudAccountInvalidResponse",
                _ => "CloudAccountFailed"
            });
        }

        string account = state.Bound
            ? string.Format(Translator.GetString("CloudAccountBound"), state.FriendCode)
            : Translator.GetString("CloudAccountUnbound");
        string sharing = state.SharingAllowed ? Translator.GetString("CloudSharingAllowed") : SharingReason(state.Reason);
        string tag = string.Empty;
        if (state.TagOverrides is { } overrides && overrides.ValueKind == JsonValueKind.Object)
        {
            if (overrides.TryGetProperty("tag", out var value) && value.ValueKind == JsonValueKind.String)
                tag += "\n" + string.Format(Translator.GetString("CloudAccountTag"), value.GetString());
            if (overrides.TryGetProperty("color", out var color) && color.ValueKind == JsonValueKind.String)
                tag += "\n" + string.Format(Translator.GetString("CloudAccountColor"), color.GetString());
        }
        return account + "\n" + sharing + tag;
    }

    private static string SharingReason(string reason) => Translator.GetString(reason switch
    {
        "PUID_NOT_REGISTERED" => "CloudSharingUnbound",
        "USER_BANNED" => "CloudSharingUserBanned",
        "ACCOUNT_BANNED" => "CloudSharingAccountBanned",
        "ACCOUNT_REQUIRED" or "ACCOUNT_NOT_ELIGIBLE" => "CloudSharingAccountRequired",
        "SHARING_DISABLED" => "CloudSharingDisabled",
        _ => "CloudSharingDenied"
    });

    internal static bool TryHandleCommand(string text)
    {
        var args = (text ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (args.Length == 0 || !args[0].Equals("/cloud", StringComparison.OrdinalIgnoreCase)) return false;
        Uri origin = Origin;
        ReadIdentity(out string token, out string puid);
        SyncAccount(origin, token, puid, Time.realtimeSinceStartup);

        if (args.Length == 3 && args[1].Equals("tags", StringComparison.OrdinalIgnoreCase)
            && args[2].Equals("refresh", StringComparison.OrdinalIgnoreCase))
        {
            RefreshTags();
            return true;
        }
        if (args.Length == 3 && args[1].Equals("account", StringComparison.OrdinalIgnoreCase)
            && args[2].Equals("refresh", StringComparison.OrdinalIgnoreCase))
        {
            RefreshAccount();
            Notify(AccountStatus());
            return true;
        }
        if (args.Length == 2 && args[1].Equals("bind", StringComparison.OrdinalIgnoreCase))
        {
            if (CloudAccount.Current is { Bound: true } && !CloudAccount.IsLoading)
                Notify(Translator.GetString("CloudAlreadyBound"));
            else if (!CanBind) Notify(AccountStatus());
            else if (!CloudBinding.TryCreateLink(origin, token, CloudAccount.Current.BindingPublicKeys, out string url))
                Notify(Translator.GetString("CloudBindingUnavailable"));
            else
            {
                RemoveBindingFocusWatch();
                bindingOpened = true;
                bindingLostFocus = !Application.isFocused;
                bindingReturned = false;
                if (bindingFocusCallback == null)
                {
                    try
                    {
                        // The generated add_focusChanged accessor is stripped; its IL2CPP backing field remains available.
                        bindingFocusCallback = (Action<bool>)OnBindingFocusChanged;
                        Application.focusChanged += bindingFocusCallback;
                    }
                    catch (Exception error)
                    {
                        Logger.Warn(error.GetType().Name, "Cloud.FocusChanged");
                    }
                }
                if (OpenBrowser(url)) Notify(Translator.GetString("CloudBindingOpening"));
                else
                {
                    bindingOpened = false;
                    bindingLostFocus = false;
                    RemoveBindingFocusWatch();
                }
            }
            return true;
        }
        if (args.Length == 3 && args[1].Equals("share", StringComparison.OrdinalIgnoreCase)
            && (args[2].Equals("on", StringComparison.OrdinalIgnoreCase) || args[2].Equals("off", StringComparison.OrdinalIgnoreCase)))
        {
            bool enable = args[2].Equals("on", StringComparison.OrdinalIgnoreCase);
            if (enable && !CanEnableShare) Notify(AccountStatus());
            else
            {
                shareRooms.Value = enable;
                if (!enable) { privateConfirmed = false; CloudRooms.Stop(); }
                Notify(Translator.GetString(enable ? "CloudShareEnabledNotice" : "CloudShareDisabledNotice"));
            }
            CloudMenu.Refresh();
            return true;
        }
        if (args.Length == 3 && args[1].Equals("private", StringComparison.OrdinalIgnoreCase)
            && args[2].Equals("confirm", StringComparison.OrdinalIgnoreCase))
        {
            var client = AmongUsClient.Instance;
            if (CanConfirmPrivate && client && client.AmConnected && client.AmHost && GameStates.IsOnlineGame && !client.IsGamePublic
                && sessionHost == client.HostId && sessionGame == client.GameId && sessionGeneration == OnGameJoinedPatch.Generation)
            {
                privateConfirmed = true;
                wasPublic = false;
                Notify(Translator.GetString("CloudPrivateConfirmed"));
            }
            else Notify(Translator.GetString("CloudPrivateUnavailable"));
            return true;
        }
        Notify(Translator.GetString(ShareEnabled ? "CloudCommandHelpOn" : "CloudCommandHelpOff"));
        return true;
    }

    internal static void Notify(string message)
    {
        LastMessage = message;
        CloudMenu.UpdateStatus(message);
        if (HudManager.Instance) HudManager.Instance.Notifier.AddDisconnectMessage(message);
    }

    internal static async Task<Response> Send(Uri origin, HttpMethod method, string path, string token, string json = null)
    {
        HttpClient client = useSystemProxy ? systemProxyHttp : directHttp;
        if (!CloudUserAgent.TryGet(out string userAgent)) return new(0, string.Empty, 0);
        try
        {
            using var request = new HttpRequestMessage(method, new Uri(origin, path));
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            if (!string.IsNullOrEmpty(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (json != null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var retryHeader = response.Headers.RetryAfter;
            double delay = retryHeader?.Delta?.TotalSeconds ??
                (retryHeader?.Date is { } date ? (date - DateTimeOffset.UtcNow).TotalSeconds : 0);
            int retry = (int)Math.Clamp(Math.Ceiling(delay), 0, int.MaxValue);
            return new(response.StatusCode, body, retry);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            return new(0, string.Empty, 0);
        }
    }
}
