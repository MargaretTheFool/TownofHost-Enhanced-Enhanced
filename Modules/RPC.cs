using AmongUs.GameOptions;
using Hazel;
using InnerNet;
using System;
using TOHE.Modules;
using TOHE.Modules.ChatManager;
using TOHE.Patches;
using TOHE.Roles.AddOns.Crewmate;
using TOHE.Roles.AddOns.Impostor;
using TOHE.Roles.Core;
using TOHE.Roles.Crewmate;
using TOHE.Roles.Impostor;
using TOHE.Roles.Neutral;
using static TOHE.Translator;

namespace TOHE;

enum CustomRPC : uint
{
    // 内层编号使用固定 uint32；外层使用 byte 123 或聚合 byte 124。
    // 新增 CustomRPC 直接在本枚举末尾 Append，不在中间插入或复用编号。
    // 不提供旧协议兼容分支；同一房间的模组客户端必须更新到相同版本。
    VersionCheck = 80,
    RequestRetryVersionCheck = 81,
    SyncCustomSettings = 100, // AUM use 101 rpc
    SetDeathReason = 102,
    EndGame,
    PlaySound,
    SetCustomRole,

    // TOHE
    AntiBlackout,
    SetRealKiller,
    PlayCustomSound,
    SetKillTimer,
    SyncAllPlayerNames,
    SyncNameNotify,
    ShowPopUp,
    KillFlash,
    DumpLog,
    SyncRoleSkill,
    SetNameColorData,
    GuessKill,
    Judge,
    Guess,
    CouncillorJudge,
    NemesisRevenge,
    RetributionistRevenge,
    SetFriendCode,
    SyncLobbyTimer,
    SyncPlayerSetting,
    ShowChat,
    SyncShieldPersonDiedFirst,
    RemoveSubRole,
    SyncGeneralOptions,
    SyncSpeedPlayer,
    Arrow,
    NotificationPopper,

    //Roles 
    SetBountyTarget,
    SyncPuppet,
    SyncKami,
    SetKillOrSpell,
    SetKillOrHex,
    SetKillOrCurse,
    SetDousedPlayer,
    DoSpell,
    DoHex,
    DoCurse,
    SniperSync,
    SetLoversPlayers,
    SendFireworkerState,
    SetCurrentDousingTarget,
    SetEvilTrackerTarget,
    SetDrawPlayer,
    SetCrewpostorTasksDone,

    // BetterAmongUs (BAU) RPC, This is sent to allow other BAU users know who's using BAU!
    BetterCheck = 150,

    SetCurrentDrawTarget,
    RpcPassBomb,
    SyncRomanticTarget,
    SyncVengefulRomanticTarget,
    SetJailerTarget,
    SetJailerExeLimit,
    SetInspectorLimit,
    KeeperRPC,
    SetAlchemistTimer,
    UndertakerLocationSync,
    RiftMakerSyncData,
    LightningSetGhostPlayer,
    SetDarkHiderKillCount,
    SetConsigliere,
    SetGreedy,
    BenefactorRPC,
    SetSwapperVotes,
    SetMarkedPlayer,
    SetConcealerTimer,
    PresidentEnd,
    PresidentReveal,
    SetBKTimer,
    SetCursedSoulCurseLimit,
    SetInvestgatorLimit,
    SetOverseerRevealedPlayer,
    SetOverseerTimer,
    SyncVultureBodyAmount,
    SpyRedNameSync,
    SpyRedNameRemove,
    SetChameleonTimer,
    SyncAdmiredList,
    SyncAdmiredAbility,
    SetImitateLimit,
    //FFA
    SyncFFAPlayer,
    SyncFFANameNotify,
    ClearPelicanOwner,
    ClearShroudOwner,
    MeetingAbilityRequest = 188,
    // 189 was the removed presentation-only recovery RPC; do not reuse it.
    SyncExileText = 190,
    SyncAfkState,
    ProtocolCapabilities,
    Fragment,
    // 194 was the removed SetNativeRole RPC; do not reuse it.
    SystemChat = 195,
    AntiBlackoutUnavailable,
    ReplayPlayerChat,
    DoubleAgentPlant,
    AntiBlackoutBuffer,
    SyncWorkhorseTasks,
    RequestOptionChunks,
}
public enum Sounds
{
    KillSound,
    TaskComplete,
    TaskUpdateSound,
    ImpTransform,
    SabotageSound,

