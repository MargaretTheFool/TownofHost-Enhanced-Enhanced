using System;
using static TOHE.Translator;

namespace TOHE.Modules;

internal static class VoteCommand
{
    public static readonly Dictionary<(nint Meeting, int Client), int> PendingClears = [];

    public static void Handle(PlayerControl voter, string[] args)
    {
        if (!Options.EnableVoteCommand.GetBool())
        {
            Utils.SendMessage(GetString("VoteDisabled"), voter.PlayerId);
            return;
        }

        byte targetId = 253;
        if (args.Length != 2 ||
            (!string.Equals(args[1], "skip", StringComparison.OrdinalIgnoreCase) &&
             (!byte.TryParse(args[1], out targetId) || targetId is >= 252 and not 253)))
        {
            Utils.SendMessage(GetString("VoteCommandUsage"), voter.PlayerId);
            return;
        }

        var meeting = MeetingHud.Instance;
        if (!GameStates.IsMeeting || meeting.state is not (MeetingHud.MeetingStates.NotVoted or MeetingHud.MeetingStates.Voted))
        {
            Utils.SendMessage(GetString("VoteCommandNotOpen"), voter.PlayerId);
            return;
        }

        var logicOptions = GameManager.Instance.LogicOptions.TryCast<LogicOptionsNormal>();
        if (logicOptions != null && logicOptions.GetVotingTime() > 0 &&
            meeting.discussionTimer >= logicOptions.GetDiscussionTime() + logicOptions.GetVotingTime())
        {
            Utils.SendMessage(GetString("VoteCommandNotOpen"), voter.PlayerId);
            return;
        }

        var voterArea = meeting.playerStates.FirstOrDefault(area => area.PlayerId.Value == voter.PlayerId);
        if (!voter.IsAlive() || voter.Data.Disconnected || voterArea == null || voterArea.AmDead)
        {
            Utils.SendMessage(GetString("CannotVoteWhenDead"), voter.PlayerId);
            return;
        }
        if (PendingClears.ContainsKey((meeting.Pointer, voter.GetClientId())))
        {
            Utils.SendMessage(GetString("VoteCommandClearing"), voter.PlayerId);
            return;
        }
        if (voterArea.DidVote)
        {
            Utils.SendMessage(GetString("VoteCommandAlreadyVoted"), voter.PlayerId);
            return;
        }

        if (targetId != 253)
        {
            var target = Utils.GetPlayerById(targetId);
            if (target == null || !target.IsAlive() || target.Data.Disconnected ||
                !meeting.playerStates.Any(area => area.PlayerId.Value == targetId && !area.AmDead))
            {
                Utils.SendMessage(GetString("VoteDead"), voter.PlayerId);
                return;
            }
        }

        // This follows the same host CastVote path as a confirmed meeting vote,
        // including role CheckVote and the normal CheckForEndVoting hooks.
        voter.RpcCastVote(targetId);
    }
}
