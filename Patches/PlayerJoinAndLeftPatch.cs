using AmongUs.Data;
using AmongUs.GameOptions;
using Hazel;
using InnerNet;
using System;
using System.Text.RegularExpressions;
using TOHE.Modules;
using TOHE.Modules.ChatManager;
using TOHE.Patches;
using TOHE.Roles.Core.AssignManager;
using TOHE.Roles.Crewmate;
using static TOHE.Translator;

namespace TOHE;

[HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
class OnGameJoinedPatch
{
    internal static uint Generation { get; private set; }
    internal static bool IsCurrentSession(uint generation)
        => Generation == generation && AmongUsClient.Instance != null && AmongUsClient.Instance.AmConnected;

    internal static bool IsCurrentClient(ClientData client, uint generation)
    {
        if (client == null || !IsCurrentSession(generation)) return false;
        var current = Utils.GetClientById(client.Id);
        return current != null && current.Pointer == client.Pointer;
    }

    public static void Postfix(AmongUsClient __instance)
    {
        var generation = ++Generation;
        ForeignVersionCheck.Reset();
        if (Options.IsLoaded) InitializeSession(__instance);
        else Main.Instance.StartCoroutine(WaitForOptions(__instance, generation));
    }

    private static System.Collections.IEnumerator WaitForOptions(AmongUsClient client, uint generation)
    {
        var deadline = UnityEngine.Time.realtimeSinceStartup + 10f;
        while (!Options.IsLoaded)
        {
            if (client == null || !client.AmConnected || Generation != generation) yield break;
            if (UnityEngine.Time.realtimeSinceStartup >= deadline)
            {
                Logger.Error("Mod options did not load within 10 seconds", "OnGameJoined");
                client.ExitGame(DisconnectReasons.Error);
                yield break;
            }
            yield return null;
        }
        if (client != null && client.AmConnected && Generation == generation) InitializeSession(client);
    }

    private static void InitializeSession(AmongUsClient __instance)
    {
        OptionItem.SetRemotePreset(!__instance.AmHost);
        var generation = Generation;
        Logger.Info($"{__instance.GameId} Joining room - Room code: {GameCode.IntToGameName(AmongUsClient.Instance.GameId) ?? string.Empty}", "OnGameJoined");

        Main.IsHostVersionCheating = false;
        Main.playerVersion = [];
        SoundManager.Instance.ChangeAmbienceVolume(DataManager.Settings.Audio.AmbienceVolume);

        Main.HostClientId = AmongUsClient.Instance.HostId;
        if (!DebugModeManager.AmDebugger && Main.VersionCheat.Value)
            Main.VersionCheat.Value = false;

        ChatUpdatePatch.DoBlockChat = false;
        GameStates.InGame = false;
        if (ErrorText.Instance != null) ErrorText.Instance.Clear();
        EAC.Init();
        Main.AllClientRealNames.Clear();

        if (AmongUsClient.Instance.AmHost) // Execute the following only on the host
        {
            EndGameManagerPatch.IsRestarting = false;
            if (!RehostManager.IsAutoRehostDone)
            {
                AmongUsClient.Instance.ChangeGamePublic(RehostManager.ShouldPublic);
                RehostManager.IsAutoRehostDone = true;
            }

            Main.HostRealName = DataManager.Player.Customization.Name;
            if (!Main.AllClientRealNames.ContainsKey(__instance.ClientId))
            {
                Main.AllClientRealNames.Add(__instance.ClientId, DataManager.Player.Customization.Name);
            }

            GameStartManagerPatch.GameStartManagerUpdatePatch.exitTimer = -1;
            Main.DoBlockNameChange = false;
            RoleAssign.SetRoles = [];
            RoundAssignment.Reset();
            GhostRoleAssign.forceRole = [];
            EAC.DeNum = new();
            Main.AllPlayerNames.Clear();
            Main.PlayerQuitTimes.Clear();
            KickPlayerPatch.AttemptedKickPlayerList = [];

            switch (GameOptionsManager.Instance.CurrentGameOptions.GameMode)
            {
                case GameModes.NormalFools:
                case GameModes.Normal:
                    Logger.Info(" Is Normal Game", "Game Mode");

                    AURoleOptions.SetOpt(Main.NormalOptions.Cast<IGameOptions>());

                    // if custom game mode is HideNSeekTOHE in normal game, set standart
                    if (Options.CurrentGameMode == CustomGameMode.HidenSeekTOHE)
                    {
                        // Select standart
                        Options.GameMode.SetValue(0);
                    }
                    break;

                case GameModes.SeekFools:
                case GameModes.HideNSeek:
                    Logger.Info(" Is Hide & Seek", "Game Mode");

                    // if custom game mode is Standard/FFA in H&S game, set HideNSeekTOHE
                    if (Options.CurrentGameMode is CustomGameMode.Standard or CustomGameMode.FFA)
                    {
                        // Select HideNSeekTOHE
                        Options.GameMode.SetValue(2);
                    }
                    break;

                case GameModes.None:
                    Logger.Info(" Is None", "Game Mode");
                    break;

                default:
                    Logger.Info(" No found", "Game Mode");
                    break;
            }
        }

        _ = new LateTask(() =>
        {
            if (!IsCurrentSession(generation)) return;
            try
            {
                if (!GameStates.IsOnlineGame) return;
                if (!GameStates.IsModHost)
                    RPC.RpcRequestRetryVersionCheck();
                if (BanManager.CheckEACList(PlayerControl.LocalPlayer.FriendCode, PlayerControl.LocalPlayer.GetClient().GetHashedPuid()) && GameStates.IsOnlineGame)
                {
                    AmongUsClient.Instance.ExitGame(DisconnectReasons.Banned);
                    SceneChanger.ChangeScene("MainMenu");
                    return;
                }

                var client = AmongUsClient.Instance.GetClientFromCharacter(PlayerControl.LocalPlayer);
                var host = AmongUsClient.Instance.GetHost();

                if (!GameStates.IsVanillaServer)
                {
                    RPC.RpcSetFriendCode(EOSManager.Instance.FriendCode);
                }

                Logger.Info($"{client.PlayerName.RemoveHtmlTags()}(ClientID:{client.Id}/FriendCode:{client.FriendCode}/HashPuid:{client.GetHashedPuid()}/Platform:{client.PlatformData.Platform}) finished join room", "Session: OnGameJoined");
                Logger.Info($"{host.PlayerName.RemoveHtmlTags()}(ClientID:{host.Id}/FriendCode:{host.FriendCode}/HashPuid:{host.GetHashedPuid()}/Platform:{host.PlatformData.Platform}) is the host", "Session: OnGameJoined");
            }
            catch (Exception error)
            {
                Logger.Error("Error while trying to log local client data.", "OnGameJoinedPatch");
                Logger.Exception(error, "OnGameJoinedPatch");
                _ = new LateTask(() =>
                {
                    if (!IsCurrentSession(generation)) return;
                    try
                    {
                        if (!GameStates.IsOnlineGame && !GameStates.IsLocalGame) return;
                        var client = AmongUsClient.Instance.GetClientFromCharacter(PlayerControl.LocalPlayer);
                        var host = AmongUsClient.Instance.GetHost();
                        Logger.Info($"{client.PlayerName.RemoveHtmlTags()}(ClientID:{client.Id}/FriendCode:{client.FriendCode}/HashPuid:{client.GetHashedPuid()}/Platform:{client.PlatformData.Platform}) finished join room", "Session: OnGameJoined Retry");
                        Logger.Info($"{host.PlayerName.RemoveHtmlTags()}(ClientID:{host.Id}/FriendCode:{host.FriendCode}/HashPuid:{host.GetHashedPuid()}/Platform:{host.PlatformData.Platform}) is the host", "Session: OnGameJoined Retry");
                    }
                    catch (Exception retryError)
                    {
                        Logger.Exception(retryError, "OnGameJoinedPatch.RetryLogLocalClient");
                    }
                    ;
                }, 1.5f, "Retry Log Local Client");
            }
        }, 0.7f, "OnGameJoinedPatch");
    }
}
[HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.DisconnectInternal))]
class DisconnectInternalPatch
{
    public static void Prefix(InnerNetClient __instance, [HarmonyArgument(0)] DisconnectReasons reason, [HarmonyArgument(1)] string stringReason)
    {
        // ExitGame calls DisconnectInternal directly, without OnDisconnected.
        BanManager.ClearSessionBans();
        if (reason != DisconnectReasons.NewConnection) CloudService.Disconnect();
        Logger.Info($"Disconnect (Reason:{reason}:{stringReason}, ping:{__instance.Ping})", "Reason Disconnect");
        RehostManager.OnDisconnectInternal(reason);
    }
}
[HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerJoined))]
public static class OnPlayerJoinedPatch
{
    public static bool IsDisconnected(this ClientData client)
    {
        var __instance = AmongUsClient.Instance;
        if (client == null || __instance == null) return true;
        for (int i = 0; i < __instance.allClients.Count; i++)
        {
            ClientData clientData = __instance.allClients[i];
            if (clientData.Id == client.Id)
            {
                return false;
            }
        }
        return true;
        //When a client disconnects, it is removed from allClients in method amongusclient.removeplayer
    }
    public static bool HasInvalidFriendCode(string friendcode)
    {
        if (string.IsNullOrEmpty(friendcode))
        {
            return true;
        }

        if (friendcode.Length < 7) // #1234 is 5 chars, and its impossible for a friend code to only have 3
        {
            return true;
        }

        if (friendcode.Count(c => c == '#') != 1)
        {
            return true;
        }

        string pattern = @"[\W\d]";
        if (Regex.IsMatch(friendcode[..friendcode.IndexOf("#")], pattern))
        {
            return true;
        }

        return false;
    }
    public static void Postfix(/*AmongUsClient __instance,*/ [HarmonyArgument(0)] ClientData client)
    {
        if (client == null) return;
        if (AmongUsClient.Instance.AmHost && BanManager.IsSessionBanned(client.ProductUserId))
        {
            AmongUsClient.Instance.KickPlayer(client.Id, true);
            Logger.Info($"Client {client.Id} rejoined with a PUID banned in this room", "BAN");
            return;
        }
        var generation = OnGameJoinedPatch.Generation;
        Logger.Info($"{client.PlayerName}(ClientID:{client.Id}/FriendCode:{client.FriendCode}/HashPuid:{client.GetHashedPuid()}/Platform:{client.PlatformData.Platform}) Joining room", "Session: OnPlayerJoined");

        Main.AssignRolesIsStarted = false;

        _ = new LateTask(() =>
        {
            if (!OnGameJoinedPatch.IsCurrentClient(client, generation) || !AmongUsClient.Instance.AmHost) return;
            try
            {
                if (client.Character != null && client.Character.Data != null && client.Character.Data.IsIncomplete)
                {
                    Logger.SendInGame(GetString("Error.InvalidColor") + $" {client.Id}/{client.PlayerName}");
                    AmongUsClient.Instance.KickPlayer(client.Id, false);
                    Logger.Info($"Kicked client {client.Id}/{client.PlayerName} bcz PlayerControl is not spawned in time.", "OnPlayerJoinedPatchPostfix");
                    return;
                }

                if (AmongUsClient.Instance.AmHost && !Main.playerVersion.TryGetValue(client.Id, out _))
                {
                    var retry = CustomRpcTransport.Start(CustomRPC.RequestRetryVersionCheck, SendOption.Reliable, client.Id);
                    CustomRpcTransport.Finish(retry);
                }
            }
            catch (Exception error)
            {
                Logger.Exception(error, "OnPlayerJoinedPatch.GreenBeanKickLateTask");
            }
        }, 4.5f, "green bean kick late task", false);


        if (AmongUsClient.Instance.AmHost && HasInvalidFriendCode(client.FriendCode) && Options.KickPlayerFriendCodeInvalid.GetBool() && !GameStates.IsLocalGame)
        {
            if (!Options.TempBanPlayerFriendCodeInvalid.GetBool())
            {
                AmongUsClient.Instance.KickPlayer(client.Id, false);
                Logger.SendInGame(string.Format(GetString("Message.KickedByInvalidFriendCode"), client.PlayerName));
                Logger.Info($"Kicked a player {client?.PlayerName} because of invalid friend code", "Kick");
            }
            else
            {
                if (!BanManager.TempBanWhiteList.Contains(client.GetHashedPuid()))
                    BanManager.TempBanWhiteList.Add(client.GetHashedPuid());
                AmongUsClient.Instance.KickPlayer(client.Id, true);
                Logger.SendInGame(string.Format(GetString("Message.TempBannedByInvalidFriendCode"), client.PlayerName));
                Logger.Info($"TempBanned a player {client?.PlayerName} because of invalid friend code", "Temp Ban");
            }
        }

        if (Options.AllowOnlyWhiteList.GetBool() && !BanManager.CheckAllowList(client?.FriendCode) && !GameStates.IsLocalGame)
        {
            AmongUsClient.Instance.KickPlayer(client.Id, false);
            Logger.SendInGame(string.Format(GetString("Message.KickedByWhiteList"), client.PlayerName));
            Logger.Warn($"Kicked player {client?.PlayerName}, because friendcode: {client?.FriendCode} is not in WhiteList.txt", "Kick");
        }

        Platforms platform = client.PlatformData.Platform;
        if (AmongUsClient.Instance.AmHost && Options.KickOtherPlatformPlayer.GetBool() && platform != Platforms.Unknown && !GameStates.IsLocalGame)
        {
            if ((platform == Platforms.Android && Options.OptKickAndroidPlayer.GetBool()) ||
                (platform == Platforms.IPhone && Options.OptKickIphonePlayer.GetBool()) ||
                (platform == Platforms.Xbox && Options.OptKickXboxPlayer.GetBool()) ||
                (platform == Platforms.Playstation && Options.OptKickPlayStationPlayer.GetBool()) ||
                (platform == Platforms.Switch && Options.OptKickNintendoPlayer.GetBool()))
            {
                string msg = string.Format(GetString("MsgKickOtherPlatformPlayer"), client?.PlayerName, platform.ToString());
                AmongUsClient.Instance.KickPlayer(client.Id, false);
                Logger.SendInGame(msg);
                Logger.Info(msg, "Other Platform Kick"); ;
            }
        }
        if (DestroyableSingleton<FriendsListManager>.Instance.IsPlayerBlockedUsername(client.FriendCode) && AmongUsClient.Instance.AmHost)
        {
            AmongUsClient.Instance.KickPlayer(client.Id, true);
            Logger.Info($"Ban Player ー {client?.PlayerName}({client.FriendCode}) has been banned.", "BAN");
        }
        BanManager.CheckBanPlayer(client);

        if (AmongUsClient.Instance.AmHost)
        {
            if (Main.SayStartTimes.ContainsKey(client.Id)) Main.SayStartTimes.Remove(client.Id);
            if (Main.SayBanwordsTimes.ContainsKey(client.Id)) Main.SayBanwordsTimes.Remove(client.Id);

            if (client.GetHashedPuid() != "" && Options.TempBanPlayersWhoKeepQuitting.GetBool()
                && !BanManager.CheckAllowList(client.FriendCode) && !GameStates.IsLocalGame)
            {
                if (Main.PlayerQuitTimes.ContainsKey(client.GetHashedPuid()))
                {
                    if (Main.PlayerQuitTimes[client.GetHashedPuid()] >= Options.QuitTimesTillTempBan.GetInt())
                    {
                        if (!BanManager.TempBanWhiteList.Contains(client.GetHashedPuid()))
                            BanManager.TempBanWhiteList.Add(client.GetHashedPuid());
                        AmongUsClient.Instance.KickPlayer(client.Id, true);
                        Logger.SendInGame(string.Format(GetString("Message.TempBannedForSpamQuitting"), client.PlayerName));
                        Logger.Info($"Temp Ban Player ー {client?.PlayerName}({client.FriendCode}) has been temp banned.", "BAN");
                    }
                }
            }
        }
    }
}
[HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerLeft))]
class OnPlayerLeftPatch
{
    public static bool StartingProcessing = false;
    public static byte LeftPlayerId = byte.MaxValue;
    static void Prefix([HarmonyArgument(0)] ClientData data)
    {
        StartingProcessing = true;
        if (data != null)
        {
            ForeignVersionCheck.Remove(data.Id);
            ChatManager.RemoveRecipient(data.Id);
        }
        LeftPlayerId = data?.Character?.PlayerId ?? byte.MaxValue;

        if (data?.Character == null) return;

        if (data != null && data.Character != null)
            StartGameHostPatch.DataDisconnected[data.Character.PlayerId] = true;

        if (GameStates.IsInGame && Main.PlayerStates.TryGetValue(data.Character.PlayerId, out var leavingState))
        {
            leavingState.Disconnected = true;
        }

        if (!AmongUsClient.Instance.AmHost) return;

        if (Main.AssignRolesIsStarted)
        {
            Logger.Warn($"Assign roles not ended, try remove player {data.Character.PlayerId} from role assign", "OnPlayerLeft");
            RoleAssign.RoleResult?.Remove(data.Character.PlayerId);
            RpcSetRoleReplacer.Senders?.Remove(data.Character.PlayerId);
        }

        if (GameStates.IsNormalGame && GameStates.IsInGame)
            MurderPlayerPatch.AfterPlayerDeathTasks(data?.Character, data?.Character, GameStates.IsMeeting);

        if (AmongUsClient.Instance.AmHost && data.Character != null)
        {
            // Remove messages sending to left player
            for (int i = 0; i < Main.MessagesToSend.Count; i++)
            {
                var (msg, sendTo, title, noReplay) = Main.MessagesToSend[i];
                if (sendTo == data.Character.PlayerId)
                {
                    Main.MessagesToSend.RemoveAt(i);
                    i--;
                }
            }
        }
    }
    public static void Postfix(AmongUsClient __instance, [HarmonyArgument(0)] ClientData data, [HarmonyArgument(1)] DisconnectReasons reason)
    {
        if (data == null)
        {
            StartingProcessing = false;
            return;
        }
        try
        {
            if (GameStates.IsNormalGame && GameStates.IsInGame && data.Character != null
                && Main.PlayerStates.TryGetValue(data.Character.PlayerId, out var state))
            {
                if (data.Character.Is(CustomRoles.Lovers) && !data.Character.Data.IsDead)
                {
                    foreach (var lovers in Main.LoversPlayers.ToArray())
                    {
                        Main.isLoversDead = true;
                        Main.LoversPlayers.Remove(lovers);
                        Main.PlayerStates[lovers.PlayerId].RemoveSubRole(CustomRoles.Lovers);
                    }
                }

                if (Spiritualist.HasEnabled) Spiritualist.RemoveTarget(data.Character.PlayerId);

                state.Disconnected = true;
                state.SetDead();

                // if the player left while he had a Notice message, clear it
                if (NameNotifyManager.Notifying(data.Character))
                {
                    NameNotifyManager.Notice.Remove(data.Character.PlayerId);
                    //Utils.DoNotifyRoles(SpecifyTarget: data.Character, ForceLoop: true);
                }

                if (AmongUsClient.Instance.AmHost)
                {
                    try
                    {
                        data.Character.RpcSetName(state.NormalOutfit.PlayerName);
                    }
                    catch (Exception error)
                    {
                        Logger.Exception(error, "OnPlayerLeftPatch.Postfix.RpcSetName");
                    }
                }

                AntiBlackout.OnDisconnect(data.Character.Data);
                PlayerGameOptionsSender.RemoveSender(data.Character);
            }

            if (Main.HostClientId != data.Id && Main.playerVersion.ContainsKey(data.Id))
                Main.playerVersion.Remove(data.Id);

            if (Main.HostClientId == data.Id && Main.playerVersion.ContainsKey(data.Id))
            {
                var player = PlayerControl.LocalPlayer;
                var title = "<color=#aaaaff>" + GetString("DefaultSystemMessageTitle") + "</color>";
                var name = player?.Data?.PlayerName;
                var msg = "";
                if (GameStates.IsInGame)
                {
                    Utils.ErrorEnd("Host exits the game");
                    msg = GetString("Message.HostLeftGameInGame");
                }
                else if (GameStates.IsLobby)
                    msg = GetString("Message.HostLeftGameInLobby");

                if (!AmongUsClient.Instance.AmHost)
                {
                    player.SetName(title);
                    DestroyableSingleton<HudManager>.Instance.Chat.AddChat(player, msg);
                    player.SetName(name);
                }

                //On Become Host is called before OnPlayerLeft, so this is safe to use
                if (AmongUsClient.Instance.AmHost)
                {
                    ChatManager.SendSystem(player, msg, title: title);
                }
                Main.HostClientId = AmongUsClient.Instance.HostId;
                var generation = OnGameJoinedPatch.Generation;
                //We won;t notify vanilla players for host's quit bcz niko dont know how to prevent message spamming
                _ = new LateTask(() =>
                {
                    if (!OnGameJoinedPatch.IsCurrentSession(generation)) return;
                    if (!GameStates.IsOnlineGame) return;
                    if (Main.playerVersion.ContainsKey(AmongUsClient.Instance.HostId))
                    {
                        if (AmongUsClient.Instance.AmHost)
                            Utils.SendMessage(string.Format(GetString("Message.HostLeftGameNewHostIsMod"), AmongUsClient.Instance.GetHost().Character?.GetRealName() ?? "null"));
                    }
                    else
                    {
                        var player = PlayerControl.LocalPlayer;
                        var title = "<color=#aaaaff>" + GetString("DefaultSystemMessageTitle") + "</color>";
                        var name = player?.Data?.PlayerName;
                        var msg = string.Format(GetString("Message.HostLeftGameNewHostIsNotMod"), AmongUsClient.Instance.GetHost().Character?.GetRealName() ?? "null");
                        player.SetName(title);
                        DestroyableSingleton<HudManager>.Instance.Chat.AddChat(player, msg);
                        player.SetName(name);
                    }
                }, 0.5f, "On Host Disconnected");
            }

            switch (reason)
            {
                case DisconnectReasons.Hacking:
                    Logger.SendInGame(string.Format(GetString("PlayerLeftByAU-Anticheat"), data?.PlayerName));
                    break;
                case DisconnectReasons.Error when __instance.AmHost && GameStates.IsInGame:
                    Logger.SendInGame(string.Format(GetString("PlayerLeftByError"), data?.PlayerName));
                    var gameId = __instance.GameId;
                    _ = new LateTask(() =>
                    {
                        if (__instance == null || !__instance.AmHost || __instance.GameId != gameId
                            || !GameStates.IsInGame || GameManager.Instance == null) return;
                        CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Error);
                        GameManager.Instance.enabled = false;
                        Utils.NotifyGameEnding();
                        GameManager.Instance.RpcEndGame(GameOverReason.ImpostorDisconnect, false);
                    }, 3f, "Disconnect Error Auto-end");

                    break;
            }

            Logger.Info($"{data?.PlayerName} - (ClientID:{data?.Id} / FriendCode:{data?.FriendCode} / HashPuid:{data?.GetHashedPuid()} / Platform:{data?.PlatformData.Platform}) Disconnect (Reason:{reason}，Ping:{AmongUsClient.Instance.Ping})", "Session OnPlayerLeftPatch");

            // End the game when a player exits game during assigning roles (AntiBlackOut Protect)
            if (Main.AssignRolesIsStarted)
            {
                Utils.ErrorEnd("The player left the game during assigning roles");
            }


            if (data != null)
                Main.playerVersion.Remove(data.Id);

            if (AmongUsClient.Instance.AmHost)
            {
                Main.SayStartTimes.Remove(__instance.ClientId);
                Main.SayBanwordsTimes.Remove(__instance.ClientId);

                if (GameStates.IsLobby && !GameStates.IsLocalGame)
                {
                    if (data?.GetHashedPuid() != "" && Options.TempBanPlayersWhoKeepQuitting.GetBool()
                        && !BanManager.CheckAllowList(data?.FriendCode))
                    {
                        if (!Main.PlayerQuitTimes.ContainsKey(data?.GetHashedPuid()))
                            Main.PlayerQuitTimes.Add(data?.GetHashedPuid(), 1);
                        else Main.PlayerQuitTimes[data?.GetHashedPuid()]++;

                        if (Main.PlayerQuitTimes[data?.GetHashedPuid()] >= Options.QuitTimesTillTempBan.GetInt())
                        {
                            BanManager.TempBanWhiteList.Add(data?.GetHashedPuid());
                            //should ban on player's next join game
                        }
                    }
                }

                if (GameStates.IsMeeting && data.Character != null)
                {
                    Swapper.CheckSwapperTarget(data.Character.PlayerId);

                    // Prevent double check end voting
                    if (MeetingHud.Instance.state is MeetingHud.MeetingStates.Discussion or MeetingHud.MeetingStates.NotVoted or MeetingHud.MeetingStates.Voted)
                    {
                        MeetingHud.Instance.CheckForEndVoting();
                    }
                }
            }
        }
        catch (Exception error)
        {
            Logger.Error(error.ToString(), "OnPlayerLeftPatch.Postfix");
            //Logger.SendInGame("Error: " + error.ToString());
        }