    Test,
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.ShouldProcessRpc))]
class ShouldProcessRpcPatch
{
    /*
     * Sinse stupid AU code added check process rpc for outfit players, so need patch this
     * Always return true because the check is absolutely pointless
     */
    public static bool Prefix(PlayerControl __instance, RpcCalls rpc, byte sequenceId, ref bool __result)
    {
        if (rpc is RpcCalls.SetSkinStr)
            Logger.Info($"Player Id: {__instance.PlayerId} - Old skin sequenceId {__instance.Data.DefaultOutfit.SkinSequenceId} - New skin sequenceId {sequenceId}", "ShouldProcessRpc");

        __result = true;
        return false;
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.HandleRpc))]
internal class RPCHandlerPatch
{
    public static bool TrustedRpc(uint id)
    => (CustomRPC)id is CustomRPC.VersionCheck
        or CustomRPC.RequestRetryVersionCheck
        or CustomRPC.RequestOptionChunks
        or CustomRPC.ProtocolCapabilities
        or CustomRPC.AntiBlackout
        or CustomRPC.Judge
        or CustomRPC.CouncillorJudge
        or CustomRPC.NemesisRevenge
        or CustomRPC.RetributionistRevenge
        or CustomRPC.Guess
        or CustomRPC.PresidentEnd
        or CustomRPC.SetSwapperVotes
        or CustomRPC.DumpLog
        or CustomRPC.SetFriendCode
        or CustomRPC.BetterCheck
        or CustomRPC.MeetingAbilityRequest
        or CustomRPC.DoubleAgentPlant;
    public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] byte callId, [HarmonyArgument(1)] MessageReader reader)
    {
        if (callId == 102)
        {
            ForeignVersionCheck.ReceiveEhrVersion(__instance, reader);
            return false;
        }
        if (callId == 203)
        {
            ForeignVersionCheck.ReceiveLotusRpc(__instance, reader);
            return false;
        }
        if (callId is RpcPayloadSnapshot.OuterCallId or RpcPayloadSnapshot.PackedOuterCallId)
        {
            CustomRpcReceiver.Receive(__instance, reader, callId == RpcPayloadSnapshot.PackedOuterCallId);
            return false;
        }
        // Only the outer envelope enters custom dispatch. Native IDs stay byte.
        if (!Enum.IsDefined(typeof(RpcCalls), callId)) return false;
        if (EAC.PlayerControlReceiveRpc(__instance, callId, reader)) return false;
        MessageReader subReader = MessageReader.Get(reader);
        try
        {
            return ValidateRpc(__instance, callId, subReader);
        }
        finally
        {
            subReader.Recycle();
        }
    }
    private static bool ValidateRpc(PlayerControl __instance, byte callId, MessageReader subReader)
    {
        var rpcType = (RpcCalls)callId;
        Logger.Info($"{__instance?.Data?.PlayerId}({(__instance.IsHost() ? "Host" : __instance?.Data?.PlayerName)}):{callId}({RPC.GetRpcName(callId)})", "ReceiveRPC");
        switch (rpcType)
        {
            case RpcCalls.SetName: //SetNameRPC
                subReader.ReadUInt32();
                string name = subReader.ReadString();
                if (subReader.BytesRemaining != 0) return false;
                Logger.Info("RPC Set Name For Player: " + __instance.GetNameWithRole() + " => " + name, "SetName");
                break;
            case RpcCalls.SetRole: //SetRoleRPC
                var role = (RoleTypes)subReader.ReadUInt16();
                var canOverriddenRole = subReader.ReadBoolean();
                Logger.Info("RPC Set Role For Player: " + __instance.GetRealName() + " => " + role + " CanOverrideRole: " + canOverriddenRole, "SetRole");
                break;
            case RpcCalls.SendChat: // Free chat
                var text = subReader.ReadString();
                Logger.Info($"{__instance.GetNameWithRole().RemoveHtmlTags()}:{text.RemoveHtmlTags()}", "ReceiveChat");
                ChatCommands.OnReceiveChat(__instance, text, out var canceled);
                if (canceled) return false;
                if (AmongUsClient.Instance.AmHost && !__instance.AmOwner)
                    ChatManager.RecordPlayerChat(__instance, text,
                        textHasNamePadding: __instance.IsModded() &&
                            (!OfficialAnticheatPolicy.Enabled || text.StartsWith("\n")));
                break;
            case RpcCalls.SendQuickChat:
                Logger.Info($"{__instance.GetNameWithRole().RemoveHtmlTags()}:Some message from quick chat", "ReceiveChat");
                var quickPayload = new byte[subReader.BytesRemaining];
                for (int i = 0; i < quickPayload.Length; i++) quickPayload[i] = subReader.ReadByte();
                if (!OfficialAnticheatPolicy.Enabled)
                {
                    ChatCommands.OnReceiveChat(__instance, "Some message from quick chat", out var canceledQuickChat);
                    if (canceledQuickChat) return false;
                }
                if (AmongUsClient.Instance.AmHost && !__instance.AmOwner) ChatManager.RecordQuickChat(__instance, quickPayload);
                break;
            case RpcCalls.StartMeeting:
                var p = Utils.GetPlayerById(subReader.ReadByte());
                Logger.Info($"{__instance.GetNameWithRole()} => {p?.GetNameWithRole() ?? "null"}", "StartMeeting");
                break;
        }
        return true;
    }
    internal static void DispatchCustomRpc(PlayerControl __instance, uint callId, MessageReader reader, bool isLocalRecipient)
    {
        var rpcType = (CustomRPC)callId;
        switch (rpcType)
        {
            case CustomRPC.ProtocolCapabilities:
                RpcCompatibility.ReceiveCapabilities(__instance, reader);
                break;
            case CustomRPC.SyncExileText:
                if (isLocalRecipient && __instance.OwnerId == AmongUsClient.Instance.HostId)
                    ExileText.Receive(__instance, reader);
                break;
            case CustomRPC.AntiBlackoutUnavailable:
                if (isLocalRecipient && __instance.OwnerId == AmongUsClient.Instance.HostId)
                    ErrorText.Instance?.ShowAntiBlackoutWarning();
                break;
            case CustomRPC.AntiBlackout:
                Logger.Fatal($"{__instance?.Data?.PlayerName}({__instance.PlayerId}): Error: {reader.ReadString()} - end the game according to the setting", "Anti-black");

                if (GameStates.IsShip || !GameStates.IsLobby || GameStates.IsCoStartGame)
                {
                    //CoStartGame is running, we are fucked.
                    ChatUpdatePatch.DoBlockChat = true;
                    Main.OverrideWelcomeMsg = string.Format(GetString("RpcAntiBlackOutNotifyInLobby"), __instance?.Data?.PlayerName, GetString("EndWhenPlayerBug"));

                    if (Options.EndWhenPlayerBug.GetBool())
                    {
                        _ = new LateTask(() =>
                        {
                            Logger.SendInGame(string.Format(GetString("RpcAntiBlackOutEndGame"), __instance?.Data?.PlayerName));
                        }, 3f, "RPC Anti-Black Msg SendInGame Error During Loading");

                        if (AmongUsClient.Instance.AmHost)
                        {
                            if (GameStates.IsInGame && !GameStates.IsCoStartGame)
                            {
                                CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Error);
                                GameManager.Instance.LogicFlow.CheckEndCriteria();
                                RPC.ForceEndGame(CustomWinner.Error);
                            }
                            else
                            {
                                _ = new LateTask(() =>
                                {
                                    CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Error);
                                    GameManager.Instance.LogicFlow.CheckEndCriteria();
                                    RPC.ForceEndGame(CustomWinner.Error);
                                }, 5.5f, "RPC Anti-Black End Game As Critical Error");
                            }
                        }
                    }
                    else
                    {
                        _ = new LateTask(() =>
                        {
                            Logger.SendInGame(string.Format(GetString("RpcAntiBlackOutIgnored"), __instance?.Data?.PlayerName));
                        }, 3f, "RPC Anti-Black Msg SendInGame Out Ignored");

                        if (AmongUsClient.Instance.AmHost && __instance != null)
                        {
                            if (GameStates.IsInGame && !GameStates.IsCoStartGame)
                            {
                                AmongUsClient.Instance.KickPlayer(__instance.GetClientId(), false);
                                Logger.SendInGame(string.Format(GetString("RpcAntiBlackOutKicked"), __instance?.Data?.PlayerName));
                            }
                            else
                            {
                                _ = new LateTask(() =>
                                {
                                    AmongUsClient.Instance.KickPlayer(__instance.GetClientId(), false);
                                    Logger.SendInGame(string.Format(GetString("RpcAntiBlackOutKicked"), __instance?.Data?.PlayerName));
                                }, 5.5f, "RPC Anti-Black Kicked As Critical Error");
                            }

                            ChatUpdatePatch.DoBlockChat = false;
                        }
                    }
                }
                else if (GameStartManager.Instance != null)
                {
                    // We imagine rpc is received when starting game in lobby, not fucked yet
                    if (AmongUsClient.Instance.AmHost)
                    {
                        GameStartManager.Instance.ResetStartState();
                        if (__instance != null)
                        {
                            AmongUsClient.Instance.KickPlayer(__instance.GetClientId(), false);
                        }
                    }
                    Logger.SendInGame(string.Format(GetString("RpcAntiBlackOutKicked"), __instance?.Data?.PlayerName));
                }
                else
                {
                    Logger.SendInGame("[Critical Error] Your client is in a unknow state while receiving AntiBlackOut rpcs from others.");
                    Logger.Fatal($"Client is in a unknow state while receiving AntiBlackOut rpcs from others.", "Anti-black");
                }
                break;

            case CustomRPC.VersionCheck:
                var verifiedClientId = -1;
                try
                {
                    Version version = Version.Parse(reader.ReadString());
                    string tag = reader.ReadString();
                    string forkId = reader.ReadString();
                    bool cheating = reader.ReadBoolean();
                    var joiningClientId = __instance.GetClientId();
                    if (joiningClientId < 0)
                        break;

                    bool hadVersion = Main.playerVersion.ContainsKey(joiningClientId);
                    bool wasVersionMatched = hadVersion && IsVersionMatch(joiningClientId);
                    if (!hadVersion)
                    {
                        RPC.RpcVersionCheck();
                    }

                    Main.playerVersion[joiningClientId] = new PlayerVersion(version, tag, forkId);
                    if (!hadVersion && AmongUsClient.Instance.AmHost && GameStates.IsLobby)
                        PlayerIdentifiers.RefreshLobby(seer: __instance);

                    if (Main.VersionCheat.Value && joiningClientId == AmongUsClient.Instance.HostId) RPC.RpcVersionCheck();

                    if (joiningClientId == Main.HostClientId && cheating)
                        Main.IsHostVersionCheating = true;

                    if (Main.VersionCheat.Value && AmongUsClient.Instance.AmHost)
                        Main.playerVersion[joiningClientId] = Main.playerVersion[AmongUsClient.Instance.HostId];

                    // Kick Unmached Player Start
                    if (AmongUsClient.Instance.AmHost)
                    {
                        if (!IsVersionMatch(joiningClientId))
                            ScheduleVersionMismatchKick(__instance);
                        else if (!wasVersionMatched)
                            verifiedClientId = joiningClientId;
                    }
                    // Kick Unmached Player End
                }
                catch
                {
                    Logger.Warn($"{__instance?.Data?.PlayerName}({__instance.PlayerId}): バージョン情報が無効です", "RpcVersionCheck");
                    var retryClientId = __instance.GetClientId();
                    var targetClient = __instance.GetClient();
                    var generation = OnGameJoinedPatch.Generation;
                    if (retryClientId < 0) break;
                    _ = new LateTask(() =>
                    {
                        if (!OnGameJoinedPatch.IsCurrentClient(targetClient, generation) || PlayerControl.LocalPlayer == null) return;
                        MessageWriter writer = CustomRpcTransport.Start(CustomRPC.RequestRetryVersionCheck, SendOption.Reliable, retryClientId);
                        CustomRpcTransport.Finish(writer);
                    }, 1f, "Retry Version Check Task");
                }
                if (verifiedClientId >= 0) OptionItem.SyncAllOptions(verifiedClientId);
                break;

            case CustomRPC.RequestRetryVersionCheck:
                RPC.RpcVersionCheck();
                break;

            case CustomRPC.SetFriendCode:
                RPC.SetFriendCode(__instance, reader.ReadString());
                break;

            case CustomRPC.SyncCustomSettings:
                if (isLocalRecipient) OptionSync.Receive(reader);
                break;

            case CustomRPC.RequestOptionChunks:
                if (isLocalRecipient && AmongUsClient.Instance.AmHost && __instance.OwnerId != AmongUsClient.Instance.HostId &&
                    Main.playerVersion.ContainsKey(__instance.OwnerId) && IsVersionMatch(__instance.OwnerId))
                    OptionSync.ReceiveRequest(__instance, reader);
                break;

            case CustomRPC.RemoveSubRole:
                byte targetId = reader.ReadByte();
                var Subrole = (CustomRoles)reader.ReadPackedInt32();
                Main.PlayerStates[targetId].RemoveSubRole(Subrole);
                break;

            case CustomRPC.SetDeathReason:
                RPC.GetDeathReason(reader);
                break;
            case CustomRPC.EndGame:
                RPC.EndGame(reader);
                break;
            case CustomRPC.PlaySound:
                byte playerID = reader.ReadByte();
                Sounds sound = (Sounds)reader.ReadByte();
                if (isLocalRecipient) RPC.PlaySound(playerID, sound);
                break;
            case CustomRPC.ShowPopUp:
                string message = reader.ReadString();
                string title = reader.ReadString();

                // add title
                if (title != "")
                    message = $"{title}\n{message}";

                if (isLocalRecipient) HudManager.Instance.ShowPopUp(message);
                break;
            case CustomRPC.SetCustomRole:
                byte CustomRoleTargetId = reader.ReadByte();
                CustomRoles role = (CustomRoles)reader.ReadPackedInt32();
                if (reader.BytesRemaining == 0)
                    RPC.SetCustomRole(CustomRoleTargetId, role);
                else if (isLocalRecipient)
                    // Startup views confirm previously synchronized metadata;
                    // repeating OnAdd here would reset skills at each phase.
                    RoleDistribution.ReceiveNativeRole(__instance, reader, CustomRoleTargetId, role);
                break;
            case CustomRPC.SyncLobbyTimer:
                GameStartManagerPatch.timer = reader.ReadPackedInt32();
                break;
            case CustomRPC.SyncRoleSkill:
                RPC.SyncRoleSkillReader(reader);
                break;
            case CustomRPC.SyncWorkhorseTasks:
                Workhorse.ReceiveRPC(reader);
                break;
            case CustomRPC.ClearPelicanOwner:
                Pelican.ReceiveOwnerClear(reader, __instance);
                break;
            case CustomRPC.ClearShroudOwner:
                Shroud.ReceiveOwnerClear(reader, __instance);
                break;
            case CustomRPC.Arrow:
                {
                    if (!isLocalRecipient) break;
                    if (reader.ReadBoolean()) TargetArrow.ReceiveRPC(reader);
                    else LocateArrow.ReceiveRPC(reader);
                }
                break;
            case CustomRPC.NotificationPopper:
                {
                    var item = reader.ReadPackedInt32();
                    var playSound = reader.ReadBoolean();

                    var key = OptionItem.AllOptions[item];

                    if (isLocalRecipient) NotificationPopperPatch.AddSettingsChangeMessage(item, key, playSound);
                }
                break;
            case CustomRPC.SetBountyTarget:
                BountyHunter.ReceiveRPC(reader);
                break;
            case CustomRPC.SyncPuppet:
                Puppeteer.ReceiveRPC(reader);
                break;
            case CustomRPC.SetKillOrSpell:
                Witch.ReceiveRPC(reader, false);
                break;
            case CustomRPC.SetKillOrHex:
                HexMaster.ReceiveRPC(reader, false);
                break;
            case CustomRPC.ShowChat:
                var clientId = reader.ReadPackedUInt32();
                var show = reader.ReadBoolean();
                if (isLocalRecipient && AmongUsClient.Instance.ClientId == clientId)
                {
                    HudManager.Instance.Chat.SetVisible(show);
                }
                break;
            case CustomRPC.SystemChat:
                byte speakerId = reader.ReadByte();
                string heading = reader.ReadString();
                string body = reader.ReadString();
                if (reader.BytesRemaining != 0) throw new System.IO.InvalidDataException("Invalid system chat payload");
                if (isLocalRecipient) OfficialChat.ShowLocal(Utils.GetPlayerById(speakerId) ?? __instance, body, heading);
                break;
            case CustomRPC.ReplayPlayerChat:
                bool quickChat = reader.ReadBoolean();
                byte replaySpeakerId = reader.ReadByte();
                string replayTitle = reader.ReadString();
                bool textHasNamePadding = reader.ReadBoolean();
                string replayText;
                if (quickChat)
                {
                    int length = reader.ReadPackedInt32();
                    if (length < 0 || length > reader.BytesRemaining) throw new System.IO.InvalidDataException("Invalid quick chat replay length");
                    var payload = new byte[length];
                    for (int i = 0; i < length; i++) payload[i] = reader.ReadByte();
                    int namesCount = reader.ReadPackedInt32();
                    if (namesCount < 0 || namesCount > reader.BytesRemaining / 2) throw new System.IO.InvalidDataException("Invalid quick chat name count");
                    var names = new ChatViewHistory.ReferencedName[namesCount];
                    for (int i = 0; i < namesCount; i++) names[i] = new(reader.ReadByte(), reader.ReadString());
                    replayText = ChatManager.QuickChatText(payload, names);
                }
                else replayText = reader.ReadString();
                if (reader.BytesRemaining != 0) throw new System.IO.InvalidDataException("Invalid player chat replay payload");
                var replaySpeaker = Utils.GetPlayerById(replaySpeakerId);
                if (isLocalRecipient && replaySpeaker?.Data != null) OfficialChat.ShowLocal(replaySpeaker, replayText,
                    replayTitle, censor: !quickChat, textHasNamePadding: textHasNamePadding, allowDeadSpeaker: true);
                break;
            case CustomRPC.SetDrawPlayer:
                Revolutionist.ReceiveDrawPlayerRPC(reader);
                break;
            case CustomRPC.SetOverseerRevealedPlayer:
                Overseer.ReceiveSetRevealedPlayerRPC(reader);
                break;
            case CustomRPC.SetOverseerTimer:
                Overseer.ReceiveTimerRPC(reader);
                break;
            case CustomRPC.SetNameColorData:
                NameColorManager.ReceiveRPC(reader);
                break;
            case CustomRPC.DoSpell:
                Witch.ReceiveRPC(reader, true);
                break;
            case CustomRPC.DoHex:
                HexMaster.ReceiveRPC(reader, true);
                break;
            case CustomRPC.SniperSync:
                Sniper.ReceiveRPC(reader);
                break;
            case CustomRPC.UndertakerLocationSync:
                Undertaker.ReceiveRPC(reader);
                break;
            case CustomRPC.RiftMakerSyncData:
                RiftMaker.ReceiveRPC(reader);
                break;
            case CustomRPC.SetLoversPlayers:
                Main.LoversPlayers.Clear();
                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                    Main.LoversPlayers.Add(Utils.GetPlayerById(reader.ReadByte()));
                break;
            case CustomRPC.BetterCheck: // Better Among Us RPC
                {
                    var SetBetterUser = reader.ReadBoolean(); // Used to set player as better user, boolean is used for a future for BAU later on.
                    var IsBetterHost = reader.ReadBoolean(); // Used to set the player as better host, this should never be flagged for a TOHE lobby, if it is it's a spoofed RPC
                    var Signature = reader.ReadString(); // Used to verify that the RPC isn't spoofed, only possible in BAU mod due to a special signature that can't really be replicated easily
                    var Version = reader.ReadString(); // Used to read players BAU version

                    if (IsBetterHost)
                    {
                        EAC.Report(__instance, "BetterCheck set as BetterHost");
                        EAC.HandleCheat(__instance, "BetterCheck set as BetterHost");
                        break;
                    }

                    if (string.IsNullOrEmpty(Signature) || string.IsNullOrEmpty(Version))
                    {
                        EAC.Report(__instance, "BetterCheck invalid info");
                        EAC.HandleCheat(__instance, "BetterCheck invalid info");
                        break;
                    }

                    Main.BAUPlayers[__instance.Data] = __instance.Data.Puid;
                }
                break;
            case CustomRPC.SendFireworkerState:
                Fireworker.ReceiveRPC(reader);
                break;
            case CustomRPC.SetCurrentDousingTarget:
                Arsonist.ReceiveCurrentDousingTargetRPC(reader);
                break;
            case CustomRPC.SetDousedPlayer:
                Arsonist.ReceiveSetDousedPlayerRPC(reader);
                break;
            case CustomRPC.SetCurrentDrawTarget:
                Revolutionist.ReceiveSetCurrentDrawTarget(reader);
                break;
            case CustomRPC.SetEvilTrackerTarget:
                EvilTracker.ReceiveRPC(reader);
                break;
            case CustomRPC.SetRealKiller:
                byte tarid = reader.ReadByte();
                byte killerId = reader.ReadByte();
                RPC.SetRealKiller(tarid, killerId);
                break;
            //case CustomRPC.SetTrackerTarget:
            //    Tracker.ReceiveRPC(reader);
            //    break;
            case CustomRPC.SetJailerExeLimit:
                Jailer.ReceiveRPC(reader, setTarget: false);
                break;
            case CustomRPC.SetJailerTarget:
                Jailer.ReceiveRPC(reader, setTarget: true);
                break;
            case CustomRPC.SetCrewpostorTasksDone:
                Crewpostor.ReceiveRPC(reader);
                break;
            case CustomRPC.SyncAdmiredList:
                Admirer.ReceiveRPC(reader, true);
                break;
            case CustomRPC.SyncAdmiredAbility:
                Admirer.ReceiveRPC(reader, false);
                break;
            case CustomRPC.PlayCustomSound:
                if (isLocalRecipient) CustomSoundsManager.ReceiveRPC(reader);
                break;
            case CustomRPC.LightningSetGhostPlayer:
                Lightning.ReceiveRPC(reader);
                break;
            case CustomRPC.SetGreedy:
                Greedy.ReceiveRPC(reader);
                break;
            case CustomRPC.BenefactorRPC:
                Benefactor.ReceiveRPC(reader);
                break;
            case CustomRPC.GuessKill:
                GuessManager.RpcClientGuess(Utils.GetPlayerById(reader.ReadByte()));
                break;
            case CustomRPC.SetMarkedPlayer:
                Ninja.ReceiveRPC(reader);
                break;
            case CustomRPC.SyncGeneralOptions:
                byte paciefID = reader.ReadByte();
                //playerstate:
                {
                    CustomRoles rola = (CustomRoles)reader.ReadPackedInt32();
                    bool isdead = reader.ReadBoolean();
                    bool IsDC = reader.ReadBoolean();
                    PlayerState.DeathReason drip = (PlayerState.DeathReason)reader.ReadPackedInt32();
                    if (Main.PlayerStates.ContainsKey(paciefID))
                    {
                        var state = Main.PlayerStates[paciefID];
                        state.MainRole = rola;
                        state.IsDead = isdead;
                        state.Disconnected = IsDC;
                        state.deathReason = drip;
                    }
                }
                float Killcd = reader.ReadSingle();
                float speed = reader.ReadSingle();

                Main.AllPlayerKillCooldown[paciefID] = Killcd;
                Main.AllPlayerSpeed[paciefID] = speed;
                break;
            case CustomRPC.SyncSpeedPlayer:
                byte readerPlayerId = reader.ReadByte();
                float newSpeed = reader.ReadSingle();
                Main.AllPlayerSpeed[readerPlayerId] = newSpeed;
                break;
            case CustomRPC.SyncPlayerSetting:
                byte playerid = reader.ReadByte();
                CustomRoles rl = (CustomRoles)reader.ReadPackedInt32();
                if (Main.PlayerStates.ContainsKey(playerid))
                {
                    Main.PlayerStates[playerid].MainRole = rl;
                }
                break;

            case CustomRPC.SetKillTimer:
                float time = reader.ReadSingle();
                if (isLocalRecipient) PlayerControl.LocalPlayer.SetKillTimer(time);
                break;
            case CustomRPC.SyncFFAPlayer:
                FFAManager.ReceiveRPCSyncFFAPlayer(reader);
                break;
            case CustomRPC.SyncAllPlayerNames:
                Main.AllPlayerNames.Clear();
                Main.AllClientRealNames.Clear();
                int num = reader.ReadPackedInt32();
                for (int i = 0; i < num; i++)
                    Main.AllPlayerNames.TryAdd(reader.ReadByte(), reader.ReadString());
                int num2 = reader.ReadPackedInt32();
                for (int i = 0; i < num2; i++)
                    Main.AllClientRealNames.TryAdd(reader.ReadInt32(), reader.ReadString());
                break;
            case CustomRPC.SyncFFANameNotify:
                FFAManager.ReceiveRPCSyncNameNotify(reader);
                break;
            case CustomRPC.AntiBlackoutBuffer:
                AntiBlackout.ReceiveBuffer(reader);
                break;
            case CustomRPC.SyncNameNotify:
                NameNotifyManager.ReceiveRPC(reader);
                break;
            case CustomRPC.Judge:
                Judge.ReceiveRPC_Custom(reader, __instance);
                break;
            case CustomRPC.PresidentEnd:
                President.ReceiveRPC(reader, __instance);
                break;
            case CustomRPC.PresidentReveal:
                President.ReceiveRPC(reader, __instance, isEnd: false);
                break;
            case CustomRPC.CouncillorJudge:
                Councillor.ReceiveRPC_Custom(reader, __instance);
                break;
            case CustomRPC.Guess:
                GuessManager.ReceiveRPC(reader, __instance);
                break;
            case CustomRPC.NemesisRevenge:
                Nemesis.ReceiveRPC_Custom(reader, __instance);
                break;
            case CustomRPC.RetributionistRevenge:
                Retributionist.ReceiveRPC_Custom(reader, __instance);
                break;
            case CustomRPC.DoubleAgentPlant:
                byte bombTarget = reader.ReadByte();
                if (isLocalRecipient && reader.BytesRemaining == 0)
                    DoubleAgent.ReceivePlantRequest(__instance, bombTarget);
                break;
            case CustomRPC.SetChameleonTimer:
                Chameleon.ReceiveRPC_Custom(reader);
                break;
            case CustomRPC.SetAlchemistTimer:
                Alchemist.ReceiveRPC(reader);
                break;
            case CustomRPC.SetCursedSoulCurseLimit:
                CursedSoul.ReceiveRPC(reader);
                break;
            case CustomRPC.SetConsigliere:
                Consigliere.ReceiveRPC(reader);
                break;
            case CustomRPC.SetInvestgatorLimit:
                Investigator.ReceiveRPC(reader);
                break;
            case CustomRPC.KillFlash:
                var playKillSound = reader.ReadBoolean();
                if (isLocalRecipient)
                {
                    Utils.FlashColor(new(1f, 0f, 0f, 0.3f));
                    if (Constants.ShouldPlaySfx()) RPC.PlaySound(PlayerControl.LocalPlayer.PlayerId, playKillSound ? Sounds.KillSound : Sounds.SabotageSound);
                }
                break;
            case CustomRPC.DumpLog:
                var target = Utils.GetPlayerById(reader.ReadByte());
                if (target != null && !target.FriendCode.GetDevUser().DeBug)
                {
                    Logger.Info($"Player {target.GetNameWithRole()} used /dump", "RPC_DumpLogger");
                }
                break;
            case CustomRPC.SyncVultureBodyAmount:
                Vulture.ReceiveBodyRPC(reader);
                break;
            case CustomRPC.SpyRedNameSync:
                Spy.ReceiveRPC(reader);
                break;
            case CustomRPC.SpyRedNameRemove:
                Spy.ReceiveRPC(reader, isRemove: true);
                break;
            //case CustomRPC.SetCleanserCleanLimit:
            //    Cleanser.ReceiveRPC(reader);
            //    break;
            case CustomRPC.SetInspectorLimit:
                Inspector.ReceiveRPC(reader);
                break;
            case CustomRPC.KeeperRPC:
                Keeper.ReceiveRPC(reader);
                break;
            case CustomRPC.SetSwapperVotes:
                Swapper.ReceiveSwapRPC(reader, __instance);
                break;
            case CustomRPC.SyncShieldPersonDiedFirst:
                Main.FirstDied = reader.ReadString();
                Main.FirstDiedPrevious = reader.ReadString();
                break;
            case CustomRPC.MeetingAbilityRequest:
                MeetingAbilities.ReceiveRequest(__instance, reader);
                break;
            case CustomRPC.SyncAfkState:
                AfkMonitor.ReceiveState(__instance, reader);
                break;
        }
    }

    internal static void ScheduleVersionMismatchKick(PlayerControl player)
    {
        var generation = OnGameJoinedPatch.Generation;
        var targetClient = player.GetClient();
        _ = new LateTask(() =>
        {
            if (!OnGameJoinedPatch.IsCurrentClient(targetClient, generation) || !AmongUsClient.Instance.AmHost) return;
            if (player?.Data?.Disconnected is not null and not true)
            {
                var msg = string.Format(GetString("KickBecauseDiffrentVersionOrMod"), player.Data.PlayerName);
                Logger.Warn(msg, "Version Kick");
                Logger.SendInGame(msg);
                AmongUsClient.Instance.KickPlayer(targetClient.Id, false);
            }
        }, 5f, "Kick Because Diffrent Version Or Mod");
    }

    private static bool IsVersionMatch(int ClientId)
    {
        if (Main.VersionCheat.Value) return true;
        Version version = Main.playerVersion[ClientId].version;
        string tag = Main.playerVersion[ClientId].tag;
        string forkId = Main.playerVersion[ClientId].forkId;

        if (version != Main.version
            || tag != $"{ThisAssembly.Git.Commit}({ThisAssembly.Git.Branch})"
            || forkId != Main.ForkId)
            return false;

        return true;
    }
}

[HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.HandleRpc))]
internal class PlayerPhysicsRPCHandlerPatch
{
    private static bool HasVent(int ventId) => ShipStatus.Instance.AllVents.Any(v => v.Id == ventId);
    private static bool HasLadder(int ladderId) => ShipStatus.Instance.Ladders.Any(l => l.Id == ladderId);

    public static bool Prefix(PlayerPhysics __instance, byte callId, MessageReader reader)
    {
        //var rpcType = (RpcCalls)callId;
        //MessageReader subReader = MessageReader.Get(reader);

        if (EAC.PlayerPhysicsRpcCheck(__instance, callId, reader)) return false;

        var player = __instance.myPlayer;

        if (!player)
        {
            Logger.Warn("Received Physics RPC without a player", "PlayerPhysics_ReceiveRPC");
            return false;
        }

        if (!Main.MeetingIsStarted)
        {
            __instance.myPlayer.walkingToVent = true;
            VentSystemDeterioratePatch.ForceUpdate = true;
        }

        Logger.Info($"{player.PlayerId}({(__instance.IsHost() ? "Host" : player.Data.PlayerName)}):{callId}({RPC.GetRpcName(callId)})", "PlayerPhysics_ReceiveRPC");
        return true;
    }
}

internal static class RPC
{
    public static void SyncCustomSettingsRPC(int targetId = -1) => OptionSync.SendAll(targetId);

