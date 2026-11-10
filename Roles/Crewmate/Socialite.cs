using Hazel;
using InnerNet;
using TOHE.Modules;
using TOHE.Roles.Core;
using UnityEngine;
using static TOHE.Options;
using static TOHE.Translator;
using static TOHE.Utils;
namespace TOHE.Roles.Crewmate;

internal class Socialite : RoleBase
{
    //===========================SETUP================================\\
    private const int Id = 31800;
    public static bool HasEnabled => CustomRoleManager.HasEnabled(CustomRoles.Socialite);
    public override bool IsDesyncRole => true;
    public override CustomRoles ThisRoleBase => CustomRoles.Impostor;
    public override Custom_RoleType ThisRoleType => Custom_RoleType.CrewmateSupport;
    //==================================================================\\

    private static OptionItem AbilityCooldown;
    private static OptionItem AbilityLimitOpt;
    private static OptionItem ResetParties;

    private readonly HashSet<byte> PartiedPlayers = [];
    private readonly HashSet<byte> PreGuestList = [];
    private readonly HashSet<byte> GuestList = [];
    public override void SetupCustomOption()
    {
        SetupRoleOptions(Id, TabGroup.CrewmateRoles, CustomRoles.Socialite);
        AbilityCooldown = FloatOptionItem.Create(Id + 10, GeneralOption.AbilityCooldown, new(1f, 180f, 1f), 30f, TabGroup.CrewmateRoles, false).SetParent(CustomRoleSpawnChances[CustomRoles.Socialite])
            .SetValueFormat(OptionFormat.Seconds);
        AbilityLimitOpt = IntegerOptionItem.Create(Id + 11, GeneralOption.SkillLimitTimes, new(1, 200, 1), 30, TabGroup.CrewmateRoles, false).SetParent(CustomRoleSpawnChances[CustomRoles.Socialite])
            .SetValueFormat(OptionFormat.Times);
        ResetParties = BooleanOptionItem.Create(Id + 12, "SocialiteResetParties", true, TabGroup.CrewmateRoles, false).SetParent(CustomRoleSpawnChances[CustomRoles.Socialite]);
    }
    public override void Init()
    {
        PartiedPlayers.Clear();
        PreGuestList.Clear();
        GuestList.Clear();
    }

    public override void Add(byte playerId)
    {
        AbilityLimit = AbilityLimitOpt.GetInt();
    }

    public override void SetKillCooldown(byte id) => Main.AllPlayerKillCooldown[id] = AbilityLimit > 0 ? AbilityCooldown.GetFloat() : 300f;
    public override bool CanUseKillButton(PlayerControl pc) => AbilityLimit > 0;
    public override string GetProgressText(byte playerId, bool comms) => ColorString(AbilityLimit > 0 ? GetRoleColor(CustomRoles.Socialite) : Color.gray, $"({AbilityLimit})");
    public override string GetMark(PlayerControl seer, PlayerControl seen, bool isForMeeting = false) => PartiedPlayers.Contains(seen.PlayerId) ? CustomRoles.Socialite.GetColoredTextByRole("♪") : string.Empty;
    internal bool IsGuest(byte playerId) => GuestList.Contains(playerId);
    internal static bool BlocksInteraction(PlayerControl actor, PlayerControl target, string action) =>
        CustomRoleManager.AllEnabledRoles.OfType<Socialite>().Any(role => role.CheckInteraction(actor, target, action));

