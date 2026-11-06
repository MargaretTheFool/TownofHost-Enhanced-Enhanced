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
#if ANDROID
using System.Collections;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine.Networking;
#endif

namespace TOHE.Modules;

internal static class CloudService
{
    internal sealed record Response(HttpStatusCode Status, string Body, int RetryAfterSeconds);
    internal readonly record struct MenuState(bool UserAgentReady, string UserAgent, bool HasIdentity,
        bool AccountLoading, bool AccountRequestInFlight, CloudAccount.State Account, string AccountError, bool TagsLoading,
        bool OnlineRoom, bool CurrentRoom, bool IsHost, CloudRooms.SharingStatus Room,
        bool RoomManuallyStopped, bool ShareEnabled, bool TagsEnabled, bool UseSystemProxy, bool LobbyButtonEnabled);
#if ANDROID
    private sealed record PendingRequest(string Url, string Method, string Token, string Json, string UserAgent,
        TaskCompletionSource<Response> Completion);
    private static readonly ConcurrentQueue<PendingRequest> pendingRequests = new();
    private static CancellationTokenSource requestShutdown = new();
#else
    private static readonly HttpClient systemProxyHttp = new(new HttpClientHandler { UseProxy = true }) { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly HttpClient directHttp = new(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(10) };
#endif
    private static readonly Uri Origin = new("https://tohee.niko233.top/");
    private static ConfigEntry<bool> shareRooms;
    private static ConfigEntry<bool> systemProxy;
    private static ConfigEntry<bool> showLobbyButton;
    private static volatile bool useSystemProxy = true;
    private static Uri accountOrigin;
    private static string accountToken;
    private static string accountPuid;
    private static CloudAccount.State displayedAccount;
    private static float nextTick;
    private static int sessionGame;
    private static int sessionHost = -1;
    private static uint sessionGeneration;
    private static uint roomGeneration;
    private static uint endedGeneration;
    private static bool privateConfirmed;
    private static bool roomManuallyStopped;
    private static string sessionRoomKey;
    private static bool wasPublic;
    private static bool roundEnded;
    private static int gameVersion;
    private static bool snapshotUnavailable;
    private static bool bindingOpened;
    private static bool bindingLostFocus;
    private static bool bindingReturned;
    private static Il2CppSystem.Action<bool> bindingFocusCallback;

    internal static bool ShareEnabled => shareRooms?.Value == true;
    internal static bool TagsEnabled => CloudTags.Enabled;
    internal static bool UseSystemProxy => useSystemProxy;
    internal static bool LobbyButtonEnabled => showLobbyButton?.Value != false;
    private static bool IsCurrentRoom
    {
        get
        {
            var client = AmongUsClient.Instance;
            return client && client.AmConnected && GameStates.IsOnlineGame
                && sessionHost == client.HostId && sessionGame == client.GameId && sessionGeneration == OnGameJoinedPatch.Generation;
        }
    }
    private static CloudRooms.SharingStatus RoomStatus => CloudRooms.GetStatus(IsCurrentRoom ? sessionRoomKey : null);
    private static bool RoomRequestInFlight => CloudRooms.GetStatus(null).IsRequestInFlight;
    private static bool IsBusy => CloudAccount.IsRequestInFlight || CloudTags.IsLoading || RoomRequestInFlight;
    internal static bool CanBind => !IsBusy && !CloudAccount.IsLoading && CloudUserAgent.TryGet(out _)
        && CloudAccount.Current is { Bound: false };
    private static bool CanEnableShare => CloudUserAgent.TryGet(out _) && !CloudAccount.IsLoading
        && CloudAccount.Current is { SharingAllowed: true };
    internal static bool CanRefreshAccount => !IsBusy && !CloudAccount.IsLoading;
    internal static bool CanChangeShare => !IsBusy && (ShareEnabled || CanEnableShare);
    internal static bool CanChangeTags => !IsBusy;
    internal static bool CanChangeProxy => !IsBusy;
    internal static bool RoomIsShared => RoomStatus.IsShared;
    internal static bool ShowLobbyButton => LobbyButtonEnabled && GameStates.IsLobby && GameStates.IsOnlineGame
        && CloudAccount.DisplayState is { Bound: true, SharingEnabled: true };
    internal static bool CanChangeRoomShare => IsCurrentRoom && AmongUsClient.Instance.AmHost
        && !IsBusy && (RoomIsShared || (ShareEnabled && CanEnableShare));
    internal static bool CanManage => !IsBusy;
    internal static string LastMessage { get; private set; } = string.Empty;

    internal static string MainMenuAccountLabel
    {
        get
        {
            var account = CloudAccount.Current;
            string key;
            if (!CloudUserAgent.TryGet(out _) || accountToken == null || accountPuid == null)
                key = "CloudMainMenuUnavailable";
            else if (CloudAccount.IsLoading)
                key = "CloudMainMenuLoading";
            else if (account == null)
                key = "CloudMainMenuError";
            else
                key = account.Bound ? "CloudMainMenuBound" : "CloudMainMenuUnbound";
            return Translator.GetString(key);
        }
    }

    internal static MenuState CaptureMenuState()
    {
        bool userAgentReady = CloudUserAgent.TryGet(out string userAgent);
        var client = AmongUsClient.Instance;
        bool onlineRoom = client && client.AmConnected && GameStates.IsOnlineGame;
        bool currentRoom = onlineRoom && sessionHost == client.HostId && sessionGame == client.GameId
            && sessionGeneration == OnGameJoinedPatch.Generation;
        var room = CloudRooms.GetStatus(currentRoom ? sessionRoomKey : null);
        return new(userAgentReady, userAgent, accountToken != null && accountPuid != null,
            CloudAccount.IsLoading, CloudAccount.IsRequestInFlight, CloudAccount.Current, CloudAccount.Error, CloudTags.IsLoading,
            onlineRoom, currentRoom, onlineRoom && client.AmHost, room, roomManuallyStopped, ShareEnabled, TagsEnabled, UseSystemProxy,
            LobbyButtonEnabled);
    }

    internal static void Initialize(ConfigFile config)
    {
#if ANDROID
        if (requestShutdown.IsCancellationRequested) requestShutdown = new();
#endif
        shareRooms = config.Bind("TOHEE Cloud", "Share rooms", false, "Room codes become visible to eligible Cloud users and authorized API callers.");
        showLobbyButton = config.Bind("TOHEE Cloud", "Show lobby button", true, "Show the Cloud button in online lobbies.");
        if (systemProxy != null) systemProxy.SettingChanged -= OnSystemProxyChanged;
#if ANDROID
        systemProxy = config.Bind("TOHEE Cloud", "Use system proxy", true, "This setting is unavailable on Android; Cloud uses the device's network settings.");
#else
        systemProxy = config.Bind("TOHEE Cloud", "Use system proxy", true, "Use the default system proxy for Cloud HTTP requests; disable to connect directly.");
#endif
        systemProxy.SettingChanged += OnSystemProxyChanged;
        useSystemProxy = systemProxy.Value;
        CloudUserAgent.Initialize();
        CloudTags.Initialize(config);
    }

    private static void OnSystemProxyChanged(object sender, EventArgs args) => useSystemProxy = systemProxy.Value;

    internal static void SetLobbyButtonEnabled(bool enabled)
    {
        showLobbyButton.Value = enabled;
        CloudMenu.Refresh();
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

        if (token == null || puid == null || !CloudUserAgent.TryGet(out _)) CloudRooms.Stop();
        else if (CloudAccount.IsLoading) CloudRooms.Pause();
        else if (CloudAccount.Current?.SharingAllowed != true) CloudRooms.Stop();

        var displayState = CloudAccount.DisplayState;
        if (!ReferenceEquals(displayedAccount, displayState))
        {
            displayedAccount = displayState;
            CloudTags.RefreshHostName();
        }
    }

    internal static void Tick()
    {
#if ANDROID
        while (pendingRequests.TryDequeue(out var request))
        {
            if (request.Completion.Task.IsCompleted) continue;
            try { Main.Instance.StartCoroutine(SendUnityRequest(request)); }
            catch (Exception error)
            {
                Logger.Warn(error.GetType().Name, "Cloud.Send");
                request.Completion.TrySetResult(new(0, string.Empty, 0));
            }
        }
#endif
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
        // Room requests run on a worker; refresh every frame so the controls lock
        // as soon as a request starts and unlock as soon as it finishes.
        CloudMenu.Refresh();
        if (now < nextTick) return;
        nextTick = now + 1f;
        while (CloudRooms.Messages.TryDequeue(out string message)) Notify(Translator.GetString(message));
        CloudTags.Tick(origin);

        if (!client || !client.AmConnected || !GameStates.IsOnlineGame)
        {
            if (sessionHost != -1) Disconnect();
            return;
        }
        // JoinGame temporarily clears ClientId when returning from the end screen.
        if (roundEnded && sessionGame == client.GameId && client.IsGameOver && client.ClientId < 0) return;
        if (roundEnded && sessionGame == client.GameId && !client.IsGameOver && client.AmHost)
        {
            sessionHost = client.HostId;
            sessionGeneration = OnGameJoinedPatch.Generation;
        }
        if (sessionHost != client.HostId || sessionGame != client.GameId || sessionGeneration != OnGameJoinedPatch.Generation)
        {
            bool sameRoom = sessionHost != -1 && sessionGame == client.GameId
                && sessionGeneration == OnGameJoinedPatch.Generation;
            bool stopped = roomManuallyStopped;
            string previousRoomKey = sessionRoomKey;
            Disconnect();
            if (sameRoom)
            {
                roomManuallyStopped = stopped;
                sessionRoomKey = previousRoomKey;
            }
            sessionHost = client.HostId;
            sessionGame = client.GameId;
            sessionGeneration = OnGameJoinedPatch.Generation;
            roomGeneration = sessionGeneration;
            wasPublic = client.IsGamePublic;
            gameVersion = Constants.GetBroadcastVersion();
        }
        if (client.IsGameOver && client.AmHost && !roundEnded)
        {
            roundEnded = true;
            endedGeneration = sessionGeneration;
        }
        if (roundEnded && !client.IsGameOver && sessionGeneration != endedGeneration
            && PlayerControl.LocalPlayer && PlayerControl.LocalPlayer.Data != null) roundEnded = false;
        if (wasPublic && !client.IsGamePublic) privateConfirmed = false;
        wasPublic = client.IsGamePublic;
        if (!client.AmHost || token == null || puid == null || !ShareEnabled
            || roomManuallyStopped || CloudAccount.IsLoading || CloudAccount.Current?.SharingAllowed != true
            || (!client.IsGamePublic && !privateConfirmed))
        {
            if (!client.AmHost || !ShareEnabled || roomManuallyStopped || (!client.IsGamePublic && !privateConfirmed)) CloudRooms.Stop();
            return;
        }
        if (!CloudRooms.TrySnapshot(client, gameVersion, roomGeneration, CloudAccount.Current.Regions,
                out string snapshot, out string roomKey))
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
        sessionRoomKey = roomKey;
        CloudRooms.Publish(origin, puid, token, roomKey, snapshot);
    }

    internal static void Disconnect()
    {
        CloudRooms.Stop();
        sessionHost = -1;
        privateConfirmed = false;
        roomManuallyStopped = false;
        sessionRoomKey = null;
        wasPublic = false;
        roundEnded = false;
        snapshotUnavailable = false;
    }

    internal static void Shutdown()
    {
        bindingOpened = bindingLostFocus = bindingReturned = false;
        RemoveBindingFocusWatch();
        if (systemProxy != null) systemProxy.SettingChanged -= OnSystemProxyChanged;
        Disconnect();
#if ANDROID
        requestShutdown.Cancel();
        while (pendingRequests.TryDequeue(out _)) { }
#endif
    }

    internal static void RefreshAccount(bool ensureAfterCurrent = false)
    {
        if (!ensureAfterCurrent && !CanRefreshAccount) return;
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
        if (!CanChangeTags)
        {
            Notify(Translator.GetString("CloudRequestInProgress"));
            return;
        }
        CloudTags.SetEnabled(enabled, Origin);
        if (!enabled) Notify(Translator.GetString("CloudTagsDisabled"));
        CloudMenu.Refresh();
    }

    internal static void SetUseSystemProxy(bool enabled)
    {
        if (!CanChangeProxy)
        {
            Notify(Translator.GetString("CloudRequestInProgress"));
            return;
        }
        systemProxy.Value = enabled;
        CloudMenu.Refresh();
    }

    internal static void RefreshTags()
    {
        if (!CanChangeTags) Notify(Translator.GetString("CloudRequestInProgress"));
        else CloudTags.Refresh(Origin);
        CloudMenu.Refresh();
    }

    internal static void ToggleRoomShare() => RequestRoomSharing(RoomIsShared);

    private static void RequestRoomSharing(bool stop)
    {
        if (IsBusy)
        {
            Notify(Translator.GetString("CloudRequestInProgress"));
            return;
        }
        var client = AmongUsClient.Instance;
        if (!IsCurrentRoom || !client.AmHost || (!stop && (!ShareEnabled || !CanEnableShare)))
        {
            Notify(Translator.GetString("CloudRoomControlUnavailable"));
            return;
        }

        if (stop)
        {
            roomManuallyStopped = true;
            privateConfirmed = false;
            CloudRooms.Stop();
            Notify(Translator.GetString("CloudRoomStopRequested"));
        }
        else
        {
            roomManuallyStopped = false;
            if (!client.IsGamePublic)
            {
                if (wasPublic) CloudRooms.Stop();
                wasPublic = false;
                privateConfirmed = true;
            }
            Notify(Translator.GetString("CloudRoomShareRequested"));
            nextTick = 0f;
            Tick();
        }
        CloudMenu.Refresh();
    }

    internal static void ManageAccount()
    {
        if (IsBusy) Notify(Translator.GetString("CloudRequestInProgress"));
        else OpenBrowser(new Uri(Origin, "/accounts").AbsoluteUri);
    }

    private static bool OpenBrowser(string url)
    {
        try
        {
#if ANDROID
            Constants.OpenURL(url);
#else
            Application.OpenURL(url);
#endif
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

    internal static string RoomStatusText(bool compact = false)
    {
        var client = AmongUsClient.Instance;
        if (!client || !client.AmConnected || !GameStates.IsOnlineGame)
            return Translator.GetString("CloudRoomStatusNoRoom");

        var state = RoomStatus;
        return Translator.GetString((compact ? "CloudLobbyStatus" : "CloudRoomStatus") + (state.Operation switch
        {
            CloudRooms.RequestOperation.Create => "Sharing",
            CloudRooms.RequestOperation.Update when state.IsShared => "Updating",
            CloudRooms.RequestOperation.Update => "Sharing",
            CloudRooms.RequestOperation.Delete => "Stopping",
            _ when state.IsShared && roomManuallyStopped => "StopPending",
            _ => state.IsShared ? "Shared" : "NotShared"
        }));
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
            if (!CanRefreshAccount) Notify(Translator.GetString("CloudRequestInProgress"));
            else
            {
                RefreshAccount();
                Notify(AccountStatus());
            }
            return true;
        }
        if (args.Length == 2 && args[1].Equals("bind", StringComparison.OrdinalIgnoreCase))
        {
            if (IsBusy)
                Notify(Translator.GetString("CloudRequestInProgress"));
            else if (CloudAccount.Current is { Bound: true })
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
            if (IsBusy) Notify(Translator.GetString("CloudRequestInProgress"));
            else if (enable && !CanEnableShare) Notify(AccountStatus());
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
            if (IsBusy) Notify(Translator.GetString("CloudRequestInProgress"));
            else if (client && !client.IsGamePublic) RequestRoomSharing(false);
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
        var hud = DestroyableSingleton<HudManager>._instance;
        if (hud && hud.Notifier) hud.Notifier.AddDisconnectMessage(message);
    }

    internal static async Task<Response> Send(Uri origin, HttpMethod method, string path, string token, string json = null)
    {
        if (!CloudUserAgent.TryGet(out string userAgent)) return new(0, string.Empty, 0);
#if ANDROID
        var completion = new TaskCompletionSource<Response>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(requestShutdown.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var registration = timeout.Token.Register(() => completion.TrySetResult(new(0, string.Empty, 0)));
        pendingRequests.Enqueue(new(new Uri(origin, path).AbsoluteUri, method.Method, token, json, userAgent, completion));
        return await completion.Task.ConfigureAwait(false);
#else
        HttpClient client = useSystemProxy ? systemProxyHttp : directHttp;
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
#endif
    }

#if ANDROID
    private static IEnumerator SendUnityRequest(PendingRequest pending)
    {
        UnityWebRequest request = null;
        UnityWebRequestAsyncOperation operation;
        try
        {
            request = new UnityWebRequest(pending.Url, pending.Method)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 10
            };
            request.SetRequestHeader("User-Agent", pending.UserAgent);
            if (!string.IsNullOrEmpty(pending.Token)) request.SetRequestHeader("Authorization", "Bearer " + pending.Token);
            if (pending.Json != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(pending.Json));
                request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
            }
            operation = request.SendWebRequest();
        }
        catch (Exception error)
        {
            request?.Dispose();
            Logger.Warn(error.GetType().Name, "Cloud.Send");
            pending.Completion.TrySetResult(new(0, string.Empty, 0));
            yield break;
        }

        try
        {
            while (!operation.isDone && !pending.Completion.Task.IsCompleted) yield return null;
            if (pending.Completion.Task.IsCompleted)
            {
                request.Abort();
                yield break;
            }

            try
            {
                if (request.result is not (UnityWebRequest.Result.Success or UnityWebRequest.Result.ProtocolError))
                {
                    pending.Completion.TrySetResult(new(0, string.Empty, 0));
                    yield break;
                }
                RetryConditionHeaderValue.TryParse(request.GetResponseHeader("Retry-After"), out var retryHeader);
                double delay = retryHeader?.Delta?.TotalSeconds ??
                    (retryHeader?.Date is { } date ? (date - DateTimeOffset.UtcNow).TotalSeconds : 0);
                int retry = (int)Math.Clamp(Math.Ceiling(delay), 0, int.MaxValue);
                pending.Completion.TrySetResult(new((HttpStatusCode)request.responseCode, request.downloadHandler.text ?? string.Empty, retry));
            }
            catch (Exception error)
            {
                Logger.Warn(error.GetType().Name, "Cloud.Send");
                pending.Completion.TrySetResult(new(0, string.Empty, 0));
            }
        }
        finally { request.Dispose(); }
    }
#endif
}
