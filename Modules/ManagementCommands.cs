using System;
using System.IO;
using TOHE.Modules;
using static TOHE.Translator;

namespace TOHE;

internal static class ManagementCommands
{
    internal static bool IsAdministrator(PlayerControl player) => player && player.Data != null
        && PlayerTagFiles.IsAdministrator(player);

    internal static bool TryHandle(PlayerControl sender, string text)
    {
        var args = text.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        var parsed = ManagementCommandPolicy.Parse(args[0]);
        if (!parsed.HasValue) return false;
        var action = parsed.Value;
        var client = AmongUsClient.Instance;
        var host = sender.OwnerId == client.HostId;
        var friendCode = PlayerFriendCode.FromOwner(sender);
        var dev = friendCode.GetDevUser();
        var permitted = host || ManagementCommandPolicy.TagGrant(action, PlayerTagFiles.PermissionLevel(sender),
                PlayerTagFiles.CanSay(sender), PlayerTagFiles.CanEnd(sender), PlayerTagFiles.CanExecute(sender));
        if (action == ManagementAction.Chat) permitted |= dev.IsDev;
        if (action == ManagementAction.Rename) permitted |= Options.PlayerCanSetName.GetBool() || dev.IsDev || dev.NameCmd;
        if (action == ManagementAction.Color) permitted |= Options.PlayerCanSetColor.GetBool() || dev.IsDev || dev.ColorCmd;
        void Reply(string message) => Utils.SendMessage(message, sender.PlayerId, noReplay: true);
        if (!permitted) { Reply(GetString("FeatureCommandNoAccess")); return true; }

        switch (action)
        {
            case ManagementAction.Rename:
                if (!GameStates.IsLobby) { Reply(GetString("Message.OnlyCanUseInLobby")); break; }
                var name = string.Join(" ", args.Skip(1));
                if (name.Length is < 1 or > 10) { Reply(GetString("Message.AllowNameLength")); break; }
                if (host) Main.HostRealName = name;
                Main.AllPlayerNames[sender.PlayerId] = name;
                Utils.ApplySuffix(sender);
                Reply(string.Format(GetString("Message.SetName"), name));
                break;
            case ManagementAction.Color:
                if (!GameStates.IsLobby) { Reply(GetString("Message.OnlyCanUseInLobby")); break; }
                var colorText = args.Length > 1 ? args[1] : "";
                var color = Utils.MsgToColor(colorText, host);
                if (color == byte.MaxValue) { Reply(GetString("IllegalColor")); break; }
                sender.RpcSetColor(color);
                Reply(string.Format(GetString("Message.SetColor"), colorText));
                break;
            case ManagementAction.Start:
                if (!GameStates.IsLobby || !GameStartManager.Instance || GameStates.IsInGame || GameStates.IsCoStartGame)
                { Reply(GetString("Message.OnlyCanUseInLobby")); break; }
                GameStartManager.Instance.BeginGame();
                break;
            case ManagementAction.List:
                Reply(GetString("PlayerIdList") + string.Concat(Main.AllPlayerControls.Where(player => player && player.Data != null && !player.Data.Disconnected)
                    .Select(player => $"\n{player.PlayerId} → {player.GetRealName()}")));
                break;
            case ManagementAction.Chat:
                if (args.Length < 2) break;
                var message = string.Join(" ", args.Skip(1));
                var title = GetString(host ? "MessageFromTheHost" : dev.IsDev ? "MessageFromDev" : "MessageFromModerator");
                Utils.SendMessage(message, title: $"{title} ~ {sender.GetRealName().RemoveHtmlTags()}");
                WriteLog($"{Identity(sender)} used /say: {SingleLine(message)}", Reply);
                break;
            case ManagementAction.End:
                if (!GameStates.IsInGame || !GameManager.Instance) { Reply(GetString("Message.CanNotUseInLobby")); break; }
                CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Draw);
                GameManager.Instance.LogicFlow.CheckEndCriteria();
                break;
            case ManagementAction.Execute:
                var executed = ManagementCommandPolicy.TryTarget(args, out var executedId) ? Utils.GetPlayerById(executedId) : null;
                if (!GameStates.IsInGame || !executed || executed.Data == null || executed.Data.Disconnected || !executed.IsAlive()
                    || !Main.PlayerStates.TryGetValue(executed.PlayerId, out var state) || !host && executed.OwnerId == client.HostId)
                { Reply(GetString("FeatureInvalidTarget")); break; }
                var executedName = executed.GetRealName().RemoveHtmlTags();
                executed.SetDeathReason(PlayerState.DeathReason.etc);
                executed.SetRealKiller(sender);
                state.SetDead();
                executed.Data.IsDead = true;
                executed.RpcExileV2();
                MurderPlayerPatch.AfterPlayerDeathTasks(sender, executed, GameStates.IsMeeting);
                Utils.SendMessage(executed.OwnerId == client.HostId ? GetString("HostKillSelfByCommand") : string.Format(GetString("Message.Executed"), executedName));
                break;
            case ManagementAction.Warn:
            case ManagementAction.Kick:
            case ManagementAction.Ban:
                if (ManagementCommandPolicy.RequiresReason(action) && args.Length < 3) { Reply(GetString("BanCommandNoReason")); break; }
                var target = ManagementCommandPolicy.TryTarget(args, out var targetId) ? Utils.GetPlayerById(targetId) : null;
                if (!target || target.Data == null || target.Data.Disconnected
                    || !ManagementCommandPolicy.CanModerateTarget(host, target.OwnerId == client.HostId,
                        ManagementCommandPolicy.TagProtectsTarget(action, PlayerTagFiles.PermissionLevel(target))))
                { Reply(GetString("FeatureInvalidTarget")); break; }
                var reason = args.Length > 2 ? string.Join(" ", args.Skip(2)) : GetString("ManagementReasonUnspecified");
                // Snapshot before disconnecting the native object.
                var actorIdentity = Identity(sender);
                var targetIdentity = Identity(target);
                var actorName = sender.GetRealName().RemoveHtmlTags();
                var targetName = target.GetRealName().RemoveHtmlTags();
                var roleName = GameStates.IsInGame ? Utils.GetRoleName(target.GetCustomRole()) : "";
                var key = action == ManagementAction.Warn ? "WarnCommandWarned" : action == ManagementAction.Ban ? "BanCommandBanned" : "KickCommandKicked";
                if (action != ManagementAction.Warn) client.KickPlayer(target.OwnerId, action == ManagementAction.Ban);
                Utils.SendMessage($"{targetName} {GetString(key)} {actorName}\n{GetString("ManagementReason")}: {reason}"
                    + (roleName.Length == 0 ? "" : $"\n{GetString("ConfirmEjections.Role")}: {roleName}"));
                WriteLog($"{actorIdentity} {action}: {targetIdentity} Role: {SingleLine(roleName)} Reason: {SingleLine(reason)}", Reply);
                break;
        }
        return true;
    }

    private static string Identity(PlayerControl player) => $"{player.PlayerId},{SingleLine(PlayerFriendCode.FromOwner(player))},{player.GetClient()?.GetHashedPuid() ?? ""},{SingleLine(player.GetRealName().RemoveHtmlTags())}";
    private static string SingleLine(string text) => (text ?? "").Replace('\r', ' ').Replace('\n', ' ');
    private static void WriteLog(string message, Action<string> reply)
    {
        try
        {
            Directory.CreateDirectory("TOHEE-DATA");
            File.AppendAllText(Path.Combine("TOHEE-DATA", "ModLogs.txt"), $"[{DateTime.Now:O}] {message}{Environment.NewLine}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Logger.Warn("Management action completed but its audit log could not be written", "ManagementCommands");
            reply(GetString("ManagementLogFailed"));
        }
    }
}