    public static void SyncCustomSettingRPC(OptionItem option) => OptionSync.SendOne(option);

    public static void PlaySoundRPC(byte PlayerID, Sounds sound)
    {
        if (AmongUsClient.Instance.AmHost)
            PlaySound(PlayerID, sound);
        MessageWriter writer = CustomRpcTransport.Start(CustomRPC.PlaySound, SendOption.Reliable, -1);
        writer.Write(PlayerID);
        writer.Write((byte)sound);
        CustomRpcTransport.Finish(writer);
    }
    public static void SyncAllPlayerNames()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        CustomRpcTransport.SendPayload(CustomRPC.SyncAllPlayerNames, writer =>
        {
            writer.WritePacked(Main.AllPlayerNames.Count);
            foreach (var name in Main.AllPlayerNames)
            {
                writer.Write(name.Key);
                writer.Write(name.Value);
            }
            writer.WritePacked(Main.AllClientRealNames.Count);
            foreach (var name in Main.AllClientRealNames)
            {
                writer.Write(name.Key);
                writer.Write(name.Value);
            }
        });
    }
    public static void ShowPopUp(this PlayerControl pc, string message, string title = "")
    {
        if (!AmongUsClient.Instance.AmHost) return;
        CustomRpcTransport.SendPayload(CustomRPC.ShowPopUp, writer =>
        {
            writer.Write(message);
            writer.Write(title);
        }, pc.GetClientId());
    }
    public static void RpcSetFriendCode(string fc)
    {
        MessageWriter writer = CustomRpcTransport.Start(CustomRPC.SetFriendCode, SendOption.Reliable);
        writer.Write(fc);
        CustomRpcTransport.Finish(writer);
        SetFriendCode(PlayerControl.LocalPlayer, fc);
    }
    public static void SetFriendCode(PlayerControl target, string fc)
    {
        if (GameStates.IsVanillaServer) return;
        if (target.GetClient() != null && target.GetClient().ProductUserId != string.Empty) return;
        // On Niko233's region this is not needed lol
        target.FriendCode = fc;
        target.Data.FriendCode = fc;
        target.GetClient().FriendCode = fc;
        target.Data.MarkDirty();
    }
    private static readonly HashSet<(uint Generation, int HostId, bool Retry)> PendingVersionRequests = [];
    public static void RpcVersionCheck() => Main.Instance.StartCoroutine(WaitAndSendVersion(false));
    public static void RpcRequestRetryVersionCheck() => Main.Instance.StartCoroutine(WaitAndSendVersion(true));

    private static System.Collections.IEnumerator WaitAndSendVersion(bool retry)
    {
        var client = AmongUsClient.Instance;
        var generation = OnGameJoinedPatch.Generation;
        if (client == null || !OnGameJoinedPatch.IsCurrentSession(generation)) yield break;
        var hostId = client.HostId;
        var key = (generation, hostId, retry);
        if (!PendingVersionRequests.Add(key)) yield break;
        var deadline = UnityEngine.Time.realtimeSinceStartup + 8f;
        try
        {
            while (OnGameJoinedPatch.IsCurrentSession(generation) && client.HostId == hostId)
            {
                if (UnityEngine.Time.realtimeSinceStartup >= deadline)
                {
                    Logger.Warn("Version exchange timed out waiting for player data", "RpcVersionCheck");
                    yield break;
                }
                var player = PlayerControl.LocalPlayer;
                if (player != null && hostId >= 0 && player.GetClientId() >= 0 && client.GetHost() != null)
                {
                    try
                    {
                        // Reannounce on each exchange, including requests from a new
                        // peer, before version traffic can depend on packed framing.
                        RpcCompatibility.SendCapabilities();
                        if (retry)
                        {
                            var writer = CustomRpcTransport.Start(CustomRPC.RequestRetryVersionCheck, SendOption.Reliable, hostId);
                            CustomRpcTransport.Finish(writer);
                        }
                        else
                        {
                            if (Main.playerVersion.ContainsKey(hostId) || !Main.VersionCheat.Value)
                            {
                                bool cheating = Main.VersionCheat.Value;
                                CustomRpcTransport.SendPayload(CustomRPC.VersionCheck, writer =>
                                {
                                    writer.Write(cheating ? Main.playerVersion[hostId].version.ToString() : Main.PluginVersion);
                                    writer.Write(cheating ? Main.playerVersion[hostId].tag : $"{ThisAssembly.Git.Commit}({ThisAssembly.Git.Branch})");
                                    writer.Write(cheating ? Main.playerVersion[hostId].forkId : Main.ForkId);
                                    writer.Write(cheating);
                                });
                            }
                            Main.playerVersion[player.GetClientId()] = new PlayerVersion(Main.PluginVersion, $"{ThisAssembly.Git.Commit}({ThisAssembly.Git.Branch})", Main.ForkId);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Version exchange failed: {ex.GetType().Name}", "RpcVersionCheck");
                    }
                    yield break;
                }
                yield return null;
            }
        }
        finally { PendingVersionRequests.Remove(key); }
    }
    public static void SendDeathReason(byte playerId, PlayerState.DeathReason deathReason)
    {
        MessageWriter writer = CustomRpcTransport.Start(CustomRPC.SetDeathReason, SendOption.Reliable, -1);
        writer.Write(playerId);
        writer.Write((int)deathReason);
        CustomRpcTransport.Finish(writer);
    }
    public static void GetDeathReason(MessageReader reader)
    {
        var playerId = reader.ReadByte();
        var deathReason = reader.ReadInt32();
        Main.PlayerStates[playerId].deathReason = (PlayerState.DeathReason)deathReason;
        Main.PlayerStates[playerId].IsDead = true;
    }
    public static void ForceEndGame(CustomWinner win)
    {
        if (ShipStatus.Instance == null) return;
        try { CustomWinnerHolder.ResetAndSetWinner(win); }
        catch { }
        if (AmongUsClient.Instance.AmHost)
        {
            ShipStatus.Instance.enabled = false;
            Utils.NotifyGameEnding();

            try { GameManager.Instance.LogicFlow.CheckEndCriteria(); }
            catch { }
            try { GameManager.Instance.RpcEndGame(GameOverReason.ImpostorDisconnect, false); }
            catch { }
        }
    }
    public static void EndGame(MessageReader reader)
    {
        try
        {
            CustomWinnerHolder.ReadFrom(reader);
        }
        catch (Exception ex)
        {
            Logger.Error($"正常にEndGameを行えませんでした。\n{ex}", "EndGame", false);
        }
    }
    public static void PlaySound(byte playerID, Sounds sound)
    {
        if (PlayerControl.LocalPlayer.PlayerId == playerID)
        {
            switch (sound)
            {
                case Sounds.KillSound:
                    SoundManager.Instance.PlaySound(PlayerControl.LocalPlayer.KillSfx, false, 1f);
                    break;
                case Sounds.TaskComplete:
                    SoundManager.Instance.PlaySound(DestroyableSingleton<HudManager>.Instance.TaskCompleteSound, false, 1f);
                    break;
                case Sounds.TaskUpdateSound:
                    SoundManager.Instance.PlaySound(DestroyableSingleton<HudManager>.Instance.TaskUpdateSound, false, 1f);
                    break;
                case Sounds.ImpTransform:
                    SoundManager.Instance.PlaySound(DestroyableSingleton<HnSImpostorScreamSfx>.Instance.HnSOtherImpostorTransformSfx, false, 0.8f);
                    break;
                case Sounds.SabotageSound:
                    SoundManager.Instance.PlaySound(ShipStatus.Instance.SabotageSound, false, 0.8f);
                    break;
            }
        }
    }
    public static void SetCustomRole(byte targetId, CustomRoles role)
    {
        if (role < CustomRoles.NotAssigned)
        {
            Main.PlayerStates[targetId].SetMainRole(role);
            targetId.GetRoleClassById()?.OnAdd(targetId);
        }
        else if (role >= CustomRoles.NotAssigned)   //500:NoSubRole 501~:SubRole 
        {
            Main.PlayerStates[targetId].SetSubRole(role);

            switch (role)
            {
                case CustomRoles.LastImpostor:
                    LastImpostor.AddMidGame(targetId);
                    break;
            }
        }

        if (!AmongUsClient.Instance.IsGameOver)
            DestroyableSingleton<HudManager>.Instance.SetHudActive(true);
        //    HudManager.Instance.Chat.SetVisible(true);

        if (PlayerControl.LocalPlayer.PlayerId == targetId) RemoveDisableDevicesPatch.UpdateDisableDevices();
    }
    public static void SyncRoleSkillReader(MessageReader reader)
    {
        try
        {
            var pc = reader.ReadNetObject<PlayerControl>();

            switch (pc.GetRoleClass())
            {
                default:
                    pc.GetRoleClass().ReceiveRPC(reader, pc);
                    break;
            }
        }
        catch (Exception error)
        {
            Logger.Error($" Error RPC:{error}", "SyncRoleSkillReader");
        }
    }
    public static void SyncLoversPlayers()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        MessageWriter writer = CustomRpcTransport.Start(CustomRPC.SetLoversPlayers, SendOption.Reliable, -1);
        writer.Write(Main.LoversPlayers.Count);
        foreach (var lp in Main.LoversPlayers)
        {
            writer.Write(lp.PlayerId);
        }
        CustomRpcTransport.Finish(writer);
    }
    public static void SendRpcLogger(uint targetNetId, byte callId, int targetClientId = -1)
    {
        if (!DebugModeManager.AmDebugger) return;
        string rpcName = GetRpcName(callId);
        string from = targetNetId.ToString();
        string target = targetClientId.ToString();
        try
        {
            target = targetClientId < 0 ? "All" : AmongUsClient.Instance.GetClient(targetClientId).PlayerName;
            from = Main.AllPlayerControls.FirstOrDefault(c => c.NetId == targetNetId)?.Data?.PlayerName;
        }
        catch { }
        Logger.Info($"FromNetID:{targetNetId}({from}) TargetClientID:{targetClientId}({target}) CallID:{callId}({rpcName})", "SendRPC");
    }
    public static string GetRpcName(byte callId)
    {
        if (callId == RpcPayloadSnapshot.OuterCallId) return "TOHE custom envelope";
        if (callId == RpcPayloadSnapshot.PackedOuterCallId) return "TOHE packed envelope";
        return Enum.GetName(typeof(RpcCalls), callId) ?? callId.ToString();
    }
    public static void SetRealKiller(byte targetId, byte killerId)
    {
        var state = Main.PlayerStates[targetId];
        state.RealKiller.Item1 = DateTime.Now;
        state.RealKiller.Item2 = killerId;
        state.RoleofKiller = Main.PlayerStates.TryGetValue(killerId, out var kState) ? kState.MainRole : CustomRoles.NotAssigned;

        if (!AmongUsClient.Instance.AmHost) return;
        MessageWriter writer = CustomRpcTransport.Start(CustomRPC.SetRealKiller, SendOption.Reliable, -1);
        writer.Write(targetId);
        writer.Write(killerId);
        CustomRpcTransport.Finish(writer);
    }
}
[HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.StartRpcImmediately))]
internal class StartRpcImmediatelyPatch
{
    public static void Prefix(/*InnerNet.InnerNetClient __instance,*/ [HarmonyArgument(0)] uint targetNetId, [HarmonyArgument(1)] byte callId, [HarmonyArgument(3)] int targetClientId = -1)
    {
        RPC.SendRpcLogger(targetNetId, callId, targetClientId);
    }
}