        StartingProcessing = false;
    }
}
[HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.Spawn))]
class InnerNetClientSpawnPatch
{
    public static void Postfix([HarmonyArgument(1)] int ownerId, [HarmonyArgument(2)] SpawnFlags flags)
    {
        if (!AmongUsClient.Instance.AmHost || flags != SpawnFlags.IsClientCharacter) return;

        ClientData client = Utils.GetClientById(ownerId);
        var generation = OnGameJoinedPatch.Generation;

        LateTask Schedule(Action action, float delay, string name) => new(() =>
        {
            if (OnGameJoinedPatch.IsCurrentClient(client, generation) && AmongUsClient.Instance.AmHost
                && client.Character != null && PlayerControl.LocalPlayer != null) action();
        }, delay, name);

        if (client == null || client.Character == null // client is null
            || client.ColorId < 0 || Palette.PlayerColors.Length <= client.ColorId) // invalid client color
        {
            Logger.Warn("client is null or client have invalid color", "TrySyncAndSendMessage");
            return;
        }
        else
        {
            Logger.Msg($"Spawn player data: ID {ownerId}: {client.PlayerName}", "InnerNetClientSpawn");
            _ = Schedule(() =>
            {
                if (Main.OverrideWelcomeMsg != "") Utils.SendMessage(Main.OverrideWelcomeMsg, client.Character.PlayerId);
                else TemplateManager.SendTemplate("welcome", client.Character.PlayerId, true);
            }, 3f, "Welcome Message");

            _ = Schedule(() =>
            {
                if (client == null || client.Character == null)
                {
                    Logger.Warn("client is null", "Spawn.RPCRequestRetryVersionCheck");
                    return;
                }

                var sender = CustomRpcTransport.Start(CustomRPC.RequestRetryVersionCheck, SendOption.Reliable, client.Character.OwnerId);
                CustomRpcTransport.Finish(sender);
            }, 3f, "RPC Request Retry Version Check");

            if (GameStates.IsOnlineGame)
            {
                _ = Schedule(() =>
                {
                    if (GameStates.IsLobby && ServerRegion.IsOfficialConnection
                        && client.Character != null && LobbyBehaviour.Instance != null
                        && client.Character.IsNonHostModdedClient())
                    {
                        // Official servers notify vanilla clients themselves;
                        // private servers have no lobby expiry to synchronize.
                        MessageWriter writer = CustomRpcTransport.Start(CustomRPC.SyncLobbyTimer, SendOption.Reliable, client.Id);
                        writer.WritePacked((int)GameStartManagerPatch.timer);
                        CustomRpcTransport.Finish(writer);
                    }
                }, 3.1f, "Sync Official Lobby Timer");
            }

            if (Options.GradientTagsOpt.GetBool())
            {
                _ = Schedule(() =>
                {
                    Utils.SendMessage(GetString("Warning.GradientTags"), client.Character.PlayerId);
                }, 3.3f, "GradientWarning");
            }
        }

        Main.GuessNumber[client.Character.PlayerId] = [-1, 7];

        if (Main.OverrideWelcomeMsg == "" && Main.PlayerStates.Count != 0 && Main.clientIdList.Contains(client.Id))
        {
            if (GameStates.IsNormalGame)
            {
                if (Options.AutoDisplayKillLog.GetBool() && Main.PlayerStates.Count != 0 && Main.clientIdList.Contains(client.Id))
                {
                    _ = Schedule(() =>
                    {
                        if (!AmongUsClient.Instance.IsGameStarted && client.Character != null)
                        {
                            Main.isChatCommand = true;
                            Utils.ShowKillLog(client.Character.PlayerId);
                        }
                    }, 3f, "DisplayKillLog");
                }
                if (Options.AutoDisplayLastRoles.GetBool())
                {
                    _ = Schedule(() =>
                    {
                        if (!AmongUsClient.Instance.IsGameStarted && client.Character != null)
                        {
                            Main.isChatCommand = true;
                            Utils.SendMessage("\n", client.Character.PlayerId, Main.LastSummaryMessage);
                        }
                    }, 3.1f, "DisplayLastRoles");
                }
                if (Options.AutoDisplayLastResult.GetBool())
                {
                    _ = Schedule(() =>
                    {
                        if (!AmongUsClient.Instance.IsGameStarted && client.Character != null)
                        {
                            Main.isChatCommand = true;
                            Utils.ShowLastResult(client.Character.PlayerId);
                        }
                    }, 3.2f, "DisplayLastResult");
                }
                if (PlayerControl.LocalPlayer.FriendCode.GetDevUser().IsUp && Options.EnableUpMode.GetBool())
                {
                    _ = Schedule(() =>
                    {
                        if (!AmongUsClient.Instance.IsGameStarted && client.Character != null)
                        {
                            Main.isChatCommand = true;
                            //     Utils.SendMessage($"{GetString("Message.YTPlanNotice")} {PlayerControl.LocalPlayer.FriendCode.GetDevUser().UpName}", client.Character.PlayerId);
                        }
                    }, 3.3f, "DisplayUpWarnning");
                }
            }
        }
    }
}