    private void SendRPC()
    {
        MessageWriter writer = CustomRpcTransport.Start(CustomRPC.SyncRoleSkill, SendOption.Reliable);
        writer.WriteNetObject(_Player);
        writer.Write(AbilityLimit);
        writer.Write(PartiedPlayers.Count);
        foreach (byte playerId in PartiedPlayers)
            writer.Write(playerId);
        CustomRpcTransport.Finish(writer);
    }
    public override void ReceiveRPC(MessageReader reader, PlayerControl pc)
    {
        AbilityLimit = reader.ReadSingle();
        int count = reader.ReadInt32();
        PartiedPlayers.Clear();
        for (int i = 0; i < count; i++)
            PartiedPlayers.Add(reader.ReadByte());
    }
    public override bool ForcedCheckMurderAsKiller(PlayerControl killer, PlayerControl target)
    {
        string reason = killer == null || target == null ? "player missing" :
            AbilityLimit < 1 ? "no uses remaining" :
            PartiedPlayers.Contains(target.PlayerId) ? "target already partying" : null;
        if (reason != null)
        {
            Logger.Info($"Party ability canceled: actor={killer?.GetRealName()} ({killer?.PlayerId}), target={target?.GetRealName()} ({target?.PlayerId}), reason={reason}", "Socialite");
            return false;
        }
        PartiedPlayers.Add(target.PlayerId);
        killer.SetKillCooldown(AbilityLimit == 1 ? 300f : -1f);
        AbilityLimit--;
        SendRPC();
        killer.Notify(string.Format(GetString("SocialitePartyMsg"), target.GetRealName()));
        killer.ResetKillCooldown();
        Logger.Info($"Party ability used; murder canceled: actor={killer.GetRealName()} ({killer.PlayerId}), target={target.GetRealName()} ({target.PlayerId}), uses remaining={AbilityLimit}", "Socialite");
        return false;
    }
    public override bool CheckMurderOnOthersTarget(PlayerControl killer, PlayerControl target) =>
        CheckInteraction(killer, target, "RpcCheckAndMurder");

    private bool CheckInteraction(PlayerControl killer, PlayerControl target, string action)
    {
        if (killer == null || target == null) return false;
        if (GuestList.Contains(killer.PlayerId)) return false;
        if (PartiedPlayers.Contains(target.PlayerId))
        {
            Logger.Info($"{action} canceled: actor={killer.GetRealName()} ({killer.PlayerId}, {killer.GetCustomRole()}), target={target.GetRealName()} ({target.PlayerId}), Socialite={_Player?.GetRealName()} ({_Player?.PlayerId}); target is partying and actor is not a guest", "Socialite");
            killer.Notify(GetString("SocialiteBlockNotification"));
            PreGuestList.Add(killer.PlayerId);
            return true;
        }
        return false;
    }
    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        foreach (var playerId in PreGuestList)
        {
            GuestList.Add(playerId);
        }
    }
    public override void OnMeetingHudStart(PlayerControl pc)
    {
        string message = GetString("SocialiteGuestListMsg");
        bool showIds = PlayerIdentifiers.Shows(pc.IsModded(), meeting: true);
        foreach (var playerId in GuestList)
        {
            message += (showIds ? $"[{playerId}] " : "") + (PreGuestList.Contains(playerId) ? $"<i>{GetPlayerById(playerId).GetRealName()}</i>\n" : $"{GetPlayerById(playerId).GetRealName()}\n");
        }
        SendMessage(message, pc.PlayerId, title: CustomRoles.Socialite.ToColoredString().ToUpper());
    }
    public override bool CheckVote(PlayerControl voter, PlayerControl target)
    {
        if (voter == null || target == null) return true;
        if (IsGuest(target.PlayerId))
        {
            Logger.Info($"Guest-list vote canceled: actor={voter.GetRealName()} ({voter.PlayerId}), target={target.GetRealName()} ({target.PlayerId}); target is already a guest", "Socialite");
            SendMessage(GetString("SocialiteAlreadyGuest"), voter.PlayerId, title: CustomRoles.Socialite.ToColoredString().ToUpper());
            return false;
        }
        GuestList.Add(target.PlayerId);
        Logger.Info($"Guest-list vote used; vote canceled: actor={voter.GetRealName()} ({voter.PlayerId}), target={target.GetRealName()} ({target.PlayerId})", "Socialite");
        SendMessage(string.Format(GetString("SocialiteVoteMsg"), target.GetRealName()), voter.PlayerId, title: CustomRoles.Socialite.ToColoredString().ToUpper());
        return false;
    }
    public override void AfterMeetingTasks()
    {
        PreGuestList.Clear();
        if (ResetParties.GetBool())
        {
            PartiedPlayers.Clear();
            if (_Player != null) SendRPC();
        }
    }
}
